using BepInEx.Logging;

namespace DraftModeTOUM
{
    /// <summary>
    /// Centralized logging system for DraftMode. 
    /// Set ENABLE_DEBUG = true below to show debug messages.
    /// Set ENABLE_DEBUG = false to completely disable debug logging in the build.
    /// </summary>
    public static class LoggingSystem
    {
        // ╔════════════════════════════════════════════════════════════╗
        // ║  CHANGE THIS TO CONTROL DEBUG LOGGING AT COMPILE TIME      ║
        // ║  true  = All debug messages are compiled and shown         ║
        // ║  false = All debug messages are removed from the build     ║
        // ╚════════════════════════════════════════════════════════════╝
        private const bool ENABLE_DEBUG = false;

        private static ManualLogSource _logger;

        public static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Log debug messages (compiled out if ENABLE_DEBUG is false)
        /// </summary>
        public static void Debug(string message)
        {
#if ENABLE_DEBUG
            if (_logger == null) return;
            _logger.LogInfo($"[DEBUG] {message}");
#endif
        }

        /// <summary>
        /// Log info messages (always logged)
        /// </summary>
        public static void Info(string message)
        {
            if (_logger == null) return;
            _logger.LogInfo($"[INFO] {message}");
        }

        /// <summary>
        /// Log warning messages (always logged)
        /// </summary>
        public static void Warning(string message)
        {
            if (_logger == null) return;
            _logger.LogWarning($"[WARN] {message}");
        }

        /// <summary>
        /// Log error messages (always logged)
        /// </summary>
        public static void Error(string message)
        {
            if (_logger == null) return;
            _logger.LogError($"[ERROR] {message}");
        }

        public static bool IsDebugEnabled => ENABLE_DEBUG;
    }
}
