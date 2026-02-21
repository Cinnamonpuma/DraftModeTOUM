using DraftModeTOUM.Managers;
using HarmonyLib;
using InnerNet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    public static class RequireModPatch
    {
        public static bool RequireDraftMod { get; set; } = true;

        private const string MOD_NAME = "DraftModeTOUM";

        // How long (seconds) to wait after join before doing the fallback verification kick.
        // ReceiveClientModInfo usually fires within 1-2 seconds; 8s gives plenty of margin.
        private const float FALLBACK_KICK_DELAY = 8f;

        private static string RequiredEntry =>
            $"{MOD_NAME}: {PluginInfo.PLUGIN_VERSION}";

        private static readonly HashSet<int> _verifiedClients = new HashSet<int>();

        // Kicked for joining mid-draft — re-kick immediately on rejoin, no second chance
        private static readonly HashSet<string> _draftKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Kicked for missing/wrong mod — do NOT re-kick on join, let ModInfoPostfix verify first.
        // If they still fail verification, ModInfoPostfix kicks them again and re-adds them here.
        private static readonly HashSet<string> _modKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pending fallback kicks: clientId -> (playerName, timeOfJoin)
        private static readonly Dictionary<int, (string playerName, float deadline)> _pendingFallbacks
            = new Dictionary<int, (string, float)>();

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
                return;
            }

            // Register a fallback deadline. FallbackTick (called from Update) will
            // kick this player if ModInfoPostfix hasn't verified them in time.
            if (RequireDraftMod)
            {
                _pendingFallbacks[data.Id] = (playerName, Time.realtimeSinceStartup + FALLBACK_KICK_DELAY);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Registered fallback kick for {playerName} (clientId={data.Id}) in {FALLBACK_KICK_DELAY}s.");
            }
        }

        /// <summary>
        /// Called every frame from FallbackTickPatch. Checks if any pending fallback
        /// deadlines have passed and kicks unverified players.
        /// </summary>
        public static void FallbackTick()
        {
            if (_pendingFallbacks.Count == 0) return;
            if (!AmongUsClient.Instance.AmHost || !RequireDraftMod)
            {
                _pendingFallbacks.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            var expired = new List<int>();

            foreach (var kvp in _pendingFallbacks)
            {
                if (now >= kvp.Value.deadline)
                    expired.Add(kvp.Key);
            }

            foreach (int clientId in expired)
            {
                var (playerName, _) = _pendingFallbacks[clientId];
                _pendingFallbacks.Remove(clientId);

                // Already verified — no action needed
                if (_verifiedClients.Contains(clientId))
                {
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Fallback: {playerName} (clientId={clientId}) already verified — no kick.");
                    continue;
                }

                // Check if the player is still in the lobby
                var client = AmongUsClient.Instance.GetClient(clientId);
                if (client == null)
                {
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Fallback: {playerName} (clientId={clientId}) already left — no kick.");
                    continue;
                }

                // Still here and unverified — kick them
                DraftModePlugin.Logger.LogWarning(
                    $"[RequireModPatch] Fallback kick: {playerName} (clientId={clientId}) never verified. Kicking.");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — could not verify <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}. Please ensure you have the mod installed.</color>");
                _modKickedPlayers.Add(playerName);
                AmongUsClient.Instance.KickPlayer(clientId, false);
            }
        }

        public static void ModInfoPostfix(PlayerControl client, Dictionary<byte, string> list)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;
            if (client.AmOwner) return;

            var playerInfo = GameData.Instance.GetPlayerById(client.PlayerId);
            if (playerInfo == null) return;

            string playerName = client.Data.PlayerName ?? string.Empty;

            if (_verifiedClients.Contains(playerInfo.ClientId))
            {
                _pendingFallbacks.Remove(playerInfo.ClientId);
                return;
            }

            bool hasMod = list.Values.Any(v =>
                v.Contains(MOD_NAME, StringComparison.OrdinalIgnoreCase));

            bool hasCorrectVersion = list.Values.Any(v =>
                v.Contains(RequiredEntry, StringComparison.OrdinalIgnoreCase));

            if (hasCorrectVersion)
            {
                _verifiedClients.Add(playerInfo.ClientId);
                _pendingFallbacks.Remove(playerInfo.ClientId);
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

            _pendingFallbacks.Remove(playerInfo.ClientId);
            _modKickedPlayers.Add(playerName);
            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {playerName} ({playerInfo.ClientId}) — {reason}.");
        }

        public static void ClearSession()
        {
            _pendingFallbacks.Clear();
            _verifiedClients.Clear();
            _draftKickedPlayers.Clear();
            _modKickedPlayers.Clear();
        }
    }
}
