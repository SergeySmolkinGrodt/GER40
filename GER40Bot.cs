using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GER40Bot : Robot
    {
        [Parameter("Symbol", DefaultValue = "GER40")]
        public string SymbolNameParameter { get; set; }

        [Parameter("Risk Percentage", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0)]
        public double RiskPercentage { get; set; }

        [Parameter("PSAR AF Step", DefaultValue = 0.02, MinValue = 0.001)]
        public double PsarAfStep { get; set; }

        [Parameter("PSAR AF Max", DefaultValue = 0.2, MinValue = 0.01)]
        public double PsarAfMax { get; set; }

        [Parameter("Min SL (Pips) from PSAR Entry", DefaultValue = 2, MinValue = 1)]
        public double MinPsarStopLossPips { get; set; } 

        [Parameter("TP Lookback Period", DefaultValue = 10, MinValue = 1)]
        public int TakeProfitLookbackPeriod { get; set; }

        [Parameter("ATR Period (Info)", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }
        
        [Parameter("Bot Label", DefaultValue = "GER40Bot_PSAR_DynamicSLTP")]
        public string BotLabel { get; set; }

        [Parameter("Enable PSAR Trailing Stop", DefaultValue = true)]
        public bool EnablePsarTrailingStop { get; set; }

        private ParabolicSAR _psar;
        private AverageTrueRange _atr;
        private Symbol _tradedSymbol;

        protected override void OnStart()
        {
            _tradedSymbol = Symbols.GetSymbol(SymbolNameParameter);
            if (_tradedSymbol == null)
            {
                Print($"Ошибка: Символ {SymbolNameParameter} не найден.");
                Stop();
                return;
            }
            if (_tradedSymbol.PipValue == 0 || _tradedSymbol.PipSize == 0)
            {
                Print($"Ошибка: PipValue ({_tradedSymbol.PipValue}) или PipSize ({_tradedSymbol.PipSize}) для символа {_tradedSymbol.Name} равен нулю.");
                Stop();
                return;
            }

            _psar = Indicators.ParabolicSAR(PsarAfStep, PsarAfMax);
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);

            Print($"GER40 Bot (PSAR Dynamic SL/TP) стартовал для символа {_tradedSymbol.Name}.");
            Print($"Risk %={RiskPercentage}, PSAR Step={PsarAfStep}, Max={PsarAfMax}, MinSLPips={MinPsarStopLossPips}, TP_Lookback={TakeProfitLookbackPeriod}");
            Print($"PSAR Trailing: {EnablePsarTrailingStop}");
        }

        protected override void OnBar()
        {
            if (Bars.Count < Math.Max(3, TakeProfitLookbackPeriod +1)) // Ensure enough data for indicators and TP lookback
            {
                return; 
            }

            double currentPsarValue = _psar.Result.Last(1);
            double previousPsarValue = _psar.Result.Last(2);
            // double currentAtrValue = _atr.Result.Last(1); // ATR currently informational

            if (double.IsNaN(currentPsarValue) || double.IsNaN(previousPsarValue) /*|| double.IsNaN(currentAtrValue)*/)
            {
                return; // Not all indicators are ready
            }

            ManageOpenPositions(currentPsarValue);

            var existingPosition = Positions.Find(BotLabel, _tradedSymbol.Name);
            if (existingPosition != null)
            {
                return; 
            }
            
            double closedBarHigh = Bars.HighPrices.Last(1);
            double closedBarLow = Bars.LowPrices.Last(1);
            double barBeforeThatHigh = Bars.HighPrices.Last(2);
            double barBeforeThatLow = Bars.LowPrices.Last(2);

            bool buySignal = currentPsarValue < closedBarLow && previousPsarValue > barBeforeThatHigh;
            bool sellSignal = currentPsarValue > closedBarHigh && previousPsarValue < barBeforeThatLow;

            if (buySignal || sellSignal)
            {
                double entryPrice;
                double stopLossLevel = currentPsarValue;
                double stopLossPips;
                TradeType tradeType;

                if (buySignal)
                {
                    tradeType = TradeType.Buy;
                    entryPrice = _tradedSymbol.Ask;
                    stopLossPips = (entryPrice - stopLossLevel) / _tradedSymbol.PipSize;
                }
                else // sellSignal
                {
                    tradeType = TradeType.Sell;
                    entryPrice = _tradedSymbol.Bid;
                    stopLossPips = (stopLossLevel - entryPrice) / _tradedSymbol.PipSize;
                }

                if (stopLossPips < MinPsarStopLossPips)
                {
                    Print($"Предупреждение ({tradeType}): Расчетный SL ({stopLossPips:F2} pips) от PSAR ({stopLossLevel:F5}) до цены входа ({entryPrice:F5}) слишком мал (менее {MinPsarStopLossPips} pips). Пропуск сигнала.");
                    return;
                }

                double riskAmountInAccountCurrency = Account.Balance * (RiskPercentage / 100.0);
                double stopLossValuePerUnit = stopLossPips * _tradedSymbol.PipValue;

                if (stopLossValuePerUnit <= 1e-9) 
                {
                    Print($"Ошибка ({tradeType}): Стоимость SL ({stopLossPips:F2} pips) на единицу слишком мала. SL Value/Unit: {stopLossValuePerUnit}");
                    return;
                }

                double calculatedVolumeInUnitsDouble = riskAmountInAccountCurrency / stopLossValuePerUnit;
                if (calculatedVolumeInUnitsDouble < _tradedSymbol.VolumeInUnitsMin)
                {
                    Print($"Предупреждение ({tradeType}): Расчетный объем ({_tradedSymbol.VolumeInUnitsToQuantity(calculatedVolumeInUnitsDouble):F2} lots) на основе {RiskPercentage}% риска ({stopLossPips:F2} SL pips) меньше мин. объема. Пропуск.");
                    return;
                }
            
                long volumeToExecute = (long)_tradedSymbol.NormalizeVolumeInUnits(calculatedVolumeInUnitsDouble, RoundingMode.Down);
                if (volumeToExecute < _tradedSymbol.VolumeInUnitsMin) { Print($"Warning ({tradeType}): Normalized volume ({_tradedSymbol.VolumeInUnitsToQuantity((double)volumeToExecute):F2} lots) was less than min, set to min."); volumeToExecute = _tradedSymbol.VolumeInUnitsMin; } 
                if (volumeToExecute == 0) { Print($"Ошибка ({tradeType}): Объем 0."); return; }
                if (volumeToExecute > _tradedSymbol.VolumeInUnitsMax) { volumeToExecute = (long)_tradedSymbol.VolumeInUnitsMax; }

                double? takeProfitTargetPips = null;
                if (buySignal)
                {
                    double tpPrice = Bars.HighPrices.Maximum(TakeProfitLookbackPeriod);
                    if (tpPrice > entryPrice) 
                        takeProfitTargetPips = (tpPrice - entryPrice) / _tradedSymbol.PipSize;
                }
                else // sellSignal
                {
                    double tpPrice = Bars.LowPrices.Minimum(TakeProfitLookbackPeriod);
                    if (tpPrice < entryPrice)
                        takeProfitTargetPips = (entryPrice - tpPrice) / _tradedSymbol.PipSize;
                }
                
                if(takeProfitTargetPips.HasValue && takeProfitTargetPips.Value <=0) takeProfitTargetPips = null; // Ensure TP is valid

                Print($"Сигнал {tradeType} для {_tradedSymbol.Name} объемом {_tradedSymbol.VolumeInUnitsToQuantity((double)volumeToExecute).ToString("F2")} lots. Вход: {entryPrice:F5}, PSAR(SL): {stopLossLevel:F5} ({stopLossPips:F2} pips). TP: {(takeProfitTargetPips.HasValue ? takeProfitTargetPips.Value.ToString("F2")+" pips" : "N/A")}");
                ExecuteMarketOrder(tradeType, _tradedSymbol.Name, volumeToExecute, BotLabel, stopLossPips, takeProfitTargetPips);
            }
        }

        private void ManageOpenPositions(double currentPsarForTrailing)
        {
            if (!EnablePsarTrailingStop) return;

            foreach (var position in Positions.FindAll(BotLabel, _tradedSymbol.Name))
            {
                double newStopLossPrice = 0;
                bool modify = false;
                double currentStopLoss = position.StopLoss ?? (position.TradeType == TradeType.Buy ? 0 : double.MaxValue);

                if (position.TradeType == TradeType.Buy)
                {
                    if (currentPsarForTrailing > currentStopLoss && currentPsarForTrailing < _tradedSymbol.Ask - (_tradedSymbol.PipSize * MinPsarStopLossPips) ) // Ensure new SL is better and not too close
                    {
                        newStopLossPrice = currentPsarForTrailing;
                        modify = true;
                    }
                }
                else // Sell position
                {
                    if (currentPsarForTrailing < currentStopLoss && currentPsarForTrailing > _tradedSymbol.Bid + (_tradedSymbol.PipSize * MinPsarStopLossPips) ) // Ensure new SL is better and not too close
                    {
                        newStopLossPrice = currentPsarForTrailing;
                        modify = true;
                    }
                }

                if (modify)
                {
                    // Ensure the new SL does not cross the entry price in the wrong direction or get too close to current price
                    bool isValidModification = false;
                    if (position.TradeType == TradeType.Buy && newStopLossPrice < _tradedSymbol.Ask && newStopLossPrice > position.EntryPrice)
                    {
                        isValidModification = true;
                    }
                    else if (position.TradeType == TradeType.Sell && newStopLossPrice > _tradedSymbol.Bid && newStopLossPrice < position.EntryPrice)
                    {
                        isValidModification = true;
                    }
                    // Simpler check: just ensure SL improves without being past current market price (with a small buffer)
                    if (position.TradeType == TradeType.Buy && newStopLossPrice < _tradedSymbol.Ask - _tradedSymbol.PipSize && newStopLossPrice > currentStopLoss)
                    {
                        isValidModification = true;
                    }
                     else if (position.TradeType == TradeType.Sell && newStopLossPrice > _tradedSymbol.Bid + _tradedSymbol.PipSize && newStopLossPrice < currentStopLoss)
                    {
                        isValidModification = true;
                    }

                    if (isValidModification) // Use the simple check
                    {
                        var result = position.ModifyStopLossPrice(newStopLossPrice);
                        if (result.IsSuccessful)
                        {
                            Print($"Позиция {position.Id} ({position.TradeType}): SL обновлен по PSAR на {newStopLossPrice:F5}");
                        }
                        else
                        {
                            Print($"Ошибка модификации SL для позиции {position.Id}: {result.Error}");
                        }
                    }
                }
            }
        }

        protected override void OnStop()
        {
            Print($"GER40 Bot (PSAR Dynamic SL/TP) ({_tradedSymbol?.Name}) остановлен.");
        }
    }
}
