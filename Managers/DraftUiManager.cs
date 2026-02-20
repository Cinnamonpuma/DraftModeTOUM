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
                DraftScreenController.Show(roles.ToArray());
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

            // Close circle style — grab ref and null immediately so re-entrant calls are no-ops
            var circle = _circleMinigame;
            _circleMinigame = null;
            if (circle != null)
            {
                try
                {
                    bool alive = false;
                    try { alive = circle.gameObject != null && circle.gameObject.activeSelf; } catch { }
                    if (alive) circle.Close();
                    else
                    {
                        // Was never opened (still inactive) — just destroy it
                        bool exists = false;
                        try { exists = circle.gameObject != null; } catch { }
                        if (exists) UnityEngine.Object.Destroy(circle.gameObject);
                    }
                }
                catch (Exception ex)
                {
                    DraftModePlugin.Logger.LogWarning($"[DraftUiManager] Circle CloseAll exception (safe to ignore): {ex.Message}");
                }
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

        private static void OnPickSelected(int index)
        {
            // Close and destroy the circle immediately, THEN send pick
            // (SendPickToHost → CloseAll will see null and skip circle cleanly)
            var circle = _circleMinigame;
            _circleMinigame = null;
            try { circle?.Close(); } catch { }
            DraftNetworkHelper.SendPickToHost(index);
        }

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
            // Random card only added when host setting is on; index = roles.Count
            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, roles.Count));
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
