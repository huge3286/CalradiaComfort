using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace StorySkip
{
    internal static class MatrilinealMarriage
    {
        internal static void InitializePatches(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(DefaultMarriageModel),
                    nameof(DefaultMarriageModel.GetClanAfterMarriage),
                    new[] { typeof(Hero), typeof(Hero) }),
                postfix: new HarmonyMethod(typeof(MatrilinealMarriage),
                    nameof(AfterClanAfterMarriage)));
            harmony.Patch(
                AccessTools.Method(typeof(DefaultMarriageModel),
                    nameof(DefaultMarriageModel.IsCoupleSuitableForMarriage),
                    new[] { typeof(Hero), typeof(Hero) }),
                postfix: new HarmonyMethod(typeof(MatrilinealMarriage),
                    nameof(AfterCoupleSuitableForMarriage)));
        }

        private static void AfterClanAfterMarriage(Hero __0, Hero __1, ref Clan __result)
        {
            if (GetIncomingGroom(__0, __1) != null)
            {
                // The native action transfers the groom, dispatches clan-change
                // events and handles his party, governorship and old clan leader.
                // The same model also supplies the marriage preview and valuation.
                __result = Clan.PlayerClan;
            }
        }

        private static void AfterCoupleSuitableForMarriage(Hero __0, Hero __1, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            Hero groom = GetIncomingGroom(__0, __1);
            if (groom != null && groom.Clan.Leader == groom &&
                groom.Clan.GetHeirApparents().Count == 0)
            {
                // Native clan succession cannot replace a departing leader when
                // no eligible heir remains. Prevent this marriage before it acts.
                __result = false;
            }
        }

        private static Hero GetIncomingGroom(Hero first, Hero second)
        {
            if (!ComfortFeatures.MatrilinealMarriageEnabled || Campaign.Current == null ||
                first == null || second == null || first.IsFemale == second.IsFemale)
            {
                return null;
            }

            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                return null;
            }

            Hero bride = first.IsFemale ? first : second;
            Hero groom = first.IsFemale ? second : first;
            if (bride.Clan == playerClan && groom.Clan != null && groom.Clan != playerClan)
            {
                return groom;
            }

            return null;
        }
    }
}
