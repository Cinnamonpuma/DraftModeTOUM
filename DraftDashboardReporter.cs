using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DraftModeTOUM.Managers;
using UnityEngine;

namespace DraftModeTOUM
{
    /// <summary>
    /// Sends heartbeats to the DraftMode PHP dashboard every 3s.
    /// User ID is generated once and stored in BepInEx/config/DraftModeTOUM.userid
    /// so it's stable across sessions without touching any game API.
    /// </summary>
    public class DraftDashboardReporter : MonoBehaviour
    {
        private const string HeartbeatUrl      = "https://mckelanor.xyz/au/draft/admin/api/heartbeat.php";
        private const string ConsumeForcedRoleUrl = "https://mckelanor.xyz/au/draft/admin/api/consume-forced-role.php";
        private const float  HeartbeatInterval = 3f;

        private static DraftDashboardReporter _instance;
        private static readonly HttpClient    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private static string _userId = null;

        private float  _nextHeartbeat     = 0f;
        private static string _pendingForcedRole = null;
        private static string _cachedLobbyCode   = "";

        // FIX: CancellationTokenSource per session so in-flight HTTP tasks are
        // cancelled immediately on disconnect, preventing them from touching
        // game state (e.g. _pendingForcedRole) after it has been cleared.
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        // ── Singleton ────────────────────────────────────────────────────────────

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftDashboardReporter");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftDashboardReporter>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ── Unity loop ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (_pendingForcedRole != null)
            {
                string role = _pendingForcedRole;
                _pendingForcedRole = null;
                ApplyForcedRole(role);
            }

            if (!CanTick()) return;

            if (Time.time >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.time + HeartbeatInterval;
                TrySendHeartbeat();
            }
        }

        // ── Heartbeat ─────────────────────────────────────────────────────────────

        private static void TrySendHeartbeat()
        {
            try
            {
                var me = PlayerControl.LocalPlayer;
                if (me == null || me.Data == null) return;

                string userId    = GetOrCreateUserId();
                string name      = me.Data.PlayerName ?? me.name;
                string lobbyCode = GetLobbyCode();
                bool   isHost    = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

                // FIX: Pass the current token so tasks cancel on disconnect
                var token = _cts.Token;
                Task.Run(async () => await PostHeartbeat(userId, name, lobbyCode, isHost, token), token);
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Send setup failed: {ex.Message}");
            }
        }

        private static async Task PostHeartbeat(string userId, string name, string lobbyCode, bool isHost, CancellationToken ct)
        {
            try
            {
                string json =
                    "{\"player\":{" +
                    "\"userId\":\""    + Esc(userId)    + "\"," +
                    "\"name\":\""      + Esc(name)      + "\"," +
                    "\"lobbyCode\":\"" + Esc(lobbyCode) + "\"," +
                    "\"gameState\":\"lobby\"," +
                    "\"isHost\":"      + (isHost ? "true" : "false") +
                    "}}";

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // FIX: Use CancellationToken so this request aborts on disconnect
                using var req = new HttpRequestMessage(HttpMethod.Post, HeartbeatUrl) { Content = content };
                var resp    = await _http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync();

                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Heartbeat {resp.StatusCode}");

                // FIX: Only parse response if not cancelled
                if (!ct.IsCancellationRequested)
                    ParseResponse(body);
            }
            catch (OperationCanceledException)
            {
                // Normal on disconnect — don't log as error
                LoggingSystem.Debug($"[DashboardReporter] Heartbeat {resp.StatusCode}");
                ParseResponse(body);
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Heartbeat failed: {ex.Message}");
            }
        }

        // ── Response ──────────────────────────────────────────────────────────────

        private static void ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("forcedRole", out var fr) &&
                    fr.ValueKind == JsonValueKind.String)
                {
                    string role = fr.GetString();
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        LoggingSystem.Debug($"[DashboardReporter] Forced role queued: {role}");
                        _pendingForcedRole = role;
                    }
                }
            }
            catch { }
        }

        // ── Forced role ───────────────────────────────────────────────────────────

        private static void ApplyForcedRole(string roleName)
        {
            try
            {
                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Relaying forced role '{roleName}' to host");
                LoggingSystem.Debug($"[DashboardReporter] Relaying forced role '{roleName}' to host...");
                // Always go through the RPC helper:
                // • If this client IS the host → sets it directly on DraftManager
                // • If this client is NOT the host → sends ForceRole RPC to host
                DraftModeTOUM.Patches.DraftNetworkHelper.SendForceRoleToHost(roleName);
                
                // DO NOT consume here - the role stays in queue until the draft actually uses it
                // It will be consumed by DraftManager when the draft offer is generated
            }
            catch (Exception ex)
            {
                LoggingSystem.Error($"[DashboardReporter] ApplyForcedRole failed: {ex.Message}");
            }
        }

        private static async Task ConsumeForcedRole(string userId)
        {
            try
            {
                string json = "{\"userId\":\"" + Esc(userId) + "\"}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync(ConsumeForcedRoleUrl, content);
                LoggingSystem.Debug($"[DashboardReporter] Forced role consumed from queue");
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Failed to consume forced role: {ex.Message}");
            }
        }

        // ── User ID (file-based) ──────────────────────────────────────────────────

        private static string GetOrCreateUserId()
        {
            if (_userId != null) return _userId;

            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "DraftModeTOUM.userid");

                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (existing.StartsWith("DRAFT-") && existing.Length > 6)
                    {
                        _userId = existing;
                        LoggingSystem.Debug($"[DashboardReporter] Loaded user ID: {_userId}");
                        return _userId;
                    }
                }

                string newId = "DRAFT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                File.WriteAllText(path, newId);
                _userId = newId;
                LoggingSystem.Debug($"[DashboardReporter] Created new user ID: {_userId}");
            }
            catch (Exception ex)
            {
                _userId = "DRAFT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                LoggingSystem.Warning($"[DashboardReporter] Could not read/write userid file: {ex.Message}. Using session ID: {_userId}");
            }

            return _userId;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool CanTick()
        {
            try
            {
                if (AmongUsClient.Instance == null) return false;
                var state = AmongUsClient.Instance.GameState;
                return state == InnerNet.InnerNetClient.GameStates.Joined ||
                       state == InnerNet.InnerNetClient.GameStates.Started;
            }
            catch { return false; }
        }

        public static void CacheLobbyCode(string code)
        {
            _cachedLobbyCode = string.IsNullOrWhiteSpace(code) ? "" : code.Trim().ToUpperInvariant();
            LoggingSystem.Debug($"[DashboardReporter] Cached lobby code: {_cachedLobbyCode}");
        }

        public static void ClearLobbyCode()
        {
            _cachedLobbyCode = "";

            // FIX: Cancel all in-flight heartbeat tasks and create a fresh token
            // so no stale callbacks fire after the session ends.
            try
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            catch { }
            _cts = new CancellationTokenSource();

            // FIX: Also clear any pending forced role from the old session
            _pendingForcedRole = null;
        }

        private static string GetLobbyCode() => _cachedLobbyCode;

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
