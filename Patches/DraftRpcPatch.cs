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
        Recap = 224
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
                    return false;

                case DraftRpc.AnnounceTurn:
                    if (!AmongUsClient.Instance.AmHost)
                        HandleAnnounceTurn(reader);
                    return false;

                case DraftRpc.Recap:

                    if (!AmongUsClient.Instance.AmHost)
                        DraftManager.SendChatLocal(reader.ReadString());
                    return false;

                default:
                    return true;
            }
        }

        private static void HandleStartDraft(MessageReader reader)
        {
            int totalSlots = reader.ReadInt32();
            int listCount = reader.ReadInt32();
            List<byte> pids = new List<byte>();
            List<int> slots = new List<int>();
            for (int i = 0; i < listCount; i++) { pids.Add(reader.ReadByte()); slots.Add(reader.ReadInt32()); }
            DraftManager.SetDraftStateFromHost(totalSlots, pids, slots);
            DraftManager.SendChatLocal($"<color=#FF4444><b>DRAFT MODE ENABLED!</b></color>");
        }

 
        private static void HandleAnnounceTurn(MessageReader reader)
        {
            int turnNumber = reader.ReadInt32(); 
            int slot = reader.ReadInt32();
            byte pickerId = reader.ReadByte();
            string[] roles = { reader.ReadString(), reader.ReadString(), reader.ReadString() };

            DraftManager.SetClientTurn(turnNumber);

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
                DraftManager.SendChatLocal($"<color=#00FF00><b>YOUR TURN!</b></color>");
                for (int i = 0; i < 3; i++)
                    DraftManager.SendChatLocal($"{i + 1}. <color=#4BD7E4>{roles[i]}</color>");
                DraftManager.SendChatLocal($"4. <color=#3CCC3C>RANDOM</color>");
            }
            else
            {
                DraftManager.SendChatLocal($"<color=yellow>Player {slot} is picking...</color>");
            }
        }
    }

    public static class DraftNetworkHelper
    {
        public static void SendPickToHost(int index)
        {
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
            DraftManager.SendChatLocal($"<color=#FF4444><b>DRAFT MODE ENABLED!</b></color>");


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
            foreach (var r in roles) writer.Write(r);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void BroadcastRecap(string recapText)
        {
            DraftManager.SendChatLocal(recapText);


            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)DraftRpc.Recap,
                Hazel.SendOption.Reliable,
                -1);
            writer.Write(recapText);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
}