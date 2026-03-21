using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using MiraAPI.PluginLoading;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using UnityEngine;

namespace DraftModeTOUM
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.reactor.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("mira.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("auavengers.tou.mira", BepInDependency.DependencyFlags.HardDependency)]
    [ReactorModFlags(ModFlags.RequireOnAllClients)]
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
                ClassInjector.RegisterTypeInIl2Cpp<DraftDashboardReporter>();
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
            PatchLobbyCodeMethod(_harmony);

            // DraftDashboardReporter is started lazily from MainMenuManagerPatch
            // and re-ensured on lobby join — NOT here, as Unity scene isn't ready during Load().

            Logger.LogInfo("DraftModeTOUM loaded successfully!");
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }

        /// <summary>
        /// Scans all loaded assemblies for a static method that converts a lobby code
        /// string (e.g. "STICKY") to an int, then patches it to capture the code.
        /// This is necessary because GameCode class name differs across IL2CPP versions.
        /// </summary>
        public static void PatchLobbyCodeMethod(Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo target = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in asm.GetTypes())
                        {
                            try
                            {
                                foreach (var method in type.GetMethods(
                                    System.Reflection.BindingFlags.Static |
                                    System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.NonPublic))
                                {
                                    if ((method.Name.Contains("GameName") || method.Name.Contains("NameToInt") || method.Name.Contains("CodeToInt"))
                                        && method.GetParameters().Length == 1
                                        && method.GetParameters()[0].ParameterType == typeof(string))
                                    {
                                        Logger.LogInfo($"[LobbyCodePatch] Found candidate: {type.FullName}.{method.Name}");
                                        target = method;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                if (target != null)
                {
                    var prefix = typeof(DraftModePlugin).GetMethod(
                        nameof(LobbyCodeMethodPrefix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                    Logger.LogInfo($"[LobbyCodePatch] Patched {target.DeclaringType?.Name}.{target.Name}");
                }
                else
                {
                    Logger.LogWarning("[LobbyCodePatch] No GameNameToInt method found — lobby code capture unavailable.");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"[LobbyCodePatch] Scan error: {ex.Message}");
            }
        }

        public static void LobbyCodeMethodPrefix(string[] __args)
        {
            try
            {
                // Use __args to capture the first parameter by position rather than by name,
                // because different Among Us builds name the parameter differently
                // ("gameCode", "gameId", etc.) which causes HarmonyX to fail.
                string gameCode = (__args != null && __args.Length > 0) ? __args[0] : null;
                if (!string.IsNullOrWhiteSpace(gameCode))
                {
                    string code = gameCode.Trim().ToUpperInvariant();
                    DraftDashboardReporter.CacheLobbyCode(code);
                    Logger.LogInfo($"[LobbyCodePatch] Captured lobby code: {code}");
                }
            }
            catch { }
        }
    }

    internal static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.draftmodetoun.mod";
        public const string PLUGIN_NAME = "DraftModeTOUM";
        public const string PLUGIN_VERSION = "1.0.5";
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class OnDisconnectPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftScreenController.Hide();
            DraftUiManager.CloseAll();
            DraftRecapOverlay.Hide();
            bool draftStillInProgress = DraftManager.IsDraftActive;
            DraftManager.Reset(cancelledBeforeCompletion: draftStillInProgress);
            DraftDashboardReporter.ClearLobbyCode();
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

    /// <summary>
    /// Start the reporter as soon as the main menu is alive.
    /// This is the earliest point where Unity scene/GameObject creation is safe.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    public static class MainMenuManagerStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftDashboardReporter.EnsureExists();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] DashboardReporter ensured from MainMenu.");
        }
    }

    /// <summary>
    /// Re-ensure the reporter every time we successfully join a server,
    /// in case it was destroyed during a previous session.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    public static class OnGameJoinedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AmongUsClient __instance)
        {
            DraftDashboardReporter.EnsureExists();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] DashboardReporter ensured on game join.");

            // Fallback: directly read the game code from AmongUsClient so the lobby
            // code is always captured even if the GameCode.GameNameToIntV2 patch missed it.
            try
            {
                string gameId = __instance.GameId.ToString();
                if (!string.IsNullOrWhiteSpace(gameId) && gameId != "0")
                {
                    // GameId is an int on official servers but modded servers (Impostor)
                    // also supply a word code via the join URL — try to reconstruct it
                    // from the raw int using GameCode.IntToGameName if it exists,
                    // otherwise just use the int as a string so the dashboard shows *something*.
                    string code = gameId;
                    try
                    {
                        // Use standard .NET reflection on the IL2CPP-proxied type
                        var gcType = typeof(InnerNet.GameCode);
                        foreach (var m in gcType.GetMethods(
                            System.Reflection.BindingFlags.Static |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic))
                        {
                            if ((m.Name.Contains("IntToGame") || m.Name.Contains("IntToName") || m.Name.Contains("IntToCode"))
                                && m.GetParameters().Length == 1)
                            {
                                var result = m.Invoke(null, new object[] { __instance.GameId });
                                string s = result?.ToString();
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    code = s;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    DraftDashboardReporter.CacheLobbyCode(code);
                    DraftModePlugin.Logger.LogInfo($"[DraftModePlugin] Fallback lobby code from GameId: {code}");
                }
            }
            catch { }
        }
    }
}
