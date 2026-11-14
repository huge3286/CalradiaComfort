using System;
using HarmonyLib;
using SandBox;

namespace StorySkip
{
    internal static class ComfortBranding
    {
        internal const string DisplayName = "舒适卡拉迪亚 / Calradia Comfort";

        internal static void InitializePatches(Harmony harmony)
        {
            // The save screen resolves module IDs independently of SubModule.xml.
            // Keep the saved ID stable and change only its human-readable name.
            harmony.Patch(
                AccessTools.Method(typeof(SandBoxSaveHelper),
                    "GetModuleNameFromModuleId", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(ComfortBranding),
                    nameof(AfterModuleNameResolved)));
        }

        private static void AfterModuleNameResolved(string __0, ref string __result)
        {
            if (string.Equals(__0, "StorySkip", StringComparison.Ordinal))
            {
                __result = DisplayName;
            }
        }
    }
}
