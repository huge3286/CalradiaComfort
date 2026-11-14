using System;
using System.Reflection;
using System.Xml;
using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal static class QuickRecruitmentUI
    {
        private static UIExtender _extender;
        private static readonly FieldInfo GameTextManagerField =
            AccessTools.Field(typeof(GameTexts), "_gameTextManager");

        internal static void Initialize(Harmony harmony)
        {
            if (_extender != null)
                return;
            if (GameTextManagerField == null)
                throw new MissingFieldException("The native game text manager could not be found.");
            // Registering the hero page mixin JITs its native Refresh method.
            // This can run CampaignUIHelper's static initializer. UIExtender's
            // patch helper swallows its exception, permanently poisoning that
            // native type when GameTexts has not been initialized yet.
            if (GameTextManagerField.GetValue(null) == null)
                return;

            // The extender uses the stable module ID to find this mod's resources.
            _extender = UIExtender.Create("StorySkip");
            _extender.Register(new[]
            {
                typeof(QuickRecruitmentViewModelMixin),
                typeof(QuickRecruitmentPrefabExtension),
                typeof(UltimateTroopUpgradeCharacterMixin),
                typeof(UltimateTroopUpgradeSlotsPrefabExtension),
                typeof(UltimateTroopUpgradePrefabExtension),
            });
            _extender.Enable();
        }

        internal static void Cleanup()
        {
            if (_extender == null)
                return;

            _extender.Disable();
            _extender.Deregister();
            _extender = null;
        }
    }

    [ViewModelMixin(nameof(RecruitmentVM.RefreshValues))]
    public sealed class QuickRecruitmentViewModelMixin : BaseViewModelMixin<RecruitmentVM>
    {
        private bool _isQuickRecruitmentVisible;
        private bool _canQuickRecruitOrdinary;
        private bool _canQuickRecruitNoble;
        private string _quickRecruitOrdinaryText;
        private string _quickRecruitNobleText;
        private bool _finalized;

        public QuickRecruitmentViewModelMixin(RecruitmentVM viewModel) : base(viewModel)
        {
            QuickRecruitOrdinaryHint = new HintViewModel(new TextObject(
                "{=cc_quick_recruit_ordinary_hint}Stage all eligible common troops. Normal relations, costs, and party capacity still apply. Select Done to recruit or cancel to undo."));
            QuickRecruitNobleHint = new HintViewModel(new TextObject(
                "{=cc_quick_recruit_noble_hint}Stage all eligible troops in noble upgrade trees. Normal relations, costs, and party capacity still apply."));
            QuickRecruitment.StateChanged += OnRecruitmentStateChanged;
            OnRefresh();
        }

        public override void OnRefresh()
        {
            if (_finalized)
                return;

            QuickRecruitOrdinaryText = new TextObject("{=cc_quick_recruit_ordinary}Recruit Common").ToString();
            QuickRecruitNobleText = new TextObject("{=cc_quick_recruit_noble}Recruit Noble").ToString();
            RefreshAvailability();
        }

        public override void OnFinalize()
        {
            _finalized = true;
            QuickRecruitment.StateChanged -= OnRecruitmentStateChanged;
            base.OnFinalize();
        }

        protected override void OnViewModelPropertyChanged(string propertyName)
        {
            // SandBox enables the screen after constructing and refreshing its VM.
            if (!_finalized && propertyName == nameof(RecruitmentVM.Enabled))
                RefreshAvailability();
        }

        private void OnRecruitmentStateChanged(RecruitmentVM viewModel)
        {
            if (!_finalized && ReferenceEquals(viewModel, ViewModel))
                RefreshAvailability();
        }

        private void RefreshAvailability()
        {
            IsQuickRecruitmentVisible = QuickRecruitment.IsEnabled;
            CanQuickRecruitOrdinary = IsQuickRecruitmentVisible && QuickRecruitment.CanRecruit(ViewModel, false);
            CanQuickRecruitNoble = IsQuickRecruitmentVisible && QuickRecruitment.CanRecruit(ViewModel, true);
        }

        [DataSourceMethod]
        public void ExecuteQuickRecruitOrdinary()
        {
            QuickRecruitment.Recruit(ViewModel, false);
            RefreshAvailability();
        }

        [DataSourceMethod]
        public void ExecuteQuickRecruitNoble()
        {
            QuickRecruitment.Recruit(ViewModel, true);
            RefreshAvailability();
        }

        [DataSourceProperty]
        public bool IsQuickRecruitmentVisible
        {
            get => _isQuickRecruitmentVisible;
            private set => SetField(ref _isQuickRecruitmentVisible, value, nameof(IsQuickRecruitmentVisible));
        }

        [DataSourceProperty]
        public bool CanQuickRecruitOrdinary
        {
            get => _canQuickRecruitOrdinary;
            private set => SetField(ref _canQuickRecruitOrdinary, value, nameof(CanQuickRecruitOrdinary));
        }

        [DataSourceProperty]
        public bool CanQuickRecruitNoble
        {
            get => _canQuickRecruitNoble;
            private set => SetField(ref _canQuickRecruitNoble, value, nameof(CanQuickRecruitNoble));
        }

        [DataSourceProperty]
        public string QuickRecruitOrdinaryText
        {
            get => _quickRecruitOrdinaryText;
            private set => SetField(ref _quickRecruitOrdinaryText, value, nameof(QuickRecruitOrdinaryText));
        }

        [DataSourceProperty]
        public string QuickRecruitNobleText
        {
            get => _quickRecruitNobleText;
            private set => SetField(ref _quickRecruitNobleText, value, nameof(QuickRecruitNobleText));
        }

        [DataSourceProperty]
        public HintViewModel QuickRecruitOrdinaryHint { get; }

        [DataSourceProperty]
        public HintViewModel QuickRecruitNobleHint { get; }
    }

    // Keep all three actions in one row, including the native hint and shortcut.
    [PrefabExtension("RecruitmentPopup", "descendant::ListPanel[@Id='RecruitmentScrollInnerPanel']/Children/ButtonWidget[@Command.Click='ExecuteRecruitAll']")]
    public sealed class QuickRecruitmentPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;

        [PrefabExtensionXmlNode(false)]
        public XmlNode GetExtension()
        {
            var document = new XmlDocument();
            document.LoadXml(@"<ListPanel Id=""CalradiaComfortQuickRecruitment"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" StackLayout.LayoutMethod=""HorizontalLeftToRight"" HorizontalAlignment=""Right"" MarginTop=""5"" MarginRight=""15"">
  <Children>
    <ButtonWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""44"" Command.Click=""ExecuteRecruitAll"" IsEnabled=""@CanRecruitAll"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
      <Children>
        <ListPanel WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" StackLayout.LayoutMethod=""HorizontalLeftToRight"" PositionXOffset=""-44"" UpdateChildrenStates=""true"">
          <Children>
            <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Text=""@RecruitAllText"" MarginTop=""10"" MarginRight=""5"" Brush=""Recruitment.RecruitAllButton.Text"" />
            <ImageWidget DoNotPassEventsToChildren=""true"" HeightSizePolicy=""Fixed"" WidthSizePolicy=""Fixed"" SuggestedHeight=""!RecruitAllButton.Height"" SuggestedWidth=""!RecruitAllButton.Width"" Brush=""ButtonLeftDoubleArrowBrush1"" />
          </Children>
        </ListPanel>
        <HintWidget DataSource=""{RecruitAllHint}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
        <InputKeyVisualWidget DataSource=""{RecruitAllInputKey}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""60"" SuggestedHeight=""60"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" PositionYOffset=""-8"" KeyID=""@KeyID"" IsVisible=""@IsVisible""/>
      </Children>
    </ButtonWidget>
    <ButtonWidget Id=""CalradiaComfortRecruitOrdinary"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""24"" Command.Click=""ExecuteQuickRecruitOrdinary"" IsEnabled=""@CanQuickRecruitOrdinary"" IsVisible=""@IsQuickRecruitmentVisible"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
      <Children>
        <ListPanel WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" StackLayout.LayoutMethod=""HorizontalLeftToRight"" UpdateChildrenStates=""true"">
          <Children>
            <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Text=""@QuickRecruitOrdinaryText"" MarginTop=""10"" MarginRight=""5"" Brush=""Recruitment.RecruitAllButton.Text"" />
            <ImageWidget DoNotPassEventsToChildren=""true"" HeightSizePolicy=""Fixed"" WidthSizePolicy=""Fixed"" SuggestedHeight=""!RecruitAllButton.Height"" SuggestedWidth=""!RecruitAllButton.Width"" Brush=""ButtonLeftDoubleArrowBrush1"" />
          </Children>
        </ListPanel>
        <HintWidget DataSource=""{QuickRecruitOrdinaryHint}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
      </Children>
    </ButtonWidget>
    <ButtonWidget Id=""CalradiaComfortRecruitNoble"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""24"" Command.Click=""ExecuteQuickRecruitNoble"" IsEnabled=""@CanQuickRecruitNoble"" IsVisible=""@IsQuickRecruitmentVisible"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
      <Children>
        <ListPanel WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" StackLayout.LayoutMethod=""HorizontalLeftToRight"" UpdateChildrenStates=""true"">
          <Children>
            <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Text=""@QuickRecruitNobleText"" MarginTop=""10"" MarginRight=""5"" Brush=""Recruitment.RecruitAllButton.Text"" />
            <ImageWidget DoNotPassEventsToChildren=""true"" HeightSizePolicy=""Fixed"" WidthSizePolicy=""Fixed"" SuggestedHeight=""!RecruitAllButton.Height"" SuggestedWidth=""!RecruitAllButton.Width"" Brush=""ButtonLeftDoubleArrowBrush1"" />
          </Children>
        </ListPanel>
        <HintWidget DataSource=""{QuickRecruitNobleHint}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
      </Children>
    </ButtonWidget>
  </Children>
</ListPanel>");
            return document.DocumentElement;
        }
    }
}
