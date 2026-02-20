using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class RoleColors
    {
        public static readonly Color CrewFallback     = new Color32(75,  215, 228, 255);
        public static readonly Color ImpostorFallback = new Color32(255, 70,  70,  255);
        public static readonly Color NeutralFallback  = new Color32(171, 70,  255, 255);
        public static readonly Color RandomColour     = new Color32(59,  204, 59,  255);

        public static Color GetColor(string roleName)
        {
            return roleName.Replace(" ", "").ToLowerInvariant() switch
            {
                // Crew Investigative
                "aurial"       => new Color32(179, 77,  153, 255),
                "investigator" => new Color32(0,   179, 179, 255),
                "lookout"      => new Color32(51,  255, 102, 255),
                "mystic"       => new Color32(77,  153, 230, 255),
                "seer"         => new Color32(255, 204, 128, 255),
                "snitch"       => new Color32(212, 176, 56,  255),
                "trapper"      => new Color32(166, 209, 179, 255),
                // Crew Killing
                "deputy"       => new Color32(255, 204, 0,   255),
                "hunter"       => new Color32(41,  171, 135, 255),
                "sheriff"      => new Color32(255, 255, 0,   255),
                "veteran"      => new Color32(153, 128, 64,  255),
                "vigilante"    => new Color32(255, 255, 153, 255),
                // Crew Power
                "jailor"       => new Color32(166, 166, 166, 255),
                "monarch"      => new Color32(234, 83,  91,  255),
                "politician"   => new Color32(102, 0,   153, 255),
                "prosecutor"   => new Color32(179, 128, 0,   255),
                "swapper"      => new Color32(102, 230, 102, 255),
                "timelord"     => new Color32(135, 137, 211, 255),
                // Crew Protective
                "altruist"     => new Color32(102, 0,   0,   255),
                "cleric"       => new Color32(0,   255, 179, 255),
                "medic"        => new Color32(0,   102, 0,   255),
                "mirrorcaster" => new Color32(144, 162, 195, 255),
                "oracle"       => new Color32(191, 0,   191, 255),
                "warden"       => new Color32(153, 0,   255, 255),
                // Crew Support
                "engineer"     => new Color32(255, 166, 10,  255),
                "imitator"     => new Color32(179, 217, 77,  255),
                "medium"       => new Color32(166, 128, 255, 255),
                "plumber"      => new Color32(204, 102, 0,   255),
                "sentry"       => new Color32(100, 150, 200, 255),
                "transporter"  => new Color32(0,   237, 255, 255),
                // Impostors
                "eclipsal"     => ImpostorFallback,
                "escapist"     => ImpostorFallback,
                "grenadier"    => ImpostorFallback,
                "morphling"    => ImpostorFallback,
                "swooper"      => ImpostorFallback,
                "venerer"      => ImpostorFallback,
                "ambusher"     => ImpostorFallback,
                "bomber"       => ImpostorFallback,
                "parasite"     => ImpostorFallback,
                "scavenger"    => ImpostorFallback,
                "warlock"      => ImpostorFallback,
                "ambassador"   => ImpostorFallback,
                "puppeteer"    => ImpostorFallback,
                "spellslinger" => ImpostorFallback,
                "blackmailer"  => ImpostorFallback,
                "hypnotist"    => ImpostorFallback,
                "janitor"      => ImpostorFallback,
                "miner"        => ImpostorFallback,
                "undertaker"   => ImpostorFallback,
                "traitor"      => ImpostorFallback,
                // Neutral Benign
                "amnesiac"     => new Color32(128, 179, 255, 255),
                "fairy"        => NeutralFallback,
                "mercenary"    => new Color32(140, 102, 153, 255),
                "survivor"     => new Color32(255, 230, 77,  255),
                // Neutral Evil
                "chef"         => new Color32(218, 162, 103, 255),
                "doomsayer"    => new Color32(0,   255, 128, 255),
                "executioner"  => new Color32(99,  59,  31,  255),
                "inquisitor"   => new Color32(217, 66,  145, 255),
                "jester"       => new Color32(255, 191, 204, 255),
                // Neutral Killing
                "arsonist"     => new Color32(255, 77,  0,   255),
                "glitch"       => Color.green,
                "juggernaut"   => new Color32(140, 0,   77,  255),
                "plaguebearer" => new Color32(230, 255, 179, 255),
                "pestilence"   => new Color32(77,  77,  77,  255),
                "soulcollector"=> new Color32(153, 255, 204, 255),
                "vampire"      => new Color32(163, 41,  41,  255),
                "werewolf"     => new Color32(168, 102, 41,  255),
                // Fallback by faction
                _ => RoleCategory.GetFaction(roleName) switch
                {
                    RoleFaction.Impostor => ImpostorFallback,
                    RoleFaction.Neutral  => NeutralFallback,
                    _                    => CrewFallback
                }
            };
        }
    }
}
