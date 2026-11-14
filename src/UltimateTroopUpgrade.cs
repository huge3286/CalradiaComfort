using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StorySkip
{
    internal sealed class UltimateUpgradeOption
    {
        internal CharacterObject Source;
        internal CharacterObject Target;
        internal CharacterObject[] Troops;
        internal int[] Branches;
    }

    internal static class UltimateTroopUpgrade
    {
        private sealed class RouteBudget
        {
            internal long Xp;
            internal long Gold;
            internal int LargestXp;
            internal int LargestGold;
            internal readonly Dictionary<ItemCategory, int> Items = new Dictionary<ItemCategory, int>();
        }

        private static readonly HashSet<PartyScreenLogic> Executing = new HashSet<PartyScreenLogic>();
        internal static bool IsActive => ComfortFeatures.UltimateTroopUpgradeEnabled && SharedTroopExperience.IsActive;

        internal static List<UltimateUpgradeOption> GetOptions(CharacterObject source)
        {
            var result = new List<UltimateUpgradeOption>();
            if (source == null || source.IsHero)
                return result;
            var queue = new Queue<UltimateUpgradeOption>();
            var visited = new HashSet<CharacterObject> { source };
            queue.Enqueue(new UltimateUpgradeOption
            {
                Source = source, Target = source, Troops = new[] { source }, Branches = new int[0]
            });
            // Native trees are small. Bound traversal for malformed modded graphs.
            while (queue.Count > 0 && visited.Count <= 2048)
            {
                UltimateUpgradeOption path = queue.Dequeue();
                CharacterObject[] targets = path.Target.UpgradeTargets;
                if (targets == null || targets.Length == 0)
                {
                    if (path.Branches.Length > 0)
                        result.Add(path);
                    continue;
                }
                if (path.Branches.Length >= 64)
                    continue;
                for (int branch = 0; branch < targets.Length; branch++)
                {
                    CharacterObject target = targets[branch];
                    if (target == null || target.IsHero || target.Level < path.Target.Level || !visited.Add(target))
                        continue;
                    var troops = new CharacterObject[path.Troops.Length + 1];
                    var branches = new int[path.Branches.Length + 1];
                    Array.Copy(path.Troops, troops, path.Troops.Length);
                    Array.Copy(path.Branches, branches, path.Branches.Length);
                    troops[troops.Length - 1] = target;
                    branches[branches.Length - 1] = branch;
                    queue.Enqueue(new UltimateUpgradeOption
                    {
                        Source = source, Target = target, Troops = troops, Branches = branches
                    });
                }
            }
            return result;
        }

        private static bool TryGetContext(PartyVM party, PartyCharacterVM troop,
            out PartyScreenLogic logic, out TroopRoster roster)
        {
            logic = party?.PartyScreenLogic;
            roster = null;
            if (!IsActive || logic == null || logic.CurrentData == null || logic.IsTroopUpgradesDisabled ||
                troop?.Character == null || troop.IsHero || troop.IsPrisoner ||
                troop.Side != PartyScreenLogic.PartyRosterSide.Right || troop.Type != PartyScreenLogic.TroopType.Member ||
                !ReferenceEquals(logic.RightOwnerParty, PartyBase.MainParty) ||
                !SharedTroopExperience.AppliesTo(logic, troop.Side, troop.Type) ||
                party.MainPartyTroops == null || !party.MainPartyTroops.Contains(troop))
                return false;
            roster = logic.GetRoster(troop.Side, troop.Type);
            return roster != null && roster.FindIndexOfTroop(troop.Character) >= 0;
        }

        private static bool TryGetBudget(UltimateUpgradeOption option, out RouteBudget budget)
        {
            budget = null;
            if (option?.Source == null || option.Target == null || option.Troops == null || option.Branches == null ||
                option.Branches.Length == 0 || option.Branches.Length > 64 ||
                option.Troops.Length != option.Branches.Length + 1 ||
                !ReferenceEquals(option.Troops[0], option.Source) ||
                !ReferenceEquals(option.Troops[option.Troops.Length - 1], option.Target) ||
                (option.Target.UpgradeTargets != null && option.Target.UpgradeTargets.Length != 0))
                return false;
            var model = Campaign.Current?.Models.PartyTroopUpgradeModel;
            if (model == null)
                return false;
            var calculated = new RouteBudget();
            var seen = new HashSet<CharacterObject> { option.Source };
            for (int edge = 0; edge < option.Branches.Length; edge++)
            {
                CharacterObject source = option.Troops[edge];
                CharacterObject target = option.Troops[edge + 1];
                int branch = option.Branches[edge];
                CharacterObject[] targets = source?.UpgradeTargets;
                PerkObject requiredPerk;
                if (source == null || source.IsHero || target == null || target.IsHero ||
                    targets == null || branch < 0 || branch >= targets.Length ||
                    !ReferenceEquals(targets[branch], target) || !seen.Add(target) || target.Level < source.Level ||
                    !model.IsTroopUpgradeable(PartyBase.MainParty, source) ||
                    !model.DoesPartyHaveRequiredPerksForUpgrade(PartyBase.MainParty, source, target, out requiredPerk))
                    return false;
                int xp = source.GetUpgradeXpCost(PartyBase.MainParty, branch);
                int gold = source.GetUpgradeGoldCost(PartyBase.MainParty, branch);
                if (xp < 0 || gold < 0)
                    return false;
                calculated.Xp += xp;
                calculated.Gold += gold;
                calculated.LargestXp = Math.Max(calculated.LargestXp, xp);
                calculated.LargestGold = Math.Max(calculated.LargestGold, gold);
                ItemCategory category = target.UpgradeRequiresItemFromCategory;
                if (category != null)
                {
                    int count;
                    calculated.Items.TryGetValue(category, out count);
                    calculated.Items[category] = count + 1;
                }
            }
            budget = calculated;
            return true;
        }

        internal static bool CanUpgrade(PartyVM party, PartyCharacterVM troop)
        {
            PartyScreenLogic logic;
            TroopRoster roster;
            if (!TryGetContext(party, troop, out logic, out roster))
                return false;
            foreach (UltimateUpgradeOption option in GetOptions(troop.Character))
                if (GetAffordableCount(party, troop, option) > 0)
                    return true;
            return false;
        }

        internal static int GetAffordableCount(PartyVM party, PartyCharacterVM troop, UltimateUpgradeOption option)
        {
            PartyScreenLogic logic;
            TroopRoster roster;
            RouteBudget budget;
            if (!TryGetContext(party, troop, out logic, out roster) || option == null ||
                !ReferenceEquals(option.Source, troop.Character) || !TryGetBudget(option, out budget))
                return 0;
            int index = roster.FindIndexOfTroop(option.Source);
            long count = Math.Max(0, roster.GetElementNumber(index) - roster.GetElementWoundedNumber(index));
            if (budget.Xp > 0)
                count = Math.Min(count, SharedTroopExperience.GetAvailableXp(logic) / budget.Xp);
            if (budget.Gold > 0)
            {
                Hero leader = logic.RightPartyLeader?.HeroObject;
                if (leader == null)
                    return 0;
                long delta = logic.CurrentData.PartyGoldChangeAmount;
                count = Math.Min(count, Math.Max(0, (long)leader.Gold + delta) / budget.Gold);
                count = Math.Min(count, (delta - int.MinValue) / budget.Gold);
            }
            if (budget.LargestXp > 0)
                count = Math.Min(count, int.MaxValue / budget.LargestXp);
            if (budget.LargestGold > 0)
                count = Math.Min(count, int.MaxValue / budget.LargestGold);
            ItemRoster inventory = logic.RightOwnerParty.ItemRoster;
            foreach (var requirement in budget.Items)
            {
                long available = 0;
                for (int itemIndex = 0; itemIndex < inventory.Count; itemIndex++)
                {
                    ItemRosterElement item = inventory.GetElementCopyAtIndex(itemIndex);
                    if (ReferenceEquals(item.EquipmentElement.Item?.ItemCategory, requirement.Key))
                        available += Math.Max(0, item.Amount);
                }
                count = Math.Min(count, available / requirement.Value);
            }
            return (int)Math.Max(0, count);
        }

        internal static string GetOptionDescription(PartyVM party, PartyCharacterVM troop, UltimateUpgradeOption option)
        {
            RouteBudget budget;
            if (!TryGetBudget(option, out budget))
                return new TextObject("{=cc_route_invalid}This route does not meet the normal upgrade requirements.").ToString();
            var cost = new TextObject("{=cc_route_cost}Per troop: {XP} XP, {GOLD} gold")
                .SetTextVariable("XP", budget.Xp.ToString()).SetTextVariable("GOLD", budget.Gold.ToString());
            var text = new StringBuilder(cost.ToString());
            foreach (var item in budget.Items)
                text.Append(", ").Append(item.Key.GetName()).Append(" × ").Append(item.Value);
            text.Append('\n').Append(new TextObject("{=cc_route_count}Can reach final tier: {COUNT}")
                .SetTextVariable("COUNT", GetAffordableCount(party, troop, option)));
            text.Append('\n').Append(new TextObject("{=cc_route_path}Route:")).Append(' ');
            for (int i = 0; i < option.Troops.Length; i++)
            {
                if (i != 0) text.Append(" → ");
                text.Append(option.Troops[i].Name);
            }
            return text.ToString();
        }

        internal static int UpgradeTo(PartyVM party, PartyCharacterVM troop, UltimateUpgradeOption option)
        {
            PartyScreenLogic logic;
            TroopRoster roster;
            if (!TryGetContext(party, troop, out logic, out roster) || Executing.Contains(logic))
                return 0;
            int count = GetAffordableCount(party, troop, option);
            if (count <= 0)
                return 0;
            Executing.Add(logic);
            bool started = false;
            try
            {
                for (int edge = 0; edge < option.Branches.Length; edge++)
                {
                    CharacterObject source = option.Troops[edge];
                    CharacterObject target = option.Troops[edge + 1];
                    PartyCharacterVM row = FindRow(party, source);
                    int sourceIndex = roster.FindIndexOfTroop(source);
                    if (row == null || sourceIndex < 0)
                        throw new InvalidOperationException("Upgrade source row is unavailable.");
                    int sourceBefore = roster.GetElementNumber(sourceIndex);
                    int targetBefore = GetNumber(roster, target);
                    // Native VM's update callback uses CurrentCharacter, so it
                    // must follow the source of every individual native command.
                    party.CurrentCharacter = row;
                    int insertIndex = logic.GetIndexToInsertTroop(PartyScreenLogic.PartyRosterSide.Right,
                        PartyScreenLogic.TroopType.Member, roster.GetElementCopyAtIndex(sourceIndex));
                    var command = new PartyScreenLogic.PartyCommand();
                    command.FillForUpgradeTroop(PartyScreenLogic.PartyRosterSide.Right,
                        PartyScreenLogic.TroopType.Member, source, count, option.Branches[edge], insertIndex);
                    if (!logic.ValidateCommand(command))
                        throw new InvalidOperationException("Native upgrade validation failed.");
                    started = true;
                    logic.AddCommand(command);
                    if (sourceBefore - GetNumber(roster, source) != count ||
                        GetNumber(roster, target) - targetBefore != count)
                        throw new InvalidOperationException("Native upgrade did not complete the requested step.");
                }
                return count;
            }
            catch (Exception ex)
            {
                Debug.Print("[CalradiaComfort] Ultimate troop upgrade failed: " + ex);
                if (!started)
                    return 0;
                // The native upgrade popup owns this checkpoint. Never overwrite
                // it: Cancel must still undo every change made since it opened.
                logic.ResetToLastSavedPartyScreenData(false);
                return -1;
            }
            finally
            {
                Executing.Remove(logic);
            }
        }

        private static PartyCharacterVM FindRow(PartyVM party, CharacterObject character)
        {
            foreach (PartyCharacterVM row in party.MainPartyTroops)
                if (ReferenceEquals(row.Character, character))
                    return row;
            return null;
        }

        private static int GetNumber(TroopRoster roster, CharacterObject character)
        {
            int index = roster.FindIndexOfTroop(character);
            return index < 0 ? 0 : roster.GetElementNumber(index);
        }
    }
}
