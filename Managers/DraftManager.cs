using AmongUs.GameOptions;
using DraftModeTOUM.Patches;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TownOfUs.Utilities;

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

        private static bool _soloTestMode = false;
        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int> _pidToSlot = new Dictionary<byte, int>();
        private static List<string> _lobbyRolePool = new List<string>();
        private static HashSet<string> _draftedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static int GetSlotForPlayer(byte playerId) => _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState GetStateForSlot(int slot) => _slotMap.TryGetValue(slot, out var s) ? s : null;

        public static PlayerDraftState GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static void SetClientTurn(int turnNumber)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn = turnNumber;
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
                _slotMap[slotNumbers[i]] = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }
            TurnOrder = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn = 1;
            TurnTimeLeft = TurnDuration;
        }

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            DraftTicker.EnsureExists();
            Reset();
            UpCommandRequests.Clear();

            var players = PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && !p.Data.Disconnected).ToList();
            _soloTestMode = (players.Count < 2);

            _lobbyRolePool = RolePoolBuilder.BuildPool();
            if (_lobbyRolePool.Count == 0) return;

            int totalSlots = _soloTestMode ? 4 : players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            List<byte> syncPids = new List<byte>();
            List<int> syncSlots = new List<int>();

            for (int i = 0; i < totalSlots; i++)
            {
                int slot = shuffledSlots[i];
                byte pid = _soloTestMode
                    ? (i == 0 ? PlayerControl.LocalPlayer.PlayerId : (byte)(200 + i))
                    : players[i].PlayerId;
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
            OfferRolesToCurrentPicker();
        }

        public static void Reset()
        {
            IsDraftActive = false;
            CurrentTurn = 0;
            TurnTimeLeft = 0f;
            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoles.Clear();
            TurnOrder.Clear();
            _soloTestMode = false;
            UpCommandRequests.Clear();
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;
            state.OfferedRoles = _lobbyRolePool
                .Where(r => !_draftedRoles.Contains(r))
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(3)
                .ToList();
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

        private static void AutoPickRandom() => FinalisePickForCurrentSlot(PickFullRandom());

        private static string PickFullRandom()
        {
            var available = _lobbyRolePool.Where(r => !_draftedRoles.Contains(r)).ToList();
            return available.Count > 0 ? available[UnityEngine.Random.Range(0, available.Count)] : "Crewmate";
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole = roleName;
            state.HasPicked = true;
            state.IsPickingNow = false;
            _draftedRoles.Add(roleName);

            CurrentTurn++;
            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();
                DraftNetworkHelper.BroadcastRecap(BuildRecapMessage());
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
                var state = GetStateForSlot(slot);
                sb.AppendLine($"Player {state.SlotNumber}: <color=#4BD7E4>{state.ChosenRole}</color>");
            }
            return sb.ToString();
        }

        private static void ApplyAllRoles()
        {
            foreach (var state in _slotMap.Values)
            {
                if (state.PlayerId >= 200) continue;
                var p = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x.PlayerId == state.PlayerId);
                if (p != null) UpCommandRequests.SetRequest(p.Data.PlayerName, state.ChosenRole);
            }
        }

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }
    }
}