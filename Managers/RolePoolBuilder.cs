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
        // Keyed by RoleTypes cast to ushort — no name strings in the pool at all
        public List<ushort> RoleIds { get; } = new();
        public Dictionary<ushort, int> MaxCounts { get; } = new();
        public Dictionary<ushort, int> Weights { get; } = new();
        public Dictionary<ushort, RoleFaction> Factions { get; } = new();
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

            if (pool.RoleIds.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning(
                    "[RolePoolBuilder] No enabled roles detected — using fallback role list");
                BuildFallback(pool);
            }

            DraftModePlugin.Logger.LogInfo(
                $"[RolePoolBuilder] Found {pool.RoleIds.Count} enabled roles");

            return pool;
        }

        private static void BuildFromRoleOptions(DraftRolePool pool)
        {
            var roleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
            if (roleOptions == null) return;

            int playerCount = GameData.Instance != null
                ? GameData.Instance.AllPlayers.ToArray().Count(p => p != null && !p.Disconnected)
                : 10;

            IEnumerable<RoleBehaviour> roles;
            try
            {
                roles = MiscUtils.GetPotentialRoles().ToArray();
                DraftModePlugin.Logger.LogInfo(
                    $"[RolePoolBuilder] GetPotentialRoles returned {roles.Count()} roles");
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RolePoolBuilder] GetPotentialRoles failed ({ex.Message}), falling back to AllRegisteredRoles");
                roles = MiscUtils.AllRegisteredRoles.ToArray();
            }

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

                // Filter out Hide and Seek exclusive roles by namespace
                var ns = role.GetType().Namespace;
                if (ns != null && (ns.Contains("HnsCrewmate") || ns.Contains("HnsImpostor"))) continue;

                if (IsBannedRole(role.NiceName)) continue;

                int count = roleOptions.GetNumPerGame(role.Role);
                int chance = roleOptions.GetChancePerGame(role.Role);
                if (count <= 0 || chance <= 0) continue;

                int cappedCount = Math.Min(count, playerCount);

                var faction = role.IsImpostor
                    ? RoleFaction.Impostor
                    : ((role is MiraAPI.Roles.ICustomRole cr__ && cr__.Team != ModdedRoleTeams.Crewmate && cr__.Team != ModdedRoleTeams.Impostor)
                        ? RoleCategory.GetFactionFromRole(role)
                        : RoleFaction.Crewmate);

                AddRole(pool, (ushort)role.Role, cappedCount, chance, faction);
            }
        }

        private static readonly HashSet<string> _bannedRoles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Haunter", "Spectre", "Teleporter", "Pestilence", "Traitor", "Mayor"
            };

        public static bool IsBannedRole(string niceName) => _bannedRoles.Contains(niceName);

        private static void AddRole(DraftRolePool pool, ushort roleId, int maxCount, int weight, RoleFaction faction)
        {
            if (!pool.MaxCounts.ContainsKey(roleId))
            {
                pool.RoleIds.Add(roleId);
                pool.MaxCounts[roleId] = Math.Max(1, maxCount);
                pool.Weights[roleId] = Math.Max(1, weight);
                pool.Factions[roleId] = faction;
            }
            else
            {
                pool.MaxCounts[roleId] = Math.Max(pool.MaxCounts[roleId], maxCount);
                pool.Weights[roleId] = Math.Max(pool.Weights[roleId], weight);
            }
        }

        private static void BuildFallback(DraftRolePool pool)
        {
            if (RoleManager.Instance == null) return;
            foreach (var role in RoleManager.Instance.AllRoles.ToArray())
            {
                if (role == null) continue;
                if (IsBannedRole(role.NiceName)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;

                // Filter out Hide and Seek exclusive roles by namespace
                var ns = role.GetType().Namespace;
                if (ns != null && (ns.Contains("HnsCrewmate") || ns.Contains("HnsImpostor"))) continue;

                var faction = role.IsImpostor
                    ? RoleFaction.Impostor
                    : ((role is MiraAPI.Roles.ICustomRole cr__ && cr__.Team != ModdedRoleTeams.Crewmate && cr__.Team != ModdedRoleTeams.Impostor)
                        ? RoleCategory.GetFactionFromRole(role)
                        : RoleFaction.Crewmate);

                AddRole(pool, (ushort)role.Role, 1, 100, faction);
            }
        }
    }
}