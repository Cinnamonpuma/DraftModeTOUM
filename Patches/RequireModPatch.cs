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


        private static readonly HashSet<int> _kickedClients = new HashSet<int>();

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


        public static void OnPlayerJoinedPostfix(ClientData client)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;


            if (client.Id == AmongUsClient.Instance.ClientId) return;


            if (_kickedClients.Contains(client.Id))
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Previously kicked client {client.Id} ({client.PlayerName}) rejoined — kicking again.");

                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{client.PlayerName} was kicked — {MOD_NAME} v{PluginInfo.PLUGIN_VERSION} required.</color>");

                AmongUsClient.Instance.KickPlayer(client.Id, false);
            }
        }

        public static void ModInfoPostfix(PlayerControl client, Dictionary<byte, string> list)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;
            if (client.AmOwner) return;

            var playerInfo = GameData.Instance.GetPlayerById(client.PlayerId);
            if (playerInfo == null) return;

 
            if (_verifiedClients.Contains(playerInfo.ClientId)) return;

            bool hasMod = list.Values.Any(v =>
    v.IndexOf(MOD_NAME, StringComparison.OrdinalIgnoreCase) >= 0);

bool hasCorrectVersion = list.Values.Any(v =>
    v.IndexOf(RequiredEntry, StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasCorrectVersion)
            {

                _verifiedClients.Add(playerInfo.ClientId);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] {client.Data.PlayerName} verified with {RequiredEntry}.");
                return;
            }

            string reason = hasMod
                ? $"outdated version of <b>{MOD_NAME}</b> — please update to v{PluginInfo.PLUGIN_VERSION}"
                : $"missing <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}";

            DraftManager.SendChatLocal(
                $"<color=#FF4444>{client.Data.PlayerName} was kicked — {reason}.</color>");


            _kickedClients.Add(playerInfo.ClientId);

            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {client.Data.PlayerName} ({playerInfo.ClientId}) — {reason}.");
        }


        public static void ClearSession()
        {
            _verifiedClients.Clear();
            _kickedClients.Clear();
        }
    }
}
