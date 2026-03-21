using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.Patches.Stubs;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using Reactor.Utilities;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM;

// Circle-style draft UI — roles arranged in a spinning wheel
public sealed class DraftCircleMinigame : Minigame
{
    public Transform? RolesHolder;
    public GameObject? RolePrefab;
    public TextMeshPro? StatusText;
    public TextMeshPro? RoleName;
    public SpriteRenderer? RoleIcon;
    public TextMeshPro? RoleTeam;
    public GameObject? RedRing;
    public GameObject? WarpRing;
    public TextMeshPro? TurnListText;

    private GameObject? _statusGo;
    private GameObject? _roleNameGo;
    private GameObject? _roleTeamGo;
    private GameObject? _roleIconGo;
    private GameObject? _turnListGo;

    public static int CurrentCard { get; set; }
    public static int RoleCount { get; set; }

    private readonly Color _bgColor = new Color32(24, 0, 0, 215);

    private List<DraftRoleCard>? _cards;
    private Action<int>? _onPick;
    private bool _hasPicked;

    public DraftCircleMinigame(IntPtr cppPtr) : base(cppPtr) { }

    private void Awake()
    {
        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Awake() called.");
        if (Instance) Instance.Close();

        RolesHolder = transform.FindChild("Roles");
        RolePrefab = transform.FindChild("RoleCardHolder").gameObject;

        var status = transform.FindChild("Status");
        _statusGo = status.gameObject;
        var roleNameTr = status.FindChild("RoleName");
        var roleTeamTr = status.FindChild("RoleTeam");
        var roleImageTr = status.FindChild("RoleImage");
        _roleNameGo = roleNameTr.gameObject;
        _roleTeamGo = roleTeamTr.gameObject;
        _roleIconGo = roleImageTr.gameObject;
        StatusText = status.GetComponent<TextMeshPro>();
        RoleName = roleNameTr.GetComponent<TextMeshPro>();
        RoleTeam = roleTeamTr.GetComponent<TextMeshPro>();
        RoleIcon = roleImageTr.GetComponent<SpriteRenderer>();
        RedRing = status.FindChild("RoleRing").gameObject;
        WarpRing = status.FindChild("RingWarp").gameObject;

        var font = HudManager.Instance.TaskPanel.taskText.font;
        var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

        StatusText.font = font; StatusText.fontMaterial = fontMat;
        StatusText.text = "Draft Pick";
        _statusGo.SetActive(false);

        RoleName.font = font; RoleName.fontMaterial = fontMat;
        RoleName.text = " "; _roleNameGo.SetActive(false);

        RoleTeam.font = font; RoleTeam.fontMaterial = fontMat;
        RoleTeam.text = " "; _roleTeamGo.SetActive(false);

        RoleIcon.sprite = TouRoleIcons.RandomAny.LoadAsset();
        _roleIconGo.SetActive(false);
        RedRing.SetActive(false);
        WarpRing.SetActive(false);

        var listGo = new GameObject("DraftTurnList");
        listGo.transform.SetParent(transform, false);
        listGo.transform.localPosition = new Vector3(-4.2f, 1.8f, -1f);

        _turnListGo = listGo;
        TurnListText = listGo.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();
        TurnListText.font = font;
        TurnListText.fontMaterial = fontMat;
        TurnListText.fontSize = 1.5f;
        TurnListText.alignment = TextAlignmentOptions.TopLeft;
        TurnListText.enableWordWrapping = false;
        TurnListText.text = "";
        listGo.SetActive(false);

        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Awake() completed.");
    }

