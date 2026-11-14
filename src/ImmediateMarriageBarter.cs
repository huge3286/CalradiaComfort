using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace StorySkip
{
    // A short-lived UI transaction, never a saveable agreement. Submission first
    // frees the adult. Refusing the marriage cannot undo that completed release.
    internal static class ImmediateMarriageBarter
    {
        private static readonly FieldInfo GoldOwnerField = AccessTools.Field(typeof(GoldBarterable), "_ownerHero");
        private static readonly FieldInfo GoldOtherField = AccessTools.Field(typeof(GoldBarterable), "_otherHero");
        private static Session _current;
        private static BarterData _finishedData;

        internal sealed class Session
        {
            internal Hero Owner;
            internal Hero Partner;
            internal PartyBase Party;
            internal PartyBase OtherParty;
            internal BarterManager Manager;
            internal BarterData Data;
            internal MarriageBarterable Marriage;
            internal SetPrisonerFreeBarterable Release;
            internal Action<bool> Completed;
            internal bool ReleaseApplied;
            internal bool MarriageApplied;
            internal bool Committing;
            internal bool Finished;
        }

        internal static void InitializePatches(Harmony harmony)
        {
            if (GoldOwnerField == null || GoldOtherField == null)
                throw new MissingFieldException("Native gold barter participants changed.");
            harmony.Patch(AccessTools.Method(typeof(BarterManager), "BeginPlayerBarter"),
                prefix: Hook(nameof(CaptureData)));
            harmony.Patch(AccessTools.Method(typeof(BarterManager), "IsOfferAcceptable"),
                prefix: Hook(nameof(BeforeOfferAcceptable)),
                postfix: Hook(nameof(AfterOfferAcceptable)));
            harmony.Patch(AccessTools.Method(typeof(BarterManager), "ApplyAndFinalizePlayerBarter"),
                prefix: Hook(nameof(BeforeCommit)), finalizer: Hook(nameof(FinishCommit)));
            harmony.Patch(AccessTools.Method(typeof(SetPrisonerFreeBarterable), "Apply"),
                prefix: Hook(nameof(BeforeApplyRelease)));
            harmony.Patch(AccessTools.Method(typeof(MarriageBarterable), "Apply"),
                prefix: Hook(nameof(BeforeApplyMarriage)));
        }

        private static HarmonyMethod Hook(string method) => new HarmonyMethod(typeof(ImmediateMarriageBarter), method);

        internal static void ClearSession()
        {
            Complete(_current, false);
            _finishedData = null;
        }

        internal static void Open(Hero partner, Action<bool> completed)
        {
            // Opening or canceling the UI never performs release/marriage/payment.
            Complete(_current, false);
            Hero owner = Hero.MainHero;
            if (!CanOpen(owner, partner))
            {
                completed?.Invoke(false);
                return;
            }

            var session = new Session
            {
                Owner = owner,
                Partner = partner,
                Party = PartyBase.MainParty,
                OtherParty = partner.PartyBelongedTo?.Party,
                Manager = Campaign.Current.BarterManager,
                Marriage = new MarriageBarterable(owner, PartyBase.MainParty, partner, owner),
                Release = new SetPrisonerFreeBarterable(partner, owner, PartyBase.MainParty, partner),
                Completed = completed
            };
            _current = session;
            session.Manager.Closed += OnSessionClosed;
            var couple = Tuple.Create(partner, owner);
            try
            {
                session.Manager.StartBarterOffer(owner, partner, session.Party, session.OtherParty,
                    null,
                    (item, data, context) => ReferenceEquals(item, session.Release) ||
                        (ReferenceEquals(item, session.Marriage) &&
                         session.Manager.InitializeMarriageBarterContext(item, data, couple)),
                    0, false, new Barterable[] { session.Marriage, session.Release });
                if (session.Data == null)
                    throw new InvalidOperationException("The marriage barter UI did not bind its data.");
            }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Cannot open immediate marriage barter: " + exception);
                Complete(session, false);
            }
        }

        private static bool CanOpen(Hero owner, Hero partner)
        {
            return ComfortFeatures.ReleasedCaptiveCourtshipEnabled && Campaign.Current != null &&
                Campaign.Current.BarterManager != null && owner != null && partner != null &&
                owner == Hero.MainHero && !owner.IsPrisoner &&
                (PluralMarriage.RawSpouse(owner) == null ||
                 (ComfortFeatures.PluralMarriageEnabled && PluralMarriageCampaignBehavior.Current != null)) &&
                partner.IsPrisoner && PartyBase.MainParty != null &&
                partner.PartyBelongedToAsPrisoner == PartyBase.MainParty &&
                PartyBase.MainParty.PrisonRoster.Contains(partner.CharacterObject) &&
                ReleasedCaptiveCourtshipCampaignBehavior.IsPotentialFutureMatch(owner, partner);
        }

        private static void CaptureData(BarterManager __instance, BarterData __0)
        {
            Session session = _current;
            if (session == null || session.Finished || session.Data != null ||
                session.Manager != __instance || __0 == null ||
                __0.OffererHero != session.Owner || __0.OtherHero != session.Partner ||
                __0.OffererParty != session.Party || __0.OtherParty != session.OtherParty)
                return;
            var items = __0.GetBarterables();
            if (items.Contains(session.Marriage) && items.Contains(session.Release))
                session.Data = __0;
        }

        private static bool IsBoundSession(Session session, BarterManager manager, BarterData data)
        {
            return session != null && !session.Finished && ReferenceEquals(session, _current) &&
                ReferenceEquals(session.Manager, manager) && ReferenceEquals(session.Data, data) &&
                data != null && data.OffererHero == session.Owner && data.OtherHero == session.Partner &&
                data.OffererParty == session.Party && data.OtherParty == session.OtherParty &&
                session.Owner == Hero.MainHero && Campaign.Current != null &&
                Campaign.Current.BarterManager == manager && PartyBase.MainParty == session.Party &&
                data.GetBarterables().Contains(session.Marriage) &&
                data.GetBarterables().Contains(session.Release);
        }

        private static bool HasValidOffer(Session session)
        {
            var offered = session.Data.GetOfferedBarterables();
            if (!offered.Contains(session.Marriage) || !offered.Contains(session.Release) ||
                !session.Marriage.IsContextDependent || !session.Release.IsContextDependent)
                return false;
            long ownerGold = 0;
            long partnerGold = 0;
            foreach (Barterable item in offered)
            {
                if (ReferenceEquals(item, session.Marriage) || ReferenceEquals(item, session.Release))
                    continue;
                if (item == null || item.GetType() != typeof(GoldBarterable) ||
                    item.CurrentAmount < 0 || item.CurrentAmount > item.MaxAmount)
                    return false;
                Hero from = (Hero)GoldOwnerField.GetValue(item);
                Hero to = (Hero)GoldOtherField.GetValue(item);
                if (from == session.Owner && to == session.Partner)
                    ownerGold += item.CurrentAmount;
                else if (from == session.Partner && to == session.Owner)
                    partnerGold += item.CurrentAmount;
                else return false;
            }
            return ownerGold <= session.Owner.Gold && partnerGold <= session.Partner.Gold;
        }

        private static void AfterOfferAcceptable(BarterManager __instance, BarterData __0, ref bool __result)
        {
            Session session = _current;
            if (session != null && ReferenceEquals(session.Data, __0))
                __result &= IsBoundSession(session, __instance, __0) && HasValidOffer(session);
        }

        private static bool BeforeOfferAcceptable(BarterManager __instance, BarterData __0, ref bool __result)
        {
            Session session = _current;
            if ((__0 != null && ReferenceEquals(_finishedData, __0)) ||
                (session != null && ReferenceEquals(session.Data, __0) &&
                 (session.ReleaseApplied || !IsBoundSession(session, __instance, __0) ||
                  !HasValidOffer(session))))
            {
                // A released hero no longer has a captor party: native release
                // valuation must not be called on a stale/committing offer.
                __result = false;
                return false;
            }
            return true;
        }

        private static bool BeforeCommit(BarterManager __instance, Hero __0, Hero __1, BarterData __2,
            out Session __state)
        {
            __state = null;
            // Repeated UI submission must not reapply marriage or gold.
            if (ReferenceEquals(_finishedData, __2) && __2 != null) return false;
            Session session = _current;
            if (session == null || !ReferenceEquals(session.Data, __2)) return true;
            if (session.Committing) return false;
            __state = session;
            session.Committing = true;
            try
            {
                if (__0 != session.Owner || __1 != session.Partner ||
                    !IsBoundSession(session, __instance, __2) || !HasValidOffer(session) ||
                    !CanOpen(session.Owner, session.Partner) ||
                    !__instance.IsOfferAcceptable(__2, session.Partner, session.OtherParty))
                {
                    Cancel(session);
                    return false;
                }
                // Run the genuine pending-release item before any fee or marriage.
                // If later checks fail, this release is deliberately never reversed.
                session.Release.Apply();
                session.ReleaseApplied = !session.Partner.IsPrisoner &&
                    session.Partner.PartyBelongedToAsPrisoner == null &&
                    !session.Party.PrisonRoster.Contains(session.Partner.CharacterObject);
                if (!session.ReleaseApplied)
                {
                    Cancel(session);
                    return false;
                }
                session.Partner.ChangeState(Hero.CharacterStates.Active);
                if (!WillFreelyAgree(session.Owner, session.Partner) ||
                    !Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(session.Owner, session.Partner))
                {
                    Cancel(session);
                    return false;
                }

                // Native MarriageAction may return silently on invalid couples.
                // Verify real spouse/clan effects BEFORE permitting native gold Apply.
                session.Marriage.Apply();
                session.MarriageApplied = HasCompletedMarriage(session);
                if (!session.MarriageApplied)
                {
                    Cancel(session);
                    return false;
                }
                PlaceNewSpouse(session);
                return true;
            }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Immediate marriage submission failed before payment: " + exception);
                Cancel(session);
                return false;
            }
        }

        private static bool WillFreelyAgree(Hero owner, Hero partner)
        {
            if (owner == null || partner == null || owner.IsPrisoner || partner.IsPrisoner ||
                !owner.IsAlive || !partner.IsAlive || owner.Age < 18f || partner.Age < 18f ||
                PluralMarriage.RawSpouse(partner) != null ||
                !ReleasedCaptiveCourtshipCampaignBehavior.IsPotentialFutureMatch(owner, partner))
                return false;
            Romance.RomanceLevelEnum level = Romance.GetRomanticLevel(owner, partner);
            if (level == Romance.RomanceLevelEnum.Ended || level == Romance.RomanceLevelEnum.Rejection ||
                level == Romance.RomanceLevelEnum.FailedInCompatibility ||
                level == Romance.RomanceLevelEnum.FailedInPracticalities)
                return false;
            if (level >= Romance.RomanceLevelEnum.CoupleAgreedOnMarriage &&
                level < Romance.RomanceLevelEnum.Marriage)
                return true;
            return partner.GetRelation(owner) >= 0 &&
                Campaign.Current.Models.RomanceModel.GetAttractionValuePercentage(partner, owner) >= 40;
        }

        private static bool HasCompletedMarriage(Session session)
        {
            return session.Owner.IsAlive && session.Partner.IsAlive && !session.Partner.IsPrisoner &&
                PluralMarriage.RawSpouse(session.Owner) == session.Partner &&
                PluralMarriage.RawSpouse(session.Partner) == session.Owner &&
                session.Owner.Clan != null && session.Partner.Clan == session.Owner.Clan;
        }

        private static bool BeforeApplyRelease(SetPrisonerFreeBarterable __instance)
        {
            Session session = _current;
            return session == null || !session.Committing || !session.ReleaseApplied ||
                !ReferenceEquals(session.Release, __instance);
        }

        private static void PlaceNewSpouse(Session session)
        {
            Hero partner = session.Partner;
            if (!HasCompletedMarriage(session) || partner.PartyBelongedTo != null ||
                partner.CurrentSettlement != null)
                return;
            try
            {
                MobileParty mainParty = MobileParty.MainParty;
                if (mainParty != null && mainParty.Party == session.Party &&
                    !mainParty.MemberRoster.Contains(partner.CharacterObject))
                    AddHeroToPartyAction.Apply(partner, mainParty, false);
            }
            catch (Exception exception)
            {
                // A marriage already made by the native action remains genuine
                // if a downstream party-join listener fails.
                Debug.Print("[CalradiaComfort] New spouse party placement failed: " + exception);
            }
            finally
            {
                if (!partner.IsPrisoner && partner.PartyBelongedTo == null &&
                    partner.CurrentSettlement == null)
                {
                    try { partner.ChangeState(Hero.CharacterStates.Released); }
                    catch (Exception exception)
                    {
                        Debug.Print("[CalradiaComfort] New spouse recovery state failed: " + exception);
                    }
                }
            }
        }

        private static bool BeforeApplyMarriage(MarriageBarterable __instance)
        {
            Session session = _current;
            return session == null || !session.Committing || !session.MarriageApplied ||
                !ReferenceEquals(session.Marriage, __instance);
        }

        private static Exception FinishCommit(Exception __exception, Session __state)
        {
            if (__state != null && !__state.Finished)
            {
                if (__exception != null)
                    Debug.Print("[CalradiaComfort] Native marriage barter finalization failed: " + __exception);
                Complete(__state, __state.MarriageApplied && HasCompletedMarriage(__state));
            }
            return __exception;
        }

        private static void Cancel(Session session)
        {
            RestoreReleasedRecovery(session);
            try { session.Manager.CancelAndFinalizePlayerBarter(session.Owner, session.Partner, session.Data); }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Cannot finalize declined marriage barter: " + exception);
            }
            finally { Complete(session, false); }
        }

        private static void OnSessionClosed()
        {
            Session session = _current;
            if (session != null)
                Complete(session, session.MarriageApplied && HasCompletedMarriage(session));
        }

        private static void Complete(Session session, bool married)
        {
            if (session == null || session.Finished) return;
            if (!married) RestoreReleasedRecovery(session);
            session.Finished = true;
            session.Manager.Closed -= OnSessionClosed;
            _finishedData = session.Data;
            if (ReferenceEquals(_current, session)) _current = null;
            Action<bool> completed = session.Completed;
            session.Completed = null;
            try { completed?.Invoke(married); }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Marriage barter completion listener failed: " + exception);
            }
        }

        private static void RestoreReleasedRecovery(Session session)
        {
            try
            {
                Hero partner = session?.Partner;
                if (partner != null && partner.IsAlive && !partner.IsPrisoner && partner.IsActive &&
                    partner.PartyBelongedTo == null && partner.CurrentSettlement == null &&
                    !HasCompletedMarriage(session))
                {
                    // Native daily recovery watches Released/Fugitive, rather than
                    // partyless Active heroes. Restore the free recovery state when
                    // a declined marriage gave this hero no new party or settlement.
                    // Captivity/rosters are never restored.
                    partner.ChangeState(Hero.CharacterStates.Released);
                }
            }
            catch (Exception exception)
            {
                Debug.Print("[CalradiaComfort] Released hero recovery state failed: " + exception);
            }
        }
    }
}
