using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using TownOfUs.LocalSettings.Attributes; 

namespace DraftModeTOUM.Managers
{
    public static class RolePoolBuilder
    {
        public static List<string> BuildPool()
        {
            var enabledRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ReadFromLocalSettings(enabledRoles);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RolePoolBuilder] Failed reading LocalSettings: {ex.Message}");
            }

            if (enabledRoles.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning(
                    "[RolePoolBuilder] No enabled roles detected — using full role list");

                return GetAllRoles()
                    .OrderBy(_ => UnityEngine.Random.value)
                    .ToList();
            }

            DraftModePlugin.Logger.LogInfo(
                $"[RolePoolBuilder] Found {enabledRoles.Count} enabled roles");

            return enabledRoles
                .OrderBy(_ => UnityEngine.Random.value)
                .ToList();
        }

        private static void ReadFromLocalSettings(HashSet<string> result)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {

                if (!asm.GetName().Name.Contains("TownOfUs"))
                    continue;

                foreach (var type in asm.GetTypes())
                {

                    if (type.Namespace == null || !type.Namespace.StartsWith("TownOfUs.LocalSettings"))
                        continue;

                    ReadSettingsFromType(type, result);
                }
            }
        }

        private static void ReadSettingsFromType(Type type, HashSet<string> result)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static);

            foreach (var prop in properties)
            {

                var attr = prop.GetCustomAttribute<LocalizedLocalNumberSettingAttribute>();
                if (attr == null) continue;


                var configBase = prop.GetValue(null) as ConfigEntryBase;
                if (configBase == null) continue;


                float value = Convert.ToSingle(configBase.BoxedValue);

                if (value > 0)
                {
                    var roleName = prop.Name;


                    if (roleName.EndsWith("Count", StringComparison.OrdinalIgnoreCase))
                    {
                        roleName = roleName.Substring(0, roleName.Length - 5);
                    }

                    result.Add(roleName);
                }
            }
        }

        private static IEnumerable<string> GetAllRoles()
        {

            return new[]
            {
                "Aurial","Forensic","Lookout","Mystic","Seer",
                "Snitch","Sonar","Trapper", "Deputy","Hunter","Sheriff",
                "Veteran","Vigilante", "Jailor","Monarch","Politician",
                "Prosecutor","Swapper","Time Lord", "Altruist","Cleric","Medic",
                "Mirrorcaster","Oracle","Warden", "Engineer","Imitator","Medium",
                "Plumber","Sentry","Transporter", "Eclipsal","Escapist","Grenadier",
                "Morphling","Swooper","Venerer", "Ambusher","Bomber","Parasite",
                "Scavenger","Warlock", "Ambassador","Puppeteer","Spellslinger",
                "Blackmailer","Hypnotist","Janitor","Miner","Undertaker",
                "Fairy","Mercenary","Survivor", "Doomsayer","Executioner","Jester",
                 "Arsonist","Glitch","Juggernaut","Plaguebearer",
                "SoulCollector","Vampire","Werewolf", "Chef","Inquisitor"
            };
        }
    }
}