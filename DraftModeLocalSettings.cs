using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using MiraAPI.LocalSettings.Attributes;
using MiraAPI.LocalSettings.SettingTypes;
using TownOfUs.Assets;

namespace DraftModeTOUM;





public sealed class DraftModeLocalSettings(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Draft Mode";
    protected override bool ShouldCreateLabels => true;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabIcon = TouAssets.TouMiraIcon
    };

    
    
    
    
    [LocalToggleSetting]
    public ConfigEntry<bool> OverrideUiStyle { get; private set; } =
        config.Bind("DraftLocal", "OverrideUiStyle", false);

    
    
    
    
    [LocalToggleSetting]
    public ConfigEntry<bool> UseCircleStyle { get; private set; } =
        config.Bind("DraftLocal", "UseCircleStyle", false);
}

