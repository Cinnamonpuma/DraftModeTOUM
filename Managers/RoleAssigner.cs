using AmongUs.GameOptions;
using MiraAPI.Roles;
using System;
using TownOfUs.Roles;
using TownOfUs.Roles.Crewmate;
using TownOfUs.Roles.Impostor;
using TownOfUs.Roles.Neutral;
using TownOfUs.Roles.Other;

namespace DraftModeTOUM.Managers
{
    public static class RoleAssigner
    {
        public static void AssignRole(PlayerControl player, string roleName)
        {
            if (player == null || string.IsNullOrWhiteSpace(roleName))
                return;

            var normalized = Normalize(roleName);



            try
            {
                AssignInternal(player, normalized);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError(
                    $"[RoleAssigner] Failed to assign '{normalized}': {ex}");
            }
        }

        private static void AssignInternal(PlayerControl player, string role)
        {
            switch (role)
            {
                // ── Vanilla ─────────────────────────────────────
                case "crewmate": player.RpcSetRole(RoleTypes.Crewmate); return;
                case "impostor": player.RpcSetRole(RoleTypes.Impostor); return;

                // ── Crew Investigative ───────────────────────────
                case "aurial": Set(player, typeof(AurialRole)); return;
                case "forensic": Set(player, typeof(ForensicRole)); return;
                case "haunter": Set(player, typeof(HaunterRole)); return;
                case "investigator": Set(player, typeof(InvestigatorRole)); return;
                case "lookout": Set(player, typeof(LookoutRole)); return;
                case "mystic": Set(player, typeof(MysticRole)); return;
                case "seer": Set(player, typeof(SeerRole)); return;
                case "snitch": Set(player, typeof(SnitchRole)); return;
                case "sonar": Set(player, typeof(SonarRole)); return;
                case "spy": Set(player, typeof(SpyRole)); return;
                case "trapper": Set(player, typeof(TrapperRole)); return;

                // ── Crew Killing ────────────────────────────────
                case "deputy": Set(player, typeof(DeputyRole)); return;
                case "hunter": Set(player, typeof(HunterRole)); return;
                case "sheriff": Set(player, typeof(SheriffRole)); return;
                case "veteran": Set(player, typeof(VeteranRole)); return;
                case "vigilante": Set(player, typeof(VigilanteRole)); return;

                // ── Crew Power ──────────────────────────────────
                case "jailor": Set(player, typeof(JailorRole)); return;
                case "mayor": Set(player, typeof(MayorRole)); return;
                case "monarch": Set(player, typeof(MonarchRole)); return;
                case "politician": Set(player, typeof(PoliticianRole)); return;
                case "prosecutor": Set(player, typeof(ProsecutorRole)); return;
                case "swapper": Set(player, typeof(SwapperRole)); return;
                case "timelord": Set(player, typeof(TimeLordRole)); return;

                // ── Crew Protective ─────────────────────────────
                case "altruist": Set(player, typeof(AltruistRole)); return;
                case "cleric": Set(player, typeof(ClericRole)); return;
                case "medic": Set(player, typeof(MedicRole)); return;
                case "mirrorcaster": Set(player, typeof(MirrorcasterRole)); return;
                case "oracle": Set(player, typeof(OracleRole)); return;
                case "warden": Set(player, typeof(WardenRole)); return;

                // ── Crew Support ────────────────────────────────
                case "engineer": Set(player, typeof(EngineerRole)); return;
                case "imitator": Set(player, typeof(ImitatorRole)); return;
                case "medium": Set(player, typeof(MediumRole)); return;
                case "plumber": Set(player, typeof(PlumberRole)); return;
                case "sentry": Set(player, typeof(SentryRole)); return;
                case "transporter": Set(player, typeof(TransporterRole)); return;

                // ── Impostor Concealing ─────────────────────────
                case "eclipsal": Set(player, typeof(EclipsalRole)); return;
                case "escapist": Set(player, typeof(EscapistRole)); return;
                case "grenadier": Set(player, typeof(GrenadierRole)); return;
                case "morphling": Set(player, typeof(MorphlingRole)); return;
                case "swooper": Set(player, typeof(SwooperRole)); return;
                case "venerer": Set(player, typeof(VenererRole)); return;

                // ── Impostor Killing ────────────────────────────
                case "ambusher": Set(player, typeof(AmbusherRole)); return;
                case "bomber": Set(player, typeof(BomberRole)); return;
                case "parasite": Set(player, typeof(ParasiteRole)); return;
                case "scavenger": Set(player, typeof(ScavengerRole)); return;
                case "warlock": Set(player, typeof(WarlockRole)); return;

                // ── Impostor Power ──────────────────────────────
                case "ambassador": Set(player, typeof(AmbassadorRole)); return;
                case "puppeteer": Set(player, typeof(PuppeteerRole)); return;
                case "spellslinger": Set(player, typeof(SpellslingerRole)); return;
                case "traitor": Set(player, typeof(TraitorRole)); return;

                // ── Impostor Support ────────────────────────────
                case "blackmailer": Set(player, typeof(BlackmailerRole)); return;
                case "hypnotist": Set(player, typeof(HypnotistRole)); return;
                case "janitor": Set(player, typeof(JanitorRole)); return;
                case "miner": Set(player, typeof(MinerRole)); return;
                case "undertaker": Set(player, typeof(UndertakerRole)); return;

                // ── Neutral Benign ──────────────────────────────
                case "amnesiac": Set(player, typeof(AmnesiacRole)); return;
                case "fairy": Set(player, typeof(FairyRole)); return;
                case "mercenary": Set(player, typeof(MercenaryRole)); return;
                case "survivor": Set(player, typeof(SurvivorRole)); return;

                // ── Neutral Evil ────────────────────────────────
                case "doomsayer": Set(player, typeof(DoomsayerRole)); return;
                case "executioner": Set(player, typeof(ExecutionerRole)); return;
                case "jester": Set(player, typeof(JesterRole)); return;
                case "spectre": Set(player, typeof(SpectreRole)); return;

                // ── Neutral Killing ─────────────────────────────
                case "arsonist": Set(player, typeof(ArsonistRole)); return;
                case "glitch": Set(player, typeof(GlitchRole)); return;
                case "juggernaut": Set(player, typeof(JuggernautRole)); return;
                case "plaguebearer": Set(player, typeof(PlaguebearerRole)); return;
                case "pestilence": Set(player, typeof(PestilenceRole)); return;
                case "soulcollector": Set(player, typeof(SoulCollectorRole)); return;
                case "vampire": Set(player, typeof(VampireRole)); return;
                case "werewolf": Set(player, typeof(WerewolfRole)); return;

                // ── Neutral Outlier ─────────────────────────────
                case "chef": Set(player, typeof(ChefRole)); return;
                case "inquisitor": Set(player, typeof(InquisitorRole)); return;

                default:
                    DraftModePlugin.Logger.LogError(
                        $"[RoleAssigner] Unknown role '{role}'. No role assigned.");
                    return;
            }
        }

        private static string Normalize(string role)
        {
            return role
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "");
        }

        private static void Set(PlayerControl player, Type roleBehaviourType)
        {
            var roleId = (RoleTypes)RoleId.Get(roleBehaviourType);
            player.RpcSetRole(roleId);
        }
    }
}
