using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Patches;
using MiraAPI.LocalSettings;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class DraftUiManager
    {
        // Circle-style minigame instance (only used when UseCircleStyle is on)
        private static DraftCircleMinigame? _circleMinigame;

        private static bool UseCircle
        {
            get
            {
                // Local override takes priority — lets each player choose their own UI
                var local = MiraAPI.LocalSettings.LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
                if (local != null && local.OverrideUiStyle.Value)
                    return local.UseCircleStyle.Value;

                // Fall back to whatever the host has set
                return MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance.UseCircleStyle;
            }
        }

        public static void ShowPicker(List<string> roles)
        {
            if (HudManager.Instance == null || roles == null || roles.Count == 0) return;

            DraftStatusOverlay.Hide();

            if (UseCircle)
            {
                ShowCircle(roles);
            }
            else
            {
                // Cards style — pass up to 3 roles; DraftScreenController adds the Random card
                var arr = roles.Take(3).ToArray();
                DraftScreenController.Show(arr);
            }
        }

        public static void RefreshTurnList()
        {
            if (UseCircle && _circleMinigame != null &&
                _circleMinigame.gameObject != null && _circleMinigame.gameObject.activeSelf)
            {
                _circleMinigame.RefreshTurnList();
            }
            // Cards style has no turn list panel
        }

        public static void CloseAll()
        {
            // Close cards style
            DraftScreenController.Hide();

            // Close circle style
            if (_circleMinigame != null)
            {
                try
                {
                    if (_circleMinigame.gameObject != null && _circleMinigame.gameObject.activeSelf)
                        _circleMinigame.Close();
                }
                catch (Exception ex)
                {
                    DraftModePlugin.Logger.LogWarning($"[DraftUiManager] Circle CloseAll exception (safe to ignore): {ex.Message}");
                }
                _circleMinigame = null;
            }

            if (DraftManager.IsDraftActive)
                DraftStatusOverlay.Show();
        }

        // ── Circle helpers ────────────────────────────────────────────────────

        private static void ShowCircle(List<string> roles)
        {
            EnsureCircleMinigame();
            if (_circleMinigame == null)
            {
                DraftModePlugin.Logger.LogError("[DraftUiManager] Cannot show circle — minigame failed to create.");
                return;
            }
            var cards = BuildCards(roles);
            _circleMinigame.Open(cards, OnPickSelected);
        }

        private static void EnsureCircleMinigame()
        {
            if (_circleMinigame != null)
            {
                bool destroyed = false;
                try { destroyed = (_circleMinigame.gameObject == null); }
                catch { destroyed = true; }
                if (destroyed) _circleMinigame = null;
            }
            if (_circleMinigame == null)
                _circleMinigame = DraftCircleMinigame.Create();
        }

        private static void OnPickSelected(int index) => DraftNetworkHelper.SendPickToHost(index);

        private static List<DraftRoleCard> BuildCards(List<string> roles)
        {
            var cards = new List<DraftRoleCard>();
            for (int i = 0; i < roles.Count; i++)
            {
                string roleName = roles[i];
                var role   = FindRoleByName(roleName);
                string team = role != null ? MiscUtils.GetParsedRoleAlignment(role) : "Unknown";
                var icon  = GetRoleIcon(role);
                var color = GetRoleColor(role);
                cards.Add(new DraftRoleCard(roleName, team, icon, color, i));
            }
            // Random card always last at index 3
            cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, 3));
            return cards;
        }

        private static RoleBehaviour? FindRoleByName(string roleName)
        {
            if (RoleManager.Instance == null) return null;
            string normalized = Normalize(roleName);
            return RoleManager.Instance.AllRoles.ToArray()
                .FirstOrDefault(r => Normalize(r.NiceName) == normalized);
        }

        private static Sprite? GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
                return cr.Configuration.Icon.LoadAsset();
            if (role?.RoleIconSolid != null)
                return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        private static Color GetRoleColor(RoleBehaviour? role)
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            return role != null ? role.TeamColor : Color.white;
        }

        private static string Normalize(string s) =>
            (s ?? string.Empty).ToLowerInvariant().Replace(" ", "").Replace("-", "");
    }
}
