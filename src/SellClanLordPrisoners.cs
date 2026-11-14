using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class SellClanLordPrisoners
    {
        internal sealed class Captive
        {
            internal Hero Hero;
            internal PartyBase Source;
        }

        internal sealed class SaleResult
        {
            internal int Sold;
            internal long Gold;
            internal bool Failed;
        }

        private static bool _selling;

        internal static bool CanUse()
        {
            var town = Settlement.CurrentSettlement;
            var main = MobileParty.MainParty;
            return Campaign.Current != null && ComfortFeatures.SellClanLordPrisonersEnabled && !_selling &&
                Hero.MainHero != null && !Hero.MainHero.IsPrisoner && Clan.PlayerClan != null &&
                town != null && town.IsTown && town.SiegeEvent == null && main != null &&
                main.CurrentSettlement == town && main.MapEvent == null;
        }

        private static HashSet<string> GetLocks()
        {
            var tracker = Campaign.Current.GetCampaignBehavior<IViewDataTracker>();
            return new HashSet<string>(tracker?.GetPartyPrisonerLocks() ?? new string[0]);
        }

        internal static bool IsOwnedSource(PartyBase source)
        {
            if (source == null || Clan.PlayerClan == null) return false;
            if (source == PartyBase.MainParty) return true;
            if (source.IsMobile)
                return source.MobileParty != null && source.MobileParty.IsActive &&
                    source.MobileParty.ActualClan == Clan.PlayerClan && source.MobileParty.MapEvent == null;
            var settlement = source.Settlement;
            return settlement != null && (settlement.IsTown || settlement.IsCastle) &&
                settlement.OwnerClan == Clan.PlayerClan && settlement.SiegeEvent == null && source.MapEvent == null;
        }

        internal static bool IsEligible(Hero hero, PartyBase source, HashSet<string> locks)
        {
            return IsOwnedSource(source) && hero != null && hero != Hero.MainHero && hero.IsAlive &&
                hero.IsLord && hero.IsPrisoner && hero.Clan != Clan.PlayerClan &&
                hero.PartyBelongedToAsPrisoner == source && hero.CharacterObject != null &&
                !hero.CharacterObject.IsNotTransferableInPartyScreen &&
                source.PrisonRoster.GetTroopCount(hero.CharacterObject) > 0 &&
                !locks.Contains(hero.CharacterObject.StringId);
        }

        internal static List<Captive> Collect()
        {
            var result = new List<Captive>();
            if (Campaign.Current == null || Clan.PlayerClan == null || MobileParty.MainParty == null) return result;
            var sources = new HashSet<PartyBase> { PartyBase.MainParty };
            foreach (var component in Clan.PlayerClan.WarPartyComponents)
                if (component.MobileParty != null) sources.Add(component.MobileParty.Party);
            foreach (var settlement in Clan.PlayerClan.Settlements)
                if (settlement != null && (settlement.IsTown || settlement.IsCastle)) sources.Add(settlement.Party);
            var locks = GetLocks();
            var seen = new HashSet<Hero>();
            foreach (var source in sources)
            {
                if (!IsOwnedSource(source)) continue;
                foreach (var entry in source.PrisonRoster.GetTroopRoster())
                {
                    Hero hero = entry.Character?.HeroObject;
                    if (entry.Number > 0 && IsEligible(hero, source, locks) && seen.Add(hero))
                        result.Add(new Captive { Hero = hero, Source = source });
                }
            }
            return result;
        }

        internal static int Price(Hero hero) => Math.Max(0,
            Campaign.Current.Models.RansomValueCalculationModel.PrisonerRansomValue(hero.CharacterObject, Hero.MainHero));

        internal static SaleResult SellAll()
        {
            var result = new SaleResult();
            if (!CanUse()) return result;
            var captives = Collect();
            var locks = GetLocks();
            var main = PartyBase.MainParty;
            _selling = true;
            try
            {
                foreach (var captive in captives)
                {
                    var hero = captive.Hero;
                    if (!IsEligible(hero, captive.Source, locks)) continue;
                    int price = Price(hero);
                    if (price <= 0 || price > (long)int.MaxValue - Hero.MainHero.Gold) continue;
                    int before = Hero.MainHero.Gold;
                    try
                    {
                        // Native transfer updates the actual captor and both rosters. Use the main
                        // party as seller so the same tavern valuation, payout and skill events apply.
                        if (captive.Source != main)
                            TransferPrisonerAction.Apply(hero.CharacterObject, captive.Source, main);
                        if (!hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != main ||
                            main.PrisonRoster.GetTroopCount(hero.CharacterObject) <= 0) continue;
                        var selected = TroopRoster.CreateDummyTroopRoster();
                        selected.AddToCounts(hero.CharacterObject, 1);
                        // Same null buyer as native tavern ransom: release instead of depositing
                        // in the town dungeon, preventing resale of the same captive.
                        SellPrisonersAction.ApplyForSelectedPrisoners(main, null, selected);
                        if (!hero.IsPrisoner) result.Sold++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed = true;
                        if (!hero.IsPrisoner) result.Sold++;
                        // Restore a transferred but unsold captive. Never repeat a native sale
                        // whose events may already have released the hero or paid its proceeds.
                        if (captive.Source != main && hero.IsPrisoner && hero.PartyBelongedToAsPrisoner == main &&
                            main.PrisonRoster.GetTroopCount(hero.CharacterObject) > 0 && IsOwnedSource(captive.Source))
                        {
                            try { TransferPrisonerAction.Apply(hero.CharacterObject, main, captive.Source); }
                            catch (Exception restore) { Debug.Print("[CalradiaComfort] Captive restore failed: " + restore); }
                        }
                        Debug.Print("[CalradiaComfort] Lord ransom interrupted: " + ex);
                    }
                    finally { result.Gold += Math.Max(0L, (long)Hero.MainHero.Gold - before); }
                    if (result.Failed) break;
                }
            }
            finally { _selling = false; }
            return result;
        }
    }

    internal sealed class SellClanLordPrisonersCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents() => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddMenus);
        public override void SyncData(IDataStore dataStore) { }

        private void AddMenus(CampaignGameStarter starter)
        {
            // v1.4.8's tavern district is town_backstreet (the town_tavern option enters its scene).
            starter.AddGameMenuOption("town_backstreet", "comfort_sell_all_lord_prisoners",
                "卖出所有俘虏领主（{COMFORT_LORD_COUNT} 人，{COMFORT_LORD_GOLD} 第纳尔）",
                CanSell, Sell, false, 2);
        }

        private bool CanSell(MenuCallbackArgs args)
        {
            if (!SellClanLordPrisoners.CanUse()) return false;
            var captives = SellClanLordPrisoners.Collect();
            long gold = 0;
            foreach (var captive in captives) gold += SellClanLordPrisoners.Price(captive.Hero);
            args.Text.SetTextVariable("COMFORT_LORD_COUNT", captives.Count);
            args.Text.SetTextVariable("COMFORT_LORD_GOLD", gold.ToString());
            args.optionLeaveType = GameMenuOption.LeaveType.Ransom;
            args.IsEnabled = captives.Count > 0 && gold > 0 && gold <= (long)int.MaxValue - Hero.MainHero.Gold;
            args.Tooltip = new TextObject(captives.Count == 0 ? "玩家家族没有可出售的俘虏领主；锁定及不可转移的角色保留。" :
                gold > (long)int.MaxValue - Hero.MainHero.Gold ? "本次赎金会超出角色金币上限，暂时无法全部出售。" :
                "统一赎回主角、家族部队与家族领地地牢的俘虏领主，赎金归主角。普通士兵俘虏保留。");
            return true;
        }

        private void Sell(MenuCallbackArgs args)
        {
            var result = SellClanLordPrisoners.SellAll();
            InformationManager.DisplayMessage(new InformationMessage("已售出 " + result.Sold +
                " 名俘虏领主，获得 " + result.Gold + " 第纳尔。" +
                (result.Failed ? "部分交易中断，其余俘虏未出售。" : "")));
            GameMenu.SwitchToMenu("town_backstreet");
        }
    }
}
