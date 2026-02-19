using DraftModeTOUM.Managers;
using HarmonyLib;
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

            // ── /draft ────────────────────────────────────────────────────────
            if (msg.StartsWith("/draft", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                    DraftManager.SendSystemMessage("Only the host can start a draft.");
                else if (DraftManager.IsDraftActive)
                    DraftManager.SendSystemMessage("A draft is already in progress.");
                else
                    DraftManager.StartDraft();

                __instance.freeChatField.textArea.Clear();
                return false;
            }

            // ── /draftrecap ───────────────────────────────────────────────────
            if (msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendSystemMessage("Only the host can change draft settings.");
                }
                else
                {
                    DraftManager.ShowRecap = !DraftManager.ShowRecap;
                    string state = DraftManager.ShowRecap
                        ? "<color=#00FF00>ON</color>"
                        : "<color=#FF4444>OFF</color>";
                    DraftManager.SendSystemMessage($"Draft recap is now: {state}");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

            // ── /draftmod ─────────────────────────────────────────────────────
            if (msg.StartsWith("/draftmod", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendSystemMessage("Only the host can change draft settings.");
                }
                else
                {
                    Patches.RequireModPatch.RequireDraftMod = !Patches.RequireModPatch.RequireDraftMod;
                    string state = Patches.RequireModPatch.RequireDraftMod
                        ? "<color=#00FF00>ON</color>"
                        : "<color=#FF4444>OFF</color>";
                    DraftManager.SendSystemMessage($"Require DraftModeTOUM: {state}");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

            // ── Number picks (chat fallback while screen is open) ─────────────
            if (DraftManager.IsDraftActive
                && (msg == "1" || msg == "2" || msg == "3" || msg == "4"))
            {
                var currentPicker = DraftManager.GetCurrentPickerState();
                if (currentPicker != null
                    && currentPicker.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                {
                    int index = int.Parse(msg) - 1;
                    DraftNetworkHelper.SendPickToHost(index);
                    // No system message here — the screen already gives visual feedback
                }
                else
                {
                    DraftManager.SendSystemMessage("It is not your turn to pick.");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

            return true;
        }
    }
}
