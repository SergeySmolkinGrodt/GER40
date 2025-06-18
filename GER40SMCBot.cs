using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
//using cAlgo.Indicators; // No longer needed as the indicator is now a helper class

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GER40SMCBot : Robot
    {
        [Parameter("Instance Label", DefaultValue = "GER40SMCBot_01")]
        public string InstanceLabel { get; set; }

        [Parameter("Take Profit RR", DefaultValue = 2, MinValue = 0.1)]
        public double TakeProfitRR { get; set; }
        
        [Parameter("Swing Period", DefaultValue = 5, MinValue = 2)]
        public int SwingPeriod { get; set; }

        private MarketStructure _marketStructure;
        private int _lastTradedBosIndex = -1;

        protected override void OnStart()
        {
            _marketStructure = new MarketStructure(this, SwingPeriod);
        }

        protected override void OnBar()
        {
            _marketStructure.Update();
            
            // Ensure there are no open positions managed by this bot instance
            if (Positions.FindAll(InstanceLabel).Length > 0)
            {
                return;
            }

            var lastConfirmedBos = _marketStructure.BosEvents
                .Where(b => b.IsConfirmed)
                .OrderBy(b => b.EndIndex)
                .LastOrDefault();

            if (lastConfirmedBos != null && lastConfirmedBos.EndIndex > _lastTradedBosIndex)
            {
                // Prevent trading the same signal again
                _lastTradedBosIndex = lastConfirmedBos.EndIndex;

                if (lastConfirmedBos.IsBullish)
                {
                    var lastSwingLow = _marketStructure.StructurePoints
                        .Where(p => p.Type == SwingType.Low && p.Index < lastConfirmedBos.EndIndex)
                        .OrderBy(p => p.Index)
                        .LastOrDefault();
                    
                    if (lastSwingLow != null)
                    {
                        var stopLossPrice = lastSwingLow.Price;
                        var entryPrice = Symbol.Ask;
                        var stopLossPips = (entryPrice - stopLossPrice) / Symbol.PipSize;

                        if (stopLossPips <= 0) return;

                        var volumeInUnits = Symbol.VolumeForProportionalRisk(ProportionalAmountType.Balance, 1, stopLossPips, RoundingMode.Down);
                        if (volumeInUnits == 0) return;

                        var takeProfitPips = stopLossPips * TakeProfitRR;

                        ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, InstanceLabel, stopLossPips, takeProfitPips);
                    }
                }
                else // IsBearish
                {
                    var lastSwingHigh = _marketStructure.StructurePoints
                        .Where(p => p.Type == SwingType.High && p.Index < lastConfirmedBos.EndIndex)
                        .OrderBy(p => p.Index)
                        .LastOrDefault();

                    if (lastSwingHigh != null)
                    {
                        var stopLossPrice = lastSwingHigh.Price;
                        var entryPrice = Symbol.Bid;
                        var stopLossPips = (stopLossPrice - entryPrice) / Symbol.PipSize;

                        if (stopLossPips <= 0) return;

                        var volumeInUnits = Symbol.VolumeForProportionalRisk(ProportionalAmountType.Balance, 1, stopLossPips, RoundingMode.Down);
                        if (volumeInUnits == 0) return;
                        
                        var takeProfitPips = stopLossPips * TakeProfitRR;
                        
                        ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, InstanceLabel, stopLossPips, takeProfitPips);
                    }
                }
            }
        }
    }
