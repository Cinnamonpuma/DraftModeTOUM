using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using MiraAPI.LocalSettings.Attributes;
using MiraAPI.LocalSettings.SettingTypes;
using TownOfUs.Assets;

namespace DraftModeTOUM;

/// <summary>
/// Per-client local settings — only for things that should NOT be synced to all players.
/// Currently: UI style override (each player can choose their own draft picker style).
/// </summary>
public sealed class DraftModeLocalSettings(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Draft Mode";
    protected override bool ShouldCreateLabels => true;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabIcon = TouAssets.TouMiraIcon
    };

    /// <summary>
    /// When ON, this player ignores the host's UseCircleStyle game option and
    /// uses their own preferred UI style instead.
    /// </summary>
    [LocalToggleSetting]
    public ConfigEntry<bool> OverrideUiStyle { get; private set; } =
        config.Bind("DraftLocal", "OverrideUiStyle", false);

    /// <summary>
    /// The UI style this player prefers when OverrideUiStyle is ON.
    /// Off = Cards, On = Circle.
    /// </summary>
    [LocalToggleSetting]
    public ConfigEntry<bool> UseCircleStyle { get; private set; } =
        config.Bind("DraftLocal", "UseCircleStyle", false);
}
