using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace StorySkip
{
    internal static class SharedTroopExperience
    {
        private sealed class ScreenTransaction
        {
            internal PartyScreenLogic Logic;
            internal PartyBase Party;
            internal TroopRoster Roster;
            internal PartyScreenLogic.PartyRosterSide Side;
            internal long Earned;
            internal long Spent;
            internal long SavedEarned;
            internal long SavedSpent;
        }

        private static ConditionalWeakTable<PartyScreenLogic, ScreenTransaction> _screens =
            new ConditionalWeakTable<PartyScreenLogic, ScreenTransaction>();
        private static ConditionalWeakTable<TroopRoster, ScreenTransaction> _screenRosters =
            new ConditionalWeakTable<TroopRoster, ScreenTransaction>();
        private static SharedTroopExperienceCampaignBehavior _behavior;
        [ThreadStatic] private static int _suppressCollection;
        [ThreadStatic] private static PartyScreenLogic _commandLogic;

        private static readonly MethodInfo NativeGetXp = AccessTools.Method(typeof(TroopRoster),
            nameof(TroopRoster.GetElementXp), new[] { typeof(int) });
        private static readonly MethodInfo NativeSetXp = AccessTools.Method(typeof(TroopRoster),
            nameof(TroopRoster.SetElementXp), new[] { typeof(int), typeof(int) });

        internal static bool IsActive => _behavior != null && _behavior.Ready &&
            ComfortFeatures.SharedTroopExperienceEnabled && Campaign.Current != null && PartyBase.MainParty != null;

        internal static void InitializePatches(Harmony harmony)
        {
            Patch(harmony, typeof(TroopRoster), nameof(TroopRoster.AddXpToTroopAtIndex),
                nameof(BeforeAddXpAtIndex));
            Patch(harmony, typeof(TroopRoster), nameof(TroopRoster.SetElementXp),
                nameof(BeforeSetElementXp));
            Patch(harmony, typeof(MobilePartyHelper), nameof(MobilePartyHelper.PartyAddSharedXp),
                nameof(BeforePartyAddSharedXp));
            Patch(harmony, typeof(MapEventParty), "CommitXpGain",
                transpiler: nameof(CollectBattleOverflow));
            Patch(harmony, typeof(PartyScreenLogic), nameof(PartyScreenLogic.Initialize),
                nameof(BeforeScreenInitialized));
            harmony.Patch(AccessTools.Method(typeof(PartyScreenLogic), "ProcessCommand"),
                prefix: new HarmonyMethod(typeof(SharedTroopExperience), nameof(BeforeScreenCommand)),
                finalizer: new HarmonyMethod(typeof(SharedTroopExperience), nameof(AfterScreenCommand)));
            Patch(harmony, typeof(PartyScreenLogic), "ValidateCommand",
                nameof(BeforeValidateCommand), transpiler: nameof(ReadUpgradeExperience));
            Patch(harmony, typeof(PartyScreenLogic), "UpgradeTroop",
                transpiler: nameof(SpendUpgradeExperience));
            Patch(harmony, typeof(PartyScreenLogic), nameof(PartyScreenLogic.DoneLogic),
                nameof(BeforeScreenDone), nameof(AfterScreenDone));
            Patch(harmony, typeof(PartyScreenLogic), "ResetLogic", nameof(BeforeScreenReset));
            Patch(harmony, typeof(PartyScreenLogic), "SavePartyScreenData", nameof(BeforeScreenSaved));
            Patch(harmony, typeof(PartyScreenLogic), "ResetToLastSavedPartyScreenData",
                nameof(BeforeSavedScreenRestored));
            Patch(harmony, typeof(PartyScreenLogic), nameof(PartyScreenLogic.OnPartyScreenClosed),
                postfix: nameof(AfterScreenClosed));
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null,
            string postfix = null, string transpiler = null)
        {
            MethodInfo original = AccessTools.Method(type, method);
            if (original == null)
                throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(SharedTroopExperience), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(SharedTroopExperience), postfix),
                transpiler: transpiler == null ? null : new HarmonyMethod(typeof(SharedTroopExperience), transpiler));
        }

        internal static void BindCampaign(SharedTroopExperienceCampaignBehavior behavior)
        {
            _behavior = behavior;
            _screens = new ConditionalWeakTable<PartyScreenLogic, ScreenTransaction>();
            _screenRosters = new ConditionalWeakTable<TroopRoster, ScreenTransaction>();
        }

        internal static long GetAvailableXp(PartyScreenLogic logic = null)
        {
            long available = _behavior?.ExperiencePool ?? 0;
            ScreenTransaction transaction;
            if (logic != null && _screens.TryGetValue(logic, out transaction) &&
                transaction.Party == PartyBase.MainParty)
                available = Math.Max(0, AddSaturated(available, transaction.Earned) - transaction.Spent);
            return available;
        }

        internal static bool AppliesTo(PartyScreenLogic logic,
            PartyScreenLogic.PartyRosterSide side, PartyScreenLogic.TroopType type)
        {
            if (!IsActive || logic == null || type != PartyScreenLogic.TroopType.Member)
                return false;
            ScreenTransaction transaction;
            if (_screens.TryGetValue(logic, out transaction))
                return transaction.Party == PartyBase.MainParty && transaction.Side == side;
            return side == PartyScreenLogic.PartyRosterSide.Right && logic.RightOwnerParty == PartyBase.MainParty ||
                side == PartyScreenLogic.PartyRosterSide.Left && logic.LeftOwnerParty == PartyBase.MainParty;
        }

        private static bool IsRegular(TroopRoster roster, int index)
        {
            if (roster == null || index < 0 || index >= roster.Count)
                return false;
            CharacterObject character = roster.GetCharacterAtIndex(index);
            return character != null && !character.IsHero;
        }

        private static bool IsMainMemberRoster(TroopRoster roster)
        {
            return IsActive && ReferenceEquals(roster, PartyBase.MainParty.MemberRoster);
        }

        private static bool TryGetScreen(TroopRoster roster, out ScreenTransaction transaction)
        {
            transaction = null;
            return roster != null && _screenRosters.TryGetValue(roster, out transaction) &&
                transaction.Party == PartyBase.MainParty;
        }

        private static bool CanCollect(TroopRoster roster, int index)
        {
            if (!IsActive || _suppressCollection != 0 || !IsRegular(roster, index))
                return false;
            ScreenTransaction transaction;
            return IsMainMemberRoster(roster) || TryGetScreen(roster, out transaction);
        }

        private static long AddSaturated(long value, long addition)
        {
            return addition > long.MaxValue - value ? long.MaxValue : value + addition;
        }

        private static void AddExperience(TroopRoster roster, long amount)
        {
            if (amount <= 0)
                return;
            ScreenTransaction transaction;
            if (TryGetScreen(roster, out transaction) &&
                (!ReferenceEquals(roster, transaction.Party.MemberRoster) || _commandLogic == transaction.Logic))
                transaction.Earned = AddSaturated(transaction.Earned, amount);
            else
                _behavior.ExperiencePool = AddSaturated(_behavior.ExperiencePool, amount);
        }

        private static bool BeforeAddXpAtIndex(TroopRoster __instance, int __0, int __1)
        {
            if (!CanCollect(__instance, __0))
                return true;
            // AddXpToTroop calls this leaf method. Suppress it after collecting the
            // award so its SetElementXp call cannot collect the same award twice.
            if (__1 > 0)
                AddExperience(__instance, __1);
            return false;
        }

        private static void BeforeSetElementXp(TroopRoster __instance, int __0, ref int __1)
        {
            if (!CanCollect(__instance, __0))
                return;
            // Native regular XP is always empty once pooled. This also catches
            // AddToCountsAtIndex when battle/recruitment rosters are merged.
            if (__1 > 0)
                AddExperience(__instance, __1);
            __1 = 0;
        }

        private static bool BeforePartyAddSharedXp(MobileParty __0, float __1)
        {
            if (!IsActive || __0 == null || __0.Party != PartyBase.MainParty ||
                __0.MemberRoster.TotalRegulars == 0)
                return true;
            // The native distributor drops XP when no stack has room. The bank
            // receives that one award directly, including with only elite troops.
            if (__1 > 0 && !float.IsNaN(__1) && !float.IsInfinity(__1))
                AddExperience(__0.MemberRoster, __1 >= long.MaxValue ? long.MaxValue : (long)Math.Floor(__1));
            return false;
        }

        internal static void CollectStoredExperience()
        {
            if (!IsActive)
                return;
            TroopRoster roster = PartyBase.MainParty.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                if (!IsRegular(roster, i))
                    continue;
                int xp = roster.GetElementXp(i);
                if (xp > 0)
                {
                    AddExperience(roster, xp);
                    ClearXp(roster, i);
                }
            }
        }

        private static void ClearXp(TroopRoster roster, int index)
        {
            _suppressCollection++;
            try { roster.SetElementXp(index, 0); }
            finally { _suppressCollection--; }
        }

        private static void BeforeScreenInitialized(PartyScreenLogic __instance,
            PartyScreenLogicInitializationData __0)
        {
            RemoveScreen(__instance);
            if (!IsActive)
                return;
            PartyBase party = PartyBase.MainParty;
            TroopRoster roster;
            PartyScreenLogic.PartyRosterSide side;
            if (__0.RightOwnerParty == party)
            {
                side = PartyScreenLogic.PartyRosterSide.Right;
                roster = __0.RightMemberRoster;
            }
            else if (__0.LeftOwnerParty == party)
            {
                side = PartyScreenLogic.PartyRosterSide.Left;
                roster = __0.LeftMemberRoster;
            }
            else
                return;
            if (roster == null)
                return;
            CollectStoredExperience();
            // A supplied clone describes the existing party. Its XP was just
            // migrated from the real roster and must not become a second award.
            if (!ReferenceEquals(roster, party.MemberRoster))
                for (int i = 0; i < roster.Count; i++)
                    if (IsRegular(roster, i) && roster.GetElementXp(i) != 0)
                        ClearXp(roster, i);
            var transaction = new ScreenTransaction
            {
                Logic = __instance, Party = party, Roster = roster, Side = side
            };
            _screens.Add(__instance, transaction);
            _screenRosters.Remove(roster);
            _screenRosters.Add(roster, transaction);
        }

        private static bool BeforeValidateCommand(PartyScreenLogic __instance,
            PartyScreenLogic.PartyCommand __0, ref bool __result)
        {
            if (__0 == null || __0.Code != PartyScreenLogic.PartyCommandCode.UpgradeTroop ||
                !AppliesTo(__instance, __0.RosterSide, __0.Type))
                return true;
            CharacterObject troop = __0.Character;
            bool valid = troop != null && !troop.IsHero && __0.TotalNumber > 0 &&
                !__instance.IsTroopUpgradesDisabled && __0.UpgradeTarget >= 0 &&
                troop.UpgradeTargets != null && __0.UpgradeTarget < troop.UpgradeTargets.Length;
            if (valid)
            {
                int cost = troop.GetUpgradeXpCost(PartyBase.MainParty, __0.UpgradeTarget);
                long totalCost = (long)cost * __0.TotalNumber;
                // Native arithmetic uses int for its cost multiplication.
                valid = cost >= 0 && totalCost <= int.MaxValue;
            }
            if (valid)
                return true;
            __result = false;
            return false;
        }

        private static void BeforeScreenCommand(PartyScreenLogic __instance, out PartyScreenLogic __state)
        {
            __state = _commandLogic;
            _commandLogic = __instance;
        }

        private static Exception AfterScreenCommand(PartyScreenLogic __state, Exception __exception)
        {
            _commandLogic = __state;
            return __exception;
        }

        private static int GetUpgradeExperience(TroopRoster roster, int index)
        {
            ScreenTransaction transaction;
            if (IsActive && IsRegular(roster, index) && TryGetScreen(roster, out transaction))
                return (int)Math.Min(int.MaxValue, GetAvailableXp(transaction.Logic));
            return roster.GetElementXp(index);
        }

        private static void SetUpgradeExperience(TroopRoster roster, int index, int remaining)
        {
            ScreenTransaction transaction;
            if (IsActive && IsRegular(roster, index) && TryGetScreen(roster, out transaction))
            {
                long spent = (long)GetUpgradeExperience(roster, index) - remaining;
                if (spent < 0 || spent > GetAvailableXp(transaction.Logic))
                    throw new InvalidOperationException("Shared troop experience upgrade exceeds its budget.");
                // Debit at the native XP deduction, before its UI refresh delegate.
                transaction.Spent = AddSaturated(transaction.Spent, spent);
                return;
            }
            roster.SetElementXp(index, remaining);
        }

        private static IEnumerable<CodeInstruction> ReadUpgradeExperience(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceUpgradeCalls(instructions, false);
        }

        private static IEnumerable<CodeInstruction> SpendUpgradeExperience(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceUpgradeCalls(instructions, true);
        }

        private static IEnumerable<CodeInstruction> ReplaceUpgradeCalls(IEnumerable<CodeInstruction> instructions, bool spend)
        {
            var result = new List<CodeInstruction>(instructions);
            int reads = 0;
            int writes = 0;
            foreach (CodeInstruction instruction in result)
            {
                if (instruction.Calls(NativeGetXp))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(SharedTroopExperience), nameof(GetUpgradeExperience));
                    reads++;
                }
                else if (spend && instruction.Calls(NativeSetXp))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(SharedTroopExperience), nameof(SetUpgradeExperience));
                    writes++;
                }
            }
            if (reads != 1 || spend && writes != 1)
                throw new InvalidOperationException("Unsupported native troop upgrade XP calls.");
            return result;
        }

        private static bool BeforeScreenDone(PartyScreenLogic __instance, ref bool __result)
        {
            ScreenTransaction transaction;
            if (!_screens.TryGetValue(__instance, out transaction))
                return true;
            if (transaction.Party == PartyBase.MainParty &&
                transaction.Spent <= AddSaturated(_behavior.ExperiencePool, transaction.Earned))
                return true;
            __result = false;
            return false;
        }

        private static void AfterScreenDone(PartyScreenLogic __instance, bool __result)
        {
            ScreenTransaction transaction;
            if (!__result || !_screens.TryGetValue(__instance, out transaction))
                return;
            _behavior.ExperiencePool = Math.Max(0,
                AddSaturated(_behavior.ExperiencePool, transaction.Earned) - transaction.Spent);
            transaction.Earned = transaction.Spent = transaction.SavedEarned = transaction.SavedSpent = 0;
        }

        private static void BeforeScreenReset(PartyScreenLogic __instance)
        {
            ScreenTransaction transaction;
            if (_screens.TryGetValue(__instance, out transaction))
                transaction.Earned = transaction.Spent = 0;
        }

        private static void BeforeScreenSaved(PartyScreenLogic __instance)
        {
            ScreenTransaction transaction;
            if (_screens.TryGetValue(__instance, out transaction))
            {
                transaction.SavedEarned = transaction.Earned;
                transaction.SavedSpent = transaction.Spent;
            }
        }

        private static void BeforeSavedScreenRestored(PartyScreenLogic __instance)
        {
            ScreenTransaction transaction;
            if (_screens.TryGetValue(__instance, out transaction))
            {
                transaction.Earned = transaction.SavedEarned;
                transaction.Spent = transaction.SavedSpent;
            }
        }

        private static void AfterScreenClosed(PartyScreenLogic __instance)
        {
            RemoveScreen(__instance);
        }

        private static void RemoveScreen(PartyScreenLogic logic)
        {
            ScreenTransaction transaction;
            if (_screens.TryGetValue(logic, out transaction))
            {
                ScreenTransaction current;
                if (_screenRosters.TryGetValue(transaction.Roster, out current) && ReferenceEquals(current, transaction))
                    _screenRosters.Remove(transaction.Roster);
                _screens.Remove(logic);
            }
        }

        private static void AddBattleOverflow(MapEventParty mapEventParty, CharacterObject troop, int xp)
        {
            if (IsActive && mapEventParty.Party == PartyBase.MainParty && troop != null && !troop.IsHero && xp > 0)
                AddExperience(PartyBase.MainParty.MemberRoster, xp);
        }

        private static IEnumerable<CodeInstruction> CollectBattleOverflow(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            int call = result.FindIndex(instruction => instruction.operand is MethodInfo method &&
                method.DeclaringType.FullName == "TaleWorlds.CampaignSystem.CharacterDevelopment.SkillLevelingManager" &&
                method.Name == "OnBattleEnded");
            if (call < 3)
                throw new InvalidOperationException("Unsupported native battle XP overflow call.");
            CodeInstruction troopLoad = result[call - 2];
            CodeInstruction xpLoad = result[call - 1];
            int guard = call - 3;
            while (guard >= 0 && result[guard].opcode != OpCodes.Ldloc_1)
                guard--;
            if (guard < 0 || result[guard + 1].opcode != OpCodes.Brfalse_S && result[guard + 1].opcode != OpCodes.Brfalse)
                throw new InvalidOperationException("Unsupported native battle XP overflow guard.");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(result[guard].labels);
            result[guard].labels.Clear();
            first.blocks.AddRange(result[guard].blocks);
            result[guard].blocks.Clear();
            result.InsertRange(guard, new[]
            {
                first,
                new CodeInstruction(troopLoad.opcode, troopLoad.operand),
                new CodeInstruction(xpLoad.opcode, xpLoad.operand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SharedTroopExperience), nameof(AddBattleOverflow)))
            });
            // The native overflow still reaches SkillLevelingManager unchanged.
            // Pooling regular XP must not alter its hero skill XP behavior.
            return result;
        }
    }

    internal sealed class SharedTroopExperienceCampaignBehavior : CampaignBehaviorBase
    {
        private long _experiencePool;
        internal bool Ready { get; private set; }
        internal long ExperiencePool
        {
            get => _experiencePool;
            set => _experiencePool = Math.Max(0, value);
        }

        internal SharedTroopExperienceCampaignBehavior()
        {
            SharedTroopExperience.BindCampaign(this);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, _ => StartCampaign());
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("calradia_comfort_shared_troop_xp", ref _experiencePool);
            _experiencePool = Math.Max(0, _experiencePool);
        }

        private void StartCampaign()
        {
            Ready = true;
            SharedTroopExperience.CollectStoredExperience();
        }

        private void OnTick(float dt)
        {
            if (Ready)
                SharedTroopExperience.CollectStoredExperience();
        }
    }
}
