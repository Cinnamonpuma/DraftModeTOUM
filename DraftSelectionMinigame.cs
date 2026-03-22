using System;
using System.Collections;
using System.Collections.Generic;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using Reactor.Utilities;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using UnityEngine.Events;

namespace DraftModeTOUM
{
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject _screenRoot;
        private ushort[] _offeredRoleIds;
        private bool _hasPicked;
        private TextMeshPro _statusText;

        private const string PrefabName = "SelectRoleGame";
        private const float TeamNameFontSize = 3.8f;


       private static float CardScaleForCount(int count) => count switch
        {
            <= 3 => 0.55f,
            <= 4 => 0.55f,
            <= 5 => 0.55f,
            <= 6 => 0.55f,
            <= 7 => 0.55f,
            <= 8 => 0.55f,
            _    => 0.55f,
        };

        // Spacing (in layout group units) between card edges — negative = overlap
        // For grid mode (>5 cards) this is used for both X and Y (Y halved)
        private static float SpacingForCount(int count) => count switch
        {
            <= 3 => -1f,
            <= 4 => -1f,
            <= 5 => -1f,
            <= 6 => 0f,   // grid: 2 rows of 3, more breathing room
            <= 8 => 0f,  // grid: 2 rows of 4
            _    => 0f,   // grid: 2 rows of 5+
        };

        private static Color GetTeamColor(string teamName)
{
    if (string.IsNullOrEmpty(teamName)) return Color.white;

    string lower = teamName.ToLowerInvariant();
    if (lower.Contains("crewmate")) return new Color32(0,   255, 255, 255);
    if (lower.Contains("impostor") ||
        lower.Contains("imposter")) return new Color32(255,   0,   0, 255);
    if (lower.Contains("neutral"))  return new Color32(180, 180, 180, 255);

    return Color.white;
}
        // ── Public API ────────────────────────────────────────────────────────────

