using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public sealed class DraftRoleCard
    {
        public string  RoleName { get; }
        public string  TeamName { get; }
        public Sprite? Icon     { get; }
        public Color   Color    { get; }
        public int     Index    { get; }

        public DraftRoleCard(string roleName, string teamName, Sprite? icon, Color color, int index)
        {
            RoleName = roleName;
            TeamName = teamName;
            Icon     = icon;
            Color    = color;
            Index    = index;
        }
    }
}
