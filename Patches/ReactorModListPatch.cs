using HarmonyLib;
using Hazel;
using System;
using System.Reflection;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// Patches Reactor's CustomRpcManager.HandleRpc to swallow KeyNotFoundException
    /// caused by unknown mods (e.g. Submerged not installed on host) in a joining
    /// player's mod list. Without this, Reactor crashes and corrupts the message reader,
    /// preventing ModInfoPostfix from firing and leaving unverified players in the lobby.
    /// </summary>
    [HarmonyPatch]
    public static class ReactorModListPatch
    {
        static MethodBase? TargetMethod()
        {
            try
            {
                var type = AccessTools.TypeByName("Reactor.Networking.Rpc.CustomRpcManager");
                if (type == null)
                {
                    DraftModePlugin.Logger.LogWarning("[ReactorModListPatch] Could not find CustomRpcManager.");
                    return null;
                }
                var method = AccessTools.Method(type, "HandleRpc");
                if (method == null)
                {
                    DraftModePlugin.Logger.LogWarning("[ReactorModListPatch] Could not find HandleRpc.");
                    return null;
                }
                DraftModePlugin.Logger.LogInfo("[ReactorModListPatch] Patching CustomRpcManager.HandleRpc.");
                return method;
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[ReactorModListPatch] TargetMethod failed: {ex.Message}");
                return null;
            }
        }

        static Exception? Finalizer(Exception? __exception)
        {
            if (__exception is System.Collections.Generic.KeyNotFoundException knfe)
            {
                // Unknown mod ID in joining player's list (e.g. Submerged not on host).
                // Swallow the crash so the lobby stays stable — ModInfoPostfix will still
                // fire from any successful mod list reads that happened before the bad entry.
                DraftModePlugin.Logger.LogWarning(
                    $"[ReactorModListPatch] Swallowed unknown mod key crash: {knfe.Message}");
                return null;
            }

            return __exception;
        }
    }
}