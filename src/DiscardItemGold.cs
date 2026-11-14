using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class DiscardItemGold
    {
        private static readonly FieldInfo InventoryModeField = AccessTools.Field(typeof(InventoryLogic), "_inventoryMode");
        private static readonly FieldInfo RostersField = AccessTools.Field(typeof(InventoryLogic), "_rosters");
        private static ConditionalWeakTable<InventoryLogic, CommitMarker> _commits =
            new ConditionalWeakTable<InventoryLogic, CommitMarker>();
        [ThreadStatic] private static CommitFrame _activeCommit;
        [ThreadStatic] private static CloseFrame _activeClose;

        private sealed class CommitMarker
        {
            internal bool InProgress;
            internal bool Committed;
            internal Dictionary<EquipmentElement, long> InitialLeft;
        }

        private sealed class CommitFrame
        {
            internal CommitFrame Previous;
            internal InventoryLogic Logic;
            internal CommitMarker Marker;
            internal Campaign Campaign;
            internal Hero Recipient;
            internal ItemRoster LeftRoster;
            internal bool Cancelled;
            internal bool EventObserved;
            internal long Gold;
        }

        private sealed class CloseFrame
        {
            internal CloseFrame Previous;
            internal InventoryLogic Logic;
            internal bool Cancelled;
        }

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            MethodInfo close = AccessTools.Method(typeof(InventoryScreenHelper), "CloseInventoryPresentation", new[] { typeof(bool) });
            MethodInfo done = AccessTools.Method(typeof(InventoryLogic), nameof(InventoryLogic.DoneLogic), Type.EmptyTypes);
            MethodInfo initializeRosters = AccessTools.Method(typeof(InventoryLogic), "InitializeRosters");
            if (close == null || done == null || initializeRosters == null || InventoryModeField == null || RostersField == null)
                throw new MissingMemberException("Discard settlement hooks changed in this Bannerlord version.");
            harmony.Patch(close,
                prefix: new HarmonyMethod(typeof(DiscardItemGold), nameof(BeforeClose)),
                finalizer: new HarmonyMethod(typeof(DiscardItemGold), nameof(FinishClose)));
            harmony.Patch(done,
                prefix: new HarmonyMethod(typeof(DiscardItemGold), nameof(BeforeDone)),
                postfix: new HarmonyMethod(typeof(DiscardItemGold), nameof(AfterDone)),
                finalizer: new HarmonyMethod(typeof(DiscardItemGold), nameof(FinishDone)));
            harmony.Patch(initializeRosters,
                postfix: new HarmonyMethod(typeof(DiscardItemGold), nameof(CaptureInitialRoster)));
        }

        internal static void ClearSession()
        {
            _commits = new ConditionalWeakTable<InventoryLogic, CommitMarker>();
            _activeCommit = null;
            _activeClose = null;
        }

        internal static bool IsDiscardInventory(InventoryLogic logic)
        {
            if (logic == null || Campaign.Current == null || Hero.MainHero == null ||
                MobileParty.MainParty == null || logic.OwnerParty != MobileParty.MainParty ||
                logic.OtherParty != null || logic.IsTrading || !logic.CanGainXpFromDiscarding)
                return false;
            var mode = (InventoryScreenHelper.InventoryMode)InventoryModeField.GetValue(logic);
            return mode == InventoryScreenHelper.InventoryMode.Default || mode == InventoryScreenHelper.InventoryMode.Loot;
        }

        internal static long CalculateGold(IEnumerable<ItemRosterElement> items, int percent)
        {
            if (items == null) return 0;
            percent = Math.Max(0, Math.Min(100, percent));
            if (percent == 0) return 0;
            decimal gold = 0;
            foreach (ItemRosterElement entry in items)
            {
                EquipmentElement equipment = entry.EquipmentElement;
                if (entry.Amount <= 0 || equipment.Item == null || equipment.IsQuestItem ||
                    PortableFirePotEquipment.IsFirePot(equipment.Item) || !equipment.Item.IsTransferable)
                    continue;
                int value = equipment.ItemValue;
                if (value <= 0) continue;
                gold += (decimal)value * entry.Amount * percent / 100m;
                if (gold >= long.MaxValue) return long.MaxValue;
            }
            // Round once for the entire discarded batch, not once per cheap item.
            return (long)decimal.Truncate(gold);
        }

        internal static List<ItemRosterElement> GetDiscardedItems(InventoryLogic logic)
        {
            var result = new List<ItemRosterElement>();
            var mode = (InventoryScreenHelper.InventoryMode)InventoryModeField.GetValue(logic);
            var initial = new Dictionary<EquipmentElement, long>();
            if (mode == InventoryScreenHelper.InventoryMode.Default)
            {
                // Cheat inventory starts with a free catalogue on the left. Only
                // net additions count as owned items discarded in this mode.
                if (!_commits.TryGetValue(logic, out CommitMarker marker) || marker.InitialLeft == null)
                    return result;
                initial = marker.InitialLeft;
            }
            foreach (ItemRosterElement entry in logic.GetElementsInRoster((InventoryLogic.InventorySide)0))
            {
                initial.TryGetValue(entry.EquipmentElement, out long count);
                long discarded = (long)entry.Amount - count;
                if (discarded > 0)
                    result.Add(new ItemRosterElement(entry.EquipmentElement, (int)Math.Min(int.MaxValue, discarded)));
            }
            return result;
        }

        private static void CaptureInitialRoster(InventoryLogic __instance)
        {
            var initial = new Dictionary<EquipmentElement, long>();
            foreach (ItemRosterElement entry in __instance.GetElementsInRoster((InventoryLogic.InventorySide)0))
            {
                initial.TryGetValue(entry.EquipmentElement, out long count);
                initial[entry.EquipmentElement] = count + Math.Max(0, entry.Amount);
            }
            // Snapshot once when rosters are initialized. Native donation and
            // slaughter update the reset backup; they must not replace this.
            _commits.Remove(__instance);
            _commits.Add(__instance, new CommitMarker { InitialLeft = initial });
        }

        private static void BeforeClose(bool __0, out CloseFrame __state)
        {
            __state = new CloseFrame
            {
                Previous = _activeClose,
                Logic = InventoryScreenHelper.GetActiveInventoryState()?.InventoryLogic,
                Cancelled = __0
            };
            _activeClose = __state;
        }

        private static void FinishClose(CloseFrame __state)
        {
            if (__state != null) _activeClose = __state.Previous;
        }

        private static bool IsCancelling(InventoryLogic logic)
        {
            for (CloseFrame close = _activeClose; close != null; close = close.Previous)
                if (close.Logic == logic && close.Cancelled) return true;
            return false;
        }

        private static void BeforeDone(InventoryLogic __instance, out CommitFrame __state)
        {
            __state = null;
            try
            {
                if (!IsDiscardInventory(__instance)) return;
                CommitMarker marker = _commits.GetValue(__instance, _ => new CommitMarker());
                if (marker.Committed || marker.InProgress) return;
                var rosters = RostersField.GetValue(__instance) as ItemRoster[];
                if (rosters == null || rosters.Length < 1 || rosters[0] == null) return;
                __state = new CommitFrame
                {
                    Previous = _activeCommit,
                    Logic = __instance,
                    Marker = marker,
                    Campaign = Campaign.Current,
                    Recipient = Hero.MainHero,
                    LeftRoster = rosters[0],
                    Cancelled = IsCancelling(__instance)
                };
                marker.InProgress = true;
                _activeCommit = __state;
                if (!__state.Cancelled && ComfortFeatures.DiscardItemGoldEnabled)
                    __state.Gold = CalculateGold(GetDiscardedItems(__instance), ComfortFeatures.DiscardItemGoldPercent);
            }
            catch (Exception ex)
            {
                if (__state != null) __state.Gold = 0;
                Debug.Print("[CalradiaComfort] Discard valuation failed: " + ex);
            }
        }

        internal static void OnItemsDiscarded(ItemRoster roster)
        {
            CommitFrame frame = _activeCommit;
            if (frame != null && ReferenceEquals(roster, frame.LeftRoster))
                frame.EventObserved = true;
        }

        private static void AfterDone(bool __result, CommitFrame __state)
        {
            if (__state == null || !__result || !__state.EventObserved) return;
            // Consume the commit before invoking native gold events, which can
            // re-enter inventory code. Successful disabled/zero/cancel commits
            // must not become payable later by repeating DoneLogic.
            __state.Marker.Committed = true;
            if (__state.Cancelled || __state.Gold <= 0 || !ComfortFeatures.DiscardItemGoldEnabled ||
                Campaign.Current != __state.Campaign || Hero.MainHero != __state.Recipient ||
                !IsDiscardInventory(__state.Logic))
                return;
            long capacity = Math.Max(0L, (long)int.MaxValue - __state.Recipient.Gold);
            int payment = (int)Math.Min(__state.Gold, Math.Min(int.MaxValue, capacity));
            if (payment == 0) return;
            try
            {
                GiveGoldAction.ApplyBetweenCharacters(null, __state.Recipient, payment, false);
                InformationManager.DisplayMessage(new InformationMessage("丢弃物品兑换：+" + payment + " 第纳尔"));
            }
            catch (Exception ex)
            {
                // Never retry an action whose native gold event may already
                // have credited the recipient before another listener threw.
                Debug.Print("[CalradiaComfort] Discard gold settlement failed: " + ex);
            }
        }

        private static void FinishDone(CommitFrame __state)
        {
            if (__state == null) return;
            __state.Marker.InProgress = false;
            _activeCommit = __state.Previous;
        }
    }

    internal sealed class DiscardItemGoldCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnItemsDiscardedByPlayerEvent.AddNonSerializedListener(this, DiscardItemGold.OnItemsDiscarded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Native inventory and hero gold serialization persist the result.
        }
    }
}
