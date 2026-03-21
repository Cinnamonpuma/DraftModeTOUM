using DraftModeTOUM.Managers;
using HarmonyLib;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// Lets BeginGame() run normally (so all its setup executes), then immediately
    /// zeroes countDownTimer so Update() fires FinallyBegin() on the very next frame
    /// instead of waiting 5 seconds.
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    public static class GameStartManagerCountdownPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameStartManager __instance)
        {
            if (!DraftManager.SkipCountdown) return;

            DraftModePlugin.Logger.LogInfo("[CountdownPatch] Zeroing countDownTimer after BeginGame.");
            __instance.countDownTimer = 0f;
        }
    }
}