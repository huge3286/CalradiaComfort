using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace StorySkip
{
    // Observers only: no scores, orders, movement, initiative or game state are changed here.
    internal static class ClanStrategyDiagnostics
    {
        private sealed class Raid { internal Settlement Site; internal double Until; }
        private sealed class Stamp { internal string Key; internal double Hour; }
        private static readonly Dictionary<MobileParty, Raid> Raiders = new Dictionary<MobileParty, Raid>();
        private static readonly Dictionary<string, Stamp> Stamps = new Dictionary<string, Stamp>();
        private static readonly System.Reflection.FieldInfo AiParty = AccessTools.Field(typeof(MobilePartyAi), "_mobileParty");
        private static StreamWriter _writer;
        private static bool _failed;
        private static long _bytes;
        private static double _snapshotHour = -1;
        internal static string LogPath { get; private set; }
        private static double Hour => CampaignTime.Now.ToHours;
        private static bool Active => _writer != null && !_failed && _bytes < 8 * 1024 * 1024 &&
            ClanPartyStrategyBehavior.Current?.Enabled == true;
        private static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Clean(object text) => (text?.ToString() ?? "-").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        private static string Party(MobileParty p) => p == null ? "-" : Clean(p.Name) + "[" + p.StringId + "]";
        private static string Site(Settlement s) => s == null ? "-" : Clean(s.Name) + "[" + s.StringId + "]";
        private static string Power(MobileParty p) => p == null ? "-" : N(ClanPartyStrategy.Strength(p));
        private static string Position(MobileParty p) => p == null ? "-" :
            N(p.Position.ToVec2().X) + "," + N(p.Position.ToVec2().Y);
        private static string Distance(MobileParty a, MobileParty b) => a == null || b == null ? "-" :
            N(a.Position.Distance(b.Position)); // Straight-line map distance, explicitly labelled in the log.
        private static string State(MobileParty p) => "party=" + Party(p) + " pos=" + Position(p) +
            " power=" + Power(p) + " default=" + p.DefaultBehavior + " short=" + p.ShortTermBehavior +
            " targetParty=" + Party(p.TargetParty) + " shortTarget=" + Party(p.ShortTermTargetParty) +
            " targetSite=" + Site(p.TargetSettlement) + " inBattle=" + (p.MapEvent != null) +
            " inSiege=" + (p.BesiegerCamp != null) + " army=" + Party(p.Army?.LeaderParty) +
            " currentSite=" + Site(p.CurrentSettlement) + " sizeRatio=" + N(p.PartySizeRatio) +
            " foodDays=" + p.GetNumDaysForFoodToLast();

        // A broken logger must never prevent a decision, raid event or native AI result.
        private static void Safe(Action action)
        {
            if (!Active) return;
            try { action(); }
            catch (Exception ex)
            {
                if (_failed) return;
                _failed = true;
                try { Write("DIAGNOSTIC_ERROR", ex.GetType().Name + ": " + Clean(ex.Message)); }
                catch (Exception) { /* File I/O failure is isolated from gameplay. */ }
            }
        }
        private static void Write(string type, string message)
        {
            if (_writer == null || _bytes >= 8 * 1024 * 1024) return;
            string line = DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " gameHour=" + N(Hour) +
                " " + type + " " + message;
            _writer.WriteLine(line); _bytes += Encoding.UTF8.GetByteCount(line) + 2;
            if (_bytes >= 8 * 1024 * 1024) _writer.WriteLine("LOG_LIMIT reached 8 MiB; further records paused for this session.");
        }
        private static void Changed(string id, string key, double heartbeat, Action write)
        {
            if (Stamps.TryGetValue(id, out var stamp) && stamp.Key == key && Hour - stamp.Hour < heartbeat) return;
            Stamps[id] = new Stamp { Key = key, Hour = Hour }; write();
        }
        internal static void InitializePatches(Harmony harmony)
        {
            if (AiParty == null) throw new MissingFieldException("Native AI party reference changed.");
            harmony.Patch(AccessTools.Method(typeof(DefaultMobilePartyAIModel), "GetBestInitiativeBehavior"),
                postfix: new HarmonyMethod(typeof(ClanStrategyDiagnostics), nameof(ObserveInitiative)));
            harmony.Patch(AccessTools.Method(typeof(DefaultMobilePartyAIModel), "CalculateInitiativeScoresForEnemy"),
                postfix: new HarmonyMethod(typeof(ClanStrategyDiagnostics), nameof(ObserveThreat)));
            harmony.Patch(AccessTools.Method(typeof(MobilePartyAi), "GetBehaviors"),
                postfix: new HarmonyMethod(typeof(ClanStrategyDiagnostics), nameof(ObserveBehavior)));
        }
        internal static void Begin()
        {
            End();
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "Logs", "CalradiaComfort");
                Directory.CreateDirectory(directory);
                LogPath = Path.Combine(directory, "strategy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                _writer = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                Write("SESSION", "Calradia Comfort v0.22.7; release idle armies through native objective-finished action; " +
                    "distances are straight-line map units except routeDistance on ASSIGN; " +
                    "power is native estimated combat power, not troop count; scores are native outputs.");
                foreach (var site in Settlement.All.Where(s => s.IsVillage && s.MapFaction == Clan.PlayerClan.MapFaction &&
                    s.Village.VillageState == Village.VillageStates.BeingRaided))
                    Track(site.LastAttackerParty, site);
            }
            catch (Exception) { End(); }
        }
        internal static void End()
        {
            try { _writer?.Dispose(); } catch (Exception) { }
            _writer = null; _bytes = 0; _failed = false; _snapshotHour = -1;
            Raiders.Clear(); Stamps.Clear();
        }
        private static void Track(MobileParty raider, Settlement site)
        {
            if (raider == null || site?.IsVillage != true || site.MapFaction != Clan.PlayerClan.MapFaction) return;
            Raiders[raider] = new Raid { Site = site, Until = Hour + 12d };
        }
        private static bool Tracked(MobileParty party)
        {
            if (party == null) return false;
            if (party.DefaultBehavior == AiBehavior.RaidSettlement) Track(party, party.TargetSettlement);
            if (!Raiders.TryGetValue(party, out var raid)) return false;
            if (raid.Site.Village.VillageState == Village.VillageStates.BeingRaided) raid.Until = Hour + 12d;
            if (Hour <= raid.Until) return true;
            Raiders.Remove(party); return false;
        }
        internal static void VillageChanged(Village village, Village.VillageStates oldState, Village.VillageStates newState, MobileParty raider)
        {
            Safe(() =>
            {
                var site = village?.Settlement;
                if (site == null || site.MapFaction != Clan.PlayerClan.MapFaction) return;
                // Some end-state events omit their instigator; the settlement retains its last attacker.
                var attacker = raider ?? site.LastAttackerParty;
                Track(attacker, site);
                Write("RAID_STATE", "site=" + Site(site) + " from=" + oldState + " to=" + newState +
                    " " + (attacker == null ? "raider=-" : State(attacker)));
            });
        }
        private static void ObserveInitiative(MobileParty __0, AiBehavior __1, MobileParty __2, float __3)
        {
            if (!Active) return;
            Safe(() =>
            {
                if (!Tracked(__0)) return;
                string key = __1 + ":" + (__2?.StringId ?? "-");
                Changed("initiative:" + __0.StringId, key, 1d, () => Write("NATIVE_INITIATIVE",
                    "site=" + Site(Raiders[__0].Site) + " " + State(__0) + " choice=" + __1 +
                    " score=" + N(__3) + " threatOrTarget=" + Party(__2) + " targetPower=" + Power(__2) +
                    " straightDistance=" + Distance(__0, __2)));
            });
        }
        private static void ObserveThreat(MobileParty __0, MobileParty __1, float __2, float __3, float __4)
        {
            if (!Active) return;
            Safe(() =>
            {
                if (!Tracked(__0) || __1 == null || (__2 <= 0f && __1.ActualClan != Clan.PlayerClan)) return;
                string key = (__2 > 1f ? "flee>1" : __2 > 0f ? "flee>0" : "no-flee") + ":" + (__3 > 1f);
                Changed("threat:" + __0.StringId + ":" + __1.StringId, key, 1d, () => Write("NATIVE_THREAT",
                    "raider=" + Party(__0) + " site=" + Site(Raiders[__0].Site) + " candidate=" + Party(__1) +
                    " ownFamily=" + (__1.ActualClan == Clan.PlayerClan) + " straightDistance=" + Distance(__0, __1) +
                    " raiderPower=" + Power(__0) + " candidatePower=" + Power(__1) +
                    " localAdvantage=" + N(__4) + " fleeScore=" + N(__2) + " attackScore=" + N(__3)));
            });
        }
        private static void ObserveBehavior(MobilePartyAi __instance, AiBehavior __0, IInteractablePoint __1)
        {
            if (!Active || Raiders.Count == 0) return;
            Safe(() =>
            {
                var party = (MobileParty)AiParty.GetValue(__instance);
                if (!Tracked(party)) return;
                var target = (__1 as PartyBase)?.MobileParty;
                string key = __0 + ":" + (target?.StringId ?? "-");
                Changed("behavior:" + party.StringId, key, 1d, () => Write("NATIVE_BEHAVIOR",
                    "site=" + Site(Raiders[party].Site) + " " + State(party) + " result=" + __0 +
                    " resultTarget=" + Party(target) + " targetPower=" + Power(target) +
                    " straightDistance=" + Distance(party, target)));
            });
        }
        internal static string NativeScores(PartyThinkParams think)
        {
            if (!Active) return "";
            try
            {
                return string.Join(";", think.AIBehaviorScores.OrderByDescending(e => e.Item2).Take(5).Select(e =>
                    e.Item1.AiBehavior + ":" + (e.Item1.Party is Settlement s ? Site(s) : Party(e.Item1.Party as MobileParty)) +
                    ":" + N(e.Item2) + ":gather=" + e.Item1.WillGatherArmy)) +
                    " siegeCandidates=" + think.AIBehaviorScores.Count(e => e.Item1.AiBehavior == AiBehavior.BesiegeSettlement) +
                    " armyCandidates=" + (think.PossibleArmyMembersUponArmyCreation?.Count ?? 0);
            }
            catch (Exception) { return "unavailable"; }
        }
        internal static void EnemyScores(MobileParty party, PartyThinkParams think)
        {
            if (!Active) return;
            Safe(() =>
            {
                if (!Tracked(party)) return;
                string native = NativeScores(think);
                Changed("enemyScores:" + party.StringId, native, 1d, () => Write("ENEMY_SCORES",
                    State(party) + " locked=" + think.DoNotChangeBehavior + " nativeScores={" + native + "}"));
            });
        }
        internal static void Decision(MobileParty party, ClanPartyStrategyBehavior.Order order, bool reserve, PartyThinkParams think, string native)
        {
            Safe(() =>
            {
                string selected = order == null ? "native" : order.Kind + ":" + order.Site.StringId;
                var chosen = think.AIBehaviorScores.OrderByDescending(e => e.Item2).FirstOrDefault().Item1;
                string command = chosen.AiBehavior + ":" + (chosen.Party is Settlement s ? Site(s) : Party(chosen.Party as MobileParty));
                string key = selected + ":" + reserve + ":" + think.DoNotChangeBehavior + ":" + command;
                Changed("decision:" + party.StringId, key, 1d, () => Write("DECISION",
                    State(party) + " order=" + selected + " reserve=" + reserve + " locked=" + think.DoNotChangeBehavior +
                    " selectedCommand=" + command + " nativeScores={" + native + "}"));
            });
        }
        internal static void Applied(MobileParty party)
        {
            Safe(() =>
            {
                string key = party.DefaultBehavior + ":" + (party.TargetParty?.StringId ?? party.TargetSettlement?.StringId ?? "-") +
                    ":" + (party.Army?.LeaderParty?.StringId ?? "-");
                Changed("applied:" + party.StringId, key, 1d, () => Write("NATIVE_APPLIED", State(party)));
            });
        }
        internal static void Pursuit(MobileParty party, MobileParty enemy)
        {
            Safe(() => Changed("pursuit:" + party.StringId, enemy.StringId, 1d, () => Write("DIRECT_PURSUIT",
                State(party) + " enemy=" + Party(enemy) + " targetPower=" + Power(enemy) +
                " straightDistance=" + Distance(party, enemy) + " shortCommand=EngageParty")));
        }
        internal static void MissionFocus(MobileParty party, MobileParty enemy)
        {
            Safe(() => Changed("siege-focus:" + party.StringId, enemy.StringId, 1d,
                () => Write("MISSION_FOCUS", State(party) + " ignoredOpportunisticTarget=" + Party(enemy))));
        }
        internal static void ArmyReleased(MobileParty party, int members)
        {
            Safe(() => Write("ARMY_RELEASED", State(party) + " familyMembers=" + members +
                " reason=no-viable-siege-or-emergency next=independent-decisions"));
        }
        internal static void Assigned(MobileParty party, ClanPartyStrategyBehavior.Order order, float distance, Func<float> needed, Func<float> assigned, Func<float> external)
        {
            Safe(() => Write("ASSIGN", State(party) + " order=" + order.Kind + " site=" + Site(order.Site) +
                " routeDistance=" + N(distance) + " requiredPower=" + N(needed()) + " assignedPower=" + N(assigned()) +
                " externalCover=" + N(external()) + " raider=" + Party(order.Site.LastAttackerParty) +
                " raiderStraightDistance=" + Distance(party, order.Site.LastAttackerParty)));
        }
        internal static void OrderEnded(MobileParty party, ClanPartyStrategyBehavior.Order order)
        {
            Safe(() => Write("ORDER_END", State(party) + " order=" + order.Kind + " site=" + Site(order.Site) +
                " ageHours=" + N(Hour - order.Started) + " villageState=" + (order.Site.Village?.VillageState.ToString() ?? "-")));
        }
        internal static void Snapshot(IEnumerable<MobileParty> parties, IDictionary<MobileParty, ClanPartyStrategyBehavior.Order> orders, ISet<MobileParty> reserve)
        {
            Safe(() =>
            {
                if (Math.Floor(Hour) == _snapshotHour) return;
                _snapshotHour = Math.Floor(Hour);
                foreach (var p in parties)
                {
                    orders.TryGetValue(p, out var order);
                    Write("PARTY", State(p) + " order=" + (order == null ? "-" : order.Kind + ":" + Site(order.Site)) +
                        " reserve=" + reserve.Contains(p));
                }
                foreach (var raider in Raiders.Keys.ToArray())
                    if (Tracked(raider)) Write("RAIDER", State(raider) + " site=" + Site(Raiders[raider].Site));
            });
        }
    }
}
