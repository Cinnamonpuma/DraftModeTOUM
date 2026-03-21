using DraftModeTOUM.Managers;
using HarmonyLib;
using MiraAPI.GameOptions;
using System.Linq;
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class ChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        public static bool Prefix(ChatController __instance)
        {
            string msg = __instance.freeChatField.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(msg)) return true;

            if (msg.StartsWith("/draft", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendChatLocal("<color=red>Only host can start draft.</color>");
                }
                else if (DraftManager.IsDraftActive)
                {
                    DraftManager.SendChatLocal("<color=red>Draft already active.</color>");
                }
                else if (!OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft)
                {
                    DraftManager.SendChatLocal("<color=red>Draft Mode is disabled in settings.</color>");
                }
                else
                {
                    DraftManager.StartDraft();
                }
                ClearChat(__instance);
                return false;
            }

            if (msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendChatLocal("<color=red>Only host can end the draft.</color>");
                }
                else if (!DraftManager.IsDraftActive)
                {
                    DraftManager.SendChatLocal("<color=red>No draft is currently active.</color>");
                }
                else
                {
                    DraftManager.Reset(cancelledBeforeCompletion: true);
                    DraftManager.SendChatLocal("<color=#FFD700>Draft has been cancelled by the host.</color>");
                }
                ClearChat(__instance);
                return false;
            }

            if (msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendChatLocal("<color=red>Only host can change draft settings.</color>");
                }
                else
                {
                    var opts = OptionGroupSingleton<DraftModeOptions>.Instance;
                    opts.ShowRecap = !opts.ShowRecap;
                    DraftManager.ShowRecap = opts.ShowRecap;
                    string status = DraftManager.ShowRecap ? "<color=green>ON</color>" : "<color=red>OFF</color>";
                    DraftManager.SendChatLocal($"<color=#FFD700>Draft recap is now: {status}</color>");
                }
                ClearChat(__instance);
                return false;
            }

            return true;
        }

        private static void ClearChat(ChatController chat)
        {
            chat.freeChatField.Clear();
            chat.quickChatMenu.Clear();
            chat.quickChatField.Clear();
            chat.UpdateChatMode();
        }
    }
}
