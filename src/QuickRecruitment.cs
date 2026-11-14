using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;

namespace StorySkip
{
    /// <summary>
    /// Adds filtered selections to the native recruitment cart. Recruitment itself
    /// remains the responsibility of RecruitmentVM.OnDone and its native actions.
    /// </summary>
    public static class QuickRecruitment
    {
        public static bool IsEnabled => ComfortFeatures.QuickRecruitmentEnabled;

        public static event Action<RecruitmentVM> StateChanged;

        public static void InitializePatches(Harmony harmony)
        {
            foreach (string methodName in new[] { "RefreshPartyProperties", "RefreshScreen", "RefreshValues" })
            {
                harmony.Patch(AccessTools.Method(typeof(RecruitmentVM), methodName),
                    postfix: new HarmonyMethod(typeof(QuickRecruitment), nameof(AfterStateChanged)));
            }
        }

        public static bool CanRecruit(RecruitmentVM vm, bool noble)
        {
            if (!CanUseScreen(vm) || GetFreePlaces(vm) <= 0)
                return false;

            long availableGold = GetRemainingGold(vm);
            if (availableGold < 0)
                return false;

            var nobleTrees = new Dictionary<CultureObject, HashSet<CharacterObject>>();
            return GetDisplayedTroops(vm).Any(troop => IsEligible(troop, noble, availableGold, nobleTrees));
        }

        public static void Recruit(RecruitmentVM vm, bool noble)
        {
            if (!CanUseScreen(vm))
                return;

            // Native selection replaces the corresponding cell with an empty VM.
            // Snapshot the original cells just as ExecuteRecruitAll does.
            var troops = GetDisplayedTroops(vm).ToList();
            var nobleTrees = new Dictionary<CultureObject, HashSet<CharacterObject>>();
            foreach (RecruitVolunteerTroopVM troop in troops)
            {
                if (!CanUseScreen(vm) || GetFreePlaces(vm) <= 0)
                    break;

                long availableGold = GetRemainingGold(vm);
                if (availableGold < 0)
                    break;

                if (!IsEligible(troop, noble, availableGold, nobleTrees))
                    continue;

                // This checks the current relation/unlocked-slot state again and
                // stages the same native cart item as an ordinary manual click.
                troop.ExecuteRecruit();
            }

            AfterStateChanged(vm);
        }

        public static bool IsNobleTroop(CharacterObject troop)
        {
            return IsNobleTroop(troop, new Dictionary<CultureObject, HashSet<CharacterObject>>());
        }

        private static bool CanUseScreen(RecruitmentVM vm)
        {
            return IsEnabled && vm != null && vm.Enabled && !vm.IsQuitting
                && vm.VolunteerList != null && vm.TroopsInCart != null;
        }

        private static IEnumerable<RecruitVolunteerTroopVM> GetDisplayedTroops(RecruitmentVM vm)
        {
            foreach (RecruitVolunteerVM owner in vm.VolunteerList)
            {
                if (owner?.Troops == null)
                    continue;
                foreach (RecruitVolunteerTroopVM troop in owner.Troops)
                    if (troop != null)
                        yield return troop;
            }
        }

        private static bool IsEligible(RecruitVolunteerTroopVM troop, bool noble, long availableGold,
            Dictionary<CultureObject, HashSet<CharacterObject>> nobleTrees)
        {
            return troop != null && !troop.IsTroopEmpty && !troop.IsInCart
                && troop.CanBeRecruited && troop.PlayerHasEnoughRelation
                && troop.Character != null && !troop.Character.IsHero
                && troop.Cost >= 0 && troop.Cost <= availableGold
                && IsNobleTroop(troop.Character, nobleTrees) == noble;
        }

        private static bool IsNobleTroop(CharacterObject troop,
            Dictionary<CultureObject, HashSet<CharacterObject>> nobleTrees)
        {
            if (troop == null || troop.IsHero)
                return false;

            CultureObject culture = troop.Culture;
            // Bandit cultures also expose an elite basic troop, sometimes shared
            // with a kingdom. Those do not constitute a kingdom's noble line.
            if (culture == null || !culture.IsMainCulture || culture.EliteBasicTroop == null)
                return false;

            if (!nobleTrees.TryGetValue(culture, out HashSet<CharacterObject> tree))
            {
                tree = GetUpgradeTree(culture.EliteBasicTroop);
                nobleTrees.Add(culture, tree);
            }
            return tree.Contains(troop);
        }

        private static HashSet<CharacterObject> GetUpgradeTree(CharacterObject root)
        {
            var visited = new HashSet<CharacterObject>();
            var pending = new Stack<CharacterObject>();
            if (root != null)
                pending.Push(root);

            while (pending.Count > 0)
            {
                CharacterObject troop = pending.Pop();
                if (troop == null || !visited.Add(troop))
                    continue;
                CharacterObject[] targets = troop.UpgradeTargets;
                if (targets == null)
                    continue;
                foreach (CharacterObject target in targets)
                    if (target != null)
                        pending.Push(target);
            }
            return visited;
        }

        private static long GetRemainingGold(RecruitmentVM vm)
        {
            long wallet = Hero.MainHero?.Gold ?? vm.TotalWealth;
            long cost = 0;
            foreach (RecruitVolunteerTroopVM troop in vm.TroopsInCart)
                if (troop != null)
                    cost += Math.Max(0, troop.Cost);
            return wallet - cost;
        }

        private static int GetFreePlaces(RecruitmentVM vm)
        {
            long occupied = Math.Max((long)vm.CurrentPartySize,
                (long)vm.InitialPartySize + vm.TroopsInCart.Count);
            long free = (long)vm.PartyCapacity - occupied;
            return free <= 0 ? 0 : (int)Math.Min(int.MaxValue, free);
        }

        private static void AfterStateChanged(RecruitmentVM __instance)
        {
            StateChanged?.Invoke(__instance);
        }
    }
}
