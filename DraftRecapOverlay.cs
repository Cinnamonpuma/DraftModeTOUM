using UnityEngine;
using System.Collections.Generic;

namespace DraftModeTOUM
{
    // Recap UI removed — recap is shown in chat via DraftManager.SendChatLocal
    public static class DraftRecapOverlay
    {
        public static void Show(List<RecapEntry> entries) { }
        public static void Hide() { }
    }

    public sealed class RecapEntry
    {
        public string PlayerName { get; }
        public string RoleName   { get; }
        public Color  RoleColor  { get; }

        public RecapEntry(string playerName, string roleName, Color roleColor)
        {
            PlayerName = playerName;
            RoleName   = roleName;
            RoleColor  = roleColor;
        }
    }
}
