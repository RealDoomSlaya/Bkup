from pathlib import Path

src_path = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_4.cs')
out_path = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5.cs')
s = src_path.read_text(encoding='utf-8')
s = s.replace('public const string PluginVersion = "1.4.0";', 'public const string PluginVersion = "1.5.0";')
s = s.replace('loaded: official squads are converted from resolved Loadout objects into editable CustomSquadMembers.', 'loaded: exact official role IDs are preserved first; resolved-loadout overrides are only applied when ER2 needs them.')

start = s.index('        private static void ConvertOfficialMembers(')
end = s.index('        private static object EnsureMember(', start)
replacement = r'''        private static void ConvertOfficialMembers(object official, object editable, object loadoutIds, string squadId, int slots)
        {
            for (int i = 0; i < slots; i++)
            {
                object roleRaw = R.Item(loadoutIds, i);
                string roleId = roleRaw == null ? string.Empty : roleRaw.ToString();
                object resolved = R.Call(official, "GetLoadout", i);
                object member = EnsureMember(editable, i);
                if (member == null)
                {
                    Plugin.ModLog.LogError("Could not create editable member " + i + " for '" + squadId + "'.");
                    continue;
                }

                // This is the key difference from 1.4: the official SquadData already gives
                // the exact concrete role ID (eg ger_infantry_win_assault_1942).  Preserve it
                // and let ER2's own CustomSquadMember/ToLoadout path resolve that role first.
                // 1.4 destroyed that state by blanking the fields and re-adding every item.
                R.Set(member, "loadout_type", roleId);
                R.Call(member, "FixMember");

                List<string> exact = InventoryIds(resolved);
                object baselineLoadout = R.Call(member, "ToLoadout");
                List<string> baseline = InventoryIds(baselineLoadout);
                bool baselineExact = InventoryEqual(exact, baseline);

                Plugin.ModLog.LogInfo("BASELINE " + squadId + "[" + i + "] role='" + roleId +
                    "' exact=" + exact.Count + " rebuilt=" + baseline.Count + " equal=" + baselineExact +
                    " => " + MemberSummary(member) + ".");

                if (resolved == null || baselineExact)
                    continue;

                // If ER2's role-only conversion is not byte-for-byte inventory-equivalent,
                // use its own generic item-class resolver. GetItemObject() returns the base
                // ItemObject proxy, which is why 1.4 saw every item as 'other'.
                ApplySpecificOverrides(member, exact);

                // Only add inventory entries genuinely missing after the typed overrides.
                // Never duplicate the whole official loadout on top of its base role.
                FillMissingInventory(member, exact);

                object finalLoadout = R.Call(member, "ToLoadout");
                List<string> finalIds = InventoryIds(finalLoadout);
                Plugin.ModLog.LogInfo("FINAL " + squadId + "[" + i + "] role='" + roleId +
                    "' exact=" + exact.Count + " rebuilt=" + finalIds.Count +
                    " equal=" + InventoryEqual(exact, finalIds) + " => " + MemberSummary(member) +
                    " items=[" + string.Join(",", finalIds.ToArray()) + "].");
            }
        }

        private static List<string> InventoryIds(object loadout)
        {
            var result = new List<string>();
            if (loadout == null) return result;
            object inventory = R.Get(loadout, "inventory_items");
            for (int i = 0; i < R.Count(inventory); i++)
            {
                object raw = R.Item(inventory, i);
                if (raw != null && !string.IsNullOrEmpty(raw.ToString())) result.Add(raw.ToString());
            }
            return result;
        }

        private static Dictionary<string, int> Counts(List<string> ids)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                int n;
                result.TryGetValue(ids[i], out n);
                result[ids[i]] = n + 1;
            }
            return result;
        }

        private static bool InventoryEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            Dictionary<string, int> ca = Counts(a);
            Dictionary<string, int> cb = Counts(b);
            if (ca.Count != cb.Count) return false;
            foreach (KeyValuePair<string, int> kv in ca)
            {
                int n;
                if (!cb.TryGetValue(kv.Key, out n) || n != kv.Value) return false;
            }
            return true;
        }

        private static object GetSpecificItem(string itemId, Type wantedType)
        {
            if (wantedType == null || string.IsNullOrEmpty(itemId)) return null;
            MethodInfo[] methods = _itemsDatabaseType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "GetSpecificItemClass" || !m.IsGenericMethodDefinition || m.GetParameters().Length != 1)
                    continue;
                try
                {
                    return m.MakeGenericMethod(wantedType).Invoke(null, new object[] { itemId });
                }
                catch { }
            }
            return null;
        }

        private static void ApplySpecificOverrides(object member, List<string> exact)
        {
            string firstWeapon = null;
            string secondWeapon = null;

            for (int i = 0; i < exact.Count; i++)
            {
                string itemId = exact[i];

                object clothing = GetSpecificItem(itemId, _itemClothingType);
                if (clothing != null)
                {
                    string wearable = (R.Get(clothing, "type") ?? string.Empty).ToString();
                    if (wearable.IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        R.Set(member, "uniform_id", itemId);
                        continue;
                    }
                    if (wearable.IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        R.Set(member, "vest_id", itemId);
                        continue;
                    }
                }

                if (GetSpecificItem(itemId, _itemHelmetType) != null)
                {
                    R.Set(member, "headgear_id", itemId);
                    continue;
                }
                if (GetSpecificItem(itemId, _attachmentScopeType) != null)
                {
                    R.Set(member, "weap1_scope", itemId);
                    continue;
                }
                if (GetSpecificItem(itemId, _attachmentBipodType) != null)
                {
                    R.Set(member, "weap1_bipod", itemId);
                    continue;
                }
                if (GetSpecificItem(itemId, _attachmentBayonetType) != null)
                {
                    R.Set(member, "weap1_bayonet", itemId);
                    continue;
                }
                if (GetSpecificItem(itemId, _weaponType) != null)
                {
                    if (firstWeapon == null) firstWeapon = itemId;
                    else if (secondWeapon == null) secondWeapon = itemId;
                }
            }

            if (firstWeapon != null) R.Set(member, "weap1_id", firstWeapon);
            if (secondWeapon != null) R.Set(member, "weap2_id", secondWeapon);
        }

        private static void FillMissingInventory(object member, List<string> exact)
        {
            List<string> current = InventoryIds(R.Call(member, "ToLoadout"));
            Dictionary<string, int> need = Counts(exact);
            Dictionary<string, int> have = Counts(current);

            foreach (KeyValuePair<string, int> kv in need)
            {
                int existing;
                have.TryGetValue(kv.Key, out existing);
                for (int n = existing; n < kv.Value; n++)
                    R.Call(member, "AddInventoryItem", kv.Key);
            }
        }

'''
s = s[:start] + replacement + s[end:]
out_path.write_text(s, encoding='utf-8')
print(out_path)
