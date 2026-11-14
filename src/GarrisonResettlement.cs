using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class GarrisonResettlement
    {
        private static readonly HashSet<MobileParty> Processing = new HashSet<MobileParty>();

        internal static void InitializePatches(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(DesertionCampaignBehavior), "CheckDesertionForParty",
                new[] { typeof(MobileParty) });
            if (target == null) throw new MissingMethodException("Native desertion hook changed.");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(GarrisonResettlement), nameof(BeforeDesertion)));
        }

        internal static Settlement GetEligibleSettlement(MobileParty party)
        {
            if (!ComfortFeatures.GarrisonResettlementEnabled || Campaign.Current == null ||
                party == null || !party.IsActive || !party.IsGarrison || party.IsDisbanding ||
                party.Party == null || party.Party.MapEvent != null || Clan.PlayerClan == null)
                return null;
            var settlement = party.GarrisonPartyComponent?.Settlement;
            return settlement != null && settlement.IsFortification && !settlement.IsUnderSiege &&
                settlement.OwnerClan == Clan.PlayerClan ? settlement : null;
        }

        private static void BeforeDesertion(MobileParty __0)
        {
            var settlement = GetEligibleSettlement(__0);
            if (settlement == null || !Processing.Add(__0)) return;
            try
            {
                var villages = settlement.BoundVillages.Where(v => v != null && v.Bound == settlement &&
                    v.Settlement?.OwnerClan == Clan.PlayerClan).ToList();
                int moved = ResettleOverflow(__0.MemberRoster, __0.Party.PartySizeLimit, villages);
                if (moved > 0)
                    InformationManager.DisplayMessage(new InformationMessage(
                        settlement.Name + "：" + moved + " 名超额驻军退役，直属村庄合计增加 " + moved + " 户。"));
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Garrison resettlement interrupted: " + ex);
            }
            finally { Processing.Remove(__0); }
            // Native wage and morale desertion still runs after excess headcount is removed.
        }

        internal static int ResettleOverflow(TroopRoster roster, int capacity, IList<Village> destinations)
        {
            if (roster == null || destinations == null) return 0;
            int excess = Math.Max(0, roster.TotalManCount - Math.Max(0, capacity));
            if (excess == 0) return 0;
            var villages = destinations.Where(v => v != null && !float.IsNaN(v.Hearth) &&
                !float.IsInfinity(v.Hearth) && v.Hearth >= 0 && v.Hearth < 10000000f)
                .Distinct().OrderBy(v => v.Hearth).ThenBy(v => v.StringId, StringComparer.Ordinal).ToList();
            if (villages.Count == 0) return 0;

            // Snapshot before mutating native roster indices. Heroes never retire through this feature.
            var troops = roster.GetTroopRoster().Where(e => e.Character != null &&
                !e.Character.IsHero && e.Number > 0).OrderBy(e => e.Character.Tier)
                .ThenBy(e => e.Character.Level).ThenBy(e => e.Character.StringId, StringComparer.Ordinal).ToList();
            int count = Math.Min(excess, troops.Sum(e => e.Number));
            if (count == 0) return 0;
            // Whole households, evenly split; any remainder goes to the poorer villages first.
            int each = count / villages.Count;
            int remainder = count % villages.Count;
            for (int i = 0; i < villages.Count; i++)
                if ((double)villages[i].Hearth + each + (i < remainder ? 1 : 0) >= 10000000d)
                    return 0;

            int moved = 0;
            try
            {
                foreach (var entry in troops)
                {
                    int take = Math.Min(count - moved, entry.Number);
                    if (take == 0) break;
                    // Retire healthy soldiers first within a troop type, then its wounded if necessary.
                    int wounded = Math.Max(0, take - (entry.Number - entry.WoundedNumber));
                    int xp = (int)((long)entry.Xp * take / entry.Number);
                    int before = roster.GetTroopCount(entry.Character);
                    try { roster.AddToCounts(entry.Character, -take, false, -wounded, -xp); }
                    finally { moved += Math.Max(0, before - roster.GetTroopCount(entry.Character)); }
                }
            }
            finally
            {
                // Native roster callbacks may throw after applying a removal. Credit actual removals
                // even in that case, so an interrupted conversion cannot simply lose the soldiers.
                for (int i = 0; i < villages.Count; i++)
                    villages[i].Hearth += moved / villages.Count + (i < moved % villages.Count ? 1 : 0);
            }
            return moved;
        }
    }
}
