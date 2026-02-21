using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using MiraAPI.PluginLoading;
using UnityEngine;

namespace DraftModeTOUM
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.reactor.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("mira.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("auavengers.tou.mira", BepInDependency.DependencyFlags.HardDependency)]
    public class DraftModePlugin : BasePlugin, IMiraPlugin
    {
        public static ManualLogSource Logger = null!;
        private Harmony _harmony = null!;

        public string OptionsTitleText => "Draft Mode";

        public ConfigFile GetConfigFile() => Config;

        public override void Load()
        {
            Logger = Log;
            Logger.LogInfo($"DraftModeTOUM v{PluginInfo.PLUGIN_VERSION} loading...");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<DraftTicker>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftScreenController>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftCircleMinigame>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftStatusOverlay>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftRecapOverlay>();
                Logger.LogInfo("Draft UI Components registered.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to register types: {ex}");
            }

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
            RequireModPatch.Apply(_harmony);

            Logger.LogInfo("DraftModeTOUM loaded successfully!");
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }
    }

    internal static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.draftmodetoun.mod";
        public const string PLUGIN_NAME = "DraftModeTOUM";
        public const string PLUGIN_VERSION = "1.0.4";
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class OnDisconnectPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RequireModPatch.ClearSession();
            DraftScreenController.Hide();
            DraftUiManager.CloseAll();
            DraftRecapOverlay.Hide();
            bool draftStillInProgress = DraftManager.IsDraftActive;
            DraftManager.Reset(cancelledBeforeCompletion: draftStillInProgress);
            DraftModePlugin.Logger.LogInfo($"[DraftModePlugin] Session cleared on disconnect.");
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    public static class BeginGameCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftScreenController.Hide();
            // Do NOT hide DraftStatusOverlay here to avoid lobby flash.
            DraftRecapOverlay.Hide();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] Game starting...");
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class IntroCutsceneHidePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Backup method: Hides overlay when "Shhh" screen appears
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    public static class ShipStatusStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Ultimate backup: Hides overlay when map loads
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
        }
    }
}