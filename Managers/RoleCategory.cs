using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
public enum RoleFaction
{
Crewmate,
Impostor,
Neutral
}

public enum RoleSubAlignment
{
// Crewmate
CrewInvestigative,
CrewKilling,
CrewPower,
CrewProtective,
CrewSupport,
// Impostor
ImpostorConcealing,
ImpostorKilling,
ImpostorPower,
ImpostorSupport,
// Neutral
NeutralBenign,
NeutralEvil,
NeutralKilling,
NeutralOutlier,
// Fallback
Unknown
}

public static class RoleCategory
{
private static readonly Dictionary<string, (RoleFaction faction, RoleSubAlignment sub)>
RoleMap = new Dictionary<string, (RoleFaction, RoleSubAlignment)>(
System.StringComparer.OrdinalIgnoreCase)
{
// Crew Investigative
{ "Aurial", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Forensic", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Haunter", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Investigator", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Lookout", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Mystic", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Seer", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Snitch", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Sonar", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Spy", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
{ "Trapper", (RoleFaction.Crewmate, RoleSubAlignment.CrewInvestigative) },
// Crew Killing
{ "Deputy", (RoleFaction.Crewmate, RoleSubAlignment.CrewKilling) },
{ "Hunter", (RoleFaction.Crewmate, RoleSubAlignment.CrewKilling) },
{ "Sheriff", (RoleFaction.Crewmate, RoleSubAlignment.CrewKilling) },
{ "Veteran", (RoleFaction.Crewmate, RoleSubAlignment.CrewKilling) },
{ "Vigilante", (RoleFaction.Crewmate, RoleSubAlignment.CrewKilling) },
// Crew Power
{ "Jailor", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "Mayor", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "Monarch", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "Politician", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "Prosecutor", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "Swapper", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
{ "TimeLord", (RoleFaction.Crewmate, RoleSubAlignment.CrewPower) },
// Crew Protective
{ "Altruist", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
{ "Cleric", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
{ "Medic", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
{ "Mirrorcaster", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
{ "Oracle", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
{ "Warden", (RoleFaction.Crewmate, RoleSubAlignment.CrewProtective) },
// Crew Support
{ "Engineer", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
{ "Imitator", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
{ "Medium", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
{ "Plumber", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
{ "Sentry", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
{ "Transporter", (RoleFaction.Crewmate, RoleSubAlignment.CrewSupport) },
// Impostor Concealing
{ "Eclipsal", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
{ "Escapist", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
{ "Grenadier", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
{ "Morphling", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
{ "Swooper", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
{ "Venerer", (RoleFaction.Impostor, RoleSubAlignment.ImpostorConcealing) },
// Impostor Killing
{ "Ambusher", (RoleFaction.Impostor, RoleSubAlignment.ImpostorKilling) },
{ "Bomber", (RoleFaction.Impostor, RoleSubAlignment.ImpostorKilling) },
{ "Parasite", (RoleFaction.Impostor, RoleSubAlignment.ImpostorKilling) },
{ "Scavenger", (RoleFaction.Impostor, RoleSubAlignment.ImpostorKilling) },
{ "Warlock", (RoleFaction.Impostor, RoleSubAlignment.ImpostorKilling) },
// Impostor Power
{ "Ambassador", (RoleFaction.Impostor, RoleSubAlignment.ImpostorPower) },
{ "Puppeteer", (RoleFaction.Impostor, RoleSubAlignment.ImpostorPower) },
{ "Spellslinger", (RoleFaction.Impostor, RoleSubAlignment.ImpostorPower) },
{ "Traitor", (RoleFaction.Impostor, RoleSubAlignment.ImpostorPower) },
// Impostor Support
{ "Blackmailer", (RoleFaction.Impostor, RoleSubAlignment.ImpostorSupport) },
{ "Hypnotist", (RoleFaction.Impostor, RoleSubAlignment.ImpostorSupport) },
{ "Janitor", (RoleFaction.Impostor, RoleSubAlignment.ImpostorSupport) },
{ "Miner", (RoleFaction.Impostor, RoleSubAlignment.ImpostorSupport) },
{ "Undertaker", (RoleFaction.Impostor, RoleSubAlignment.ImpostorSupport) },
// Neutral Benign
{ "Amnesiac", (RoleFaction.Neutral, RoleSubAlignment.NeutralBenign) },
{ "Fairy", (RoleFaction.Neutral, RoleSubAlignment.NeutralBenign) },
{ "Mercenary", (RoleFaction.Neutral, RoleSubAlignment.NeutralBenign) },
{ "Survivor", (RoleFaction.Neutral, RoleSubAlignment.NeutralBenign) },
// Neutral Evil
{ "Doomsayer", (RoleFaction.Neutral, RoleSubAlignment.NeutralEvil) },
{ "Executioner", (RoleFaction.Neutral, RoleSubAlignment.NeutralEvil) },
{ "Jester", (RoleFaction.Neutral, RoleSubAlignment.NeutralEvil) },
{ "Spectre", (RoleFaction.Neutral, RoleSubAlignment.NeutralEvil) },
// Neutral Killing
{ "Arsonist", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Glitch", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Juggernaut", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Plaguebearer", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Pestilence", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "SoulCollector", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Vampire", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
{ "Werewolf", (RoleFaction.Neutral, RoleSubAlignment.NeutralKilling) },
// Neutral Outlier
{ "Chef", (RoleFaction.Neutral, RoleSubAlignment.NeutralOutlier) },
{ "Inquisitor", (RoleFaction.Neutral, RoleSubAlignment.NeutralOutlier) },
};

public static RoleFaction GetFaction(string roleName)
{
if (RoleMap.TryGetValue(roleName, out var entry)) return entry.faction;
return RoleFaction.Crewmate;
}

public static RoleSubAlignment GetSubAlignment(string roleName)
{
if (RoleMap.TryGetValue(roleName, out var entry)) return entry.sub;
return RoleSubAlignment.Unknown;
}

public static bool IsImpostor(string roleName) => GetFaction(roleName) == RoleFaction.Impostor;
public static bool IsNeutral(string roleName) => GetFaction(roleName) == RoleFaction.Neutral;
public static bool IsNeutralKilling(string roleName) => GetSubAlignment(roleName) == RoleSubAlignment.NeutralKilling;
public static bool IsNeutralEvil(string roleName) => GetSubAlignment(roleName) == RoleSubAlignment.NeutralEvil;
public static bool IsNeutralBenign(string roleName) => GetSubAlignment(roleName) == RoleSubAlignment.NeutralBenign;
public static bool IsNeutralOutlier(string roleName) => GetSubAlignment(roleName) == RoleSubAlignment.NeutralOutlier;
}
}
