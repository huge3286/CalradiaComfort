using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class InstantPregnancy
    {
        internal static bool IsEligiblePair(Hero owner, Hero partner)
        {
            return Campaign.Current != null && ComfortFeatures.InstantPregnancyEnabled &&
                owner != null && owner == Hero.MainHero && partner != null && partner != owner &&
                owner.IsAlive && partner.IsAlive && owner.Age >= 18f && partner.Age >= 18f &&
                !owner.IsPrisoner && !partner.IsPrisoner && owner.IsFemale != partner.IsFemale &&
                owner.Clan != null && owner.Clan == partner.Clan && PluralMarriage.AreSpouses(owner, partner);
        }

        internal static bool CanConceive(Hero owner, Hero partner, out TextObject reason)
        {
            reason = new TextObject("");
            if (!IsEligiblePair(owner, partner))
            {
                reason = new TextObject("现在无法与这位角色使用此选项。");
                return false;
            }
            Hero mother = owner.IsFemale ? owner : partner;
            if (mother.IsPregnant)
            {
                reason = new TextObject("已经怀孕，正在等待新成员出生。");
                return false;
            }
            if (CampaignOptions.IsLifeDeathCycleDisabled)
            {
                reason = new TextObject("当前战役未启用生育与死亡，无法按正常孕期生育。");
                return false;
            }
            if (Campaign.Current.GetCampaignBehavior<PregnancyCampaignBehavior>() == null)
            {
                reason = new TextObject("当前战役没有启用原版生育流程。");
                return false;
            }
            return true;
        }

        internal static bool TryConceive(Hero owner, Hero partner)
        {
            TextObject reason;
            if (!CanConceive(owner, partner, out reason)) return false;
            Hero mother = owner.IsFemale ? owner : partner;
            Hero father = owner.IsFemale ? partner : owner;
            PluralMarriage.MakePregnantWith(mother, father);
            return mother.IsPregnant;
        }
    }

    internal sealed class InstantPregnancyCampaignBehavior : CampaignBehaviorBase
    {
        private bool _conceived;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogs);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Native PregnancyCampaignBehavior owns parents, due date and save data.
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            _conceived = false;
            // The screenshot's "What is your proposal?" state, plus direct access
            // from the spouse's normal options. Both routes execute the same action.
            foreach (string input in new[] { "hero_main_options", "lord_talk_speak_diplomacy_2" })
                starter.AddPlayerLine("comfort_instant_pregnancy_" + input, input,
                    "comfort_instant_pregnancy_response", "我们来开一局吧。",
                    CanOffer, Conceive, 115, CanClick);
            starter.AddDialogLine("comfort_instant_pregnancy_success", "comfort_instant_pregnancy_response",
                "hero_main_options", "好，我们一起期待新的家人吧。", () => _conceived, null, 115);
            starter.AddDialogLine("comfort_instant_pregnancy_unavailable", "comfort_instant_pregnancy_response",
                "hero_main_options", "现在还不合适，我们稍后再说吧。", () => !_conceived, null, 114);
        }

        private bool CanOffer() => InstantPregnancy.IsEligiblePair(Hero.MainHero, Hero.OneToOneConversationHero);
        private bool CanClick(out TextObject reason) => InstantPregnancy.CanConceive(Hero.MainHero, Hero.OneToOneConversationHero, out reason);
        private void Conceive() => _conceived = InstantPregnancy.TryConceive(Hero.MainHero, Hero.OneToOneConversationHero);
    }
}
