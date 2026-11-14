using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Tags;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;

namespace StorySkip
{
    internal static class PluralMarriageUI
    {
        private static readonly FieldInfo HeroField =
            AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_hero");
        private static readonly FieldInfo RelatedHeroesField =
            AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_allRelatedHeroes");

        internal static void InitializePatches(Harmony harmony)
        {
            if (HeroField == null || RelatedHeroesField == null)
            {
                throw new MissingFieldException(
                    "The encyclopedia hero family fields could not be found.");
            }

            harmony.Patch(
                AccessTools.Method(typeof(EncyclopediaHeroPageVM), "RefreshValues"),
                prefix: new HarmonyMethod(typeof(PluralMarriageUI),
                    nameof(BeforeEncyclopediaRefresh)));
            harmony.Patch(
                AccessTools.Method(typeof(ConversationHelper),
                    nameof(ConversationHelper.GetHeroRelationToHeroTextShort),
                    new[] { typeof(Hero), typeof(Hero), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(PluralMarriageUI),
                    nameof(AfterRelationText)));
            harmony.Patch(
                AccessTools.Method(typeof(PlayerIsSpouseTag),
                    nameof(PlayerIsSpouseTag.IsApplicableTo),
                    new[] { typeof(CharacterObject) }),
                postfix: new HarmonyMethod(typeof(PluralMarriageUI),
                    nameof(AfterSpouseTag)));
        }

        private static void BeforeEncyclopediaRefresh(EncyclopediaHeroPageVM __instance)
        {
            Hero owner = HeroField.GetValue(__instance) as Hero;
            List<Hero> relatedHeroes = RelatedHeroesField.GetValue(__instance) as List<Hero>;
            if (owner == null || relatedHeroes == null)
            {
                return;
            }

            // Native RefreshValues creates the family cards, checks encyclopedia
            // visibility and assigns their roles from this list. Extend its input
            // so additional spouses receive the same handling as other relatives.
            foreach (Hero spouse in PluralMarriage.GetSpouses(owner))
            {
                if (spouse != null && spouse != owner && !relatedHeroes.Contains(spouse))
                {
                    relatedHeroes.Add(spouse);
                }
            }
        }

        private static void AfterRelationText(Hero __0, Hero __1, ref string __result)
        {
            // This helper supplies both encyclopedia family roles and the clan
            // member's relation label. Native logic recognizes one spouse only.
            if (__0 != null && __1 != null && PluralMarriage.AreSpouses(__0, __1))
            {
                __result = GameTexts.FindText("str_spouse", null).ToString();
            }
        }

        private static void AfterSpouseTag(CharacterObject __0, ref bool __result)
        {
            if (__result || Campaign.Current == null || Game.Current == null || __0?.HeroObject == null) return;
            // Native greetings compare only MainHero.Spouse, which cannot list
            // every marriage. Existing spouses remain spouses even when the
            // option to create additional marriages has been switched off.
            if (PluralMarriage.AreSpouses(Hero.MainHero, __0.HeroObject))
                __result = true;
        }
    }
}
