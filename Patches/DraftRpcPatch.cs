using DraftModeTOUM.Managers;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Patches
{
    public enum DraftRpc : byte
    {
        SubmitPick = 220,
        AnnounceTurn = 221,
        StartDraft = 223,
        Recap = 224,
        SlotNotify = 225
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
                    if (!AmongUsClient.Instance.AmHost)
                        HandleStartDraft(reader);
                    else
                        ConsumeStartDraftPacket(reader);
                    return false;

                case DraftRpc.AnnounceTurn:
                    if (!AmongUsClient.Instance.AmHost)
                        HandleAnnounceTurn(reader);
                    else
                        ConsumeAnnounceTurnPacket(reader);
                    return false;

                case DraftRpc.Recap:
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        bool showRecap = reader.ReadBoolean();
                        if (showRecap)
                        {
                            int count = reader.ReadInt32();
                            var entries = new List<RecapEntry>();
                            for (int i = 0; i < count; i++)
                            {
                                int slot = reader.ReadInt32();
                                string role = reader.ReadString();
                                entries.Add(new RecapEntry(slot, role));
                            }
                            DraftRecapOverlay.Show(entries);
                        }

                        // Maintain the background and trigger the exit delay
                        DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
                        DraftManager.Reset(cancelledBeforeCompletion: false);
                        DraftManager.TriggerEndDraftSequence();
                    }
                    else
                    {
                        // Host safely ignores its own broadcast
                        bool showRecap = reader.ReadBoolean();
                        if (showRecap)
                        {
                            int count = reader.ReadInt32();
                            for (int i = 0; i < count; i++)
                            {
                                reader.ReadInt32();
                                reader.ReadString();
                            }
                        }
                    }
                    return false;

                case DraftRpc.SlotNotify:
                    if (!AmongUsClient.Instance.AmHost)
                    {
                        int count = reader.ReadInt32();
                        var pidList = new List<byte>();
                        var slotList = new List<int>();
                        for (int i = 0; i < count; i++)
                        {
                            pidList.Add(reader.ReadByte());
                            slotList.Add(reader.ReadInt32());
                        }
                        DraftManager.SetDraftStateFromHost(count, pidList, slotList);
                        DraftUiManager.RefreshTurnList();
                    }
                    else
                    {
                        int count = reader.ReadInt32();
                        for (int i = 0; i < count; i++) { reader.ReadByte(); reader.ReadInt32(); }
                    }
                    return false;

                default:
                    return true;
            }
        }

        private static void ConsumeStartDraftPacket(MessageReader reader)
        {
            int totalSlots = reader.ReadInt32();
            int listCount = reader.ReadInt32();
            for (int i = 0; i < listCount; i++) { reader.ReadByte(); reader.ReadInt32(); }
        }

        private static void ConsumeAnnounceTurnPacket(MessageReader reader)
        {
            reader.ReadInt32(); // turnNumber
            reader.ReadInt32(); // slot
            reader.ReadByte();  // pickerId
            int roleCount = reader.ReadInt32();
            for (int i = 0; i < roleCount; i++) reader.ReadString();
        }

        private static void HandleStartDraft(MessageReader reader)
        {
            int totalSlots = reader.ReadInt32();
            int listCount = reader.ReadInt32();
            List<byte> pids = new List<byte>();
            List<int> slots = new List<int>();
            for (int i = 0; i < listCount; i++) { pids.Add(reader.ReadByte()); slots.Add(reader.ReadInt32()); }
            DraftManager.SetDraftStateFromHost(totalSlots, pids, slots);
            DraftUiManager.CloseAll();
        }

        private static void HandleAnnounceTurn(MessageReader reader)
        {
            int turnNumber = reader.ReadInt32();
            int slot = reader.ReadInt32();
            byte pickerId = reader.ReadByte();
            int roleCount = reader.ReadInt32();
            var roles = new string[roleCount];
            for (int i = 0; i < roleCount; i++) roles[i] = reader.ReadString();

            DraftManager.SetClientTurn(turnNumber, slot);
            DisplayTurnAnnouncement(slot, pickerId, roles);
        }

        public static void HandleAnnounceTurnLocal(int slot, byte pickerId, List<string> roles)
        {
            DisplayTurnAnnouncement(slot, pickerId, roles.ToArray());
        }

        private static void DisplayTurnAnnouncement(int slot, byte pickerId, string[] roles)
        {
            if (PlayerControl.LocalPlayer.PlayerId == pickerId)
            {
                DraftUiManager.ShowPicker(roles.ToList());
            }
            else
            {
                DraftUiManager.CloseAll();
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
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(totalSlots);
            writer.Write(pids.Count);
            for (int i = 0; i < pids.Count; i++) { writer.Write(pids[i]); writer.Write(slots[i]); }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void SendTurnAnnouncement(int slot, byte playerId, List<string> roles, int turnNumber)
        {
            DraftRpcPatch.HandleAnnounceTurnLocal(slot, playerId, roles);

            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.AnnounceTurn,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(turnNumber);
            writer.Write(slot);
            writer.Write(playerId);
            writer.Write(roles.Count);
            foreach (var r in roles) writer.Write(r);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastSlotNotifications(Dictionary<byte, int> pidToSlot)
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.SlotNotify,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(pidToSlot.Count);
            foreach (var kvp in pidToSlot)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastRecap(List<RecapEntry> entries, bool showRecap)
        {
            // Show it locally for the host
            if (showRecap)
            {
                DraftRecapOverlay.Show(entries);
            }

            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.Recap,
                Hazel.SendOption.Reliable,
                -1);

            writer.Write(showRecap);
            if (showRecap)
            {
                writer.Write(entries.Count);
                foreach (var entry in entries)
                {
                    writer.Write(entry.SlotNumber);
                    writer.Write(entry.RoleName);
                }
            }

            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
}