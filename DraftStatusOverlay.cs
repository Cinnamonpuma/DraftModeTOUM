using Reactor.Utilities.Attributes;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using DraftModeTOUM.Managers;
using Reactor.Utilities;

namespace DraftModeTOUM
{
    public enum OverlayState { Hidden, Waiting, BackgroundOnly }

    [RegisterInIl2Cpp]
    public sealed class DraftStatusOverlay : MonoBehaviour
    {
        private static DraftStatusOverlay? _instance;

        private GameObject?  _root;
        private GameObject?  _bgOverlay;
        private TextMeshPro? _yourNumberLabel;
        private TextMeshPro? _yourNumberValue;
        private TextMeshPro? _nowPickingLabel;
        private TextMeshPro? _nowPickingValue;
        private GameObject?  _roleCardNewRoleObj;

        private static GameObject? _cachedRolePrefab;

        private ushort?      _pendingRoleId     = null;
        private ushort?      _shownRoleId       = null;
        private int          _cachedMySlot      = -1;
        private int          _cachedPickerSlot  = -1;
        private int          _cachedPickerCount = -1;
        private OverlayState _currentState      = OverlayState.Hidden;

        private List<GameObject> _hiddenHudChildren = new();

        private static readonly Color WaitingBgColor = new Color(0f, 0f, 0f, 1f);

        private static readonly Vector3 CardHudPos = new Vector3(-2.0f, 0.3f, -21f);
        private const float CardScale              = 0.55f;
        private const float CardTiltDeg            = -8f;
        private const float TeamNameFontSize       = 3.8f;

        public DraftStatusOverlay(System.IntPtr ptr) : base(ptr) { }

        

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftStatusOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftStatusOverlay>();
        }

        public static void SetState(OverlayState state)
        {
            EnsureExists();
            _instance!._currentState = state;
            _instance.UpdateVisibility();
        }

        public static void Refresh()
        {
            if (_instance == null) return;
            _instance.UpdateContent();
        }

        public static void NotifyLocalPlayerPicked(ushort roleId)
        {
            EnsureExists();
            DraftModePlugin.Logger.LogInfo($"[DraftStatusOverlay] NotifyLocalPlayerPicked roleId={roleId}");
            if (roleId != _instance!._shownRoleId)
            {
                _instance._shownRoleId   = roleId;
                _instance._pendingRoleId = null;
                _instance.ShowRoleCard(roleId);
            }
        }

        public static void ClearHudReferences()
        {
            if (_instance == null) return;
            _instance._hiddenHudChildren.Clear();
            _instance._root             = null;
            _instance._bgOverlay        = null;
            _instance._yourNumberLabel  = null;
            _instance._yourNumberValue  = null;
            _instance._nowPickingLabel  = null;
            _instance._nowPickingValue  = null;
            _instance.DestroyRoleCard();
            _instance._pendingRoleId    = null;
            _instance._shownRoleId      = null;
            _instance._cachedMySlot     = -1;
            _instance._cachedPickerSlot = -1;
            _instance._cachedPickerCount = -1;
        }

        

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        private void OnDestroy()
        {
            RestoreHudElements();
            if (_instance == this) _instance = null;
        }

        

        private void BuildUI()
        {
            if (HudManager.Instance == null) return;

            var font    = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            _bgOverlay = new GameObject("DraftWaitingBg");
            _bgOverlay.transform.SetParent(HudManager.Instance.transform, false);
            _bgOverlay.transform.localPosition = new Vector3(0f, 0f, 1f);

            var bgSr              = _bgOverlay.AddComponent<SpriteRenderer>();
            bgSr.sprite           = MakeWhiteSprite();
            bgSr.color            = WaitingBgColor;
            bgSr.sortingLayerName = "UI";
            bgSr.sortingOrder     = 49;

            var cam   = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);
            _bgOverlay.transform.localScale = new Vector3(camW, camH, 1f);
            _bgOverlay.SetActive(false);

            _root = new GameObject("DraftOverlayRoot");
            _root.transform.SetParent(HudManager.Instance.transform, false);
            _root.transform.localPosition = new Vector3(0f, 0.6f, -20f);

