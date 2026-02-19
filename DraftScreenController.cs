using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using TMPro;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM
{
    /// <summary>
    /// Spawns the SelectRoleGame prefab, populates 4 role cards (3 offered + RANDOM),
    /// loads each role's icon via TouRoleUtils.GetRoleIcon(), and tints the card
    /// background to the role's specific colour on hover.
    /// </summary>
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject  _screenRoot;
        private string[]    _offeredRoles;
        private bool        _hasPicked;
        private TextMeshPro _statusText;

        private const string PrefabName = "SelectRoleGame";

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

            // 3. Child references
            var rolesHolder = _screenRoot.transform.Find("Roles") ?? _screenRoot.transform;

            var statusGo = _screenRoot.transform.Find("Status");
            if (statusGo != null) _statusText = statusGo.GetComponent<TextMeshPro>();
            if (_statusText != null)
                _statusText.text = "<color=#00FF00><b>Pick your role!</b></color>";

            var holderGo     = _screenRoot.transform.Find("RoleCardHolder");
            var cardTemplate = holderGo != null && holderGo.childCount > 0
                ? holderGo.GetChild(0).gameObject : null;

            if (cardTemplate == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] RoleCard template not found.");
                Destroy(_screenRoot);
                Destroy(gameObject);
                Instance = null;
                return;
            }

            // 4. Spawn 4 cards
            for (int i = 0; i < 4; i++)
            {
                int    idx      = i;
                bool   isRandom = (i == 3);
                string roleName = isRandom ? "RANDOM" : _offeredRoles[i];

                // Per-role colour from RoleColors, or green for RANDOM
                Color roleColour = isRandom
                    ? RoleColors.RandomColour
                    : RoleColors.GetColor(roleName);

                var card = Instantiate(cardTemplate, rolesHolder);
                card.SetActive(true);
                card.name = $"DraftCard_{i}";

                // RoleName text — coloured to role colour
                SetTmp(card, "RoleName", roleName, roleColour);

                // RoleTeam text — faction label, same colour
                SetTmp(card, "RoleTeam",
                    isRandom ? "Any" : GetFactionLabel(roleName),
                    roleColour);

                // RoleImage sprite — loaded via TouRoleUtils.GetRoleIcon()
                var imageGo = card.transform.Find("RoleImage");
                if (imageGo != null)
                {
                    var sr = imageGo.GetComponent<SpriteRenderer>();
                    if (sr != null && !isRandom)
                    {
                        var sprite = TryGetRoleSprite(roleName);
                        if (sprite != null) sr.sprite = sprite;
                    }
                }

                // Card background SpriteRenderer (on the card root itself)
                var cardBg = card.GetComponent<SpriteRenderer>();

                // Wire PassiveButton hover → role colour tint
                var btn = card.GetComponent<PassiveButton>();
                if (btn != null)
                {
                    btn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    btn.OnClick.AddListener((System.Action)(() => OnCardClicked(idx)));

                    if (cardBg != null)
                    {
                        btn.OnMouseOver = new UnityEngine.Events.UnityEvent();
                        btn.OnMouseOver.AddListener((System.Action)(() =>
                            cardBg.color = roleColour));

                        btn.OnMouseOut = new UnityEngine.Events.UnityEvent();
                        btn.OnMouseOut.AddListener((System.Action)(() =>
                            cardBg.color = Color.white));
                    }
                }
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

                    string typeName = role.GetType().Name
                        .ToLowerInvariant()
                        .Replace("role", "");

                    if (typeName == normalised)
                        return role.GetRoleIcon();

                    string displayName = role.NiceName?
                        .Replace(" ", "")
                        .ToLowerInvariant() ?? "";

                    if (displayName == normalised)
                        return role.GetRoleIcon();
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

        private static void SetTmp(GameObject card, string childName, string text, Color color)
        {
            var go = card.transform.Find(childName);
            if (go == null) return;
            var tmp = go.GetComponent<TextMeshPro>();
            if (tmp == null) return;
            tmp.text  = text;
            tmp.color = color;
        }

        private static string GetFactionLabel(string roleName) =>
            RoleCategory.GetFaction(roleName) switch
            {
                RoleFaction.Impostor => "Impostor",
                RoleFaction.Neutral  => "Neutral",
                _                    => "Crewmate"
            };
    }
}
