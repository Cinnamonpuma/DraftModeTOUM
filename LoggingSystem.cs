using BepInEx.Logging;

namespace DraftModeTOUM
{

    public static class LoggingSystem
    {
        
        
        
        
        
        private const bool ENABLE_DEBUG = false;

        private static ManualLogSource _logger;

        public static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
        }


        public static void Debug(string message)
        {
#if ENABLE_DEBUG
            if (_logger == null) return;
            _logger.LogInfo($"[DEBUG] {message}");
#endif
        }

        
        
        
        public static void Info(string message)
        {
            if (_logger == null) return;
            _logger.LogInfo($"[INFO] {message}");
        }

        
        
        
        public static void Warning(string message)
        {
            if (_logger == null) return;
            _logger.LogWarning($"[WARN] {message}");
        }

        
        
        
        public static void Error(string message)
        {
            if (_logger == null) return;
            _logger.LogError($"[ERROR] {message}");
        }

        public static bool IsDebugEnabled => ENABLE_DEBUG;
    }
}

