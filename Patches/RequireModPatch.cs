using DraftModeTOUM.Managers;
using HarmonyLib;
using InnerNet;
using System;
using System.Collections;
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

        // Tracks pending fallback coroutine host objects: clientId -> GameObject
        private static readonly Dictionary<int, GameObject> _pendingFallbacks = new Dictionary<int, GameObject>();

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

            // Schedule a fallback verification kick in case ReceiveClientModInfo never fires
            // (e.g. Reactor crashes on an unknown mod key like Submerged before reaching it).
            if (RequireDraftMod)
            {
                ScheduleFallbackKick(data.Id, playerName);
            }
        }

        /// <summary>
        /// Schedules a coroutine that kicks the player if they haven't been verified
        /// within FALLBACK_KICK_DELAY seconds. This covers cases where Reactor's
        /// HandleRpc crashes before ReceiveClientModInfo fires (e.g. Submerged key miss).
        /// </summary>
        private static void ScheduleFallbackKick(int clientId, string playerName)
        {
            // Cancel any existing pending fallback for this client (e.g. rapid rejoin)
            CancelFallbackKick(clientId);

            var go = new GameObject($"DraftModFallbackKick_{clientId}");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<CoroutineRunner>();
            runner.Run(FallbackKickCoroutine(clientId, playerName, go));
            _pendingFallbacks[clientId] = go;

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Scheduled fallback kick for {playerName} (clientId={clientId}) in {FALLBACK_KICK_DELAY}s.");
        }

        private static void CancelFallbackKick(int clientId)
        {
            if (_pendingFallbacks.TryGetValue(clientId, out var go))
            {
                if (go != null)
                    UnityEngine.Object.Destroy(go);
                _pendingFallbacks.Remove(clientId);
            }
        }

        private static IEnumerator FallbackKickCoroutine(int clientId, string playerName, GameObject self)
        {
            yield return new WaitForSeconds(FALLBACK_KICK_DELAY);

            _pendingFallbacks.Remove(clientId);

            if (!AmongUsClient.Instance.AmHost || !RequireDraftMod)
            {
                UnityEngine.Object.Destroy(self);
                yield break;
            }

            // If already verified, no action needed
            if (_verifiedClients.Contains(clientId))
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Fallback: {playerName} (clientId={clientId}) already verified — no kick.");
                UnityEngine.Object.Destroy(self);
                yield break;
            }

            // Check if the player is still in the lobby
            var client = AmongUsClient.Instance.GetClient(clientId);
            if (client == null)
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Fallback: {playerName} (clientId={clientId}) already left — no kick.");
                UnityEngine.Object.Destroy(self);
                yield break;
            }

            // Player is still here and unverified — kick them
            DraftModePlugin.Logger.LogWarning(
                $"[RequireModPatch] Fallback kick: {playerName} (clientId={clientId}) never verified by ModInfoPostfix. Kicking.");
            DraftManager.SendChatLocal(
                $"<color=#FF4444>{playerName} was kicked — could not verify <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}. Please ensure you have the mod installed.</color>");
            _modKickedPlayers.Add(playerName);
            AmongUsClient.Instance.KickPlayer(clientId, false);

            UnityEngine.Object.Destroy(self);
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
                // Already verified — cancel any pending fallback just in case
                CancelFallbackKick(playerInfo.ClientId);
                return;
            }

            bool hasMod = list.Values.Any(v =>
                v.Contains(MOD_NAME, StringComparison.OrdinalIgnoreCase));

            bool hasCorrectVersion = list.Values.Any(v =>
                v.Contains(RequiredEntry, StringComparison.OrdinalIgnoreCase));

            if (hasCorrectVersion)
            {
                _verifiedClients.Add(playerInfo.ClientId);
                CancelFallbackKick(playerInfo.ClientId);
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

            CancelFallbackKick(playerInfo.ClientId);
            _modKickedPlayers.Add(playerName);
            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {playerName} ({playerInfo.ClientId}) — {reason}.");
        }

        public static void ClearSession()
        {
            // Cancel all pending fallback coroutines
            foreach (var go in _pendingFallbacks.Values)
            {
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            }
            _pendingFallbacks.Clear();

            _verifiedClients.Clear();
            _draftKickedPlayers.Clear();
            _modKickedPlayers.Clear();
        }
    }

    /// <summary>
    /// Minimal MonoBehaviour used solely to run coroutines on a temp GameObject.
    /// </summary>
    internal class CoroutineRunner : MonoBehaviour
    {
        public void Run(IEnumerator coroutine) => StartCoroutine(coroutine);
    }
}
