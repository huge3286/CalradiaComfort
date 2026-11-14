using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    [ViewModelMixin(nameof(PartyCharacterVM.InitializeUpgrades))]
    public sealed class UltimateTroopUpgradeCharacterMixin : BaseViewModelMixin<PartyCharacterVM>
    {
        private static readonly FieldInfo PartyField = AccessTools.Field(typeof(PartyCharacterVM), "_partyVm");

        private bool _isUltimateUpgradeVisible;
        private bool _canUltimateUpgrade;
        private string _ultimateUpgradeText;
        private float _ultimateUpgradeSlotsWidth = 230;
        private bool _finalized;
        private bool _refreshing;

        public UltimateTroopUpgradeCharacterMixin(PartyCharacterVM viewModel) : base(viewModel)
        {
            UltimateUpgradeHint = new HintViewModel(new TextObject(
                "{=cc_ultimate_upgrade_hint}Upgrade eligible healthy troops directly to a final tier. Requires shared experience and pays the normal cost at every step."));
            OnRefresh();
        }

        public override void OnRefresh()
        {
            if (_finalized || _refreshing)
                return;

            _refreshing = true;
            try
            {
                UltimateUpgradeText = new TextObject("{=cc_ultimate_upgrade}Max Tier").ToString();
                PartyVM party = GetParty();
                PartyCharacterVM troop = ViewModel;
                PartyScreenLogic logic = party?.PartyScreenLogic;
                IsUltimateUpgradeVisible = ComfortFeatures.UltimateTroopUpgradeEnabled && troop != null &&
                    troop.Character != null && !troop.IsHero && !troop.IsPrisoner &&
                    troop.Type == PartyScreenLogic.TroopType.Member &&
                    troop.Side == PartyScreenLogic.PartyRosterSide.Right &&
                    logic != null && PartyBase.MainParty != null &&
                    ReferenceEquals(logic.RightOwnerParty, PartyBase.MainParty) &&
                    troop.Character.UpgradeTargets != null && troop.Character.UpgradeTargets.Length != 0;
                CanUltimateUpgrade = IsUltimateUpgradeVisible && UltimateTroopUpgrade.IsActive &&
                    UltimateTroopUpgrade.CanUpgrade(party, troop);
            }
            finally
            {
                _refreshing = false;
            }
        }

        protected override void OnViewModelPropertyChanged(string propertyName)
        {
            if (!_finalized && (propertyName == nameof(PartyCharacterVM.Number) ||
                propertyName == nameof(PartyCharacterVM.WoundedCount) ||
                propertyName == nameof(PartyCharacterVM.IsTroopUpgradable) ||
                propertyName == nameof(PartyCharacterVM.Character) ||
                propertyName == nameof(PartyCharacterVM.Upgrades)))
            {
                OnRefresh();
            }
        }

        public override void OnFinalize()
        {
            _finalized = true;
            base.OnFinalize();
        }

        private PartyVM GetParty()
        {
            PartyCharacterVM troop = ViewModel;
            return troop == null ? null : PartyField?.GetValue(troop) as PartyVM;
        }

        [DataSourceMethod]
        public void ExecuteUltimateUpgrade()
        {
            OnRefresh();
            PartyVM party = GetParty();
            PartyCharacterVM troop = ViewModel;
            if (_finalized || !CanUltimateUpgrade || party?.UpgradePopUp == null || !party.UpgradePopUp.IsOpen ||
                !UltimateTroopUpgrade.CanUpgrade(party, troop))
                return;

            var elements = new List<InquiryElement>();
            foreach (UltimateUpgradeOption option in UltimateTroopUpgrade.GetOptions(troop.Character))
            {
                int count = UltimateTroopUpgrade.GetAffordableCount(party, troop, option);
                elements.Add(new InquiryElement(option, option.Target.Name.ToString(),
                    new CharacterImageIdentifier(CharacterCode.CreateFrom(option.Target)), count > 0,
                    UltimateTroopUpgrade.GetOptionDescription(party, troop, option)));
            }
            if (elements.Count == 0)
                return;

            string description = new TextObject(
                "{=cc_ultimate_upgrade_selection}Choose a final tier for {TROOP}. Experience, gold and mounts are charged for each step. Only troops that can reach the selected tier are upgraded. Confirm the changes on the party screen.")
                .SetTextVariable("TROOP", troop.Character.Name).ToString();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=cc_ultimate_upgrade_title}Select Final Tier").ToString(), description,
                elements, true, 1, 1,
                new TextObject("{=cc_ultimate_upgrade_confirm}Upgrade").ToString(),
                new TextObject("{=cc_ultimate_upgrade_cancel}Cancel").ToString(),
                selected => ConfirmSelection(party, troop, selected), null, string.Empty, true));
        }

        private void ConfirmSelection(PartyVM party, PartyCharacterVM troop, List<InquiryElement> selected)
        {
            if (_finalized || selected == null || selected.Count != 1 || party?.UpgradePopUp == null || !party.UpgradePopUp.IsOpen ||
                !ReferenceEquals(ViewModel, troop) || !ReferenceEquals(GetParty(), party) ||
                !UltimateTroopUpgrade.CanUpgrade(party, troop))
                return;

            UltimateUpgradeOption option = selected[0].Identifier as UltimateUpgradeOption;
            if (option == null || !ReferenceEquals(option.Source, troop.Character))
                return;

            string sourceName = troop.Character.Name.ToString();
            int upgraded = UltimateTroopUpgrade.UpgradeTo(party, troop, option);
            if (upgraded > 0)
            {
                string message = new TextObject(
                    "{=cc_ultimate_upgrade_done}Queued an upgrade for {COUNT} {SOURCE} to {TARGET}. Select Done to confirm.")
                    .SetTextVariable("COUNT", upgraded)
                    .SetTextVariable("SOURCE", sourceName)
                    .SetTextVariable("TARGET", option.Target.Name).ToString();
                InformationManager.DisplayMessage(new InformationMessage(message));
            }
            else if (upgraded < 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=cc_ultimate_upgrade_rolled_back}Upgrade failed. Changes made since opening the upgrade panel were rolled back.").ToString()));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=cc_ultimate_upgrade_unavailable}No troops upgraded. Check experience, gold, mounts, and other requirements.").ToString()));
            }
            OnRefresh();
        }

        [DataSourceProperty]
        public bool IsUltimateUpgradeVisible
        {
            get => _isUltimateUpgradeVisible;
            private set
            {
                SetField(ref _isUltimateUpgradeVisible, value, nameof(IsUltimateUpgradeVisible));
                UltimateUpgradeSlotsWidth = value ? 132 : 230;
            }
        }

        [DataSourceProperty]
        public bool CanUltimateUpgrade
        {
            get => _canUltimateUpgrade;
            private set => SetField(ref _canUltimateUpgrade, value, nameof(CanUltimateUpgrade));
        }

        [DataSourceProperty]
        public string UltimateUpgradeText
        {
            get => _ultimateUpgradeText;
            private set => SetField(ref _ultimateUpgradeText, value, nameof(UltimateUpgradeText));
        }

        [DataSourceProperty]
        public HintViewModel UltimateUpgradeHint { get; }

        [DataSourceProperty]
        public float UltimateUpgradeSlotsWidth
        {
            get => _ultimateUpgradeSlotsWidth;
            private set => SetField(ref _ultimateUpgradeSlotsWidth, value, nameof(UltimateUpgradeSlotsWidth));
        }
    }

    // Share the native 230-wide upgrade column: 132 for two 64-wide native
    // branches, 8 spacing, and a 90-wide action. Hidden restores native width.
    [PrefabExtension("PartyTroopUpgradeItem", "descendant::ScrollablePanel[@Id='ScrollablePanel']")]
    public sealed class UltimateTroopUpgradeSlotsPrefabExtension : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes =>
            new List<PrefabExtensionSetAttributePatch.Attribute>
            {
                new PrefabExtensionSetAttributePatch.Attribute("SuggestedWidth", "@UltimateUpgradeSlotsWidth")
            };
    }

    [PrefabExtension("PartyTroopUpgradeItem", "descendant::ScrollablePanel[@Id='ScrollablePanel']")]
    public sealed class UltimateTroopUpgradePrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionXmlNode(false)]
        public XmlNode GetExtension()
        {
            var document = new XmlDocument();
            document.LoadXml(@"<ButtonWidget Id=""CalradiaComfortUltimateUpgrade"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""90"" SuggestedHeight=""28"" VerticalAlignment=""Center"" MarginLeft=""8"" Brush=""Party.RecruitPopup.RecruitAllButton"" Command.Click=""ExecuteUltimateUpgrade"" IsEnabled=""@CanUltimateUpgrade"" IsVisible=""@IsUltimateUpgradeVisible"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
  <Children>
    <TextWidget DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""CustomBattle.PopUp.Buttons.Text"" Brush.FontSize=""18"" Text=""@UltimateUpgradeText"" />
    <HintWidget DataSource=""{UltimateUpgradeHint}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
  </Children>
</ButtonWidget>");
            return document.DocumentElement;
        }
    }
}
