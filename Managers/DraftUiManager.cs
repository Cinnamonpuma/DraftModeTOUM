using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using DraftModeTOUM.Patches;
using MiraAPI.LocalSettings;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    /// <summary>
    /// Manages the lifetime of every draft-related UI panel so they can all
    /// be closed in one call (e.g. on disconnect or game-start).
    /// Also exposes helpers used by DraftRpcPatch and DraftSelectionMinigame.
    /// </summary>
    public static class DraftUiManager
    {
        private static DraftCircleMinigame? _circleMinigame;
        private static List<ushort>         _lastOfferedRoleIds = new();

        private static bool UseCircle
        {
            get
            {
                var local = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
                if (local != null && local.OverrideUiStyle.Value)
                    return local.UseCircleStyle.Value;
                return MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance.UseCircleStyle;
            }
        }

        // ── Entry points ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called on the client whose turn it is. roleIds come from the host
        /// but are resolved locally — the host's name/rename never reaches the UI.
        /// </summary>
        public static void ShowPicker(List<ushort> roleIds)
        {
            if (HudManager.Instance == null || roleIds == null || roleIds.Count == 0) return;
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

            if (UseCircle)
                ShowCircle(roleIds);
            else
                DraftScreenController.Show(roleIds.ToArray());
        }

        public static void RefreshTurnList()
        {
            DraftStatusOverlay.Refresh();

            if (UseCircle && _circleMinigame != null)
            {
                bool alive = false;
                try { alive = _circleMinigame.gameObject != null && _circleMinigame.gameObject.activeSelf; } catch { }
                if (alive)
                    _circleMinigame.RefreshTurnList();
            }
        }

        public static void CloseAll()
        {
            DraftScreenController.Hide();

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
                        bool exists = false;
                        try { exists = circle.gameObject != null; } catch { }
                        if (exists) UnityEngine.Object.Destroy(circle.gameObject);
                    }
                }
                catch { }
            }

            if (DraftManager.IsDraftActive)
                DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        // ── Card building ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds DraftRoleCard list from role IDs sent by the host.
        /// Every piece of display data (name, team, icon, color) is resolved
        /// entirely from the client's local RoleManager — the host string never appears.
        /// </summary>
        public static List<DraftRoleCard> BuildCards(List<ushort> roleIds)
        {
            var cards = new List<DraftRoleCard>();
            for (int i = 0; i < roleIds.Count; i++)
            {
                ushort id   = roleIds[i];
                var    role = ResolveRole(id);

                string displayName = role?.NiceName ?? $"Role {id}";
                string team = GetTeamLabel(role) ?? "Unknown";
                Sprite icon = GetRoleIcon(role);
                Color color = GetRoleColor(role);
                // Everything resolved locally — host rename has zero effect
                string displayName = role?.NiceName          ?? $"Role {id}";
                string team        = GetTeamLabel(role)       ?? "Unknown";
                Sprite icon        = GetRoleIcon(role);
                Color  color       = GetRoleColor(role);

                cards.Add(new DraftRoleCard(displayName, team, icon, color, i));
            }

            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard(
                    "Random", "Random",
                    TouRoleIcons.RandomAny.LoadAsset(),
                    Color.white,
                    roleIds.Count));

            return cards;
        }

        // ── Role resolution ──────────────────────────────────────────────────────

        public static RoleBehaviour? ResolveRole(ushort roleId)
        {
            try { return RoleManager.Instance?.GetRole((RoleTypes)roleId); }
            catch { return null; }
        }

        public static string GetTeamLabel(RoleBehaviour? role)
        {
            if (role == null) return "Unknown";
            try { return MiscUtils.GetParsedRoleAlignment(role); } catch { }
            return RoleCategory.GetFactionFromRole(role) switch
            {
                RoleFaction.Impostor       => "Impostor",
                RoleFaction.NeutralKilling => "Neutral Killing",
                RoleFaction.Neutral        => "Neutral",
                _                          => "Crewmate"
            };
        }

        public static Sprite GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
            {
                try { return cr.Configuration.Icon.LoadAsset(); } catch { }
            }
            if (role?.RoleIconSolid != null) return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        public static Color GetRoleColor(RoleBehaviour? role)
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            if (role != null)           return role.TeamColor;
            return Color.white;
        }

        // ── Circle UI ────────────────────────────────────────────────────────────

        private static void ShowCircle(List<ushort> roleIds)
        {
            _lastOfferedRoleIds = roleIds;
            EnsureCircleMinigame();
            if (_circleMinigame == null) return;
            var cards = BuildCards(roleIds);
            _circleMinigame.Open(cards, OnPickSelected);
        }

        private static void EnsureCircleMinigame()
        {
            if (_circleMinigame != null)
            {
                bool destroyed = false;
                try { destroyed = _circleMinigame.gameObject == null; } catch { destroyed = true; }
                if (destroyed) _circleMinigame = null;
            }
            if (_circleMinigame == null)
                _circleMinigame = DraftCircleMinigame.Create();
        }

        private static void OnPickSelected(int index)
        {
            ushort? pickedId = (index < _lastOfferedRoleIds.Count) ? _lastOfferedRoleIds[index] : (ushort?)null;
            DraftModePlugin.Logger.LogInfo($"[DraftUiManager] OnPickSelected index={index} roleId={pickedId}");
            if (pickedId.HasValue)
                DraftStatusOverlay.NotifyLocalPlayerPicked(pickedId.Value);
            var circle = _circleMinigame;
            _circleMinigame = null;
            try { circle?.Close(); } catch { }
            DraftNetworkHelper.SendPickToHost(index);
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        public static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "");
    }
}
