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
        public string SymbolName { get; set; }

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

        private CommodityChannelIndex _cci;
        private bool _tradeExecutedToday = false;
        private int _entryHourUTC;

        protected override void OnStart()
        {
            Print("GER40 Bot started.");
            Print($"Symbol: {SymbolName}");
            Print($"Risk per trade: {RiskPercent}%");
            Print($"Take Profit RR: 1:{TakeProfitRR}");
            Print($"Entry time (UTC+3): {EntryHourUTC3:00}:{EntryMinute:00}");

            // Convert UTC+3 entry time to UTC server time
            _entryHourUTC = EntryHourUTC3 - 3;
            if (_entryHourUTC < 0)
            {
                _entryHourUTC += 24; // Handle previous day if entry time was early morning UTC+3
            }
            Print($"Calculated UTC Entry Hour: {_entryHourUTC}");

            _cci = Indicators.CommodityChannelIndex(CCIPeriod);

            // Reset daily trade flag at the start of a new trading day (approximately)
            Timer.Start(TimeSpan.FromHours(1)); 
        }

        protected override void OnTimer()
        {
            // Reset the trade executed flag at the start of a new day (server time)
            if (Server.Time.TimeOfDay < TimeSpan.FromHours(1) && _tradeExecutedToday)
            {
                Print("New trading day. Resetting daily trade flag.");
                _tradeExecutedToday = false;
            }
        }

        protected override void OnTick()
        {
            // Check if it's time to trade and no trade has been executed today
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

            // For now, let's assume a long trade. CCI logic will determine direction.
            // We need to decide how CCI will be used (e.g., CCI > 0 for Long, CCI < 0 for Short)
            // This is a placeholder for actual signal generation.
            TradeType tradeType = DetermineTradeDirection();

            if (tradeType == TradeType.Buy || tradeType == TradeType.Sell)
            {
                var symbol = Symbols.GetSymbol(SymbolName);
                if (symbol == null)
                {
                    Print($"Error: Symbol {SymbolName} not found.");
                    return;
                }

                double stopLossPips = CalculateStopLossPips(symbol);
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
                    // Trailing stop will be managed in OnTick by ManageTrailingStops()
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
            // Placeholder: Implement CCI logic here
            // Example: If CCI > 0 buy, if CCI < 0 sell
            // For now, let's make it always buy for testing
            double cciCurrent = _cci.Result.LastValue;
            double cciPrevious = _cci.Result.Last(1);

            Print($"Current CCI({CCIPeriod}): {cciCurrent}, Previous: {cciPrevious}, Level: {CCILevel}");

            // Buy signal: CCI crosses above +CCILevel
            if (cciPrevious < CCILevel && cciCurrent > CCILevel)
            {
                Print($"CCI crossed above +{CCILevel}. Signaling LONG.");
                return TradeType.Buy;
            }
            // Sell signal: CCI crosses below -CCILevel
            else if (cciPrevious > -CCILevel && cciCurrent < -CCILevel)
            {
                Print($"CCI crossed below -{CCILevel}. Signaling SHORT.");
                return TradeType.Sell;
            }
            
            Print("CCI did not cross signal levels. No signal.");
            return default; // No trade
        }

        private double CalculateStopLossPips(Symbol symbol)
        {
            // SL is 1% of account balance
            double stopLossAmount = Account.Balance * (RiskPercent / 100.0);
            
            // Calculate pips based on symbol's pip value
            // This needs to consider the risk per trade, not just account balance for SL points.
            // The position size will be adjusted to meet the risk % for this SL in pips.
            // Let's set SL based on a price distance first, e.g., 100 pips,
            // then position size will be calculated based on that.
            // OR, if SL is fixed at 1% of capital, then the *distance* in pips depends on position size.
            // The problem states "Стоп лосс давай сделаем на процент от торгового капитала 1%" - this typically means the monetary loss.
            // The "1% риск на сделку фиксировано" usually dictates the position size *given* a stop-loss distance.

            // Let's reinterpret: The *monetary* SL is 1% of capital.
            // We need a way to define the SL *distance* in pips.
            // Let's use a fixed pip distance for SL for now, and then calculate position size.
            // Or, use ATR for stop loss distance.
            // For now, let's use a fixed example like 50 pips for GER40 (Germany40 usually has large movements)
            // User specified "Стоп лосс давай сделаем на процент от торгового капитала 1%".
            // This is the monetary value. The number of pips for this SL depends on lot size.
            // The "1% риск на сделку фиксировано" usually defines the lot size based on a pip-defined SL.
            // This is a bit circular. Let's assume the 1% capital is the max loss *amount*.
            // We need a logical way to set the initial SL *distance*. Let's use an ATR multiple as a common practice.
            
            var atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
            double atrValue = atr.Result.Last(1); // Use previous bar's ATR
            if (double.IsNaN(atrValue) || atrValue == 0)
            {
                Print("ATR is not available or zero. Using a default SL of 50 pips.");
                atrValue = 50 * symbol.PipSize; // Default to 50 pips if ATR is not good
            }
            
            // Let's use 1 * ATR for SL distance in pips
            double stopLossDistanceInPrice = atrValue * 1.5; // ATR value is already in price, not pips
            double stopLossInPips = stopLossDistanceInPrice / symbol.PipSize;

            Print($"ATR(14): {atrValue}, Calculated SL distance in price: {stopLossDistanceInPrice}, SL in pips: {stopLossInPips}");
            
            // Ensure SL is at least a minimum number of pips
            return Math.Max(stopLossInPips, symbol.PipSize * 10 / symbol.PipSize); // Min 10 pips
        }

        private double CalculatePositionSize(Symbol symbol, double stopLossPips)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double stopLossInQuoteCurrency = stopLossPips * symbol.PipValue; // This PipValue is for 1 unit of base currency

            if (stopLossInQuoteCurrency <= 0)
            {
                Print("Error: Stop loss in quote currency is zero or negative. Cannot calculate position size.");
                return 0;
            }
            
            // Volume in units of base currency
            double volumeInUnits = riskAmount / (stopLossPips * symbol.PipValue); 
            
            // Convert to lots
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
        
        // Placeholder for Trailing Stop Logic
        // private void ActivateTrailingStop(Position position, double initialStopLossPips)
        // {
        //     // This needs to be called on tick or bar to adjust SL
        //     Print($"Trailing Stop activated for position {position.Id}");
        // }

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
                    // If TrailingStopPipsParam is 0, use the initial SL distance of the trade
                    // We need to have stored this or recalculate it. It's better to store it or derive it.
                    // For now, let's assume initial SL pips was what was used for CalculateStopLossPips
                    // This is a simplification; ideally, initial SL pips for *this specific trade* should be known.
                    // Let's try to retrieve it from the position if possible, or re-calculate based on current conditions (less ideal for TS).
                    // A common approach for trailing stop is to use a fixed pip distance or ATR based distance set at time of trade.
                    // If we want it to be truly dynamic based on the *initial* SL, we need to persist that value with the trade.
                    // For simplicity in this step, if TrailingStopPipsParam is 0, we'll re-calculate an ATR based SL distance.
                    // This means the trailing stop will adjust to current ATR if not specified.
                    var symbol = Symbols.GetSymbol(SymbolName);
                    if (symbol == null) continue;
                    trailingStopDistancePips = CalculateAtrBasedStopLossPips(symbol); 
                }

                if (trailingStopDistancePips <= 0) continue; // Invalid trailing stop distance

                if (position.TradeType == TradeType.Buy)
                {
                    double newStopLossPrice = Symbol.Ask - trailingStopDistancePips * Symbol.PipSize;
                    // Ensure SL is higher than current SL and also higher than entry price (to lock in profit)
                    if (position.StopLoss == null || newStopLossPrice > position.StopLoss)
                    {
                        // Only trail if it's profitable or at least break-even plus some buffer
                        if (newStopLossPrice > position.EntryPrice) 
                        {
                            ModifyPosition(position, newStopLossPrice, position.TakeProfit);
                            Print($"Trailing Buy SL for {position.Id} to {newStopLossPrice}");
                        }
                    }
                }
                else if (position.TradeType == TradeType.Sell)
                {
                    double newStopLossPrice = Symbol.Bid + trailingStopDistancePips * Symbol.PipSize;
                    // Ensure SL is lower than current SL and also lower than entry price (to lock in profit)
                    if (position.StopLoss == null || newStopLossPrice < position.StopLoss)
                    {
                        // Only trail if it's profitable or at least break-even plus some buffer
                        if (newStopLossPrice < position.EntryPrice)
                        {
                             ModifyPosition(position, newStopLossPrice, position.TakeProfit);
                             Print($"Trailing Sell SL for {position.Id} to {newStopLossPrice}");
                        }
                    }
                }
            }
        }

        private double CalculateAtrBasedStopLossPips(Symbol symbol)
        {
            var atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple); // Consider parameterizing ATR period for TS
            double atrValue = atr.Result.Last(1); 
            if (double.IsNaN(atrValue) || atrValue == 0)
            {
                Print("ATR for trailing stop is not available or zero. Using a default of 30 pips.");
                return 30; // Default to 30 pips for trailing if ATR is not good
            }
            double stopLossDistanceInPrice = atrValue * 1.5; // Consider parameterizing ATR multiplier for TS
            return stopLossDistanceInPrice / symbol.PipSize;
        }

        protected override void OnStop()
        {
            Print("GER40 Bot stopped.");
            Timer.Stop();
        }
    }
}
