using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace StorySkip
{
    internal static class ClanPartyStrategy
    {
        [ThreadStatic] private static MobileParty _militaryEvaluation;
        internal static readonly FieldInfo Scores = AccessTools.Field(typeof(PartyThinkParams), "_aiBehaviorScores");
        private static readonly FieldInfo AiOwner = AccessTools.Field(typeof(MobilePartyAi), "_mobileParty");
        internal static void InitializePatches(Harmony harmony)
        {
            if (Scores == null || AiOwner == null) throw new MissingFieldException("Native party AI layout changed.");
            harmony.Patch(AccessTools.Method(typeof(AiPartyThinkBehavior), "PartyHourlyAiTick"),
                prefix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(BeforeThink)),
                postfix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(AfterThink)));
            harmony.Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "AiHourlyTick"),
                postfix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(AfterScores)));
            harmony.Patch(AccessTools.Method(typeof(AiMilitaryBehavior), "AiHourlyTick"),
                prefix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(BeforeMilitary)),
                finalizer: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(AfterMilitary)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(MobileParty), "Objective"),
                postfix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(ObjectiveForEvaluation)));
            harmony.Patch(AccessTools.Method(typeof(MobileParty), "SetMoveRaidSettlement"),
                prefix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(BlockRaid)));
            harmony.Patch(AccessTools.Method(typeof(DefaultArmyManagementCalculationModel), "CanLordCreateArmy"),
                postfix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(AllowFamilyGathering)));
            harmony.Patch(AccessTools.Method(typeof(MobilePartyAi), "GetGoAroundPartyBehavior"),
                prefix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(DirectPursuit)));
            harmony.Patch(AccessTools.Method(typeof(DefaultMobilePartyAIModel), "GetBestInitiativeBehavior"),
                postfix: new HarmonyMethod(typeof(ClanPartyStrategy), nameof(KeepMissionFocus)));
            ClanStrategyDiagnostics.InitializePatches(harmony);
        }
        private static void BeforeThink(MobileParty __0)
        {
            var current = ClanPartyStrategyBehavior.Current;
            if (current == null || !current.Ready) return;
            current.SafeRefresh(false);
            if (current.Enabled && CanControl(__0)) __0.Ai.RethinkAtNextHourlyTick = true;
        }
        private static void AfterScores(MobileParty __0, PartyThinkParams __1)
        {
            var current = ClanPartyStrategyBehavior.Current;
            if (current?.Enabled != true) return;
            if (!CanControl(__0)) { ClanStrategyDiagnostics.EnemyScores(__0, __1); return; }
            try
            {
                string native = ClanStrategyDiagnostics.NativeScores(__1);
                current.Decide(__0, __1);
                current.TraceDecision(__0, __1, native);
            }
            catch (Exception ex) { Debug.Print("[CalradiaComfort] Party strategy scoring: " + ex); }
        }
        private static void AfterThink(MobileParty __0)
        {
            if (ClanPartyStrategyBehavior.Current?.Enabled == true && CanControl(__0))
                ClanStrategyDiagnostics.Applied(__0);
        }
        private static void BeforeMilitary(MobileParty __0, out MobileParty __state)
        {
            __state = _militaryEvaluation;
            _militaryEvaluation = ClanPartyStrategyBehavior.Current?.Enabled == true && CanControl(__0) ? __0 : null;
        }
        private static Exception AfterMilitary(Exception __exception, MobileParty __state)
        { _militaryEvaluation = __state; return __exception; }
        private static void ObjectiveForEvaluation(MobileParty __instance, ref MobileParty.PartyObjective __result)
        {
            // Evaluate siege, raid and defence independently of the coarse vanilla aggressive/defensive
            // toggle. The final policy removes raids. Never change the saved objective or UI setting.
            if (ReferenceEquals(__instance, _militaryEvaluation)) __result = MobileParty.PartyObjective.Neutral;
        }
        private static bool BlockRaid(MobileParty __instance) =>
            ClanPartyStrategyBehavior.Current?.Enabled != true || !CanControl(__instance);
        private static bool DirectPursuit(MobilePartyAi __instance, MobileParty __0,
            ref AiBehavior __1, ref CampaignVec2 __2)
        {
            var current = ClanPartyStrategyBehavior.Current;
            if (current?.Enabled != true) return true;
            try
            {
                var party = (MobileParty)AiOwner.GetValue(__instance);
                if (!current.ShouldDirectPursue(party, __0)) return true;
                // Keep GoAroundParty as the supported strategic task (and army objective), but
                // skip its native standoff position only for an authorized combat interception.
                // EngageParty is valid here in the short-term movement layer.
                __1 = AiBehavior.EngageParty;
                __2 = __0.Position;
                ClanStrategyDiagnostics.Pursuit(party, __0);
                return false;
            }
            catch (Exception ex) { Debug.Print("[CalradiaComfort] Direct pursuit: " + ex); return true; }
        }
        private static void AllowFamilyGathering(MobileParty __0, ref MBList<MobileParty> __1, ref bool __result)
        {
            var current = ClanPartyStrategyBehavior.Current;
            // This override is active only inside the managed party's native military evaluation.
            // Vanilla rejects clan members while their clan leader has a party (normally the player).
            // Recover a family-only invitation list, retaining native army construction and gathering.
            if (__result || current?.Enabled != true || !ReferenceEquals(__0, _militaryEvaluation)) return;
            var members = current.FamilyArmyMembers(__0);
            if (members.Count == 0) return;
            __1 = members;
            __result = true;
        }
        private static void KeepMissionFocus(MobileParty __0, ref AiBehavior __1,
            ref MobileParty __2, ref float __3, ref Vec2 __4)
        {
            var current = ClanPartyStrategyBehavior.Current;
            if (current?.Enabled != true || __1 != AiBehavior.EngageParty || __2 == null) return;
            try
            {
                // Rescue/protection must reach their incident rather than chase unrelated enemies.
                // Native fleeing remains untouched; siege armies may still reinforce nearby battles.
                if (!current.ShouldIgnoreOpportunity(__0, __2)) return;
                ClanStrategyDiagnostics.MissionFocus(__0, __2);
                __1 = AiBehavior.None; __2 = null; __3 = 0f; __4 = Vec2.Zero;
            }
            catch (Exception ex) { Debug.Print("[CalradiaComfort] Mission focus: " + ex); }
        }

        internal static bool CanControl(MobileParty party)
        {
            if (Campaign.Current == null || Clan.PlayerClan == null || party == null ||
                party == MobileParty.MainParty || !party.IsActive || !party.IsLordParty ||
                party.ActualClan != Clan.PlayerClan || party.IsDisbanding || party.IsCurrentlyUsedByAQuest ||
                party.LeaderHero == null || !party.LeaderHero.IsAlive || party.LeaderHero.IsPrisoner ||
                party.Ai == null || party.Ai.IsDisabled || party.Ai.DoNotMakeNewDecisions || party.AttachedTo != null)
                return false;
            return party.Army == null || (party.Army.LeaderParty == party &&
                !party.Army.Parties.Contains(MobileParty.MainParty));
        }
        internal static bool Committed(MobileParty party) => party.MapEvent != null || party.BesiegerCamp != null ||
            (party.CurrentSettlement?.IsUnderSiege ?? false);
        internal static float Strength(MobileParty party) => Math.Max(0f, party.GetTotalLandStrengthWithFollowers(true));
        internal static bool SupplyReady(MobileParty party)
        {
            var models = Campaign.Current.Models;
            float size = models.ArmyManagementCalculationModel.AIMobilePartySizeRatioToCallToArmy;
            float food = Math.Max(3f, models.MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege);
            return party.PartySizeRatio >= size && party.GetNumDaysForFoodToLast() > food;
        }
        internal static bool SiegeSupplied(MobileParty party) => party.GetNumDaysForFoodToLast() >
            Campaign.Current.Models.MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege;
        internal static float EnemyRoute(MobileParty party, MobileParty enemy, out AIBehaviorData data)
        {
            AiHelper.GetBestNavigationTypeAndDistanceOfMobilePartyForMobileParty(party, enemy, out var navigation, out float distance);
            data = new AIBehaviorData(enemy, AiBehavior.GoAroundParty, navigation, false, false, false);
            return navigation == MobileParty.NavigationType.None || float.IsNaN(distance) || float.IsInfinity(distance)
                ? float.MaxValue : distance;
        }
        internal static float EnemyDistance(MobileParty party, MobileParty enemy, MobileParty.NavigationType navigation) =>
            Campaign.Current.Models.MapDistanceModel.GetDistance(party, enemy, navigation, out _);
        internal static float Distance(MobileParty party, Settlement target, out AIBehaviorData data, AiBehavior action)
        {
            AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(party, target, false,
                out var navigation, out float distance, out bool fromPort);
            data = new AIBehaviorData(target, action, navigation, false, false, fromPort);
            return navigation == MobileParty.NavigationType.None || float.IsNaN(distance) || float.IsInfinity(distance)
                ? float.MaxValue : distance;
        }
        internal static void Pin(PartyThinkParams think, AIBehaviorData data)
        {
            // Strategic thinking handles GoAroundParty; EngageParty belongs to short-term AI.
            // The latter is ignored by native command dispatch and can disband an army leader.
            if (data.AiBehavior == AiBehavior.EngageParty) data.AiBehavior = AiBehavior.GoAroundParty;
            ((MBList<ValueTuple<AIBehaviorData, float>>)Scores.GetValue(think)).Clear();
            var score = (data, 100f);
            think.AddBehaviorScore(in score);
            think.CurrentObjectiveValue = 0f;
            // Native strategic dispatch, navigation, encounters and siege assault rules remain.
            // Authorized combat pursuits replace GoAroundParty's short-term standoff position.
        }
    }

    internal sealed class ClanPartyStrategyBehavior : CampaignBehaviorBase
    {
        internal enum Kind { Rescue, SupportSiege, Attack, Refit, Patrol, Skirmish }
        internal sealed class Threat
        {
            internal Settlement Site;
            internal MobileParty Relief;
            internal float Demand;
            internal bool IsSupport => Relief != null;
        }
        internal sealed class Order
        {
            internal Kind Kind;
            internal Settlement Site;
            internal AIBehaviorData Data;
            internal double Started;
            internal double Arrived = -1;
            internal bool BeganSiege;
            internal double ClearSince = -1;
        }
        internal static ClanPartyStrategyBehavior Current { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Enabled => Ready && ComfortFeatures.ClanPartyStrategyEnabled && Campaign.Current != null;
        private readonly Dictionary<MobileParty, Order> _orders = new Dictionary<MobileParty, Order>();
        private readonly HashSet<MobileParty> _reserve = new HashSet<MobileParty>();
        private readonly Dictionary<MobileParty, (Settlement Site, double Until)> _recentRefits = new Dictionary<MobileParty, (Settlement, double)>();
        private List<MobileParty> _parties = new List<MobileParty>();
        private string _savedOrders = "";
        private int _lastHour = -1;
        private bool _refreshing;
        public ClanPartyStrategyBehavior() { Current = this; }
        internal static void ClearCurrent() { Current = null; ClanStrategyDiagnostics.End(); }
        private static double Hours => CampaignTime.Now.ToHours;
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHour);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillage);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiege);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnBattleEnd);
        }
        public override void SyncData(IDataStore store)
        {
            if (Ready) _savedOrders = SaveOrders();
            store.SyncData("comfort_clan_party_strategy_orders", ref _savedOrders);
        }
        private void OnSession(CampaignGameStarter starter)
        { Ready = true; LoadOrders(); ClanStrategyDiagnostics.Begin(); RepairLoadedPatrols(); SafeRefresh(true); }
        internal void RepairLoadedPatrols()
        {
            // An old saved point patrol can tick (and fault inside War Sails) before the next AI hour.
            // Reissue only our recorded family patrols with their saved settlement immediately on load.
            foreach (var party in CollectParties())
            {
                if (party.DefaultBehavior != AiBehavior.PatrolAroundPoint || party.TargetSettlement != null ||
                    ClanPartyStrategy.Committed(party) || !_orders.TryGetValue(party, out var order) ||
                    order.Kind != Kind.Patrol || order.Site == null) continue;
                try
                {
                    SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, order.Site, order.Data.NavigationType,
                        order.Data.IsFromPort, party.IsCurrentlyAtSea && order.Site.HasPort);
                    ClanStrategyDiagnostics.Applied(party);
                }
                catch (Exception ex) { Debug.Print("[CalradiaComfort] Saved patrol repair: " + ex); }
            }
        }
        private void OnHour() => SafeRefresh(true);
        private void OnVillage(Village village, Village.VillageStates oldState, Village.VillageStates newState, MobileParty raider)
        {
            ClanStrategyDiagnostics.VillageChanged(village, oldState, newState, raider);
            SafeRefresh(true);
        }
        private void OnSiege(TaleWorlds.CampaignSystem.Siege.SiegeEvent siege) => SafeRefresh(true);
        private void OnBattleEnd(MapEvent mapEvent) => SafeRefresh(true);
        internal void SafeRefresh(bool force)
        {
            if (!Ready || _refreshing) return;
            if (!Enabled) { _orders.Clear(); _reserve.Clear(); _recentRefits.Clear(); _lastHour = -1; return; }
            int hour = (int)Math.Floor(Hours);
            if (!force && hour == _lastHour) return;
            _refreshing = true;
            try { Refresh(); _lastHour = hour; }
            catch (Exception ex) { Debug.Print("[CalradiaComfort] Party strategy dispatch: " + ex); }
            finally { _refreshing = false; }
        }
        internal List<MobileParty> CollectParties() => Clan.PlayerClan.WarPartyComponents.Select(c => c.MobileParty)
            .Where(ClanPartyStrategy.CanControl).Distinct().OrderBy(p => p.StringId, StringComparer.Ordinal).ToList();
        internal List<Threat> CollectThreats()
        {
            var faction = Clan.PlayerClan.MapFaction;
            var result = new List<Threat>();
            foreach (var site in Settlement.All)
            {
                if (site.MapFaction == faction && IsUnderAttack(site)) result.Add(new Threat { Site = site });
                // A friendly siege on enemy land is a separate protection task. DefendSettlement
                // would target the friendly besieger there, so use native engagement of its relief enemy.
                if (!site.IsFortification || site.SiegeEvent?.BesiegerCamp?.MapFaction != faction) continue;
                foreach (var enemy in MobileParty.AllLordParties)
                {
                    if (!enemy.IsActive || enemy.MapFaction == null || !enemy.MapFaction.IsAtWarWith(faction) ||
                        enemy.CurrentSettlement == site ||
                        (enemy.Army != null && enemy.Army.LeaderParty != enemy) || enemy.AttachedTo != null) continue;
                    bool targeting = (enemy.DefaultBehavior == AiBehavior.DefendSettlement && enemy.TargetSettlement == site) ||
                        (enemy.TargetParty?.BesiegerCamp == site.SiegeEvent.BesiegerCamp) ||
                        (enemy.MapEvent != null && enemy.MapEvent == site.SiegeEvent.BesiegerCamp.LeaderParty.MapEvent);
                    if (targeting && ReliefWithinReach(enemy, site)) result.Add(new Threat { Site = site, Relief = enemy });
                }
            }
            return result.GroupBy(t => t.Site).SelectMany(group =>
            {
                var relief = group.Where(t => t.IsSupport).OrderByDescending(t => ClanPartyStrategy.Strength(t.Relief)).ToList();
                if (relief.Count > 0) relief[0].Demand = relief.Sum(t => ClanPartyStrategy.Strength(t.Relief)) * 1.1f;
                return group.Where(t => !t.IsSupport).Concat(relief.Take(1));
            }).ToList();
        }
        internal static bool IsUnderAttack(Settlement site) => site != null &&
            ((site.IsVillage && site.Village.VillageState == Village.VillageStates.BeingRaided) ||
             (site.IsFortification && site.SiegeEvent != null && site.SiegeEvent.BesiegerCamp != null &&
              site.SiegeEvent.BesiegerCamp.MapFaction != site.MapFaction));
        private static bool ReliefWithinReach(MobileParty enemy, Settlement site) =>
            ClanPartyStrategy.Distance(enemy, site, out _, AiBehavior.DefendSettlement) <=
                Math.Max(1f, enemy.Speed) * CampaignTime.HoursInDay;
        private bool ThreatActive(Order order)
        {
            if (order.Kind == Kind.Rescue)
            {
                if (order.Site.MapFaction != Clan.PlayerClan.MapFaction) return false;
                if (IsUnderAttack(order.Site)) { order.ClearSince = -1; return true; }
                // A raider briefly fleeing must not instantly release every defender. Finish one
                // local clearing period, while still using native DefendSettlement near the village.
                if (order.ClearSince < 0) order.ClearSince = Hours;
                return Hours - order.ClearSince < 6d;
            }
            var enemy = order.Data.Party as MobileParty;
            var camp = order.Site.SiegeEvent?.BesiegerCamp;
            return camp != null && camp.MapFaction == Clan.PlayerClan.MapFaction && enemy != null && enemy.IsActive && ReliefWithinReach(enemy, order.Site) &&
                enemy.CurrentSettlement != order.Site &&
                enemy.MapFaction != null && enemy.MapFaction.IsAtWarWith(Clan.PlayerClan.MapFaction) &&
                ((enemy.DefaultBehavior == AiBehavior.DefendSettlement && enemy.TargetSettlement == order.Site) ||
                 enemy.TargetParty?.BesiegerCamp == camp ||
                 (enemy.MapEvent != null && enemy.MapEvent == camp.LeaderParty.MapEvent));
        }
        internal bool IsOrderValid(MobileParty party, Order order)
        {
            if (order.Site == null || !ClanPartyStrategy.CanControl(party)) return false;
            // Do not redirect a battle or tear down a siege merely because another target scores better.
            if (ClanPartyStrategy.Committed(party))
            { if (party.BesiegerCamp != null && order.Kind == Kind.Attack) order.BeganSiege = true; return true; }
            if (order.Kind == Kind.Rescue || order.Kind == Kind.SupportSiege)
                return ThreatActive(order) && ClanPartyStrategy.Distance(party, order.Site, out _, AiBehavior.DefendSettlement) < float.MaxValue;
            if (order.Kind == Kind.Skirmish)
                return IsNearbyEnemy(party, order.Data) && ClanPartyStrategy.Strength(party) >=
                    ClanPartyStrategy.Strength((MobileParty)order.Data.Party) * 1.1f && Hours - order.Started < 12d;
            if (order.Kind == Kind.Attack)
                return !order.BeganSiege && order.Site.IsFortification && order.Site.MapFaction != null &&
                    party.MapFaction.IsAtWarWith(order.Site.MapFaction) &&
                    (order.Site.SiegeEvent == null || order.Site.SiegeEvent.BesiegerCamp.MapFaction == party.MapFaction) &&
                    ClanPartyStrategy.Distance(party, order.Site, out _, AiBehavior.BesiegeSettlement) < float.MaxValue &&
                    ((party.Army != null && party.Army.IsWaitingForArmyMembers()) ||
                     CanAttack(party, order.Site, order.Data.WillGatherArmy, null));
            if (order.Site.MapFaction != party.MapFaction) return false;
            if (order.Kind == Kind.Patrol) return Hours - order.Started < 24d;
            if (party.CurrentSettlement == order.Site)
            {
                if (ClanPartyStrategy.SupplyReady(party)) return false;
                if (order.Arrived < 0) order.Arrived = Hours;
                return Hours - order.Arrived < 6d; // Complete one native recruitment/supply visit.
            }
            return ClanPartyStrategy.Distance(party, order.Site, out _, AiBehavior.GoToSettlement) < float.MaxValue;
        }
        internal static int ReserveCount(int commandGroups) => commandGroups <= 0 ? 0 : Math.Max(1, (commandGroups + 2) / 3);
        internal void Refresh()
        {
            _parties = CollectParties();
            foreach (var entry in _recentRefits.ToArray())
                if (Hours >= entry.Value.Until || !_parties.Contains(entry.Key)) _recentRefits.Remove(entry.Key);
            foreach (var party in _parties.Where(p => p.BesiegerCamp != null && !_orders.ContainsKey(p)))
            {
                var site = party.BesiegerCamp.SiegeEvent.BesiegedSettlement;
                _orders[party] = new Order { Kind = Kind.Attack, Site = site, BeganSiege = true, Started = Hours,
                    Data = new AIBehaviorData(site, AiBehavior.BesiegeSettlement, party.NavigationCapability, false, false, false) };
            }
            foreach (var entry in _orders.ToArray())
                if (!IsOrderValid(entry.Key, entry.Value)) Release(entry.Key, entry.Value);
            var threats = CollectThreats();
            var pool = _parties.Where(p => !ClanPartyStrategy.Committed(p) &&
                (!_orders.TryGetValue(p, out var o) || (o.Kind != Kind.Rescue && o.Kind != Kind.SupportSiege))).ToList();
            // One nearest available command group per uncovered incident first; only then reinforce
            // incidents whose committed/on-the-way friendly strength is insufficient.
            var uncovered = threats.Where(t => Assigned(t) <= 0f && ExternalCover(t) < Required(t)).ToList();
            while (pool.Count > 0 && uncovered.Count > 0)
            {
                if (!AssignNearest(pool, uncovered)) break;
                uncovered.RemoveAll(t => Assigned(t) > 0f);
            }
            var insufficient = threats.Where(t => Assigned(t) + ExternalCover(t) < Required(t)).ToList();
            while (pool.Count > 0 && insufficient.Count > 0)
            {
                if (!AssignNearest(pool, insufficient)) break;
                insufficient.RemoveAll(t => Assigned(t) + ExternalCover(t) >= Required(t));
            }
            var previousReserve = new HashSet<MobileParty>(_reserve);
            _reserve.Clear();
            int responding = _parties.Count(p => _orders.TryGetValue(p, out var o) &&
                (o.Kind == Kind.Rescue || o.Kind == Kind.SupportSiege));
            foreach (var p in _parties.Where(p => !ClanPartyStrategy.Committed(p) &&
                p.Army == null &&
                (!_orders.TryGetValue(p, out var o) || (o.Kind != Kind.Attack && o.Kind != Kind.Rescue && o.Kind != Kind.SupportSiege)))
                .OrderByDescending(p => previousReserve.Contains(p)).ThenBy(ClanPartyStrategy.Strength)
                .ThenBy(p => p.StringId, StringComparer.Ordinal)
                .Take(Math.Max(0, ReserveCount(_parties.Count) - responding))) _reserve.Add(p);
            ClanStrategyDiagnostics.Snapshot(_parties, _orders, _reserve);
        }
        private void Release(MobileParty party, Order order)
        {
            if (order.Kind == Kind.Refit && order.Arrived >= 0 && Hours - order.Arrived >= 6d)
                _recentRefits[party] = (order.Site, Hours + 12d);
            ClanStrategyDiagnostics.OrderEnded(party, order);
            _orders.Remove(party);
        }
        internal bool MayJoinOffensive(MobileParty party)
        {
            if (!ClanPartyStrategy.CanControl(party) || party.Army != null || ClanPartyStrategy.Committed(party) ||
                _reserve.Contains(party) || !ClanPartyStrategy.SupplyReady(party)) return false;
            // Routine patrol and a now-completed supply need may advance into an offensive mission.
            // Rescue, protection, combat and an already selected siege keep their locked assignments.
            return !_orders.TryGetValue(party, out var order) || order.Kind == Kind.Patrol || order.Kind == Kind.Refit;
        }
        internal MBList<MobileParty> FamilyArmyMembers(MobileParty leader)
        {
            var result = new MBList<MobileParty>();
            if (!MayJoinOffensive(leader) || leader.IsCurrentlyAtSea || Clan.PlayerClan.Kingdom == null ||
                Clan.PlayerClan.IsUnderMercenaryService || Clan.PlayerClan.Influence <= 100f ||
                !Clan.PlayerClan.MapFaction.FactionsAtWarWith.Any(f => !f.IsBanditFaction)) return result;
            foreach (var member in _parties.Where(p => p != leader && MayJoinOffensive(p)))
            {
                float distance = ClanPartyStrategy.EnemyDistance(leader, member, leader.NavigationCapability);
                if (!member.IsCurrentlyAtSea && member.GetNumDaysForFoodToLast() >=
                    Campaign.Current.Models.ArmyManagementCalculationModel.MinimumNeededFoodInDaysToCallToArmy &&
                    distance <= Math.Max(1f, member.Speed) * CampaignTime.HoursInDay) result.Add(member);
            }
            return result;
        }
        internal static bool IsNearbyEnemy(MobileParty party, AIBehaviorData data)
        {
            var enemy = data.Party as MobileParty;
            if (enemy == null || enemy == party || !enemy.IsActive || enemy.CurrentSettlement != null || enemy.MapFaction == null ||
                enemy.IsCurrentlyUsedByAQuest || enemy.AttachedTo != null ||
                (enemy.Army != null && enemy.Army.LeaderParty != enemy) ||
                (!enemy.IsLordParty && !enemy.IsBandit) || !party.MapFaction.IsAtWarWith(enemy.MapFaction)) return false;
            float distance = ClanPartyStrategy.EnemyRoute(party, enemy, out var route);
            float radius = Math.Min(40f, Math.Max(party.SeeingRange, Math.Max(1f, party.Speed) * 3f));
            return route.NavigationType == data.NavigationType && distance <= radius;
        }
        internal bool ShouldDirectPursue(MobileParty party, MobileParty enemy)
        {
            if (!ClanPartyStrategy.CanControl(party) || ClanPartyStrategy.Committed(party) || enemy == null ||
                party.DefaultBehavior != AiBehavior.GoAroundParty || party.TargetParty != enemy ||
                party.IsCurrentlyAtSea != enemy.IsCurrentlyAtSea || party.Food <= 0 ||
                !_orders.TryGetValue(party, out var order)) return false;
            if (order.Kind == Kind.Rescue)
            {
                if (order.Site?.MapFaction != party.MapFaction || !IsRescueEnemy(order, enemy)) return false;
            }
            else if ((order.Kind != Kind.Skirmish && order.Kind != Kind.SupportSiege) || order.Data.Party != enemy)
                return false;
            // Recheck the same local, reachable, hostile and strength constraints used to select
            // the task. A changed target or navigation mode falls back to native behavior.
            ClanPartyStrategy.EnemyRoute(party, enemy, out var data);
            return IsNearbyEnemy(party, data) &&
                ClanPartyStrategy.Strength(party) >= ClanPartyStrategy.Strength(enemy) * 1.1f;
        }
        internal bool HasArmySiegeMission(MobileParty party) => ClanPartyStrategy.CanControl(party) &&
            party.Army != null && !ClanPartyStrategy.Committed(party) &&
            party.DefaultBehavior == AiBehavior.BesiegeSettlement &&
            _orders.TryGetValue(party, out var order) && order.Kind == Kind.Attack &&
            party.TargetSettlement == order.Site && IsOrderValid(party, order);
        private static MobileParty RescueEnemy(Order order) =>
            order.Site.SiegeEvent?.BesiegerCamp?.LeaderParty ?? order.Site.LastAttackerParty;
        private static bool IsRescueEnemy(Order order, MobileParty enemy)
        {
            if (enemy == null || enemy != RescueEnemy(order)) return false;
            // During the short clearing period, do not follow the former raider far from the site.
            return IsUnderAttack(order.Site) || ClanPartyStrategy.Distance(enemy, order.Site, out _, AiBehavior.DefendSettlement)
                <= Math.Max(3f, enemy.Speed);
        }
        private bool TryRescueIntercept(MobileParty party, Order order, out AIBehaviorData data)
        {
            data = default;
            var enemy = RescueEnemy(order);
            if (!IsRescueEnemy(order, enemy)) return false;
            ClanPartyStrategy.EnemyRoute(party, enemy, out data);
            return IsNearbyEnemy(party, data) && ClanPartyStrategy.Strength(party) >= ClanPartyStrategy.Strength(enemy) * 1.1f;
        }
        internal bool ShouldIgnoreOpportunity(MobileParty party, MobileParty enemy)
        {
            if (!ClanPartyStrategy.CanControl(party) || ClanPartyStrategy.Committed(party) ||
                !_orders.TryGetValue(party, out var order) || !IsOrderValid(party, order)) return false;
            if (order.Kind == Kind.Rescue) return !IsRescueEnemy(order, enemy);
            if (order.Kind == Kind.SupportSiege) return order.Data.Party != enemy;
            return enemy.MapEvent == null && HasArmySiegeMission(party);
        }
        private Order NearbyFight(MobileParty party, Settlement anchor)
        {
            if (anchor == null || party.Food <= 0f) return null;
            float radius = Math.Min(40f, Math.Max(party.SeeingRange, Math.Max(1f, party.Speed) * 3f));
            MobileParty target = null; AIBehaviorData best = default; float nearest = float.MaxValue;
            // Scan local world parties, including deserters (native BanditPartyComponent). Native
            // engagement scores may be absent for a commander still inside a settlement.
            foreach (var enemy in MobileParty.All)
            {
                if (enemy == party || !enemy.IsActive || enemy.Position.Distance(party.Position) > radius ||
                    (!enemy.IsLordParty && !enemy.IsBandit)) continue;
                float distance = ClanPartyStrategy.EnemyRoute(party, enemy, out var data);
                if (distance >= nearest || !IsNearbyEnemy(party, data) ||
                    ClanPartyStrategy.Strength(party) < ClanPartyStrategy.Strength(enemy) * 1.1f) continue;
                float alreadyAssigned = _orders.Where(e => e.Key != party && e.Value.Kind == Kind.Skirmish &&
                    e.Value.Data.Party == enemy && IsOrderValid(e.Key, e.Value)).Sum(e => ClanPartyStrategy.Strength(e.Key));
                if (alreadyAssigned >= ClanPartyStrategy.Strength(enemy) * 1.1f) continue;
                target = enemy; best = data; nearest = distance;
            }
            return target == null ? null : new Order { Kind = Kind.Skirmish, Site = anchor, Data = best, Started = Hours };
        }
        private Settlement PatrolAnchor(MobileParty party) => Settlement.All.Where(s => s.IsFortification &&
            s.MapFaction == party.MapFaction && !IsUnderAttack(s)).OrderBy(s =>
                ClanPartyStrategy.Distance(party, s, out _, AiBehavior.PatrolAroundPoint)).FirstOrDefault();
        private bool AssignNearest(List<MobileParty> pool, List<Threat> threats)
        {
            MobileParty chosen = null; Threat incident = null; AIBehaviorData data = default;
            float nearest = float.MaxValue;
            foreach (var threat in threats.OrderBy(t => t.Site.StringId, StringComparer.Ordinal))
                foreach (var party in pool)
                {
                    float d = ClanPartyStrategy.Distance(party, threat.Site, out var candidate, AiBehavior.DefendSettlement);
                    if (d >= nearest) continue;
                    if (threat.IsSupport)
                        candidate = new AIBehaviorData(threat.Relief, AiBehavior.GoAroundParty, candidate.NavigationType, false, false, candidate.IsFromPort);
                    chosen = party; incident = threat; data = candidate; nearest = d;
                }
            if (chosen == null) return false;
            _orders[chosen] = new Order { Kind = incident.IsSupport ? Kind.SupportSiege : Kind.Rescue,
                Site = incident.Site, Data = data, Started = Hours };
            ClanStrategyDiagnostics.Assigned(chosen, _orders[chosen], nearest,
                () => Required(incident), () => Assigned(incident), () => ExternalCover(incident));
            chosen.Ai.RethinkAtNextHourlyTick = true;
            pool.Remove(chosen);
            return true;
        }
        private bool Matches(Order order, Threat threat) => order.Site == threat.Site &&
            (threat.IsSupport ? order.Kind == Kind.SupportSiege && order.Data.Party == threat.Relief : order.Kind == Kind.Rescue);
        private float Assigned(Threat threat) => _orders.Where(e => Matches(e.Value, threat)).Sum(e => ClanPartyStrategy.Strength(e.Key));
        private static MobileParty Enemy(Threat threat) => threat.Relief ?? threat.Site.LastAttackerParty;
        private static float Required(Threat threat) => Math.Max(1f, threat.Demand > 0 ? threat.Demand :
            Enemy(threat) == null ? 1f : ClanPartyStrategy.Strength(Enemy(threat)) * 1.1f);
        private float ExternalCover(Threat threat)
        {
            var enemy = Enemy(threat);
            float inCamp = threat.IsSupport ? threat.Site.SiegeEvent.BesiegerCamp
                .GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege).Sum(p => Math.Max(0, p.CalculateCurrentStrength())) : 0;
            return inCamp + MobileParty.AllLordParties.Where(p => !_parties.Contains(p) &&
                p.IsActive && p.MapFaction == Clan.PlayerClan.MapFaction && p.AttachedTo == null &&
                (p.Army == null || p.Army.LeaderParty == p) &&
                (threat.Site.SiegeEvent == null || p.BesiegerCamp != threat.Site.SiegeEvent.BesiegerCamp) &&
                ((p.DefaultBehavior == AiBehavior.DefendSettlement && p.TargetSettlement == threat.Site) ||
                 (enemy != null && p.TargetParty == enemy)) &&
                ClanPartyStrategy.Distance(p, threat.Site, out _, AiBehavior.DefendSettlement) <=
                    Math.Max(1f, p.Speed) * CampaignTime.HoursInDay * 0.5f).Sum(ClanPartyStrategy.Strength);
        }
        internal static bool CanAttack(MobileParty party, Settlement site, bool gather, PartyThinkParams think)
        {
            if (site?.IsFortification != true) return false;
            float attackers = ClanPartyStrategy.Strength(party);
            if (gather && party.Army == null)
            {
                // A saved gathering decision must still be backed by a native-created army after load.
                if (think == null) return false;
                attackers += think.PossibleArmyMembersUponArmyCreation.Where(p => p != party && p.Army == null)
                    .Sum(ClanPartyStrategy.Strength);
            }
            float defenders = site.GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege)
                .Sum(p => Math.Max(0f, p.CalculateCurrentStrength()));
            float advantage = Math.Max(1f, Campaign.Current.Models.CombatSimulationModel.GetSettlementAdvantage(site));
            return attackers >= Math.Max(1f, defenders * (float)Math.Pow(advantage, 0.8d) * 1.1f);
        }
        private bool TryAssignSiege(MobileParty party, PartyThinkParams think)
        {
            int attacks = _orders.Count(e => e.Value.Kind == Kind.Attack);
            // Army members disappear from the independent command-group count. Existing armies
            // must not all become reserves just because the final independent groups merged.
            int capacity = Math.Max(_parties.Count(p => p.Army != null),
                _parties.Count - ReserveCount(_parties.Count));
            if ((party.Army == null && _reserve.Contains(party)) ||
                !ClanPartyStrategy.SiegeSupplied(party) || attacks >= capacity) return false;
            var scores = (MBList<ValueTuple<AIBehaviorData, float>>)ClanPartyStrategy.Scores.GetValue(think);
            var siege = scores.Where(e => e.Item2 > 0f && e.Item1.AiBehavior == AiBehavior.BesiegeSettlement &&
                e.Item1.Party is Settlement target && target.MapFaction != null && party.MapFaction.IsAtWarWith(target.MapFaction) &&
                CanAttack(party, target, e.Item1.WillGatherArmy, think)).OrderByDescending(e => e.Item2).FirstOrDefault();
            if (siege.Item2 <= 0f) return false;
            var order = new Order { Kind = Kind.Attack, Site = (Settlement)siege.Item1.Party, Data = siege.Item1, Started = Hours };
            _orders[party] = order; ClanPartyStrategy.Pin(think, order.Data); return true;
        }
        internal bool TryDisbandIdleArmy(MobileParty party, PartyThinkParams think)
        {
            if (!Enabled || !ClanPartyStrategy.CanControl(party) || party.Army == null ||
                ClanPartyStrategy.Committed(party) || party.Army.IsWaitingForArmyMembers()) return false;
            if (_orders.TryGetValue(party, out var order) &&
                (order.Kind == Kind.Attack || order.Kind == Kind.Rescue || order.Kind == Kind.SupportSiege) &&
                IsOrderValid(party, order)) return false;
            var army = party.Army;
            var members = army.Parties.Where(p => p.ActualClan == Clan.PlayerClan).ToArray();
            // Use the native objective-finished action so followers detach, army events and
            // kingdom lists are cleaned normally, without a player-forced disband penalty.
            DisbandArmyAction.ApplyByObjectiveFinished(army);
            if (party.Army == army) return false;
            foreach (var member in members)
            {
                if (_orders.TryGetValue(member, out var old)) Release(member, old);
                if (ClanPartyStrategy.CanControl(member)) member.Ai.RethinkAtNextHourlyTick = true;
            }
            _lastHour = -1; // Recount independent commanders and reserves on the next decision.
            // This think cache was evaluated using the former army's strength and invitations.
            // Do not execute or recreate anything from that stale cache after native dispersion.
            ((MBList<ValueTuple<AIBehaviorData, float>>)ClanPartyStrategy.Scores.GetValue(think)).Clear();
            think.SetArmyMembers(new MBList<MobileParty>());
            think.DoNotChangeBehavior = true;
            ClanStrategyDiagnostics.ArmyReleased(party, members.Length);
            return true;
        }
        internal void Decide(MobileParty party, PartyThinkParams think)
        {
            var scores = ((MBList<ValueTuple<AIBehaviorData, float>>)ClanPartyStrategy.Scores.GetValue(think));
            for (int i = scores.Count - 1; i >= 0; i--)
                if (scores[i].Item1.AiBehavior == AiBehavior.RaidSettlement) scores.RemoveAt(i);
            if (ClanPartyStrategy.Committed(party) && party.MapEvent?.IsRaid != true)
            {
                if (party.BesiegerCamp != null && _orders.TryGetValue(party, out var siegeOrder) &&
                    siegeOrder.Kind == Kind.Attack && party.Food > 0f && CanAttack(party, siegeOrder.Site, false, null))
                {
                    ClanPartyStrategy.Pin(think, siegeOrder.Data);
                    think.DoNotChangeBehavior = true;
                }
                // Actual combat is never interrupted. During a siege, leave native retreat decisions
                // available when its supplies or strength no longer support the operation.
                return;
            }
            if (think.DoNotChangeBehavior && party.Army != null && party.Army.IsWaitingForArmyMembers()) return;
            if (_orders.TryGetValue(party, out var order))
            {
                if (order.Data.WillGatherArmy && party.Army != null) order.Data.WillGatherArmy = false;
                bool routineCanAdvance = order.Kind == Kind.Patrol || order.Kind == Kind.Refit ||
                    (party.Army != null && order.Kind == Kind.Skirmish);
                if (IsOrderValid(party, order) && !routineCanAdvance)
                {
                    if (order.Kind == Kind.Rescue)
                    {
                        // Only intercept this incident's attacker. Generic NearbyFight here lets
                        // a nominal rescuer chase looters forever while the scheduler counts cover.
                        if (TryRescueIntercept(party, order, out var intercept))
                        { ClanPartyStrategy.Pin(think, intercept); return; }
                    }
                    ClanPartyStrategy.Pin(think, order.Data); return;
                }
                if (routineCanAdvance)
                {
                    // Keep the original routine order if no offensive opportunity materializes below.
                }
                else Release(party, order);
            }
            if (think.DoNotChangeBehavior) return; // Native army gathering retains its own waiting rules.
            if (party.Army == null && think.PossibleArmyMembersUponArmyCreation != null)
            {
                // Native army creation consumes this same list. Merely reserving commanders in our
                // scheduler is insufficient if a new army can immediately summon those reserves away.
                think.SetArmyMembers(new MBList<MobileParty>(think.PossibleArmyMembersUponArmyCreation.Where(p =>
                    p == party || p.ActualClan != Clan.PlayerClan ||
                    MayJoinOffensive(p))));
            }
            // Formed armies are siege forces: rescue orders above still take priority, but a
            // viable fortress outranks local skirmishes, including an older saved skirmish order.
            if (party.Army != null && TryAssignSiege(party, think)) return;
            if (TryDisbandIdleArmy(party, think)) return;
            if (party.Army != null && order?.Kind == Kind.Skirmish && IsOrderValid(party, order))
            { ClanPartyStrategy.Pin(think, order.Data); return; }
            var localFight = NearbyFight(party, order?.Site ?? PatrolAnchor(party));
            if (localFight != null) { _orders[party] = localFight; ClanPartyStrategy.Pin(think, localFight.Data); return; }
            if (party.Army == null && TryAssignSiege(party, think)) return;
            if (_orders.TryGetValue(party, out order) && IsOrderValid(party, order))
            { ClanPartyStrategy.Pin(think, order.Data); return; }
            if (order != null && _orders.ContainsKey(party)) Release(party, order);
            var visit = scores.Where(e => e.Item2 > 0 && e.Item1.AiBehavior == AiBehavior.GoToSettlement &&
                e.Item1.Party is Settlement s && s.MapFaction == party.MapFaction && !IsUnderAttack(s) &&
                (!_recentRefits.TryGetValue(party, out var recent) || recent.Site != s || Hours >= recent.Until))
                .OrderByDescending(e => e.Item2).FirstOrDefault();
            if (visit.Item2 > 0 && !ClanPartyStrategy.SupplyReady(party))
                order = new Order { Kind = Kind.Refit, Site = (Settlement)visit.Item1.Party, Data = visit.Item1, Started = Hours };
            else
            {
                Settlement closest = null; AIBehaviorData patrol = default; float nearest = float.MaxValue;
                foreach (var site in Settlement.All.Where(s => s.IsFortification && s.MapFaction == party.MapFaction && !IsUnderAttack(s)))
                {
                    float d = ClanPartyStrategy.Distance(party, site, out var data, AiBehavior.PatrolAroundPoint);
                    if (d < nearest)
                    {
                        nearest = d; closest = site;
                        patrol = new AIBehaviorData(site, AiBehavior.PatrolAroundPoint, data.NavigationType, false, party.IsCurrentlyAtSea && site.HasPort, data.IsFromPort);
                    }
                }
                if (closest == null) return;
                order = new Order { Kind = Kind.Patrol, Site = closest, Data = patrol, Started = Hours };
                order = NearbyFight(party, closest) ?? order;
            }
            _orders[party] = order; ClanPartyStrategy.Pin(think, order.Data);
        }
        internal void TraceDecision(MobileParty party, PartyThinkParams think, string native)
        {
            _orders.TryGetValue(party, out var order);
            ClanStrategyDiagnostics.Decision(party, order, _reserve.Contains(party), think, native);
        }
        internal string SaveOrders() => string.Join("\n", _orders.Select(e => string.Join("|", new[] {
            Uri.EscapeDataString(e.Key.StringId), Uri.EscapeDataString(e.Value.Site.StringId), ((int)e.Value.Kind).ToString(),
            Uri.EscapeDataString((e.Value.Data.Party as MobileParty)?.StringId ?? ""),
            e.Value.Started.ToString("R", CultureInfo.InvariantCulture), e.Value.Arrived.ToString("R", CultureInfo.InvariantCulture),
            ((int)e.Value.Data.NavigationType).ToString(), e.Value.Data.WillGatherArmy ? "1" : "0",
            e.Value.Data.IsTargetingPort ? "1" : "0", e.Value.Data.IsFromPort ? "1" : "0", e.Value.BeganSiege ? "1" : "0",
            e.Value.ClearSince.ToString("R", CultureInfo.InvariantCulture) })));
        internal void LoadOrders()
        {
            _orders.Clear();
            foreach (string line in (_savedOrders ?? "").Split('\n'))
            {
                try
                {
                    var f = line.Split('|'); if (f.Length != 11 && f.Length != 12) continue;
                    var p = MBObjectManager.Instance.GetObject<MobileParty>(Uri.UnescapeDataString(f[0]));
                    var s = MBObjectManager.Instance.GetObject<Settlement>(Uri.UnescapeDataString(f[1]));
                    if (p == null || s == null || !int.TryParse(f[2], out int k) || !Enum.IsDefined(typeof(Kind), k)) continue;
                    var kind = (Kind)k;
                    var enemy = string.IsNullOrEmpty(f[3]) ? null : MBObjectManager.Instance.GetObject<MobileParty>(Uri.UnescapeDataString(f[3]));
                    if ((kind == Kind.SupportSiege || kind == Kind.Skirmish) && enemy == null) continue;
                    var action = kind == Kind.Rescue ? AiBehavior.DefendSettlement : (kind == Kind.SupportSiege || kind == Kind.Skirmish) ? AiBehavior.GoAroundParty :
                        kind == Kind.Attack ? AiBehavior.BesiegeSettlement : kind == Kind.Refit ? AiBehavior.GoToSettlement : AiBehavior.PatrolAroundPoint;
                    var target = (TaleWorlds.CampaignSystem.Map.IMapPoint)enemy ?? s;
                    _orders[p] = new Order { Kind = kind, Site = s, Started = double.Parse(f[4], CultureInfo.InvariantCulture),
                        Arrived = double.Parse(f[5], CultureInfo.InvariantCulture), BeganSiege = f[10] == "1",
                        ClearSince = f.Length == 12 ? double.Parse(f[11], CultureInfo.InvariantCulture) : -1,
                        Data = kind == Kind.Patrol
                            ? new AIBehaviorData(s, action, (MobileParty.NavigationType)int.Parse(f[6]), false, f[8] == "1", f[9] == "1")
                            : new AIBehaviorData(target, action, (MobileParty.NavigationType)int.Parse(f[6]), f[7] == "1", f[8] == "1", f[9] == "1") };
                }
                catch (Exception) { /* Ignore stale or malformed saved orders; native world objects are authoritative. */ }
            }
        }
    }
}

