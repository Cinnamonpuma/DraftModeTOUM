using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    public enum RoleFaction
    {
        Crewmate,
        Impostor,
        Neutral,
        NeutralKilling
    }

    // ROLES BELOW ARE IMPOSTOR ROLES.

    public static class RoleCategory
    {
        private static readonly HashSet<string> ImpostorRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Concealing
            "Eclipsal", "Escapist", "Grenadier", "Morphling", "Swooper", "Venerer",
            // Killing
            "Ambusher", "Bomber", "Parasite", "Scavenger", "Warlock",
            // Power
            "Ambassador", "Puppeteer", "Spellslinger", "Traitor",
            // Support
            "Blackmailer", "Hypnotist", "Janitor", "Miner", "Undertaker"
        };

        private static readonly HashSet<string> NeutralKillingRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Arsonist", "Glitch", "Juggernaut", "Plaguebearer", "Pestilence", "SoulCollector", "Vampire", "Werewolf"
        };

        private static readonly HashSet<string> NeutralOtherRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Benign
            "Amnesiac", "Fairy", "Mercenary", "Survivor",
            // Evil
            "Doomsayer", "Executioner", "Jester", "Spectre",
            // Outlier
            "Chef", "Inquisitor"
        };

        public static RoleFaction GetFaction(string roleName)
        {
            var normalized = Normalize(roleName);
            if (ImpostorRoles.Contains(normalized))      return RoleFaction.Impostor;
            if (NeutralKillingRoles.Contains(normalized)) return RoleFaction.NeutralKilling;
            if (NeutralOtherRoles.Contains(normalized)) return RoleFaction.Neutral;
            return RoleFaction.Crewmate;
        }

        public static bool IsImpostor(string roleName)       => ImpostorRoles.Contains(Normalize(roleName));
        public static bool IsNeutralKilling(string roleName) => NeutralKillingRoles.Contains(Normalize(roleName));
        public static bool IsNeutral(string roleName)        => NeutralOtherRoles.Contains(Normalize(roleName));

        private static string Normalize(string roleName)
        {
            return (roleName ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);
        }
    }
}
