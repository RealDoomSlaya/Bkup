using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ER2OfficialContentRevealDeepFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "er2.officialcontentreveal.deepfix";
        public const string PluginName = "ER2 Official Content Reveal - Deep Import Fix";
        public const string PluginVersion = "1.3.0";
        internal static ManualLogSource ModLog;

        public override void Load()
        {
            ModLog = Log;
            try
            {
                var h = new Harmony(PluginGuid);
                Type gui = AccessTools.TypeByName("MissionEditorGUI");
                if (gui == null) throw new MissingMemberException("MissionEditorGUI");

                MethodInfo openPanel = AccessTools.Method(gui, "OpenEditSquadPanel");
                MethodInfo editSelected = AccessTools.Method(gui, "EditSelectedSquad");
                if (openPanel == null || editSelected == null)
                    throw new MissingMethodException("MissionEditorGUI squad editing methods");

                h.Patch(openPanel,
                    postfix: new HarmonyMethod(typeof(DeepFix), nameof(DeepFix.OpenPanelPostfix)));
                h.Patch(editSelected,
                    prefix: new HarmonyMethod(typeof(DeepFix), nameof(DeepFix.EditSelectedPrefix)));

                Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            }
            catch (Exception ex)
            {
                Log.LogError("Deep import fix initialization failed: " + ex);
            }
        }
    }

    internal static class DeepFix
    {
        private static readonly Type CustomSquadMemberType = AccessTools.TypeByName("CustomSquadMember");
        private static readonly Type ItemsDatabaseType = AccessTools.TypeByName("ItemsDatabase");
        private static readonly Type SquadEditorSceneType = AccessTools.TypeByName("SquadEditorScene");

        public static void OpenPanelPostfix(object __instance, int __0)
        {
            try
            {
                object squad = GetSquadAt(__instance, __0);
                if (squad == null) return;
                Hydrate(__instance, squad, "OPEN-PANEL");
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("OpenEditSquadPanel hydration failed: " + ex);
            }
        }

        public static bool EditSelectedPrefix(object __instance)
        {
            try
            {
                int index = Convert.ToInt32(R.Get(__instance, "edit_squad_id") ?? -1);
                object squad = GetSquadAt(__instance, index);
                if (squad == null) return true;

                Hydrate(__instance, squad, "EDIT");

                MethodInfo edit = SquadEditorSceneType == null ? null : AccessTools.Method(SquadEditorSceneType, "EditSquad");
                object panel = R.Get(__instance, "squad_editor_3dbuttons_panel");
                if (edit == null || panel == null)
                {
                    Plugin.ModLog.LogWarning("Direct SquadEditorScene.EditSquad bridge unavailable; using ER2 retail path.");
                    return true;
                }

                edit.Invoke(null, new[] { squad, panel });
                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("EditSelectedSquad deep bridge failed: " + ex);
                return true;
            }
        }

        private static object GetSquadAt(object gui, int index)
        {
            object battle = R.Get(gui, "current_battle");
            object squads = R.Get(battle, "customSquads");
            int count = R.Count(squads);
            if (index < 0 || index >= count) return null;
            return R.Item(squads, index);
        }

        private static void Hydrate(object gui, object squad, string stage)
        {
            object battle = R.Get(gui, "current_battle");
            string customId = R.Get(squad, "squad_id") as string;

            List<string> ids = R.StringList(R.Get(squad, "loadouts"));
            string officialId = FindOfficialSourceId(battle, customId);

            if (ids.Count == 0 && !string.IsNullOrEmpty(officialId))
            {
                object official = GetOfficialSquad(officialId);
                ids = R.StringList(R.Call(official, "GetLoadoutIDs"));
            }

            object members = R.Get(squad, "members");
            int memberCount = R.Count(members);

            Plugin.ModLog.LogInfo(stage + " custom='" + customId + "' source='" + officialId +
                "' loadouts=" + ids.Count + " members=" + memberCount + ".");

            int wanted = Math.Max(ids.Count, memberCount);
            for (int i = 0; i < wanted; i++)
            {
                members = R.Get(squad, "members");
                memberCount = R.Count(members);
                object member = i < memberCount ? R.Item(members, i) : null;

                if (member == null && i < ids.Count && CustomSquadMemberType != null)
                {
                    member = Activator.CreateInstance(CustomSquadMemberType);
                    R.Call(squad, "AddMember", member);
                    members = R.Get(squad, "members");
                    memberCount = R.Count(members);
                    if (i < memberCount) member = R.Item(members, i) ?? member;
                }
                if (member == null) continue;

                string type = R.Get(member, "loadout_type") as string;
                if ((string.IsNullOrEmpty(type) || string.Equals(type, "none", StringComparison.OrdinalIgnoreCase)) && i < ids.Count)
                {
                    type = ids[i];
                    R.Set(member, "loadout_type", type);
                }

                R.Call(member, "FixMember");

                object rebuilt = R.Call(member, "ToLoadout");
                int inventoryCount = R.Count(R.Get(rebuilt, "inventory_items"));
                Plugin.ModLog.LogInfo(stage + " member " + i + " type='" + (R.Get(member, "loadout_type") as string) +
                    "' inv=" + inventoryCount +
                    " uniform='" + (R.Get(member, "uniform_id") as string) +
                    "' vest='" + (R.Get(member, "vest_id") as string) +
                    "' head='" + (R.Get(member, "headgear_id") as string) +
                    "' w1='" + (R.Get(member, "weap1_id") as string) +
                    "' w2='" + (R.Get(member, "weap2_id") as string) + "'.");
            }
        }

        private static string FindOfficialSourceId(object battle, string customId)
        {
            if (battle == null || string.IsNullOrEmpty(customId)) return null;
            object phases = R.Get(battle, "vphases");
            for (int p = 0; p < R.Count(phases); p++)
            {
                object phase = R.Item(phases, p);
                object spawns = R.Get(phase, "unit_spawns");
                for (int s = 0; s < R.Count(spawns); s++)
                {
                    object spawn = R.Item(spawns, s);
                    string cid = R.Get(spawn, "custom_squad_id") as string;
                    if (string.Equals(cid, customId, StringComparison.Ordinal))
                        return R.Get(spawn, "squad_id") as string;
                }
            }
            return null;
        }

        private static object GetOfficialSquad(string id)
        {
            if (ItemsDatabaseType == null || string.IsNullOrEmpty(id)) return null;
            MethodInfo m = AccessTools.Method(ItemsDatabaseType, "GetSquadLoadouts", new[] { typeof(string), typeof(int) })
                           ?? AccessTools.Method(ItemsDatabaseType, "GetSquadLoadouts");
            if (m == null) return null;
            ParameterInfo[] p = m.GetParameters();
            if (p.Length == 2) return m.Invoke(null, new object[] { id, -1 });
            if (p.Length == 1) return m.Invoke(null, new object[] { id });
            return null;
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
                if (p != null)
                {
                    try { return p.GetValue(obj, null); } catch { }
                }
                FieldInfo f = t.GetField(name, All);
                if (f != null)
                {
                    try { return f.GetValue(obj); } catch { }
                }
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
                if (p != null && p.CanWrite)
                {
                    try { p.SetValue(obj, value, null); return; } catch { }
                }
                FieldInfo f = t.GetField(name, All);
                if (f != null)
                {
                    try { f.SetValue(obj, value); return; } catch { }
                }
                t = t.BaseType;
            }
        }

        internal static object Call(object obj, string name, params object[] args)
        {
            if (obj == null) return null;
            MethodInfo[] ms = obj.GetType().GetMethods(All);
            foreach (MethodInfo m in ms)
            {
                if (m.Name != name || m.GetParameters().Length != args.Length) continue;
                try { return m.Invoke(obj, args); }
                catch (ArgumentException) { }
            }
            return null;
        }

        internal static int Count(object seq)
        {
            if (seq == null) return 0;
            if (seq is Array a) return a.Length;
            object n = Get(seq, "Count") ?? Get(seq, "Length");
            if (n != null) return Convert.ToInt32(n);
            if (seq is ICollection c) return c.Count;
            return 0;
        }

        internal static object Item(object seq, int i)
        {
            if (seq == null) return null;
            if (seq is Array a) return a.GetValue(i);
            PropertyInfo p = seq.GetType().GetProperty("Item", All);
            if (p != null)
            {
                try { return p.GetValue(seq, new object[] { i }); } catch { }
            }
            MethodInfo m = AccessTools.Method(seq.GetType(), "get_Item", new[] { typeof(int) });
            if (m != null) return m.Invoke(seq, new object[] { i });
            if (seq is IList l) return l[i];
            return null;
        }

        internal static List<string> StringList(object seq)
        {
            var r = new List<string>();
            for (int i = 0; i < Count(seq); i++)
            {
                object x = Item(seq, i);
                if (x != null) r.Add(x.ToString());
            }
            return r;
        }
    }
}