            _yourNumberLabel = MakeText(_root, "YourNumberLabel", font, fontMat,
                "YOUR NUMBER:", 2.2f, new Color(0.6f, 0.9f, 1f),
                new Vector3(0f, 0.55f, 0f), bold: false);
            _yourNumberValue = MakeText(_root, "YourNumberValue", font, fontMat,
                "?", 5.5f, Color.white,
                new Vector3(0f, 0.05f, 0f), bold: true);
            _nowPickingLabel = MakeText(_root, "NowPickingLabel", font, fontMat,
                "NOW PICKING:", 1.6f, new Color(1f, 0.85f, 0.1f),
                new Vector3(0f, -0.55f, 0f), bold: false);
            _nowPickingValue = MakeText(_root, "NowPickingValue", font, fontMat,
                "?", 3.0f, new Color(1f, 0.85f, 0.1f),
                new Vector3(0f, -1.05f, 0f), bold: true);

            _root.SetActive(false);
        }

        

        private static bool EnsureRolePrefab()
        {
            if (_cachedRolePrefab != null) return true;
            try
            {
                var bundle = TouAssets.MainBundle;
                if (bundle == null) return false;
                var prefab = bundle.LoadAsset("SelectRoleGame")?.TryCast<GameObject>();
                if (prefab == null) return false;
                var holderGo = prefab.transform.Find("RoleCardHolder");
                if (holderGo == null) return false;
                _cachedRolePrefab = holderGo.gameObject;
                return true;
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Prefab load failed: {ex.Message}");
                return false;
            }
        }

        

        private void ShowRoleCard(ushort roleId)
        {
            DestroyRoleCard();
            if (!EnsureRolePrefab() || HudManager.Instance == null) return;

            var    role     = DraftUiManager.ResolveRole(roleId);
            string roleName = role?.NiceName ?? $"Role {roleId}";
            string teamName = DraftUiManager.GetTeamLabel(role);
            Sprite icon     = DraftUiManager.GetRoleIcon(role);
            Color  color    = DraftUiManager.GetRoleColor(role);

            _roleCardNewRoleObj = UnityEngine.Object.Instantiate(
                _cachedRolePrefab, HudManager.Instance.transform);
            _roleCardNewRoleObj.name = "DraftChosenRoleCard";

            if (_roleCardNewRoleObj.transform.childCount == 0) { DestroyRoleCard(); return; }
            var actualCard = _roleCardNewRoleObj.transform.GetChild(0);
            if (actualCard.childCount < 3) { DestroyRoleCard(); return; }

            var roleText      = actualCard.GetChild(0).GetComponent<TextMeshPro>();
            var roleImage     = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
            var teamText      = actualCard.GetChild(2).GetComponent<TextMeshPro>();
            var passiveButton = actualCard.GetComponent<PassiveButton>();
            var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

            _roleCardNewRoleObj.transform.localPosition = CardHudPos;
            _roleCardNewRoleObj.transform.localScale    = Vector3.one * CardScale;
            _roleCardNewRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, CardTiltDeg);

            if (roleText  != null) roleText.text  = roleName;
            if (teamText  != null)
            {
                teamText.text             = teamName;
                teamText.fontSizeMax      = TeamNameFontSize;
                teamText.enableAutoSizing = true;
                teamText.color            = GetTeamColor(teamName);
            }
            if (roleImage != null) { roleImage.sprite = icon; roleImage.SetSizeLimit(2.8f); roleImage.color = Color.white; }

            var cardBg = actualCard.GetComponent<SpriteRenderer>();
            if (cardBg   != null) cardBg.color   = color;
            if (rollover != null) { rollover.OutColor = color; rollover.OverColor = Color.white; }
            if (roleText != null) roleText.color  = color;

            
            foreach (var tmp in _roleCardNewRoleObj.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                var r = tmp.GetComponent<Renderer>();
                if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 1; }
            }
            foreach (var sr in _roleCardNewRoleObj.GetComponentsInChildren<SpriteRenderer>())
            {
                sr.sortingLayerName = "UI";
                sr.sortingOrder     = 1;
            }

            
            var col = actualCard.GetComponent<Collider2D>() as Collider2D
                   ?? actualCard.GetComponent<BoxCollider2D>() as Collider2D;
            if (col == null)
            {
                var box  = actualCard.gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(4f, 6f);
                col      = box;
            }

            if (passiveButton != null)
            {
                passiveButton.enabled   = true;
                passiveButton.Colliders = new Collider2D[] { col };

                passiveButton.OnClick.RemoveAllListeners();
                ushort capturedId = roleId;
                passiveButton.OnClick.AddListener((System.Action)(() => OpenWiki(capturedId)));

                passiveButton.OnMouseOver.RemoveAllListeners();
                passiveButton.OnMouseOver.AddListener((System.Action)(() =>
                {
                    if (_roleCardNewRoleObj != null)
                        _roleCardNewRoleObj.transform.localScale = Vector3.one * (CardScale * 1.08f);
                }));
                passiveButton.OnMouseOut.RemoveAllListeners();
                passiveButton.OnMouseOut.AddListener((System.Action)(() =>
                {
                    if (_roleCardNewRoleObj != null)
                        _roleCardNewRoleObj.transform.localScale = Vector3.one * CardScale;
                }));
            }

            _roleCardNewRoleObj.SetActive(true);
            Coroutines.Start(CoPopInCard(_roleCardNewRoleObj.transform));
        }

        

        private void OpenWiki(ushort roleId)
        {
            try
            {
                var r = DraftUiManager.ResolveRole(roleId);
                if (r is not TownOfUs.Modules.Wiki.IWikiDiscoverable wikiTarget)
                {
                    DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Role {roleId} not IWikiDiscoverable");
                    return;
                }

                
                
                if (_roleCardNewRoleObj != null)
                    _roleCardNewRoleObj.SetActive(false);

                var wiki = TownOfUs.Modules.Wiki.IngameWikiMinigame.Create();
                wiki.Begin(null);
                wiki.OpenFor(wikiTarget);

                
                
                
                Coroutines.Start(CoWaitForWikiDestroyed(wiki));
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Wiki open failed: {ex.Message}");
                
                if (_roleCardNewRoleObj != null)
                    _roleCardNewRoleObj.SetActive(true);
            }
        }

        private IEnumerator CoWaitForWikiDestroyed(TownOfUs.Modules.Wiki.IngameWikiMinigame wiki)
        {
            
            
            while (wiki != null)
                yield return null;

            
            if (_roleCardNewRoleObj != null)
                _roleCardNewRoleObj.SetActive(true);
        }

        

        private void DestroyRoleCard()
        {
            if (_roleCardNewRoleObj != null)
            {
                try { UnityEngine.Object.Destroy(_roleCardNewRoleObj); } catch { }
                _roleCardNewRoleObj = null;
            }
        }

        private static IEnumerator CoPopInCard(Transform holder)
        {
            holder.localScale = Vector3.zero;
            float duration    = 0.25f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float s = Mathf.LerpUnclamped(0f, CardScale, EaseOutBack(t / duration));
                holder.localScale = Vector3.one * s;
                yield return null;
            }
            holder.localScale = Vector3.one * CardScale;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static Color GetTeamColor(string teamName)
        {
            if (string.IsNullOrEmpty(teamName)) return Color.white;
            string lower = teamName.ToLowerInvariant();
            if (lower.Contains("crewmate"))                               return new Color32(0,   255, 255, 255);
            if (lower.Contains("impostor") || lower.Contains("imposter")) return new Color32(255,   0,   0, 255);
            if (lower.Contains("neutral"))                                return new Color32(180, 180, 180, 255);
            return Color.white;
        }

        

        private void Update()
        {
            if (_currentState == OverlayState.Hidden) return;

            if (_currentState == OverlayState.Waiting)
            {
                if (_root == null) BuildUI();
                if (_root == null) return;

                if (DraftManager.IsDraftActive)
                {
                    int mySlot     = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
                    int pickerSlot = -1;
                    int pickerCount = 0;
                    foreach (var s in DraftManager.GetActivePickerStates())
                    {
                        if (s == null || !s.IsPickingNow) continue;
                        pickerCount++;
                        if (pickerSlot < 0) pickerSlot = s.SlotNumber;
                    }

                    if (mySlot != _cachedMySlot || pickerSlot != _cachedPickerSlot || pickerCount != _cachedPickerCount)
                    {
                        _cachedMySlot     = mySlot;
                        _cachedPickerSlot = pickerSlot;
                        _cachedPickerCount = pickerCount;
                        UpdateContent();
                    }
                }
            }

            if (_pendingRoleId.HasValue && _pendingRoleId != _shownRoleId)
            {
                _shownRoleId   = _pendingRoleId;
                _pendingRoleId = null;
                ShowRoleCard(_shownRoleId.Value);
            }
        }

        private void UpdateContent()
        {
            if (_root == null) return;

            int mySlot     = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            int pickerSlot  = -1;
            int pickerCount = 0;
            bool isMyTurn   = false;
            foreach (var s in DraftManager.GetActivePickerStates())
            {
                if (s == null || !s.IsPickingNow) continue;
                pickerCount++;
                if (pickerSlot < 0) pickerSlot = s.SlotNumber;
                if (s.PlayerId == PlayerControl.LocalPlayer.PlayerId) isMyTurn = true;
            }

            if (_yourNumberValue != null)
                _yourNumberValue.text = mySlot > 0 ? mySlot.ToString() : "?";
            if (_nowPickingValue != null)
                _nowPickingValue.text = pickerCount > 1 ? "MULTI" : (pickerSlot > 0 ? pickerSlot.ToString() : "?");
            if (_nowPickingValue != null)
                _nowPickingValue.color = isMyTurn ? new Color(0.1f, 1f, 0.4f) : new Color(1f, 0.85f, 0.1f);
            if (_nowPickingLabel != null)
                _nowPickingLabel.text  = isMyTurn ? "YOUR TURN!" : (pickerCount > 1 ? "NOW PICKING (MULTI):" : "NOW PICKING:");
        }

        private void UpdateVisibility()
        {
            if (_root == null && _currentState != OverlayState.Hidden) BuildUI();
            if (_root == null) return;

            if (_currentState == OverlayState.Hidden)
            {
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(false);
                DestroyRoleCard();
                _pendingRoleId = null;
                _shownRoleId   = null;
                RestoreHudElements();
            }
            else if (_currentState == OverlayState.Waiting)
            {
                _root.SetActive(true);
                if (_bgOverlay != null) _bgOverlay.SetActive(true);
                HideHudElements();
            }
            else if (_currentState == OverlayState.BackgroundOnly)
            {
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(true);
                HideHudElements();
            }
        }

        private void HideHudElements()
        {
            _hiddenHudChildren.RemoveAll(go => go == null);

            var gsm = UnityEngine.Object.FindObjectOfType<GameStartManager>();
            if (gsm != null && gsm.gameObject.activeSelf)
            {
                gsm.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(gsm.gameObject))
                    _hiddenHudChildren.Add(gsm.gameObject);
            }

            var lobbyInfoPane = UnityEngine.Object.FindObjectOfType<LobbyInfoPane>();
            if (lobbyInfoPane != null && lobbyInfoPane.gameObject.activeSelf)
            {
                lobbyInfoPane.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(lobbyInfoPane.gameObject))
                    _hiddenHudChildren.Add(lobbyInfoPane.gameObject);
            }
        }

        private void RestoreHudElements()
        {
            foreach (var go in _hiddenHudChildren)
                if (go != null)
                    try { go.SetActive(true); } catch { }
            _hiddenHudChildren.Clear();
        }

        private static TextMeshPro MakeText(
            GameObject parent, string name,
            TMP_FontAsset font, Material fontMat,
            string text, float fontSize, Color color,
            Vector3 offset, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = offset;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font               = font;
            tmp.fontMaterial       = fontMat;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.fontStyle          = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableWordWrapping = false;
            tmp.text               = text;

            var r = go.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 50; }
            return tmp;
        }

        private static Sprite? _white;
        private static Sprite MakeWhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px  = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }
    }
}

