using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ModuleManager;

namespace StorySkip
{
    // Keep DLC types behind guarded calls so the base-game JIT never needs NavalDLC.dll.
    internal static class NavalSupport
    {
        // Enabled modules are fixed for the lifetime of the game process.
        private static bool? _active;
        internal static bool IsActive => _active ?? (_active = ModuleHelper.IsModuleActive("NavalDLC")).Value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Initialize(Harmony harmony) => StorySkipNaval.InitializePatches(harmony);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Apply(ref int stage) => StorySkipNaval.Apply(ref stage);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool GrantCrewReward() => StorySkipNaval.TryGrantCrewReward();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool SisterComesOfAge(Hero hero) => StorySkipNaval.OnSisterComesOfAge(hero);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool EnsureSisterAdultState() => StorySkipNaval.EnsureSisterAdultState();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static MethodInfo FindSpeedMethod(Type[] parameters) =>
            AccessTools.Method(typeof(NavalDLC.GameComponents.NavalDLCPartySpeedCalculationModel), "CalculateFinalSpeed", parameters);
    }
}
