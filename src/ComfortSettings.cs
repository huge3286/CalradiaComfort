using MCM.Abstractions.Base.Global;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;

namespace StorySkip
{
    public sealed class ComfortSettings : AttributeGlobalSettings<ComfortSettings>
    {
        private const string Convenience = "{=cc_group_convenience}Convenience";
        private const string Cheats = "{=cc_group_cheats}Cheats";

        // Separate from the original release, whose cheat options defaulted to on.
        public override string Id => "CalradiaComfort.Public";
        public override string DisplayName => ComfortBranding.DisplayName;
        public override string FolderName => "CalradiaComfort.Public";
        public override string FormatType => "json";

        [SettingPropertyBool("{=cc_opt_story}Skip the Opening Story", Order = 0, RequireRestart = false,
            HintText = "{=cc_opt_story_hint}Applies to new campaigns. Reunite your family, obtain the Dragon Banner, and keep its kingdom choice.")]
        [SettingPropertyGroup(Convenience, GroupOrder = 0)]
        public bool SkipOpeningStory { get; set; } = true;

        [SettingPropertyBool("{=cc_opt_recruit}One-Click Recruitment", Order = 1, RequireRestart = false,
            HintText = "{=cc_opt_recruit_hint}Select all available common or noble recruits with one click. Normal prices and recruitment limits apply.")]
        [SettingPropertyGroup(Convenience, GroupOrder = 0)]
        public bool QuickRecruitment { get; set; } = true;

        [SettingPropertyBool("{=cc_opt_achievements}Enable Steam Achievements with Mods", Order = 2, RequireRestart = false,
            HintText = "{=cc_opt_achievements_hint}Earn Steam achievements while playing with mods. You still need to meet each achievement's requirements.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool EnableAchievements { get; set; }

        [SettingPropertyBool("{=cc_opt_perks}Automatically Unlock Both Perks", Order = 3, RequireRestart = false,
            HintText = "{=cc_opt_perks_hint}Heroes in your clan get both perks at each skill milestone instead of choosing one.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool BothPerks { get; set; }

        [SettingPropertyBool("{=cc_opt_growth}Max Out Hero Skills and Attributes", Order = 4, RequireRestart = false,
            HintText = "{=cc_opt_growth_hint}Maxes out attributes, focus points, and skills for adult heroes in your clan. Turning this off keeps the gains already applied.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool LearningLimitSkills { get; set; }

        [SettingPropertyBool("{=cc_opt_xp}Shared Troop XP", Order = 5, RequireRestart = false,
            HintText = "{=cc_opt_xp_hint}All troop types in your party earn XP into one pool. Spend it to upgrade any troop type in that party. Normal XP, gold, and mount costs still apply.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool SharedTroopExperience { get; set; }

        [SettingPropertyBool("{=cc_opt_upgrade}Upgrade Troops to Max Tier in One Click", Order = 6, RequireRestart = false,
            HintText = "{=cc_opt_upgrade_hint}Choose a final troop type from the upgrade tree. Each step costs the usual XP, gold, and mounts. Requires Shared Troop XP.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool UltimateTroopUpgrade { get; set; }

        [SettingPropertyBool("{=cc_opt_party_limit}Set a Custom Clan Party Limit", Order = 7, RequireRestart = false,
            HintText = "{=cc_opt_party_limit_hint}Choose how many parties your clan can field at once.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool ClanPartyLimit { get; set; }

        [SettingPropertyBool("{=cc_opt_companion_limit}Set a Custom Companion Limit", Order = 8, RequireRestart = false,
            HintText = "{=cc_opt_companion_limit_hint}Choose how many companions your clan can have.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool ClanCompanionLimit { get; set; }

        [SettingPropertyBool("{=cc_opt_speed}Speed Up Friendly Parties", Order = 9, RequireRestart = false,
            HintText = "{=cc_opt_speed_hint}Adds a fixed campaign map speed bonus to parties in your faction, including caravans and villagers.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool AlliedPartySpeed { get; set; }

        [SettingPropertyInteger("{=cc_opt_party_value}Max Clan Parties", 1, 100, Order = 10, RequireRestart = false,
            HintText = "{=cc_opt_party_value_hint}Used when Set a Custom Clan Party Limit is on.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public int ClanPartyLimitValue { get; set; } = 20;

        [SettingPropertyInteger("{=cc_opt_companion_value}Max Companions", 1, 100, Order = 11, RequireRestart = false,
            HintText = "{=cc_opt_companion_value_hint}Used when Set a Custom Companion Limit is on.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public int ClanCompanionLimitValue { get; set; } = 20;

        [SettingPropertyFloatingInteger("{=cc_opt_speed_value}Friendly Party Speed Bonus", 0f, 10f, "0.0", Order = 12, RequireRestart = false,
            HintText = "{=cc_opt_speed_value_hint}Used when Speed Up Friendly Parties is on. A value of 1.0 adds 1.0 to map speed.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public float AlliedPartySpeedBonus { get; set; } = 1f;

    }

    internal static class ComfortFeatures
    {
        internal static bool ConfiguredStorySkip => ComfortSettings.Instance?.SkipOpeningStory ?? true;
        internal static bool QuickRecruitmentEnabled => ComfortSettings.Instance?.QuickRecruitment ?? true;
        internal static bool AchievementsEnabled => ComfortSettings.Instance?.EnableAchievements ?? false;
        internal static bool BothPerksEnabled => ComfortSettings.Instance?.BothPerks ?? false;
        internal static bool LearningLimitSkillsEnabled => ComfortSettings.Instance?.LearningLimitSkills ?? false;
        internal static bool SharedTroopExperienceEnabled => ComfortSettings.Instance?.SharedTroopExperience ?? false;
        internal static bool UltimateTroopUpgradeEnabled => ComfortSettings.Instance?.UltimateTroopUpgrade ?? false;
        internal static bool ClanPartyLimitEnabled => ComfortSettings.Instance?.ClanPartyLimit ?? false;
        internal static bool ClanCompanionLimitEnabled => ComfortSettings.Instance?.ClanCompanionLimit ?? false;
        internal static bool AlliedPartySpeedEnabled => ComfortSettings.Instance?.AlliedPartySpeed ?? false;
        internal static int ClanPartyLimitValue => System.Math.Max(1, System.Math.Min(100, ComfortSettings.Instance?.ClanPartyLimitValue ?? 20));
        internal static int ClanCompanionLimitValue => System.Math.Max(1, System.Math.Min(100, ComfortSettings.Instance?.ClanCompanionLimitValue ?? 20));
        internal static float AlliedPartySpeedBonus
        {
            get
            {
                float bonus = ComfortSettings.Instance?.AlliedPartySpeedBonus ?? 1f;
                return float.IsNaN(bonus) ? 1f : System.Math.Max(0f, System.Math.Min(10f, bonus));
            }
        }
    }
}
