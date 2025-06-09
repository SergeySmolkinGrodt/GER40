using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GER40SMCBot : Robot
    {
        [Parameter("Instance Label", DefaultValue = "GER40SMCBot_01")]
        public string InstanceLabel { get; set; }

        [Parameter("Volume (Lots)", DefaultValue = 0.1, MinValue = 0.01, Step = 0.01)]
        public double VolumeInLots { get; set; }

        [Parameter("Take Profit RR", DefaultValue = 2, MinValue = 0.1)]
        public double TakeProfitRR { get; set; }
        
        [Parameter("Swing Period", DefaultValue = 5, MinValue = 2)]
        public int SwingPeriod { get; set; }

        private MarketStructure _marketStructure;
        private int _lastTradedBosIndex = -1;

        protected override void OnStart()
        {
            _marketStructure = Indicators.GetIndicator<MarketStructure>(SwingPeriod);
        }

        protected override void OnBar()
        {
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
                        var takeProfitPips = stopLossPips * TakeProfitRR;

                        ExecuteMarketOrder(TradeType.Buy, SymbolName, Symbol.QuantityToVolumeInUnits(VolumeInLots), InstanceLabel, stopLossPips, takeProfitPips);
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
                        var takeProfitPips = stopLossPips * TakeProfitRR;
                        
                        ExecuteMarketOrder(TradeType.Sell, SymbolName, Symbol.QuantityToVolumeInUnits(VolumeInLots), InstanceLabel, stopLossPips, takeProfitPips);
                    }
                }
            }
        }
    }
} 