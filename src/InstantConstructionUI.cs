using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    [ViewModelMixin(nameof(TownManagementVM.RefreshValues))]
    public sealed class InstantConstructionViewModelMixin : BaseViewModelMixin<TownManagementVM>
    {
        private bool _isMaxConstructionVisible;
        private bool _canMaximizeBuildings;
        private string _maximizeBuildingsText;
        private bool _finalized;

        public InstantConstructionViewModelMixin(TownManagementVM viewModel) : base(viewModel)
        {
            MaximizeBuildingsHint = new HintViewModel(new TextObject(
                "{=cc_max_buildings_hint}将当前玩家家族城镇或城堡的全部建筑立即升至三级，保留原版建筑完成效果。住房、灌溉、庆典等日常项目保持原状。点击即生效。"));
            OnRefresh();
        }

        public override void OnRefresh()
        {
            if (_finalized)
                return;
            MaximizeBuildingsText = new TextObject("{=cc_max_buildings}一键升满建筑").ToString();
            RefreshAvailability();
        }

        protected override void OnViewModelPropertyChanged(string propertyName)
        {
            if (!_finalized && propertyName == nameof(TownManagementVM.Show))
                RefreshAvailability();
        }

        public override void OnFinalize()
        {
            _finalized = true;
            base.OnFinalize();
        }

        private void RefreshAvailability()
        {
            var town = InstantConstruction.GetTown(ViewModel);
            IsMaxConstructionVisible = InstantConstruction.CanComplete(town);
            CanMaximizeBuildings = IsMaxConstructionVisible && InstantConstruction.CanMaximize(town);
        }

        [DataSourceMethod]
        public void ExecuteMaximizeBuildings()
        {
            var town = InstantConstruction.GetTown(ViewModel);
            if (!InstantConstruction.CanMaximize(town))
            {
                RefreshAvailability();
                return;
            }
            int completed = InstantConstruction.MaximizeBuildings(town);
            InstantConstruction.RefreshManagement(ViewModel);
            RefreshAvailability();
            if (completed > 0)
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=cc_max_buildings_result}已完成 {COUNT} 次建筑升级。").SetTextVariable("COUNT", completed).ToString()));
        }

        [DataSourceProperty]
        public bool IsMaxConstructionVisible
        {
            get => _isMaxConstructionVisible;
            private set => SetField(ref _isMaxConstructionVisible, value, nameof(IsMaxConstructionVisible));
        }

        [DataSourceProperty]
        public bool CanMaximizeBuildings
        {
            get => _canMaximizeBuildings;
            private set => SetField(ref _canMaximizeBuildings, value, nameof(CanMaximizeBuildings));
        }

        [DataSourceProperty]
        public string MaximizeBuildingsText
        {
            get => _maximizeBuildingsText;
            private set => SetField(ref _maximizeBuildingsText, value, nameof(MaximizeBuildingsText));
        }

        [DataSourceProperty]
        public HintViewModel MaximizeBuildingsHint { get; }
    }

    [PrefabExtension("TownManagement", "descendant::Standard.PopupCloseButton[@Parameter.ButtonAction='ExecuteDone']")]
    public sealed class InstantConstructionPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Prepend;

        [PrefabExtensionXmlNode(false)]
        public XmlNode GetExtension()
        {
            var document = new XmlDocument();
            document.LoadXml(@"<ButtonWidget Id=""CalradiaComfortMaximizeBuildings"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""330"" SuggestedHeight=""70"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" PositionXOffset=""-380"" MarginTop=""940"" IsVisible=""@IsMaxConstructionVisible"" IsEnabled=""@CanMaximizeBuildings"" Command.Click=""ExecuteMaximizeBuildings"" Brush=""Standard.PopupCloseButton"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
  <Children>
    <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Text=""@MaximizeBuildingsText"" Brush=""Standard.DialogCloseButtons.ButtonText"" DoNotAcceptEvents=""true"" />
    <HintWidget DataSource=""{MaximizeBuildingsHint}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
  </Children>
</ButtonWidget>");
            return document.DocumentElement;
        }
    }
}
