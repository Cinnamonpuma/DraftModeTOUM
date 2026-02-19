using DraftModeTOUM.Managers;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DraftModeTOUM.Patches
{
    public static class RequireModPatch
    {
        public static bool RequireDraftMod { get; set; } = true;

        private const string MOD_NAME = "DraftModeTOUM";

        public static void Apply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    "TownOfUs.Networking.SendClientModInfoRpc:ReceiveClientModInfo");

                if (target == null)
                {
                    DraftModePlugin.Logger.LogError(
                        "[RequireModPatch] Could not find ReceiveClientModInfo — patch skipped.");
                    return;
                }

                var postfix = new HarmonyMethod(
                    typeof(RequireModPatch),
                    nameof(Postfix));

                harmony.Patch(target, postfix: postfix);
                DraftModePlugin.Logger.LogInfo("[RequireModPatch] Patched ReceiveClientModInfo successfully.");
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[RequireModPatch] Failed to apply patch: {ex}");
            }
        }

        public static void Postfix(PlayerControl client, Dictionary<byte, string> list)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;
            if (client.AmOwner) return;

            bool hasMod = list.Values.Any(v =>
                v.Contains(MOD_NAME, StringComparison.OrdinalIgnoreCase));

            if (hasMod) return;

            var playerInfo = GameData.Instance.GetPlayerById(client.PlayerId);
            if (playerInfo == null) return;

            DraftManager.SendChatLocal(
                $"<color=#FF4444>{client.Data.PlayerName} was kicked — <b>{MOD_NAME}</b> is required to join.</color>");

            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {client.Data.PlayerName} — missing {MOD_NAME}.");
        }
    }
}