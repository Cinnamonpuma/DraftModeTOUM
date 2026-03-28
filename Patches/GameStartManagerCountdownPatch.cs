using DraftModeTOUM.Managers;
using HarmonyLib;
using MiraAPI.GameOptions;
using Reactor.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Patches
{
    
    
    
    
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

    
    
    
    
    
    
    
    [HarmonyPatch(typeof(GameStartManager))]
    public static class BeginGameDraftInterceptPatch
    {
        private static readonly string[] StartMethodNames =
        {
            "BeginGame",
            "StartGame",
            "StartCountdown",
            "StartCountDown",
            "HandleStart",
            "StartButton"
        };

        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(GameStartManager)))
            {
                if (m == null) continue;
                if (m.ReturnType != typeof(void)) continue;
                if (!StartMethodNames.Contains(m.Name)) continue;
                yield return m;
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(GameStartManager __instance)
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            if (DraftManager.SkipCountdown) return true;
            if (DraftManager.IsDraftActive) return true;
            if (!OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft) return true;

            DraftModePlugin.Logger.LogInfo("[DraftIntercept] BeginGame intercepted — starting draft.");

            
            __instance.countDownTimer = 10f;

            Coroutines.Start(CoStartDraft(__instance));
            return false;
        }

        private static IEnumerator CoStartDraft(GameStartManager gsm)
        {
            
            yield return null;

            
            
            gsm.countDownTimer = 10f;

            DraftManager.SendChatLocal("<color=#FFD700>Draft starting! Wait for your turn to pick a role.</color>");
            DraftManager.StartDraft();
        }
    }
}

