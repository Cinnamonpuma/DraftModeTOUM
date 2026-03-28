using MiraAPI.Roles;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Managers
{
    public enum RoleFaction
    {
        Crewmate,
        Impostor,
        Neutral,
        NeutralKilling
    }

    public static class RoleCategory
    {
        private static readonly HashSet<string> NeutralKillingRoles =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                "Arsonist", "Glitch", "Juggernaut", "Plaguebearer",
                "Pestilence", "SoulCollector", "Vampire", "Werewolf"
            };

        private static readonly HashSet<string> NeutralOtherRoles =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                "Amnesiac", "Fairy", "Mercenary", "Survivor",
                "Doomsayer", "Executioner", "Jester", "Spectre",
                "Chef", "Inquisitor"
            };

        
        
        
        
        
        
        public static RoleFaction GetFactionFromRole(RoleBehaviour role)
        {
            if (role == null) return RoleFaction.Crewmate;

            
            if (role.IsImpostor) return RoleFaction.Impostor;

            
            if (role is ICustomRole customRole &&
                customRole.Team != ModdedRoleTeams.Crewmate &&
                customRole.Team != ModdedRoleTeams.Impostor)
            {
                
                string normalized = Normalize(role.NiceName);
                if (NeutralKillingRoles.Contains(normalized)) return RoleFaction.NeutralKilling;
                return RoleFaction.Neutral;
            }

            return RoleFaction.Crewmate;
        }

        
        
        
        public static RoleFaction GetFaction(string roleName)
        {
            string normalized = Normalize(roleName);
            if (NeutralKillingRoles.Contains(normalized)) return RoleFaction.NeutralKilling;
            if (NeutralOtherRoles.Contains(normalized))   return RoleFaction.Neutral;

            
            if (RoleManager.Instance != null)
            {
                foreach (var r in RoleManager.Instance.AllRoles.ToArray())
                {
                    if (r == null) continue;
                    if (Normalize(r.NiceName) != normalized) continue;
                    return GetFactionFromRole(r);
                }
            }

            return RoleFaction.Crewmate;
        }

        public static bool IsNeutralKilling(string roleName) =>
            NeutralKillingRoles.Contains(Normalize(roleName));

        public static bool IsNeutral(string roleName) =>
            NeutralOtherRoles.Contains(Normalize(roleName));

        private static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "");
    }
}

