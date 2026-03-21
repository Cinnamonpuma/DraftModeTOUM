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
        // Admin pins a role via the web dashboard. On the player's next turn it is
        // injected as a guaranteed card and auto-confirmed so they receive it.
        // Only the HOST stores and acts on this — non-hosts relay via ForceRole RPC.
        private static string  _forcedRoleName    = null;
        private static ushort? _forcedRoleId      = null;
        private static byte    _forcedRoleTargetId = 255; // 255 = unset

        /// <summary>
        /// Called on the host (directly or via RPC relay) to pin a forced role
        /// for a specific player. targetPlayerId is the Among Us player ID.
        /// </summary>
        public static void SetForcedDraftRole(string roleName, byte targetPlayerId)
        {
            _forcedRoleName     = roleName;
            _forcedRoleId       = null;
            _forcedRoleTargetId = targetPlayerId;
            DraftModePlugin.Logger.LogInfo($"[DraftManager] Forced draft card set: '{roleName}' for player {targetPlayerId}");
        }

        // ---- Public accessors ---------------------------------------------------

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;

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

            // Preserve any pending forced role across Reset()
            string  savedForcedName   = _forcedRoleName;
            byte    savedForcedTarget = _forcedRoleTargetId;

            Reset(cancelledBeforeCompletion: true);
            ApplyLocalSettings();

            // Restore the forced role if one was pending
            if (!string.IsNullOrWhiteSpace(savedForcedName) && savedForcedTarget != 255)
            {
                _forcedRoleName     = savedForcedName;
                _forcedRoleTargetId = savedForcedTarget;
                _forcedRoleId       = null; // will be resolved after pool is built
                DraftModePlugin.Logger.LogInfo(
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
            ResolveForcedRoleId(); // resolve name → ID now that pool is built

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);

            DraftStatusOverlay.SetState(OverlayState.Waiting);
            OfferRolesToCurrentPicker();
        }

        // ---- Forced role resolution ---------------------------------------------

        /// <summary>
        /// Resolves _forcedRoleName to a role ID. Searches the pool first,
        /// then all RoleTypes. Must be called after _pool is built.
        /// </summary>
        private static void ResolveForcedRoleId()
        {
            if (string.IsNullOrWhiteSpace(_forcedRoleName)) return;
            _forcedRoleId = null;

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Resolving forced role '{_forcedRoleName}' " +
                $"for player {_forcedRoleTargetId} (pool has {_pool.RoleIds.Count} roles)");

            // Search pool first
            foreach (var id in _pool.RoleIds)
            {
                var role = RoleManager.Instance?.GetRole((RoleTypes)id);
                if (role == null) continue;
                DraftModePlugin.Logger.LogInfo($"[DraftManager]   pool role: '{role.NiceName}' (id={id})");
                if (string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = id;
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Forced role resolved in pool: '{_forcedRoleName}' -> {id}");
                    return;
                }
            }

            // Fall back to all registered roles (bypasses pool — still force-assigns it)
            foreach (RoleTypes rt in System.Enum.GetValues(typeof(RoleTypes)))
            {
                var role = RoleManager.Instance?.GetRole(rt);
                if (role != null && string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = (ushort)rt;
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Forced role resolved (outside pool): '{_forcedRoleName}' -> {rt}");
                    return;
                }
            }

            DraftModePlugin.Logger.LogWarning(
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

            buckets = buckets.OrderBy(_ => UnityEngine.Random.value).ToList();

            for (int i = 0; i < TurnOrder.Count; i++)
            {
                var state = GetStateForSlot(TurnOrder[i]);
                if (state != null) state.GuaranteedFaction = buckets[i];
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Buckets assigned: {impSlots} Imp, {nkSlots} NK, " +
                $"{npSlots} NP, {playerCount - impSlots - nkSlots - npSlots} Crew");
        }

        // ---- Reset --------------------------------------------------------------

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive    = false;
            TurnTimerRunning = false;
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
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost || !TurnTimerRunning) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
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
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;
            TurnTimerRunning   = false;

            // ---- Forced card injection ------------------------------------------
            // If admin pinned a role for this slot's player, inject it and auto-pick.
            DraftModePlugin.Logger.LogInfo(
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

                DraftModePlugin.Logger.LogInfo(
                    $"[DraftManager] Injecting forced card '{forcedName}' for slot {state.SlotNumber}");

                var available2 = GetAvailableIds();
                var offered2   = new List<ushort> { forcedId };
                var fill       = available2.Where(id => id != forcedId).ToList();
                offered2.AddRange(PickWeightedUnique(fill, Math.Max(0, OfferedRolesCount - 1)));
                while (offered2.Count < OfferedRolesCount)
                    offered2.Add((ushort)RoleTypes.Crewmate);

                // Shuffle visually so position doesn't give it away
                offered2 = offered2.OrderBy(_ => UnityEngine.Random.value).ToList();
                int forcedIndex = offered2.IndexOf(forcedId);

                state.OfferedRoleIds = offered2;
                DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, offered2, CurrentTurn);

                // Auto-pick after animation
                Coroutines.Start(CoAutoPickForced(state.PlayerId, forcedIndex));
                return;
            }

            // ---- Normal offer ---------------------------------------------------
            int target    = OfferedRolesCount;
            var available = GetAvailableIds();
            var offered   = new List<ushort>();

            // Step 1: guaranteed faction card
            if (state.GuaranteedFaction.HasValue && available.Count > 0)
            {
                var bucketPool = GetAvailableForFaction(state.GuaranteedFaction.Value);
                if (bucketPool.Count > 0)
                {
                    offered.AddRange(PickWeightedUnique(bucketPool, 1));
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Slot {state.SlotNumber} guaranteed " +
                        $"{state.GuaranteedFaction.Value} card: {(RoleTypes)offered[0]}");
                }
                else
                {
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Slot {state.SlotNumber} guaranteed faction " +
                        $"{state.GuaranteedFaction.Value} is exhausted, filling with available");
                }
            }

            // Step 2: fill remaining slots
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

        // Auto-picks the forced card after the cards animate in
        private static IEnumerator CoAutoPickForced(byte playerId, int cardIndex)
        {
            yield return new WaitForSeconds(1.5f);
            DraftModePlugin.Logger.LogInfo($"[DraftManager] Auto-submitting forced pick at index {cardIndex}");
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
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRoleId = roleId;
            state.HasPicked    = true;
            state.IsPickingNow = false;

            _draftedCounts[roleId] = GetDraftedCount(roleId) + 1;

            var faction = GetFaction(roleId);
            if      (faction == RoleFaction.Impostor)       _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral)        _neutralPassivesDrafted++;

            DraftModePlugin.Logger.LogInfo(
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
                DraftModePlugin.Logger.LogInfo(
                    $"[DraftManager] Queued {(RoleTypes)state.ChosenRoleId.Value} for player {state.PlayerId}");
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] {PendingRoleAssignments.Count} roles queued for game start");
        }

        public static bool ApplyPendingRolesOnGameStart()
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            if (PendingRoleAssignments.Count == 0) return true;

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Attempting to apply " +
                $"{PendingRoleAssignments.Count - _appliedPlayers.Count} remaining roles...");

            foreach (var kvp in PendingRoleAssignments)
            {
                if (_appliedPlayers.Contains(kvp.Key)) continue;

                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == kvp.Key);
                if (p == null)
                {
                    DraftModePlugin.Logger.LogWarning($"[DraftManager] Player {kvp.Key} not found yet — will retry");
                    continue;
                }

                try
                {
                    p.RpcSetRole(kvp.Value, false);
                    _appliedPlayers.Add(kvp.Key);
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Applied {kvp.Value} to {p.Data.PlayerName} (id {kvp.Key})");
                }
                catch (Exception ex)
                {
                    DraftModePlugin.Logger.LogWarning(
                        $"[DraftManager] RpcSetRole failed for player {kvp.Key}: {ex.Message} — will retry");
                }
            }

            bool allDone = _appliedPlayers.Count >= PendingRoleAssignments.Count;
            if (allDone)
            {
                DraftModePlugin.Logger.LogInfo("[DraftManager] All roles applied successfully.");
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
            }
            return allDone;
        }

        public static IEnumerator CoApplyRolesWithRetry()
        {
            if (!AmongUsClient.Instance.AmHost) yield break;
            if (PendingRoleAssignments.Count == 0) yield break;

            DraftModePlugin.Logger.LogInfo("[DraftManager] Starting role application retry loop...");

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
                    DraftModePlugin.Logger.LogInfo($"[DraftManager] Role retry loop finished after {elapsed:F1}s");
                    yield break;
                }
            }

            if (PendingRoleAssignments.Count > 0)
            {
                DraftModePlugin.Logger.LogWarning("[DraftManager] Retry loop timed out — falling back to UpCommandRequests");
                foreach (var kvp in PendingRoleAssignments)
                {
                    if (_appliedPlayers.Contains(kvp.Key)) continue;
                    var role = RoleManager.Instance?.GetRole(kvp.Value);
                    var p    = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x.PlayerId == kvp.Key);
                    if (role != null && p != null)
                    {
                        UpCommandRequests.SetRequest(p.Data.PlayerName, role.NiceName);
                        DraftModePlugin.Logger.LogInfo(
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

        public static void TriggerEndDraftSequence() => Coroutines.Start(CoEndDraftSequence());

        private static IEnumerator CoEndDraftSequence()
        {
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);
            try { DraftRecapOverlay.Hide(); } catch { }

            if (!AutoStartAfterDraft)
            {
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
            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}
