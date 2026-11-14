using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Library;

namespace StorySkip
{
    internal static class InstantConstruction
    {
        internal const int MaximumBuildingLevel = 3;
        private static bool _campaignReady;
        private static readonly HashSet<Town> CompletingTowns = new HashSet<Town>();
        private static readonly FieldInfo ManagementSettlement = AccessTools.Field(typeof(TownManagementVM), "_settlement");
        private static readonly MethodInfo RefreshManagementStats = AccessTools.Method(typeof(TownManagementVM), "RefreshTownManagementStats", Type.EmptyTypes);
        private static readonly MethodInfo RefreshManagementDevelopment = AccessTools.Method(typeof(TownManagementVM), "RefreshCurrentDevelopment", Type.EmptyTypes);

        internal static void InitializePatches(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (ManagementSettlement == null || RefreshManagementStats == null || RefreshManagementDevelopment == null)
                throw new MissingMemberException("Building management hooks changed in this Bannerlord version.");
            // Construction is an explicit UI command. Loading a campaign, opening
            // management, or toggling the feature must not upgrade any property.
        }

        internal static void SetCampaignReady(bool ready)
        {
            _campaignReady = ready;
            if (!ready)
                CompletingTowns.Clear();
        }

        internal static Town GetTown(TownManagementVM viewModel)
        {
            return viewModel == null ? null : (ManagementSettlement?.GetValue(viewModel) as Settlement)?.Town;
        }

        internal static bool IsEnabled => _campaignReady && Campaign.Current != null && ComfortFeatures.InstantConstructionEnabled;

        internal static bool CanComplete(Town town)
        {
            return IsEnabled && town?.Settlement != null && Clan.PlayerClan != null && town.OwnerClan == Clan.PlayerClan;
        }

        internal static bool CanMaximize(Town town)
        {
            if (!CanComplete(town) || town.Buildings == null || CompletingTowns.Contains(town))
                return false;
            foreach (Building building in town.Buildings)
            {
                if (IsPermanentBuilding(town, building) && building.CurrentLevel < MaximumBuildingLevel)
                    return true;
            }
            return false;
        }

        private static bool IsPermanentBuilding(Town town, Building building)
        {
            return building?.BuildingType != null && building.Town == town && !building.BuildingType.IsDailyProject;
        }

        internal static int MaximizeBuildings(Town town)
        {
            if (!CanMaximize(town) || !CompletingTowns.Add(town))
                return 0;
            int completed = 0;
            try
            {
                // Building level events can change collections or ownership.
                foreach (Building building in new List<Building>(town.Buildings))
                {
                    if (!IsPermanentBuilding(town, building))
                        continue;
                    while (CanComplete(town) && building.CurrentLevel < MaximumBuildingLevel)
                    {
                        int previousLevel = building.CurrentLevel;
                        building.BuildingProgress = building.GetConstructionCost();
                        // Use native LevelUp for governor rewards and listeners
                        // such as patrols. Directly setting CurrentLevel bypasses them.
                        building.LevelUp();
                        if (building.CurrentLevel <= previousLevel)
                            break;
                        completed++;
                    }
                    if (!CanComplete(town))
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Maximum construction failed: " + ex);
            }
            finally
            {
                // Preserve daily defaults and unfinished projects when an event
                // interrupts this command or adds a building during completion.
                RemoveCompletedQueueEntries(town);
                CompletingTowns.Remove(town);
            }
            return completed;
        }

        private static void RemoveCompletedQueueEntries(Town town)
        {
            if (town.BuildingsInProgress == null)
                return;
            int count = town.BuildingsInProgress.Count;
            while (count-- > 0)
            {
                Building building = town.BuildingsInProgress.Dequeue();
                if (!IsPermanentBuilding(town, building) || building.CurrentLevel < MaximumBuildingLevel)
                    town.BuildingsInProgress.Enqueue(building);
            }
        }

        internal static void RefreshManagement(TownManagementVM viewModel)
        {
            if (viewModel?.ProjectSelection == null)
                return;
            // Native Done resubmits LocalDevelopmentList. Rebuild it from the
            // cleaned real queue before the user closes or edits this screen.
            viewModel.ProjectSelection.Refresh();
            RefreshManagementDevelopment.Invoke(viewModel, null);
            RefreshManagementStats.Invoke(viewModel, null);
        }
    }

    internal sealed class InstantConstructionCampaignBehavior : CampaignBehaviorBase
    {
        internal InstantConstructionCampaignBehavior()
        {
            InstantConstruction.SetCampaignReady(false);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, StartCampaign);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, StartCampaign);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Native town/building serialization stores the resulting levels.
        }

        private void StartCampaign()
        {
            InstantConstruction.SetCampaignReady(true);
        }
    }
}
