using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using Reactor.Utilities;
using System.Collections;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;

namespace DraftModeTOUM
{
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject  _screenRoot;
        private string[]    _offeredRoles;
        private bool        _hasPicked;
        private TextMeshPro _statusText;

        // The solid navy we snap to and hold — matches the card background colour exactly
        private static readonly Color NavyColor = new Color(0f, 0f, 0f, 0.65f);

        private const string PrefabName = "SelectRoleGame";
        public static float  CardZSpacing = 0.5f;

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
            // Restore hidden HUD children
            foreach (var go in Instance._hiddenHudChildren)
                if (go != null) go.SetActive(true);
            Instance._hiddenHudChildren.Clear();
            if (Instance._canvasOverlay != null) Destroy(Instance._canvasOverlay);
            if (Instance._cardCanvas    != null) Destroy(Instance._cardCanvas);
            if (Instance._screenRoot    != null) Destroy(Instance._screenRoot);
            Destroy(Instance.gameObject);
            Instance = null;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private GameObject _canvasOverlay;
        private GameObject _cardCanvas;
        private System.Collections.Generic.List<GameObject> _hiddenHudChildren = new();

        private void BuildScreen()
        {
            // Hide the lobby right-side panel (GameStartManager) specifically.
            var gsm = Object.FindObjectOfType<GameStartManager>();
            if (gsm != null && gsm.gameObject.activeSelf)
            {
                gsm.gameObject.SetActive(false);
                _hiddenHudChildren.Add(gsm.gameObject);
            }

            // Navy background SpriteRenderer in HUD space
            var bgGo = new GameObject("DraftNavyBg");
            DontDestroyOnLoad(bgGo);
            _canvasOverlay = bgGo;
            _cardCanvas    = null;

            if (HudManager.Instance != null)
                bgGo.transform.SetParent(HudManager.Instance.transform, false);
            var bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.sprite           = GetWhiteSprite();
            bgSr.color            = NavyColor;
            bgSr.sortingLayerName = "UI";
            bgSr.sortingOrder     = 9999;

            var cam = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);
            bgGo.transform.localPosition = new Vector3(0f, 0f, 1f);
            bgGo.transform.localScale    = new Vector3(camW, camH, 1f);

