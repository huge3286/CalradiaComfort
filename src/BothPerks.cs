using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper.PerkSelection;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class BothPerks
    {
        private static bool _campaignReady;
        private static readonly HashSet<Hero> PendingHeroes = new HashSet<Hero>();

        [ThreadStatic]
        private static HashSet<Hero> _updatingHeroes;

        private static readonly FieldInfo SelectionDeveloperField =
            AccessTools.Field(typeof(PerkSelectionVM), "_developer");
        private static readonly FieldInfo SelectedPerksField =
            AccessTools.Field(typeof(PerkSelectionVM), "_selectedPerks");

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo setPerk = AccessTools.Method(typeof(Hero), "SetPerkValueInternal",
                new[] { typeof(PerkObject), typeof(bool) });
            MethodInfo setSkill = AccessTools.Method(typeof(Hero), "SetSkillValue",
                new[] { typeof(SkillObject), typeof(int) });
            MethodInfo selected = AccessTools.Method(typeof(PerkSelectionVM), "IsPerkSelected",
                new[] { typeof(PerkObject) });
            ConstructorInfo heroItem = AccessTools.Constructor(typeof(CharacterDeveloperHeroItemVM),
                new[] { typeof(Hero), typeof(Action) });
            if (setPerk == null || setSkill == null || selected == null || heroItem == null ||
                SelectionDeveloperField == null || SelectedPerksField == null)
            {
                throw new MissingMemberException(
                    "Both Perks hooks changed in this Bannerlord version.");
            }

            harmony.Patch(setPerk, postfix: new HarmonyMethod(typeof(BothPerks), nameof(AfterPerkValueSet)));
            harmony.Patch(setSkill, postfix: new HarmonyMethod(typeof(BothPerks), nameof(AfterSkillValueSet)));
            harmony.Patch(selected, postfix: new HarmonyMethod(typeof(BothPerks), nameof(IncludePendingAlternative)));
            harmony.Patch(heroItem, prefix: new HarmonyMethod(typeof(BothPerks), nameof(BeforeHeroItemConstructed)));
        }

        internal static void SetCampaignReady(bool ready)
        {
            _campaignReady = ready;
            if (!ready)
                PendingHeroes.Clear();
        }

        private static bool IsPlayerHero(Hero hero)
        {
            return hero != null && !hero.IsDead && hero.HeroDeveloper != null &&
                   (hero == Hero.MainHero ||
                    (Clan.PlayerClan != null && hero.Clan == Clan.PlayerClan));
        }

        private static bool IsQualified(Hero hero, PerkObject perk)
        {
            return perk != null && !perk.IsTrash && perk.Skill != null &&
                   hero.GetSkillValue(perk.Skill) >= perk.RequiredSkillValue;
        }

        private static bool CanUpdate(Hero hero)
        {
            return _campaignReady && Campaign.Current != null &&
                   ComfortFeatures.BothPerksEnabled && IsPlayerHero(hero);
        }

        private static void AfterPerkValueSet(Hero __instance, PerkObject __0, bool __1)
        {
            if (!__1 || !CanUpdate(__instance) || __0 == null ||
                !__instance.GetPerkValue(__0))
                return;

            TryGrantAlternative(__instance, __0);
        }

        private static void AfterSkillValueSet(Hero __instance, SkillObject __0)
        {
            if (__0 != null && CanUpdate(__instance))
            {
                // Initialization can reset focus/attributes after setting skills.
                // Finish that operation before awarding one-time perk bonuses.
                PendingHeroes.Add(__instance);
            }
        }

        private static void BeforeHeroItemConstructed(Hero __0)
        {
            // Refresh before the UI takes its attribute, focus and perk snapshots.
            RefreshHero(__0);
        }

        private static void IncludePendingAlternative(
            PerkSelectionVM __instance, PerkObject __0, ref bool __result)
        {
            if (__result || __0?.AlternativePerk == null || !ComfortFeatures.BothPerksEnabled)
                return;

            HeroDeveloper developer = SelectionDeveloperField.GetValue(__instance) as HeroDeveloper;
            List<PerkObject> selections = SelectedPerksField.GetValue(__instance) as List<PerkObject>;
            if (developer != null && CanUpdate(developer.Hero) &&
                IsQualified(developer.Hero, __0) && selections != null)
            {
                // This is a preview only; the actual perk is granted on Apply.
                __result = selections.Contains(__0.AlternativePerk);
            }
        }

        private static void TryGrantAlternative(Hero hero, PerkObject perk)
        {
            PerkObject alternative = perk.AlternativePerk;
            if (alternative == null || !IsQualified(hero, alternative) ||
                hero.GetPerkValue(alternative) || !EnterUpdate(hero))
                return;

            try
            {
                // AddPerk raises the native OnPerkOpened event, including its
                // one-time attribute/focus/health consequences.
                hero.HeroDeveloper.AddPerk(alternative);
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Paired perk grant failed: " + ex);
            }
            finally
            {
                _updatingHeroes.Remove(hero);
            }
        }

        private static bool EnterUpdate(Hero hero)
        {
            if (_updatingHeroes == null)
                _updatingHeroes = new HashSet<Hero>();
            return _updatingHeroes.Add(hero);
        }

        internal static void RefreshHero(Hero hero, SkillObject skill = null)
        {
            if (!CanUpdate(hero) || !EnterUpdate(hero))
                return;

            try
            {
                foreach (PerkObject perk in PerkObject.All)
                {
                    if ((skill != null && perk.Skill != skill) ||
                        !IsQualified(hero, perk) || hero.GetPerkValue(perk))
                        continue;

                    hero.HeroDeveloper.AddPerk(perk);
                }
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Perk refresh failed: " + ex);
            }
            finally
            {
                _updatingHeroes.Remove(hero);
            }
        }

        internal static void RefreshPlayerClan()
        {
            if (!_campaignReady || Campaign.Current == null ||
                !ComfortFeatures.BothPerksEnabled || Clan.PlayerClan == null)
                return;

            // Snapshot the clan because native perk event listeners may update it.
            var heroes = new HashSet<Hero>(Clan.PlayerClan.Heroes.ToArray());
            foreach (Hero companion in Clan.PlayerClan.Companions.ToArray())
                heroes.Add(companion);
            if (Hero.MainHero != null)
                heroes.Add(Hero.MainHero);
            foreach (Hero hero in heroes)
                RefreshHero(hero);
        }

        internal static void RefreshPendingHeroes()
        {
            if (PendingHeroes.Count == 0)
                return;
            Hero[] pending = PendingHeroes.ToArray();
            PendingHeroes.Clear();
            foreach (Hero hero in pending)
                RefreshHero(hero);
        }
    }

    internal sealed class BothPerksCampaignBehavior : CampaignBehaviorBase
    {
        private bool _ready;
        private bool _wasEnabled;

        internal BothPerksCampaignBehavior()
        {
            BothPerks.SetCampaignReady(false);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyClanTick);
            CampaignEvents.HeroGainedSkill.AddNonSerializedListener(this, OnSkillGained);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnNewCompanionAdded);
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Granted perks are already serialized by Hero; no extra save data is needed.
        }

        private void StartCampaign()
        {
            _ready = true;
            BothPerks.SetCampaignReady(true);
            _wasEnabled = ComfortFeatures.BothPerksEnabled;
            BothPerks.RefreshPlayerClan();
        }

        private void OnTick(float dt)
        {
            if (!_ready)
                return;

            bool enabled = ComfortFeatures.BothPerksEnabled;
            if (enabled && !_wasEnabled)
                BothPerks.RefreshPlayerClan();
            _wasEnabled = enabled;
            BothPerks.RefreshPendingHeroes();
        }

        private void OnDailyClanTick(Clan clan)
        {
            if (_ready && clan == Clan.PlayerClan)
                BothPerks.RefreshPlayerClan();
        }

        private void OnSkillGained(Hero hero, SkillObject skill, int change, bool shouldNotify)
        {
            if (_ready)
                BothPerks.RefreshHero(hero, skill);
        }

        private void OnNewCompanionAdded(Hero hero)
        {
            if (_ready)
                BothPerks.RefreshHero(hero);
        }

        private void OnHeroChangedClan(Hero hero, Clan previousClan)
        {
            if (_ready)
                BothPerks.RefreshHero(hero);
        }

        private void OnHeroComesOfAge(Hero hero)
        {
            if (_ready)
                BothPerks.RefreshHero(hero);
        }
    }
}
