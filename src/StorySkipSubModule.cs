using System;
using HarmonyLib;
using StoryMode;
using StoryMode.StoryModeObjects;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace StorySkip
{
    public static class StorySkipState
    {
        private static bool _isTargetCampaign;

        public static bool IsTargetCampaign
        {
            get
            {
                return _isTargetCampaign && Game.Current != null &&
                       Game.Current.GameType is CampaignStoryMode;
            }
        }

        internal static void SetTargetCampaign(bool value)
        {
            _isTargetCampaign = value;
        }
    }

    public sealed class StorySkipSubModule : MBSubModuleBase
    {
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony("StorySkip.NewCampaign");
            _harmony = harmony;
            ComfortBranding.InitializePatches(harmony);
            StorySkipStoryMode.InitializePatches(harmony);
            StorySkipNaval.InitializePatches(harmony);
            EnableAchievements.InitializePatches(harmony);
            BothPerks.InitializePatches(harmony);
            LearningLimitSkills.InitializePatches(harmony);
            SharedTroopExperience.InitializePatches(harmony);
            SharedTroopExperienceUI.InitializePatches(harmony);
            QuickRecruitment.InitializePatches(harmony);
            InstantConstruction.InitializePatches(harmony);
            MatrilinealMarriage.InitializePatches(harmony);
            PluralMarriage.InitializePatches(harmony);
            PluralMarriageUI.InitializePatches(harmony);
            ImmediateMarriageBarter.InitializePatches(harmony);
            DiscardItemGold.InitializePatches(harmony);
            HeroLiveLocationUI.InitializePatches(harmony);
            ClanPartyLimit.InitializePatches(harmony);
            ClanCompanionLimit.InitializePatches(harmony);
            AlliedPartySpeed.InitializePatches(harmony);
            PlayerOverwhelmingAttack.InitializePatches(harmony);
            InstantSiegeAssault.InitializePatches(harmony);
            QuietRelationNotifications.InitializePatches(harmony);
            PortableFirePotEquipment.InitializePatches(harmony);
            GarrisonResettlement.InitializePatches(harmony);
            ClanPartyStrategy.InitializePatches(harmony);
        }

        protected override void OnSubModuleUnloaded()
        {
            QuickRecruitmentUI.Cleanup();
            InstantConstruction.SetCampaignReady(false);
            PluralMarriageCampaignBehavior.ClearCurrent();
            ImmediateMarriageBarter.ClearSession();
            DiscardItemGold.ClearSession();
            HeroLiveLocationUI.ClearSession();
            AlliedPartySpeed.ClearSession();
            ClanPartyStrategyBehavior.ClearCurrent();
            base.OnSubModuleUnloaded();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            StorySkipState.SetTargetCampaign(false);
            ClanPartyStrategyBehavior.ClearCurrent();
            ImmediateMarriageBarter.ClearSession();
            DiscardItemGold.ClearSession();
            HeroLiveLocationUI.ClearSession();
            AlliedPartySpeed.ClearSession();
            if (gameStarterObject is CampaignGameStarter starter)
            {
                EnableAchievements.AddMissingAchievementBehavior(starter);
                starter.AddBehavior(new StorySkipCampaignBehavior());
                starter.AddBehavior(new BothPerksCampaignBehavior());
                starter.AddBehavior(new LearningLimitSkillsCampaignBehavior());
                starter.AddBehavior(new EnableAchievementsCampaignBehavior());
                starter.AddBehavior(new SharedTroopExperienceCampaignBehavior());
                starter.AddBehavior(new InstantConstructionCampaignBehavior());
                starter.AddBehavior(new PluralMarriageCampaignBehavior());
                starter.AddBehavior(new ReleasedCaptiveCourtshipCampaignBehavior());
                starter.AddBehavior(new InstantPregnancyCampaignBehavior());
                starter.AddBehavior(new DiscardItemGoldCampaignBehavior());
                starter.AddBehavior(new SellClanLordPrisonersCampaignBehavior());
                starter.AddBehavior(new PortableFirePotsCampaignBehavior());
                starter.AddBehavior(new CapitalGainsCampaignBehavior());
                starter.AddBehavior(new ClanPartyStrategyBehavior());
            }
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            // All these UI extensions are campaign screens. Register once after
            // native text initialization, for both new games and loaded saves.
            if (game?.GameType is Campaign)
                QuickRecruitmentUI.Initialize(_harmony);
        }

        public override void OnNewGameCreated(Game game, object initializerObject)
        {
            base.OnNewGameCreated(game, initializerObject);
            if (game.GameType is CampaignStoryMode && ComfortFeatures.ConfiguredStorySkip)
            {
                StorySkipState.SetTargetCampaign(true);
            }
        }
    }

    internal sealed class StorySkipCampaignBehavior : CampaignBehaviorBase
    {
        private bool _isTargetCampaign;
        private bool _characterCreationFinished;
        private bool _storyApplied;
        private bool _bannerCollectionQuestsCompleted;
        private int _bannerCollectionQuestStage;
        private bool _teveaQuestCompleted;
        private bool _createTeveaQuestIfMissing;
        private bool _navalApplied;
        private bool _navalOriginalSuccessApplied;
        private int _navalQuestStage;
        private bool _navalCrewRewardApplied;
        private bool _sisterSkillsApplied;
        private bool _reportedSuccess;
        private float _retryDelay;

        internal bool TeveaQuestCompleted => _teveaQuestCompleted;

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnCharacterCreationFinished);
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, OnGameLoadFinished);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, TryApply);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("StorySkip_TargetCampaign", ref _isTargetCampaign);
            dataStore.SyncData("StorySkip_CreationFinished", ref _characterCreationFinished);
            dataStore.SyncData("StorySkip_StoryApplied", ref _storyApplied);
            dataStore.SyncData("CalradiaComfort_BannerCollectionQuestsCompleted", ref _bannerCollectionQuestsCompleted);
            dataStore.SyncData("CalradiaComfort_BannerCollectionQuestStage", ref _bannerCollectionQuestStage);
            dataStore.SyncData("CalradiaComfort_TeveaQuestCompleted", ref _teveaQuestCompleted);
            dataStore.SyncData("CalradiaComfort_CreateTeveaQuestIfMissing", ref _createTeveaQuestIfMissing);
            dataStore.SyncData("StorySkip_NavalApplied", ref _navalApplied);
            dataStore.SyncData("CalradiaComfort_NavalOriginalSuccessApplied", ref _navalOriginalSuccessApplied);
            dataStore.SyncData("CalradiaComfort_NavalQuestStage", ref _navalQuestStage);
            dataStore.SyncData("CalradiaComfort_NavalCrewRewardApplied", ref _navalCrewRewardApplied);
            dataStore.SyncData("StorySkip_SisterSkillsApplied", ref _sisterSkillsApplied);
            dataStore.SyncData("StorySkip_ReportedSuccess", ref _reportedSuccess);
            StorySkipState.SetTargetCampaign(_isTargetCampaign);
            _navalOriginalSuccessApplied |= _navalQuestStage >= 3;
            _navalApplied |= _navalOriginalSuccessApplied;
            _bannerCollectionQuestsCompleted |= _bannerCollectionQuestStage >= 6;
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            _isTargetCampaign = Game.Current != null &&
                                Game.Current.GameType is CampaignStoryMode &&
                                ComfortFeatures.ConfiguredStorySkip;
            _createTeveaQuestIfMissing = _isTargetCampaign;
            StorySkipState.SetTargetCampaign(_isTargetCampaign);
        }

        private void OnCharacterCreationFinished()
        {
            if (!_isTargetCampaign)
            {
                return;
            }

            _characterCreationFinished = true;
        }

        private void OnTick(float dt)
        {
            if (_retryDelay > 0f)
            {
                _retryDelay -= dt;
                return;
            }

            _retryDelay = 1f;
            TryApply();
        }

        private void OnHeroComesOfAge(Hero hero)
        {
            if (_isTargetCampaign && _navalApplied && !_sisterSkillsApplied &&
                hero == StoryModeHeroes.LittleSister)
            {
                try
                {
                    _sisterSkillsApplied = StorySkipNaval.OnSisterComesOfAge(hero);
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] Sister adulthood update failed: " + ex);
                }
            }
        }

        private void OnGameLoadFinished()
        {
            if (_isTargetCampaign)
            {
                try
                {
                    // StoryMode registers this quest behavior again on every load.
                    StorySkipStoryMode.RemoveFinalPhaseQuestBehavior();
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] Final-phase behavior cleanup failed: " + ex);
                }
            }

            TrySisterAdultSkills();
            TryApply();
        }

        private void TryApply()
        {
            if (!_isTargetCampaign || !_characterCreationFinished)
            {
                return;
            }

            TrySisterAdultSkills();
            try
            {
                StorySkipStoryMode.TryFinishBannerChoice();
            }
            catch (Exception ex)
            {
                Debug.Print("[StorySkip] Dragon Banner choice cleanup failed: " + ex);
            }

            if (_storyApplied && _bannerCollectionQuestsCompleted &&
                _navalApplied && _navalOriginalSuccessApplied &&
                _navalCrewRewardApplied && _teveaQuestCompleted)
            {
                return;
            }

            bool storyAppliedNow = false;
            if (!_storyApplied)
            {
                try
                {
                    _storyApplied = StorySkipStoryMode.Apply();
                    storyAppliedNow = _storyApplied;
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] StoryMode application failed: " + ex);
                }
            }

            if (_storyApplied && !_bannerCollectionQuestsCompleted)
            {
                try
                {
                    _bannerCollectionQuestsCompleted =
                        StorySkipStoryMode.TryCompleteBannerCollectionQuests(
                            ref _bannerCollectionQuestStage);
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] Dragon Banner collection quest completion failed: " + ex);
                }
            }

            if (_storyApplied && !_teveaQuestCompleted)
            {
                try
                {
                    _teveaQuestCompleted = StorySkipStoryMode.TryCompleteTeveaQuest(
                        _createTeveaQuestIfMissing);
                    if (_teveaQuestCompleted && storyAppliedNow)
                    {
                        Debug.Print("[StorySkip] Completed the Tevea stealth tutorial quest with its rewards.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] Tevea quest completion failed: " + ex);
                }
            }

            if (!_navalOriginalSuccessApplied)
            {
                try
                {
                    _navalOriginalSuccessApplied = StorySkipNaval.Apply(
                        ref _navalQuestStage);
                    _navalApplied |= _navalOriginalSuccessApplied;
                    if (_navalOriginalSuccessApplied && SisterIsAdult())
                    {
                        // Apply already ran the sibling skill recovery once.
                        _sisterSkillsApplied = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] War Sails application failed: " + ex);
                }
            }

            if (_navalOriginalSuccessApplied && !_navalCrewRewardApplied)
            {
                try
                {
                    _navalCrewRewardApplied = StorySkipNaval.TryGrantCrewReward();
                }
                catch (Exception ex)
                {
                    Debug.Print("[StorySkip] War Sails crew reward failed: " + ex);
                }
            }

            if (_storyApplied && _bannerCollectionQuestsCompleted &&
                _navalOriginalSuccessApplied && _navalCrewRewardApplied &&
                !_reportedSuccess)
            {
                Debug.Print("[StorySkip] Completed safe opening quests and prepared the Dragon Banner choice and War Sails rescue.");
                _reportedSuccess = true;
            }
        }

        private static bool SisterIsAdult()
        {
            Hero sister = StoryModeHeroes.LittleSister;
            return sister != null && Campaign.Current != null &&
                   sister.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge;
        }

        private void TrySisterAdultSkills()
        {
            if (!_isTargetCampaign || !_navalApplied || _sisterSkillsApplied ||
                !SisterIsAdult())
            {
                return;
            }

            try
            {
                _sisterSkillsApplied = StorySkipNaval.EnsureSisterAdultState();
            }
            catch (Exception ex)
            {
                Debug.Print("[StorySkip] Sister adult skills update failed: " + ex);
            }
        }
    }
}
