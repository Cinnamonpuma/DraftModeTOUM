using AmongUs.GameOptions;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte PlayerId { get; set; }
        public int SlotNumber { get; set; }
        public string? ChosenRole { get; set; }
        public bool HasPicked { get; set; }
        public bool IsPickingNow { get; set; }
        public List<string> OfferedRoles { get; set; } = new List<string>();
    }

    public static class DraftManager
    {
        public static bool IsDraftActive { get; private set; }
        public static int CurrentTurn { get; private set; }
        public static float TurnTimeLeft { get; private set; }
        public static float TurnDuration { get; set; } = 10f;

        // ── Recap toggle ─────────────────────────────────────────────────────
        public static bool ShowRecap { get; set; } = true;
        public static bool AutoStartAfterDraft { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances { get; set; } = true;
        public static int  OfferedRolesCount { get; set; } = 3;
        public static bool ShowRandomOption  { get; set; } = true;

        // ── Faction caps — change these to adjust limits ─────────────────────
        public static int MaxImpostors       { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        private static int _impostorsDrafted      = 0;
        private static int _neutralKillingsDrafted = 0;
        private static int _neutralPassivesDrafted = 0;


        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int> _pidToSlot = new Dictionary<byte, int>();
        private static List<string> _lobbyRolePool = new List<string>();
        private static Dictionary<string, int> _roleMaxCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _roleWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleFaction> _roleFactions = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _draftedRoleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static int GetSlotForPlayer(byte playerId) => _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState? GetStateForSlot(int slot) => _slotMap.TryGetValue(slot, out var s) ? s : null;

        public static PlayerDraftState? GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn = turnNumber;
            TurnTimeLeft = TurnDuration;
            // Update IsPickingNow flags so the turn list renders correctly on clients
            foreach (var state in _slotMap.Values)
            {
                state.IsPickingNow = (state.SlotNumber == currentPickerSlot);
                if (state.SlotNumber < currentPickerSlot)
                    state.HasPicked = true;
            }
        }

        public static void SetDraftStateFromHost(int totalSlots, List<byte> playerIds, List<int> slotNumbers)
        {
            _slotMap.Clear();
            _pidToSlot.Clear();
            TurnOrder.Clear();

            IsDraftActive = true;
            for (int i = 0; i < playerIds.Count; i++)
            {
                var state = new PlayerDraftState { PlayerId = playerIds[i], SlotNumber = slotNumbers[i] };
                _slotMap[slotNumbers[i]] = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }
            TurnOrder = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn = 1;
            TurnTimeLeft = TurnDuration;

            // Show the center-screen overlay on all clients
            DraftStatusOverlay.Show();
        }

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;
            DraftTicker.EnsureExists();
            Reset(cancelledBeforeCompletion: true); // clear any previous draft + old UpCommandRequests
            ApplyLocalSettings();

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();
            var pool = RolePoolBuilder.BuildPool();
            _lobbyRolePool = pool.Roles;
            _roleMaxCounts = pool.MaxCounts;
            _roleWeights = pool.Weights;
            _roleFactions = pool.Factions;
            if (_lobbyRolePool.Count == 0) return;

            int totalSlots = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            List<byte> syncPids = new List<byte>();
            List<int> syncSlots = new List<int>();

            // Broadcast a start announcement with the pick order
            var orderMsg = new StringBuilder();
            orderMsg.AppendLine("<color=#FFD700><b>\U0001F3B2 DRAFT STARTING!</b></color>");
            orderMsg.AppendLine($"<color=#AAAAAA>{totalSlots} players — each gets a random pick number.</color>");
            SendChatLocal(orderMsg.ToString());

            for (int i = 0; i < totalSlots; i++)
            {
                int slot = shuffledSlots[i];
                byte pid = players[i].PlayerId;
                _slotMap[slot] = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn = 1;
            TurnTimeLeft = TurnDuration;
            IsDraftActive = true;

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);

            // Tell each real player their slot number privately
            NotifyPlayersOfSlots();

            // Show the center-screen status overlay for the host
            DraftStatusOverlay.Show();

            OfferRolesToCurrentPicker();
        }

        private static void NotifyPlayersOfSlots()
        {
            // Host's panel is refreshed via RefreshTurnList (no chat message needed).
            // Non-hosts receive the slot map so their panel can display their number too.
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);
        }

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive = false;
            CurrentTurn = 0;
            TurnTimeLeft = 0f;
            DraftUiManager.CloseAll();
            DraftStatusOverlay.Hide();
            // Only hide recap on manual cancel — after a completed draft it auto-hides when the game loads
            if (cancelledBeforeCompletion) DraftRecapOverlay.Hide();
            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoleCounts.Clear();
            _roleMaxCounts.Clear();
            _roleWeights.Clear();
            _roleFactions.Clear();
            TurnOrder.Clear();

            _impostorsDrafted       = 0;
            _neutralKillingsDrafted  = 0;
            _neutralPassivesDrafted  = 0;
            // Only wipe UpCommandRequests if we're cancelling before roles were applied.
            // If ApplyAllRoles() already ran, leave the requests intact so TOU-Mira's
            // SelectRoles patch can read them when the game actually starts.
            if (cancelledBeforeCompletion)
            {
                UpCommandRequests.Clear();
            }
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        // Returns roles that are within counts and faction caps
        private static List<string> GetAvailableRoles()
        {
            return _lobbyRolePool.Where(r =>
            {
                if (GetDraftedCount(r) >= GetMaxCount(r)) return false;
                var faction = GetFaction(r);
                if (faction == RoleFaction.Impostor       && _impostorsDrafted      >= MaxImpostors)       return false;
                if (faction == RoleFaction.NeutralKilling && _neutralKillingsDrafted >= MaxNeutralKillings) return false;
                if (faction == RoleFaction.Neutral        && _neutralPassivesDrafted  >= MaxNeutralPassives) return false;
                return true;
            }).ToList();
        }

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;

            var available = GetAvailableRoles();

            int target = OfferedRolesCount;

            if (available.Count == 0)
            {
                state.OfferedRoles = Enumerable.Repeat("Crewmate", target).ToList();
            }
            else
            {
                var offered = new List<string>();

                // Guarantee 1 impostor + 1 neutral when target >= 3
                if (target >= 3)
                {
                    var impPool = available.Where(r => GetFaction(r) == RoleFaction.Impostor).ToList();
                    offered.AddRange(PickWeightedUnique(impPool, 1));

                    var nkPool = available.Where(r => GetFaction(r) == RoleFaction.NeutralKilling).ToList();
                    var npPool = available.Where(r => GetFaction(r) == RoleFaction.Neutral).ToList();
                    var neutralPool = nkPool.Count > 0 ? nkPool : npPool;
                    offered.AddRange(PickWeightedUnique(
                        neutralPool.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList(), 1));
                }

                // Fill remaining with crew, then anything else
                var crewPool = available.Where(r => GetFaction(r) == RoleFaction.Crewmate
                    && !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
                offered.AddRange(PickWeightedUnique(crewPool, target - offered.Count));

                if (offered.Count < target)
                {
                    var extras = PickWeightedUnique(
                        available.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList(),
                        target - offered.Count);
                    offered.AddRange(extras);
                }

                state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
            }

            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            // Last index (= OfferedRoles.Count) is the Random card
            string chosenRole = (choiceIndex >= state.OfferedRoles.Count)
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosenRole);
            return true;
        }

        private static void AutoPickRandom()
        {
            var state = GetCurrentPickerState();
            // If Random option is off, pick from what was offered — not the whole pool
            if (!ShowRandomOption && state != null && state.OfferedRoles.Count > 0)
            {
                var pick = state.OfferedRoles[UnityEngine.Random.Range(0, state.OfferedRoles.Count)];
                FinalisePickForCurrentSlot(pick);
            }
            else
            {
                FinalisePickForCurrentSlot(PickFullRandom());
            }
        }

        private static string PickFullRandom()
        {
            var available = GetAvailableRoles();
            if (available.Count == 0) return "Crewmate";
            return UseRoleChances ? PickWeighted(available) : available[UnityEngine.Random.Range(0, available.Count)];
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole = roleName;
            state.HasPicked = true;
            state.IsPickingNow = false;
            _draftedRoleCounts[roleName] = GetDraftedCount(roleName) + 1;

            // Update faction counters
            var faction = GetFaction(roleName);
            if (faction == RoleFaction.Impostor)       _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral)        _neutralPassivesDrafted++;

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] '{roleName}' drafted ({faction}). " +
                $"Impostors: {_impostorsDrafted}/{MaxImpostors}, " +
                $"NK: {_neutralKillingsDrafted}/{MaxNeutralKillings}, " +
                $"NP: {_neutralPassivesDrafted}/{MaxNeutralPassives}");

            CurrentTurn++;

            // Refresh the turn list for anyone who has the minigame open
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();   // populates UpCommandRequests — must happen BEFORE Reset
                DraftUiManager.CloseAll();

                if (ShowRecap)
                    DraftNetworkHelper.BroadcastRecap(BuildRecapMessage());
                else
                    DraftNetworkHelper.BroadcastRecap("<color=#FFD700><b>\u2500\u2500 DRAFT COMPLETE \u2500\u2500</b></color>");

                // Reset draft state but do NOT clear UpCommandRequests
                // (TOU-Mira's SelectRoles will read them when the game starts)
                Reset(cancelledBeforeCompletion: false);

                TryStartGameAfterDraft();
            }
            else
            {
                TurnTimeLeft = TurnDuration;
                OfferRolesToCurrentPicker();
            }
        }

        private static string BuildRecapMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<color=#FFD700><b>── DRAFT RECAP ──</b></color>");
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                string role = s.ChosenRole ?? "?";
                string color = GetFaction(role) switch
                {
                    RoleFaction.Impostor       => "#FF4444",
                    RoleFaction.NeutralKilling => "#FF8800",
                    RoleFaction.Neutral        => "#AA44FF",
                    _                          => "#4BD7E4"
                };
                sb.AppendLine($"Player {s.SlotNumber}: <color={color}>{role}</color>");
            }
            return sb.ToString();
        }

        public static List<RecapEntry> BuildRecapEntries()
        {
            var entries = new List<RecapEntry>();
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                string role = s.ChosenRole ?? "?";

                // Resolve player name
                var player = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(p => p.PlayerId == s.PlayerId);
                string playerName = player?.Data?.PlayerName ?? $"Player {s.SlotNumber}";

                UnityEngine.Color roleColor = GetFaction(role) switch
                {
                    RoleFaction.Impostor       => new UnityEngine.Color(1f,   0.27f, 0.27f),
                    RoleFaction.NeutralKilling => new UnityEngine.Color(1f,   0.53f, 0f),
                    RoleFaction.Neutral        => new UnityEngine.Color(0.67f,0.27f, 1f),
                    _                          => new UnityEngine.Color(0.29f,0.84f, 0.9f)
                };

                entries.Add(new RecapEntry(playerName, role, roleColor));
            }
            return entries;
        }

        private static void ApplyAllRoles()
        {
            // Register every drafted role via UpCommandRequests so TOU-Mira's
            // SelectRoles patch honours them when the game actually starts.
            // UpCommandRequests.TryGetRequest matches by GetRoleName() or ITownOfUsRole.LocaleKey,
            // so we store the exact display name returned by GetRoleName().
            // MiscUtils.AllRegisteredRoles is the same source UpCommandRequests uses for matching,
            // so resolving against it gives us the canonical name.
            var allRoles = MiscUtils.AllRegisteredRoles.ToArray();

            foreach (var state in _slotMap.Values)
            {
                if (state.PlayerId >= 200 || state.ChosenRole == null) continue;
                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == state.PlayerId);
                if (p == null) continue;

                // Verify the role name is actually resolvable before registering.
                // Use NiceName (the base-game property on RoleBehaviour) for matching;
                // GetRoleName() is a MiraAPI extension that does the same thing.
                string roleName = state.ChosenRole;
                var match = allRoles.FirstOrDefault(r =>
                    r.NiceName.Equals(roleName, StringComparison.OrdinalIgnoreCase) ||
                    r.NiceName.Replace(" ", "").Equals(roleName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    roleName = match.NiceName; // canonical spelling
                else
                    DraftModePlugin.Logger.LogWarning($"[DraftManager] Role '{roleName}' not found in AllRegisteredRoles — request may not resolve.");

                DraftModePlugin.Logger.LogInfo(
                    $"[DraftManager] Queuing role '{roleName}' for player '{p.Data.PlayerName}' (id={p.PlayerId})");
                UpCommandRequests.SetRequest(p.Data.PlayerName, roleName);
            }

            // Sanity check log
            var all = UpCommandRequests.GetAllRequests();
            DraftModePlugin.Logger.LogInfo($"[DraftManager] ApplyAllRoles complete — {all.Count} requests queued for SelectRoles:");
            foreach (var kvp in all)
                DraftModePlugin.Logger.LogInfo($"  '{kvp.Key}' => '{kvp.Value}'");
        }

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;

            TurnDuration      = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap         = opts.ShowRecap;
            AutoStartAfterDraft   = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances     = opts.UseRoleChances;
            OfferedRolesCount  = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption   = opts.ShowRandomOption;
            MaxImpostors       = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors),       0, 10);
            MaxNeutralKillings = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings),  0, 10);
            MaxNeutralPassives = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives),  0, 10);
        }

        private static int GetDraftedCount(string roleName)
        {
            return _draftedRoleCounts.TryGetValue(roleName, out var count) ? count : 0;
        }

        private static int GetMaxCount(string roleName)
        {
            return _roleMaxCounts.TryGetValue(roleName, out var count) ? count : 1;
        }

        private static RoleFaction GetFaction(string roleName)
        {
            return _roleFactions.TryGetValue(roleName, out var faction)
                ? faction
                : RoleCategory.GetFaction(roleName);
        }

        private static int GetWeight(string roleName)
        {
            return _roleWeights.TryGetValue(roleName, out var weight) ? Math.Max(1, weight) : 1;
        }

        private static string PickWeighted(List<string> candidates)
        {
            int total = candidates.Sum(GetWeight);
            if (total <= 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];

            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc = 0;
            foreach (var r in candidates)
            {
                acc += GetWeight(r);
                if (roll <= acc) return r;
            }
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static List<string> PickWeightedUnique(List<string> candidates, int count)
        {
            var results = new List<string>();
            var temp = new List<string>(candidates);

            while (results.Count < count && temp.Count > 0)
            {
                var pick = UseRoleChances ? PickWeighted(temp) : temp[UnityEngine.Random.Range(0, temp.Count)];
                results.Add(pick);
                temp.Remove(pick);
            }

            return results;
        }

        private static void TryStartGameAfterDraft()
        {
            if (!AutoStartAfterDraft) return;
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;

            // Small delay so UpCommandRequests are registered before SelectRoles fires
            Coroutines.Start(CoStartGame());
        }

        private static IEnumerator CoStartGame()
        {
            // Wait long enough for the recap overlay to be visible before the scene changes
            yield return new WaitForSeconds(ShowRecap ? 4.0f : 0.5f);
            if (GameStartManager.Instance != null)
            {
                DraftModePlugin.Logger.LogInfo("[DraftManager] Auto-starting game after draft.");
                GameStartManager.Instance.BeginGame();
            }
        }
    }
}