        public static void Show(ushort[] roleIds)
        {
            Hide();
            if (HudManager.Instance?.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;
            var go = new GameObject("DraftScreenController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DraftScreenController>();
            Instance._offeredRoleIds = roleIds;
            Instance.BuildScreen();
        }

        public static void Hide()
        {
            if (Instance == null) return;
            if (Instance._screenRoot != null) Destroy(Instance._screenRoot);

            if (HudManager.Instance != null)
            {
                var hud = HudManager.Instance.transform;
                for (int i = hud.childCount - 1; i >= 0; i--)
                {
                    var child = hud.GetChild(i);
                    if (child != null && child.name.StartsWith("DraftCard_"))
                        Destroy(child.gameObject);
                }

            }

            Destroy(Instance.gameObject);
            Instance = null;
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        private void BuildScreen()
        {
            if (HudManager.Instance == null) return;
            if (HudManager.Instance.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;

            GameObject prefab = null;
            try
            {
                var bundle = TouAssets.MainBundle;
                if (bundle != null)
                    prefab = bundle.LoadAsset(PrefabName)?.TryCast<GameObject>();
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftScreenController] Bundle load failed: {ex.Message}");
            }

            if (prefab == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] SelectRoleGame prefab not found.");
                Destroy(gameObject); Instance = null; return;
            }

            _screenRoot = Instantiate(prefab);
            _screenRoot.name = "DraftRoleSelectScreen";
            DontDestroyOnLoad(_screenRoot);

            if (HudManager.Instance != null)
            {
                _screenRoot.transform.SetParent(HudManager.Instance.transform, false);
                _screenRoot.transform.localPosition = Vector3.zero;
            }

            var holderGo    = _screenRoot.transform.Find("RoleCardHolder");
            var statusGo    = _screenRoot.transform.Find("Status");
            var rolesHolder = _screenRoot.transform.Find("Roles");

            // Status text
            if (statusGo != null)
            {
                _statusText = statusGo.GetComponent<TextMeshPro>();
                if (_statusText != null)
                {
                    _statusText.font         = HudManager.Instance.TaskPanel.taskText.font;
                    _statusText.fontMaterial = HudManager.Instance.TaskPanel.taskText.fontMaterial;
                    _statusText.text         = "<color=#FFFFFF><b>Pick Your Role!</b></color>";
                    statusGo.gameObject.SetActive(true);
                }
            }

            if (holderGo == null)
            {
                Destroy(_screenRoot); Destroy(gameObject); Instance = null; return;
            }

            var rolePrefab = holderGo.gameObject;

            // Build card data via shared pipeline
            var idList = new List<ushort>();
            if (_offeredRoleIds != null) idList.AddRange(_offeredRoleIds);
            var cards = DraftUiManager.BuildCards(idList);

            int totalCards = cards.Count;
            float cardScale = CardScaleForCount(totalCards);
            float spacing   = SpacingForCount(totalCards);

            bool useGrid = totalCards > 5;

            if (useGrid)
            {
                // Disable the layout group entirely — we'll position cards manually
                var hLayout = rolesHolder?.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (hLayout != null) hLayout.enabled = false;

                // Expand the Roles holder vertically so both rows are visible
                var rt = rolesHolder?.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, 12f);
            }
            else
            {
                // Single row — just override HorizontalLayoutGroup spacing
                var layoutGroup = rolesHolder?.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (layoutGroup != null)
                    layoutGroup.spacing = spacing;
            }

            for (int i = 0; i < totalCards; i++)
            {
                var card       = cards[i];
                int capturedIdx = card.Index;

                var btn = CreateCard(
                    rolePrefab, rolesHolder,
                    card.RoleName, card.TeamName,
                    card.Icon ?? TouRoleIcons.RandomAny.LoadAsset(),
                    i, totalCards, card.Color,
                    cardScale, useGrid, spacing);

                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener((UnityAction)(() => OnCardClicked(capturedIdx)));
            }

            Coroutines.Start(CoAnimateCards(rolesHolder, cardScale, useGrid, totalCards));
        }

        // ── Card factory ──────────────────────────────────────────────────────────

        private static PassiveButton CreateCard(
            GameObject rolePrefab,
            Transform  rolesHolder,
            string     roleName,
            string     teamName,
            Sprite     icon,
            int        cardIndex,
            int        totalCards,
            Color      color,
            float      cardScale,
            bool       useGrid = false,
            float      spacing = 0f)
        {
            var newRoleObj    = UnityEngine.Object.Instantiate(rolePrefab, rolesHolder);
            var actualCard    = newRoleObj!.transform.GetChild(0);
            var roleText      = actualCard.GetChild(0).GetComponent<TextMeshPro>();
            var roleImage     = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
            var teamText      = actualCard.GetChild(2).GetComponent<TextMeshPro>();
            var passiveButton = actualCard.GetComponent<PassiveButton>();
            var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

            // In grid mode tilt per column, otherwise per global index
            // so bottom row cards don't inherit extreme rotation from high cardIndex
            int   tiltIndex = useGrid ? (cardIndex % Mathf.CeilToInt(totalCards / 2f)) : cardIndex;
            float tiltScale = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(3f, 9f, totalCards));
            float randZ     = (-10f + tiltIndex * 5f) * tiltScale
                              + UnityEngine.Random.Range(-1.5f, 1.5f) * tiltScale;

            // Hover: push forward in Z — same feel as TOU
            passiveButton.OnMouseOver.AddListener((UnityAction)(() =>
            {
                var pos = newRoleObj.transform.localPosition;
                newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z - 10f);
            }));
            passiveButton.OnMouseOut.AddListener((UnityAction)(() =>
            {
                var pos = newRoleObj.transform.localPosition;
                newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z + 10f);
            }));

            newRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, -randZ);

            if (useGrid)
            {
                // 2-row manual layout: split into top and bottom rows
                int cols    = Mathf.CeilToInt(totalCards / 2f);
                int row     = cardIndex / cols;
                int col     = cardIndex % cols;

                // Card width/height in world units at chosen scale
                float cardW = 2.5f * cardScale;
                float cardH = 3.7f * cardScale;
                float xGap  = cardW + spacing;
                float yGap  = cardH + spacing * 0.5f;

                // Centre the grid horizontally
                float totalW = cols * xGap - spacing;
                float startX = -totalW / 2f + cardW / 2f;
                // Row 0 = top, Row 1 = bottom
                float startY = yGap / 2f;

                float xPos = startX + col * xGap;
                float yPos = startY - row * yGap;

                newRoleObj.transform.localPosition = new Vector3(xPos, yPos, cardIndex);
            }
            else
            {
                // X managed by HorizontalLayoutGroup
                newRoleObj.transform.localPosition = new Vector3(
                    newRoleObj.transform.localPosition.x, 0f, cardIndex);
            }

            newRoleObj.transform.localScale = Vector3.one * cardScale;

            roleText.text    = roleName;
            teamText.text    = teamName;
            roleImage.sprite = icon;
            roleImage.SetSizeLimit(2.8f);
            var cardBgRenderer = actualCard.GetComponent<SpriteRenderer>();
            if (cardBgRenderer != null) cardBgRenderer.color = color;
            roleImage.color = Color.white;

            // Team text: shrink font cap so it fits on smaller cards
            // Prefab sets fontSizeMax=4; we reduce it proportionally
            teamText.fontSizeMax = Mathf.Lerp(4f, 2f, Mathf.InverseLerp(3f, 9f, totalCards));
            teamText.enableAutoSizing = true;

            rollover.OutColor  = color;
            rollover.OverColor = Color.white;
            roleText.color     = color;
            teamText.fontSizeMax = Mathf.Lerp(TeamNameFontSize, TeamNameFontSize * 0.5f, Mathf.InverseLerp(3f, 9f, totalCards));
            teamText.color       = GetTeamColor(teamName);

            return passiveButton;
        }

        // ── Animation ─────────────────────────────────────────────────────────────

        private static IEnumerator CoAnimateCards(Transform rolesHolder, float cardScale, bool useGrid, int totalCards)
        {
            if (rolesHolder == null) yield break;
            int cols = Mathf.CeilToInt(totalCards / 2f);
            // Use index loop — IL2CPP Transform enumerator crashes if children are
            // destroyed during iteration, and the cast path is also unstable.
            for (int i = 0; i < rolesHolder.childCount; i++)
            {
                Transform card = rolesHolder.GetChild(i);
                if (card == null) continue;
                if (card.childCount == 0) continue;
                Transform child = card.GetChild(0);
                if (child == null) continue;
                int animIndex = useGrid ? (i % cols) : i;
                yield return CoAnimateCardIn(child, animIndex);
                try { Coroutines.Start(MiscUtils.BetterBloop(child, finalSize: cardScale, duration: 0.22f, intensity: 0.16f)); }
                catch (Exception bex) { DraftModePlugin.Logger.LogWarning($"[DraftScreen] BetterBloop failed: {bex.Message}"); }
                yield return new WaitForSeconds(0.08f);
            }
            // Signal that all cards are shown — start the timer now
            if (Instance != null) Instance._cardsReady = true;
            DraftNetworkHelper.NotifyPickerReady();
        }

        private static IEnumerator CoAnimateCardIn(Transform card, int currentCard)
        {
            // Slide in from below — identical math to TOU, just smaller randY offset
            float randY = (currentCard * currentCard * 0.5f - currentCard) * 0.05f
                          + UnityEngine.Random.Range(-0.08f, 0f);
            float randZ = -10f + currentCard * 5f + UnityEngine.Random.Range(-1.5f, 0f);
            if (currentCard == 0) { randY = 0f; randZ = -2f; }

            card.localRotation = Quaternion.Euler(0f, 0f, -randZ);
            card.localPosition = new Vector3(card.localPosition.x, card.localPosition.y - 5f, card.localPosition.z);
            card.localRotation = Quaternion.Euler(0f, 0f, 14f);
            card.localScale    = new Vector3(0.15f, 0.15f, 0.15f);
            card.parent.gameObject.SetActive(true);

            for (float timer = 0f; timer < 0.35f; timer += Time.deltaTime)
            {
                float t = timer / 0.35f;
                card.localPosition = new Vector3(
                    card.localPosition.x,
                    Mathf.SmoothStep(-5f, randY, t),
                    card.localPosition.z);
                card.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.SmoothStep(-randZ + 2.5f, -randZ, t));
                yield return null;
            }

            card.localPosition = new Vector3(card.localPosition.x, randY, card.localPosition.z);
            card.localRotation = Quaternion.Euler(0f, 0f, -randZ);
        }

        // ── Timer ─────────────────────────────────────────────────────────────────

        private float _localTimeLeft = -1f;
        private bool  _cardsReady    = false;

        private void Update()
        {
            if (HudManager.Instance?.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;

            if (_hasPicked || _statusText == null || !DraftManager.IsDraftActive || !_cardsReady) return;

            int secs;
            if (AmongUsClient.Instance.AmHost)
            {
                secs = Mathf.Max(0, Mathf.CeilToInt(DraftManager.TurnTimeLeft));
            }
            else
            {
                if (_localTimeLeft < -0.5f) _localTimeLeft = DraftManager.TurnDuration;
                if (_localTimeLeft > 0f) _localTimeLeft -= Time.deltaTime;
                secs = Mathf.Max(0, Mathf.CeilToInt(_localTimeLeft));
            }

            _statusText.text =
                $"<color=#FFFFFF><b>Pick Your Role!</b></color>   " +
                $"<color={(secs <= 5 ? "#FF5555" : "#FFD700")}>" +
                $"{secs} Second{(secs != 1 ? "s" : "")} Remain</color>";
        }

        // ── Pick ──────────────────────────────────────────────────────────────────

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;
            DraftNetworkHelper.SendPickToHost(index);
            Invoke(nameof(DestroySelf), 1.2f);
        }

        private void DestroySelf() => Hide();
    }
}
