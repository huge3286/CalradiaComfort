using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class HeroLiveLocationUI
    {
        private static ConditionalWeakTable<EncyclopediaHeroPageVM, HeroLiveLocationViewModelMixin> _pages =
            new ConditionalWeakTable<EncyclopediaHeroPageVM, HeroLiveLocationViewModelMixin>();

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo tick = AccessTools.Method(typeof(EncyclopediaPageVM), nameof(EncyclopediaPageVM.OnTick), Type.EmptyTypes);
            if (tick == null)
                throw new MissingMethodException(typeof(EncyclopediaPageVM).FullName, nameof(EncyclopediaPageVM.OnTick));

            // The native encyclopedia calls OnTick only on its active page.
            // Hero pages inherit this empty method; all other page types pass through.
            harmony.Patch(tick, postfix: new HarmonyMethod(typeof(HeroLiveLocationUI), nameof(AfterPageTick)));
        }

        internal static void Register(EncyclopediaHeroPageVM page, HeroLiveLocationViewModelMixin mixin)
        {
            if (page == null || mixin == null)
                return;

            _pages.Remove(page);
            _pages.Add(page, mixin);
        }

        internal static void Unregister(EncyclopediaHeroPageVM page, HeroLiveLocationViewModelMixin mixin)
        {
            HeroLiveLocationViewModelMixin registered;
            if (page != null && _pages.TryGetValue(page, out registered) && ReferenceEquals(registered, mixin))
                _pages.Remove(page);
        }

        internal static void ClearSession()
        {
            // The table does not keep closed pages or their target heroes alive.
            _pages = new ConditionalWeakTable<EncyclopediaHeroPageVM, HeroLiveLocationViewModelMixin>();
        }

        private static void AfterPageTick(EncyclopediaPageVM __instance)
        {
            EncyclopediaHeroPageVM page = __instance as EncyclopediaHeroPageVM;
            HeroLiveLocationViewModelMixin mixin;
            if (page != null && _pages.TryGetValue(page, out mixin))
                mixin.RefreshOnTick();
        }
    }

    [ViewModelMixin(nameof(EncyclopediaHeroPageVM.Refresh))]
    public sealed class HeroLiveLocationViewModelMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
    {
        private static readonly long RefreshInterval = Math.Max(1L, System.Diagnostics.Stopwatch.Frequency / 2L);
        private bool _isLiveLocationVisible;
        private string _liveLocationText = string.Empty;
        private string _liveLocationDisplayText = string.Empty;
        private bool _finalized;
        private bool _refreshing;
        private bool _loggedRefreshFailure;
        private long _nextRefreshTimestamp;

        public HeroLiveLocationViewModelMixin(EncyclopediaHeroPageVM viewModel) : base(viewModel)
        {
            HeroLiveLocationUI.Register(viewModel, this);
            OnRefresh();
        }

        public override void OnRefresh()
        {
            if (_finalized || _refreshing)
                return;

            _nextRefreshTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() + RefreshInterval;
            _refreshing = true;
            try
            {
                // This row intentionally has no HasMet/IsKnownToPlayer gate.
                // The native portrait, skills, biography and LastSeenText remain untouched.
                if (!ComfortFeatures.HeroLiveLocationEnabled || Campaign.Current == null || ViewModel == null)
                {
                    HideLocation();
                    return;
                }

                Hero hero = ViewModel.Obj as Hero;
                HeroLocationSnapshot location = hero == null ? null : HeroLiveLocation.Resolve(hero);
                if (location == null || string.IsNullOrEmpty(location.Description))
                {
                    HideLocation();
                    return;
                }

                LiveLocationText = location.Description;
                IsLiveLocationVisible = true;
                LiveLocationDisplayText = LiveLocationText;
                _loggedRefreshFailure = false;
            }
            catch (Exception exception)
            {
                // A stale page during campaign teardown must not break encyclopedia input.
                HideLocation();
                if (!_loggedRefreshFailure)
                {
                    _loggedRefreshFailure = true;
                    Debug.Print("[StorySkip] Hero live location refresh failed: " + exception);
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        internal void RefreshOnTick()
        {
            if (!_finalized && System.Diagnostics.Stopwatch.GetTimestamp() >= _nextRefreshTimestamp)
                OnRefresh();
        }

        public override void OnFinalize()
        {
            _finalized = true;
            HideLocation();
            HeroLiveLocationUI.Unregister(ViewModel, this);
            base.OnFinalize();
        }

        private void HideLocation()
        {
            IsLiveLocationVisible = false;
            LiveLocationText = string.Empty;
            LiveLocationDisplayText = ViewModel?.LastSeenText ?? string.Empty;
        }

        [DataSourceProperty]
        public bool IsLiveLocationVisible
        {
            get => _isLiveLocationVisible;
            private set => SetField(ref _isLiveLocationVisible, value, nameof(IsLiveLocationVisible));
        }

        [DataSourceProperty]
        public string LiveLocationText
        {
            get => _liveLocationText;
            private set => SetField(ref _liveLocationText, value, nameof(LiveLocationText));
        }

        [DataSourceProperty]
        public string LiveLocationDisplayText
        {
            get => _liveLocationDisplayText;
            private set => SetField(ref _liveLocationDisplayText, value, nameof(LiveLocationDisplayText));
        }
    }

    [PrefabExtension("EncyclopediaHeroPage", "descendant::RichTextWidget[@Text='@LastSeenText']")]
    public sealed class HeroLiveLocationPrefabExtension : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes =>
            new List<PrefabExtensionSetAttributePatch.Attribute>
            {
                new PrefabExtensionSetAttributePatch.Attribute("Text", "@LiveLocationDisplayText")
            };
    }
}
