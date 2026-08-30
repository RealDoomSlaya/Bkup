using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ER2OfficialContentReveal
{
    [BepInPlugin("er2.officialcontentreveal.namefix", "ER2 Official Content Reveal - Custom Squad Name Fix", "1.2.2")]
    public sealed class CustomSquadNameFixPlugin : BasePlugin
    {
        public override void Load()
        {
            try
            {
                var harmony = new Harmony("er2.officialcontentreveal.namefix");
                Type customSquad = AccessTools.TypeByName("CustomSquad");
                if (customSquad == null)
                {
                    Log.LogError("CustomSquad type not found.");
                    return;
                }

                ConstructorInfo target = null;
                foreach (var ctor in customSquad.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var p = ctor.GetParameters();
                    if (p.Length == 2 && p[0].ParameterType == typeof(string))
                    {
                        target = ctor;
                        break;
                    }
                }

                if (target == null)
                {
                    Log.LogError("CustomSquad(string, ...) constructor not found.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(typeof(CustomSquadNameFixPlugin), nameof(CustomSquadCtorPostfix)));
                Log.LogInfo("CustomSquad constructor name fix installed.");
            }
            catch (Exception ex)
            {
                Log.LogError("CustomSquad name-fix initialization failed: " + ex);
            }
        }

        public static void CustomSquadCtorPostfix(object __instance, string squadName_id)
        {
            if (__instance == null || string.IsNullOrEmpty(squadName_id))
                return;

            try
            {
                string current = GetString(__instance, "squad_name_private");
                bool unnamed = string.IsNullOrWhiteSpace(current) ||
                               current.IndexOf("unnamed", StringComparison.OrdinalIgnoreCase) >= 0;

                // Ordinary user-created squads use generated squad_#### IDs. Do not
                // turn those into ugly internal IDs. The official-aware bridge, on
                // the other hand, passes the visible official row name into this
                // constructor; ER2 then tries to localize it as an ID and falls back
                // to "Unnamed squad". Preserve the raw visible official name here.
                bool generatedCustomId = squadName_id.StartsWith("squad_", StringComparison.OrdinalIgnoreCase);

                if (unnamed && !generatedCustomId &&
                    squadName_id.IndexOf("unnamed", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    SetMember(__instance, "squad_name_private", squadName_id);
                    SetMember(__instance, "squadName", squadName_id);
                }
            }
            catch
            {
                // Never interfere with ER2's constructor if a field changes in a
                // future build; the main plugin will continue to function.
            }
        }

        private static string GetString(object obj, string name)
        {
            Type t = obj.GetType();
            while (t != null)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return p.GetValue(obj, null) as string;
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(obj) as string;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetMember(object obj, string name, string value)
        {
            Type t = obj.GetType();
            while (t != null)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(obj, value, null);
                    return;
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(obj, value);
                    return;
                }
                t = t.BaseType;
            }
        }
    }
}
