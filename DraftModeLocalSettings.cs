using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using MiraAPI.LocalSettings.Attributes;
using MiraAPI.LocalSettings.SettingTypes;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using UnityEngine;

namespace DraftModeTOUM;

public sealed class DraftModeLocalSettings(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Draft Mode";
    protected override bool ShouldCreateLabels => true;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabIcon = TouAssets.TouMiraIcon
    };

    [LocalSettingsButton]
    public LocalSettingsButton StartDraftButton { get; private set; } =
        new("Start Draft (Host)", StartDraftClicked);

    [LocalToggleSetting]
    public ConfigEntry<bool> EnableDraftToggle { get; private set; } =
        config.Bind("Draft", "EnableDraft", true);

    [LocalToggleSetting]
    public ConfigEntry<bool> LockLobbyOnDraftStart { get; private set; } =
        config.Bind("Draft", "LockLobbyOnDraftStart", true);

    [LocalToggleSetting]
    public ConfigEntry<bool> AutoStartAfterDraft { get; private set; } =
        config.Bind("Draft", "AutoStartAfterDraft", true);

    [LocalToggleSetting]
    public ConfigEntry<bool> ShowRecap { get; private set; } =
        config.Bind("Draft", "ShowRecap", true);

    [LocalToggleSetting]
    public ConfigEntry<bool> UseRoleChances { get; private set; } =
        config.Bind("Draft", "UseRoleChances", true);

    [LocalNumberSetting(min: 5f, max: 60f, increment: 1f, suffixType: MiraNumberSuffixes.Seconds, formatString: "0")]
    public ConfigEntry<float> TurnDuration { get; private set; } =
        config.Bind("Draft", "TurnDurationSeconds", 10f);

    [LocalNumberSetting(min: 0f, max: 5f, increment: 1f, suffixType: MiraNumberSuffixes.None, formatString: "0")]
    public ConfigEntry<float> MaxImpostors { get; private set; } =
        config.Bind("Draft", "MaxImpostors", 2f);

    [LocalNumberSetting(min: 0f, max: 10f, increment: 1f, suffixType: MiraNumberSuffixes.None, formatString: "0")]
    public ConfigEntry<float> MaxNeutrals { get; private set; } =
        config.Bind("Draft", "MaxNeutrals", 3f);

    private static void StartDraftClicked()
    {
        if (!AmongUsClient.Instance.AmHost)
        {
            DraftModePlugin.Logger.LogInfo("[DraftMode] Start Draft clicked by non-host.");
            DraftModeTOUM.Managers.DraftManager.SendChatLocal("<color=red>Only host can start draft.</color>");
            return;
        }

        if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined)
        {
            DraftModeTOUM.Managers.DraftManager.SendChatLocal("<color=red>Draft can only be started in the lobby.</color>");
            return;
        }

        if (DraftModeTOUM.Managers.DraftManager.IsDraftActive)
        {
            DraftModeTOUM.Managers.DraftManager.SendChatLocal("<color=red>Draft already active.</color>");
            return;
        }

        if (!LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance.EnableDraftToggle.Value)
        {
            DraftModeTOUM.Managers.DraftManager.SendChatLocal("<color=red>Draft Mode is disabled in settings.</color>");
            return;
        }

        DraftModeTOUM.Managers.DraftManager.StartDraft();
    }
}
