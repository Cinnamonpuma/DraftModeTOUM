using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.Patches.Stubs;
using MiraAPI.Utilities;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;

namespace DraftModeTOUM;

[RegisterInIl2Cpp]
public sealed class DraftSelectionMinigame : Minigame
{
    // Unity-visible fields (no managed generic types)
    public Transform?      RolesHolder;
    public GameObject?     RolePrefab;
    public TextMeshPro?    StatusText;
    public TextMeshPro?    RoleName;
    public SpriteRenderer? RoleIcon;
    public TextMeshPro?    RoleTeam;
    public GameObject?     RedRing;
    public GameObject?     WarpRing;
    public TextMeshPro?    TurnListText;

    private readonly Color _bgColor = new Color32(24, 0, 0, 215);
    private int _currentCardIndex;

    // Managed-only data — hidden from IL2CPP to avoid bridge failures
    private List<DraftRoleCard>? _cards;
    private Action<int>?          _onPick;

    public DraftSelectionMinigame(IntPtr cppPtr) : base(cppPtr) { }

    private void Awake()
    {
        if (Instance) Instance.Close();

        RolesHolder = transform.FindChild("Roles");
        RolePrefab  = transform.FindChild("RoleCardHolder").gameObject;

        var status = transform.FindChild("Status");
        StatusText = status.gameObject.GetComponent<TextMeshPro>();
        RoleName   = status.FindChild("RoleName").gameObject.GetComponent<TextMeshPro>();
        RoleTeam   = status.FindChild("RoleTeam").gameObject.GetComponent<TextMeshPro>();
        RoleIcon   = status.FindChild("RoleImage").gameObject.GetComponent<SpriteRenderer>();
        RedRing    = status.FindChild("RoleRing").gameObject;
        WarpRing   = status.FindChild("RingWarp").gameObject;

        var font    = HudManager.Instance.TaskPanel.taskText.font;
        var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

        StatusText.font = font; StatusText.fontMaterial = fontMat;
        StatusText.text = "Draft Pick";
        StatusText.gameObject.SetActive(false);

        RoleName.font = font; RoleName.fontMaterial = fontMat;
        RoleName.text = " "; RoleName.gameObject.SetActive(false);

        RoleTeam.font = font; RoleTeam.fontMaterial = fontMat;
        RoleTeam.text = " "; RoleTeam.gameObject.SetActive(false);

        RoleIcon.sprite = TouRoleIcons.RandomAny.LoadAsset();
        RoleIcon.gameObject.SetActive(false);
        RedRing.SetActive(false);
        WarpRing.SetActive(false);

        // Left-side turn order panel
        var listGo = new GameObject("DraftTurnList");
        listGo.transform.SetParent(transform, false);
        listGo.transform.localPosition = new Vector3(-4.2f, 1.8f, -1f);

        TurnListText = listGo.AddComponent<TextMeshPro>();
        TurnListText.font               = font;
        TurnListText.fontMaterial       = fontMat;
        TurnListText.fontSize           = 1.5f;
        TurnListText.alignment          = TextAlignmentOptions.TopLeft;
        TurnListText.enableWordWrapping  = false;
        TurnListText.text               = "";
        TurnListText.gameObject.SetActive(false);
    }

    public static DraftSelectionMinigame? Create()
    {
        var prefab = TouAssets.AltRoleSelectionGame.LoadAsset();
        if (prefab == null)
        {
            DraftModePlugin.Logger.LogError("[DraftSelectionMinigame] TouAssets.AltRoleSelectionGame.LoadAsset() returned null — asset bundle not ready yet!");
            return null;
        }
        if (HudManager.Instance == null)
        {
            DraftModePlugin.Logger.LogError("[DraftSelectionMinigame] HudManager.Instance is null — cannot create minigame.");
            return null;
        }
        var go = Instantiate(prefab, HudManager.Instance.transform);
        var existing = go.GetComponent<Minigame>();
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        go.SetActive(false);
        return go.AddComponent<DraftSelectionMinigame>();
    }

    [HideFromIl2Cpp]
    public void Open(List<DraftRoleCard> cards, Action<int> onPick)
    {
        _cards            = cards;
        _onPick           = onPick;
        _currentCardIndex = 0;
        ClearCards();
        Coroutines.Start(CoOpen(this));
    }

    private static IEnumerator CoOpen(DraftSelectionMinigame minigame)
    {
        while (ExileController.Instance != null)
            yield return new WaitForSeconds(0.65f);
        minigame.gameObject.SetActive(true);
        minigame.Begin();
    }

    public override void Close()
    {
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(_bgColor, Color.clear));
        MinigameStubs.Close(this);
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
            sb.AppendLine($"<color=#00FFFF><b>You are Player #{mySlot}</b></color>\n");

