using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

public sealed record XuiPreviewProperty(
    string Target,
    string Property,
    string Value);

public sealed record XuiPreviewScenario(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<XuiPreviewProperty> Properties,
    IReadOnlySet<string> ForceShownTargets)
{
    public IReadOnlyDictionary<string, string>? PropertiesFor(
        string nodeId,
        string nodeKey)
    {
        Dictionary<string, string>? result = null;
        foreach (XuiPreviewProperty property in Properties)
        {
            if (!property.Target.Equals(nodeId, StringComparison.Ordinal) &&
                !property.Target.Equals(nodeKey, StringComparison.Ordinal))
            {
                continue;
            }

            result ??= new Dictionary<string, string>(StringComparer.Ordinal);
            result[property.Property] = property.Value;
        }

        return result;
    }

    public static XuiPreviewScenario Empty { get; } = new(
        "authored",
        "Composed state",
        "Settles independent animation scopes at their earliest fully visible pose without changing authored data.",
        [],
        new HashSet<string>(StringComparer.Ordinal));

    public override string ToString() => DisplayName;
}

public sealed record XuiRenderContext(
    XuiPreviewScenario? Scenario = null,
    IReadOnlySet<string>? ForceShownTargets = null,
    IReadOnlySet<string>? ForceHiddenTargets = null,
    bool ResolveLocalization = true,
    XuiControllerRuntimeProfile? ControllerRuntimeProfile = null,
    bool ApplyCommonControllerProfile = true)
{
    public XuiPreviewScenario EffectiveScenario =>
        Scenario ?? XuiPreviewScenario.Empty;

    public bool IsForceShown(string id, string key) =>
        EffectiveScenario.ForceShownTargets.Contains(id) ||
        EffectiveScenario.ForceShownTargets.Contains(key) ||
        ForceShownTargets?.Contains(id) == true ||
        ForceShownTargets?.Contains(key) == true;

    public bool IsForceHidden(string id, string key) =>
        ForceHiddenTargets?.Contains(id) == true ||
        ForceHiddenTargets?.Contains(key) == true;

    public IReadOnlyDictionary<string, string>? PropertiesFor(
        string id,
        string key)
    {
        IReadOnlyDictionary<string, string>? controller =
            ControllerRuntimeProfile?.PropertiesFor(id, key);
        IReadOnlyDictionary<string, string>? scenario =
            EffectiveScenario.PropertiesFor(id, key);
        if (controller is null)
        {
            return scenario;
        }

        if (scenario is null)
        {
            return controller;
        }

        Dictionary<string, string> combined =
            new(StringComparer.Ordinal);
        foreach ((string property, string value) in controller)
        {
            combined[property] = value;
        }

        foreach ((string property, string value) in scenario)
        {
            combined[property] = value;
        }

        return combined;
    }
}

