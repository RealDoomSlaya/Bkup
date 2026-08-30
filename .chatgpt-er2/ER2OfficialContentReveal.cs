using System;
using System.Collections;
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
        public const string PluginVersion = "1.2.1";
        internal static ManualLogSource ModLog;

        public override void Load()
        {
            ModLog = Log;
            try
            {
                var harmony = new Harmony(PluginGuid);
                EditorGatePatches.Install(harmony);
                OfficialSquadEditBridge.Install(harmony);
                Log.LogInfo(PluginName + " " + PluginVersion + " loaded: editor/debug gates + official-squad edit bridge enabled.");
            }
            catch (Exception ex)
            {
                Log.LogError("Fatal initialization failure: " + ex);
            }
        }
    }

    internal static class EditorGatePatches
    {
        internal static void Install(Harmony harmony)
        {
            PatchPostfix(harmony, "UnityEngine.Application", "get_isEditor", nameof(ForceTruePostfix));
            PatchPostfix(harmony, "UnityEngine.Debug", "get_isDebugBuild", nameof(ForceTruePostfix));

            Type gateType = AccessTools.TypeByName("DisactiveIfPlatform");
            if (gateType == null)
            {
                Plugin.ModLog.LogWarning("DisactiveIfPlatform type not found; developer-object gate patch skipped.");
                return;
            }

            MethodInfo run = AccessTools.Method(gateType, "Run");
            if (run == null)
            {
                Plugin.ModLog.LogWarning("DisactiveIfPlatform.Run not found; developer-object gate patch skipped.");
                return;
            }

            harmony.Patch(run,
                prefix: new HarmonyMethod(typeof(EditorGatePatches), nameof(GatePrefix)),
                postfix: new HarmonyMethod(typeof(EditorGatePatches), nameof(GatePostfix)));
            Plugin.ModLog.LogInfo("Developer editor-content gate patched.");
        }

        private static void PatchPostfix(Harmony harmony, string typeName, string methodName, string patchName)
        {
            Type t = AccessTools.TypeByName(typeName);
            MethodInfo m = t == null ? null : AccessTools.Method(t, methodName);
            if (m == null)
            {
                Plugin.ModLog.LogWarning(typeName + "." + methodName + " not found; gate patch skipped.");
                return;
            }
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(EditorGatePatches), patchName));
        }

        public static void ForceTruePostfix(ref bool __result) => __result = true;

        public static void GatePrefix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                bool hidden = ReadBool(__instance, "onlyOnEditor") ||
                              ReadBool(__instance, "onlyIfIsDebugBuild") ||
                              !string.IsNullOrEmpty(ReadString(__instance, "enableOnBranch")) ||
                              !string.IsNullOrEmpty(ReadString(__instance, "enableOnBranch2"));
                if (!hidden) return;

                ReflectionUtil.SetMember(__instance, "onlyOnEditor", false);
                ReflectionUtil.SetMember(__instance, "onlyIfIsDebugBuild", false);
                ReflectionUtil.SetMember(__instance, "enableOnPC", true);
                ReflectionUtil.SetMember(__instance, "enableOnThisBranch", true);
                ReflectionUtil.SetMember(__instance, "enableOnBranch", string.Empty);
                ReflectionUtil.SetMember(__instance, "enableOnBranch2", string.Empty);
            }
            catch (Exception ex) { Plugin.ModLog.LogWarning("Developer gate prefix failed: " + ex.Message); }
        }

        public static void GatePostfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                object objects = ReflectionUtil.GetMember(__instance, "objects");
                int count = ReflectionUtil.SequenceCount(objects);
                for (int i = 0; i < count; i++)
                {
                    object target = ReflectionUtil.SequenceItem(objects, i);
                    if (target == null) continue;
                    MethodInfo setActive = AccessTools.Method(target.GetType(), "SetActive", new[] { typeof(bool) });
                    if (setActive != null) setActive.Invoke(target, new object[] { true });
                }
            }
            catch (Exception ex) { Plugin.ModLog.LogWarning("Developer gate postfix failed: " + ex.Message); }
        }

        private static bool ReadBool(object obj, string name)
        {
            object value = ReflectionUtil.GetMember(obj, name);
            return value is bool b && b;
        }

        private static string ReadString(object obj, string name) => ReflectionUtil.GetMember(obj, name) as string;
    }

    internal static class OfficialSquadEditBridge
    {
        private static bool _pendingOfficialEdit;
        private static object _pendingGui;
        private static object _dummySpawner;

        private static Type _guiType;
        private static Type _spawnerType;
        private static Type _itemsDatabaseType;
        private static Type _customSquadType;
        private static MethodInfo _setSquadLoadout;
        private static MethodInfo _openEditSquadPanel;
        private static MethodInfo _getSquadLoadouts;

        internal static void Install(Harmony harmony)
        {
            _guiType = AccessTools.TypeByName("MissionEditorGUI");
            _spawnerType = AccessTools.TypeByName("MissionEditorBattleData.MissionEditorPhaseUnitSpawn")
                           ?? AccessTools.TypeByName("MissionEditorPhaseUnitSpawn");
            _itemsDatabaseType = AccessTools.TypeByName("ItemsDatabase");
            _customSquadType = AccessTools.TypeByName("CustomSquad");

            if (_guiType == null || _spawnerType == null || _itemsDatabaseType == null || _customSquadType == null)
            {
                Plugin.ModLog.LogError("Required ER2 editor types were not found; official-squad edit bridge not installed.");
                return;
            }

            MethodInfo selectAndEdit = AccessTools.Method(_guiType, "SelectAndEditSquad");
            _setSquadLoadout = AccessTools.Method(_guiType, "PSpawnerUnit_SetSquadLoadout");
            _openEditSquadPanel = AccessTools.Method(_guiType, "OpenEditSquadPanel");
            _getSquadLoadouts = AccessTools.Method(_itemsDatabaseType, "GetSquadLoadouts", new[] { typeof(string), typeof(int) })
                                ?? AccessTools.Method(_itemsDatabaseType, "GetSquadLoadouts");

            if (selectAndEdit == null || _setSquadLoadout == null || _openEditSquadPanel == null || _getSquadLoadouts == null)
            {
                Plugin.ModLog.LogError("Required ER2 editor methods were not found; official-squad edit bridge not installed.");
                return;
            }

            harmony.Patch(selectAndEdit,
                prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(SelectAndEditPrefix)));

            Type displayClass = _guiType.GetNestedType("__c__DisplayClass195_0", BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo callback = displayClass == null ? null : AccessTools.Method(displayClass, "_PSpawnerUnit_SetSquadLoadout_b__0");

            if (callback == null)
            {
                Type[] nested = _guiType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < nested.Length && callback == null; i++)
                {
                    MethodInfo[] methods = nested[i].GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int j = 0; j < methods.Length; j++)
                    {
                        if (methods[j].Name.IndexOf("PSpawnerUnit_SetSquadLoadout_b__0", StringComparison.Ordinal) >= 0)
                        {
                            callback = methods[j];
                            displayClass = nested[i];
                            break;
                        }
                    }
                }
            }

            if (callback == null)
            {
                Plugin.ModLog.LogError("PSpawnerUnit_SetSquadLoadout selection callback not found; official-squad edit bridge not installed.");
                return;
            }

            harmony.Patch(callback,
                prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(OnSpawnerSquadPickedPrefix)));
            Plugin.ModLog.LogInfo("Official-squad edit bridge installed on " + displayClass.FullName + "." + callback.Name);
        }

        public static bool SelectAndEditPrefix(object __instance)
        {
            if (__instance == null) return true;
            try
            {
                _pendingOfficialEdit = true;
                _pendingGui = __instance;
                _dummySpawner = Activator.CreateInstance(_spawnerType);
                _setSquadLoadout.Invoke(__instance, new[] { _dummySpawner });
                return false;
            }
            catch (Exception ex)
            {
                _pendingOfficialEdit = false;
                _pendingGui = null;
                _dummySpawner = null;
                Plugin.ModLog.LogError("Failed to open official-aware Edit Squad picker: " + ex);
                return true;
            }
        }

        public static bool OnSpawnerSquadPickedPrefix(object __0)
        {
            if (!_pendingOfficialEdit) return true;

            object gui = _pendingGui;
            _pendingOfficialEdit = false;
            _pendingGui = null;
            _dummySpawner = null;

            if (gui == null || __0 == null) return false;

            try
            {
                object battle = ReflectionUtil.GetMember(gui, "current_battle");
                if (battle == null)
                {
                    Plugin.ModLog.LogError("Edit Squad selection returned with no current battle.");
                    return false;
                }

                string selectedId = ReflectionUtil.GetMember(__0, "item_id") as string;
                if (string.IsNullOrEmpty(selectedId))
                {
                    Plugin.ModLog.LogWarning("Edit Squad picker returned an entry with no item_id.");
                    return false;
                }

                object existing = ReflectionUtil.InvokeBest(battle, "FindCustomSquad", selectedId);
                if (existing != null)
                {
                    int existingIndex = FindCustomSquadIndex(battle, ReflectionUtil.GetMember(existing, "squad_id") as string);
                    if (existingIndex >= 0) OpenEditor(gui, existingIndex);
                    else Plugin.ModLog.LogWarning("Found custom squad '" + selectedId + "' but could not locate its array index.");
                    return false;
                }

                object official = null;
                object rowData = ReflectionUtil.GetMember(__0, "data");
                if (rowData != null)
                {
                    Type squadDataType = AccessTools.TypeByName("SquadData");
                    MethodInfo tryCast = rowData.GetType().GetMethod("TryCast", BindingFlags.Instance | BindingFlags.Public);
                    if (tryCast != null && tryCast.IsGenericMethodDefinition && squadDataType != null)
                    {
                        try { official = tryCast.MakeGenericMethod(squadDataType).Invoke(rowData, null); } catch { }
                    }
                }
                if (official == null) official = InvokeGetSquadLoadouts(selectedId);
                if (official == null)
                {
                    Plugin.ModLog.LogError("Could not resolve selected official squad '" + selectedId + "'.");
                    return false;
                }

                string displayName = ReflectionUtil.GetMember(__0, "item_name") as string;
                if (string.IsNullOrEmpty(displayName)) displayName = ReflectionUtil.InvokeBest(official, "GetSquadName") as string;
                if (string.IsNullOrEmpty(displayName)) displayName = selectedId;

                object loadoutIds = ReflectionUtil.InvokeBest(official, "GetLoadoutIDs");
                if (loadoutIds == null)
                {
                    Plugin.ModLog.LogError("Official squad '" + selectedId + "' returned no loadout IDs.");
                    return false;
                }

                object editable = CreateCustomSquad(displayName, loadoutIds);
                if (editable == null)
                {
                    Plugin.ModLog.LogError("Could not construct editable copy of official squad '" + selectedId + "'.");
                    return false;
                }

                object addResult = ReflectionUtil.InvokeBest(battle, "AddCustomSquad", editable);
                int editableIndex = addResult == null ? -1 : Convert.ToInt32(addResult);
                object customSquads = ReflectionUtil.GetMember(battle, "customSquads");
                int customCount = ReflectionUtil.SequenceCount(customSquads);

                if (editableIndex < 0 || editableIndex >= customCount)
                {
                    Plugin.ModLog.LogError("ER2 rejected the editable copy of official squad '" + selectedId + "'.");
                    return false;
                }

                object added = ReflectionUtil.SequenceItem(customSquads, editableIndex);
                string customId = ReflectionUtil.GetMember(added, "squad_id") as string;
                int remapped = string.IsNullOrEmpty(customId) ? 0 : RemapMissionSpawns(battle, selectedId, customId);

                Plugin.ModLog.LogInfo("Imported official squad '" + selectedId + "' as editable custom squad '" + customId + "'; remapped " + remapped + " matching mission spawn(s).");
                OpenEditor(gui, editableIndex);
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("Official squad import/edit failed: " + ex);
            }

            return false;
        }

        private static object InvokeGetSquadLoadouts(string selectedId)
        {
            ParameterInfo[] p = _getSquadLoadouts.GetParameters();
            if (p.Length == 2) return _getSquadLoadouts.Invoke(null, new object[] { selectedId, -1 });
            if (p.Length == 1) return _getSquadLoadouts.Invoke(null, new object[] { selectedId });
            return null;
        }

        private static object CreateCustomSquad(string name, object loadoutIds)
        {
            ConstructorInfo[] ctors = _customSquadType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type loadoutType = loadoutIds.GetType();
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] p = ctors[i].GetParameters();
                if (p.Length != 2 || p[0].ParameterType != typeof(string)) continue;
                if (!p[1].ParameterType.IsAssignableFrom(loadoutType) && !loadoutType.IsAssignableFrom(p[1].ParameterType)) continue;
                return ctors[i].Invoke(new object[] { name, loadoutIds });
            }
            try { return Activator.CreateInstance(_customSquadType, new object[] { name, loadoutIds }); }
            catch { return null; }
        }

        private static int FindCustomSquadIndex(object battle, string customId)
        {
            if (battle == null || string.IsNullOrEmpty(customId)) return -1;
            object squads = ReflectionUtil.GetMember(battle, "customSquads");
            int count = ReflectionUtil.SequenceCount(squads);
            for (int i = 0; i < count; i++)
            {
                object squad = ReflectionUtil.SequenceItem(squads, i);
                string id = ReflectionUtil.GetMember(squad, "squad_id") as string;
                if (string.Equals(id, customId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int RemapMissionSpawns(object battle, string officialSquadId, string customSquadId)
        {
            int changed = 0;
            object phases = ReflectionUtil.GetMember(battle, "vphases");
            int phaseCount = ReflectionUtil.SequenceCount(phases);
            for (int p = 0; p < phaseCount; p++)
            {
                object phase = ReflectionUtil.SequenceItem(phases, p);
                object spawns = ReflectionUtil.GetMember(phase, "unit_spawns");
                int spawnCount = ReflectionUtil.SequenceCount(spawns);
                for (int s = 0; s < spawnCount; s++)
                {
                    object spawn = ReflectionUtil.SequenceItem(spawns, s);
                    string squadId = ReflectionUtil.GetMember(spawn, "squad_id") as string;
                    if (!string.Equals(squadId, officialSquadId, StringComparison.Ordinal)) continue;
                    ReflectionUtil.SetMember(spawn, "custom_squad_id", customSquadId);
                    changed++;
                }
            }
            return changed;
        }

        private static void OpenEditor(object gui, int squadIndex) => _openEditSquadPanel.Invoke(gui, new object[] { squadIndex });
    }

    internal static class ReflectionUtil
    {
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            PropertyInfo p = FindProperty(t, name);
            if (p != null) return p.GetValue(obj, null);
            FieldInfo f = FindField(t, name);
            return f == null ? null : f.GetValue(obj);
        }

        internal static void SetMember(object obj, string name, object value)
        {
            if (obj == null) return;
            Type t = obj.GetType();
            PropertyInfo p = FindProperty(t, name);
            if (p != null && p.CanWrite) { p.SetValue(obj, value, null); return; }
            FieldInfo f = FindField(t, name);
            if (f != null) f.SetValue(obj, value);
        }

        internal static int SequenceCount(object seq)
        {
            if (seq == null) return 0;
            if (seq is Array a) return a.Length;
            object count = GetMember(seq, "Count") ?? GetMember(seq, "Length");
            if (count != null) return Convert.ToInt32(count);
            if (seq is ICollection c) return c.Count;
            return 0;
        }

        internal static object SequenceItem(object seq, int index)
        {
            if (seq == null) return null;
            if (seq is Array a) return a.GetValue(index);
            Type t = seq.GetType();
            PropertyInfo item = t.GetProperty("Item", AllInstance);
            if (item != null) return item.GetValue(seq, new object[] { index });
            MethodInfo getItem = AccessTools.Method(t, "get_Item", new[] { typeof(int) });
            if (getItem != null) return getItem.Invoke(seq, new object[] { index });
            if (seq is IList list) return list[index];
            return null;
        }

        internal static object InvokeBest(object obj, string methodName, params object[] args)
        {
            if (obj == null) return null;
            MethodInfo[] methods = obj.GetType().GetMethods(AllInstance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, methodName, StringComparison.Ordinal)) continue;
                if (methods[i].GetParameters().Length != args.Length) continue;
                try { return methods[i].Invoke(obj, args); }
                catch (ArgumentException) { }
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type t, string name)
        {
            while (t != null)
            {
                PropertyInfo p = t.GetProperty(name, AllInstance);
                if (p != null) return p;
                t = t.BaseType;
            }
            return null;
        }

        private static FieldInfo FindField(Type t, string name)
        {
            while (t != null)
            {
                FieldInfo f = t.GetField(name, AllInstance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }
    }
}