        sb.AppendLine("<b>── Draft Order ──</b>");

        var order = Managers.DraftManager.TurnOrder;
        for (int i = 0; i < order.Count; i++)
        {
            int slot  = order[i];
            var state = Managers.DraftManager.GetStateForSlot(slot);
            if (state == null) continue;

            bool   isMe  = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
            string me    = isMe ? " ◀" : "";
            string label, color;

            if (state.HasPicked)         { label = "(Picked)";         color = "#888888"; }
            else if (state.IsPickingNow) { label = "<b>(Picking)</b>"; color = "#FFD700"; }
            else                         { label = "(Waiting)";        color = "#AAAAAA"; }

            sb.AppendLine($"<color={color}>Player {slot}... {label}{me}</color>");
        }
        return sb.ToString();
    }

    private void Begin()
    {
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, _bgColor));

        StatusText!.gameObject.SetActive(true);
        RoleName!.gameObject.SetActive(true);
        RoleTeam!.gameObject.SetActive(true);
        RoleIcon!.gameObject.SetActive(true);
        RedRing!.SetActive(true);
        WarpRing!.SetActive(true);
        RoleIcon.transform.localScale = Vector3.one * 0.35f;

        TurnListText!.gameObject.SetActive(true);
        RefreshTurnList();

        if (_cards != null)
            foreach (var card in _cards)
                CreateCard(card);

        TransType = TransitionType.None;
        Begin(null);
    }

    private void ClearCards()
    {
        if (RolesHolder == null) return;
        foreach (var child in RolesHolder.transform)
        {
            var t = child.Cast<Transform>();
            if (t != null) Destroy(t.gameObject);
        }
    }

    [HideFromIl2Cpp]
    private void CreateCard(DraftRoleCard card)
    {
        var newRoleObj     = Instantiate(RolePrefab, RolesHolder);
        var actualCard     = newRoleObj!.transform.GetChild(0);
        var roleText       = actualCard.GetChild(0).gameObject.GetComponent<TextMeshPro>();
        var roleImage      = actualCard.GetChild(1).gameObject.GetComponent<SpriteRenderer>();
        var teamText       = actualCard.GetChild(2).gameObject.GetComponent<TextMeshPro>();
        var selection      = actualCard.GetChild(3).gameObject;
        var passiveButton  = actualCard.GetComponent<PassiveButton>();
        var buttonRollover = actualCard.GetComponent<ButtonRolloverHandler>();

        selection.SetActive(false);

        // Capture locals for closures — avoid capturing 'card' directly through IL2CPP boundary
        string roleName = card.RoleName;
        string teamName = card.TeamName;
        Sprite? icon    = card.Icon;
        int     index   = card.Index;
        Color   color   = card.Color;

        passiveButton.OnMouseOver.AddListener(new Action(() =>
        {
            selection.SetActive(true);
            RoleName!.text = roleName;
            RoleTeam!.text = teamName;
            if (icon != null) RoleIcon!.sprite = icon;
            RoleIcon!.transform.localScale = Vector3.one * 0.35f;
        }));
        passiveButton.OnMouseOut.AddListener(new Action(() => { selection.SetActive(false); }));

        int count = _cards?.Count ?? 1;
        float angle = (2 * Mathf.PI / count) * _currentCardIndex;
        float x = 1.9f * Mathf.Cos(angle);
        float y = 0.1f + 1.9f * Mathf.Sin(angle);

        newRoleObj.transform.localPosition = new Vector3(x, y, -1f);
        newRoleObj.name = roleName + " DraftSelection";

        roleText.text    = roleName;
        teamText.text    = teamName;
        roleImage.sprite = icon;
        roleImage.transform.localScale = Vector3.one * 0.4f;

        buttonRollover.OverColor = color;
        roleText.color           = color;
        teamText.color           = color;

        passiveButton.OnClick.RemoveAllListeners();
        passiveButton.OnClick.AddListener(new Action(() =>
        {
            _onPick?.Invoke(index);
            Close();
        }));

        _currentCardIndex++;
        newRoleObj.gameObject.SetActive(true);
    }
}

// Plain managed class — NOT registered in IL2CPP, no bridge issues
public sealed class DraftRoleCard
{
    public string  RoleName { get; }
    public string  TeamName { get; }
    public Sprite? Icon     { get; }
    public Color   Color    { get; }
    public int     Index    { get; }

    public DraftRoleCard(string roleName, string teamName, Sprite? icon, Color color, int index)
    {
        RoleName = roleName;
        TeamName = teamName;
        Icon     = icon;
        Color    = color;
        Index    = index;
    }
}
