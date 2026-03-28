using HarmonyLib;
using Hazel;
using System;
using System.Reflection;

namespace DraftModeTOUM.Patches
{
    
    
    
    
    
    
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
                
                
                
                DraftModePlugin.Logger.LogWarning(
                    $"[ReactorModListPatch] Swallowed unknown mod key crash: {knfe.Message}");
                return null;
            }

            return __exception;
        }
    }
}
