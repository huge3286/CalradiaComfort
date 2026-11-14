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

        [SettingPropertyBool("{=cc_opt_story}Skip opening story", Order = 0, RequireRestart = false,
            HintText = "{=cc_opt_story_hint}Applies to new campaigns. Reunite your family, obtain the Dragon Banner, and keep its kingdom choice.")]
        [SettingPropertyGroup(Convenience, GroupOrder = 0)]
        public bool SkipOpeningStory { get; set; } = true;

        [SettingPropertyBool("{=cc_opt_recruit}One-click recruitment", Order = 1, RequireRestart = false,
            HintText = "{=cc_opt_recruit_hint}Stage all eligible common or noble recruits. Normal relations, cost, and capacity limits apply.")]
        [SettingPropertyGroup(Convenience, GroupOrder = 0)]
        public bool QuickRecruitment { get; set; } = true;

        [SettingPropertyBool("{=cc_opt_achievements}Allow Steam achievements with mods", Order = 2, RequireRestart = false,
            HintText = "{=cc_opt_achievements_hint}Restore normal achievement checks. Achievement requirements still apply.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool EnableAchievements { get; set; }

        [SettingPropertyBool("{=cc_opt_perks}Grant both eligible perks", Order = 3, RequireRestart = false,
            HintText = "{=cc_opt_perks_hint}Grant both eligible perks to heroes in the player clan. Requires the normal skill level.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool BothPerks { get; set; }

        [SettingPropertyBool("{=cc_opt_growth}Max hero progression", Order = 4, RequireRestart = false,
            HintText = "{=cc_opt_growth_hint}Max attributes, focus, and skills for eligible adult player-clan heroes. Turning this off does not undo gains.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool LearningLimitSkills { get; set; }

        [SettingPropertyBool("{=cc_opt_xp}Pool troop experience", Order = 5, RequireRestart = false,
            HintText = "{=cc_opt_xp_hint}Share the main party's troop experience. Upgrades still use their original costs.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool SharedTroopExperience { get; set; }

        [SettingPropertyBool("{=cc_opt_upgrade}One-click maximum troop tier", Order = 6, RequireRestart = false,
            HintText = "{=cc_opt_upgrade_hint}Requires pooled troop experience. Upgrade through every tier at the normal cost.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool UltimateTroopUpgrade { get; set; }

        [SettingPropertyBool("{=cc_opt_party_limit}Override clan party limit", Order = 7, RequireRestart = false,
            HintText = "{=cc_opt_party_limit_hint}Override the player clan's maximum number of field parties.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool ClanPartyLimit { get; set; }

        [SettingPropertyBool("{=cc_opt_companion_limit}Override companion limit", Order = 8, RequireRestart = false,
            HintText = "{=cc_opt_companion_limit_hint}Override the player clan's companion limit.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool ClanCompanionLimit { get; set; }

        [SettingPropertyBool("{=cc_opt_speed}Increase allied map speed", Order = 9, RequireRestart = false,
            HintText = "{=cc_opt_speed_hint}Apply a flat speed bonus to allied map parties.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public bool AlliedPartySpeed { get; set; }

        [SettingPropertyInteger("{=cc_opt_party_value}Clan party limit", 1, 100, Order = 10, RequireRestart = false,
            HintText = "{=cc_opt_party_value_hint}Only applies when the party limit override is enabled.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public int ClanPartyLimitValue { get; set; } = 20;

        [SettingPropertyInteger("{=cc_opt_companion_value}Companion limit", 1, 100, Order = 11, RequireRestart = false,
            HintText = "{=cc_opt_companion_value_hint}Only applies when the companion limit override is enabled.")]
        [SettingPropertyGroup(Cheats, GroupOrder = 1)]
        public int ClanCompanionLimitValue { get; set; } = 20;

        [SettingPropertyFloatingInteger("{=cc_opt_speed_value}Allied speed bonus", 0f, 10f, "0.0", Order = 12, RequireRestart = false,
            HintText = "{=cc_opt_speed_value_hint}Only applies when allied map speed is enabled.")]
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
