using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace StorySkip
{
    internal sealed class HeroLocationSnapshot
    {
        internal HeroLocationSnapshot(string description, Settlement linkSettlement = null)
        {
            Description = description;
            LinkSettlement = linkSettlement;
        }

        internal string Description { get; }
        internal Settlement LinkSettlement { get; }
    }

    internal static class HeroLiveLocation
    {
        internal static HeroLocationSnapshot Resolve(Hero hero)
        {
            if (hero == null) return new HeroLocationSnapshot("角色数据暂不可用");
            if (hero.IsDead) return new HeroLocationSnapshot("已死亡");
            if (hero.IsDisabled) return new HeroLocationSnapshot("已退出当前世界活动，无地图位置");
            if (hero.IsNotSpawned) return new HeroLocationSnapshot("尚未出现在地图上");

            // Captivity is authoritative. CurrentSettlement checks an ordinary
            // party first, so a leftover ordinary-party reference must not win.
            if (hero.IsPrisoner)
            {
                PartyBase captor = hero.PartyBelongedToAsPrisoner;
                if (captor?.Settlement != null)
                    return AtSettlement("被俘，关押于", captor.Settlement, "地牢");
                if (captor?.MobileParty != null && captor.MobileParty.IsActive)
                    return InParty(captor.MobileParty, true);
                return new HeroLocationSnapshot("被俘，当前关押位置暂不可用");
            }

            MobileParty party = hero.PartyBelongedTo;
            if (party != null && party.IsActive)
                return InParty(party, false);

            // These states may retain historical object references during
            // recovery. Do not report their home/last-seen site as a location.
            if (hero.IsFugitive) return new HeroLocationSnapshot("逃亡中，暂无活动地图位置");
            if (hero.IsReleased) return new HeroLocationSnapshot("已获释，等待重新出现在地图上");
            if (hero.IsTraveling) return new HeroLocationSnapshot("旅行中，暂无可定位的地图部队");
            if (hero.StayingInSettlement != null)
                return AtSettlement("停留于", hero.StayingInSettlement);
            return new HeroLocationSnapshot("当前未在聚落或活动部队中，暂无可定位的地图位置");
        }

        private static HeroLocationSnapshot AtSettlement(string state, Settlement settlement, string suffix = "")
        {
            return new HeroLocationSnapshot(state + " " + SettlementLink(settlement) + suffix, settlement);
        }

        private static HeroLocationSnapshot InParty(MobileParty party, bool prisoner)
        {
            string partyName = PartyNameWithLink(party);
            string state = prisoner ? "被 " + partyName + " 俘虏" : "随 " + partyName;
            Settlement current = party.CurrentSettlement;
            if (current != null)
                return AtSettlement(state + "，位于", current);

            // Use the actual map event/besieger site only as the current action
            // location. Neither query mutates visibility or last-seen records.
            if (!prisoner && party.MapEvent != null)
            {
                Settlement battleSite = party.MapEvent.MapEventSettlement;
                if (battleSite != null)
                {
                    string activity = party.MapEvent.IsRaid ? "，参与村庄袭击战事，位于" : "，交战中，位于";
                    return AtSettlement(state + activity, battleSite);
                }
                state += "，交战中";
            }
            if (!prisoner && party.BesiegedSettlement != null)
                return AtSettlement(state + "，围攻", party.BesiegedSettlement);

            Settlement nearest = FindNearestSettlement(party.Position);
            string movement = party.IsCurrentlyAtSea ? "海上航行" : "地图活动中";
            if (nearest != null)
                return new HeroLocationSnapshot(state + "，" + movement + "（" + SettlementLink(nearest) +
                    (party.IsCurrentlyAtSea ? "附近海域）" : "附近）"), nearest);
            return new HeroLocationSnapshot(state + "，" + movement + "，附近聚落暂不可用");
        }

        internal static Settlement FindNearestSettlement(CampaignVec2 position)
        {
            if (Campaign.Current == null || !IsFinite(position)) return null;
            Settlement nearest = null;
            float minimum = float.PositiveInfinity;
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage) ||
                    !IsFinite(settlement.Position))
                    continue;
                float distance = position.DistanceSquared(settlement.Position);
                if (!float.IsNaN(distance) && distance < minimum)
                {
                    minimum = distance;
                    nearest = settlement;
                }
            }
            return nearest;
        }

        private static bool IsFinite(CampaignVec2 position)
        {
            return !float.IsNaN(position.X) && !float.IsNaN(position.Y) &&
                   !float.IsInfinity(position.X) && !float.IsInfinity(position.Y);
        }

        private static string SettlementLink(Settlement settlement)
        {
            return settlement.EncyclopediaLinkWithName?.ToString() ?? settlement.Name?.ToString() ?? "未命名聚落";
        }

        private static string PartyNameWithLink(MobileParty party)
        {
            string name = party.Name?.ToString() ?? "未命名部队";
            Hero leader = party.LeaderHero;
            string leaderName = leader?.Name?.ToString();
            string leaderLink = leader?.EncyclopediaLinkWithName?.ToString();
            if (string.IsNullOrEmpty(leaderName) || string.IsNullOrEmpty(leaderLink))
                return name;
            int index = name.IndexOf(leaderName, StringComparison.Ordinal);
            if (index >= 0)
                return name.Substring(0, index) + leaderLink + name.Substring(index + leaderName.Length);
            // A custom party name need not include its current leader. Keep
            // that name and identify the actual leader rather than the owner.
            return name + "（领队：" + leaderLink + "）";
        }
    }
}
