using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GER40Bot : Robot
    {
        [Parameter("Symbol Name", DefaultValue = "GER40")]
        public new string SymbolName { get; set; }

        [Parameter("Risk per Trade (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
        public double RiskPercent { get; set; }

        [Parameter("Take Profit RR Ratio", DefaultValue = 2.0, MinValue = 0.5)]
        public double TakeProfitRR { get; set; }

        [Parameter("Entry Hour (UTC+3)", DefaultValue = 9, MinValue = 0, MaxValue = 23)]
        public int EntryHourUTC3 { get; set; }

        [Parameter("Entry Minute", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int EntryMinute { get; set; }

        [Parameter("CCI Period", DefaultValue = 14, MinValue = 1)]
        public int CCIPeriod { get; set; }

        [Parameter("CCI Level", DefaultValue = 100, MinValue = 10)]
        public int CCILevel { get; set; }

        [Parameter("Enable Trailing Stop", DefaultValue = true)]
        public bool EnableTrailingStop { get; set; }

        [Parameter("Trailing Stop Pips (0 = use initial SL)", DefaultValue = 0, MinValue = 0)]
        public double TrailingStopPipsParam { get; set; }

        [Parameter("S/R Lookback Period (H1)", Group = "Stop Loss Settings", DefaultValue = 12, MinValue = 3, MaxValue = 50)]
        public int SRLookbackPeriod { get; set; }

        [Parameter("S/R Buffer Pips", Group = "Stop Loss Settings", DefaultValue = 5, MinValue = 0, MaxValue = 20)]
        public double SRBufferPips { get; set; }

        private CommodityChannelIndex _cci;
        private bool _tradeExecutedToday = false;
        private int _entryHourUTC;
        private cAlgo.API.Internals.Bars _h1Series; // For S/R calculation

        protected override void OnStart()
        {
            Print("GER40 Bot started.");
            Print($"Symbol: {SymbolName}");
            Print($"Risk per trade: {RiskPercent}%");
            Print($"Take Profit RR: 1:{TakeProfitRR}");
            Print($"Entry time (UTC+3): {EntryHourUTC3:00}:{EntryMinute:00}");

            _entryHourUTC = EntryHourUTC3 - 3;
            if (_entryHourUTC < 0)
            {
                _entryHourUTC += 24;
            }
            Print($"Calculated UTC Entry Hour: {_entryHourUTC}");

            _cci = Indicators.CommodityChannelIndex(CCIPeriod);
            _h1Series = MarketData.GetBars(TimeFrame.Hour1);

            Timer.Start(TimeSpan.FromHours(1)); 
        }

        protected override void OnTimer()
        {
            if (Server.Time.TimeOfDay < TimeSpan.FromHours(1) && _tradeExecutedToday)
            {
                Print("New trading day. Resetting daily trade flag.");
                _tradeExecutedToday = false;
            }
        }

        protected override void OnTick()
        {
            if (!_tradeExecutedToday && Server.Time.Hour == _entryHourUTC && Server.Time.Minute == EntryMinute)
            {
                Print($"Attempting to enter trade at {Server.Time}");
                OpenTrade();
            }

            if (EnableTrailingStop)
            {
                ManageTrailingStops();
            }
        }
        
        private void OpenTrade()
        {
            if (Positions.Count > 0 || PendingOrders.Any(o => o.SymbolName == SymbolName))
            {
                Print("A position or pending order already exists for this symbol. Skipping new trade.");
                return;
            }

            TradeType tradeType = DetermineTradeDirection();

            if (tradeType == TradeType.Buy || tradeType == TradeType.Sell)
            {
                var symbol = Symbols.GetSymbol(SymbolName);
                if (symbol == null)
                {
                    Print($"Error: Symbol {SymbolName} not found.");
                    return;
                }

                double stopLossPips = CalculateStopLossPips(symbol, tradeType);
                if (stopLossPips <= 0)
                {
                    Print("Invalid Stop Loss pips calculated. Cannot open trade.");
                    return;
                }

                double positionSize = CalculatePositionSize(symbol, stopLossPips);
                if (positionSize <= 0)
                {
                    Print("Invalid position size calculated. Cannot open trade.");
                    return;
                }
                
                string label = $"GER40Bot_{DateTime.UtcNow:yyyyMMddHHmmss}";
                var result = ExecuteMarketOrder(tradeType, SymbolName, positionSize, label, stopLossPips, stopLossPips * TakeProfitRR);

                if (result.IsSuccessful)
                {
                    Print($"Trade opened: {result.Position.Id}, Type: {tradeType}, Size: {positionSize}, SL pips: {stopLossPips}, TP pips: {stopLossPips * TakeProfitRR}");
                    _tradeExecutedToday = true;
                }
                else
                {
                    Print($"Error opening trade: {result.Error}");
                }
            }
            else
            {
                Print("No trade signal based on CCI.");
            }
        }

        private TradeType DetermineTradeDirection()
        {
            double cciCurrent = _cci.Result.LastValue;
            double cciPrevious = _cci.Result.Last(1);

            Print($"Current CCI({CCIPeriod}): {cciCurrent}, Previous: {cciPrevious}, Level: {CCILevel}");

            if (cciPrevious < CCILevel && cciCurrent > CCILevel)
            {
                Print($"CCI crossed above +{CCILevel}. Signaling LONG.");
                return TradeType.Buy;
            }
            else if (cciPrevious > -CCILevel && cciCurrent < -CCILevel)
            {
                Print($"CCI crossed below -{CCILevel}. Signaling SHORT.");
                return TradeType.Sell;
            }
            
            Print("CCI did not cross signal levels. No signal.");
            return default; 
        }

        private double CalculateStopLossPips(Symbol symbol, TradeType tradeType)
        {
            if (_h1Series.Count < SRLookbackPeriod)
            {
                Print($"Not enough H1 bars for S/R calculation ({_h1Series.Count}/{SRLookbackPeriod}). Using ATR based SL.");
                return CalculateAtrStopLossPipsWithMultiplier(symbol, 1.5); 
            }

            double relevantLevel;
            double currentPrice;

            if (tradeType == TradeType.Buy)
            {
                currentPrice = symbol.Ask;
                double lowestLow = double.MaxValue;
                for (int i = 1; i <= SRLookbackPeriod; i++)
                {
                    if (i >= _h1Series.Count) break; // Ensure index is within bounds
                    if (_h1Series.LowPrices[i] < lowestLow)
                    {
                        lowestLow = _h1Series.LowPrices[i];
                    }
                }
                relevantLevel = lowestLow;
                if (relevantLevel == double.MaxValue || relevantLevel >= currentPrice) 
                {
                    Print($"S/R (Support {relevantLevel}) is invalid or at/above current Ask {currentPrice}. Fallback to ATR SL.");
                    return CalculateAtrStopLossPipsWithMultiplier(symbol, 1.5);
                }
                double slPips = (currentPrice - relevantLevel) / symbol.PipSize + SRBufferPips;
                Print($"Trade Type: Buy. Current Ask: {currentPrice}. Lowest Low ({SRLookbackPeriod} H1 bars): {relevantLevel}. SL Pips (incl. buffer): {slPips}");
                return Math.Max(slPips, 10); 
            }
            else if (tradeType == TradeType.Sell)
            {
                currentPrice = symbol.Bid; 
                double highestHigh = double.MinValue;
                for (int i = 1; i <= SRLookbackPeriod; i++)
                {
                    if (i >= _h1Series.Count) break; // Ensure index is within bounds
                    if (_h1Series.HighPrices[i] > highestHigh)
                    {
                        highestHigh = _h1Series.HighPrices[i];
                    }
                }
                relevantLevel = highestHigh;
                if (relevantLevel == double.MinValue || relevantLevel <= currentPrice) 
                {
                    Print($"S/R (Resistance {relevantLevel}) is invalid or at/below current Bid {currentPrice}. Fallback to ATR SL.");
                    return CalculateAtrStopLossPipsWithMultiplier(symbol, 1.5);
                }
                double slPips = (relevantLevel - currentPrice) / symbol.PipSize + SRBufferPips;
                Print($"Trade Type: Sell. Current Bid: {currentPrice}. Highest High ({SRLookbackPeriod} H1 bars): {relevantLevel}. SL Pips (incl. buffer): {slPips}");
                return Math.Max(slPips, 10); 
            }
            else
            {
                Print("Invalid trade type for S/R SL calculation. Fallback to ATR SL.");
                return CalculateAtrStopLossPipsWithMultiplier(symbol, 1.5);
            }
        }

        // ATR SL for S/R fallback, takes a multiplier
        private double CalculateAtrStopLossPipsWithMultiplier(Symbol symbol, double atrMultiplier)
        {
            var atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple); 
            double atrValue = atr.Result.Last(1); 
            if (double.IsNaN(atrValue) || atrValue == 0)
            {
                Print($"ATR for SL fallback is not available or zero. Using a default of 30 pips.");
                return 30; 
            }
            double stopLossDistanceInPrice = atrValue * atrMultiplier; 
            return Math.Max(stopLossDistanceInPrice / symbol.PipSize, 10); 
        }

        private double CalculatePositionSize(Symbol symbol, double stopLossPips)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double stopLossInQuoteCurrency = stopLossPips * symbol.PipValue; 

            if (stopLossInQuoteCurrency <= 0)
            {
                Print("Error: Stop loss in quote currency is zero or negative. Cannot calculate position size.");
                return 0;
            }
            
            double volumeInUnits = riskAmount / stopLossInQuoteCurrency; 
            double lots = symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);

            Print($"Calculating Position Size: Risk Amount: {riskAmount}, SL Pips: {stopLossPips}, PipValue: {symbol.PipValue}, VolumeInUnits: {volumeInUnits}, Lots: {lots}");

            if (lots < symbol.VolumeInUnitsMin)
            {
                Print($"Calculated lots {lots} is less than minimum {symbol.VolumeInUnitsMin}. Adjusting to minimum.");
                return symbol.VolumeInUnitsMin;
            }
            if (lots > symbol.VolumeInUnitsMax)
            {
                 Print($"Calculated lots {lots} is greater than maximum {symbol.VolumeInUnitsMax}. Adjusting to maximum.");
                return symbol.VolumeInUnitsMax;
            }

            return lots;
        }
        
        private void ManageTrailingStops()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName != SymbolName) continue;

                double trailingStopDistancePips;
                if (TrailingStopPipsParam > 0)
                {
                    trailingStopDistancePips = TrailingStopPipsParam;
                }
                else
                {
                    var symbol = Symbols.GetSymbol(SymbolName);
                    if (symbol == null) continue;
                    trailingStopDistancePips = GetTrailingStopAtrDistancePips(symbol); 
                }

                if (trailingStopDistancePips <= 0) continue; 

                if (position.TradeType == TradeType.Buy)
                {
                    double newStopLossPrice = Symbol.Ask - trailingStopDistancePips * Symbol.PipSize;
                    if (position.StopLoss == null || newStopLossPrice > position.StopLoss)
                    {
                        if (newStopLossPrice > position.EntryPrice) 
                        {
                            position.ModifyStopLossPrice(newStopLossPrice, StopTriggerMethod.Trade);
                            Print($"Trailing Buy SL for {position.Id} to {newStopLossPrice}");
                        }
                    }
                }
                else if (position.TradeType == TradeType.Sell)
                {
                    double newStopLossPrice = Symbol.Bid + trailingStopDistancePips * Symbol.PipSize;
                    if (position.StopLoss == null || newStopLossPrice < position.StopLoss)
                    {
                        if (newStopLossPrice < position.EntryPrice)
                        {
                             position.ModifyStopLossPrice(newStopLossPrice, StopTriggerMethod.Trade);
                             Print($"Trailing Sell SL for {position.Id} to {newStopLossPrice}");
                        }
                    }
                }
            }
        }

        // Dedicated ATR distance calculation for Trailing Stop
        private double GetTrailingStopAtrDistancePips(Symbol symbol)
        {
            var atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple); 
            double atrValue = atr.Result.Last(1); 
            if (double.IsNaN(atrValue) || atrValue == 0)
            {
                Print("ATR for trailing stop is not available or zero. Using a default of 30 pips.");
                return 30; 
            }
            double stopDistanceInPrice = atrValue * 1.5; 
            return Math.Max(stopDistanceInPrice / symbol.PipSize, 5); 
        }

        protected override void OnStop()
        {
            Print("GER40 Bot stopped.");
            Timer.Stop();
        }
    }
}
