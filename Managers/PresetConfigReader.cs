using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DraftModeTOUM.Managers
{
public class PresetReadResult
{
/// <summary>Roles with Num >= 1, mapped to their Num value.</summary>
public Dictionary<string, int> RoleNums { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

// Faction slot totals straight from the config
public int TotalImpostorSlots { get; set; }
public int TotalNeutralKillingSlots { get; set; }
public int TotalNeutralEvilSlots { get; set; }
public int TotalNeutralBenignSlots { get; set; }
public int TotalNeutralOutlierSlots { get; set; }
}

public static class PresetConfigReader
{
// Regex: "Num TownOfUs.Roles.<Faction>.<RoleName> = <value>"
private static readonly Regex NumLine = new Regex(
@"^Num TownOfUs\.Roles\.\w+\.(\w+)\s*=\s*(\d+)",
RegexOptions.Compiled);

public static PresetReadResult Read(string presetFilePath)
{
var result = new PresetReadResult();

if (!File.Exists(presetFilePath))
{
DraftModePlugin.Logger.LogWarning(
$"[PresetConfigReader] File not found: {presetFilePath}");
return result;
}

foreach (var line in File.ReadAllLines(presetFilePath))
{
var match = NumLine.Match(line.Trim());
if (!match.Success) continue;

// Strip "Role" suffix to get the clean name (e.g. "AmnesiacRole" → "Amnesiac")
string rawName = match.Groups[1].Value;
string roleName = rawName.EndsWith("Role", StringComparison.OrdinalIgnoreCase)
? rawName[..^4]
: rawName;

// Handle "EngineerTou" → "Engineer"
if (roleName.Equals("EngineerTou", StringComparison.OrdinalIgnoreCase))
roleName = "Engineer";

// Handle "TimeLord" — config writes "TimeLordRole", normalises fine
if (!int.TryParse(match.Groups[2].Value, out int num)) continue;
if (num <= 0) continue;

result.RoleNums[roleName] = num;

// Accumulate subalignment slot totals
var sub = RoleCategory.GetSubAlignment(roleName);
switch (sub)
{
case RoleSubAlignment.ImpostorConcealing:
case RoleSubAlignment.ImpostorKilling:
case RoleSubAlignment.ImpostorPower:
case RoleSubAlignment.ImpostorSupport:
result.TotalImpostorSlots += num; break;
case RoleSubAlignment.NeutralKilling:
result.TotalNeutralKillingSlots += num; break;
case RoleSubAlignment.NeutralEvil:
result.TotalNeutralEvilSlots += num; break;
case RoleSubAlignment.NeutralBenign:
result.TotalNeutralBenignSlots += num; break;
case RoleSubAlignment.NeutralOutlier:
result.TotalNeutralOutlierSlots += num; break;
}
}

DraftModePlugin.Logger.LogInfo(
$"[PresetConfigReader] Loaded {result.RoleNums.Count} enabled roles. " +
$"Imp slots: {result.TotalImpostorSlots}, " +
$"NK: {result.TotalNeutralKillingSlots}, " +
$"NE: {result.TotalNeutralEvilSlots}, " +
$"NB: {result.TotalNeutralBenignSlots}, " +
$"NO: {result.TotalNeutralOutlierSlots}");

return result;
}

/// <summary>
/// Finds the most recently modified .cfg in mira_presets, which is the active preset.
/// </summary>
public static string FindActivePreset()
{
string folder = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
"AppData", "LocalLow", "Innersloth", "Among Us", "mira_presets");

if (!Directory.Exists(folder))
{
DraftModePlugin.Logger.LogWarning(
$"[PresetConfigReader] mira_presets folder not found: {folder}");
return null;
}

string[] files = Directory.GetFiles(folder, "*.cfg");
if (files.Length == 0)
{
DraftModePlugin.Logger.LogWarning(
"[PresetConfigReader] No .cfg files found in mira_presets.");
return null;
}

// Most recently written = active preset
string newest = null;
DateTime newestTime = DateTime.MinValue;
foreach (var f in files)
{
var t = File.GetLastWriteTime(f);
if (t > newestTime) { newestTime = t; newest = f; }
}

DraftModePlugin.Logger.LogInfo($"[PresetConfigReader] Active preset: {newest}");
return newest;
}
}
}
