using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NavalDLC.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class AlliedPartySpeed
    {
        private static readonly TextObject SpeedReason = new TextObject("{=comfort_wind_breath}风之气息");
        private static ConditionalWeakTable<MobileParty, CachedBonus> _speedCache =
            new ConditionalWeakTable<MobileParty, CachedBonus>();

        private sealed class CachedBonus
        {
            internal float Value;
        }

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            Type[] finalParameters = { typeof(MobileParty), typeof(ExplainedNumber) };
            Patch(harmony, AccessTools.Method(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed", finalParameters),
                nameof(AfterFinalSpeed));
            Patch(harmony, AccessTools.Method(typeof(NavalDLCPartySpeedCalculationModel), "CalculateFinalSpeed", finalParameters),
                nameof(AfterFinalSpeed));
            Patch(harmony, AccessTools.Method(typeof(MobileParty), "IsLastSpeedCacheInvalid", Type.EmptyTypes),
                nameof(AfterSpeedCacheCheck));
            Patch(harmony, AccessTools.Method(typeof(MobileParty), "CalculateSpeedForPartyUnified", Type.EmptyTypes),
                nameof(AfterActualSpeedUpdated));
            Patch(harmony, AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.SpeedExplained)),
                nameof(AfterActualSpeedUpdated));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase target, string postfix)
        {
            if (target == null)
                throw new MissingMethodException("The native map movement speed API changed in this Bannerlord version.");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(AlliedPartySpeed), postfix));
        }

        private static void AfterFinalSpeed(PartySpeedModel __instance, MobileParty __0, ref ExplainedNumber __result)
        {
            // War Sails delegates to the land model before its own modifiers and
            // cap. Only the outer active model may apply the fixed bonus.
            if (!ReferenceEquals(__instance, Campaign.Current?.Models?.PartySpeedCalculatingModel)) return;
            float bonus = GetEffectiveBonus(__0);
            if (bonus <= 0f) return;

            // Adding to an existing ExplainedNumber would scale the bonus by
            // its percentage modifiers. Copy the already evaluated native
            // result and its explanation, then add the requested fixed amount.
            var boosted = new ExplainedNumber(0f, __result.IncludeDescriptions);
            boosted.AddFromExplainedNumber(__result, null);
            boosted.Add(bonus, SpeedReason);
            __result = boosted;
        }

        private static float GetEffectiveBonus(MobileParty party)
        {
            if (party == null || Campaign.Current == null || !ComfortFeatures.AlliedPartySpeedEnabled) return 0f;
            float bonus = ComfortFeatures.AlliedPartySpeedBonus;
            if (bonus <= 0f) return 0f;
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null) return 0f;

            try
            {
                IFaction playerFaction = playerClan.MapFaction;
                return playerFaction != null && ReferenceEquals(party.MapFaction, playerFaction) ? bonus : 0f;
            }
            catch (NullReferenceException)
            {
                // A party's owner or home settlement may be unavailable while
                // native objects are loading or being removed from the map.
                return 0f;
            }
        }

        private static void AfterSpeedCacheCheck(MobileParty __instance, ref bool __result)
        {
            if (__result || __instance == null || Campaign.Current == null) return;
            float previous = _speedCache.TryGetValue(__instance, out CachedBonus cached) ? cached.Value : 0f;
            if (previous != GetEffectiveBonus(__instance)) __result = true;
        }

        private static void AfterActualSpeedUpdated(MobileParty __instance)
        {
            if (__instance == null || Campaign.Current == null) return;
            // Commit only where MobileParty actually updates its speed cache.
            // Recruitment also calls the final model for predictions; those
            // calculations must not mark a still-old movement cache as fresh.
            _speedCache.GetValue(__instance, CreateCachedBonus).Value = GetEffectiveBonus(__instance);
        }

        private static CachedBonus CreateCachedBonus(MobileParty party) => new CachedBonus();

        internal static void ClearSession()
        {
            _speedCache = new ConditionalWeakTable<MobileParty, CachedBonus>();
        }
    }
}
