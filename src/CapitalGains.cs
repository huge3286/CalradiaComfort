using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace StorySkip
{
    internal sealed class CapitalGainsCampaignBehavior : CampaignBehaviorBase
    {
        private bool _initialized;
        private int _lastDay;
        private double _fraction;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("comfort_capital_gains_initialized", ref _initialized);
            dataStore.SyncData("comfort_capital_gains_last_day", ref _lastDay);
            dataStore.SyncData("comfort_capital_gains_fraction", ref _fraction);
            if (double.IsNaN(_fraction) || double.IsInfinity(_fraction) || _fraction < 0 || _fraction >= 1)
                _fraction = 0;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Joining an old save starts now; loading never awards historical or offline interest.
            _lastDay = CurrentDay();
            _initialized = true;
        }

        internal static int CurrentDay() => (int)Math.Floor(CampaignTime.Now.ToDays);

        internal static int CalculateDailyPayment(int gold, double annualPercent, int daysInYear,
            ref double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction < 0 || fraction >= 1) fraction = 0;
            if (gold <= 0 || annualPercent <= 0 || daysInYear <= 0) return 0;
            double rate = Math.Pow(1d + annualPercent / 100d, 1d / daysInYear) - 1d;
            double accrued = (gold + fraction) * rate + fraction;
            if (double.IsNaN(accrued) || double.IsInfinity(accrued) || accrued < 0) return 0;
            long capacity = (long)int.MaxValue - gold;
            if (accrued >= capacity)
            {
                fraction = 0;
                return (int)Math.Max(0, capacity);
            }
            int payment = (int)Math.Floor(accrued);
            fraction = accrued - payment;
            return payment;
        }

        private void OnDailyTick()
        {
            int day = CurrentDay();
            if (!_initialized) { _lastDay = day; _initialized = true; return; }
            if (day <= _lastDay) return;
            // Consume the day before native gold events: repeated ticks/reentry cannot pay twice.
            _lastDay = day;
            var hero = Hero.MainHero;
            if (!ComfortFeatures.CapitalGainsEnabled || hero == null || !hero.IsAlive) return;
            int amount = CalculateDailyPayment(hero.Gold, ComfortFeatures.CapitalGainsAnnualPercent,
                CampaignTime.DaysInYear, ref _fraction);
            if (amount <= 0) return;
            try
            {
                GiveGoldAction.ApplyBetweenCharacters(null, hero, amount, false);
                InformationManager.DisplayMessage(new InformationMessage("资本利得：+" + amount + " 第纳尔"));
            }
            catch (Exception ex)
            {
                // A gold-event listener may throw after native has credited the wallet. Never retry it.
                Debug.Print("[CalradiaComfort] Capital gains settlement interrupted: " + ex);
            }
        }
    }
}
