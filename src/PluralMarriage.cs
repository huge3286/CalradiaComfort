using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class PluralMarriage
    {
        private static readonly FieldInfo SpouseField = AccessTools.Field(typeof(Hero), "_spouse");
        private static readonly FieldInfo ExSpousesField = AccessTools.Field(typeof(Hero), "_exSpouses");
        private static readonly MethodInfo SetRomanticStateMethod = AccessTools.Method(typeof(Romance),
            "SetRomanticState", new[] { typeof(Hero), typeof(Hero), typeof(Romance.RomanceLevelEnum) });
        [ThreadStatic] private static MarriageScope _marriage;
        [ThreadStatic] private static PregnancyScope _pregnancy;

        internal sealed class MarriageScope
        {
            internal Hero Owner;
            internal Hero Partner;
            internal Hero PreviousOwnerSpouse;
            internal Hero PreviousPartnerSpouse;
            internal MarriageScope Previous;
        }

        private sealed class PregnancyScope
        {
            internal Hero Mother;
            internal Hero Father;
            internal PregnancyScope Previous;
        }

        internal static void InitializePatches(Harmony harmony)
        {
            if (SpouseField == null || ExSpousesField == null || SetRomanticStateMethod == null)
                throw new MissingFieldException("Native spouse storage changed.");
            harmony.Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsSuitableForMarriage"),
                transpiler: Hook(nameof(ReplaceMarriageSpouseGate)));
            foreach (string name in new[] {
                "conversation_player_eligible_for_marriage_with_conversation_hero_on_condition",
                "conversation_player_eligible_for_marriage_with_hero_rltv_on_condition" })
                harmony.Patch(AccessTools.Method(typeof(RomanceCampaignBehavior), name),
                    transpiler: Hook(nameof(ReplaceMarriageSpouseGate)));
            harmony.Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage"),
                postfix: Hook(nameof(AfterCoupleSuitable)));
            harmony.Patch(AccessTools.Method(typeof(MarriageAction), "ApplyInternal"),
                prefix: Hook(nameof(BeforeMarriage)), finalizer: Hook(nameof(FinishMarriage)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Hero), nameof(Hero.Spouse)),
                prefix: Hook(nameof(BeforeSetSpouse)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Spouse)),
                postfix: Hook(nameof(AfterGetSpouse)));
            harmony.Patch(AccessTools.Method(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit"),
                prefix: Hook(nameof(BeforeSpouseVisit)), finalizer: Hook(nameof(FinishSpouseVisit)));
        }

        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(PluralMarriage), name);

        private static IEnumerable<CodeInstruction> ReplaceMarriageSpouseGate(IEnumerable<CodeInstruction> source)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Spouse));
            MethodInfo replacement = AccessTools.Method(typeof(PluralMarriage), nameof(GetBlockingSpouse));
            int replaced = 0;
            foreach (CodeInstruction instruction in source)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
                throw new MissingMethodException("Expected exactly one native marriage spouse check.");
        }

        // Change only the main hero's already-married check. Native availability,
        // courtship, clan, age, relatedness, consent and marriage events still run.
        private static Hero GetBlockingSpouse(Hero hero)
        {
            return Campaign.Current != null && ComfortFeatures.PluralMarriageEnabled &&
                PluralMarriageCampaignBehavior.Current != null &&
                hero == Hero.MainHero ? null : hero.Spouse;
        }

        private static void AfterCoupleSuitable(Hero __0, Hero __1, ref bool __result)
        {
            if (!__result || Campaign.Current == null) return;
            Hero owner = __0 == Hero.MainHero ? __0 : (__1 == Hero.MainHero ? __1 : null);
            if (owner == null || !ComfortFeatures.PluralMarriageEnabled) return;
            Hero partner = owner == __0 ? __1 : __0;
            if (partner == null || owner.Age < 18f || partner.Age < 18f ||
                owner.IsPrisoner || partner.IsPrisoner || AreSpouses(owner, partner) ||
                RawSpouse(partner) != null)
                __result = false;
        }

        private static void BeforeMarriage(Hero __0, Hero __1, out MarriageScope __state)
        {
            __state = null;
            if (Campaign.Current == null || !ComfortFeatures.PluralMarriageEnabled ||
                PluralMarriageCampaignBehavior.Current == null) return;
            Hero owner = __0 == Hero.MainHero ? __0 : (__1 == Hero.MainHero ? __1 : null);
            Hero partner = owner == __0 ? __1 : __0;
            if (owner == null || partner == null ||
                !Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(owner, partner)) return;
            Hero previous = RawSpouse(owner);
            if (previous != null)
                PluralMarriageCampaignBehavior.Current.Record(owner, previous);
            __state = new MarriageScope { Owner = owner, Partner = partner,
                PreviousOwnerSpouse = previous, PreviousPartnerSpouse = RawSpouse(partner), Previous = _marriage };
            _marriage = __state;
        }

        private static Exception FinishMarriage(Exception __exception, MarriageScope __state)
        {
            if (__state == null) return __exception;
            try
            {
                // The native action handles costs, clan transfer, parties and all
                // marriage events. Record only relationships it actually linked.
                bool linked = RawSpouse(__state.Owner) == __state.Partner &&
                    RawSpouse(__state.Partner) == __state.Owner &&
                    __state.Owner.Clan != null && __state.Owner.Clan == __state.Partner.Clan;
                if (linked)
                    PluralMarriageCampaignBehavior.Current?.Record(__state.Owner, __state.Partner);
                else
                {
                    // A failure before the native clan transfer must not leave
                    // a one-sided relationship on the incoming hero.
                    if (RawSpouse(__state.Partner) == __state.Owner)
                        SetRawSpouse(__state.Partner, __state.PreviousPartnerSpouse);
                    if (RawSpouse(__state.Owner) == __state.Partner)
                        SetRawSpouse(__state.Owner, __state.PreviousOwnerSpouse);
                }
                PluralMarriageCampaignBehavior.Current?.RepairRelationships();
            }
            finally { _marriage = __state.Previous; }
            return __exception;
        }

        private static bool BeforeSetSpouse(Hero __instance, Hero __0)
        {
            if (_marriage != null &&
                ((__instance == _marriage.Owner && __0 == _marriage.Partner) ||
                 (__instance == _marriage.Partner && __0 == _marriage.Owner)))
            {
                SetRawSpouse(__instance, __0);
                return false;
            }
            // Also protects existing marriages while the creation switch is off.
            // A secondary spouse's death must not divorce the primary spouse.
            if (__0 == null && Campaign.Current != null &&
                PluralMarriageCampaignBehavior.Current?.Detach(__instance) == true)
                return false;
            return true;
        }

        private static void AfterGetSpouse(Hero __instance, ref Hero __result)
        {
            if (_pregnancy != null && __instance == _pregnancy.Mother)
                __result = _pregnancy.Father;
        }

        private static bool BeforeSpouseVisit(Hero __0, out PregnancyScope __state)
        {
            __state = null;
            if (Campaign.Current == null || !IsManagedHero(__0)) return true;
            if (__0 == null || __0.IsPrisoner || __0.Age < 18f) return false;
            // For a female main hero, select one free, nearby husband per native
            // daily check. This context also supplies the recorded baby's father.
            List<Hero> candidates = GetSpouses(__0).Where(h => !h.IsFemale &&
                h.Age >= 18f && !h.IsPrisoner && AreNearby(__0, h)).ToList();
            if (candidates.Count == 0) return false;
            Hero father = candidates.Count == 1 ? candidates[0] : candidates[MBRandom.RandomInt(candidates.Count)];
            __state = new PregnancyScope { Mother = __0, Father = father, Previous = _pregnancy };
            _pregnancy = __state;
            return true;
        }

        private static Exception FinishSpouseVisit(Exception __exception, PregnancyScope __state)
        {
            if (__state != null) _pregnancy = __state.Previous;
            return __exception;
        }

        internal static void MakePregnantWith(Hero mother, Hero father)
        {
            // ChildConceived synchronously reads Mother.Spouse to store the
            // biological father. Supply the chosen husband without changing
            // the primary spouse or any saved marriage links.
            PregnancyScope previous = _pregnancy;
            _pregnancy = new PregnancyScope { Mother = mother, Father = father, Previous = previous };
            try { MakePregnantAction.Apply(mother); }
            finally { _pregnancy = previous; }
        }

        private static bool AreNearby(Hero first, Hero second)
        {
            return (first.PartyBelongedTo != null && first.PartyBelongedTo == second.PartyBelongedTo) ||
                (first.CurrentSettlement != null && first.CurrentSettlement == second.CurrentSettlement);
        }

        internal static Hero RawSpouse(Hero hero) => hero == null ? null : (Hero)SpouseField.GetValue(hero);
        internal static void SetRawSpouse(Hero hero, Hero spouse) => SpouseField.SetValue(hero, spouse);
        internal static void RestoreMarriageState(Hero owner, Hero partner)
        {
            SetRomanticStateMethod.Invoke(null, new object[] { owner, partner, Romance.RomanceLevelEnum.Marriage });
        }
        internal static void AddFormerSpouse(Hero hero, Hero former)
        {
            if (hero == null || former == null) return;
            var list = (MBList<Hero>)ExSpousesField.GetValue(hero);
            if (list != null && !list.Contains(former)) list.Add(former);
        }

        internal static bool IsManagedHero(Hero hero) => hero != null &&
            PluralMarriageCampaignBehavior.Current?.IsManaged(hero) == true;

        internal static IEnumerable<Hero> GetSpouses(Hero owner)
        {
            if (owner == null || !owner.IsAlive) return Enumerable.Empty<Hero>();
            var spouses = new List<Hero>();
            Hero single = RawSpouse(owner);
            if (single != null && single.IsAlive) spouses.Add(single);
            PluralMarriageCampaignBehavior.Current?.AppendSpouses(owner, spouses);
            return spouses;
        }

        internal static bool AreSpouses(Hero first, Hero second)
        {
            return first != null && second != null && first != second && first.IsAlive && second.IsAlive &&
                (RawSpouse(first) == second || RawSpouse(second) == first ||
                 PluralMarriageCampaignBehavior.Current?.Contains(first, second) == true);
        }
    }

    internal sealed class PluralMarriageCampaignBehavior : CampaignBehaviorBase
    {
        // Parallel native Hero lists avoid new saveable types. Saving owners
        // explicitly prevents relationships being assigned to a player successor.
        private List<Hero> _owners = new List<Hero>();
        private List<Hero> _partners = new List<Hero>();
        internal static PluralMarriageCampaignBehavior Current { get; private set; }

        public PluralMarriageCampaignBehavior() { Current = this; }
        internal static void ClearCurrent() { Current = null; }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, RepairRelationships);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("CalradiaComfort_MarriageOwners", ref _owners);
            dataStore.SyncData("CalradiaComfort_MarriagePartners", ref _partners);
            _owners = _owners ?? new List<Hero>();
            _partners = _partners ?? new List<Hero>();
            // Loading callbacks run after native Hero.AfterLoad.
            Current = this;
        }

        private void OnSessionLaunched(CampaignGameStarter starter) { RepairRelationships(); }
        internal bool IsManaged(Hero hero) => _owners.Contains(hero) || _partners.Contains(hero);
        internal bool Contains(Hero first, Hero second)
        {
            for (int i = 0; i < Math.Min(_owners.Count, _partners.Count); i++)
                if ((_owners[i] == first && _partners[i] == second) ||
                    (_owners[i] == second && _partners[i] == first)) return true;
            return false;
        }

        internal void AppendSpouses(Hero hero, List<Hero> result)
        {
            for (int i = 0; i < Math.Min(_owners.Count, _partners.Count); i++)
            {
                Hero other = _owners[i] == hero ? _partners[i] : (_partners[i] == hero ? _owners[i] : null);
                if (other != null && other.IsAlive && !result.Contains(other)) result.Add(other);
            }
        }

        internal void Record(Hero owner, Hero partner)
        {
            if (owner == null || partner == null || owner == partner || Contains(owner, partner) ||
                !owner.IsAlive || !partner.IsAlive || owner.Age < 18f || partner.Age < 18f ||
                owner.IsFemale == partner.IsFemale || owner.Clan == null || owner.Clan != partner.Clan ||
                PluralMarriage.RawSpouse(partner) != owner) return;
            _owners.Add(owner);
            _partners.Add(partner);
        }

        internal bool Detach(Hero hero)
        {
            if (hero == null || !IsManaged(hero)) return false;
            bool isOwner = _owners.Contains(hero);
            Hero selected = PluralMarriage.RawSpouse(hero);
            for (int i = Math.Min(_owners.Count, _partners.Count) - 1; i >= 0; i--)
            {
                Hero owner = _owners[i]; Hero partner = _partners[i];
                bool remove = !isOwner ? partner == hero : owner == hero &&
                    (!hero.IsAlive || partner == selected);
                if (!remove) continue;
                if (PluralMarriage.RawSpouse(partner) == owner) PluralMarriage.SetRawSpouse(partner, null);
                if (PluralMarriage.RawSpouse(owner) == partner) PluralMarriage.SetRawSpouse(owner, null);
                PluralMarriage.AddFormerSpouse(owner, partner);
                PluralMarriage.AddFormerSpouse(partner, owner);
                _owners.RemoveAt(i); _partners.RemoveAt(i);
            }
            if (!hero.IsAlive) PluralMarriage.SetRawSpouse(hero, null);
            RepairRelationships();
            return true;
        }

        internal void RepairRelationships()
        {
            int common = Math.Min(_owners.Count, _partners.Count);
            if (_owners.Count > common) _owners.RemoveRange(common, _owners.Count - common);
            if (_partners.Count > common) _partners.RemoveRange(common, _partners.Count - common);
            var ownersToRepair = new HashSet<Hero>(_owners.Where(h => h != null));
            for (int i = common - 1; i >= 0; i--)
            {
                Hero owner = _owners[i]; Hero partner = _partners[i];
                Hero partnerSpouse = PluralMarriage.RawSpouse(partner);
                if (owner == null || partner == null || !owner.IsAlive || !partner.IsAlive ||
                    owner.Age < 18f || partner.Age < 18f || owner.Clan == null || owner.Clan != partner.Clan ||
                    (partnerSpouse != null && partnerSpouse != owner))
                {
                    if (partner != null && partnerSpouse == owner) PluralMarriage.SetRawSpouse(partner, null);
                    if (owner != null && PluralMarriage.RawSpouse(owner) == partner) PluralMarriage.SetRawSpouse(owner, null);
                    PluralMarriage.AddFormerSpouse(owner, partner);
                    PluralMarriage.AddFormerSpouse(partner, owner);
                    _owners.RemoveAt(i); _partners.RemoveAt(i);
                    continue;
                }
                PluralMarriage.SetRawSpouse(partner, owner);
                PluralMarriage.RestoreMarriageState(owner, partner);
            }
            foreach (Hero owner in ownersToRepair)
            {
                List<Hero> living = new List<Hero>();
                for (int i = 0; i < _owners.Count; i++)
                    if (_owners[i] == owner) living.Add(_partners[i]);
                if (living.Count == 0)
                {
                    if (!owner.IsAlive) PluralMarriage.SetRawSpouse(owner, null);
                }
                else if (!living.Contains(PluralMarriage.RawSpouse(owner)))
                    PluralMarriage.SetRawSpouse(owner, living[living.Count - 1]);
            }
        }
    }
}
