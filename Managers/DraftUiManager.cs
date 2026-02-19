using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Patches;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class DraftUiManager
    {
        private static DraftSelectionMinigame? _minigame;

        public static void ShowPicker(List<string> roles)
        {
            if (HudManager.Instance == null || roles == null || roles.Count == 0) return;

            EnsureMinigame();
            if (_minigame == null)
            {
                DraftModePlugin.Logger.LogError("[DraftUiManager] Cannot show picker — minigame failed to create.");
                return;
            }
            var cards = BuildCards(roles);
            _minigame.Open(cards, OnPickSelected);
        }

        // Called whenever turn state changes so non-pickers still see updated list
        public static void RefreshTurnList()
        {
            if (_minigame == null || _minigame.gameObject == null) return;
            if (!_minigame.gameObject.activeSelf) return;
            _minigame.RefreshTurnList();
        }

        public static void CloseAll()
        {
            if (_minigame == null) return;
            try
            {
                if (_minigame.gameObject != null && _minigame.gameObject.activeSelf)
                    _minigame.Close();
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftUiManager] CloseAll exception (safe to ignore): {ex.Message}");
            }
            // Always null the reference so EnsureMinigame recreates it next time
            _minigame = null;
        }

        private static void EnsureMinigame()
        {
            // Treat destroyed Unity objects as null
            if (_minigame != null)
            {
                bool destroyed = false;
                try { destroyed = (_minigame.gameObject == null); }
                catch { destroyed = true; }
                if (destroyed) _minigame = null;
            }

            if (_minigame == null)
            {
                _minigame = DraftSelectionMinigame.Create();
                if (_minigame == null)
                    DraftModePlugin.Logger.LogError("[DraftUiManager] DraftSelectionMinigame.Create() returned null — asset may not be loaded!");
            }
        }

        private static void OnPickSelected(int index)
        {
            DraftNetworkHelper.SendPickToHost(index);
        }

        private static List<DraftRoleCard> BuildCards(List<string> roles)
        {
            var cards = new List<DraftRoleCard>();

            for (int i = 0; i < roles.Count; i++)
            {
                string roleName = roles[i];
                var role  = FindRoleByName(roleName);
                string teamName = role != null ? MiscUtils.GetParsedRoleAlignment(role) : "Unknown";
                var icon  = GetRoleIcon(role);
                var color = GetRoleColor(role);
                cards.Add(new DraftRoleCard(roleName, teamName, icon, color, i));
            }

            cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, 3));
            return cards;
        }

        private static RoleBehaviour? FindRoleByName(string roleName)
        {
            if (RoleManager.Instance == null) return null;
            string normalized = Normalize(roleName);
            return RoleManager.Instance.AllRoles.ToArray()
                .FirstOrDefault(r => Normalize(r.GetRoleName()) == normalized);
        }

        private static Sprite? GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole customRole && customRole.Configuration.Icon != null)
                return customRole.Configuration.Icon.LoadAsset();
            if (role?.RoleIconSolid != null)
                return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        private static Color GetRoleColor(RoleBehaviour? role)
        {
            if (role is ICustomRole customRole) return customRole.RoleColor;
            return role != null ? role.TeamColor : Color.white;
        }

        private static string Normalize(string roleName) =>
            (roleName ?? string.Empty).ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
    }
}
