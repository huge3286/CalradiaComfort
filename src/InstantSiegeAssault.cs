using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Siege;

namespace StorySkip
{
    internal static class InstantSiegeAssault
    {
        private delegate void CreateObject(SiegeEvent siege, SiegeEvent.SiegeEngineConstructionProgress progress, ISiegeEventSide side);
        private static readonly CreateObject CompleteNative = AccessTools.MethodDelegate<CreateObject>(
            AccessTools.Method(typeof(SiegeEvent), "CreateSiegeObject"));

        internal static void InitializePatches(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(BesiegerCamp), nameof(BesiegerCamp.IsPreparationComplete)),
                prefix: new HarmonyMethod(typeof(InstantSiegeAssault), nameof(BeforePreparationQuery)));
            harmony.Patch(AccessTools.Method(typeof(BesiegerCamp), nameof(BesiegerCamp.InitializeSiegeEventSide)),
                postfix: new HarmonyMethod(typeof(InstantSiegeAssault), nameof(BeforePreparationQuery)));
        }

        private static void BeforePreparationQuery(BesiegerCamp __instance)
        {
            if (!ComfortFeatures.InstantSiegeAssaultEnabled || Campaign.Current == null || __instance == null ||
                MobileParty.MainParty == null || !ReferenceEquals(__instance.LeaderParty, MobileParty.MainParty)) return;
            var progress = __instance.SiegeEngines?.SiegePreparations;
            if (__instance.SiegeEvent == null || progress == null || progress.Progress >= 1f) return;
            progress.SetProgress(1f);
            // Match normal completion: dispatch SiegeEngineBuilt and refresh the settlement visual once.
            CompleteNative(__instance.SiegeEvent, progress, __instance);
        }
    }
}
