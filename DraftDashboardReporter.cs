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
    
    
    
    
    
    public class DraftDashboardReporter : MonoBehaviour
    {
        private const string HeartbeatUrl      = "https://mckelanor.xyz/au/draft/admin/api/heartbeat.php";
        private const string ConsumeForcedRoleUrl = "https://mckelanor.xyz/au/draft/admin/api/consume-forced-role.php";
        private const float  HeartbeatInterval = 3f;

        private static DraftDashboardReporter _instance;
        private static readonly HttpClient    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private static string _userId = null;
        private static bool _warnedNoUserId = false;
        private static string _anonId = null;

        private float  _nextHeartbeat     = 0f;
        private static string _pendingForcedRole = null;
        private static string _cachedLobbyCode   = "";

        
        
        
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        

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

        

        private static void TrySendHeartbeat()
        {
            try
            {
                var me = PlayerControl.LocalPlayer;
                if (me == null || me.Data == null) return;

                string name      = me.Data.PlayerName ?? me.name;
                string lobbyCode = GetLobbyCode();
                bool   isHost    = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;
                string userId    = GetUserId();
                bool   hasId     = !string.IsNullOrWhiteSpace(userId);

                if (!hasId)
                {
                    if (!_warnedNoUserId)
                    {
                        _warnedNoUserId = true;
                        LoggingSystem.Warning("[DashboardReporter] DraftModeTOUM.userid missing/empty. Admin actions disabled until created.");
                    }
                    userId = BuildAnonId();
                }

                
                var token = _cts.Token;
                Task.Run(async () => await PostHeartbeat(userId, name, lobbyCode, isHost, hasId, token), token);
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Send setup failed: {ex.Message}");
            }
        }

        private static async Task PostHeartbeat(string userId, string name, string lobbyCode, bool isHost, bool hasId, CancellationToken ct)
        {
            try
            {
                string json =
                    "{\"player\":{" +
                    "\"userId\":\""    + Esc(userId)    + "\"," +
                    "\"name\":\""      + Esc(name)      + "\"," +
                    "\"lobbyCode\":\"" + Esc(lobbyCode) + "\"," +
                    "\"gameState\":\"lobby\"," +
                    "\"isHost\":"      + (isHost ? "true" : "false") + "," +
                    "\"hasId\":"       + (hasId ? "true" : "false") +
                    "}}";

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                
                using var req = new HttpRequestMessage(HttpMethod.Post, HeartbeatUrl) { Content = content };
                var resp    = await _http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync();

                

                
                if (!ct.IsCancellationRequested)
                    ParseResponse(body);
            }
            catch (OperationCanceledException)
            {
                
                
            }
            catch (Exception ex)
            {
                
            }
        }

        

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

        

        private static void ApplyForcedRole(string roleName)
        {
            try
            {
                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Relaying forced role '{roleName}' to host");
                LoggingSystem.Debug($"[DashboardReporter] Relaying forced role '{roleName}' to host...");
                
                
                
                DraftModeTOUM.Patches.DraftNetworkHelper.SendForceRoleToHost(roleName, PlayerControl.LocalPlayer.PlayerId);
                
                
                
            }
            catch (Exception ex)
            {
                LoggingSystem.Error($"[DashboardReporter] ApplyForcedRole failed: {ex.Message}");
            }
        }

        public static void TryConsumeForcedRole()
        {
            try
            {
                string userId = GetUserId();
                if (string.IsNullOrWhiteSpace(userId)) return;
                Task.Run(async () => await ConsumeForcedRole(userId));
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Failed to queue consume: {ex.Message}");
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

        

        private static string GetUserId()
        {
            if (_userId != null) return _userId;

            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "DraftModeTOUM.userid");

                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        _userId = existing;
                        LoggingSystem.Debug($"[DashboardReporter] Loaded user ID: {_userId}");
                        return _userId;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingSystem.Warning($"[DashboardReporter] Could not read userid file: {ex.Message}");
            }

            return null;
        }

        

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

            
            
            try
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            catch { }
            _cts = new CancellationTokenSource();

            
            _pendingForcedRole = null;
        }

        private static string BuildAnonId()
        {
            if (string.IsNullOrWhiteSpace(_anonId))
            {
                _anonId = Guid.NewGuid().ToString("N").ToUpperInvariant().Substring(0, 10);
            }
            return $"ANON-{_anonId}";
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

