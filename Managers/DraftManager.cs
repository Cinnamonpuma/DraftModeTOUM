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

        // ── Recap toggle ─────────────────────────────────────────────────────
        public static bool ShowRecap { get; set; } = true;

        // ── Faction caps — change these to adjust limits ─────────────────────
        public static int MaxImpostors { get; set; } = 2;
        public static int MaxNeutrals { get; set; } = 3;

        private static int _impostorsDrafted = 0;
        private static int _neutralsDrafted = 0;

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

// ── Read the active preset and compute caps ──────────────────────
string presetPath = PresetConfigReader.FindActivePreset();
PresetReadResult preset = presetPath != null
? PresetConfigReader.Read(presetPath)
: new PresetReadResult();

var players = PlayerControl.AllPlayerControls.ToArray()
.Where(p => p != null && !p.Data.Disconnected).ToList();
int playerCount = players.Count;
_soloTestMode = (playerCount < 2);
int totalSlots = _soloTestMode ? 4 : playerCount;

// Impostor cap: AU game option (authoritative) but never more than
// the total impostor role slots in the preset
int auImpostors = GameOptionsManager.Instance.CurrentGameOptions.NumImpostors;
MaxImpostors = preset.TotalImpostorSlots > 0
? Math.Min(auImpostors, preset.TotalImpostorSlots)
: auImpostors;

// Neutral caps: sum of Num values per subalignment from the preset,
// scaled so neutrals never eat more than half the non-impostor seats
int crewSeats = totalSlots - MaxImpostors;
int maxTotalNeutrals = Math.Max(1, crewSeats / 3); // rough sanity cap

MaxNeutralKilling = Clamp(preset.TotalNeutralKillingSlots, 0, maxTotalNeutrals);
MaxNeutralEvil = Clamp(preset.TotalNeutralEvilSlots, 0, maxTotalNeutrals);
MaxNeutralBenign = Clamp(preset.TotalNeutralBenignSlots, 0, maxTotalNeutrals);
MaxNeutralOutlier = Clamp(preset.TotalNeutralOutlierSlots, 0, maxTotalNeutrals);

DraftModePlugin.Logger.LogInfo(
$"[DraftManager] Caps — Imp: {MaxImpostors}, " +
$"NK: {MaxNeutralKilling}, NE: {MaxNeutralEvil}, " +
$"NB: {MaxNeutralBenign}, NO: {MaxNeutralOutlier}");

// ── Build role pool from preset (only Num >= 1 roles) ───────────
_lobbyRolePool = preset.RoleNums.Count > 0
? preset.RoleNums.Keys.OrderBy(_ => UnityEngine.Random.value).ToList()
: RolePoolBuilder.BuildPool(); // fallback: full list

if (_lobbyRolePool.Count == 0) return;

// ── Assign slots ─────────────────────────────────────────────────
var shuffledSlots = Enumerable.Range(1, totalSlots)
.OrderBy(_ => UnityEngine.Random.value).ToList();
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

