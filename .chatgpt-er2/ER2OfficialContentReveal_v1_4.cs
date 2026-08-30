using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ER2OfficialContentReveal
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "er2.officialcontentreveal";
        public const string PluginName = "ER2 Official Content Reveal";
        public const string PluginVersion = "1.4.0";
        internal static ManualLogSource ModLog;

        public override void Load()
        {
            ModLog = Log;
            try
            {
                var h = new Harmony(PluginGuid);
                EditorGatePatches.Install(h);
                OfficialSquadEditBridge.Install(h);
                Log.LogInfo(PluginName + " " + PluginVersion + " loaded: resolved official Loadout conversion enabled.");
            }
            catch (Exception ex)
            {
                Log.LogError("Fatal initialization failure: " + ex);
            }
        }
    }

    internal static class EditorGatePatches
    {
        internal static void Install(Harmony h)
        {
            PatchBoolGetter(h, "UnityEngine.Application", "get_isEditor");
            PatchBoolGetter(h, "UnityEngine.Debug", "get_isDebugBuild");

            Type t = AccessTools.TypeByName("DisactiveIfPlatform");
            MethodInfo run = t == null ? null : AccessTools.Method(t, "Run");
            if (run == null) return;

            h.Patch(run,
                prefix: new HarmonyMethod(typeof(EditorGatePatches), nameof(GatePrefix)),
                postfix: new HarmonyMethod(typeof(EditorGatePatches), nameof(GatePostfix)));
            Plugin.ModLog.LogInfo("Developer editor-content gate patched.");
        }

        private static void PatchBoolGetter(Harmony h, string typeName, string methodName)
        {
            Type t = AccessTools.TypeByName(typeName);
            MethodInfo m = t == null ? null : AccessTools.Method(t, methodName);
            if (m != null) h.Patch(m, postfix: new HarmonyMethod(typeof(EditorGatePatches), nameof(ForceTruePostfix)));
        }

        public static void ForceTruePostfix(ref bool __result) => __result = true;

        public static void GatePrefix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                bool hidden = R.Bool(__instance, "onlyOnEditor") ||
                              R.Bool(__instance, "onlyIfIsDebugBuild") ||
                              !string.IsNullOrEmpty(R.Get(__instance, "enableOnBranch") as string) ||
                              !string.IsNullOrEmpty(R.Get(__instance, "enableOnBranch2") as string);
                if (!hidden) return;

                R.Set(__instance, "onlyOnEditor", false);
                R.Set(__instance, "onlyIfIsDebugBuild", false);
                R.Set(__instance, "enableOnPC", true);
                R.Set(__instance, "enableOnThisBranch", true);
                R.Set(__instance, "enableOnBranch", string.Empty);
                R.Set(__instance, "enableOnBranch2", string.Empty);
            }
            catch (Exception ex) { Plugin.ModLog.LogWarning("Developer gate prefix failed: " + ex.Message); }
        }

        public static void GatePostfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                object objects = R.Get(__instance, "objects");
                for (int i = 0; i < R.Count(objects); i++)
                {
                    object target = R.Item(objects, i);
                    if (target == null) continue;
                    MethodInfo setActive = AccessTools.Method(target.GetType(), "SetActive", new[] { typeof(bool) });
                    if (setActive != null) setActive.Invoke(target, new object[] { true });
                }
            }
            catch (Exception ex) { Plugin.ModLog.LogWarning("Developer gate postfix failed: " + ex.Message); }
        }
    }

    internal static class OfficialSquadEditBridge
    {
        private static bool _pending;
        private static object _pendingGui;
        private static object _dummySpawner;

        private static Type _guiType;
        private static Type _spawnerType;
        private static Type _customSquadType;
        private static Type _customMemberType;
        private static Type _itemsDbType;
        private static Type _squadEditorType;
        private static Type _itemClothingType;
        private static Type _itemHelmetType;
        private static Type _weaponType;
        private static Type _scopeType;
        private static Type _bipodType;
        private static Type _bayonetType;

        private static MethodInfo _setSquadLoadout;
        private static MethodInfo _openPanel;
        private static MethodInfo _getSquadByString;
        private static MethodInfo _getItemObject;
        private static MethodInfo _editSquad;

        internal static void Install(Harmony h)
        {
            _guiType = AccessTools.TypeByName("MissionEditorGUI");
            _spawnerType = AccessTools.TypeByName("MissionEditorBattleData.MissionEditorPhaseUnitSpawn") ?? AccessTools.TypeByName("MissionEditorPhaseUnitSpawn");
            _customSquadType = AccessTools.TypeByName("CustomSquad");
            _customMemberType = AccessTools.TypeByName("CustomSquadMember");
            _itemsDbType = AccessTools.TypeByName("ItemsDatabase");
            _squadEditorType = AccessTools.TypeByName("SquadEditorScene");
            _itemClothingType = AccessTools.TypeByName("ItemClothing");
            _itemHelmetType = AccessTools.TypeByName("ItemHelmet");
            _weaponType = AccessTools.TypeByName("Weapon");
            _scopeType = AccessTools.TypeByName("AttachmentScope");
            _bipodType = AccessTools.TypeByName("AttachmentBipod");
            _bayonetType = AccessTools.TypeByName("AttachmentBayonet");

            if (_guiType == null || _spawnerType == null || _customSquadType == null || _customMemberType == null || _itemsDbType == null || _squadEditorType == null)
                throw new MissingMemberException("One or more required ER2 editor types are missing.");

            MethodInfo selectAndEdit = AccessTools.Method(_guiType, "SelectAndEditSquad");
            MethodInfo editSelected = AccessTools.Method(_guiType, "EditSelectedSquad");
            _setSquadLoadout = AccessTools.Method(_guiType, "PSpawnerUnit_SetSquadLoadout");
            _openPanel = AccessTools.Method(_guiType, "OpenEditSquadPanel");
            _getSquadByString = AccessTools.Method(_itemsDbType, "GetSquadLoadouts", new[] { typeof(string), typeof(int) });
            _getItemObject = AccessTools.Method(_itemsDbType, "GetItemObject", new[] { typeof(string) });
            _editSquad = AccessTools.Method(_squadEditorType, "EditSquad");

            if (selectAndEdit == null || editSelected == null || _setSquadLoadout == null || _openPanel == null || _getSquadByString == null || _getItemObject == null || _editSquad == null)
                throw new MissingMethodException("One or more required ER2 editor methods are missing.");

            h.Patch(selectAndEdit, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(SelectAndEditPrefix)));
            h.Patch(editSelected, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(EditSelectedPrefix)));

            MethodInfo cb = FindPickerCallback(out Type owner);
            if (cb == null) throw new MissingMethodException("PSpawnerUnit_SetSquadLoadout callback");
            h.Patch(cb, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(OnSquadPickedPrefix)));
            Plugin.ModLog.LogInfo("Official-squad picker bridge installed on " + owner.FullName + "." + cb.Name + ".");
        }

        private static MethodInfo FindPickerCallback(out Type owner)
        {
            owner = null;
            Type[] nested = _guiType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
            {
                MethodInfo[] methods = nested[i].GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                for (int j = 0; j < methods.Length; j++)
                {
                    if (methods[j].Name.IndexOf("PSpawnerUnit_SetSquadLoadout_b__0", StringComparison.Ordinal) < 0) continue;
                    owner = nested[i];
                    return methods[j];
                }
            }
            return null;
        }

        public static bool SelectAndEditPrefix(object __instance)
        {
            if (__instance == null) return true;
            try
            {
                _pending = true;
                _pendingGui = __instance;
                _dummySpawner = Activator.CreateInstance(_spawnerType);
                _setSquadLoadout.Invoke(__instance, new[] { _dummySpawner });
                return false;
            }
            catch (Exception ex)
            {
                ClearPending();
                Plugin.ModLog.LogError("Failed to open official-aware squad picker: " + ex);
                return true;
            }
        }

        public static bool OnSquadPickedPrefix(object __0)
        {
            if (!_pending) return true;
            object gui = _pendingGui;
            ClearPending();
            if (gui == null || __0 == null) return false;

            try
            {
                object battle = R.Get(gui, "current_battle");
                string selectedId = R.Get(__0, "item_id") as string;
                string displayName = R.Get(__0, "item_name") as string;
                if (battle == null || string.IsNullOrEmpty(selectedId)) return false;

                object official = ResolveOfficial(__0, selectedId);
                if (official == null)
                {
                    Plugin.ModLog.LogError("Could not resolve official squad '" + selectedId + "'.");
                    return false;
                }

                if (string.IsNullOrEmpty(displayName)) displayName = R.Call(official, "GetSquadName") as string;
                if (string.IsNullOrEmpty(displayName)) displayName = selectedId;

                object loadoutIds = R.Call(official, "GetLoadoutIDs");
                object countObj = R.Call(official, "CountLoadouts");
                int slots = countObj == null ? R.Count(loadoutIds) : Convert.ToInt32(countObj);
                if (loadoutIds == null || slots <= 0)
                {
                    Plugin.ModLog.LogError("Official squad '" + selectedId + "' returned no loadouts.");
                    return false;
                }

                object editable = CreateCustomSquad(displayName, loadoutIds);
                if (editable == null)
                {
                    Plugin.ModLog.LogError("Could not construct CustomSquad for '" + selectedId + "'.");
                    return false;
                }

                R.Set(editable, "squad_name_private", displayName);
                ConvertOfficialMembers(official, editable, loadoutIds, selectedId, slots);

                object addResult = R.Call(battle, "AddCustomSquad", editable);
                string customId = R.Get(editable, "squad_id") as string;
                int index = FindCustomSquadIndex(battle, customId, editable);
                if (index < 0 && addResult != null) { try { index = Convert.ToInt32(addResult); } catch { } }
                if (index < 0)
                {
                    Plugin.ModLog.LogError("Custom squad was created but its editor index could not be resolved.");
                    return false;
                }

                object added = R.Item(R.Get(battle, "customSquads"), index);
                if (added != null)
                {
                    editable = added;
                    R.Set(editable, "squad_name_private", displayName);
                    customId = R.Get(editable, "squad_id") as string;
                }

                int remapped = string.IsNullOrEmpty(customId) ? 0 : RemapMissionSpawns(battle, selectedId, customId);
                LogEditable("READY", editable);
                Plugin.ModLog.LogInfo("Imported official squad '" + selectedId + "' -> '" + customId + "' with " + slots + " resolved member loadout(s); remapped " + remapped + " current mission spawn(s).");

                R.Set(gui, "edit_squad_id", index);
                _openPanel.Invoke(gui, new object[] { index });
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("Official squad conversion failed: " + ex);
            }
            return false;
        }

        public static bool EditSelectedPrefix(object __instance)
        {
            if (__instance == null) return true;
            try
            {
                object battle = R.Get(__instance, "current_battle");
                object squads = R.Get(battle, "customSquads");
                int index = Convert.ToInt32(R.Get(__instance, "edit_squad_id") ?? -1);
                if (index < 0 || index >= R.Count(squads)) return true;

                object squad = R.Item(squads, index);
                object panel = R.Get(__instance, "squad_editor_3dbuttons_panel");
                if (squad == null || panel == null) return true;

                LogEditable("EDIT", squad);
                _editSquad.Invoke(null, new[] { squad, panel });
                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("Direct squad editor bridge failed: " + ex);
                return true;
            }
        }

        private static object ResolveOfficial(object row, string selectedId)
        {
            object rowData = R.Get(row, "data");
            if (rowData != null)
            {
                Type squadDataType = AccessTools.TypeByName("SquadData");
                MethodInfo tryCast = rowData.GetType().GetMethod("TryCast", BindingFlags.Instance | BindingFlags.Public);
                if (tryCast != null && tryCast.IsGenericMethodDefinition && squadDataType != null)
                {
                    try
                    {
                        object cast = tryCast.MakeGenericMethod(squadDataType).Invoke(rowData, null);
                        if (cast != null) return cast;
                    }
                    catch { }
                }
            }
            return _getSquadByString.Invoke(null, new object[] { selectedId, -1 });
        }

        private static object CreateCustomSquad(string name, object loadoutIds)
        {
            ConstructorInfo[] ctors = _customSquadType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type argType = loadoutIds.GetType();
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] p = ctors[i].GetParameters();
                if (p.Length != 2 || p[0].ParameterType != typeof(string)) continue;
                if (!p[1].ParameterType.IsAssignableFrom(argType) && !argType.IsAssignableFrom(p[1].ParameterType)) continue;
                return ctors[i].Invoke(new object[] { name, loadoutIds });
            }
            return null;
        }

        private static void ConvertOfficialMembers(object official, object editable, object loadoutIds, string squadId, int slots)
        {
            for (int i = 0; i < slots; i++)
            {
                object roleObj = R.Item(loadoutIds, i);
                string roleId = roleObj == null ? string.Empty : roleObj.ToString();
                object resolved = R.Call(official, "GetLoadout", i);
                object member = EnsureMember(editable, i);
                if (member == null)
                {
                    Plugin.ModLog.LogError("Could not create editable member " + i + " for '" + squadId + "'.");
                    continue;
                }

                R.Set(member, "loadout_type", roleId);
                R.Call(member, "FixMember");
                if (resolved == null)
                {
                    Plugin.ModLog.LogWarning("SquadData.GetLoadout(" + i + ") returned null for '" + squadId + "'; leaving ER2 role defaults in place.");
                    continue;
                }

                ResetMember(member);
                object inventory = R.Get(resolved, "inventory_items");
                var exactIds = new List<string>();
                for (int n = 0; n < R.Count(inventory); n++)
                {
                    object raw = R.Item(inventory, n);
                    if (raw == null) continue;
                    string itemId = raw.ToString();
                    if (string.IsNullOrEmpty(itemId)) continue;
                    exactIds.Add(itemId);
                    ApplyOfficialItem(member, itemId);
                }

                object rebuilt = R.Call(member, "ToLoadout");
                Plugin.ModLog.LogInfo("RESOLVE " + squadId + "[" + i + "] role='" + roleId + "' exact=[" + string.Join(",", exactIds.ToArray()) + "] => " + MemberSummary(member) + " rebuiltItems=" + R.Count(R.Get(rebuilt, "inventory_items")) + ".");
            }
        }

        private static object EnsureMember(object squad, int index)
        {
            object members = R.Get(squad, "members");
            while (R.Count(members) <= index)
            {
                object created = Activator.CreateInstance(_customMemberType);
                R.Call(squad, "AddMember", created);
                members = R.Get(squad, "members");
            }
            return R.Item(members, index);
        }

        private static void ResetMember(object member)
        {
            R.Set(member, "uniform_id", string.Empty);
            R.Set(member, "vest_id", string.Empty);
            R.Set(member, "headgear_id", string.Empty);
            R.Set(member, "weap1_id", string.Empty);
            R.Set(member, "weap1_scope", string.Empty);
            R.Set(member, "weap1_bipod", string.Empty);
            R.Set(member, "weap1_bayonet", string.Empty);
            R.Set(member, "weap2_id", string.Empty);

            object other = R.Get(member, "otherItems_ids");
            for (int i = R.Count(other) - 1; i >= 0; i--) R.Call(member, "RemoveInventoryItem", i);
        }

        private static void ApplyOfficialItem(object member, string itemId)
        {
            object item = null;
            try { item = _getItemObject.Invoke(null, new object[] { itemId }); }
            catch (Exception ex) { Plugin.ModLog.LogWarning("GetItemObject('" + itemId + "') failed: " + ex.GetBaseException().Message); }

            if (item == null)
            {
                R.Call(member, "AddInventoryItem", itemId);
                return;
            }

            Type actual = item.GetType();
            if (_itemClothingType != null && _itemClothingType.IsAssignableFrom(actual))
            {
                string wearable = (R.Get(item, "type") ?? string.Empty).ToString();
                if (wearable.IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    R.Set(member, "uniform_id", itemId);
                    return;
                }
                if (wearable.IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    R.Set(member, "vest_id", itemId);
                    return;
                }
            }

            if (_itemHelmetType != null && _itemHelmetType.IsAssignableFrom(actual))
            {
                R.Set(member, "headgear_id", itemId);
                return;
            }
            if (_scopeType != null && _scopeType.IsAssignableFrom(actual))
            {
                R.Set(member, "weap1_scope", itemId);
                return;
            }
            if (_bipodType != null && _bipodType.IsAssignableFrom(actual))
            {
                R.Set(member, "weap1_bipod", itemId);
                return;
            }
            if (_bayonetType != null && _bayonetType.IsAssignableFrom(actual))
            {
                R.Set(member, "weap1_bayonet", itemId);
                return;
            }
            if (_weaponType != null && _weaponType.IsAssignableFrom(actual))
            {
                string w1 = R.Get(member, "weap1_id") as string;
                if (string.IsNullOrEmpty(w1)) R.Set(member, "weap1_id", itemId);
                else
                {
                    string w2 = R.Get(member, "weap2_id") as string;
                    if (string.IsNullOrEmpty(w2)) R.Set(member, "weap2_id", itemId);
                    else R.Call(member, "AddInventoryItem", itemId);
                }
                return;
            }

            R.Call(member, "AddInventoryItem", itemId);
        }

        private static string MemberSummary(object member)
        {
            return "uniform='" + (R.Get(member, "uniform_id") as string) +
                   "' vest='" + (R.Get(member, "vest_id") as string) +
                   "' head='" + (R.Get(member, "headgear_id") as string) +
                   "' w1='" + (R.Get(member, "weap1_id") as string) +
                   "' scope='" + (R.Get(member, "weap1_scope") as string) +
                   "' bipod='" + (R.Get(member, "weap1_bipod") as string) +
                   "' bayonet='" + (R.Get(member, "weap1_bayonet") as string) +
                   "' w2='" + (R.Get(member, "weap2_id") as string) +
                   "' other=" + R.Count(R.Get(member, "otherItems_ids"));
        }

        private static void LogEditable(string stage, object squad)
        {
            object members = R.Get(squad, "members");
            Plugin.ModLog.LogInfo(stage + " custom='" + (R.Get(squad, "squad_id") as string) + "' name='" + (R.Call(squad, "GetSquadName") as string) + "' members=" + R.Count(members) + ".");
            for (int i = 0; i < R.Count(members); i++)
            {
                object m = R.Item(members, i);
                Plugin.ModLog.LogInfo(stage + " member " + i + " role='" + (R.Get(m, "loadout_type") as string) + "' " + MemberSummary(m) + ".");
            }
        }

        private static int FindCustomSquadIndex(object battle, string customId, object preferred)
        {
            object squads = R.Get(battle, "customSquads");
            for (int i = 0; i < R.Count(squads); i++)
            {
                object squad = R.Item(squads, i);
                if (squad == null) continue;
                if (preferred != null && ReferenceEquals(squad, preferred)) return i;
                if (!string.IsNullOrEmpty(customId) && string.Equals(R.Get(squad, "squad_id") as string, customId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int RemapMissionSpawns(object battle, string officialSquadId, string customSquadId)
        {
            int changed = 0;
            object phases = R.Get(battle, "vphases");
            for (int p = 0; p < R.Count(phases); p++)
            {
                object spawns = R.Get(R.Item(phases, p), "unit_spawns");
                for (int s = 0; s < R.Count(spawns); s++)
                {
                    object spawn = R.Item(spawns, s);
                    if (!string.Equals(R.Get(spawn, "squad_id") as string, officialSquadId, StringComparison.Ordinal)) continue;
                    R.Set(spawn, "custom_squad_id", customSquadId);
                    changed++;
                }
            }
            return changed;
        }

        private static void ClearPending()
        {
            _pending = false;
            _pendingGui = null;
            _dummySpawner = null;
        }
    }

    internal static class R
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static object Get(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            while (t != null)
            {
                PropertyInfo p = t.GetProperty(name, All);
                if (p != null) { try { return p.GetValue(obj, null); } catch { } }
                FieldInfo f = t.GetField(name, All);
                if (f != null) { try { return f.GetValue(obj); } catch { } }
                t = t.BaseType;
            }
            return null;
        }

        internal static void Set(object obj, string name, object value)
        {
            if (obj == null) return;
            Type t = obj.GetType();
            while (t != null)
            {
                PropertyInfo p = t.GetProperty(name, All);
                if (p != null && p.CanWrite) { try { p.SetValue(obj, value, null); return; } catch { } }
                FieldInfo f = t.GetField(name, All);
                if (f != null) { try { f.SetValue(obj, value); return; } catch { } }
                t = t.BaseType;
            }
        }

        internal static bool Bool(object obj, string name)
        {
            object v = Get(obj, name);
            return v is bool b && b;
        }

        internal static object Call(object obj, string name, params object[] args)
        {
            if (obj == null) return null;
            MethodInfo[] methods = obj.GetType().GetMethods(All);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (!string.Equals(m.Name, name, StringComparison.Ordinal) || m.GetParameters().Length != args.Length) continue;
                try { return m.Invoke(obj, args); }
                catch (ArgumentException) { }
            }
            return null;
        }

        internal static int Count(object seq)
        {
            if (seq == null) return 0;
            if (seq is Array a) return a.Length;
            object count = Get(seq, "Count") ?? Get(seq, "Length");
            if (count != null) return Convert.ToInt32(count);
            if (seq is ICollection c) return c.Count;
            return 0;
        }

        internal static object Item(object seq, int index)
        {
            if (seq == null || index < 0) return null;
            if (seq is Array a) return index < a.Length ? a.GetValue(index) : null;
            PropertyInfo p = seq.GetType().GetProperty("Item", All);
            if (p != null) { try { return p.GetValue(seq, new object[] { index }); } catch { } }
            MethodInfo getItem = AccessTools.Method(seq.GetType(), "get_Item", new[] { typeof(int) });
            if (getItem != null) { try { return getItem.Invoke(seq, new object[] { index }); } catch { } }
            if (seq is IList list && index < list.Count) return list[index];
            return null;
        }
    }
}