            // 2. Load the SelectRoleGame prefab
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
                Destroy(gameObject);
                Instance = null;
                return;
            }

            // 3. Instantiate and parent to HUD
            _screenRoot      = Instantiate(prefab);
            _screenRoot.name = "DraftRoleSelectScreen";
            DontDestroyOnLoad(_screenRoot);

            // Parent to HudManager, but on a separate Camera canvas at sort 10000 so it beats
            // the navy Canvas at 9999. We achieve this by putting _screenRoot on its own
            // ScreenSpaceOverlay canvas — but SpriteRenderers don't render there.
            // REAL fix: parent to HudManager, set sortingLayerName="UI" and sortingOrder=10000
            // so SR draws above the navy SR (9999), then rely on the navy Canvas (9999) to
            // cover the right-side HUD panel while the SR cards float above the navy SR.
            // The Canvas panel doesn't occlude SRs so the cards remain fully visible.
            if (HudManager.Instance != null)
            {
                _screenRoot.transform.SetParent(HudManager.Instance.transform, false);
                _screenRoot.transform.localPosition = Vector3.zero;
            }
            // Note: sorting layers are applied per-card after spawning below

            // 4. Wire status text
            var rolesHolder = _screenRoot.transform.Find("Roles");
            var holderGo    = _screenRoot.transform.Find("RoleCardHolder");
            var statusGo    = _screenRoot.transform.Find("Status");

            if (statusGo != null)
            {
                _statusText = statusGo.GetComponent<TextMeshPro>();
                // Move the title text down to clear the top buttons
                var sp = statusGo.transform.localPosition;
                statusGo.transform.localPosition = new Vector3(sp.x, sp.y - 0.5f, sp.z);
            }
            if (_statusText != null)
            {
                var font    = HudManager.Instance?.TaskPanel?.taskText?.font;
                var fontMat = HudManager.Instance?.TaskPanel?.taskText?.fontMaterial;
                if (font    != null) _statusText.font         = font;
                if (fontMat != null) _statusText.fontMaterial = fontMat;
                _statusText.text = "<color=#FFFFFF><b>Pick Your Role!</b></color>";
            }

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

            // 5. Spawn 4 cards in a straight row
            for (int i = 0; i < 4; i++)
            {
                int    idx      = i;
                bool   isRandom = (i == 3);
                string roleName = isRandom
                    ? "Random"
                    : (_offeredRoles != null && i < _offeredRoles.Length ? _offeredRoles[i] : "?");
                Color color = isRandom ? Color.white : RoleColors.GetColor(roleName);

                var newRoleObj = Instantiate(rolePrefab, parent);
                var actualCard = newRoleObj.transform.GetChild(0);

                // Straight horizontal row, no rotation
                float spacing = 1.35f;
                float startX  = -((4 - 1) * spacing) / 2f;
                float xPos    = startX + i * spacing;
                newRoleObj.transform.localRotation = Quaternion.identity;
                newRoleObj.transform.localPosition = new Vector3(xPos, -0.6f, i * CardZSpacing);
                newRoleObj.transform.localScale    = Vector3.one * 0.78f;
                newRoleObj.SetActive(true);
                newRoleObj.name = $"DraftCard_{i}_{roleName}";

                // Card text fields
                var roleText  = actualCard.GetChild(0).GetComponent<TextMeshPro>();
                var roleImage = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
                var teamText  = actualCard.GetChild(2).GetComponent<TextMeshPro>();

                if (roleText != null) { roleText.text = roleName; roleText.color = color; }
                if (teamText != null) { teamText.text = isRandom ? "Any" : GetFactionLabel(roleName); teamText.color = color; }

                // Card background: very dark role color
                var cardBgSr = actualCard.GetComponent<SpriteRenderer>();
                if (cardBgSr != null)
                {
                    Color.RGBToHSV(color, out float h, out float s, out float v);
                    cardBgSr.color = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.75f), Mathf.Clamp01(v * 0.18f));
                }

                // Glow halos (sort orders above bg=500, below card face=502)
                AddGlow(newRoleObj, color, scale: 1.18f, alpha: 0.35f, z: 0.8f,  sortOrder: 10000);
                AddGlow(newRoleObj, color, scale: 1.32f, alpha: 0.15f, z: 1.2f,  sortOrder: 10000);

                // Icon
                if (roleImage != null)
                {
                    roleImage.sprite = isRandom
                        ? TouRoleIcons.RandomAny.LoadAsset()
                        : TryGetRoleSprite(roleName) ?? roleImage.sprite;
                }

                // Button
                var btn = actualCard.GetComponent<PassiveButton>();
                if (btn != null)
                {
                    btn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    btn.OnClick.AddListener((System.Action)(() => OnCardClicked(idx)));

                    var glowSrs = newRoleObj.GetComponentsInChildren<SpriteRenderer>();

                    btn.OnMouseOver = new UnityEngine.Events.UnityEvent();
                    btn.OnMouseOver.AddListener((System.Action)(() =>
                    {
                        newRoleObj.transform.localScale = Vector3.one * 0.88f;
                        foreach (var sr in glowSrs)
                            if (sr.gameObject.name == "Glow")
                                sr.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(sr.color.a * 2.2f));
                    }));

                    btn.OnMouseOut = new UnityEngine.Events.UnityEvent();
                    btn.OnMouseOut.AddListener((System.Action)(() =>
                    {
                        newRoleObj.transform.localScale = Vector3.one * 0.78f;
                        foreach (var sr in glowSrs)
                            if (sr.gameObject.name == "Glow")
                                sr.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(sr.color.a / 2.2f));
                    }));

                    var rollover = actualCard.GetComponent<ButtonRolloverHandler>();
                    if (rollover != null) rollover.OverColor = color;
                }

                DraftModePlugin.Logger.LogInfo($"[DraftScreenController] Card {i} ({roleName}) spawned.");
            }

            // Final sweep: force ALL SpriteRenderers and TMP renderers onto "UI" layer above bg (500)
            foreach (var sr in _screenRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.sortingLayerName = "UI";
                if (sr.sortingOrder < 10000) sr.sortingOrder = 10000;
            }
            foreach (var tmp in _screenRoot.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                var r = tmp.GetComponent<Renderer>();
                if (r != null) { r.sortingLayerName = "UI"; if (r.sortingOrder < 10001) r.sortingOrder = 10001; }
            }

            DraftModePlugin.Logger.LogInfo("[DraftScreenController] Screen built.");
        }

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_hasPicked || _statusText == null) return;
            if (!DraftManager.IsDraftActive) return;
            int secs = Mathf.Max(0, Mathf.CeilToInt(DraftManager.TurnTimeLeft));
            _statusText.text =
                $"<color=#FFFFFF><b>Pick Your Role!</b></color>   " +
                $"<color={(secs <= 5 ? "#FF5555" : "#FFD700")}>" +
                $"{secs} Second{(secs != 1 ? "s" : "")} Remain</color>";
        }

        // ── Pick ──────────────────────────────────────────────────────────────

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;

            string label = (index < 3 && _offeredRoles != null && index < _offeredRoles.Length)
                ? _offeredRoles[index] : "Random";
            DraftModePlugin.Logger.LogInfo($"[DraftScreenController] Picked {index} ({label}).");

            DraftNetworkHelper.SendPickToHost(index);
            Invoke(nameof(DestroySelf), 1.2f);
        }

        private void DestroySelf() => Hide();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Sprite TryGetRoleSprite(string roleName)
        {
            try
            {
                if (RoleManager.Instance == null) return null;
                string norm = roleName.Replace(" ", "").ToLowerInvariant();
                foreach (var role in RoleManager.Instance.AllRoles)
                {
                    if (role == null) continue;
                    if ((role.NiceName ?? "").Replace(" ", "").ToLowerInvariant() != norm) continue;

                    // ICustomRole stores the icon in Configuration.Icon
                    if (role is MiraAPI.Roles.ICustomRole cr && cr.Configuration.Icon != null)
                        return cr.Configuration.Icon.LoadAsset();

                    // Base RoleBehaviour has RoleIconSolid on the AU assembly
                    if (role.RoleIconSolid != null)
                        return role.RoleIconSolid;
                }
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftScreenController] TryGetRoleSprite failed for '{roleName}': {ex.Message}");
            }
            return null;
        }

        private static void AddGlow(GameObject parent, Color color, float scale, float alpha, float z, int sortOrder)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localScale    = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite           = GetWhiteSprite();
            sr.color            = new Color(color.r, color.g, color.b, alpha);
            sr.sortingLayerName = "UI";
            sr.sortingOrder     = sortOrder;
        }

        private static Sprite _white;
        private static Sprite GetWhiteSprite()
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

        private static string GetFactionLabel(string roleName) =>
            RoleCategory.GetFaction(roleName) switch
            {
                RoleFaction.Impostor => "Impostor",
                RoleFaction.Neutral  => "Neutral",
                _                    => "Crewmate"
            };
    }
}
