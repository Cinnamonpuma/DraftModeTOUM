using DraftModeTOUM.Managers;
using HarmonyLib;
using MiraAPI.LocalSettings;
using System.Linq;
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class ChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ChatController __instance)
        {
            string msg = __instance.freeChatField.textArea.text.Trim();
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
                else if (!LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance.EnableDraftToggle.Value)
                {
                    DraftManager.SendChatLocal("<color=red>Draft Mode is disabled in settings.</color>");
                }
                else
                {
                    DraftManager.StartDraft();
                }
                __instance.freeChatField.textArea.Clear();
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
                __instance.freeChatField.textArea.Clear();
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
                    var settings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
                    settings.ShowRecap.Value = !settings.ShowRecap.Value;
                    DraftManager.ShowRecap = settings.ShowRecap.Value;
                    string status = DraftManager.ShowRecap
                        ? "<color=green>ON</color>"
                        : "<color=red>OFF</color>";
                    DraftManager.SendChatLocal($"<color=#FFD700>Draft recap is now: {status}</color>");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

            return true;
        }
    }
}
