using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public sealed class DraftRolePool
    {
        public List<string> Roles { get; } = new List<string>();
        public Dictionary<string, int> MaxCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Weights { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleFaction> Factions { get; } = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);
    }

    public static class RolePoolBuilder
    {
        public static DraftRolePool BuildPool()
        {
            var pool = new DraftRolePool();

            try
            {
                BuildFromRoleOptions(pool);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RolePoolBuilder] Failed reading role options: {ex.Message}");
            }

            if (pool.Roles.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning(
                    "[RolePoolBuilder] No enabled roles detected — using fallback role list");

                foreach (var roleName in GetAllRoles())
                {
                    AddRole(pool, roleName, 1, 100, RoleCategory.GetFaction(roleName));
                }
            }

            DraftModePlugin.Logger.LogInfo(
                $"[RolePoolBuilder] Found {pool.Roles.Count} enabled roles");

            return pool;
        }

        private static void BuildFromRoleOptions(DraftRolePool pool)
        {
            var roleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
            if (roleOptions == null) return;

            var roles = MiscUtils.AllRegisteredRoles.ToArray();

            foreach (var role in roles)
            {
                if (role == null) continue;
                if (!CustomRoleUtils.CanSpawnOnCurrentMode(role)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;

                if (role is ICustomRole customRole)
                {
                    if (customRole.Configuration.HideSettings || !customRole.VisibleInSettings())
                        continue;
                }

                // Hard-banned roles — never appear in draft regardless of settings
                var rn = role.NiceName ?? "";
                if (IsBannedRole(rn)) continue;

                int count = roleOptions.GetNumPerGame(role.Role);
                int chance = roleOptions.GetChancePerGame(role.Role);
                if (count <= 0 || chance <= 0) continue;

                var roleName = role.GetRoleName();
                // Use RoleCategory for neutrals so killing vs passive is correctly split
                var faction = role.IsImpostor()
                    ? RoleFaction.Impostor
                    : (role.IsNeutral() ? RoleCategory.GetFaction(roleName) : RoleFaction.Crewmate);

                AddRole(pool, roleName, count, chance, faction);
            }
        }

        // Roles that can never be drafted no matter what the host configures
        private static readonly HashSet<string> _bannedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Haunter", "Spectre", "Teleporter", "Pestilence", "Traitor", "Mayor"
        };

        public static bool IsBannedRole(string roleName) => _bannedRoles.Contains(roleName);

        private static void AddRole(DraftRolePool pool, string roleName, int maxCount, int weight, RoleFaction faction)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return;
            if (!pool.MaxCounts.ContainsKey(roleName))
            {
                pool.Roles.Add(roleName);
                pool.MaxCounts[roleName] = Math.Max(1, maxCount);
                pool.Weights[roleName] = Math.Max(1, weight);
                pool.Factions[roleName] = faction;
            }
            else
            {
                pool.MaxCounts[roleName] = Math.Max(pool.MaxCounts[roleName], maxCount);
                pool.Weights[roleName] = Math.Max(pool.Weights[roleName], weight);
            }
        }

        private static IEnumerable<string> GetAllRoles()
        {
            return new[]
            {
                "Aurial","Forensic","Lookout","Mystic","Seer",
                "Snitch","Sonar","Trapper", "Deputy", "Sheriff",
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