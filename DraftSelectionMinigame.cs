using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.Patches.Stubs;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;
using UnityEngine.Events;

namespace DraftModeTOUM;

[RegisterInIl2Cpp]
public sealed class DraftSelectionMinigame : Minigame
{
    // Unity-visible fields
    public Transform?      RolesHolder;
    public GameObject?     RolePrefab;
    public TextMeshPro?    StatusText;
    public TextMeshPro?    RoleName;
    public SpriteRenderer? RoleIcon;
    public TextMeshPro?    RoleTeam;
    public GameObject?     RedRing;
    public GameObject?     WarpRing;
    public TextMeshPro?    TurnListText;

    // Matches Ambassador's static card-tracking pattern exactly
    public static int CurrentCard { get; set; }
    public static int RoleCount   { get; set; }

    private readonly Color _bgColor = new Color32(24, 0, 0, 215);

    // Managed-only — IL2CPP cannot see private fields so no attribute needed
    private List<DraftRoleCard>? _cards;
    private Action<int>?          _onPick;

    public DraftSelectionMinigame(IntPtr cppPtr) : base(cppPtr) { }

    private void Awake()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Awake() called.");
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
        RoleName.text = " ";  RoleName.gameObject.SetActive(false);

        RoleTeam.font = font; RoleTeam.fontMaterial = fontMat;
        RoleTeam.text = " ";  RoleTeam.gameObject.SetActive(false);

        RoleIcon.sprite = TouRoleIcons.RandomAny.LoadAsset();
        RoleIcon.gameObject.SetActive(false);
        RedRing.SetActive(false);
        WarpRing.SetActive(false);

        // Turn order panel (left side)
        var listGo = new GameObject("DraftTurnList");
        listGo.transform.SetParent(transform, false);
        listGo.transform.localPosition = new Vector3(-4.2f, 1.8f, -1f);

        TurnListText = listGo.AddComponent<TextMeshPro>();
        TurnListText.font              = font;
        TurnListText.fontMaterial      = fontMat;
        TurnListText.fontSize          = 1.5f;
        TurnListText.alignment         = TextAlignmentOptions.TopLeft;
        TurnListText.enableWordWrapping = false;
        TurnListText.text              = "";
        TurnListText.gameObject.SetActive(false);

        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Awake() completed.");
    }

    // Mirrors AmbassadorSelectionMinigame.Create() exactly
    public static DraftSelectionMinigame Create()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Create() called.");
        var go = Instantiate(TouAssets.AltRoleSelectionGame.LoadAsset(), HudManager.Instance.transform);
        go.GetComponent<Minigame>().DestroyImmediate();
        go.SetActive(false);
        var result = go.AddComponent<DraftSelectionMinigame>();
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Create() complete.");
        return result;
    }

    [HideFromIl2Cpp]
    public void Open(List<DraftRoleCard> cards, Action<int> onPick)
    {
        DraftModePlugin.Logger.LogInfo($"[DraftSelectionMinigame] Open() called with {cards.Count} cards.");
        _cards  = cards;
        _onPick = onPick;
        // RoleCount includes the Random option (+1), matching Ambassador
        RoleCount = cards.Count + 1;
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
        CurrentCard = -1;
        RoleCount   = -1;
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

    // Mirrors AmbassadorSelectionMinigame.Begin() exactly
    private void Begin()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Begin() called.");
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, _bgColor));

        StatusText!.gameObject.SetActive(true);
        RoleName!.gameObject.SetActive(true);
        RoleTeam!.gameObject.SetActive(true);
        RoleIcon!.gameObject.SetActive(true);
        RedRing!.SetActive(true);
        WarpRing!.SetActive(true);
        RoleIcon.SetSizeLimit(2.8f);

        TurnListText!.gameObject.SetActive(true);
        RefreshTurnList();

        // Create a card for each offered role
        if (_cards != null)
        {
            foreach (var card in _cards)
            {
                var btn = CreateCard(card.RoleName, card.TeamName, card.Icon, card.Color);
                int capturedIndex = card.Index;
                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener((UnityAction)(() =>
                {
                    _onPick?.Invoke(capturedIndex);
                    Close();
                }));
            }
        }

        // Random card — index 3, matching BuildCards convention
        var randomBtn = CreateCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white);
        randomBtn.OnClick.RemoveAllListeners();
        randomBtn.OnClick.AddListener((UnityAction)(() =>
        {
            _onPick?.Invoke(3);
            Close();
        }));

        Coroutines.Start(CoAnimateCards());
        TransType = TransitionType.None;
        MinigameStubs.Begin(this, null);
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Begin() complete.");
    }

    // Mirrors AmbassadorSelectionMinigame.CreateCard() exactly
    private PassiveButton CreateCard(string roleName, string teamName, Sprite? icon, Color color)
    {
        var newRoleObj    = Instantiate(RolePrefab, RolesHolder);
        var actualCard    = newRoleObj!.transform.GetChild(0);
        var roleText      = actualCard.GetChild(0).gameObject.GetComponent<TextMeshPro>();
        var roleImage     = actualCard.GetChild(1).gameObject.GetComponent<SpriteRenderer>();
        var teamText      = actualCard.GetChild(2).gameObject.GetComponent<TextMeshPro>();
        var selection     = actualCard.GetChild(3).gameObject;
        var passiveButton = actualCard.GetComponent<PassiveButton>();
        var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

        selection.SetActive(false);

        passiveButton.OnMouseOver.AddListener((UnityAction)(() =>
        {
            selection.SetActive(true);
            RoleName!.text = roleName;
            RoleTeam!.text = teamName;
            if (icon != null) RoleIcon!.sprite = icon;
            RoleIcon!.SetSizeLimit(2.8f);
        }));
        passiveButton.OnMouseOut.AddListener((UnityAction)(() => selection.SetActive(false)));

        float angle = (2 * Mathf.PI / RoleCount) * CurrentCard;
        float x = 1.9f * Mathf.Cos(angle);
        float y = 0.1f + 1.9f * Mathf.Sin(angle);

        newRoleObj.transform.localPosition = new Vector3(x, y, -1f);
        newRoleObj.name = roleName + " DraftSelection";

        roleText.text  = roleName;
        teamText.text  = teamName;
        roleImage.sprite = icon ?? TouRoleIcons.RandomAny.LoadAsset();
        roleImage.SetSizeLimit(2.8f);

        rollover.OverColor = color;
        roleText.color     = color;
        teamText.color     = color;

        CurrentCard++;
        newRoleObj.gameObject.SetActive(true);
        return passiveButton;
    }

    // Mirrors AmbassadorSelectionMinigame.CoAnimateCards()
    [HideFromIl2Cpp]
    private IEnumerator CoAnimateCards()
    {
        foreach (var o in RolesHolder!.transform)
        {
            var card = o.Cast<Transform>();
            if (card == null) continue;
            var child = card.GetChild(0);
            Coroutines.Start(MiscUtils.BetterBloop(child, finalSize: 0.5f - (RoleCount * 0.0075f), duration: 0.1f, intensity: 0.11f));
            yield return new WaitForSeconds(0.01f);
        }
        CurrentCard = -1;
        RoleCount   = -1;
    }
}

// Plain managed data class — NOT registered in IL2CPP
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