    public static DraftCircleMinigame Create()
    {
        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Create() called.");
        var go = Instantiate(TouAssets.AltRoleSelectionGame.LoadAsset(), HudManager.Instance.transform);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Minigame>());
        go.SetActive(false);
        var result = go.AddComponent<DraftCircleMinigame>();
        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Create() complete.");
        return result;
    }

    [HideFromIl2Cpp]
    public void Open(List<DraftRoleCard> cards, Action<int> onPick)
    {
        DraftModePlugin.Logger.LogInfo($"[DraftCircleMinigame] Open() called with {cards.Count} cards.");
        _cards = cards;
        _onPick = onPick;
        _hasPicked = false;
        RoleCount = cards.Count + 1;
        CurrentCard = 0;
        Coroutines.Start(CoOpen(this));
    }

    private static IEnumerator CoOpen(DraftCircleMinigame minigame)
    {
        while (ExileController.Instance != null)
            yield return new WaitForSeconds(0.65f);
        minigame.gameObject.SetActive(true);
        minigame.Begin();
    }

    public override void Close()
    {
        _hasPicked = true;
        _onPick = null;  // prevent any late callback firing
        CurrentCard = -1;
        RoleCount = -1;

        // Clear the full-screen overlay immediately
        try
        {
            if (HudManager.Instance?.FullScreen != null)
            {
                HudManager.Instance.FullScreen.gameObject.SetActive(false);
                HudManager.Instance.FullScreen.color = Color.clear;
            }
        }
        catch { }

        // Hide immediately then DestroyImmediate so there is no deferred-destroy
        // window where the GO is still alive but _circleMinigame is already null.
        // This was causing the circle to stay visible on the last pick.
        try
        {
            gameObject.SetActive(false);
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
        catch
        {
            // Fall back to deferred destroy if DestroyImmediate throws
            try { UnityEngine.Object.Destroy(gameObject); } catch { }
        }
    }

    [HideFromIl2Cpp]
    public void RefreshTurnList()
    {
        if (TurnListText == null) return;
        TurnListText.text = BuildTurnListText();
    }

    [HideFromIl2Cpp]
    private static string BuildTurnListText()
    {
        var sb = new System.Text.StringBuilder();
        int mySlot = Managers.DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
        if (mySlot > 0)
            sb.AppendLine($"<color=#00FFFF><b>You are Pick #{mySlot}</b></color>\n");
        sb.AppendLine("<b>── Draft Order ──</b>");
        foreach (int slot in Managers.DraftManager.TurnOrder)
        {
            var state = Managers.DraftManager.GetStateForSlot(slot);
            if (state == null) continue;
            bool isMe = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
            string me = isMe ? " ◀" : "";
            string label, color;
            if (state.HasPicked) { label = "(Picked)"; color = "#888888"; }
            else if (state.IsPickingNow) { label = "<b>(Picking)</b>"; color = "#FFD700"; }
            else { label = "(Waiting)"; color = "#AAAAAA"; }
            sb.AppendLine($"<color={color}>Pick {slot}... {label}{me}</color>");
        }
        return sb.ToString();
    }

    private void Begin()
    {
        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Begin() called.");
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, _bgColor));

        _statusGo!.SetActive(true);
        _roleNameGo!.SetActive(true);
        _roleTeamGo!.SetActive(true);
        _roleIconGo!.SetActive(true);
        RedRing!.SetActive(true);
        WarpRing!.SetActive(true);
        RoleIcon!.transform.localScale = Vector3.one * 0.35f;
        _turnListGo!.SetActive(true);
        RefreshTurnList();

        if (_cards != null)
        {
            foreach (var card in _cards)
            {
                var btn = CreateCard(card.RoleName, card.TeamName, card.Icon, card.Color);
                int capturedIndex = card.Index;
                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener(new Action(() =>
                {
                    if (_hasPicked) return;
                    _hasPicked = true;
                    var cb = _onPick;
                    _onPick = null;  // prevent any second invocation
                    cb?.Invoke(capturedIndex);
                }));
            }
        }

        Coroutines.Start(CoAnimateCards());
        DraftModePlugin.Logger.LogInfo("[DraftCircleMinigame] Begin() complete.");
    }

    private PassiveButton CreateCard(string roleName, string teamName, Sprite? icon, Color color)
    {
        var newRoleObj = Instantiate(RolePrefab, RolesHolder);
        var actualCard = newRoleObj!.transform.GetChild(0);
        var roleText = actualCard.GetChild(0).gameObject.GetComponent<TextMeshPro>();
        var roleImage = actualCard.GetChild(1).gameObject.GetComponent<SpriteRenderer>();
        var teamText = actualCard.GetChild(2).gameObject.GetComponent<TextMeshPro>();
        var selection = actualCard.GetChild(3).gameObject;
        var passiveButton = actualCard.GetComponent<PassiveButton>();
        var rollover = actualCard.GetComponent<ButtonRolloverHandler>();

        selection.SetActive(false);

        passiveButton.OnMouseOver.AddListener(new Action(() =>
        {
            selection.SetActive(true);
            RoleName!.text = roleName;
            RoleTeam!.text = teamName;
            if (icon != null) RoleIcon!.sprite = icon;
            RoleIcon!.transform.localScale = Vector3.one * 0.35f;
        }));
        passiveButton.OnMouseOut.AddListener(new Action(() => selection.SetActive(false)));

        float angle = (2 * Mathf.PI / RoleCount) * CurrentCard;
        float x = 1.9f * Mathf.Cos(angle);
        float y = 0.1f + 1.9f * Mathf.Sin(angle);

        newRoleObj.transform.localPosition = new Vector3(x, y, -1f);
        newRoleObj.name = roleName + " DraftSelection";

        roleText.text = roleName;
        teamText.text = teamName;
        roleImage.sprite = icon ?? TouRoleIcons.RandomAny.LoadAsset();
        roleImage.transform.localScale = Vector3.one * 0.4f;

        rollover.OverColor = color;
        roleText.color = color;
        teamText.color = color;

        CurrentCard++;
        newRoleObj.gameObject.SetActive(true);
        return passiveButton;
    }

    [HideFromIl2Cpp]
    private IEnumerator CoAnimateCards()
    {
        if (RolesHolder == null) yield break;
        foreach (var o in RolesHolder.transform)
        {
            var card = o.Cast<Transform>();
            if (card == null) continue;
            Coroutines.Start(CoPopIn(card.GetChild(0)));
            yield return new WaitForSeconds(0.01f);
        }
        CurrentCard = -1;
        RoleCount = -1;
    }

    private static IEnumerator CoPopIn(Transform t)
    {
        float targetScale = 0.5f;
        float duration = 0.12f;
        t.localScale = Vector3.zero;
        for (float timer = 0f; timer < duration; timer += Time.deltaTime)
        {
            float scale = Mathf.LerpUnclamped(0f, targetScale, EaseOutBack(timer / duration));
            t.localScale = Vector3.one * scale;
            yield return null;
        }
        t.localScale = Vector3.one * targetScale;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
