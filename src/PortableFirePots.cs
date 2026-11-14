using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace StorySkip
{
    internal sealed class PortableFirePotsCampaignBehavior : CampaignBehaviorBase
    {
        internal const string ItemId = "comfort_portable_fire_pot";

        public override void RegisterEvents() => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                PortableFirePotEquipment.NormalizeExisting(Hero.MainHero, MobileParty.MainParty?.ItemRoster);
                if (!ComfortFeatures.PortableFirePotsEnabled || Hero.MainHero == null || MobileParty.MainParty?.ItemRoster == null) return;
                var pot = MBObjectManager.Instance.GetObject<ItemObject>(ItemId);
                if (pot?.PrimaryWeapon == null)
                {
                    Debug.Print("[CalradiaComfort] Portable fire pot item is missing; check ModuleData installation.");
                    return;
                }
                if (HasEquipped(Hero.MainHero.BattleEquipment, pot) || HasEquipped(Hero.MainHero.CivilianEquipment, pot) ||
                    MobileParty.MainParty.ItemRoster.GetItemNumber(pot) > 0) return;
                // Ordinary thrown equipment; sale and discard protection is item-specific.
                MobileParty.MainParty.ItemRoster.AddToCounts(new EquipmentElement(pot), 1);
                InformationManager.DisplayMessage(new InformationMessage("便携火罐已加入背包：基础容量 10 个，可手动装备到武器栏，按普通投掷物使用，支持骑马投掷。"));
            }
            catch (Exception ex) { Debug.Print("[CalradiaComfort] Fire pot grant failed: " + ex); }
        }

        internal static bool HasEquipped(Equipment equipment, ItemObject pot)
        {
            if (equipment == null || pot == null) return false;
            for (int i = (int)EquipmentIndex.Weapon0; i <= (int)EquipmentIndex.Weapon3; i++)
                if (equipment[i].Item == pot) return true;
            return false;
        }
    }
}
