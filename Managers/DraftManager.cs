using AmongUs.GameOptions;
using DraftModeTOUM.Patches;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte PlayerId { get; set; }
        public int SlotNumber { get; set; }
        public string ChosenRole { get; set; }
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

        public static bool ShowRecap { get; set; } = true;

        public static int MaxImpostors { get; set; } = 2;
        public static int MaxNeutrals { get; set; } = 3;

        private static int _impostorsDrafted = 0;
        private static int _neutralsDrafted = 0;
        private static bool _soloTestMode = false;

        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap   = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int>             _pidToSlot = new Dictionary<byte, int>();
        private static List<string>                      _lobbyRolePool = new List<string>();
        private static HashSet<string>                   _draftedRoles  =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Constant sender name matching TOUM's system style ─────────────────
        private const string SystemName = "<color=#8BFDFD>System</color>";

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;

        public static PlayerDraftState GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;

        public static PlayerDraftState GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static void SetClientTurn(int turnNumber)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn  = turnNumber;
            TurnTimeLeft = TurnDuration;
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
                _slotMap[slotNumbers[i]]   = state;
                _pidToSlot[playerIds[i]]   = slotNumbers[i];
            }
            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
        }

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            DraftTicker.EnsureExists();
            Reset();
            UpCommandRequests.Clear();

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();
            _soloTestMode = (players.Count < 2);

            _lobbyRolePool = RolePoolBuilder.BuildPool();
            if (_lobbyRolePool.Count == 0) return;

            int totalSlots    = _soloTestMode ? 4 : players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            var syncPids  = new List<byte>();
            var syncSlots = new List<int>();

            for (int i = 0; i < totalSlots; i++)
            {
                int  slot = shuffledSlots[i];
                byte pid  = _soloTestMode
                    ? (i == 0 ? PlayerControl.LocalPlayer.PlayerId : (byte)(200 + i))
                    : players[i].PlayerId;
                _slotMap[slot]  = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            IsDraftActive = true;

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            OfferRolesToCurrentPicker();
        }

        public static void Reset()
        {
            IsDraftActive = false;
            CurrentTurn   = 0;
            TurnTimeLeft  = 0f;
            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoles.Clear();
            TurnOrder.Clear();
            _soloTestMode      = false;
            _impostorsDrafted  = 0;
            _neutralsDrafted   = 0;
            UpCommandRequests.Clear();
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        private static List<string> GetAvailableRoles()
        {
            return _lobbyRolePool.Where(r =>
            {
                if (_draftedRoles.Contains(r)) return false;
                var faction = RoleCategory.GetFaction(r);
                if (faction == RoleFaction.Impostor && _impostorsDrafted >= MaxImpostors) return false;
                if (faction == RoleFaction.Neutral   && _neutralsDrafted  >= MaxNeutrals)  return false;
                return true;
            }).ToList();
        }

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.IsPickingNow = true;
            var available = GetAvailableRoles();
            if (available.Count == 0) { AutoPickRandom(); return; }

            // Try to guarantee at least one impostor and one neutral in the offer
            // when the caps allow it
            var impostorOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Impostor)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(_impostorsDrafted < MaxImpostors ? 1 : 0)
                .ToList();

            var neutralOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Neutral)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(_neutralsDrafted < MaxNeutrals ? 1 : 0)
                .ToList();

            var crewOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Crewmate)
                .OrderBy(_ => UnityEngine.Random.value)
                .ToList();

            var offered = new List<string>();
            offered.AddRange(impostorOffer);
            offered.AddRange(neutralOffer);

            int needed = 3 - offered.Count;
            offered.AddRange(crewOffer.Take(needed));

            if (offered.Count < 3)
            {
                var extras = available
                    .Where(r => !offered.Contains(r))
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(3 - offered.Count);
                offered.AddRange(extras);
            }

            state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();

            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            string chosenRole = (choiceIndex == 3 || choiceIndex >= state.OfferedRoles.Count)
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosenRole);
            return true;
        }

        private static void AutoPickRandom()
        {
            DraftScreenController.Hide();   // close UI if timer fired before player clicked
            FinalisePickForCurrentSlot(PickFullRandom());
        }

        private static string PickFullRandom()
        {
            var available = GetAvailableRoles();
            return available.Count > 0
                ? available[UnityEngine.Random.Range(0, available.Count)]
                : "Crewmate";
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole   = roleName;
            state.HasPicked    = true;
            state.IsPickingNow = false;
            _draftedRoles.Add(roleName);

            var faction = RoleCategory.GetFaction(roleName);
            if (faction == RoleFaction.Impostor) _impostorsDrafted++;
            else if (faction == RoleFaction.Neutral) _neutralsDrafted++;

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] '{roleName}' drafted ({faction}). " +
                $"Impostors: {_impostorsDrafted}/{MaxImpostors}, Neutrals: {_neutralsDrafted}/{MaxNeutrals}");

            CurrentTurn++;
            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();

                if (ShowRecap)
                    DraftNetworkHelper.BroadcastRecap(BuildRecapMessage());
                else
                    DraftNetworkHelper.BroadcastRecap("── DRAFT COMPLETE ──");
            }
            else
            {
                TurnTimeLeft = TurnDuration;
                OfferRolesToCurrentPicker();
            }
        }

        private static string BuildRecapMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("── DRAFT RECAP ──");
            foreach (var slot in TurnOrder)
            {
                var s     = GetStateForSlot(slot);
                string fc = RoleCategory.GetFaction(s.ChosenRole) switch
                {
                    RoleFaction.Impostor => "#FF4444",
                    RoleFaction.Neutral  => "#AA44FF",
                    _                    => "#4BD7E4"
                };
                sb.AppendLine($"Player {s.SlotNumber}: <color={fc}>{s.ChosenRole}</color>");
            }
            return sb.ToString();
        }

        private static void ApplyAllRoles()
        {
            foreach (var state in _slotMap.Values)
            {
                if (state.PlayerId >= 200) continue;
                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == state.PlayerId);
                if (p != null)
                    UpCommandRequests.SetRequest(p.Data.PlayerName, state.ChosenRole);
            }
        }

        // ── Messaging helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Shows a system-style chat message attributed to "Draft Mode" (cyan),
        /// matching how TOUM displays its own command feedback.
        /// </summary>
        public static void SendSystemMessage(string message, bool showHeadsup = false)
        {
            try
            {
                MiscUtils.AddFakeChat(
                    PlayerControl.LocalPlayer.Data,
                    SystemName,
                    message,
                    showHeadsup);
            }
            catch (Exception ex)
            {
                // Fallback in case MiscUtils is unavailable
                DraftModePlugin.Logger.LogWarning(
                    $"[DraftManager] AddFakeChat failed ({ex.Message}), falling back to raw chat.");
                SendChatLocal($"[Draft Mode] {message}");
            }
        }

        /// <summary>
        /// Low-level raw chat injection — only used internally as a fallback.
        /// Prefer SendSystemMessage for any user-facing messages.
        /// </summary>
        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat != null)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }
    }
}