public static class XuiPreviewScenarioCatalog
{
    public static IReadOnlyList<XuiPreviewScenario> Defaults { get; } =
    [
        XuiPreviewScenario.Empty,
        Scenario(
            "gameplay",
            "Gameplay HUD",
            "Health, stamina, interaction, quest, minimap, and weapon HUD placeholders.",
            [
                ("T_Hp0", "Text", "5"),
                ("T_Hp1", "Text", "7"),
                ("T_Hp2", "Text", "2"),
                ("T_Medkit0", "Text", "9"),
                ("T_Medkit1", "Text", "9"),
                ("T_Action", "Text", "[F]  Takedown"),
                ("T_QuestName", "Text", "HUNTING GOON"),
                ("T_QuestDescription", "Text", "Dahlia requires ingredients for her potion\n0/1 Putrescent Liver"),
                ("HudStorageObjective", "Show", "false"),
                ("HudQuartermasterObjective", "Show", "false"),
                ("HudPrimeGlandsBarrelObjective", "Show", "false"),
                ("HudAssistObjective", "Show", "false"),
                ("HudNestInfo", "Show", "false"),
                ("HudBounty", "Show", "false"),
                ("HudAchProgress", "Show", "false"),
                ("HudQuestObjective", "Show", "false"),
            ],
            "G_Hp",
            "G_Medkits",
            "I_MedpacksBack",
            "G_Cross",
            "I_Back",
            "G_Bar",
            "G_Icon",
            "I_NoiseIcon",
            "HudTrigger",
            "HudTrackQuest"),
        Scenario(
            "bozak_timer",
            "Bozak timer",
            "Bozak trial heading, countdown, and player count.",
            [
                ("T_Timer", "Text", "00:23.6"),
                ("T_AlternativeGaolTimer", "Text", "00:23.6"),
                ("T_Trial", "Text", "TRIAL 1"),
                ("T_Players", "Text", "3"),
            ],
            "HudChallengeInProgress",
            "HudBombTimer",
            "HudStadiumTimer"),
        Scenario(
            "bozak_complete",
            "Bozak complete",
            "Trial-complete banner and completion time.",
            [
                ("T_Timer", "Text", "00:15"),
                ("T_Complete", "Text", "TRIAL COMPLETE"),
            ],
            "HudStadiumTimer",
            "HudBigMessages",
            "G_BozakWaveEnd"),
        Scenario(
            "weapon_wheel",
            "Weapon wheel",
            "Weapon wheel names, ammunition, stats, and material counts.",
            [
                ("T_Name", "Text", "SPECTRAL BASEBALL BAT (MODIFIED)"),
                ("T_Ammo", "Text", "45"),
                ("T_Damage", "Text", "3989"),
                ("T_Durability", "Text", "118/125"),
                ("T_Handling", "Text", "99"),
                ("T_Cash", "Text", "$1793872495"),
            ],
            "HudWeaponSelection"),
        Scenario(
            "loot",
            "Loot prompt",
            "World loot prompt with item name and stats.",
            [
                ("T_Name", "Text", "Hunter's Machete"),
                ("T_ItemName", "Text", "Hunter's Machete"),
                ("T_Damage", "Text", "456"),
                ("T_Durability", "Text", "49"),
                ("T_Handling", "Text", "73"),
            ],
            "HudItemPickup"),
        Scenario(
            "subtitles",
            "Subtitles",
            "Representative two-line subtitle copy.",
            [
                ("T_Subtitle", "Text", "Bozak: Hear that? The bomb on your ankle just activated. Let's see if you can get to the van in time to disarm it!"),
            ]),
        Scenario(
            "chat",
            "Chat",
            "Representative multiplayer chat and input placeholders.",
            [
                ("T_Chat", "Text", "Light: test"),
                ("T_Input", "Text", "Say: type to chat..."),
            ]),
        Scenario(
            "warning",
            "Warning dialog",
            "Warning title, body, and yes/no actions.",
            [
                ("T_Title", "Text", "WARNING!"),
                ("T_Question", "Text", "Are you sure you want to continue?"),
                ("T_Yes", "Text", "YES"),
                ("T_No", "Text", "NO"),
            ]),
        Scenario(
            "challenge",
            "Challenge briefing",
            "Challenge objective, rules, difficulty, and reward.",
            [
                ("T_Title", "Text", "BEING A HERO"),
                ("T_Objective", "Text", "Reach 6 checkpoints within given time."),
                ("T_Rules", "Text", "Using Grappling Hook is not allowed."),
                ("T_Difficulty", "Text", "Medium"),
                ("T_Reward", "Text", "2500"),
            ]),
        Scenario(
            "pause",
            "Pause and friends",
            "Pause-menu session and friends-list placeholders.",
            [
                ("T_PlayerName", "Text", "Light"),
                ("T_CurrentSession", "Text", "CURRENT SESSION: Nightmare"),
                ("T_FriendsCount", "Text", "0/30"),
            ]),
        Scenario(
            "shop",
            "Shop",
            "Shop item list, price, statistics, and cash placeholders.",
            [
                ("T_ItemName", "Text", "EXTRAVAGANT WRECKING SLEDGEHAMMER"),
                ("T_Price", "Text", "$28526"),
                ("T_Damage", "Text", "3391"),
                ("T_Durability", "Text", "56/56"),
                ("T_Handling", "Text", "15"),
                ("T_Cash", "Text", "$1793872495"),
            ]),
    ];

    private static XuiPreviewScenario Scenario(
        string id,
        string displayName,
        string description,
        IReadOnlyList<(string Target, string Property, string Value)> values,
        params string[] additionalForceShownTargets)
    {
        XuiPreviewProperty[] properties = values
            .Select(static value => new XuiPreviewProperty(
                value.Target,
                value.Property,
                value.Value))
            .ToArray();
        HashSet<string> explicitlyHidden = properties
            .Where(static property =>
                property.Property.Equals(
                    "Show",
                    StringComparison.OrdinalIgnoreCase) &&
                XuiValueParser.TryBoolean(property.Value, out bool shown) &&
                !shown)
            .Select(static property => property.Target)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> forceShown = properties
            .Where(property => !explicitlyHidden.Contains(property.Target))
            .Select(static property => property.Target)
            .ToHashSet(StringComparer.Ordinal);
        forceShown.UnionWith(additionalForceShownTargets);
        return new XuiPreviewScenario(
            id,
            displayName,
            description,
            properties,
            forceShown);
    }
}
