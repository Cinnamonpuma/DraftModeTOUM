using DraftModeTOUM.Managers;
using HarmonyLib;
using InnerNet;

namespace DraftModeTOUM.Patches
{
    
    
    
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    public static class KickOnJoinWhileLockedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ClientData client)
        {
            if (!DraftManager.IsDraftActive) return;
            if (!AmongUsClient.Instance.AmHost) return;
            if (client == null) return;

            DraftModePlugin.Logger.LogInfo(
                $"[LobbyLock] Kicking late-joiner '{client.PlayerName}' (id {client.Id}) — draft in progress.");

            AmongUsClient.Instance.KickPlayer(client.Id, false);
        }
    }
}
