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
        CancelDraft  = 229,
        EndDraft     = 230
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
                                string role = reader.ReadString(); 
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
                            
                            if (state.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                                DraftStatusOverlay.NotifyLocalPlayerPicked(roleId);
                        }
                    }
                    else
                    {
                        reader.ReadInt32(); reader.ReadUInt16(); 
                    }
                    return false;
                case DraftRpc.PickerReady:
                    
                    if (AmongUsClient.Instance.AmHost)
                        DraftManager.NotifyPickerReady(__instance.PlayerId);
                    return false;

                case DraftRpc.ForceRole:
                    
                    if (AmongUsClient.Instance.AmHost)
                    {
                        string roleName = reader.ReadString();
                        byte targetId   = reader.ReadByte();
                        DraftManager.SetForcedDraftRole(roleName, targetId);
                        LoggingSystem.Debug($"[DraftRpcPatch] Host received ForceRole '{roleName}' for player {targetId}");
                    }
                    return false;

                case DraftRpc.CancelDraft:
                    
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        DraftUiManager.CloseAll();
                        DraftStatusOverlay.SetState(OverlayState.Hidden);
                        DraftManager.Reset(cancelledBeforeCompletion: true);
                    }
                    return false;
                case DraftRpc.EndDraft:
                    
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
            reader.ReadInt32(); 
            reader.ReadInt32(); 
            reader.ReadByte();  
            int roleCount = reader.ReadInt32();
            for (int i = 0; i < roleCount; i++) reader.ReadUInt16(); 
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
            byte localId = PlayerControl.LocalPlayer.PlayerId;
            if (localId == pickerId)
            {
                DraftUiManager.ShowPicker(roleIds.ToList());
            }
            else
            {
                var localState = DraftManager.GetStateForPlayer(localId);
                if (localState == null || !localState.IsPickingNow)
                    DraftUiManager.CloseAll();
            }
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

            DraftManager.HandlePlayerDisconnected(dcPlayerId);
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
            foreach (var id in roleIds) writer.Write(id);  
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
            
            var state = DraftManager.GetStateForSlot(slot);
            if (state != null)
            {
                state.ChosenRoleId = roleId;
                state.HasPicked    = true;
                
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
                
                DraftManager.NotifyPickerReady(PlayerControl.LocalPlayer.PlayerId);
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

        
        
        
        
        
        public static void SendForceRoleToHost(string roleName, byte targetId)
        {
            byte myId = PlayerControl.LocalPlayer.PlayerId;
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.SetForcedDraftRole(roleName, targetId);
            }
            else
            {
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId,
                    (byte)DraftRpc.ForceRole,
                    Hazel.SendOption.Reliable,
                    AmongUsClient.Instance.HostId);
                writer.Write(roleName);
                writer.Write(targetId);
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
            
            DraftManager.Reset(cancelledBeforeCompletion: true);
            DraftManager.SendChatLocal("<color=#FFD700>Draft has been cancelled by the host.</color>");

            
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.EndDraft,
                Hazel.SendOption.Reliable, -1);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
}











