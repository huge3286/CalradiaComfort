using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace StorySkip
{
    // Plain release remains available alongside a same-conversation barter.
    // A refusal after release never restores the captive state.
    internal sealed class ReleasedCaptiveCourtshipCampaignBehavior : CampaignBehaviorBase
    {
        private bool _releaseSucceeded;
        private bool _barterCompleted;
        private bool _barterSucceeded;
        private Hero _barterPartner;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogs);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // No ongoing agreement or additional hero state is stored here.
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            _releaseSucceeded = false;
            _barterCompleted = false;
            _barterSucceeded = false;
            _barterPartner = null;
            starter.AddPlayerLine(
                "comfort_captive_marriage_offer",
                "hero_main_options",
                "comfort_captive_marriage_barter_setup",
                "我想与你联姻。我们商议婚约和释放条件吧。",
                CanOfferUnconditionalRelease,
                null,
                115);
            // Open barter from an NPC setup line, matching the native marriage
            // dialog's suspended-screen and conclusion-token flow.
            starter.AddDialogLine(
                "comfort_captive_marriage_barter_setup",
                "comfort_captive_marriage_barter_setup",
                "comfort_captive_marriage_barter_conclusion",
                "可以谈谈条件。获得自由后，我会自己决定是否接受婚约。",
                null,
                OpenMarriageBarter,
                115);
            starter.AddDialogLine(
                "comfort_captive_marriage_barter_success",
                "comfort_captive_marriage_barter_conclusion",
                "close_window",
                "我愿意接受这份婚约。",
                () => _barterCompleted && _barterSucceeded,
                FinishEncounter,
                115);
            starter.AddDialogLine(
                "comfort_captive_marriage_barter_released",
                "comfort_captive_marriage_barter_conclusion",
                "close_window",
                "感谢你归还我的自由，但这份婚约没有达成。",
                () => _barterCompleted && !_barterSucceeded &&
                    _barterPartner != null && !_barterPartner.IsPrisoner,
                FinishEncounter,
                114);
            starter.AddDialogLine(
                "comfort_captive_marriage_barter_cancelled",
                "comfort_captive_marriage_barter_conclusion",
                "hero_main_options",
                "我们没有谈妥这份交易。",
                () => !_barterSucceeded && (_barterPartner == null || _barterPartner.IsPrisoner),
                null,
                110);
            starter.AddPlayerLine(
                "comfort_release_for_future_courtship",
                "hero_main_options",
                "comfort_released_for_future_courtship_response",
                "我会无条件释放你。",
                CanOfferUnconditionalRelease,
                ReleaseWithoutMarriage,
                110);
            starter.AddDialogLine(
                "comfort_unconditional_release_success",
                "comfort_released_for_future_courtship_response",
                "close_window",
                "感谢你让我自由离开。至于婚事，我会自己决定。",
                () => _releaseSucceeded,
                null,
                110);
            starter.AddDialogLine(
                "comfort_unconditional_release_unavailable",
                "comfort_released_for_future_courtship_response",
                "close_window",
                "现在还无法离开，我们稍后再谈。",
                () => !_releaseSucceeded,
                null,
                110);
        }

        private void OpenMarriageBarter()
        {
            _barterPartner = Hero.OneToOneConversationHero;
            _barterCompleted = false;
            _barterSucceeded = false;
            if (!CanOfferUnconditionalRelease())
            {
                _barterCompleted = true;
                return;
            }
            try
            {
                ImmediateMarriageBarter.Open(_barterPartner, married =>
                {
                    _barterSucceeded = married;
                    _barterCompleted = true;
                });
            }
            catch (Exception exception)
            {
                _barterCompleted = true;
                Debug.Print("[CalradiaComfort] Opening marriage barter failed: " + exception);
            }
        }

        private static void FinishEncounter()
        {
            if (PlayerEncounter.Current != null) PlayerEncounter.LeaveEncounter = true;
        }

        internal static bool CanOfferUnconditionalRelease()
        {
            if (!ComfortFeatures.ReleasedCaptiveCourtshipEnabled || Campaign.Current == null)
            {
                return false;
            }

            Hero captive = Hero.OneToOneConversationHero;
            Hero player = Hero.MainHero;
            return captive != null && player != null && captive.IsPrisoner &&
                   (player.Spouse == null || (ComfortFeatures.PluralMarriageEnabled &&
                    PluralMarriageCampaignBehavior.Current != null)) &&
                   !player.IsPrisoner &&
                   Campaign.Current.CurrentConversationContext != ConversationContext.CapturedLord &&
                   Campaign.Current.CurrentConversationContext != ConversationContext.FreeOrCapturePrisonerHero &&
                   captive.PartyBelongedToAsPrisoner == PartyBase.MainParty &&
                   PartyBase.MainParty != null &&
                   PartyBase.MainParty.PrisonRoster.Contains(captive.CharacterObject) &&
                   IsPotentialFutureMatch(player, captive);
        }

        internal static bool IsPotentialFutureMatch(Hero player, Hero captive)
        {
            if (Campaign.Current == null || player == null || captive == null ||
                player == captive || !player.IsAlive || !captive.IsAlive ||
                player.IsFemale == captive.IsFemale || captive.Spouse != null ||
                !captive.IsLord || captive.IsMinorFactionHero || captive.IsNotable ||
                captive.IsTemplate || player.Clan == null || captive.Clan == null ||
                player.Clan == captive.Clan)
            {
                return false;
            }

            var model = Campaign.Current.Models.MarriageModel;
            int playerMinimum = Math.Max(18, player.IsFemale
                ? model.MinimumMarriageAgeFemale : model.MinimumMarriageAgeMale);
            int captiveMinimum = Math.Max(18, captive.IsFemale
                ? model.MinimumMarriageAgeFemale : model.MinimumMarriageAgeMale);
            if (player.Age < playerMinimum || captive.Age < captiveMinimum ||
                !model.IsClanSuitableForMarriage(player.Clan) ||
                !model.IsClanSuitableForMarriage(captive.Clan) ||
                (player.Clan.Leader == player && captive.Clan.Leader == captive))
            {
                return false;
            }

            // Matches the native marriage model's three-generation check.
            return !AreRelatedWithinThreeGenerations(player, captive);
        }

        internal static bool AreRelatedWithinThreeGenerations(Hero first, Hero second)
        {
            var ancestors = new HashSet<Hero>();
            AddAncestors(first, 3, ancestors);
            return HasSharedAncestor(second, 3, ancestors);
        }

        private static void AddAncestors(Hero hero, int remainingDepth, HashSet<Hero> ancestors)
        {
            if (hero == null)
            {
                return;
            }

            ancestors.Add(hero);
            if (remainingDepth > 0)
            {
                AddAncestors(hero.Mother, remainingDepth - 1, ancestors);
                AddAncestors(hero.Father, remainingDepth - 1, ancestors);
            }
        }

        private static bool HasSharedAncestor(Hero hero, int remainingDepth, HashSet<Hero> ancestors)
        {
            return hero != null && (ancestors.Contains(hero) ||
                (remainingDepth > 0 &&
                 (HasSharedAncestor(hero.Mother, remainingDepth - 1, ancestors) ||
                  HasSharedAncestor(hero.Father, remainingDepth - 1, ancestors))));
        }

        private void ReleaseWithoutMarriage()
        {
            _releaseSucceeded = false;
            if (!CanOfferUnconditionalRelease())
            {
                return;
            }

            Hero captive = Hero.OneToOneConversationHero;
            try
            {
                EndCaptivityAction.ApplyByReleasedByChoice(captive, Hero.MainHero);
                _releaseSucceeded = !captive.IsPrisoner &&
                    !PartyBase.MainParty.PrisonRoster.Contains(captive.CharacterObject);
                if (!_releaseSucceeded)
                {
                    return;
                }

                // Keep the same goodwill as the native voluntary release line.
                ChangeRelationAction.ApplyPlayerRelation(captive, 4, true, true);
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.LeaveEncounter = true;
                }

                // Native release uses CharacterStates.Released. Do not force
                // Active, attach the hero to our party, or alter a romance state.
                InformationManager.DisplayMessage(new InformationMessage(
                    "已无条件释放对方。"));
            }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Unconditional captive release failed: " + exception);
                // A release which has already succeeded remains final, including
                // when a downstream relation/notification listener fails.
                _releaseSucceeded = !captive.IsPrisoner;
            }
        }
    }
}
