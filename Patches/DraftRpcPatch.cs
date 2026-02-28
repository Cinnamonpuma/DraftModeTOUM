using DraftModeTOUM.Managers;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Patches
{
    public enum DraftRpc : byte
    {
        SubmitPick   = 220,
        AnnounceTurn = 221,
        StartDraft   = 223,
        Recap        = 224,
        SlotNotify   = 225,
        PickerReady  = 226,
        PickConfirmed = 227,
        ForceRole    = 228,
        CancelDraft  = 229
        PickerReady    = 226,
        PickConfirmed  = 227,
        ForceRole      = 228,
        EndDraft       = 229
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    public static class DraftRpcPatch
    {
        public static bool Prefix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            switch ((DraftRpc)callId)
            {
                case DraftRpc.SubmitPick:
                    if (AmongUsClient.Instance.AmHost)
                        DraftManager.SubmitPick(__instance.PlayerId, reader.ReadByte());
                    return false;

                case DraftRpc.StartDraft:
                    if (!AmongUsClient.Instance.AmHost) HandleStartDraft(reader);
                    else                                ConsumeStartDraftPacket(reader);
                    return false;

                case DraftRpc.AnnounceTurn:
                    if (!AmongUsClient.Instance.AmHost) HandleAnnounceTurn(reader);
                    else                                ConsumeAnnounceTurnPacket(reader);
                    return false;

                case DraftRpc.Recap:
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        bool show = reader.ReadBoolean();
                        if (show)
                        {
                            int count   = reader.ReadInt32();
                            var entries = new List<RecapEntry>();
                            for (int i = 0; i < count; i++)
                            {
                                int    slot = reader.ReadInt32();
                                string role = reader.ReadString(); // recap uses NiceName resolved on host
                                entries.Add(new RecapEntry(slot, role));
                            }
                            DraftRecapOverlay.Show(entries);
                        }
                        DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
                        DraftManager.Reset(cancelledBeforeCompletion: false);
                        DraftManager.TriggerEndDraftSequence();
                    }
                    else
                    {
                        bool show = reader.ReadBoolean();
                        if (show)
                        {
                            int count = reader.ReadInt32();
                            for (int i = 0; i < count; i++) { reader.ReadInt32(); reader.ReadString(); }
                        }
                    }
                    return false;

                case DraftRpc.SlotNotify:
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        int count = reader.ReadInt32();
                        var pids  = new List<byte>();
                        var slots = new List<int>();
                        for (int i = 0; i < count; i++) { pids.Add(reader.ReadByte()); slots.Add(reader.ReadInt32()); }
                        DraftManager.SetDraftStateFromHost(count, pids, slots);
                        DraftUiManager.RefreshTurnList();
                    }
                    else
                    {
                        int count = reader.ReadInt32();
                        for (int i = 0; i < count; i++) { reader.ReadByte(); reader.ReadInt32(); }
                    }
                    return false;

                case DraftRpc.PickConfirmed:
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        int  slot   = reader.ReadInt32();
                        var  roleId = reader.ReadUInt16();
                        var  state  = DraftManager.GetStateForSlot(slot);
                        if (state != null)
                        {
                            state.ChosenRoleId = roleId;
                            state.HasPicked    = true;
                            // If this is our own pick, show the role card
                            if (state.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                                DraftStatusOverlay.NotifyLocalPlayerPicked(roleId);
                        }
                    }
                    else
                    {
                        reader.ReadInt32(); reader.ReadUInt16(); // consume
                    }
                    return false;

                case DraftRpc.PickerReady:
                    // Client signals their animation is done — host starts the turn timer
                    if (AmongUsClient.Instance.AmHost)
                        DraftManager.StartTurnTimer();
                    return false;

                case DraftRpc.ForceRole:
                    // Non-host client relays their forced role to the host
                    if (AmongUsClient.Instance.AmHost)
                    {
                        string roleName = reader.ReadString();
                        byte targetId   = reader.ReadByte();
                        DraftManager.SetForcedDraftRole(roleName, targetId);
                        LoggingSystem.Debug($"[DraftRpcPatch] Host received ForceRole '{roleName}' for player {targetId}");
                    }
                    return false;

                case DraftRpc.CancelDraft:
                    // Clients hide all UI and reset state when host cancels
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        DraftUiManager.CloseAll();
                        DraftStatusOverlay.SetState(OverlayState.Hidden);
                        DraftManager.Reset(cancelledBeforeCompletion: true);
                    }
                case DraftRpc.EndDraft:
                    // Host broadcasts draft end to all clients
                    DraftManager.Reset(cancelledBeforeCompletion: true);
                    DraftManager.SendChatLocal("<color=#FFD700>Draft has been cancelled by the host.</color>");
                    return false;

                default:
                    return true;
            }
        }

        private static void ConsumeStartDraftPacket(MessageReader reader)
        {
            int total = reader.ReadInt32();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) { reader.ReadByte(); reader.ReadInt32(); }
        }

        private static void ConsumeAnnounceTurnPacket(MessageReader reader)
        {
            reader.ReadInt32(); // turnNumber
            reader.ReadInt32(); // slot
            reader.ReadByte();  // pickerId
            int roleCount = reader.ReadInt32();
            for (int i = 0; i < roleCount; i++) reader.ReadUInt16(); // role IDs
        }

        private static void HandleStartDraft(MessageReader reader)
        {
            int total = reader.ReadInt32();
            int count = reader.ReadInt32();
            var pids  = new List<byte>();
            var slots = new List<int>();
            for (int i = 0; i < count; i++) { pids.Add(reader.ReadByte()); slots.Add(reader.ReadInt32()); }
            DraftManager.SetDraftStateFromHost(total, pids, slots);
            DraftUiManager.CloseAll();
        }

        private static void HandleAnnounceTurn(MessageReader reader)
        {
            int    turnNumber = reader.ReadInt32();
            int    slot       = reader.ReadInt32();
            byte   pickerId   = reader.ReadByte();
            int    roleCount  = reader.ReadInt32();
            var    roleIds    = new ushort[roleCount];
            for (int i = 0; i < roleCount; i++) roleIds[i] = reader.ReadUInt16();

            DraftManager.SetClientTurn(turnNumber, slot);
            DisplayTurnAnnouncement(slot, pickerId, roleIds);
        }

        public static void HandleAnnounceTurnLocal(int slot, byte pickerId, List<ushort> roleIds)
        {
            DisplayTurnAnnouncement(slot, pickerId, roleIds.ToArray());
        }

        private static void DisplayTurnAnnouncement(int slot, byte pickerId, ushort[] roleIds)
        {
            if (PlayerControl.LocalPlayer.PlayerId == pickerId)
                DraftUiManager.ShowPicker(roleIds.ToList());
            else
                DraftUiManager.CloseAll();
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    public static class PlayerLeftDraftPatch
    {
        public static void Postfix(AmongUsClient __instance, InnerNet.ClientData data)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!DraftManager.IsDraftActive) return;
            if (data?.Character == null) return;

            byte dcPlayerId = data.Character.PlayerId;
            DraftModePlugin.Logger.LogInfo($"[DraftManager] Player {dcPlayerId} disconnected during draft");

            // If it's the current picker's turn, auto-pick immediately for them
            var picker = DraftManager.GetCurrentPickerState();
            if (picker != null && picker.PlayerId == dcPlayerId)
            {
                DraftModePlugin.Logger.LogInfo($"[DraftManager] DC'd player was current picker — auto-picking");
                DraftManager.SubmitPick(dcPlayerId, int.MaxValue); // MaxValue forces PickFullRandom
                return;
            }

            // If a future slot belongs to the DC'd player, mark them as picked
            // with a random role so their turn is skipped automatically when reached
            var dcState = DraftManager.GetStateForPlayer(dcPlayerId);
            if (dcState != null && !dcState.HasPicked)
            {
                DraftModePlugin.Logger.LogInfo($"[DraftManager] Marking DC'd player slot {dcState.SlotNumber} for auto-skip");
                dcState.IsDisconnected = true;
            }
        }
    }

    public static class DraftNetworkHelper
    {
        public static void SendPickToHost(int index)
        {
            DraftUiManager.CloseAll();
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.SubmitPick(PlayerControl.LocalPlayer.PlayerId, index);
            }
            else
            {
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId,
                    (byte)DraftRpc.SubmitPick,
                    Hazel.SendOption.Reliable,
                    AmongUsClient.Instance.HostId);
                writer.Write((byte)index);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        public static void BroadcastDraftStart(int totalSlots, List<byte> pids, List<int> slots)
        {
            DraftManager.SetDraftStateFromHost(totalSlots, pids, slots);
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.StartDraft,
                Hazel.SendOption.Reliable, -1);
            writer.Write(totalSlots);
            writer.Write(pids.Count);
            for (int i = 0; i < pids.Count; i++) { writer.Write(pids[i]); writer.Write(slots[i]); }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void SendTurnAnnouncement(int slot, byte playerId, List<ushort> roleIds, int turnNumber)
        {
            DraftRpcPatch.HandleAnnounceTurnLocal(slot, playerId, roleIds);

            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.AnnounceTurn,
                Hazel.SendOption.Reliable, -1);
            writer.Write(turnNumber);
            writer.Write(slot);
            writer.Write(playerId);
            writer.Write(roleIds.Count);
            foreach (var id in roleIds) writer.Write(id);  // ushort, not string
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastSlotNotifications(Dictionary<byte, int> pidToSlot)
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.SlotNotify,
                Hazel.SendOption.Reliable, -1);
            writer.Write(pidToSlot.Count);
            foreach (var kvp in pidToSlot) { writer.Write(kvp.Key); writer.Write(kvp.Value); }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastPickConfirmed(int slot, ushort roleId)
        {
            // Update host state directly
            var state = DraftManager.GetStateForSlot(slot);
            if (state != null)
            {
                state.ChosenRoleId = roleId;
                state.HasPicked    = true;
                // If the host is the picker, show their role card now
                if (state.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                    DraftStatusOverlay.NotifyLocalPlayerPicked(roleId);
            }

            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.PickConfirmed,
                Hazel.SendOption.Reliable, -1);
            writer.Write(slot);
            writer.Write(roleId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void NotifyPickerReady()
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host is the picker — start timer directly
                DraftManager.StartTurnTimer();
            }
            else
            {
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId,
                    (byte)DraftRpc.PickerReady,
                    Hazel.SendOption.Reliable,
                    AmongUsClient.Instance.HostId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        /// <summary>
        /// Called by a non-host client when their heartbeat returns a forcedRole.
        /// Sends the role name + their own player ID to the host so the host can inject it.
        /// If we ARE the host, just call DraftManager directly.
        /// </summary>
        public static void SendForceRoleToHost(string roleName)
        {
            byte myId = PlayerControl.LocalPlayer.PlayerId;
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.SetForcedDraftRole(roleName, myId);
            }
            else
            {
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId,
                    (byte)DraftRpc.ForceRole,
                    Hazel.SendOption.Reliable,
                    AmongUsClient.Instance.HostId);
                writer.Write(roleName);
                writer.Write(myId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        public static void BroadcastCancelDraft()
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.CancelDraft,
                Hazel.SendOption.Reliable, -1);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastRecap(List<RecapEntry> entries, bool showRecap)
        {
            if (showRecap) DraftRecapOverlay.Show(entries);
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.Recap,
                Hazel.SendOption.Reliable, -1);
            writer.Write(showRecap);
            if (showRecap)
            {
                writer.Write(entries.Count);
                foreach (var e in entries) { writer.Write(e.SlotNumber); writer.Write(e.RoleName); }
            }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastDraftEnd()
        {
            // Host resets locally
            DraftManager.Reset(cancelledBeforeCompletion: true);
            DraftManager.SendChatLocal("<color=#FFD700>Draft has been cancelled by the host.</color>");

            // Broadcast to all clients
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.EndDraft,
                Hazel.SendOption.Reliable, -1);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
}
