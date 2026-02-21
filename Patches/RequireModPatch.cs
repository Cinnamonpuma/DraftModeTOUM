using DraftModeTOUM.Managers;
using HarmonyLib;
using InnerNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Patches
{
    public static class RequireModPatch
    {
        public static bool RequireDraftMod { get; set; } = true;

        private const string MOD_NAME = "DraftModeTOUM";

        private static string RequiredEntry =>
            $"{MOD_NAME}: {PluginInfo.PLUGIN_VERSION}";

        private static readonly HashSet<int> _verifiedClients = new HashSet<int>();

        // Kicked for joining mid-draft — re-kick immediately on rejoin, no second chance
        private static readonly HashSet<string> _draftKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Kicked for missing/wrong mod — do NOT re-kick on join, let ModInfoPostfix verify first.
        // If they still fail verification, ModInfoPostfix kicks them again and re-adds them here.
        private static readonly HashSet<string> _modKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Apply(Harmony harmony)
        {
            try
            {
                var modInfoTarget = AccessTools.Method(
                    "TownOfUs.Networking.SendClientModInfoRpc:ReceiveClientModInfo");

                if (modInfoTarget == null)
                {
                    DraftModePlugin.Logger.LogError(
                        "[RequireModPatch] Could not find ReceiveClientModInfo — mod check patch skipped.");
                }
                else
                {
                    harmony.Patch(modInfoTarget,
                        postfix: new HarmonyMethod(typeof(RequireModPatch), nameof(ModInfoPostfix)));
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Patched ReceiveClientModInfo. Requiring: {RequiredEntry}");
                }

                var joinTarget = AccessTools.Method(
                    typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined));

                if (joinTarget == null)
                {
                    DraftModePlugin.Logger.LogError(
                        "[RequireModPatch] Could not find OnPlayerJoined — rejoin patch skipped.");
                }
                else
                {
                    harmony.Patch(joinTarget,
                        postfix: new HarmonyMethod(typeof(RequireModPatch), nameof(OnPlayerJoinedPostfix)));
                    DraftModePlugin.Logger.LogInfo("[RequireModPatch] Patched OnPlayerJoined.");
                }
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[RequireModPatch] Failed to apply patch: {ex}");
            }
        }

        public static void OnPlayerJoinedPostfix(ClientData data)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (data.Id == AmongUsClient.Instance.ClientId) return;

            string playerName = data.PlayerName ?? string.Empty;

            // Only immediately re-kick players who joined during an active draft —
            // mod-kicked players are allowed back in to give ModInfoPostfix a chance
            // to verify they now have the correct mod installed.
            if (_draftKickedPlayers.Contains(playerName))
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Draft-kicked player '{playerName}' rejoined — kicking again.");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — draft has already started.</color>");
                AmongUsClient.Instance.KickPlayer(data.Id, false);
                return;
            }

            if (DraftManager.IsDraftActive && DraftManager.LockLobbyOnDraftStart)
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Draft active — kicking client {data.Id} ({playerName}).");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — draft has already started.</color>");
                _draftKickedPlayers.Add(playerName);
                AmongUsClient.Instance.KickPlayer(data.Id, false);
            }

            // Note: mod-kicked players are allowed to proceed here so ModInfoPostfix
            // can verify them. If they still lack the mod, ModInfoPostfix kicks them again.
        }

        public static void ModInfoPostfix(PlayerControl client, Dictionary<byte, string> list)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;
            if (client.AmOwner) return;

            var playerInfo = GameData.Instance.GetPlayerById(client.PlayerId);
            if (playerInfo == null) return;

            string playerName = client.Data.PlayerName ?? string.Empty;

            if (_verifiedClients.Contains(playerInfo.ClientId)) return;

            bool hasMod = list.Values.Any(v =>
                v.Contains(MOD_NAME, StringComparison.OrdinalIgnoreCase));

            bool hasCorrectVersion = list.Values.Any(v =>
                v.Contains(RequiredEntry, StringComparison.OrdinalIgnoreCase));

            if (hasCorrectVersion)
            {
                _verifiedClients.Add(playerInfo.ClientId);
                // They now have the mod — clear from mod-kicked list so future rejoins work
                _modKickedPlayers.Remove(playerName);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] {playerName} verified with {RequiredEntry}.");
                return;
            }

            string reason = hasMod
                ? $"outdated version of <b>{MOD_NAME}</b> — please update to v{PluginInfo.PLUGIN_VERSION}"
                : $"missing <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}";

            DraftManager.SendChatLocal(
                $"<color=#FF4444>{playerName} was kicked — {reason}.</color>");

            _modKickedPlayers.Add(playerName);
            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {playerName} ({playerInfo.ClientId}) — {reason}.");
        }

        public static void ClearSession()
        {
            _verifiedClients.Clear();
            _draftKickedPlayers.Clear();
            _modKickedPlayers.Clear();
        }
    }
}