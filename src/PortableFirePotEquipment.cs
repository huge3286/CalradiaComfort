using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;

namespace StorySkip
{
    internal static class PortableFirePotEquipment
    {
        internal static bool IsFirePot(ItemObject item) => item?.StringId == PortableFirePotsCampaignBehavior.ItemId;

        internal static void InitializePatches(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(InventoryLogic), "TransferIsMovementValid",
                new[] { typeof(TransferCommand).MakeByRefType() }),
                prefix: new HarmonyMethod(typeof(PortableFirePotEquipment), nameof(ProtectTransfer)));
            harmony.Patch(AccessTools.Method(typeof(SPItemVM), nameof(SPItemVM.UpdateTradeData)),
                postfix: new HarmonyMethod(typeof(PortableFirePotEquipment), nameof(ProtectTradeData)));
            harmony.Patch(AccessTools.PropertySetter(typeof(SPItemVM), nameof(SPItemVM.TradeData)),
                postfix: new HarmonyMethod(typeof(PortableFirePotEquipment), nameof(ProtectTradeData)));
        }

        private static bool ProtectTransfer(ref TransferCommand __0, ref bool __result)
        {
            if (!IsFirePot(__0.ElementToTransfer.EquipmentElement.Item)) return true;
            // Ordinary weapons must keep the native equip flow. Block only leaving the
            // player's backpack/equipment for a shop, discarded roster, or other inventory.
            if (__0.FromSide == InventoryLogic.InventorySide.OtherInventory ||
                __0.ToSide == InventoryLogic.InventorySide.OtherInventory ||
                __0.FromSide == InventoryLogic.InventorySide.None || __0.ToSide == InventoryLogic.InventorySide.None)
            { __result = false; return false; }
            if (__0.ToSide == InventoryLogic.InventorySide.CivilianEquipment ||
                __0.ToSide == InventoryLogic.InventorySide.StealthEquipment)
            { __result = false; return false; }
            return true;
        }

        private static void ProtectTradeData(SPItemVM __instance)
        {
            if (IsFirePot(__instance.ItemRosterElement.EquipmentElement.Item) && __instance.TradeData != null)
                __instance.TradeData.IsTradeable = false;
        }

        internal static void NormalizeExisting(Hero hero, TaleWorlds.CampaignSystem.Roster.ItemRoster roster)
        {
            if (hero != null)
            {
                NormalizeEquipment(hero.BattleEquipment);
                NormalizeEquipment(hero.CivilianEquipment);
            }
            if (roster == null) return;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                var entry = roster.GetElementCopyAtIndex(i);
                var e = entry.EquipmentElement;
                if (!e.IsQuestItem || !IsFirePot(e.Item)) continue;
                roster.AddToCounts(e, -entry.Amount);
                roster.AddToCounts(new EquipmentElement(e.Item, e.ItemModifier, e.CosmeticItem, false), entry.Amount);
            }
        }

        private static void NormalizeEquipment(Equipment equipment)
        {
            if (equipment == null) return;
            for (int i = 0; i < 4; i++)
            {
                var e = equipment[i];
                if (e.IsQuestItem && IsFirePot(e.Item))
                    equipment[i] = new EquipmentElement(e.Item, e.ItemModifier, e.CosmeticItem, false);
            }
        }
    }
}
