using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class QuietRelationNotifications
    {
        internal static void InitializePatches(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(DefaultNotificationsCampaignBehavior), "OnRelationChanged");
            if (target == null) throw new MissingMethodException("Native relation notification handler changed.");
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(QuietRelationNotifications), nameof(RouteRelationMessage)));
        }

        private static IEnumerable<CodeInstruction> RouteRelationMessage(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(MBInformationManager), nameof(MBInformationManager.AddQuickInformation),
                new[] { typeof(TextObject), typeof(int), typeof(BasicCharacterObject), typeof(Equipment), typeof(string) });
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    count++;
                    // Keep native player/visibility guards, localized message and final relation value.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(QuietRelationNotifications), nameof(ShowRelationMessage));
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native relation popup, found " + count);
        }

        private static void ShowRelationMessage(TextObject message, int extraTimeInMs,
            BasicCharacterObject announcerCharacter, Equipment equipment, string soundEventPath)
        {
            if (ComfortFeatures.QuietRelationNotificationsEnabled)
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            else
                MBInformationManager.AddQuickInformation(message, extraTimeInMs, announcerCharacter, equipment, soundEventPath);
        }
    }
}