private static int Clamp(int value, int min, int max) =>
value < min ? min : value > max ? max : value;

        public static void Reset(){
            IsDraftActive = false;
            CurrentTurn = 0;
            TurnTimeLeft = 0f;
            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoles.Clear();
            TurnOrder.Clear();
            _soloTestMode = false;
            _impostorsDrafted = 0;
            _neutralKillingDrafted = 0;
            _neutralEvilDrafted = 0;
            _neutralBenignDrafted = 0;
            _neutralOutlierDrafted = 0;
            MaxImpostors = 2;
            MaxNeutralKilling = 1;
            MaxNeutralEvil = 1;
            MaxNeutralBenign = 1;
            MaxNeutralOutlier = 1;
            UpCommandRequests.Clear();
}

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        // Returns roles that are undrafted and within faction caps
        private static List<string> GetAvailableRoles()
{
return _lobbyRolePool.Where(r =>
{
if (_draftedRoles.Contains(r)) return false;
if (RoleCategory.IsImpostor(r) && _impostorsDrafted >= MaxImpostors) return false;
var sub = RoleCategory.GetSubAlignment(r);
if (sub == RoleSubAlignment.NeutralKilling && _neutralKillingDrafted >= MaxNeutralKilling) return false;
if (sub == RoleSubAlignment.NeutralEvil && _neutralEvilDrafted >= MaxNeutralEvil) return false;
if (sub == RoleSubAlignment.NeutralBenign && _neutralBenignDrafted >= MaxNeutralBenign) return false;
if (sub == RoleSubAlignment.NeutralOutlier && _neutralOutlierDrafted >= MaxNeutralOutlier) return false;
return true;
}).ToList();
}

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;

            var available = GetAvailableRoles();

            if (available.Count == 0)
            {
                // Absolute fallback — should rarely happen
                state.OfferedRoles = new List<string> { "Crewmate", "Crewmate", "Crewmate" };
            }
            else
            {
                // Try to offer variety: up to 1 impostor and 1 neutral in the 3 options,
                // only if their caps haven't been reached yet
                var impostorOffer = available
                    .Where(r => RoleCategory.IsImpostor(r))
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(1).ToList();

                var neutralOffer = available
                    .Where(r => RoleCategory.IsNeutral(r))
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(1).ToList();

                var crewOffer = available
                    .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Crewmate)
                    .OrderBy(_ => UnityEngine.Random.value)
                    .ToList();

                var offered = new List<string>();
                offered.AddRange(impostorOffer);
                offered.AddRange(neutralOffer);

                // Fill to 3 with crew first, then any remaining available roles
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

                // Shuffle so the faction roles aren't always in the same position
                state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
            }

            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            // Option 4 (index 3) = random, still respects faction caps
            string chosenRole = (choiceIndex == 3 || choiceIndex >= state.OfferedRoles.Count)
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosenRole);
            return true;
        }

        private static void AutoPickRandom() => FinalisePickForCurrentSlot(PickFullRandom());

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

            state.ChosenRole = roleName;
            state.HasPicked = true;
            state.IsPickingNow = false;
            _draftedRoles.Add(roleName);

            var sub = RoleCategory.GetSubAlignment(roleName);
if (RoleCategory.IsImpostor(roleName))
_impostorsDrafted++;
else
{
switch (sub)
{
case RoleSubAlignment.NeutralKilling: _neutralKillingDrafted++; break;
case RoleSubAlignment.NeutralEvil: _neutralEvilDrafted++; break;
case RoleSubAlignment.NeutralBenign: _neutralBenignDrafted++; break;
case RoleSubAlignment.NeutralOutlier: _neutralOutlierDrafted++; break;
}
}

DraftModePlugin.Logger.LogInfo(
$"[DraftManager] '{roleName}' drafted ({sub}). " +
$"Imp: {_impostorsDrafted}/{MaxImpostors}, " +
$"NK: {_neutralKillingDrafted}/{MaxNeutralKilling}, " +
$"NE: {_neutralEvilDrafted}/{MaxNeutralEvil}, " +
$"NB: {_neutralBenignDrafted}/{MaxNeutralBenign}, " +
$"NO: {_neutralOutlierDrafted}/{MaxNeutralOutlier}");

            CurrentTurn++;
            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();

                if (ShowRecap)
                    DraftNetworkHelper.BroadcastRecap(BuildRecapMessage());
                else
                    DraftNetworkHelper.BroadcastRecap("<color=#FFD700><b>── DRAFT COMPLETE ──</b></color>");
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
                string color = RoleCategory.GetFaction(s.ChosenRole) switch
                {
                    RoleFaction.Impostor => "#FF4444",
                    RoleFaction.Neutral => "#AA44FF",
                    _ => "#4BD7E4"
                };
                sb.AppendLine($"Player {s.SlotNumber}: <color={color}>{s.ChosenRole}</color>");
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

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }
    }
}
