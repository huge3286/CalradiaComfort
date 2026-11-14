using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace StorySkip
{
    internal static class ClanPartyLimit
    {
        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            var limit = AccessTools.Method(typeof(DefaultClanTierModel), nameof(DefaultClanTierModel.GetPartyLimitForTier),
                new[] { typeof(Clan), typeof(int) });
            if (limit == null)
                throw new MissingMethodException("The native clan party limit model changed in this Bannerlord version.");
            harmony.Patch(limit, postfix: new HarmonyMethod(typeof(ClanPartyLimit), nameof(AfterPartyLimit)));
        }

        private static void AfterPartyLimit(Clan __0, ref int __result)
        {
            if (__0 == null || Campaign.Current == null || !ComfortFeatures.ClanPartyLimitEnabled) return;
            Clan playerClan = Clan.PlayerClan;
            if (playerClan != null && ReferenceEquals(__0, playerClan))
                __result = ComfortFeatures.ClanPartyLimitValue;
        }
    }
}
