using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyTroopManagerPopUp;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class SharedTroopExperienceUI
    {
        private static string BalanceLabelPrefix => new TextObject("{=cc_xp_balance}Shared XP:").ToString() + " ";

        private static readonly FieldInfo CharacterLogicField =
            AccessTools.Field(typeof(PartyCharacterVM), "_partyScreenLogic");
        private static readonly FieldInfo CharacterTroopField =
            AccessTools.Field(typeof(PartyCharacterVM), "_troop");
        private static readonly FieldInfo PopupPartyField =
            AccessTools.Field(typeof(PartyTroopManagerVM), "_partyVM");

        private static readonly ConditionalWeakTable<PartyCharacterVM, ExperienceTooltipState>
            ExperienceTooltips = new ConditionalWeakTable<PartyCharacterVM, ExperienceTooltipState>();

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo initializeUpgrades = AccessTools.Method(typeof(PartyCharacterVM),
                nameof(PartyCharacterVM.InitializeUpgrades), Type.EmptyTypes);
            MethodInfo updateCounts = AccessTools.Method(typeof(PartyVM),
                "UpdateTroopManagerPopUpCounts", Type.EmptyTypes);
            MethodInfo update = AccessTools.Method(typeof(PartyVM), "Update",
                new[] { typeof(PartyScreenLogic.PartyCommand) });
            MethodInfo refreshParty = AccessTools.Method(typeof(PartyVM),
                "RefreshPartyInformation", Type.EmptyTypes);
            MethodInfo partyGoldSetter = AccessTools.PropertySetter(typeof(PartyVM),
                nameof(PartyVM.GoldChangeText));
            MethodInfo popupGoldSetter = AccessTools.PropertySetter(typeof(PartyTroopManagerVM),
                nameof(PartyTroopManagerVM.GoldChangeText));
            ConstructorInfo[] partyConstructors = typeof(PartyVM).GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (initializeUpgrades == null || updateCounts == null || update == null ||
                refreshParty == null || partyGoldSetter == null || popupGoldSetter == null ||
                partyConstructors.Length == 0 || CharacterLogicField == null ||
                CharacterTroopField == null || PopupPartyField == null)
            {
                throw new MissingMemberException(
                    "Shared troop experience party UI hooks changed in this Bannerlord version.");
            }

            harmony.Patch(initializeUpgrades,
                prefix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(ProjectSharedExperience)),
                postfix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(FinishUpgradePreview)),
                finalizer: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RestoreExperienceAfterFailure)));
            harmony.Patch(updateCounts,
                prefix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RefreshUpgradePreviews)),
                postfix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RefreshOtherPartyUpgradePreviews)));
            harmony.Patch(partyGoldSetter,
                prefix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(DecoratePartyGoldText)));
            harmony.Patch(popupGoldSetter,
                prefix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(DecoratePopupGoldText)));
            harmony.Patch(update,
                postfix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RefreshBalanceFooter)));
            harmony.Patch(refreshParty,
                postfix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RefreshBalanceFooter)));
            foreach (ConstructorInfo constructor in partyConstructors)
            {
                harmony.Patch(constructor,
                    postfix: new HarmonyMethod(typeof(SharedTroopExperienceUI), nameof(RefreshBalanceFooter)));
            }
        }

        private static bool CanDisplayBalance(PartyScreenLogic logic)
        {
            return SharedTroopExperience.IsActive &&
                (SharedTroopExperience.AppliesTo(logic, PartyScreenLogic.PartyRosterSide.Right,
                    PartyScreenLogic.TroopType.Member) ||
                 SharedTroopExperience.AppliesTo(logic, PartyScreenLogic.PartyRosterSide.Left,
                    PartyScreenLogic.TroopType.Member));
        }

        private static bool CanUseSharedExperience(PartyCharacterVM troop, PartyScreenLogic logic)
        {
            return troop != null && troop.Character != null && !troop.IsHero && !troop.IsPrisoner &&
                   SharedTroopExperience.IsActive &&
                   SharedTroopExperience.AppliesTo(logic, troop.Side, troop.Type);
        }

        private static PartyScreenLogic GetLogic(PartyCharacterVM troop)
        {
            return troop == null ? null : CharacterLogicField.GetValue(troop) as PartyScreenLogic;
        }

        private static void ProjectSharedExperience(PartyCharacterVM __instance,
            out ExperienceProjection __state)
        {
            __state = null;
            PartyScreenLogic logic = GetLogic(__instance);
            if (!CanUseSharedExperience(__instance, logic))
                return;

            TroopRosterElement original = __instance.Troop;
            TroopRosterElement preview = original;
            // Only this view-model copy is projected. Rosters and save data never
            // receive the pool repeatedly for each troop type.
            preview.Xp = (int)Math.Min(int.MaxValue, Math.Max(0L,
                SharedTroopExperience.GetAvailableXp(logic)));
            __state = new ExperienceProjection(original);
            CharacterTroopField.SetValue(__instance, preview);
        }

        private static void FinishUpgradePreview(PartyCharacterVM __instance,
            ExperienceProjection __state)
        {
            RestoreExperience(__instance, __state);
            PartyScreenLogic logic = GetLogic(__instance);
            ExperienceTooltipState tooltip = ExperienceTooltips.GetValue(__instance,
                troop => new ExperienceTooltipState());
            if (!ReferenceEquals(__instance.TroopXPTooltip, tooltip.SharedTooltip))
                tooltip.NativeTooltip = __instance.TroopXPTooltip;

            if (!CanUseSharedExperience(__instance, logic))
            {
                if (ReferenceEquals(__instance.TroopXPTooltip, tooltip.SharedTooltip))
                    __instance.TroopXPTooltip = tooltip.NativeTooltip;
                return;
            }

            if (tooltip.SharedTooltip == null)
                tooltip.SharedTooltip = new BasicTooltipViewModel(() => GetExperienceTooltip(__instance));
            __instance.TroopXPTooltip = tooltip.SharedTooltip;

            // The native bar normally shows the remainder belonging to this
            // troop type. A pooled bank instead fills the next upgrade's bar.
            if (__instance.IsUpgradableTroop && __instance.MaxXP > 0)
            {
                __instance.CurrentXP = (int)Math.Min(__instance.MaxXP, Math.Max(0L,
                    SharedTroopExperience.GetAvailableXp(logic)));
            }
        }

        private static Exception RestoreExperienceAfterFailure(PartyCharacterVM __instance,
            ExperienceProjection __state, Exception __exception)
        {
            RestoreExperience(__instance, __state);
            return __exception;
        }

        private static void RestoreExperience(PartyCharacterVM troop, ExperienceProjection projection)
        {
            if (projection == null || projection.Restored)
                return;
            CharacterTroopField.SetValue(troop, projection.OriginalTroop);
            projection.Restored = true;
        }

        private static string GetExperienceTooltip(PartyCharacterVM troop)
        {
            PartyScreenLogic logic = GetLogic(troop);
            if (!CanUseSharedExperience(troop, logic))
                return new TextObject("{=cc_xp_amount}XP: {XP}")
                    .SetTextVariable("XP", FormatExperience(troop.Troop.Xp)).ToString();

            StringBuilder text = new StringBuilder(GetBalanceLabel(logic));
            text.Append('\n').Append(new TextObject("{=cc_xp_help}Regular troops in the main party share XP. Upgrades use the selected branch's cost. Confirm to spend XP or cancel to undo."));
            CharacterObject[] targets = troop.Character.UpgradeTargets;
            for (int branch = 0; targets != null && branch < targets.Length; branch++)
            {
                if (targets[branch] == null)
                    continue;
                text.Append('\n');
                text.Append(new TextObject("{=cc_xp_cost}{TROOP}: {XP} XP per troop")
                    .SetTextVariable("TROOP", targets[branch].Name)
                    .SetTextVariable("XP", FormatExperience(troop.Character.GetUpgradeXpCost(PartyBase.MainParty, branch))));
            }
            return text.ToString();
        }

        private static void RefreshUpgradePreviews(PartyVM __instance)
        {
            PartyScreenLogic logic = __instance.PartyScreenLogic;
            if (!CanDisplayBalance(logic))
                return;

            // Native upgrades already refresh every row. This also refreshes
            // counts after recruitment or transfer brings experience into the
            // pool, before the native popup button availability is computed.
            if (__instance.MainPartyTroops != null && SharedTroopExperience.AppliesTo(logic,
                PartyScreenLogic.PartyRosterSide.Right, PartyScreenLogic.TroopType.Member))
            {
                foreach (PartyCharacterVM troop in __instance.MainPartyTroops)
                    troop.InitializeUpgrades();
            }
        }

        private static void RefreshOtherPartyUpgradePreviews(PartyVM __instance)
        {
            PartyScreenLogic logic = __instance.PartyScreenLogic;
            // The native popup counts refer to the right-side list. Refresh
            // the main party's left-side rows after that native calculation.
            if (__instance.OtherPartyTroops != null && SharedTroopExperience.AppliesTo(logic,
                PartyScreenLogic.PartyRosterSide.Left, PartyScreenLogic.TroopType.Member))
            {
                foreach (PartyCharacterVM troop in __instance.OtherPartyTroops)
                    troop.InitializeUpgrades();
            }
        }

        private static void DecoratePartyGoldText(PartyVM __instance, ref string __0)
        {
            __0 = WithBalanceLabel(__0, __instance.PartyScreenLogic);
        }

        private static void DecoratePopupGoldText(PartyTroopManagerVM __instance, ref string __0)
        {
            if (!(__instance is PartyUpgradeTroopVM))
                return;
            PartyVM party = PopupPartyField.GetValue(__instance) as PartyVM;
            __0 = WithBalanceLabel(__0, party?.PartyScreenLogic);
        }

        private static void RefreshBalanceFooter(PartyVM __instance)
        {
            // Native gold notifications can precede the XP debit. Reassigning
            // here updates the bound label after the whole command completes.
            __instance.GoldChangeText = __instance.GoldChangeText;
            if (__instance.UpgradePopUp != null && __instance.UpgradePopUp.IsOpen)
                __instance.UpgradePopUp.GoldChangeText = __instance.UpgradePopUp.GoldChangeText;
        }

        private static string WithBalanceLabel(string originalText, PartyScreenLogic logic)
        {
            string nativeText = originalText ?? string.Empty;
            int previousLabel = nativeText.LastIndexOf(BalanceLabelPrefix, StringComparison.Ordinal);
            if (previousLabel >= 0 && (previousLabel == 0 || nativeText[previousLabel - 1] == '\n'))
                nativeText = nativeText.Substring(0, previousLabel).TrimEnd('\r', '\n');

            if (!CanDisplayBalance(logic))
                return nativeText;
            string balance = GetBalanceLabel(logic);
            return nativeText.Length == 0 ? balance : nativeText + "\n" + balance;
        }

        private static string GetBalanceLabel(PartyScreenLogic logic)
        {
            return BalanceLabelPrefix + FormatExperience(SharedTroopExperience.GetAvailableXp(logic));
        }

        private static string FormatExperience(long experience)
        {
            return Math.Max(0L, experience).ToString("N0", CultureInfo.CurrentCulture);
        }

        private sealed class ExperienceProjection
        {
            internal readonly TroopRosterElement OriginalTroop;
            internal bool Restored;

            internal ExperienceProjection(TroopRosterElement originalTroop)
            {
                OriginalTroop = originalTroop;
            }
        }

        private sealed class ExperienceTooltipState
        {
            internal BasicTooltipViewModel NativeTooltip;
            internal BasicTooltipViewModel SharedTooltip;
        }
    }
}
