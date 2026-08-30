from pathlib import Path
import runpy

# First generate the known-good 1.5 source from the 1.4 base.
runpy.run_path('.chatgpt-er2/make_v15.py', run_name='__main__')

src = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5.cs')
out = Path('.chatgpt-er2/ER2OfficialContentReveal_v1_5_1.cs')
s = src.read_text(encoding='utf-8')

# Be defensive about the generator's historical field-name aliases.
s = s.replace('_itemsDatabaseType', '_itemsDbType')
s = s.replace('_attachmentScopeType', '_scopeType')
s = s.replace('_attachmentBipodType', '_bipodType')
s = s.replace('_attachmentBayonetType', '_bayonetType')
s = s.replace('public const string PluginVersion = "1.5.0";', 'public const string PluginVersion = "1.5.1";')

needle = '''                if (battle == null || string.IsNullOrEmpty(selectedId)) return false;\n\n                object official = ResolveOfficial(__0, selectedId);'''
replacement = '''                if (battle == null || string.IsNullOrEmpty(selectedId)) return false;\n\n                // The combined picker contains BOTH official SquadData and the mission's\n                // already-created CustomSquads.  1.5 accidentally fed a custom squad_id\n                // (eg squad_5773) back through ItemsDatabase.GetSquadLoadouts(), which\n                // resolves to ER2's one-man USA unarmed fallback.  If this ID already\n                // belongs to current_battle.customSquads, open that exact object instead.\n                int existingIndex = FindCustomSquadIndex(battle, selectedId, null);\n                if (existingIndex >= 0)\n                {\n                    object existing = R.Item(R.Get(battle, "customSquads"), existingIndex);\n                    Plugin.ModLog.LogInfo("Selected existing custom squad '" + selectedId +\n                        "' at index " + existingIndex + "; opening directly (no official reconversion).");\n                    R.Set(gui, "edit_squad_id", existingIndex);\n                    _openPanel.Invoke(gui, new object[] { existingIndex });\n                    return false;\n                }\n\n                object official = ResolveOfficial(__0, selectedId);'''
if needle not in s:
    raise SystemExit('Could not find OnSquadPicked insertion point')
s = s.replace(needle, replacement, 1)

out.write_text(s, encoding='utf-8')
print(out)
