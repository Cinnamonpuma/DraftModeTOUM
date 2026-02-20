using Reactor.Utilities.Attributes;
using TMPro;
using UnityEngine;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM
{
    /// <summary>
    /// Displays a center-screen HUD during the draft showing:
    ///   YOUR NUMBER: X
    ///   NOW PICKING: Y
    /// Shown to all players. Hidden when the picker UI is open on your screen.
    /// </summary>
    [RegisterInIl2Cpp]
    public sealed class DraftStatusOverlay : MonoBehaviour
    {
        private static DraftStatusOverlay? _instance;

        private GameObject?   _root;
        private TextMeshPro?  _yourNumberLabel;   // "YOUR NUMBER:"
        private TextMeshPro?  _yourNumberValue;   // e.g. "3"
        private TextMeshPro?  _nowPickingLabel;   // "NOW PICKING:"
        private TextMeshPro?  _nowPickingValue;   // e.g. "1"

        private int  _cachedMySlot      = -1;
        private int  _cachedPickerSlot  = -1;
        private bool _overlayVisible    = false;

        public DraftStatusOverlay(System.IntPtr ptr) : base(ptr) { }

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftStatusOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftStatusOverlay>();
        }

        public static void Show()
        {
            EnsureExists();
            _instance!._overlayVisible = true;
            _instance.UpdateVisibility();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._overlayVisible = false;
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

            _root = new GameObject("DraftOverlayRoot");
            _root.transform.SetParent(HudManager.Instance.transform, false);
            // Center of screen, in front of everything
            _root.transform.localPosition = new Vector3(0f, 0.6f, -20f);

            var font    = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            // ── "YOUR NUMBER:" label ──────────────────────────────────────────
            _yourNumberLabel = MakeText(_root, "YourNumberLabel", font, fontMat,
                text:     "YOUR NUMBER:",
                fontSize: 2.2f,
                color:    new Color(0.6f, 0.9f, 1f),
                offset:   new Vector3(0f, 0.55f, 0f),
                bold:     false);

            // ── Your slot number (big) ────────────────────────────────────────
            _yourNumberValue = MakeText(_root, "YourNumberValue", font, fontMat,
                text:     "?",
                fontSize: 5.5f,
                color:    Color.white,
                offset:   new Vector3(0f, 0.05f, 0f),
                bold:     true);

            // ── "NOW PICKING:" label ──────────────────────────────────────────
            _nowPickingLabel = MakeText(_root, "NowPickingLabel", font, fontMat,
                text:     "NOW PICKING:",
                fontSize: 1.6f,
                color:    new Color(1f, 0.85f, 0.1f),
                offset:   new Vector3(0f, -0.55f, 0f),
                bold:     false);

            // ── Current picker number ─────────────────────────────────────────
            _nowPickingValue = MakeText(_root, "NowPickingValue", font, fontMat,
                text:     "?",
                fontSize: 3.0f,
                color:    new Color(1f, 0.85f, 0.1f),
                offset:   new Vector3(0f, -1.05f, 0f),
                bold:     true);

            _root.SetActive(false);
        }

        private static TextMeshPro MakeText(GameObject parent, string name,
            TMP_FontAsset font, Material fontMat,
            string text, float fontSize, Color color,
            Vector3 offset, bool bold)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = offset;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font            = font;
            tmp.fontMaterial    = fontMat;
            tmp.fontSize        = fontSize;
            tmp.color           = color;
            tmp.alignment       = TextAlignmentOptions.Center;
            tmp.fontStyle       = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableWordWrapping = false;
            tmp.text            = text;
            return tmp;
        }

        private void Update()
        {
            if (!DraftManager.IsDraftActive)
            {
                if (_overlayVisible) Hide();
                return;
            }

            // Rebuild UI if HudManager wasn't ready in Awake
            if (_root == null) BuildUI();

            // Update content only when values change to avoid per-frame allocs
            int mySlot     = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            var picker     = DraftManager.GetCurrentPickerState();
            int pickerSlot = picker?.SlotNumber ?? -1;

            if (mySlot != _cachedMySlot || pickerSlot != _cachedPickerSlot)
            {
                _cachedMySlot     = mySlot;
                _cachedPickerSlot = pickerSlot;
                UpdateContent();
            }
        }

        private void UpdateContent()
        {
            if (_root == null) return;

            int mySlot     = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            var picker     = DraftManager.GetCurrentPickerState();
            int pickerSlot = picker?.SlotNumber ?? -1;

            if (_yourNumberValue != null)
                _yourNumberValue.text = mySlot > 0 ? mySlot.ToString() : "?";

            if (_nowPickingValue != null)
                _nowPickingValue.text = pickerSlot > 0 ? pickerSlot.ToString() : "?";

            // Highlight "IT'S YOUR TURN" in green when it's you picking
            bool isMyTurn = mySlot > 0 && mySlot == pickerSlot;
            if (_nowPickingValue != null)
                _nowPickingValue.color = isMyTurn
                    ? new Color(0.1f, 1f, 0.4f)   // green = your turn
                    : new Color(1f, 0.85f, 0.1f);  // gold = someone else

            if (_nowPickingLabel != null)
                _nowPickingLabel.text = isMyTurn ? "YOUR TURN!" : "NOW PICKING:";
        }

        private void UpdateVisibility()
        {
            if (_root == null) return;
            _root.SetActive(_overlayVisible);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
