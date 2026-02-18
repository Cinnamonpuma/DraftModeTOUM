using DraftModeTOUM.Managers;
using HarmonyLib;
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

            if (msg.StartsWith("/draft", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendChatLocal("<color=red>Only host can start draft.</color>");
                }
                else if (DraftManager.IsDraftActive)
                {
                    DraftManager.SendChatLocal("<color=red>Draft already active.</color>");
                }
                else
                {
                    DraftManager.StartDraft();
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }


            if (DraftManager.IsDraftActive)
            {
                if (msg == "1" || msg == "2" || msg == "3" || msg == "4")
                {
                    var currentPicker = DraftManager.GetCurrentPickerState();
                    if (currentPicker != null && currentPicker.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                    {
                        int index = int.Parse(msg) - 1;
                        DraftNetworkHelper.SendPickToHost(index);
                        DraftManager.SendChatLocal($"<color=green>You selected Option {msg}!</color>");
                    }
                    else
                    {
                        DraftManager.SendChatLocal("<color=red>It is not your turn to pick!</color>");
                    }
                    __instance.freeChatField.textArea.Clear();
                    return false;
                }
            }
            return true;
        }
    }
}