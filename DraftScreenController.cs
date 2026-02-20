using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using TMPro;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM
{
    /// <summary>
    /// Spawns the SelectRoleGame prefab, populates 4 role cards (3 offered + RANDOM).
    /// The prefab stacks cards along Z (like a deck of cards), separated by rotation —
    /// this matches exactly how TOUM's TraitorSelectionMinigame works.
    /// CardZSpacing controls how far apart the cards are in Z (depth) which controls
    /// how much they fan out visually.
    /// </summary>
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject  _screenRoot;
        private string[]    _offeredRoles;
        private bool        _hasPicked;
        private TextMeshPro _statusText;

        private const string PrefabName = "SelectRoleGame";

        // Controls Z-depth spacing between stacked cards.
        // TOUM uses z = index (0,1,2,3). Increase to fan cards out more.
        public static float CardZSpacing = 1f;

        // Controls rotation spread (degrees per card). TOUM uses randZ = -10 + z*5.
        // Increase to rotate cards further apart.
        public static float CardRotationPerStep = 5f;

        // ── Public API ────────────────────────────────────────────────────────

        public static void Show(string[] offeredRoles)
        {
            Hide();
            var go = new GameObject("DraftScreenController");
            DontDestroyOnLoad(go);
            Instance               = go.AddComponent<DraftScreenController>();
            Instance._offeredRoles = offeredRoles;
            Instance.BuildScreen();
        }

        public static void Hide()
        {
            if (Instance == null) return;
            if (Instance._screenRoot != null) Destroy(Instance._screenRoot);
            Destroy(Instance.gameObject);
            Instance = null;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildScreen()
        {
            // 1. Load prefab from TOUM bundle
            GameObject prefab = null;
            try
            {
                var bundle = TownOfUs.Assets.TouAssets.MainBundle;
                if (bundle != null)
                    prefab = bundle.LoadAsset<GameObject>(PrefabName);
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftScreenController] Bundle load failed: {ex.Message}");
            }

            if (prefab == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] Prefab not found – falling back to chat.");
                Destroy(gameObject);
                Instance = null;
                return;
            }

            // 2. Instantiate and parent to HUD
            _screenRoot      = Instantiate(prefab);
            _screenRoot.name = "DraftRoleSelectScreen";
            DontDestroyOnLoad(_screenRoot);

            if (HudManager.Instance != null)
            {
                _screenRoot.transform.SetParent(HudManager.Instance.transform, false);
                _screenRoot.transform.localPosition = new Vector3(0f, 0f, -600f);
            }

            // 3. Child references — matching TraitorSelectionMinigame's Awake()
            var rolesHolder = _screenRoot.transform.Find("Roles");
            var holderGo    = _screenRoot.transform.Find("RoleCardHolder");

            var statusGo = _screenRoot.transform.Find("Status");
            if (statusGo != null) _statusText = statusGo.GetComponent<TextMeshPro>();
            if (_statusText != null)
                _statusText.text = "<color=#00FF00><b>Pick your role!</b></color>";

            // RolePrefab is the RoleCardHolder GameObject itself (not its child),
            // matching: RolePrefab = transform.FindChild("RoleCardHolder").gameObject
            if (holderGo == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] RoleCardHolder not found.");
                Destroy(_screenRoot);
                Destroy(gameObject);
                Instance = null;
                return;
            }

            var rolePrefab = holderGo.gameObject;
            var parent     = rolesHolder != null ? rolesHolder : _screenRoot.transform;

            // 4. Spawn 4 cards, mirroring CreateCard() in TraitorSelectionMinigame
            for (int i = 0; i < 4; i++)
            {
                int    idx      = i;
                bool   isRandom = (i == 3);
                string roleName = isRandom ? "RANDOM" : _offeredRoles[i];
                Color  color    = isRandom ? RoleColors.RandomColour : RoleColors.GetColor(roleName);

                // Instantiate the RoleCardHolder prefab under Roles (matching TOUM exactly)
                var newRoleObj  = Instantiate(rolePrefab, parent);
                var actualCard  = newRoleObj.transform.GetChild(0);

                // Z-based stacking + rotation — mirrors TOUM's CreateCard positioning
                float rotZ = -10f + i * CardRotationPerStep + UnityEngine.Random.Range(-1.5f, 1.5f);
                newRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, -rotZ);
                newRoleObj.transform.localPosition = new Vector3(
                    newRoleObj.transform.localPosition.x,
                    newRoleObj.transform.localPosition.y,
                    i * CardZSpacing);

                newRoleObj.SetActive(true);
                newRoleObj.name = $"DraftCard_{i}";

                // Populate text and image on the actual card (child 0 of the holder)
                var roleText  = actualCard.GetChild(0).GetComponent<TextMeshPro>();
                var roleImage = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
                var teamText  = actualCard.GetChild(2).GetComponent<TextMeshPro>();

                if (roleText  != null) { roleText.text  = roleName; roleText.color  = color; }
                if (teamText  != null) { teamText.text  = isRandom ? "Any" : GetFactionLabel(roleName); teamText.color = color; }

                if (roleImage != null && !isRandom)
                {
                    var sprite = TryGetRoleSprite(roleName);
                    if (sprite != null) roleImage.sprite = sprite;
                }

                // Button — wire click and hover colour via ButtonRolloverHandler if present,
                // otherwise fall back to manual SpriteRenderer tint
                var passiveButton = actualCard.GetComponent<PassiveButton>();
                if (passiveButton != null)
                {
                    passiveButton.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    passiveButton.OnClick.AddListener((System.Action)(() => OnCardClicked(idx)));

                    // Hover: lift card forward in Z (matches TOUM's hover behaviour)
                    passiveButton.OnMouseOver = new UnityEngine.Events.UnityEvent();
                    passiveButton.OnMouseOver.AddListener((System.Action)(() =>
                    {
                        var pos = newRoleObj.transform.localPosition;
                        newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z - 10f);
                    }));

                    passiveButton.OnMouseOut = new UnityEngine.Events.UnityEvent();
                    passiveButton.OnMouseOut.AddListener((System.Action)(() =>
                    {
                        var pos = newRoleObj.transform.localPosition;
                        newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z + 10f);
                    }));

                    // Also tint via ButtonRolloverHandler if available
                    var rollover = actualCard.GetComponent<ButtonRolloverHandler>();
                    if (rollover != null)
                        rollover.OverColor = color;
                }

                DraftModePlugin.Logger.LogInfo(
                    $"[DraftScreenController] Card {i} ({roleName}) spawned at z={i * CardZSpacing}, rotZ={rotZ:F1}");
            }

            DraftModePlugin.Logger.LogInfo("[DraftScreenController] Screen built successfully.");
        }

        // ── Update: live countdown ────────────────────────────────────────────

        private void Update()
        {
            if (_hasPicked || _screenRoot == null || _statusText == null) return;
            if (!DraftManager.IsDraftActive) return;
            int secs = Mathf.CeilToInt(DraftManager.TurnTimeLeft);
            _statusText.text =
                $"<color=#00FF00><b>Pick your role!</b></color>  <color=#FFD700>{secs}s</color>";
        }

        // ── Pick ──────────────────────────────────────────────────────────────

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;

            string label = index < 3 ? _offeredRoles[index] : "RANDOM";
            DraftModePlugin.Logger.LogInfo($"[DraftScreenController] Picked index {index} ({label}).");

            DraftNetworkHelper.SendPickToHost(index);

            if (_statusText != null)
                _statusText.text = $"<color=#00FF00><b>You picked: {label}!</b></color>";

            Invoke(nameof(DestroySelf), 1.2f);
        }

        private void DestroySelf() => Hide();

        // ── Role icon ─────────────────────────────────────────────────────────

        private static Sprite TryGetRoleSprite(string roleName)
        {
            try
            {
                if (RoleManager.Instance == null) return null;
                string normalised = roleName.Replace(" ", "").ToLowerInvariant();
                foreach (var role in RoleManager.Instance.AllRoles)
                {
                    if (role == null) continue;
                    string typeName = role.GetType().Name.ToLowerInvariant().Replace("role", "");
                    if (typeName == normalised) return role.GetRoleIcon();
                    string displayName = role.NiceName?.Replace(" ", "").ToLowerInvariant() ?? "";
                    if (displayName == normalised) return role.GetRoleIcon();
                }
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[DraftScreenController] GetRoleIcon failed for '{roleName}': {ex.Message}");
            }
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetFactionLabel(string roleName) =>
            RoleCategory.GetFaction(roleName) switch
            {
                RoleFaction.Impostor => "Impostor",
                RoleFaction.Neutral  => "Neutral",
                _                    => "Crewmate"
            };
    }
}
