using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SandBox.CampaignBehaviors;
using StoryMode;
using StoryMode.GameComponents.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    /// <summary>
    /// Removes the module/cheat eligibility gate from the vanilla StoryMode
    /// achievement behavior. Objectives, platform calls and counters remain
    /// vanilla. The saved disqualification flag is deliberately preserved so
    /// disabling the setting restores the original campaign eligibility.
    /// </summary>
    internal static class EnableAchievements
    {
        private sealed class SessionState
        {
            internal bool ListenersRegistered;
            internal bool InitializationStarted;
            internal bool ServiceDisconnected;
            internal bool HasObservedSetting;
            internal bool LastEnabled;
        }

        private static readonly ConditionalWeakTable<AchievementsCampaignBehavior,
            SessionState> Sessions = new ConditionalWeakTable<
                AchievementsCampaignBehavior, SessionState>();
        private static readonly FieldInfo DeactivatedField = AccessTools.Field(
            typeof(AchievementsCampaignBehavior), "_deactivateAchievements");
        private static readonly MethodInfo VersionIntegrityMethod = AccessTools.Method(
            typeof(DumpIntegrityCampaignBehavior), "CheckIfVersionIntegrityIsAchieved",
            new[] { typeof(string).MakeByRefType() });
        private static readonly MethodInfo LoadFinishedMethod = AccessTools.Method(
            typeof(AchievementsCampaignBehavior), "OnGameLoadFinished");
        private static readonly MethodInfo ConfigChangedMethod = AccessTools.Method(
            typeof(AchievementsCampaignBehavior), "OnConfigChanged");

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo activity = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "CheckAchievementSystemActivity", new[] { typeof(TextObject).MakeByRefType() });
            MethodInfo setStat = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "SetStatInternal", new[] { typeof(string), typeof(int) });
            MethodInfo metadata = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "CollectMetadataEntries");
            MethodInfo deactivate = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "DeactivateAchievements", new[] { typeof(TextObject), typeof(bool), typeof(bool) });
            MethodInfo initialize = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "CacheAndInitializeAchievementVariables");
            MethodInfo register = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "RegisterEvents");
            MethodInfo tutorial = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "CheckTutorialFinished");
            MethodInfo banner = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "ProgressAssembledDragonBanner");
            MethodInfo victory = AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "ProgressImperialBarbarianVictory");

            if (DeactivatedField == null || VersionIntegrityMethod == null ||
                LoadFinishedMethod == null || ConfigChangedMethod == null ||
                activity == null || setStat == null || metadata == null ||
                deactivate == null || initialize == null || register == null ||
                tutorial == null || banner == null || victory == null)
                throw new MissingMemberException(
                    "Enable Achievements hooks changed in this Bannerlord version.");

            harmony.Patch(activity, prefix: new HarmonyMethod(typeof(EnableAchievements),
                nameof(BeforeCheckActivity)));
            var eligibilityRead = new HarmonyMethod(typeof(EnableAchievements),
                nameof(ReplaceDisabledFlagRead));
            harmony.Patch(setStat, transpiler: eligibilityRead);
            harmony.Patch(metadata, transpiler: eligibilityRead);
            harmony.Patch(deactivate, postfix: new HarmonyMethod(typeof(EnableAchievements),
                nameof(AfterDeactivated)));
            harmony.Patch(initialize, prefix: new HarmonyMethod(typeof(EnableAchievements),
                nameof(BeforeInitialization)));
            harmony.Patch(register,
                postfix: new HarmonyMethod(typeof(EnableAchievements), nameof(AfterRegistered)),
                transpiler: new HarmonyMethod(typeof(EnableAchievements),
                    nameof(GuardStoryEventSubscriptions)));
            var storyOnly = new HarmonyMethod(typeof(EnableAchievements), nameof(HasStoryMode));
            harmony.Patch(tutorial, prefix: storyOnly);
            harmony.Patch(banner, prefix: storyOnly);
            harmony.Patch(victory, prefix: storyOnly);
        }

        internal static void AddMissingAchievementBehavior(CampaignGameStarter starter)
        {
            // Sandbox does not add the vanilla behavior itself. Supply the same
            // objective implementation, with its story-only callbacks guarded.
            if (!starter.CampaignBehaviors.Any(behavior =>
                behavior is AchievementsCampaignBehavior))
                starter.AddBehavior(new AchievementsCampaignBehavior());
        }

        private static bool HasStoryMode()
        {
            return StoryModeManager.Current != null;
        }

        private static IEnumerable<CodeInstruction> GuardStoryEventSubscriptions(
            IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo tutorialEvent = AccessTools.PropertyGetter(typeof(StoryModeEvents),
                "OnStoryModeTutorialEndedEvent");
            MethodInfo bannerEvent = AccessTools.PropertyGetter(typeof(StoryModeEvents),
                "OnBannerPieceCollectedEvent");
            int first = code.FindIndex(instruction => instruction.Calls(tutorialEvent));
            int second = code.FindIndex(instruction => instruction.Calls(bannerEvent));
            if (first < 0 || second <= first || code.Last().opcode != OpCodes.Ret ||
                code.Skip(first).Count(instruction => instruction.opcode == OpCodes.Ret) != 1)
                throw new MissingMemberException(
                    "Enable Achievements story event subscriptions changed.");

            // These are the two final subscriptions in vanilla RegisterEvents.
            // Keep every general campaign subscription unchanged, and register
            // story events only in a campaign that actually owns StoryModeEvents.
            for (int index = 0; index < first; index++)
                yield return code[index];
            var loadBehavior = new CodeInstruction(OpCodes.Ldarg_0);
            loadBehavior.labels.AddRange(code[first].labels);
            loadBehavior.blocks.AddRange(code[first].blocks);
            yield return loadBehavior;
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(
                typeof(EnableAchievements), nameof(RegisterStoryEvents)));
            yield return new CodeInstruction(OpCodes.Ret);
        }

        private static void RegisterStoryEvents(AchievementsCampaignBehavior behavior)
        {
            if (!HasStoryMode())
                return;
            var tutorial = (Action)AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "CheckTutorialFinished").CreateDelegate(typeof(Action), behavior);
            var banner = (Action)AccessTools.Method(typeof(AchievementsCampaignBehavior),
                "ProgressAssembledDragonBanner").CreateDelegate(typeof(Action), behavior);
            StoryModeEvents.OnStoryModeTutorialEndedEvent.AddNonSerializedListener(
                behavior, tutorial);
            StoryModeEvents.OnBannerPieceCollectedEvent.AddNonSerializedListener(
                behavior, banner);
        }

        private static SessionState State(AchievementsCampaignBehavior behavior)
        {
            return Sessions.GetValue(behavior, _ => new SessionState());
        }

        private static bool BeforeCheckActivity(AchievementsCampaignBehavior __instance,
            ref TextObject __0, ref bool __result)
        {
            if (!ComfortFeatures.AchievementsEnabled)
                return true;

            // Keep the original version-downgrade and platform-service restrictions.
            // Only the checks for unofficial modules, cheats and their old saved
            // disqualification flag are overridden.
            __result = CanOverrideIntegrity() && !State(__instance).ServiceDisconnected;
            __0 = __result ? null : new TextObject(
                "成就暂不可用：存档版本回退或平台成就服务未连接。");
            return false;
        }

        private static IEnumerable<CodeInstruction> ReplaceDisabledFlagRead(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(typeof(EnableAchievements),
                nameof(IsDisabled));
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld &&
                    Equals(instruction.operand, DeactivatedField))
                {
                    // Preserve labels and exception blocks on the original instruction.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replacements++;
                }
                yield return instruction;
            }
            if (replacements != 1)
                throw new MissingMemberException(
                    "Enable Achievements expected one achievement eligibility read.");
        }

        private static bool IsDisabled(AchievementsCampaignBehavior behavior)
        {
            if (ComfortFeatures.AchievementsEnabled)
                return !CanOverrideIntegrity() || State(behavior).ServiceDisconnected;

            if ((bool)DeactivatedField.GetValue(behavior))
                return true;

            // Apply the original gate immediately when the live setting is switched
            // off, even before the next campaign tick removes its event listeners.
            TextObject reason;
            return Campaign.Current == null ||
                   Campaign.Current.GetCampaignBehavior<DumpIntegrityCampaignBehavior>() == null ||
                   !DumpIntegrityCampaignBehavior.IsGameIntegrityAchieved(out reason);
        }

        private static bool CanOverrideIntegrity()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null ||
                campaign.GetCampaignBehavior<DumpIntegrityCampaignBehavior>() == null)
                return false;

            object[] versionReason = { null };
            return (bool)VersionIntegrityMethod.Invoke(null, versionReason);
        }

        private static void AfterRegistered(AchievementsCampaignBehavior __instance)
        {
            State(__instance).ListenersRegistered = true;
        }

        private static void BeforeInitialization(AchievementsCampaignBehavior __instance)
        {
            State(__instance).InitializationStarted = true;
        }

        private static void AfterDeactivated(AchievementsCampaignBehavior __instance,
            bool __2)
        {
            SessionState state = State(__instance);
            state.ListenersRegistered = false;
            state.InitializationStarted = false;
            // The vanilla third argument marks a temporary service disconnection.
            // Allow that path to remove its listeners and retry only after a load
            // or an explicit off/on toggle, as the vanilla game does on reload.
            state.ServiceDisconnected |= __2;
        }

        internal static void RefreshLiveSetting()
        {
            Campaign campaign = Campaign.Current;
            AchievementsCampaignBehavior behavior = campaign?
                .GetCampaignBehavior<AchievementsCampaignBehavior>();
            if (behavior == null)
                return;

            SessionState state = State(behavior);
            bool enabled = ComfortFeatures.AchievementsEnabled;
            bool newlyEnabled = state.HasObservedSetting && !state.LastEnabled && enabled;
            state.HasObservedSetting = true;
            state.LastEnabled = enabled;

            if (!enabled)
            {
                if (state.ListenersRegistered)
                    ConfigChangedMethod.Invoke(behavior, null);
                return;
            }

            if (newlyEnabled)
                state.ServiceDisconnected = false;
            if (state.ServiceDisconnected || !CanOverrideIntegrity())
                return;

            if (!state.ListenersRegistered)
            {
                // Vanilla deactivation removed every listener, including the load
                // and configuration handlers. Restore them once, without stacking
                // duplicate listeners or replaying achievement objectives.
                CampaignEventDispatcher.Instance.RemoveListeners(behavior);
                behavior.RegisterEvents();
            }
            if (!state.InitializationStarted)
                LoadFinishedMethod.Invoke(behavior, null);
        }
    }

    internal sealed class EnableAchievementsCampaignBehavior : CampaignBehaviorBase
    {
        private float _refreshDelay;
        private bool _reportedError;

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, Refresh);
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(
                this, OnNewGameReady);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // The live setting is global. No saved eligibility flags are rewritten.
        }

        private void OnNewGameReady(CampaignGameStarter starter)
        {
            Refresh();
        }

        private void OnTick(float dt)
        {
            _refreshDelay -= dt;
            if (_refreshDelay > 0f)
                return;
            _refreshDelay = 1f;
            Refresh();
        }

        private void Refresh()
        {
            try
            {
                EnableAchievements.RefreshLiveSetting();
            }
            catch (Exception ex)
            {
                if (_reportedError)
                    return;
                _reportedError = true;
                Debug.Print("[CalradiaComfort] Achievement eligibility refresh failed: " + ex);
            }
        }
    }
}
