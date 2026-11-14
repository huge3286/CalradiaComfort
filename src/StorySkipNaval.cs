using System;
using System.Reflection;
using HarmonyLib;
using NavalDLC;
using NavalDLC.CampaignBehaviors;
using NavalDLC.Storyline;
using NavalDLC.Storyline.CampaignBehaviors;
using NavalDLC.Storyline.MissionControllers;
using NavalDLC.Storyline.Quests;
using StoryMode;
using StoryMode.StoryModeObjects;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace StorySkip
{
    /// <summary>
    /// Settles the safe War Sails story quests through their original success paths.
    /// This code targets the NavalDLC 1.3.7 methods in the installed game.
    /// </summary>
    internal static class StorySkipNaval
    {
        private const string IntroductionShownFieldName = "_hasIntroductionPopUpBeenShown";
        private const string CanceledFieldName = "_isNavalStorylineCanceled";
        private const string LastCompletedStageFieldName = "_lastCompletedStorylineStage";
        private const string CrewRewardMethodName = "OnPlayerWelcomedGunnarsCrew";
        private static bool _isApplying;

        public static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo showIntroduction = AccessTools.Method(
                typeof(NavalInitializationCampaignBehavior), "ShowIntroductionPopUp");
            MethodInfo startStoryline = AccessTools.Method(
                typeof(NavalStorylineData), nameof(NavalStorylineData.StartNavalStoryline));
            MethodInfo suppressIntroduction = AccessTools.Method(
                typeof(StorySkipNaval), nameof(SuppressIntroduction));
            MethodInfo suppressStorylineStart = AccessTools.Method(
                typeof(StorySkipNaval), nameof(SuppressStorylineStart));

            if (showIntroduction == null || startStoryline == null ||
                suppressIntroduction == null || suppressStorylineStart == null ||
                AccessTools.Field(typeof(NavalInitializationCampaignBehavior),
                    IntroductionShownFieldName) == null)
            {
                throw new MissingMethodException(
                    "War Sails storyline entry points changed; StorySkip needs an update.");
            }

            harmony.Patch(showIntroduction, prefix: new HarmonyMethod(suppressIntroduction));
            harmony.Patch(startStoryline, prefix: new HarmonyMethod(suppressStorylineStart));
        }

        /// <summary>
        /// Returns false until the DLC has created its campaign behavior and story
        /// characters. The caller may retry on a later campaign tick.
        /// </summary>
        public static bool Apply(ref int completedStage)
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null)
                return false;

            if (_isApplying)
                return false;

            NavalStorylineCampaignBehavior behavior =
                Campaign.Current.GetCampaignBehavior<NavalStorylineCampaignBehavior>();
            Hero sister = StoryModeHeroes.LittleSister;
            if (behavior == null || sister == null)
                return false;

            if (!EnsureStorylineData(behavior))
                return false;

            _isApplying = true;
            try
            {
                if (completedStage < 3 &&
                    FindOngoingQuest<ScourgeoftheSeasQuest>() == null)
                {
                    // An old save or interrupted attempt may have lost the
                    // overarching quest. Its own event listener is needed for
                    // the original final success callback.
                    var overview = new ScourgeoftheSeasQuest();
                    overview.StartQuest();
                    if (completedStage < 1)
                        completedStage = 1;
                }

                // Persist each phase through StorySkipCampaignBehavior so a later
                // tick or load never starts a second copy of a completed quest.
                if (completedStage < 2)
                {
                    InquireAtOstican introduction =
                        FindOngoingQuest<InquireAtOstican>();
                    if (introduction == null)
                    {
                        introduction = new InquireAtOstican();
                        introduction.StartQuest();
                    }
                    MethodInfo onGunnarSaved = AccessTools.Method(
                        typeof(InquireAtOstican), "OnGunnarSaved");
                    if (onGunnarSaved == null)
                        throw new MissingMethodException(
                            "War Sails Gunnar rescue callback changed; StorySkip needs an update.");
                    Hero gunnar = NavalStorylineData.Gunnar;
                    int previousBaseRelation =
                        gunnar.GetBaseHeroRelation(Hero.MainHero);
                    onGunnarSaved.Invoke(introduction, null);
                    if (previousBaseRelation > 15)
                        gunnar.SetPersonalRelation(Hero.MainHero,
                            previousBaseRelation);
                    introduction.CompleteQuestWithSuccess();
                    completedStage = 2;
                }

                if (completedStage < 3)
                {
                    SpeakToGunnarAndSisterQuest reunion =
                        FindOngoingQuest<SpeakToGunnarAndSisterQuest>();
                    if (reunion == null)
                    {
                        reunion = new SpeakToGunnarAndSisterQuest(
                            Quest5SetPieceBattleMissionController.BossFightOutComeEnum
                                .PlayerAcceptedAndWonTheDuel);
                        reunion.StartQuest();
                    }

                    // The DLC's final quest awards the sister and settles the
                    // Scourge of the Seas journal quest via its completion event.
                    reunion.CompleteQuestWithSuccess();
                    ScourgeoftheSeasQuest remainingOverview =
                        FindOngoingQuest<ScourgeoftheSeasQuest>();
                    if (remainingOverview != null)
                    {
                        // Some old canceled saves did not register the DLC's
                        // completion listener. Use the overview quest's own
                        // original outro path if its event was missed.
                        MethodInfo updateOutro = AccessTools.Method(
                            typeof(ScourgeoftheSeasQuest), "UpdateOutro");
                        if (updateOutro == null)
                            throw new MissingMethodException(
                                "War Sails overview outro changed; StorySkip needs an update.");
                        updateOutro.Invoke(remainingOverview, null);
                    }
                    if (sister.Clan == Clan.PlayerClan &&
                        FindOngoingQuest<ScourgeoftheSeasQuest>() == null)
                        completedStage = 3;
                }

                bool completed = completedStage >= 3 &&
                    sister.Clan == Clan.PlayerClan &&
                    !Campaign.Current.QuestManager.IsThereActiveQuestWithType(
                        typeof(SpeakToGunnarAndSisterQuest)) &&
                    !Campaign.Current.QuestManager.IsThereActiveQuestWithType(
                        typeof(ScourgeoftheSeasQuest));
                if (completed)
                {
                    // The final battle normally records stage 6 before the
                    // plain QuestBase reunion starts. Gunnar's village dialogue
                    // and the sister's later state checks depend on that stage.
                    FieldInfo lastStageField = AccessTools.Field(
                        typeof(NavalStorylineCampaignBehavior),
                        LastCompletedStageFieldName);
                    if (lastStageField == null)
                        throw new MissingFieldException(
                            "War Sails completion stage changed; StorySkip needs an update.");
                    var lastStage = (NavalStorylineData.NavalStorylineStage)
                        lastStageField.GetValue(behavior);
                    if (lastStage < NavalStorylineData.NavalStorylineStage.Act3Quest5)
                        lastStageField.SetValue(behavior,
                            NavalStorylineData.NavalStorylineStage.Act3Quest5);
                    if (behavior.IsNavalStorylineActive())
                        Debug.Print("[StorySkip] War Sails voyage flag is still active; automatic deactivation was skipped to preserve the player's current party and ships.");
                    Debug.Print("[StorySkip] War Sails intro, reunion, and main journal quest completed through native success callbacks; intermediate naval chapters were not started.");
                }
                return completed;
            }
            finally
            {
                _isApplying = false;
            }
        }

        /// <summary>
        /// Invokes the original final-dialogue choice that accepts Gunnar's
        /// Wave-Steed and twenty crewmen. The caller must persist its own once-only
        /// flag because the native consequence is not idempotent.
        /// </summary>
        public static bool TryGrantCrewReward()
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                NavalDLCManager.Instance == null || NavalStorylineData.Gunnar == null ||
                MobileParty.MainParty == null || MBObjectManager.Instance == null)
                return false;

            if (MBObjectManager.Instance.GetObject<ShipHull>("northern_medium_ship") == null ||
                MBObjectManager.Instance.GetObject<CharacterObject>("nord_spear_warrior") == null ||
                MBObjectManager.Instance.GetObject<CharacterObject>("nord_vargr") == null)
                return false;

            MethodInfo acceptCrew = AccessTools.Method(
                typeof(SpeakToGunnarAndSisterQuest), CrewRewardMethodName);
            if (acceptCrew == null)
                throw new MissingMethodException(
                    "War Sails crew reward consequence changed; StorySkip needs an update.");

            // This choice consequence reads no quest instance fields. A fresh
            // instance avoids replaying the completed final quest itself.
            var reunion = new SpeakToGunnarAndSisterQuest(
                Quest5SetPieceBattleMissionController.BossFightOutComeEnum
                    .PlayerAcceptedAndWonTheDuel);
            acceptCrew.Invoke(reunion, null);
            if (MobileParty.MainParty.Anchor != null &&
                !MobileParty.MainParty.Anchor.IsValid &&
                NavalStorylineData.HomeSettlement != null &&
                NavalStorylineData.HomeSettlement.HasPort)
            {
                // In the original dialogue the player stands at Ostican's port.
                // An automated map completion needs that same ship anchor,
                // without moving the player's party.
                MobileParty.MainParty.Anchor.Settlement = NavalStorylineData.HomeSettlement;
            }
            Debug.Print("[StorySkip] Accepted Gunnar's Wave-Steed and twenty crewmen through the original dialogue consequence.");
            return true;
        }

        private static T FindOngoingQuest<T>() where T : QuestBase
        {
            foreach (QuestBase quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest is T result && quest.IsOngoing)
                    return result;
            }
            return null;
        }

        private static bool EnsureStorylineData(NavalStorylineCampaignBehavior behavior)
        {
            if (NavalDLCManager.Instance == null ||
                NavalDLCManager.Instance.NavalStorylineData == null)
                return false;

            if (NavalStorylineData.Gunnar != null &&
                NavalStorylineData.HomeSettlement != null)
                return true;

            if (!behavior.GetIsNavalStorylineCanceled())
                return false;

            // Old versions of this mod saved the DLC's canceled flag. On reload,
            // NavalStorylineData.Initialize omits its hero and settlement caches.
            // Rehydrate those caches without reactivating DLC behavior listeners.
            FieldInfo canceledField = AccessTools.Field(
                typeof(NavalStorylineCampaignBehavior), CanceledFieldName);
            if (canceledField == null)
                throw new MissingFieldException(
                    "War Sails cancellation field changed; StorySkip needs an update.");

            bool wasCanceled = (bool)canceledField.GetValue(behavior);
            canceledField.SetValue(behavior, false);
            try
            {
                NavalDLCManager.Instance.NavalStorylineData.Initialize();
            }
            finally
            {
                canceledField.SetValue(behavior, wasCanceled);
            }

            return NavalStorylineData.Gunnar != null &&
                   NavalStorylineData.HomeSettlement != null;
        }

        /// <summary>
        /// StoryMode deliberately skips the sister's skills whenever NavalDLC is on.
        /// On an old canceled save the DLC removed its age-event listener.
        /// HeroSpawn and Aging campaign behaviors still handle state, location,
        /// level, and equipment; only the sibling-specific skills are missing.
        /// </summary>
        public static bool OnSisterComesOfAge(Hero hero)
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null ||
                hero == null || hero != StoryModeHeroes.LittleSister ||
                hero.Clan != Clan.PlayerClan || !hero.IsAlive)
                return false;

            StoryModeHelpers.SetPlayerSiblingsSkillsIfNeeded(hero);
            return true;
        }

        /// <summary>
        /// Rechecks an already adult sister after the initial skip or a game load.
        /// Call this only when a saved campaign has not yet recorded that the
        /// sister's sibling skills were initialized.
        /// </summary>
        public static bool EnsureSisterAdultState()
        {
            if (!StorySkipState.IsTargetCampaign || Campaign.Current == null)
                return false;

            Hero sister = StoryModeHeroes.LittleSister;
            if (sister != null &&
                sister.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge)
                return OnSisterComesOfAge(sister);

            return false;
        }

        private static bool SuppressIntroduction(
            NavalInitializationCampaignBehavior __instance)
        {
            if (!StorySkipState.IsTargetCampaign)
                return true;

            // Preserve the DLC's one-time popup state even though its UI is skipped.
            AccessTools.Field(typeof(NavalInitializationCampaignBehavior),
                IntroductionShownFieldName).SetValue(__instance, true);
            return false;
        }

        private static bool SuppressStorylineStart()
        {
            return !StorySkipState.IsTargetCampaign;
        }
    }
}
