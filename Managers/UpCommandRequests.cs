using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    
    
    
    
    
    
    public static class UpCommandRequests
    {
        
        private static readonly Dictionary<string, string> _pending = new();

        public static void SetRequest(string playerName, string roleName)
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(roleName))
                return;

            _pending[playerName] = roleName;
            DraftModePlugin.Logger.LogInfo(
                $"[UpCommandRequests] Queued fallback role '{roleName}' for '{playerName}'");
        }

        
        public static IEnumerable<KeyValuePair<string, string>> DrainAll()
        {
            var copy = new List<KeyValuePair<string, string>>(_pending);
            _pending.Clear();
            return copy;
        }

        public static void Clear() => _pending.Clear();
    }
}

