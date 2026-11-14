using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace StorySkip
{
    internal static class ClanCompanionLimit
    {
        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            var limit = AccessTools.Method(typeof(DefaultClanTierModel), nameof(DefaultClanTierModel.GetCompanionLimit),
                new[] { typeof(Clan) });
            if (limit == null) throw new MissingMethodException("The native companion limit model changed in this Bannerlord version.");
            harmony.Patch(limit, postfix: new HarmonyMethod(typeof(ClanCompanionLimit), nameof(AfterCompanionLimit)));
        }

        private static void AfterCompanionLimit(Clan __0, ref int __result)
        {
            if (__0 == null || Campaign.Current == null || !ComfortFeatures.ClanCompanionLimitEnabled) return;
            Clan playerClan = Clan.PlayerClan;
            if (playerClan != null && ReferenceEquals(__0, playerClan))
                __result = ComfortFeatures.ClanCompanionLimitValue;
        }
    }
}
