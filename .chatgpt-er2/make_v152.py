from pathlib import Path
import runpy

# Generate the current known-good 1.5.1 source first.
runpy.run_path('.chatgpt-er2/make_v151.py', run_name='__main__')

src = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5_1.cs')
out = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5_2.cs')
s = src.read_text(encoding='utf-8')
s = s.replace('public const string PluginVersion = "1.5.1";', 'public const string PluginVersion = "1.5.2";')

# Add the native EditMember method handle alongside the existing bridge handles.
needle = '        private static MethodInfo _editSquad;\n'
replacement = '''        private static MethodInfo _editSquad;\n        private static MethodInfo _editMemberInstance;\n'''
if needle not in s:
    raise SystemExit('Could not find _editSquad field')
s = s.replace(needle, replacement, 1)

# Resolve and patch the member-click dispatcher.  The stock EditOrDuplicateMember
# path is the only thing that differs between "click existing member" (broken)
# and Shift+click duplicate (works).  Normal clicks are sent directly to EditMember.
needle = '''            _editSquad = AccessTools.Method(_squadEditorType, "EditSquad");\n\n            if (selectAndEdit == null || editSelected == null || _setSquadLoadout == null || _openPanel == null || _getSquadByString == null || _getItemObject == null || _editSquad == null)\n                throw new MissingMethodException("One or more required ER2 editor methods are missing.");\n\n            h.Patch(selectAndEdit, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(SelectAndEditPrefix)));\n            h.Patch(editSelected, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(EditSelectedPrefix)));\n'''
replacement = '''            _editSquad = AccessTools.Method(_squadEditorType, "EditSquad");\n            _editMemberInstance = AccessTools.Method(_squadEditorType, "EditMember", new[] { typeof(int) });\n            MethodInfo editOrDuplicateMember = AccessTools.Method(_squadEditorType, "EditOrDuplicateMember", new[] { typeof(int) });\n\n            if (selectAndEdit == null || editSelected == null || _setSquadLoadout == null || _openPanel == null || _getSquadByString == null || _getItemObject == null || _editSquad == null)\n                throw new MissingMethodException("One or more required ER2 editor methods are missing.");\n\n            h.Patch(selectAndEdit, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(SelectAndEditPrefix)));\n            h.Patch(editSelected, prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(EditSelectedPrefix)));\n\n            if (_editMemberInstance != null && editOrDuplicateMember != null)\n            {\n                h.Patch(editOrDuplicateMember,\n                    prefix: new HarmonyMethod(typeof(OfficialSquadEditBridge), nameof(EditOrDuplicateMemberPrefix)));\n                Plugin.ModLog.LogInfo("Member re-edit bridge installed: normal member clicks bypass EditOrDuplicateMember -> EditMember directly; Shift-click stays vanilla.");\n            }\n            else\n            {\n                Plugin.ModLog.LogWarning("Member re-edit bridge unavailable: EditMember/EditOrDuplicateMember method not found.");\n            }\n'''
if needle not in s:
    raise SystemExit('Could not find Install method patch point')
s = s.replace(needle, replacement, 1)

# Insert the new prefix and Shift test before SelectAndEditPrefix.
needle = '        public static bool SelectAndEditPrefix(object __instance)\n'
replacement = r'''        public static bool EditOrDuplicateMemberPrefix(object __instance, int __0)
        {
            // Keep ER2's native Shift+click duplicate behavior.  A plain click is
            // forced down the native EditMember(index) entry point instead of going
            // through EditOrDuplicateMember's state/branching, which is what gets
            // stuck after reopening an already-edited custom member.
            bool shift = IsShiftHeld();
            Plugin.ModLog.LogInfo("MEMBER-CLICK index=" + __0 + " shift=" + shift + ".");
            if (shift) return true;

            try
            {
                if (__instance == null || _editMemberInstance == null) return true;
                _editMemberInstance.Invoke(__instance, new object[] { __0 });
                Plugin.ModLog.LogInfo("MEMBER-REEDIT direct EditMember(" + __0 + ") dispatched.");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogError("Direct member re-edit failed; falling back to ER2 native path: " + ex);
                return true;
            }
        }

        private static bool IsShiftHeld()
        {
            try
            {
                Type inputType = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule", false);
                Type keyCodeType = Type.GetType("UnityEngine.KeyCode, UnityEngine.CoreModule", false)
                                ?? Type.GetType("UnityEngine.KeyCode, UnityEngine.InputLegacyModule", false);
                if (inputType == null || keyCodeType == null) return false;

                MethodInfo getKey = inputType.GetMethod("GetKey", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { keyCodeType }, null);
                if (getKey == null) return false;

                object left = Enum.Parse(keyCodeType, "LeftShift");
                object right = Enum.Parse(keyCodeType, "RightShift");
                return Convert.ToBoolean(getKey.Invoke(null, new[] { left }))
                    || Convert.ToBoolean(getKey.Invoke(null, new[] { right }));
            }
            catch
            {
                return false;
            }
        }

        public static bool SelectAndEditPrefix(object __instance)
'''
if needle not in s:
    raise SystemExit('Could not find SelectAndEditPrefix insertion point')
s = s.replace(needle, replacement, 1)

out.write_text(s, encoding='utf-8')
print(out)
