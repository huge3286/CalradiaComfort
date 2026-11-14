using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StoryMode;
using StoryMode.GameComponents.CampaignBehaviors;
using StoryMode.Quests.FirstPhase;
using StoryMode.Quests.PlayerClanQuests;
using StoryMode.Quests.SecondPhase;
using StoryMode.Quests.ThirdPhase;
using StoryMode.Quests.TutorialPhase;
using StoryMode.StoryModeObjects;
using StoryMode.StoryModePhases;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace StorySkip
{
    /// <summary>
    /// Skips the collection and rescue chapters, but keeps the original Dragon
    /// Banner decision with Istiana and Arzagos. The later conspiracy is skipped
    /// only after the player has chosen a kingdom. War Sails is handled separately.
    /// </summary>
    public static class StorySkipStoryMode
    {
        private static readonly FieldInfo FirstPhaseField =
            AccessTools.Field(typeof(MainStoryLine), "<FirstPhase>k__BackingField");
        private static readonly FieldInfo ThirdPhaseField =
            AccessTools.Field(typeof(MainStoryLine), "<ThirdPhase>k__BackingField");
        private static readonly FieldInfo ThirdPhaseCompletedField =
            AccessTools.Field(typeof(ThirdPhase), "<IsCompleted>k__BackingField");
        private static readonly FieldInfo CollectedPieceCountField =
            AccessTools.Field(typeof(FirstPhase), "<CollectedBannerPieceCount>k__BackingField");
        private static readonly FieldInfo InvestigationProgressField =
            AccessTools.Field(typeof(BannerInvestigationQuest), "_talkedNotablesQuestLog");
        private static readonly FieldInfo RebuildFinishingField =
            AccessTools.Field(typeof(RebuildPlayerClanQuest), "_finishQuest");
        private static bool _isCompletingBannerCollectionQuests;

        public static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            PatchIfPresent(harmony, AccessTools.Method(typeof(QuestBase), "StartQuest"),
                nameof(PreventVanillaStoryQuestStart));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(TutorialPhaseCampaignBehavior), "ShowStealthTutorialInquiry"),
                nameof(PreventFinishedStealthTutorial));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(TutorialPhaseCampaignBehavior), "StartStealthTutorial"),
                nameof(PreventFinishedStealthTutorial));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(FirstPhaseCampaignBehavior), "ShowStealthTutorialInquiry"),
                nameof(PreventFinishedStealthTutorial));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(FirstPhaseCampaignBehavior), "StartStealthTutorial"),
                nameof(PreventFinishedStealthTutorial));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(FirstPhaseCampaignBehavior), "OnStoryModeTutorialEnded"),
                nameof(PreventMainQuestCreation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(RescueFamilyQuestBehavior), "OnQuestCompleted"),
                nameof(PreventRescueQuestArm));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(RescueFamilyQuestBehavior), "OnSettlementEntered"),
                nameof(PreventRescueQuestAtTown));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(FirstPhaseCampaignBehavior), "OnBannerPieceCollected"),
                nameof(PreventMainQuestCreation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(FirstPhaseCampaignBehavior), "OnQuestCompleted"),
                nameof(PreventDuplicateBannerQuestCreation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(Helpers.HeroHelper), "SpawnHeroForTheFirstTime"),
                nameof(PreventAlreadySpawnedMentor));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(IstianasBannerPieceQuest), "InitializeHideout"),
                nameof(PreventAutoBannerHideoutPreparation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(ArzagosBannerPieceQuest), "InitializeHideout"),
                nameof(PreventAutoBannerHideoutPreparation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(AssembleEmpireQuestBehavior), "OnMainStoryLineSideChosen"),
                nameof(PreventMainQuestCreation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(WeakenEmpireQuestBehavior), "OnMainStoryLineSideChosen"),
                nameof(PreventMainQuestCreation));
            PatchIfPresent(harmony,
                AccessTools.Method(typeof(DefeatTheConspiracyQuestBehavior), "HourlyTick"),
                nameof(PreventUninitializedFinalPhaseTick));
        }

        /// <summary>
        /// Called after character creation, on a campaign tick. Returns false until
        /// the vanilla StoryMode objects and map party are ready. Safe to retry.
        /// </summary>
        public static bool Apply()
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                Hero.MainHero == null || MobileParty.MainParty == null)
            {
                return false;
            }

            MainStoryLine storyLine = StoryModeManager.Current?.MainStoryLine;
            if (storyLine?.TutorialPhase == null ||
                StoryModeManager.Current?.StoryModeHeroes == null ||
                StoryModeHeroes.ElderBrother == null || StoryModeHeroes.LittleBrother == null ||
                StoryModeHeroes.ImperialMentor == null || StoryModeHeroes.AntiImperialMentor == null ||
                Clan.PlayerClan == null || FirstPhaseField == null ||
                ThirdPhaseField == null || ThirdPhaseCompletedField == null ||
                CollectedPieceCountField == null)
            {
                return false;
            }

            var banner = storyLine.DragonBanner;
            Settlement home = FindFamilySettlement();
            if (banner == null || home == null)
            {
                return false;
            }

            // FinalizeTutorialPhase clears the party inventory and disables the
            // elder brother. It must happen before items or heroes are restored.
            if (!storyLine.TutorialPhase.IsCompleted)
            {
                storyLine.CompleteTutorialPhase(true);
            }
            else if (storyLine.FirstPhase == null)
            {
                FirstPhaseField.SetValue(storyLine, new FirstPhase());
            }

            if (storyLine.FirstPhase == null)
            {
                return false;
            }

            // A 0.1.1 save already ended the whole story. Its first-phase mentor
            // house reservations no longer exist, so it must keep that state.
            if (storyLine.IsCompleted || storyLine.SecondPhase != null ||
                storyLine.MainStoryLineSide != MainStoryLineSide.None)
            {
                return storyLine.IsCompleted;
            }

            // The original FirstPhase behavior reserves a house for each mentor
            // and later releases both reservations when a side is chosen.
            if (storyLine.ImperialMentorSettlement == null ||
                storyLine.AntiImperialMentorSettlement == null)
            {
                return false;
            }

            RemoveFinalPhaseQuestBehavior();

            // Cancel only the collection/tutorial quests. The banner decision
            // and its two kingdom choices must remain active through save/load.
            QuestBase[] activeStoryQuests = Campaign.Current.QuestManager.Quests
                .Where(q => q is StoryModeQuestBase && q.IsOngoing &&
                            !(q is VillagersInNeed) &&
                            !(q is RebuildPlayerClanQuest) &&
                            !(q is BannerInvestigationQuest) &&
                            !(q is MeetWithIstianaQuest) &&
                            !(q is MeetWithArzagosQuest) &&
                            !(q is IstianasBannerPieceQuest) &&
                            !(q is ArzagosBannerPieceQuest) &&
                            !IsBannerDecisionQuest(q)).ToArray();
            foreach (QuestBase quest in activeStoryQuests)
            {
                if (quest.IsOngoing)
                {
                    quest.CompleteQuestWithCancel(null);
                }
            }

            storyLine.FamilyRescued = true;
            RestoreBrother(StoryModeHeroes.ElderBrother, home, true);
            RestoreBrother(StoryModeHeroes.LittleBrother, home, false);
            RestoreBrotherEncyclopediaText();

            EnsureMentorInSettlement(StoryModeHeroes.ImperialMentor,
                storyLine.ImperialMentorSettlement);
            EnsureMentorInSettlement(StoryModeHeroes.AntiImperialMentor,
                storyLine.AntiImperialMentorSettlement);
            if (StoryModeHeroes.ImperialMentor.CurrentSettlement == null ||
                StoryModeHeroes.AntiImperialMentor.CurrentSettlement == null)
            {
                return false;
            }

            var roster = MobileParty.MainParty.ItemRoster;
            AssembleTheBannerQuest assembleQuest = Campaign.Current.QuestManager.Quests
                .OfType<AssembleTheBannerQuest>().FirstOrDefault(q => q.IsOngoing);
            bool newAssembleQuest = assembleQuest == null;
            if (newAssembleQuest)
            {
                // Start at the real tutorial count (1/3), so the journal's
                // collection progress is initialized by the vanilla quest.
                assembleQuest = new AssembleTheBannerQuest();
                assembleQuest.StartQuest();
            }

            if (newAssembleQuest || !storyLine.FirstPhase.AllPiecesCollected ||
                roster.GetItemNumber(banner) == 0)
            {
                // A partial retry can have a banner without the vanilla quest
                // completion log or mentor markers. Re-run the original merge
                // once so those quest states are established together.
                int existingBanners = roster.GetItemNumber(banner);
                if (existingBanners > 0)
                {
                    roster.AddToCounts(new EquipmentElement(banner, null, null, true),
                        -existingBanners);
                }

                EnsureBannerFragment(roster, "dragon_banner_center");
                EnsureBannerFragment(roster, "dragon_banner_dragonhead");
                EnsureBannerFragment(roster, "dragon_banner_handle");
                CollectedPieceCountField.SetValue(storyLine.FirstPhase, 3);

                // This invokes AssembleTheBannerQuest.OnBannerPieceCollected:
                // vanilla tracks both mentors and merges the three fragments.
                StoryModeEvents.Instance.OnBannerPieceCollected();
            }
            else
            {
                CollectedPieceCountField.SetValue(storyLine.FirstPhase, 3);
            }
            RemoveBannerFragment(roster, "dragon_banner_center");
            RemoveBannerFragment(roster, "dragon_banner_dragonhead");
            RemoveBannerFragment(roster, "dragon_banner_handle");

            return assembleQuest.IsOngoing && storyLine.FirstPhase.AllPiecesCollected &&
                   StoryModeHeroes.ElderBrother.Clan == Clan.PlayerClan &&
                   StoryModeHeroes.LittleBrother.Clan == Clan.PlayerClan &&
                   roster.GetItemNumber(banner) > 0;
        }

        /// <summary>
        /// Record all six pre-decision quests as genuine successes. The original
        /// callbacks start the two mentor visits and the two banner-piece quests.
        /// Their hideout setup is suppressed only while this instant completion
        /// runs, because it would otherwise create permanent raider parties.
        /// The already assembled banner stays available for the player's choice.
        /// The caller persists completedStage across saves so a partially
        /// successful retry cannot award the clan quest's renown twice.
        /// </summary>
        public static bool TryCompleteBannerCollectionQuests(ref int completedStage)
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                Hero.MainHero == null || Clan.PlayerClan == null)
            {
                return false;
            }

            MainStoryLine storyLine = StoryModeManager.Current?.MainStoryLine;
            if (storyLine == null)
            {
                return false;
            }

            // Older saves may already have handed over the banner. Their
            // completed decision should never be reopened for migration.
            if (storyLine.IsCompleted || storyLine.SecondPhase != null ||
                storyLine.MainStoryLineSide != MainStoryLineSide.None)
            {
                completedStage = 6;
                return true;
            }

            if (completedStage >= 6)
            {
                return true;
            }

            if (storyLine.FirstPhase == null || !storyLine.FirstPhase.AllPiecesCollected ||
                !Campaign.Current.QuestManager.Quests.OfType<AssembleTheBannerQuest>()
                    .Any(q => q.IsOngoing) || RebuildFinishingField == null ||
                InvestigationProgressField == null ||
                storyLine.ImperialMentorSettlement == null ||
                storyLine.AntiImperialMentorSettlement == null)
            {
                return false;
            }

            QuestManager manager = Campaign.Current.QuestManager;
            _isCompletingBannerCollectionQuests = true;
            try
            {
                if (completedStage < 1)
                {
                    RebuildPlayerClanQuest rebuild = manager.Quests
                        .OfType<RebuildPlayerClanQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (rebuild == null)
                    {
                        rebuild = new RebuildPlayerClanQuest();
                        rebuild.StartQuest();
                    }

                    if (!manager.Quests.Contains(rebuild) || !rebuild.IsOngoing)
                    {
                        return false;
                    }

                    // Vanilla success grants 25 renown. Mark this quest as
                    // finishing first: the renown event can otherwise reenter
                    // its completion. The rescue guard prevents Radagos.
                    RebuildFinishingField.SetValue(rebuild, true);
                    CompleteRebuildGoalLogs(rebuild);
                    rebuild.CompleteQuestWithSuccess();
                    completedStage = 1;
                }

                if (completedStage < 2)
                {
                    BannerInvestigationQuest investigation = manager.Quests
                        .OfType<BannerInvestigationQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (investigation == null)
                    {
                        investigation = new BannerInvestigationQuest();
                        investigation.StartQuest();
                    }

                    if (!manager.Quests.Contains(investigation) || !investigation.IsOngoing)
                    {
                        return false;
                    }

                    if (InvestigationProgressField.GetValue(investigation) is JournalLog progress)
                    {
                        progress.UpdateCurrentProgress(10);
                    }

                    investigation.CompleteQuestWithSuccess();
                    completedStage = 2;
                }

                if (completedStage < 3)
                {
                    MeetWithIstianaQuest meetIstiana = manager.Quests
                        .OfType<MeetWithIstianaQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (meetIstiana == null)
                    {
                        meetIstiana = new MeetWithIstianaQuest(
                            storyLine.ImperialMentorSettlement);
                        meetIstiana.StartQuest();
                    }

                    if (!manager.Quests.Contains(meetIstiana) || !meetIstiana.IsOngoing)
                    {
                        return false;
                    }

                    meetIstiana.CompleteQuestWithSuccess();
                    completedStage = 3;
                }

                if (completedStage < 4)
                {
                    MeetWithArzagosQuest meetArzagos = manager.Quests
                        .OfType<MeetWithArzagosQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (meetArzagos == null)
                    {
                        meetArzagos = new MeetWithArzagosQuest(
                            storyLine.AntiImperialMentorSettlement);
                        meetArzagos.StartQuest();
                    }

                    if (!manager.Quests.Contains(meetArzagos) || !meetArzagos.IsOngoing)
                    {
                        return false;
                    }

                    meetArzagos.CompleteQuestWithSuccess();
                    completedStage = 4;
                }

                // The first phase already holds three pieces and the original
                // merge already made the full banner. CollectBannerPiece here
                // would raise the count above three and duplicate fragments.
                if (completedStage < 5)
                {
                    IstianasBannerPieceQuest istianaPiece = manager.Quests
                        .OfType<IstianasBannerPieceQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (istianaPiece == null)
                    {
                        Settlement hideout = FindFreeBannerHideout(
                            StoryModeHeroes.ImperialMentor);
                        if (hideout == null)
                        {
                            return false;
                        }

                        istianaPiece = new IstianasBannerPieceQuest(
                            StoryModeHeroes.ImperialMentor, hideout);
                        istianaPiece.StartQuest();
                    }

                    if (!manager.Quests.Contains(istianaPiece) || !istianaPiece.IsOngoing)
                    {
                        return false;
                    }

                    istianaPiece.CompleteQuestWithSuccess();
                    completedStage = 5;
                }

                if (completedStage < 6)
                {
                    ArzagosBannerPieceQuest arzagosPiece = manager.Quests
                        .OfType<ArzagosBannerPieceQuest>().FirstOrDefault(q => q.IsOngoing);
                    if (arzagosPiece == null)
                    {
                        Settlement hideout = FindFreeBannerHideout(
                            StoryModeHeroes.AntiImperialMentor);
                        if (hideout == null)
                        {
                            return false;
                        }

                        arzagosPiece = new ArzagosBannerPieceQuest(
                            StoryModeHeroes.AntiImperialMentor, hideout);
                        arzagosPiece.StartQuest();
                    }

                    if (!manager.Quests.Contains(arzagosPiece) || !arzagosPiece.IsOngoing)
                    {
                        return false;
                    }

                    arzagosPiece.CompleteQuestWithSuccess();
                    completedStage = 6;
                }

                return completedStage >= 6;
            }
            finally
            {
                _isCompletingBannerCollectionQuests = false;
            }
        }

        private static Settlement FindFreeBannerHideout(Hero mentor)
        {
            if (mentor == null)
            {
                return null;
            }

            return Hideout.All.Select(h => h.Settlement)
                .FirstOrDefault(s => s != null && !s.IsSettlementBusy(mentor));
        }

        private static void CompleteRebuildGoalLogs(RebuildPlayerClanQuest quest)
        {
            // The journal should show all four objectives fulfilled when the
            // original success callback awards the clan quest's renown.
            FieldInfo rangeField = AccessTools.Field(typeof(JournalLog), "Range");
            if (rangeField == null)
            {
                throw new MissingFieldException("Rebuild clan journal range changed.");
            }

            foreach (string name in new[] {
                "_goldGoalLog", "_partySizeGoalLog", "_clanTierGoalLog",
                "_hireCompanionGoalLog" })
            {
                FieldInfo goalField = AccessTools.Field(typeof(RebuildPlayerClanQuest), name);
                if (goalField == null || !(goalField.GetValue(quest) is JournalLog log))
                {
                    throw new MissingFieldException("Rebuild clan journal goal changed: " + name);
                }

                log.UpdateCurrentProgress((int)rangeField.GetValue(log));
            }
        }

        /// <summary>
        /// Let the original Tevea quest start, then use its success path to write
        /// both journal entries, award its equipment, and remove its map marker.
        /// Reuse an orphaned quest left by the older StartQuest prefix on saves.
        /// </summary>
        internal static bool TryCompleteTeveaQuest(bool createIfMissing)
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                Hero.MainHero == null || MobileParty.MainParty == null)
            {
                return false;
            }

            Settlement village = Settlement.Find("village_ES3_2");
            if (village == null)
            {
                return false;
            }

            if (MBObjectManager.Instance?.GetObject<MBEquipmentRoster>(
                    "stealth_tutorial_set_player") == null)
            {
                return false;
            }

            QuestManager manager = Campaign.Current.QuestManager;
            QuestBase[] tracked = manager.TrackedObjects.TryGetValue(village, out var villageQuests)
                ? villageQuests.ToArray()
                : Array.Empty<QuestBase>();
            VillagersInNeed quest = manager.Quests.OfType<VillagersInNeed>()
                .FirstOrDefault(q => q.IsOngoing);
            if (quest == null)
            {
                quest = tracked.OfType<VillagersInNeed>()
                    .FirstOrDefault(q => !manager.Quests.Contains(q) && !q.IsFinalized);
            }

            if (quest == null && !createIfMissing)
            {
                return true;
            }

            if (quest == null)
            {
                quest = new VillagersInNeed();
            }

            if (!manager.Quests.Contains(quest))
            {
                quest.StartQuest();
            }

            if (!manager.Quests.Contains(quest) || !quest.IsOngoing)
            {
                return false;
            }

            quest.CompleteQuestWithSuccess();
            // The headman's optional payment normally comes from the final dialogue.
            // Keep post-completion failures from repeating the vanilla item reward.
            try
            {
                GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, 100, false);
            }
            catch (Exception ex)
            {
                Debug.Print("[StorySkip] Tevea headman's payment failed: " + ex);
            }

            foreach (VillagersInNeed orphan in tracked.OfType<VillagersInNeed>())
            {
                if (orphan == quest || manager.Quests.Contains(orphan))
                {
                    continue;
                }

                try
                {
                    manager.RemoveTrackedObjectForQuest(village, orphan);
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] Extra Tevea marker cleanup failed: " + ex);
                }
            }

            return true;
        }

        /// <summary>
        /// Runs on a later tick, after the original support/create quest and
        /// AssembleTheBannerQuest have finished their synchronous callbacks.
        /// The chosen side is saved by StoryMode, so this also recovers on load.
        /// </summary>
        public static void TryFinishBannerChoice()
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null)
            {
                return;
            }

            MainStoryLine storyLine = StoryModeManager.Current?.MainStoryLine;
            if (storyLine == null || storyLine.IsCompleted ||
                storyLine.MainStoryLineSide == MainStoryLineSide.None ||
                storyLine.PlayerSupportedKingdom == null ||
                ThirdPhaseField == null || ThirdPhaseCompletedField == null)
            {
                return;
            }

            if (storyLine.SecondPhase == null)
            {
                storyLine.CompleteFirstPhase();
            }

            storyLine.CancelSecondAndThirdPhase();
            RemoveFinalPhaseQuestBehavior();

            QuestBase[] remainingStoryQuests = Campaign.Current.QuestManager.Quests
                .Where(q => q is StoryModeQuestBase && q.IsOngoing &&
                            !(q is VillagersInNeed))
                .OrderBy(q => q is AssembleTheBannerQuest ? 0 : 1).ToArray();
            foreach (QuestBase quest in remainingStoryQuests)
            {
                if (quest.IsOngoing)
                {
                    quest.CompleteQuestWithCancel(null);
                }
            }

            ThirdPhase finalPhase = storyLine.ThirdPhase ?? new ThirdPhase();
            ThirdPhaseCompletedField.SetValue(finalPhase, true);
            ThirdPhaseField.SetValue(storyLine, finalPhase);
        }

        private static void EnsureMentorInSettlement(Hero mentor, Settlement settlement)
        {
            if (mentor.IsNotSpawned)
            {
                Helpers.HeroHelper.SpawnHeroForTheFirstTime(mentor, settlement);
            }
            else if (mentor.CurrentSettlement == null && mentor.PartyBelongedTo == null)
            {
                EnterSettlementAction.ApplyForCharacterOnly(mentor, settlement);
            }
        }

        private static void EnsureBannerFragment(
            TaleWorlds.CampaignSystem.Roster.ItemRoster roster, string itemId)
        {
            ItemObject fragment = Campaign.Current.ObjectManager.GetObject<ItemObject>(itemId);
            if (fragment == null)
            {
                throw new InvalidOperationException("Missing Dragon Banner fragment: " + itemId);
            }

            if (roster.GetItemNumber(fragment) == 0)
            {
                roster.AddToCounts(new EquipmentElement(fragment, null, null, true), 1);
            }
        }

        private static Settlement FindFamilySettlement()
        {
            Settlement current = MobileParty.MainParty.CurrentSettlement;
            if (current != null && current.IsTown && !current.IsUnderSiege)
            {
                return current;
            }

            // Vanilla's rescue quest places siblings in a town. Choose the
            // nearest peaceful town, including when the player starts at sea.
            var towns = Settlement.All
                .Where(s => s.IsTown && !s.IsUnderSiege && s.MapFaction != null &&
                            !s.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction))
                .OrderBy(s => s.Position.DistanceSquared(MobileParty.MainParty.Position))
                .FirstOrDefault();
            return towns ?? Settlement.All
                .Where(s => s.IsTown && !s.IsUnderSiege)
                .OrderBy(s => s.Position.DistanceSquared(MobileParty.MainParty.Position))
                .FirstOrDefault();
        }

        private static void RestoreBrother(Hero brother, Settlement home, bool isElder)
        {
            if (!brother.IsAlive)
            {
                return;
            }

            if (brother.Clan != Clan.PlayerClan)
            {
                brother.Clan = Clan.PlayerClan;
            }

            bool adult = brother.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge;
            if (isElder || adult)
            {
                if (!brother.IsActive)
                {
                    brother.ChangeState(Hero.CharacterStates.Active);
                }

                if (brother.PartyBelongedTo == null && brother.CurrentSettlement != home)
                {
                    EnterSettlementAction.ApplyForCharacterOnly(brother, home);
                }

                if (!isElder)
                {
                    StoryModeHelpers.SetPlayerSiblingsSkillsIfNeeded(brother);
                }
            }
            else if (!brother.IsNotSpawned)
            {
                // Vanilla's rescue quest uses NotSpawned for a rescued minor.
                // They remain visible in the clan and become active at adulthood.
                brother.ChangeState(Hero.CharacterStates.NotSpawned);
            }

            brother.UpdateLastKnownClosestSettlement(home);
        }

        private static void RemoveBannerFragment(TaleWorlds.CampaignSystem.Roster.ItemRoster roster,
            string itemId)
        {
            var fragment = Campaign.Current.ObjectManager.GetObject<ItemObject>(itemId);
            if (fragment == null)
            {
                return;
            }

            int count = roster.GetItemNumber(fragment);
            if (count > 0)
            {
                roster.AddToCounts(new EquipmentElement(fragment, null, null, true), -count);
            }
        }

        // CancelSecondAndThirdPhase removes phase behaviors, but the separate
        // DefeatTheConspiracyQuestBehavior remains subscribed to HourlyTick. With
        // an empty completed ThirdPhase it would dereference PlayerSupportedKingdom.
        public static void RemoveFinalPhaseQuestBehavior()
        {
            if (StorySkipState.IsTargetCampaign && Campaign.Current != null)
            {
                Campaign.Current.CampaignBehaviorManager
                    .RemoveBehavior<DefeatTheConspiracyQuestBehavior>();
            }
        }

        private static void RestoreBrotherEncyclopediaText()
        {
            TextObject younger = new TextObject(
                "{=PDlaPVIP}{PLAYER_LITTLE_BROTHER.NAME} is the little brother of {PLAYER.LINK}.");
            Helpers.StringHelpers.SetCharacterProperties(
                "PLAYER_LITTLE_BROTHER", StoryModeHeroes.LittleBrother.CharacterObject, younger);
            Helpers.StringHelpers.SetCharacterProperties(
                "PLAYER", CharacterObject.PlayerCharacter, younger);
            StoryModeHeroes.LittleBrother.EncyclopediaText = younger;

            TextObject elder = new TextObject(
                "{=LcxfWLgd}{PLAYER_BROTHER.NAME} is the elder brother of {PLAYER.LINK}.");
            Helpers.StringHelpers.SetCharacterProperties(
                "PLAYER_BROTHER", StoryModeHeroes.ElderBrother.CharacterObject, elder);
            Helpers.StringHelpers.SetCharacterProperties(
                "PLAYER", CharacterObject.PlayerCharacter, elder);
            StoryModeHeroes.ElderBrother.EncyclopediaText = elder;
        }

        private static bool VanillaStoryIsBeingSkipped()
        {
            return StorySkipState.IsTargetCampaign &&
                   StoryModeManager.Current?.MainStoryLine?.TutorialPhase?.IsCompleted == true;
        }

        private static bool PreventVanillaStoryQuestStart(QuestBase __instance)
        {
            if (!VanillaStoryIsBeingSkipped() || !(__instance is StoryModeQuestBase))
            {
                return true;
            }

            if (_isCompletingBannerCollectionQuests &&
                (__instance is RebuildPlayerClanQuest ||
                 __instance is BannerInvestigationQuest ||
                 __instance is MeetWithIstianaQuest ||
                 __instance is MeetWithArzagosQuest ||
                 __instance is IstianasBannerPieceQuest ||
                 __instance is ArzagosBannerPieceQuest))
            {
                return true;
            }

            if (__instance is VillagersInNeed)
            {
                return true;
            }

            MainStoryLine storyLine = StoryModeManager.Current?.MainStoryLine;
            return storyLine != null && !storyLine.IsCompleted &&
                   storyLine.MainStoryLineSide == MainStoryLineSide.None &&
                   IsBannerDecisionQuest(__instance);
        }

        private static bool PreventFinishedStealthTutorial()
        {
            return !StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                   Campaign.Current.GetCampaignBehavior<StorySkipCampaignBehavior>()
                       ?.TeveaQuestCompleted != true;
        }

        private static bool IsBannerDecisionQuest(QuestBase quest)
        {
            return quest is AssembleTheBannerQuest || quest is CreateKingdomQuest ||
                   quest is SupportKingdomQuest;
        }

        private static bool PreventMainQuestCreation()
        {
            return !VanillaStoryIsBeingSkipped();
        }

        private static bool PreventDuplicateBannerQuestCreation(QuestBase __0)
        {
            if (!_isCompletingBannerCollectionQuests || Campaign.Current == null)
            {
                return true;
            }

            var quests = Campaign.Current.QuestManager.Quests;
            if (__0 is BannerInvestigationQuest)
            {
                return !quests.Any(q => q.IsOngoing &&
                    (q is MeetWithIstianaQuest || q is MeetWithArzagosQuest));
            }

            if (__0 is MeetWithIstianaQuest)
            {
                return !quests.OfType<IstianasBannerPieceQuest>().Any(q => q.IsOngoing);
            }

            if (__0 is MeetWithArzagosQuest)
            {
                return !quests.OfType<ArzagosBannerPieceQuest>().Any(q => q.IsOngoing);
            }

            return true;
        }

        private static bool PreventAlreadySpawnedMentor(Hero __0)
        {
            return !_isCompletingBannerCollectionQuests ||
                   (__0 != StoryModeHeroes.ImperialMentor &&
                    __0 != StoryModeHeroes.AntiImperialMentor) ||
                   __0.IsNotSpawned;
        }

        private static bool PreventAutoBannerHideoutPreparation()
        {
            return !_isCompletingBannerCollectionQuests;
        }

        private static bool PreventRescueQuestArm(QuestBase __0)
        {
            return !VanillaStoryIsBeingSkipped() || !(__0 is RebuildPlayerClanQuest);
        }

        private static bool PreventRescueQuestAtTown()
        {
            return !VanillaStoryIsBeingSkipped();
        }

        private static bool PreventUninitializedFinalPhaseTick()
        {
            return !VanillaStoryIsBeingSkipped();
        }

        private static void PatchIfPresent(Harmony harmony, MethodBase original, string prefixName)
        {
            if (original == null)
            {
                throw new MissingMethodException("StorySkip required StoryMode patch target missing: " + prefixName);
            }

            harmony.Patch(original,
                prefix: new HarmonyMethod(typeof(StorySkipStoryMode), prefixName));
        }
    }
}
