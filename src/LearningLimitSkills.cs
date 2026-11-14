using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class LearningLimitSkills
    {
        private const int MaxRefreshPasses = 16;
        // Native skill XP tables and GetSkillLevelChange stop at level 1023.
        private const int MaxNativeSkillLevel = 1023;
        private static readonly MethodInfo GainRawXpMethod = AccessTools.Method(
            typeof(HeroDeveloper), "GainRawXp", new[] { typeof(float), typeof(bool) });
        private static bool _campaignReady;
        private static readonly HashSet<Hero> PendingHeroes = new HashSet<Hero>();
        private static readonly FieldInfo OriginalFocusPointsField = AccessTools.Field(
            typeof(CharacterDeveloperHeroItemVM), "<OrgUnspentFocusPoints>k__BackingField");
        private static readonly FieldInfo OriginalAttributePointsField = AccessTools.Field(
            typeof(CharacterDeveloperHeroItemVM), "<OrgUnspentAttributePoints>k__BackingField");

        [ThreadStatic]
        private static HashSet<Hero> _updatingHeroes;

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo addSkillXp = AccessTools.Method(typeof(HeroDeveloper), "AddSkillXp",
                new[] { typeof(SkillObject), typeof(float), typeof(bool), typeof(bool) });
            MethodInfo setFocus = AccessTools.Method(typeof(HeroDeveloper), "SetFocus",
                new[] { typeof(SkillObject), typeof(int) });
            MethodInfo setAttribute = AccessTools.Method(typeof(Hero), "SetAttributeValueInternal",
                new[] { typeof(CharacterAttribute), typeof(int) });
            MethodInfo setSkill = AccessTools.Method(typeof(Hero), "SetSkillValue",
                new[] { typeof(SkillObject), typeof(int) });
            MethodInfo initialize = AccessTools.Method(typeof(HeroDeveloper), "InitializeHeroDeveloper",
                Type.EmptyTypes);
            MethodInfo setFocusPoints = AccessTools.PropertySetter(typeof(HeroDeveloper),
                nameof(HeroDeveloper.UnspentFocusPoints));
            MethodInfo setAttributePoints = AccessTools.PropertySetter(typeof(HeroDeveloper),
                nameof(HeroDeveloper.UnspentAttributePoints));
            ConstructorInfo heroItem = AccessTools.Constructor(typeof(CharacterDeveloperHeroItemVM),
                new[] { typeof(Hero), typeof(Action) });
            MethodInfo applyChanges = AccessTools.Method(typeof(CharacterDeveloperHeroItemVM),
                "ApplyChanges", Type.EmptyTypes);
            MethodInfo refreshValues = AccessTools.Method(typeof(CharacterDeveloperHeroItemVM),
                nameof(CharacterDeveloperHeroItemVM.RefreshValues), Type.EmptyTypes);
            if (addSkillXp == null || setFocus == null || setAttribute == null || setSkill == null ||
                initialize == null || heroItem == null || applyChanges == null ||
                setFocusPoints == null || setAttributePoints == null || refreshValues == null ||
                GainRawXpMethod == null || OriginalFocusPointsField == null || OriginalAttributePointsField == null)
            {
                throw new MissingMemberException(
                    "Learning limit hooks changed in this Bannerlord version.");
            }

            harmony.Patch(addSkillXp, prefix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(BeforeSkillXpAdded)));
            harmony.Patch(setFocus,
                prefix: new HarmonyMethod(typeof(LearningLimitSkills), nameof(BeforeFocusSet)),
                postfix: new HarmonyMethod(typeof(LearningLimitSkills), nameof(AfterDeveloperChanged)));
            harmony.Patch(setAttribute, postfix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(AfterHeroChanged)));
            harmony.Patch(setSkill, postfix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(AfterHeroChanged)));
            harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(AfterDeveloperChanged)));
            harmony.Patch(setFocusPoints, prefix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(BeforeUnspentPointsSet)));
            harmony.Patch(setAttributePoints, prefix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(BeforeUnspentPointsSet)));

            // Run before other perk previews and before the VM copies the hero's data.
            var beforeHeroItem = new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(BeforeHeroItemConstructed)) { priority = Priority.First };
            harmony.Patch(heroItem, prefix: beforeHeroItem);
            harmony.Patch(refreshValues, prefix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(BeforeHeroValuesRefreshed)));
            harmony.Patch(applyChanges, postfix: new HarmonyMethod(typeof(LearningLimitSkills),
                nameof(AfterHeroChangesApplied)));
        }

        internal static void SetCampaignReady(bool ready)
        {
            _campaignReady = ready;
            if (!ready)
                PendingHeroes.Clear();
        }

        private static bool CanUpdate(Hero hero)
        {
            // Native adulthood initialization replaces development data. Filling a
            // child here would also bypass the story siblings' default stat setup.
            return _campaignReady && Campaign.Current != null &&
                   ComfortFeatures.LearningLimitSkillsEnabled && hero != null &&
                   !hero.IsDead && !hero.IsChild && hero.HeroDeveloper != null &&
                   (hero == Hero.MainHero ||
                    (Clan.PlayerClan != null && hero.Clan == Clan.PlayerClan));
        }

        private static bool IsUpdating(Hero hero)
        {
            return _updatingHeroes != null && _updatingHeroes.Contains(hero);
        }

        private static bool BeforeSkillXpAdded(HeroDeveloper __instance, SkillObject __0, float __1)
        {
            Hero hero = __instance?.Hero;
            if (__0 == null || __1 <= 0f || !CanUpdate(hero) || IsUpdating(hero))
                return true;

            // This mode replaces training and manual allocations with automatic
            // development. Internal filling keeps the native XP/level pipeline.
            QueueHero(hero);
            return false;
        }

        private static void BeforeFocusSet(HeroDeveloper __instance, ref int __1)
        {
            if (!CanUpdate(__instance?.Hero))
                return;

            // Native perk rewards call AddFocus even when focus is already full.
            // Clamp their SetFocus call too, including while we refresh this hero.
            int maximum = Campaign.Current.Models.CharacterDevelopmentModel.MaxFocusPerSkill;
            __1 = Math.Max(0, Math.Min(__1, maximum));
        }

        private static void AfterDeveloperChanged(HeroDeveloper __instance)
        {
            QueueHero(__instance?.Hero);
        }

        private static void BeforeUnspentPointsSet(HeroDeveloper __instance, ref int __0)
        {
            Hero hero = __instance?.Hero;
            if (__0 == 0 || !CanUpdate(hero))
                return;
            // Level-ups and one-time rewards cannot leave manual point spending
            // pending while every attribute and skill focus is already full.
            __0 = 0;
            QueueHero(hero);
        }

        private static void AfterHeroChanged(Hero __instance)
        {
            QueueHero(__instance);
        }

        private static void BeforeHeroItemConstructed(Hero __0)
        {
            RefreshHero(__0);
        }

        private static void BeforeHeroValuesRefreshed(CharacterDeveloperHeroItemVM __instance)
        {
            Hero hero = __instance?.Hero;
            if (!CanUpdate(hero))
                return;
            RefreshHero(hero);
            UpdateHeroItemPoints(__instance, hero.HeroDeveloper);
        }

        private static void AfterHeroChangesApplied(CharacterDeveloperHeroItemVM __instance)
        {
            // Reconcile native perk consequences after the full UI commit and
            // replace snapshots so cancelling cannot restore stale spare points.
            Hero hero = __instance?.Hero;
            if (!CanUpdate(hero))
                return;
            RefreshHero(hero);
            try
            {
                // Level-ups can grant new unspent points. Replace the VM's old
                // snapshots only now, after all pending allocations were applied.
                UpdateHeroItemPoints(__instance, hero.HeroDeveloper);
                __instance.RefreshValues();
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Learning limit UI refresh failed: " + ex);
            }
        }

        private static void UpdateHeroItemPoints(CharacterDeveloperHeroItemVM item, HeroDeveloper developer)
        {
            OriginalFocusPointsField.SetValue(item, developer.UnspentFocusPoints);
            OriginalAttributePointsField.SetValue(item, developer.UnspentAttributePoints);
            item.UnspentCharacterPoints = developer.UnspentFocusPoints;
            item.UnspentAttributePoints = developer.UnspentAttributePoints;
        }

        internal static void QueueHero(Hero hero)
        {
            if (CanUpdate(hero) && !IsUpdating(hero))
                PendingHeroes.Add(hero);
        }

        internal static void QueuePlayerClan()
        {
            if (!_campaignReady || Campaign.Current == null ||
                !ComfortFeatures.LearningLimitSkillsEnabled || Clan.PlayerClan == null)
                return;

            var heroes = new HashSet<Hero>(Clan.PlayerClan.Heroes.ToArray());
            foreach (Hero companion in Clan.PlayerClan.Companions.ToArray())
                heroes.Add(companion);
            if (Hero.MainHero != null)
                heroes.Add(Hero.MainHero);
            foreach (Hero hero in heroes)
                QueueHero(hero);
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

        internal static void RefreshHero(Hero hero)
        {
            if (!CanUpdate(hero))
                return;
            if (_updatingHeroes == null)
                _updatingHeroes = new HashSet<Hero>();
            if (!_updatingHeroes.Add(hero))
                return;

            bool continueNextTick = false;
            try
            {
                for (int pass = 0; pass < MaxRefreshPasses; pass++)
                {
                    FillAttributesAndFocus(hero);
                    // Some qualifying perks grant permanent attribute/focus points.
                    // Let those native consequences settle and recompute the caps.
                    BothPerks.RefreshHero(hero);
                    FillAttributesAndFocus(hero);
                    bool gainedSkill = false;
                    foreach (SkillObject skill in Skills.All.ToArray())
                        gainedSkill |= FillSkill(hero, skill);
                    BothPerks.RefreshHero(hero);
                    if (!gainedSkill)
                        break;
                    if (pass == MaxRefreshPasses - 1)
                        continueNextTick = true;
                }
                hero.HeroDeveloper.ClearUnspentPoints();
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Learning limit refresh failed: " + ex);
            }
            finally
            {
                // Native skill jumps can grant multiple character levels. The
                // setters also prevent fresh points, and this clears saved ones.
                hero.HeroDeveloper.ClearUnspentPoints();
                _updatingHeroes.Remove(hero);
            }

            if (continueNextTick)
                QueueHero(hero);
        }

        private static void FillAttributesAndFocus(Hero hero)
        {
            HeroDeveloper developer = hero.HeroDeveloper;
            CharacterDevelopmentModel model = Campaign.Current.Models.CharacterDevelopmentModel;
            foreach (CharacterAttribute attribute in Attributes.All.ToArray())
            {
                int current = hero.GetAttributeValue(attribute);
                if (current != model.MaxAttribute)
                    developer.AddAttribute(attribute, model.MaxAttribute - current, false);
            }
            foreach (SkillObject skill in Skills.All.ToArray())
            {
                int current = developer.GetFocus(skill);
                if (current != model.MaxFocusPerSkill)
                    developer.AddFocus(skill, model.MaxFocusPerSkill - current, false);
            }
            // Repair old over-maximum values as well as filling missing points.
            developer.ClearUnspentPoints();
        }

        internal static int FindGrowthLimit(CharacterDevelopmentModel model,
            IReadOnlyPropertyOwner<CharacterAttribute> attributes, int focus, SkillObject skill)
        {
            // Green learning limit is only where the over-limit penalty begins.
            // Use the active model's actual zero learning rate, including skills
            // with multiple governing attributes.
            if (GetLearningRate(model, attributes, focus, 0, skill) <= 0f)
                return 0;
            if (GetLearningRate(model, attributes, focus, MaxNativeSkillLevel, skill) > 0f)
                return MaxNativeSkillLevel;

            // Native learning rate is constant, then decreases to zero. Binary
            // search avoids evaluating hundreds of rates per skill/UI refresh.
            int positiveLevel = 0;
            int zeroLevel = MaxNativeSkillLevel;
            while (zeroLevel - positiveLevel > 1)
            {
                int middle = positiveLevel + (zeroLevel - positiveLevel) / 2;
                if (GetLearningRate(model, attributes, focus, middle, skill) <= 0f)
                    zeroLevel = middle;
                else
                    positiveLevel = middle;
            }
            return zeroLevel;
        }

        private static float GetLearningRate(CharacterDevelopmentModel model,
            IReadOnlyPropertyOwner<CharacterAttribute> attributes, int focus, int level, SkillObject skill)
        {
            float rate = model.CalculateLearningRate(attributes, focus, level, skill, false).ResultNumber;
            if (float.IsNaN(rate) || float.IsInfinity(rate))
                throw new InvalidOperationException("The character model returned an invalid learning rate.");
            return rate;
        }

        private static bool FillSkill(Hero hero, SkillObject skill)
        {
            HeroDeveloper developer = hero.HeroDeveloper;
            var model = Campaign.Current.Models.CharacterDevelopmentModel;
            int target = FindGrowthLimit(model, hero.CharacterAttributes, developer.GetFocus(skill), skill);
            int previousSkill = hero.GetSkillValue(skill);
            if (target <= 0)
                return false;

            int targetXp = model.GetXpRequiredForSkillLevel(target);
            if (previousSkill > target)
            {
                // Native XP gains cannot lower a skill. Reconcile the level and
                // its cumulative XP together after repairing excess focus.
                // Character level/total XP and already granted perks remain.
                developer.SetInitialSkillLevel(skill, target);
                return true;
            }
            double xpNeeded = targetXp - (double)developer.GetSkillXp(skill);
            if (target == previousSkill && xpNeeded <= 0d)
                return false;

            float focusFactor = developer.GetFocusFactor(skill);
            float xpMultiplier = Campaign.Current.Models.GenericXpModel.GetXpMultiplier(hero);
            if (float.IsNaN(xpMultiplier) || float.IsInfinity(xpMultiplier) || xpMultiplier <= 0f)
                return false;
            bool useFocusFactor = !float.IsNaN(focusFactor) && !float.IsInfinity(focusFactor) && focusFactor > 0f;
            float effectiveMultiplier = (useFocusFactor ? focusFactor : 1f) * xpMultiplier;
            if (float.IsNaN(effectiveMultiplier) || float.IsInfinity(effectiveMultiplier) ||
                effectiveMultiplier <= 0f)
                return false;

            // The model's XP threshold is cumulative, matching GetSkillXp. Divide
            // by native multipliers so the skill reaches its cap and raw character
            // XP, level increases and unspent points are awarded at the same time.
            double nextThresholdGap = model.GetXpRequiredForSkillLevel(target + 1) - (double)targetXp;
            double epsilon = Math.Max(1d, Math.Abs((double)targetXp) * 0.000001d);
            if (nextThresholdGap > 0d)
                epsilon = Math.Min(epsilon, nextThresholdGap * 0.25d);
            float rawXp = (float)((Math.Max(0d, xpNeeded) + epsilon) / effectiveMultiplier);
            if (float.IsNaN(rawXp) || float.IsInfinity(rawXp) || rawXp <= 0f)
                return false;
            developer.AddSkillXp(skill, rawXp, useFocusFactor, false);
            if (!useFocusFactor)
            {
                // An already-capped skill can have stale XP from another mod or
                // a saved direct level grant. Bypass the zero focus multiplier,
                // then account for character XP through its native level path.
                GainRawXpMethod.Invoke(developer, new object[] { rawXp, false });
            }
            return hero.GetSkillValue(skill) > previousSkill;
        }
    }

    internal sealed class LearningLimitSkillsCampaignBehavior : CampaignBehaviorBase
    {
        private bool _ready;
        private bool _wasEnabled;

        internal LearningLimitSkillsCampaignBehavior()
        {
            LearningLimitSkills.SetCampaignReady(false);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyClanTick);
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Skill XP, hero levels and points are saved by the native developer.
        }

        private void StartCampaign()
        {
            _ready = true;
            LearningLimitSkills.SetCampaignReady(true);
            _wasEnabled = ComfortFeatures.LearningLimitSkillsEnabled;
            // Initialization listeners may still replace stats later in this event.
            LearningLimitSkills.QueuePlayerClan();
        }

        private void OnTick(float dt)
        {
            if (!_ready)
                return;
            bool enabled = ComfortFeatures.LearningLimitSkillsEnabled;
            if (enabled && !_wasEnabled)
                LearningLimitSkills.QueuePlayerClan();
            _wasEnabled = enabled;
            LearningLimitSkills.RefreshPendingHeroes();
        }

        private void OnDailyClanTick(Clan clan)
        {
            if (_ready && clan == Clan.PlayerClan)
                LearningLimitSkills.QueuePlayerClan();
        }

        private void OnHeroChangedClan(Hero hero, Clan previousClan)
        {
            if (_ready)
                LearningLimitSkills.QueueHero(hero);
        }

        private void OnHeroComesOfAge(Hero hero)
        {
            if (_ready)
                LearningLimitSkills.QueueHero(hero);
        }
    }
}
