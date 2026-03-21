using AmongUs.GameOptions;
using DraftModeTOUM;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte         PlayerId       { get; set; }
        public int          SlotNumber     { get; set; }
        public ushort?      ChosenRoleId   { get; set; }
        public bool         HasPicked      { get; set; }
        public bool         IsPickingNow   { get; set; }
        public bool         IsDisconnected { get; set; }
        public List<ushort> OfferedRoleIds { get; set; } = new();

        /// <summary>
        /// The "guaranteed" faction slot pre-assigned to this player at draft start.
        /// Ensures at least one offered card is from their bucket.
        /// Null = pure crewmate slot (no special guarantee).
        /// </summary>
        public RoleFaction? GuaranteedFaction { get; set; }
    }

    public static class DraftManager
    {
        public static bool  IsDraftActive   { get; private set; }
        public static int   CurrentTurn     { get; private set; }
        public static float TurnTimeLeft    { get; private set; }
        public static float TurnDuration    { get; set; } = 10f;

        public static bool ShowRecap             { get; set; } = true;
        public static bool AutoStartAfterDraft   { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances        { get; set; } = true;
        public static int  OfferedRolesCount     { get; set; } = 3;
        public static bool ShowRandomOption      { get; set; } = true;

        public static int MaxImpostors       { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        private static int _impostorsDrafted       = 0;
        private static int _neutralKillingsDrafted = 0;
        private static int _neutralPassivesDrafted = 0;

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int>                           TurnOrder      { get; private set; } = new();
        private static Dictionary<int, PlayerDraftState> _slotMap       = new();
        private static Dictionary<byte, int>             _pidToSlot     = new();
        private static DraftRolePool                     _pool          = new();
        private static Dictionary<ushort, int>           _draftedCounts = new();

        public static readonly Dictionary<byte, RoleTypes> PendingRoleAssignments = new();
        private static readonly HashSet<byte>              _appliedPlayers        = new();

        // ---- Forced draft card --------------------------------------------------
        private static string  _forcedRoleName     = null;
        private static ushort? _forcedRoleId       = null;
        private static byte    _forcedRoleTargetId  = 255;

        // ---- Coroutine tracking -------------------------------------------------
        // Track running end-sequence coroutine so we can cancel it on reset
        private static bool _endSequenceRunning = false;

        public static void SetForcedDraftRole(string roleName, byte targetPlayerId)
        {
            _forcedRoleName     = roleName;
            _forcedRoleId       = null;
            _forcedRoleTargetId = targetPlayerId;
            LoggingSystem.Debug($"[DraftManager] Forced draft card set: '{roleName}' for player {targetPlayerId}");
            
            // If draft is already active, resolve the role ID immediately
            if (IsDraftActive)
            {
                ResolveForcedRoleId();
            }
        }

        // ---- Public accessors ---------------------------------------------------

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;
        public static PlayerDraftState GetStateForPlayer(byte playerId)
        {
            int slot = GetSlotForPlayer(playerId);
            return slot >= 0 ? GetStateForSlot(slot) : null;
        }

        public static PlayerDraftState GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        // ---- Client sync --------------------------------------------------------

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn  = turnNumber;
            TurnTimeLeft = TurnDuration;
            foreach (var state in _slotMap.Values)
            {
                state.IsPickingNow = (state.SlotNumber == currentPickerSlot);
                if (state.SlotNumber < currentPickerSlot) state.HasPicked = true;
            }
        }

        public static void SetDraftStateFromHost(int totalSlots, List<byte> playerIds, List<int> slotNumbers)
        {
            ApplyLocalSettings();
            _slotMap.Clear();
            _pidToSlot.Clear();
            TurnOrder.Clear();

            IsDraftActive = true;
            for (int i = 0; i < playerIds.Count; i++)
            {
                var state = new PlayerDraftState { PlayerId = playerIds[i], SlotNumber = slotNumbers[i] };
                _slotMap[slotNumbers[i]]  = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }
            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        // ---- Draft start --------------------------------------------------------

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;

            DraftTicker.EnsureExists();

            string  savedForcedName   = _forcedRoleName;
            byte    savedForcedTarget = _forcedRoleTargetId;

            Reset(cancelledBeforeCompletion: true);
            ApplyLocalSettings();

            if (!string.IsNullOrWhiteSpace(savedForcedName) && savedForcedTarget != 255)
            {
                _forcedRoleName     = savedForcedName;
                _forcedRoleTargetId = savedForcedTarget;
                _forcedRoleId       = null;
                DraftModePlugin.Logger.LogInfo(
                _forcedRoleId       = null; // will be resolved after pool is built
                LoggingSystem.Debug(
                    $"[DraftManager] Restored pending forced role '{savedForcedName}' " +
                    $"for player {savedForcedTarget} after Reset");
            }

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();

            _pool = RolePoolBuilder.BuildPool();
            if (_pool.RoleIds.Count == 0) return;

            int totalSlots    = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();

            List<byte> syncPids  = new();
            List<int>  syncSlots = new();

            for (int i = 0; i < totalSlots; i++)
            {
                int  slot = shuffledSlots[i];
                byte pid  = players[i].PlayerId;
                _slotMap[slot]  = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder     = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn   = 1;
            TurnTimeLeft  = TurnDuration;
            IsDraftActive = true;

            AssignFactionBuckets();
            ResolveForcedRoleId();

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);

            DraftStatusOverlay.SetState(OverlayState.Waiting);
            OfferRolesToCurrentPicker();
        }

        // ---- Forced role resolution ---------------------------------------------

        private static void ResolveForcedRoleId()
        {
            if (string.IsNullOrWhiteSpace(_forcedRoleName)) return;
            _forcedRoleId = null;

            LoggingSystem.Debug(
                $"[DraftManager] Resolving forced role '{_forcedRoleName}' " +
                $"for player {_forcedRoleTargetId} (pool has {_pool.RoleIds.Count} roles)");

            foreach (var id in _pool.RoleIds)
            {
                var role = RoleManager.Instance?.GetRole((RoleTypes)id);
                if (role == null) continue;
                LoggingSystem.Debug($"[DraftManager]   pool role: '{role.NiceName}' (id={id})");
                if (string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = id;
                    LoggingSystem.Debug(
                        $"[DraftManager] Forced role resolved in pool: '{_forcedRoleName}' -> {id}");
                    return;
                }
            }

            foreach (RoleTypes rt in System.Enum.GetValues(typeof(RoleTypes)))
            {
                var role = RoleManager.Instance?.GetRole(rt);
                if (role != null && string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = (ushort)rt;
                    LoggingSystem.Debug(
                        $"[DraftManager] Forced role resolved (outside pool): '{_forcedRoleName}' -> {rt}");
                    return;
                }
            }

            LoggingSystem.Warning(
                $"[DraftManager] Could not resolve forced role name: '{_forcedRoleName}'");
        }

        // ---- Bucket pre-planning ------------------------------------------------

        private static void AssignFactionBuckets()
        {
            int playerCount = TurnOrder.Count;

            int impSlots = Mathf.Min(MaxImpostors,       playerCount);
            int nkSlots  = Mathf.Min(MaxNeutralKillings, playerCount);
            int npSlots  = Mathf.Min(MaxNeutralPassives, playerCount);

            int poolImp = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.Impostor);
            int poolNK  = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.NeutralKilling);
            int poolNP  = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.Neutral);

            impSlots = Mathf.Min(impSlots, poolImp);
            nkSlots  = Mathf.Min(nkSlots,  poolNK);
            npSlots  = Mathf.Min(npSlots,  poolNP);

            int nonCrewTotal = impSlots + nkSlots + npSlots;
            if (nonCrewTotal > playerCount)
            {
                float scale = (float)playerCount / nonCrewTotal;
                impSlots = Mathf.FloorToInt(impSlots * scale);
                nkSlots  = Mathf.FloorToInt(nkSlots  * scale);
                npSlots  = Mathf.FloorToInt(npSlots  * scale);
            }

            var buckets = new List<RoleFaction?>();
            for (int i = 0; i < impSlots; i++) buckets.Add(RoleFaction.Impostor);
            for (int i = 0; i < nkSlots;  i++) buckets.Add(RoleFaction.NeutralKilling);
            for (int i = 0; i < npSlots;  i++) buckets.Add(RoleFaction.Neutral);
            while (buckets.Count < playerCount) buckets.Add(null);

            // Spread non-crew slots evenly across early/mid/late positions rather
            // than pure random shuffle, which tends to cluster them at the front.
            // Divide the turn order into thirds and ensure each third gets a
            // proportional share of non-crew guaranteed slots.
            int nonCrew = impSlots + nkSlots + npSlots;
            if (nonCrew > 0 && playerCount >= 3)
            {
                var nonCrewBuckets = buckets.Where(b => b.HasValue)
                                            .OrderBy(_ => UnityEngine.Random.value)
                                            .ToList();
                var crewBuckets    = buckets.Where(b => !b.HasValue).ToList();

                // Split positions into thirds, assign one non-crew to each third
                // before randomly filling the rest
                var positions = Enumerable.Range(0, playerCount)
                                          .OrderBy(_ => UnityEngine.Random.value)
                                          .ToList();

                int third = playerCount / 3;
                var earlyPos = positions.Take(third).OrderBy(_ => UnityEngine.Random.value).ToList();
                var midPos   = positions.Skip(third).Take(third).OrderBy(_ => UnityEngine.Random.value).ToList();
                var latePos  = positions.Skip(third * 2).OrderBy(_ => UnityEngine.Random.value).ToList();

                var assignedBuckets = new RoleFaction?[playerCount];

                // Distribute non-crew as evenly as possible across thirds
                int perThird = nonCrew / 3;
                int extra    = nonCrew % 3;

                var thirds = new List<List<int>> { earlyPos, midPos, latePos };
                int bucketIdx = 0;
                for (int t = 0; t < 3 && bucketIdx < nonCrewBuckets.Count; t++)
                {
                    int count = perThird + (t < extra ? 1 : 0);
                    foreach (var pos in thirds[t].Take(count))
                    {
                        if (bucketIdx >= nonCrewBuckets.Count) break;
                        assignedBuckets[pos] = nonCrewBuckets[bucketIdx++];
                    }
                }

                // Fill remaining positions with crew (null)
                for (int i = 0; i < playerCount; i++)
                    if (!assignedBuckets[i].HasValue && bucketIdx >= nonCrewBuckets.Count)
                        assignedBuckets[i] = null;

                for (int i = 0; i < TurnOrder.Count; i++)
                {
                    var state = GetStateForSlot(TurnOrder[i]);
                    if (state != null) state.GuaranteedFaction = assignedBuckets[i];
                }
            }
            else
            {
                // Fallback: pure shuffle (small player counts or no non-crew)
                buckets = buckets.OrderBy(_ => UnityEngine.Random.value).ToList();
                for (int i = 0; i < TurnOrder.Count; i++)
                {
                    var state = GetStateForSlot(TurnOrder[i]);
                    if (state != null) state.GuaranteedFaction = buckets[i];
                }
            }

            LoggingSystem.Debug(
                $"[DraftManager] Buckets assigned: {impSlots} Imp, {nkSlots} NK, " +
                $"{npSlots} NP, {playerCount - impSlots - nkSlots - npSlots} Crew");
        }

        // ---- Reset --------------------------------------------------------------

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive    = false;
            // FIX: TurnTimerRunning was never reset — this caused the ticker to keep
            // firing AutoPickRandom() on the next draft (or after disconnect).
            TurnTimerRunning = false;
            _endSequenceRunning = false;
            CurrentTurn      = 0;
            TurnTimeLeft     = 0f;
            DraftUiManager.CloseAll();

            if (cancelledBeforeCompletion)
            {
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
                DraftRecapOverlay.Hide();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
            }

            _slotMap.Clear();
            _pidToSlot.Clear();
            _pool = new DraftRolePool();
            _draftedCounts.Clear();
            TurnOrder.Clear();

            _impostorsDrafted       = 0;
            _neutralKillingsDrafted = 0;
            _neutralPassivesDrafted = 0;

            _forcedRoleName     = null;
            _forcedRoleId       = null;
            _forcedRoleTargetId = 255;

            if (cancelledBeforeCompletion)
                UpCommandRequests.Clear();
        }

        // ---- Timer --------------------------------------------------------------

        public static bool TurnTimerRunning { get; private set; } = false;

        public static void StartTurnTimer()
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimerRunning = true;
        }

        public static void Tick(float deltaTime)
        {
            // FIX: Added IsDraftActive guard so stale ticks after disconnect
            // don't call AutoPickRandom and corrupt state.
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost || !TurnTimerRunning) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f)
            {
                TurnTimerRunning = false; // FIX: stop timer before auto-pick to prevent double-fire
                AutoPickRandom();
            }
        }

        // ---- Role offering ------------------------------------------------------

        private static List<ushort> GetAvailableIds()
        {
            return _pool.RoleIds.Where(id =>
            {
                if (GetDraftedCount(id) >= GetMaxCount(id)) return false;
                var faction = GetFaction(id);
                if (faction == RoleFaction.Impostor       && _impostorsDrafted      >= MaxImpostors)       return false;
                if (faction == RoleFaction.NeutralKilling && _neutralKillingsDrafted >= MaxNeutralKillings) return false;
                if (faction == RoleFaction.Neutral        && _neutralPassivesDrafted >= MaxNeutralPassives) return false;
                return true;
            }).ToList();
        }

        private static List<ushort> GetAvailableForFaction(RoleFaction faction)
        {
            return GetAvailableIds().Where(id => GetFaction(id) == faction).ToList();
        }

        private static void OfferRolesToCurrentPicker()
        {
            // FIX: Guard against calling this when draft is no longer active
            if (!IsDraftActive) return;

            var state = GetCurrentPickerState();
            if (state == null) return;

            // Skip disconnected players immediately — auto-pick random for them
            if (state.IsDisconnected)
            {
                DraftModePlugin.Logger.LogInfo($"[DraftManager] Skipping DC'd player slot {state.SlotNumber}");
                FinalisePickForCurrentSlot(PickFullRandom());
                return;
            }

            state.IsPickingNow = true;
            TurnTimerRunning   = false;

            DraftModePlugin.Logger.LogInfo(
            // ---- Forced card injection ------------------------------------------
            // If admin pinned a role for this slot's player, inject it and auto-pick.
            LoggingSystem.Debug(
                $"[DraftManager] OfferRoles: slot={state.SlotNumber} pid={state.PlayerId} " +
                $"| forcedRoleId={_forcedRoleId?.ToString() ?? "null"} " +
                $"forcedTarget={_forcedRoleTargetId} forcedName='{_forcedRoleName}'");

            if (_forcedRoleId.HasValue && state.PlayerId == _forcedRoleTargetId)
            {
                ushort forcedId   = _forcedRoleId.Value;
                string forcedName = _forcedRoleName ?? forcedId.ToString();
                _forcedRoleName     = null;
                _forcedRoleId       = null;
                _forcedRoleTargetId = 255;

                LoggingSystem.Debug(
                    $"[DraftManager] Injecting forced card '{forcedName}' for slot {state.SlotNumber}");

                var available2 = GetAvailableIds();
                var offered2   = new List<ushort> { forcedId };
                var fill       = available2.Where(id => id != forcedId).ToList();
                offered2.AddRange(PickWeightedUnique(fill, Math.Max(0, OfferedRolesCount - 1)));
                while (offered2.Count < OfferedRolesCount)
                    offered2.Add((ushort)RoleTypes.Crewmate);

                offered2 = offered2.OrderBy(_ => UnityEngine.Random.value).ToList();
                int forcedIndex = offered2.IndexOf(forcedId);

                state.OfferedRoleIds = offered2;
                DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, offered2, CurrentTurn);

                Coroutines.Start(CoAutoPickForced(state.PlayerId, forcedIndex));
                return;
            }

            int target    = OfferedRolesCount;
            var available = GetAvailableIds();
            var offered   = new List<ushort>();

            if (state.GuaranteedFaction.HasValue && available.Count > 0)
            {
                var bucketPool = GetAvailableForFaction(state.GuaranteedFaction.Value);
                if (bucketPool.Count > 0)
                {
                    offered.AddRange(PickWeightedUnique(bucketPool, 1));
                    LoggingSystem.Debug(
                        $"[DraftManager] Slot {state.SlotNumber} guaranteed " +
                        $"{state.GuaranteedFaction.Value} card: {(RoleTypes)offered[0]}");
                }
                else
                {
                    LoggingSystem.Debug(
                        $"[DraftManager] Slot {state.SlotNumber} guaranteed faction " +
                        $"{state.GuaranteedFaction.Value} is exhausted, filling with available");
                }
            }

            int remaining = target - offered.Count;

            if (remaining > 0 && available.Count > 0)
            {
                var fillPool  = available.Where(id => !offered.Contains(id)).ToList();
                if (fillPool.Count == 0)
                {
                    while (offered.Count < target) offered.Add((ushort)RoleTypes.Crewmate);
                }
                else
                {
                    var crewFill  = fillPool.Where(id => GetFaction(id) == RoleFaction.Crewmate).ToList();
                    var otherFill = fillPool.Where(id => GetFaction(id) != RoleFaction.Crewmate).ToList();

                    if (!state.GuaranteedFaction.HasValue && otherFill.Count > 0 && remaining >= 2)
                    {
                        offered.AddRange(PickWeightedUnique(otherFill, 1));
                        remaining--;
                    }

                    if (crewFill.Count > 0) offered.AddRange(PickWeightedUnique(crewFill, remaining));

                    if (offered.Count < target)
                    {
                        var topUp = available.Where(id => !offered.Contains(id)).ToList();
                        offered.AddRange(PickWeightedUnique(topUp, target - offered.Count));
                    }

                    while (offered.Count < target) offered.Add((ushort)RoleTypes.Crewmate);
                }
            }
            else if (available.Count == 0)
            {
                while (offered.Count < target) offered.Add((ushort)RoleTypes.Crewmate);
            }

            state.OfferedRoleIds = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoleIds, CurrentTurn);
        }

        private static IEnumerator CoAutoPickForced(byte playerId, int cardIndex)
        {
            yield return new WaitForSeconds(1.5f);
            // FIX: Guard against draft ending while coroutine was waiting
            if (!IsDraftActive) yield break;
            DraftModePlugin.Logger.LogInfo($"[DraftManager] Auto-submitting forced pick at index {cardIndex}");
            LoggingSystem.Debug($"[DraftManager] Auto-submitting forced pick at index {cardIndex}");
            SubmitPick(playerId, cardIndex);
        }

        // ---- Pick submission ----------------------------------------------------

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            ushort chosenId = (choiceIndex >= state.OfferedRoleIds.Count)
                ? PickFullRandom()
                : state.OfferedRoleIds[choiceIndex];

            FinalisePickForCurrentSlot(chosenId);
            return true;
        }

        private static void AutoPickRandom()
        {
            // FIX: Guard — don't auto-pick if draft was cancelled while timer was running
            if (!IsDraftActive) return;

            var state = GetCurrentPickerState();
            if (!ShowRandomOption && state != null && state.OfferedRoleIds.Count > 0)
                FinalisePickForCurrentSlot(
                    state.OfferedRoleIds[UnityEngine.Random.Range(0, state.OfferedRoleIds.Count)]);
            else
                FinalisePickForCurrentSlot(PickFullRandom());
        }

        private static ushort PickFullRandom()
        {
            var available = GetAvailableIds();
            if (available.Count == 0) return (ushort)RoleTypes.Crewmate;
            return UseRoleChances ? PickWeighted(available) : available[UnityEngine.Random.Range(0, available.Count)];
        }

        private static void FinalisePickForCurrentSlot(ushort roleId)
        {
            // FIX: Double-guard at the start of finalise to prevent processing
            // a pick that arrived after the draft was cancelled
            if (!IsDraftActive) return;

            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRoleId = roleId;
            state.HasPicked    = true;
            state.IsPickingNow = false;

            DraftNetworkHelper.BroadcastPickConfirmed(state.SlotNumber, roleId);

            _draftedCounts[roleId] = GetDraftedCount(roleId) + 1;

            var faction = GetFaction(roleId);
            if      (faction == RoleFaction.Impostor)       _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral)        _neutralPassivesDrafted++;

            LoggingSystem.Debug(
                $"[DraftManager] Slot {state.SlotNumber} picked {(RoleTypes)roleId} ({faction}). " +
                $"Caps: Imp={_impostorsDrafted}/{MaxImpostors}, " +
                $"NK={_neutralKillingsDrafted}/{MaxNeutralKillings}, " +
                $"NP={_neutralPassivesDrafted}/{MaxNeutralPassives}");

            CurrentTurn++;
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                ApplyAllRoles();
                IsDraftActive = false;
                TurnTimerRunning = false; // FIX: ensure timer stops on draft completion
                DraftUiManager.CloseAll();
                DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
                var recapEntries = BuildRecapEntries();
                DraftNetworkHelper.BroadcastRecap(recapEntries, ShowRecap);
                Reset(cancelledBeforeCompletion: false);
                TriggerEndDraftSequence();
            }
            else
            {
                TurnTimeLeft = TurnDuration;
                OfferRolesToCurrentPicker();
            }
        }

        // ---- Recap --------------------------------------------------------------

        public static List<RecapEntry> BuildRecapEntries()
        {
            var entries = new List<RecapEntry>();
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                string roleName = "?";
                if (s.ChosenRoleId.HasValue)
                {
                    var role = RoleManager.Instance?.GetRole((RoleTypes)s.ChosenRoleId.Value);
                    roleName = role?.NiceName ?? s.ChosenRoleId.Value.ToString();
                }
                entries.Add(new RecapEntry(s.SlotNumber, roleName));
            }
            return entries;
        }

        // ---- Role application ---------------------------------------------------

        private static void ApplyAllRoles()
        {
            PendingRoleAssignments.Clear();
            _appliedPlayers.Clear();

            foreach (var state in _slotMap.Values)
            {
                if (!state.ChosenRoleId.HasValue) continue;
                if (state.PlayerId >= 200) continue;
                PendingRoleAssignments[state.PlayerId] = (RoleTypes)state.ChosenRoleId.Value;
                LoggingSystem.Debug(
                    $"[DraftManager] Queued {(RoleTypes)state.ChosenRoleId.Value} for player {state.PlayerId}");
            }

            LoggingSystem.Debug(
                $"[DraftManager] {PendingRoleAssignments.Count} roles queued for game start");
        }

        public static bool ApplyPendingRolesOnGameStart()
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            if (PendingRoleAssignments.Count == 0) return true;

            LoggingSystem.Debug(
                $"[DraftManager] Attempting to apply " +
                $"{PendingRoleAssignments.Count - _appliedPlayers.Count} remaining roles...");

            foreach (var kvp in PendingRoleAssignments)
            {
                if (_appliedPlayers.Contains(kvp.Key)) continue;

                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == kvp.Key);
                if (p == null)
                {
                    LoggingSystem.Warning($"[DraftManager] Player {kvp.Key} not found yet — will retry");
                    continue;
                }

                try
                {
                    p.RpcSetRole(kvp.Value, false);
                    _appliedPlayers.Add(kvp.Key);
                    LoggingSystem.Debug(
                        $"[DraftManager] Applied {kvp.Value} to {p.Data.PlayerName} (id {kvp.Key})");
                }
                catch (Exception ex)
                {
                    LoggingSystem.Warning(
                        $"[DraftManager] RpcSetRole failed for player {kvp.Key}: {ex.Message} — will retry");
                }
            }

            bool allDone = _appliedPlayers.Count >= PendingRoleAssignments.Count;
            if (allDone)
            {
                LoggingSystem.Debug("[DraftManager] All roles applied successfully.");
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
            }
            return allDone;
        }

        public static IEnumerator CoApplyRolesWithRetry()
        {
            if (!AmongUsClient.Instance.AmHost) yield break;
            if (PendingRoleAssignments.Count == 0) yield break;

            LoggingSystem.Debug("[DraftManager] Starting role application retry loop...");

            float elapsed  = 0f;
            float timeout  = 10f;
            float interval = 0.5f;

            while (elapsed < timeout)
            {
                yield return new WaitForSeconds(interval);
                elapsed += interval;
                if (PendingRoleAssignments.Count == 0) yield break;
                bool done = ApplyPendingRolesOnGameStart();
                if (done)
                {
                    LoggingSystem.Debug($"[DraftManager] Role retry loop finished after {elapsed:F1}s");
                    yield break;
                }
            }

            if (PendingRoleAssignments.Count > 0)
            {
                LoggingSystem.Warning("[DraftManager] Retry loop timed out — falling back to UpCommandRequests");
                foreach (var kvp in PendingRoleAssignments)
                {
                    if (_appliedPlayers.Contains(kvp.Key)) continue;
                    var role = RoleManager.Instance?.GetRole(kvp.Value);
                    var p    = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x.PlayerId == kvp.Key);
                    if (role != null && p != null)
                    {
                        UpCommandRequests.SetRequest(p.Data.PlayerName, role.NiceName);
                        LoggingSystem.Debug(
                            $"[DraftManager] UpCommandRequests fallback: {role.NiceName} for {p.Data.PlayerName}");
                    }
                }
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
            }
        }

        // ---- Helpers ------------------------------------------------------------

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration          = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap             = opts.ShowRecap;
            AutoStartAfterDraft   = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances        = opts.UseRoleChances;
            OfferedRolesCount     = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption      = opts.ShowRandomOption;
            MaxImpostors          = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors), 0, 10);
            MaxNeutralKillings    = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings), 0, 10);
            MaxNeutralPassives    = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives), 0, 10);
        }

        private static int GetDraftedCount(ushort id) => _draftedCounts.TryGetValue(id, out var c) ? c : 0;
        private static int GetMaxCount(ushort id)     => _pool.MaxCounts.TryGetValue(id, out var c) ? c : 1;

        private static RoleFaction GetFaction(ushort id)
        {
            if (_pool.Factions.TryGetValue(id, out var f)) return f;
            var role = RoleManager.Instance?.GetRole((RoleTypes)id);
            return role != null ? RoleCategory.GetFactionFromRole(role) : RoleFaction.Crewmate;
        }

        private static int GetWeight(ushort id) =>
            _pool.Weights.TryGetValue(id, out var w) ? Math.Max(1, w) : 1;

        private static ushort PickWeighted(List<ushort> candidates)
        {
            int total = candidates.Sum(GetWeight);
            if (total <= 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc  = 0;
            foreach (var id in candidates)
            {
                acc += GetWeight(id);
                if (roll <= acc) return id;
            }
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static List<ushort> PickWeightedUnique(List<ushort> candidates, int count)
        {
            var results = new List<ushort>();
            var temp    = new List<ushort>(candidates);
            while (results.Count < count && temp.Count > 0)
            {
                var pick = UseRoleChances
                    ? PickWeighted(temp)
                    : temp[UnityEngine.Random.Range(0, temp.Count)];
                results.Add(pick);
                temp.Remove(pick);
            }
            return results;
        }

        // ---- End sequence -------------------------------------------------------

        public static void TriggerEndDraftSequence()
        {
            if (_endSequenceRunning) return; // FIX: prevent double-trigger
            _endSequenceRunning = true;
            Coroutines.Start(CoEndDraftSequence());
        }

        private static IEnumerator CoEndDraftSequence()
        {
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);

            // FIX: Check if we were reset/cancelled while waiting
            if (!_endSequenceRunning) yield break;

            try { DraftRecapOverlay.Hide(); } catch { }

            if (!AutoStartAfterDraft)
            {
                _endSequenceRunning = false;
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

            bool shouldAutoStart = AutoStartAfterDraft && AmongUsClient.Instance.AmHost;
            if (shouldAutoStart &&
                GameStartManager.Instance != null &&
                AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined)
            {
                SkipCountdown = true;
                int orig = GameStartManager.Instance.MinPlayers;
                GameStartManager.Instance.MinPlayers = 1;
                GameStartManager.Instance.BeginGame();
                GameStartManager.Instance.MinPlayers = orig;
                yield return null;
                SkipCountdown = false;
            }

            yield return new WaitForSeconds(0.6f);
            _endSequenceRunning = false;
            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}
