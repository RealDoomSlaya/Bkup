from pathlib import Path
import runpy

runpy.run_path('.chatgpt-er2/make_v152.py', run_name='__main__')

src = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5_2.cs')
out = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5_3.cs')
s = src.read_text(encoding='utf-8')
s = s.replace('public const string PluginVersion = "1.5.2";', 'public const string PluginVersion = "1.5.3";')

# Extra native handles used to forcibly rebuild the member-edit UI after reopening
# an existing CustomSquad member.  Assembly-CSharp exposes editedMember as the
# currently selected CustomSquadMember; that state can survive/poison later edits.
needle = '        private static MethodInfo _editMemberInstance;\n'
replacement = '''        private static MethodInfo _editMemberInstance;\n        private static MethodInfo _setButtonsEditMember;\n        private static MethodInfo _clearButtons;\n        private static MethodInfo _refreshMemberByIndex;\n'''
if needle not in s:
    raise SystemExit('Could not find _editMemberInstance field')
s = s.replace(needle, replacement, 1)

needle = '''            _editMemberInstance = AccessTools.Method(_squadEditorType, "EditMember", new[] { typeof(int) });\n            MethodInfo editOrDuplicateMember = AccessTools.Method(_squadEditorType, "EditOrDuplicateMember", new[] { typeof(int) });\n'''
replacement = '''            _editMemberInstance = AccessTools.Method(_squadEditorType, "EditMember", new[] { typeof(int) });\n            _setButtonsEditMember = AccessTools.Method(_squadEditorType, "SetButtonsEditMember", new[] { typeof(int) });\n            _clearButtons = AccessTools.Method(_squadEditorType, "ClearButtons", Type.EmptyTypes);\n            _refreshMemberByIndex = AccessTools.Method(_squadEditorType, "RefreshMember", new[] { typeof(int) });\n            MethodInfo editOrDuplicateMember = AccessTools.Method(_squadEditorType, "EditOrDuplicateMember", new[] { typeof(int) });\n'''
if needle not in s:
    raise SystemExit('Could not find method resolve point')
s = s.replace(needle, replacement, 1)

# Replace the 1.5.2 direct-dispatch body with a state reset + forced button rebuild.
old = '''                if (__instance == null || _editMemberInstance == null) return true;\n                _editMemberInstance.Invoke(__instance, new object[] { __0 });\n                Plugin.ModLog.LogInfo("MEMBER-REEDIT direct EditMember(" + __0 + ") dispatched.");\n                return false;\n'''
new = '''                if (__instance == null || _editMemberInstance == null) return true;\n\n                object editedSquad = R.Get(__instance, "edited");\n                object members = R.Get(editedSquad, "members");\n                object selectedMember = R.Item(members, __0);\n                object staleMember = R.Get(__instance, "editedMember");\n                Plugin.ModLog.LogInfo("MEMBER-STATE before index=" + __0 +\n                    " editedNull=" + (editedSquad == null) +\n                    " selectedNull=" + (selectedMember == null) +\n                    " staleNull=" + (staleMember == null) +\n                    " same=" + (selectedMember != null && ReferenceEquals(selectedMember, staleMember)) + ".");\n\n                // The stock scene keeps editedMember as mutable edit-session state.\n                // A duplicated member is a fresh object, which is why Shift-copy can\n                // enter editing while a previously existing member can get stuck.\n                // Clear the stale selection before asking ER2 to initialize the member.\n                R.Set(__instance, "editedMember", null);\n                _editMemberInstance.Invoke(__instance, new object[] { __0 });\n\n                object after = R.Get(__instance, "editedMember");\n                Plugin.ModLog.LogInfo("MEMBER-STATE after-native index=" + __0 +\n                    " editedMemberNull=" + (after == null) + ".");\n\n                // If ER2 still did not bind the selected member, bind the exact member\n                // ourselves.  Then rebuild the normal native member-aspect buttons.\n                if (after == null && selectedMember != null)\n                {\n                    R.Set(__instance, "editedMember", selectedMember);\n                    after = R.Get(__instance, "editedMember");\n                    Plugin.ModLog.LogInfo("MEMBER-STATE forced-bind index=" + __0 +\n                        " success=" + (after != null) + ".");\n                }\n\n                if (_clearButtons != null) _clearButtons.Invoke(__instance, null);\n                if (_setButtonsEditMember != null)\n                {\n                    _setButtonsEditMember.Invoke(__instance, new object[] { __0 });\n                    Plugin.ModLog.LogInfo("MEMBER-UI SetButtonsEditMember(" + __0 + ") forced.");\n                }\n                if (_refreshMemberByIndex != null)\n                    _refreshMemberByIndex.Invoke(__instance, new object[] { __0 });\n\n                Plugin.ModLog.LogInfo("MEMBER-REEDIT EditMember(" + __0 + ") + forced UI rebuild dispatched.");\n                return false;\n'''
if old not in s:
    raise SystemExit('Could not find v1.5.2 re-edit body')
s = s.replace(old, new, 1)

# Update load log wording.
s = s.replace(
    'Member re-edit bridge installed: normal member clicks bypass EditOrDuplicateMember -> EditMember directly; Shift-click stays vanilla.',
    'Member re-edit bridge installed: stale editedMember is reset and native member-edit UI is forcibly rebuilt; Shift-click stays vanilla.'
)

out.write_text(s, encoding='utf-8')
print(out)
