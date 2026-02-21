using Reactor.Utilities.Attributes;
using TMPro;
using UnityEngine;
using DraftModeTOUM.Managers;
using System.Collections.Generic;

namespace DraftModeTOUM
{
    public enum OverlayState { Hidden, Waiting, BackgroundOnly }

    [RegisterInIl2Cpp]
    public sealed class DraftStatusOverlay : MonoBehaviour
    {
        private static DraftStatusOverlay? _instance;

        private GameObject? _root;
        private GameObject? _bgOverlay;
        private TextMeshPro? _yourNumberLabel;
        private TextMeshPro? _yourNumberValue;
        private TextMeshPro? _nowPickingLabel;
        private TextMeshPro? _nowPickingValue;

        private int _cachedMySlot = -1;
        private int _cachedPickerSlot = -1;

        private OverlayState _currentState = OverlayState.Hidden;
        private List<GameObject> _hiddenHudChildren = new();

        private static readonly Color WaitingBgColor = new Color(0f, 0f, 0f, 1f);

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

        private void Awake()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            if (HudManager.Instance == null) return;

            // ── Full-screen background ─────────────────────────────────────────
            _bgOverlay = new GameObject("DraftWaitingBg");
            _bgOverlay.transform.SetParent(HudManager.Instance.transform, false);
            _bgOverlay.transform.localPosition = new Vector3(0f, 0f, 1f);

            var bgSr = _bgOverlay.AddComponent<SpriteRenderer>();
            bgSr.sprite = MakeWhiteSprite();
            bgSr.color = WaitingBgColor;
            bgSr.sortingLayerName = "UI";
            bgSr.sortingOrder = 49;

            var cam = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);
            _bgOverlay.transform.localScale = new Vector3(camW, camH, 1f);
            _bgOverlay.SetActive(false);

            // ── Text root ─────────────────────────────────────────────────────
            _root = new GameObject("DraftOverlayRoot");
            _root.transform.SetParent(HudManager.Instance.transform, false);
            _root.transform.localPosition = new Vector3(0f, 0.6f, -20f);

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            _yourNumberLabel = MakeText(_root, "YourNumberLabel", font, fontMat,
                text: "YOUR NUMBER:", fontSize: 2.2f,
                color: new Color(0.6f, 0.9f, 1f), offset: new Vector3(0f, 0.55f, 0f), bold: false);

            _yourNumberValue = MakeText(_root, "YourNumberValue", font, fontMat,
                text: "?", fontSize: 5.5f,
                color: Color.white, offset: new Vector3(0f, 0.05f, 0f), bold: true);

            _nowPickingLabel = MakeText(_root, "NowPickingLabel", font, fontMat,
                text: "NOW PICKING:", fontSize: 1.6f,
                color: new Color(1f, 0.85f, 0.1f), offset: new Vector3(0f, -0.55f, 0f), bold: false);

            _nowPickingValue = MakeText(_root, "NowPickingValue", font, fontMat,
                text: "?", fontSize: 3.0f,
                color: new Color(1f, 0.85f, 0.1f), offset: new Vector3(0f, -1.05f, 0f), bold: true);

            _root.SetActive(false);
        }

        private static TextMeshPro MakeText(GameObject parent, string name,
            TMP_FontAsset font, Material fontMat,
            string text, float fontSize, Color color, Vector3 offset, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = offset;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.fontMaterial = fontMat;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableWordWrapping = false;
            tmp.text = text;

            var r = tmp.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 50; }

            return tmp;
        }

        private void Update()
        {
            if (!DraftManager.IsDraftActive || _currentState != OverlayState.Waiting)
                return;

            if (_root == null) BuildUI();

            int mySlot = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            var picker = DraftManager.GetCurrentPickerState();
            int pickerSlot = picker?.SlotNumber ?? -1;

            if (mySlot != _cachedMySlot || pickerSlot != _cachedPickerSlot)
            {
                _cachedMySlot = mySlot;
                _cachedPickerSlot = pickerSlot;
                UpdateContent();
            }
        }

        private void UpdateContent()
        {
            if (_root == null) return;

            int mySlot = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            var picker = DraftManager.GetCurrentPickerState();
            int pickerSlot = picker?.SlotNumber ?? -1;

            if (_yourNumberValue != null)
                _yourNumberValue.text = mySlot > 0 ? mySlot.ToString() : "?";

            if (_nowPickingValue != null)
                _nowPickingValue.text = pickerSlot > 0 ? pickerSlot.ToString() : "?";

            bool isMyTurn = mySlot > 0 && mySlot == pickerSlot;

            if (_nowPickingValue != null)
                _nowPickingValue.color = isMyTurn
                    ? new Color(0.1f, 1f, 0.4f)
                    : new Color(1f, 0.85f, 0.1f);

            if (_nowPickingLabel != null)
                _nowPickingLabel.text = isMyTurn ? "YOUR TURN!" : "NOW PICKING:";
        }

        private void UpdateVisibility()
        {
            if (_root == null && _currentState != OverlayState.Hidden) BuildUI();
            if (_root == null) return;

            if (_currentState == OverlayState.Hidden)
            {
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(false);
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
                // Hide text but keep background + HUD overrides active
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(true);
                HideHudElements();
            }
        }

        private void HideHudElements()
        {
            var gsm = UnityEngine.Object.FindObjectOfType<GameStartManager>();
            if (gsm != null && gsm.gameObject.activeSelf)
            {
                gsm.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(gsm.gameObject)) _hiddenHudChildren.Add(gsm.gameObject);
            }

            var lobbyInfoPane = UnityEngine.Object.FindObjectOfType<LobbyInfoPane>();
            if (lobbyInfoPane != null && lobbyInfoPane.gameObject.activeSelf)
            {
                lobbyInfoPane.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(lobbyInfoPane.gameObject)) _hiddenHudChildren.Add(lobbyInfoPane.gameObject);
            }
        }

        private void RestoreHudElements()
        {
            foreach (var go in _hiddenHudChildren)
            {
                if (go != null) go.SetActive(true);
            }
            _hiddenHudChildren.Clear();
        }

        private static Sprite? _white;
        private static Sprite MakeWhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}