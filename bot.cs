using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class Ger40SMCBot : Robot
    {
        [Parameter("Risk % per Trade", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0, Group = "Trade Parameters")]
        public double RiskPercent { get; set; }

        [Parameter("Asian Session Start Hour (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 23, Group = "Session Timing")]
        public int AsianSessionStartHour { get; set; }

        [Parameter("Asian Session End Hour (UTC)", DefaultValue = 2, MinValue = 0, MaxValue = 23, Group = "Session Timing")]
        public int AsianSessionEndHour { get; set; }

        // Новый параметр для фиксированного стоп-лосса
        [Parameter("Fixed SL (Pips)", DefaultValue = 100, MinValue = 10, Group = "Stop Loss")]
        public double FixedSLPips { get; set; }

        // Параметры MinSLPips и MaxSLPips удалены, так как SL теперь фиксированный.
        // Параметр SLLookbackH1 удален.

        [Parameter("TP Lookback Periods (H1 Fractals)", DefaultValue = 50, MinValue = 10, Group = "Take Profit")]
        public int TPFractalLookbackH1 { get; set; }

        [Parameter("Fallback TP Risk/Reward Ratio", DefaultValue = 2.0, MinValue = 0.5, Group = "Take Profit")]
        public double FallbackTPRR { get; set; }

        [Parameter("MA Short Period (Structure)", DefaultValue = 10, MinValue = 5, Group = "Market Structure")]
        public int MaShortPeriod { get; set; }

        [Parameter("MA Long Period (Structure)", DefaultValue = 20, MinValue = 10, Group = "Market Structure")]
        public int MaLongPeriod { get; set; }

        [Parameter("Structure Lookback (Bars)", DefaultValue = 20, MinValue = 10, Group = "Market Structure")]
        public int StructureLookback { get; set; }

        private Bars _h1Series;
        private Bars _h4Series;
        private Bars _d1Series;
        private bool _isLongContextActive = false;
        private DateTime _lastTradeAttemptTime;

        protected override void OnStart()
        {
            _h1Series = MarketData.GetBars(TimeFrame.Hour);
            _h4Series = MarketData.GetBars(TimeFrame.Hour4);
            _d1Series = MarketData.GetBars(TimeFrame.Daily);

            if (_h1Series.Count < StructureLookback || _h4Series.Count < StructureLookback || _d1Series.Count < StructureLookback)
            {
                Print("Недостаточно исторических данных для запуска бота. Увеличьте количество загружаемых баров на графике.");
                Stop();
                return;
            }
            Print($"Symbol: {Symbol.Name}, PipSize: {Symbol.PipSize}, TickSize: {Symbol.TickSize}, Digits: {Symbol.Digits}");
            Print("GER40 SMC Bot запущен. Инициализация рыночного контекста...");
            UpdateMarketContext();
            _lastTradeAttemptTime = DateTime.MinValue;
        }

        protected override void OnBar()
        {
            try
            {
                if (_h4Series.OpenTimes.Last(0) != _h4Series.OpenTimes.Last(1))
                {
                    UpdateMarketContext();
                }
            }
            catch (Exception e)
            {
                Print($"Ошибка при обновлении контекста на H4 баре: {e.Message}. Возможно, недостаточно данных.");
                return;
            }

            if (!_isLongContextActive)
            {
                return;
            }

            var currentTimeUTC = Server.Time.ToUniversalTime();

            if (_lastTradeAttemptTime.Date != currentTimeUTC.Date)
            {
                 _lastTradeAttemptTime = DateTime.MinValue;
            }

            if (currentTimeUTC.Hour >= AsianSessionStartHour && currentTimeUTC.Hour < AsianSessionEndHour)
            {
                if (_lastTradeAttemptTime != DateTime.MinValue && _lastTradeAttemptTime.Date == currentTimeUTC.Date)
                {
                    return;
                }
                bool newH1BarInSession = _h1Series.OpenTimes.Last(0).Hour >= AsianSessionStartHour &&
                                         _h1Series.OpenTimes.Last(0).Hour < AsianSessionEndHour &&
                                         _h1Series.OpenTimes.Last(0) > _lastTradeAttemptTime;

                if (newH1BarInSession)
                {
                    Print($"DEBUG: Новый H1 бар ({_h1Series.OpenTimes.Last(0)}) в Азиатской сессии. Попытка входа.");
                    TryOpenLongPosition();
                    _lastTradeAttemptTime = _h1Series.OpenTimes.Last(0); 
                }
            }
        }

        private void UpdateMarketContext()
        {
            if (_h1Series.Count < StructureLookback || _h4Series.Count < StructureLookback || _d1Series.Count < StructureLookback)
            {
                Print("Недостаточно данных для обновления рыночного контекста.");
                _isLongContextActive = false; 
                return;
            }

            bool h4Bullish = IsMarketStructureBullish(_h4Series, StructureLookback);
            bool h1Bullish = IsMarketStructureBullish(_h1Series, StructureLookback);
            bool h4Bearish = IsMarketStructureBearish(_h4Series, StructureLookback);
            bool d1Bearish = IsMarketStructureBearish(_d1Series, StructureLookback);

            if (h4Bearish || d1Bearish) 
            {
                if (_isLongContextActive)
                {
                    Print($"Рыночный контекст изменился на Медвежий (H4 Bearish: {h4Bearish}, D1 Bearish: {d1Bearish}). Деактивация покупок.");
                    _isLongContextActive = false;
                }
            }
            else if (h4Bullish && h1Bullish) 
            {
                if (!_isLongContextActive)
                {
                    Print($"Рыночный контекст подтвержден как Бычий (H4 Bullish: {h4Bullish}, H1 Bullish: {h1Bullish}). Активация покупок.");
                }
                _isLongContextActive = true;
            }
            else 
            {
                 if (_isLongContextActive) 
                 {
                    Print($"Бычий контекст H4/H1 более не подтвержден (H4 Bullish: {h4Bullish}, H1 Bullish: {h1Bullish}). Деактивация покупок.");
                    _isLongContextActive = false;
                 }
            }
        }

        private bool IsMarketStructureBullish(Bars series, int lookback)
        {
            if (series.Count < lookback) return false;
            var smaShort = Indicators.SimpleMovingAverage(series.ClosePrices, MaShortPeriod);
            var smaLong = Indicators.SimpleMovingAverage(series.ClosePrices, MaLongPeriod);
            bool maBullish = smaShort.Result.Last(0) > smaLong.Result.Last(0) && smaShort.Result.Last(1) > smaLong.Result.Last(1) && smaShort.Result.Last(0) > smaShort.Result.Last(1);
            int halfLookback = lookback / 2;
            if (halfLookback < 2) halfLookback = 2; 
            double recentMaxHigh = 0;
            for(int i=0; i < halfLookback; ++i) recentMaxHigh = Math.Max(recentMaxHigh, series.HighPrices.Last(i));
            double prevMaxHigh = 0;
            for(int i=halfLookback; i < lookback; ++i) prevMaxHigh = Math.Max(prevMaxHigh, series.HighPrices.Last(i));
            double recentMinLow = double.MaxValue;
            for(int i=0; i < halfLookback; ++i) recentMinLow = Math.Min(recentMinLow, series.LowPrices.Last(i));
            double prevMinLow = double.MaxValue;
            for(int i=halfLookback; i < lookback; ++i) prevMinLow = Math.Min(prevMinLow, series.LowPrices.Last(i));
            bool isHH = recentMaxHigh > prevMaxHigh;
            bool isHL = recentMinLow > prevMinLow;
            return maBullish && isHH && isHL;
        }

        private bool IsMarketStructureBearish(Bars series, int lookback)
        {
            if (series.Count < lookback) return false;
            var smaShort = Indicators.SimpleMovingAverage(series.ClosePrices, MaShortPeriod);
            var smaLong = Indicators.SimpleMovingAverage(series.ClosePrices, MaLongPeriod);
            bool maBearish = smaShort.Result.Last(0) < smaLong.Result.Last(0) && smaShort.Result.Last(1) < smaLong.Result.Last(1) && smaShort.Result.Last(0) < smaShort.Result.Last(1);
            int halfLookback = lookback / 2;
            if (halfLookback < 2) halfLookback = 2;
            double recentMaxHigh = 0;
            for(int i=0; i < halfLookback; ++i) recentMaxHigh = Math.Max(recentMaxHigh, series.HighPrices.Last(i));
            double prevMaxHigh = 0;
            for(int i=halfLookback; i < lookback; ++i) prevMaxHigh = Math.Max(prevMaxHigh, series.HighPrices.Last(i));
            double recentMinLow = double.MaxValue;
            for(int i=0; i < halfLookback; ++i) recentMinLow = Math.Min(recentMinLow, series.LowPrices.Last(i));
            double prevMinLow = double.MaxValue;
            for(int i=halfLookback; i < lookback; ++i) prevMinLow = Math.Min(prevMinLow, series.LowPrices.Last(i));
            bool isLH = recentMaxHigh < prevMaxHigh;
            bool isLL = recentMinLow < prevMinLow;
            return maBearish && isLH && isLL;
        }

        private void TryOpenLongPosition()
        {
            Print("DEBUG: --- Начало TryOpenLongPosition ---");
            if (Positions.Count(p => p.SymbolName == SymbolName && p.Label == "Ger40SMC_Long") > 0)
            {
                Print("DEBUG: Длинная позиция по GER40 уже открыта.");
                return;
            }

            double currentAskRaw = Symbol.Ask; // Используем "сырую" цену для всех расчетов
            Print($"DEBUG: currentAskRaw: {currentAskRaw.ToString("F5")}");


            // --- Расчет Stop Loss (теперь фиксированный) ---
            double stopLossPriceRaw = currentAskRaw - (FixedSLPips * Symbol.PipSize);
            Print($"DEBUG: Fixed SL (Raw) calculated: {stopLossPriceRaw.ToString("F5")} ({FixedSLPips} pips from entry)");
            
            // Проверка, что SL ниже цены входа
            if (stopLossPriceRaw >= currentAskRaw)
            {
                Print($"ОШИБКА: Рассчитанный SL ({stopLossPriceRaw.ToString("F5")}) не ниже цены входа ({currentAskRaw.ToString("F5")}). Увеличьте FixedSLPips или проверьте PipSize.");
                // Можно добавить аварийный SL, если это условие срабатывает из-за очень малого FixedSLPips
                // stopLossPriceRaw = currentAskRaw - (Symbol.PipSize * 10); // Например, минимальный SL в 10 тиков
                // Print($"DEBUG: Аварийный SL установлен на {stopLossPriceRaw.ToString("F5")}");
                // if (stopLossPriceRaw >= currentAskRaw) {
                //    Print("ОШИБКА: Аварийный SL также недействителен. Сделка отменена.");
                //    return;
                // }
                return; // Пока просто отменяем сделку
            }
            Print($"DEBUG: Финальный SL (Raw) для расчета TP и ордера: {stopLossPriceRaw.ToString("F5")}");

            // --- Расчет Take Profit (логика остается прежней, но использует новый SL) ---
            double takeProfitPriceRaw = 0; 
            Print($"DEBUG: TP Calculation Start (Raw). Initial TP (Raw) = {takeProfitPriceRaw}");

            if (_h1Series.HighPrices.Count > 4) 
            {
                for (int i = 2; i < Math.Min(TPFractalLookbackH1, _h1Series.HighPrices.Count - 2) ; i++)
                {
                    double fractalHighCandidate = _h1Series.HighPrices.Last(i);
                    bool isUpFractal = fractalHighCandidate > _h1Series.HighPrices.Last(i + 1) &&
                                       fractalHighCandidate > _h1Series.HighPrices.Last(i + 2) &&
                                       fractalHighCandidate > _h1Series.HighPrices.Last(i - 1) &&
                                       fractalHighCandidate > _h1Series.HighPrices.Last(i - 2);
                    
                    if (isUpFractal && fractalHighCandidate > currentAskRaw)
                    {
                        Print($"DEBUG: Found UpFractal at {fractalHighCandidate.ToString("F5")} (index {i}). CurrentAskRaw={currentAskRaw.ToString("F5")}.");
                        double potentialTPDistancePips = (fractalHighCandidate - currentAskRaw) / Symbol.PipSize;
                        // Используем FixedSLPips для расчета соотношения, если SL фиксированный
                        double slDistancePips = FixedSLPips; 

                        Print($"DEBUG: Fractal {fractalHighCandidate.ToString("F5")}: potentialTP_pips={potentialTPDistancePips.ToString("F2")}, sl_pips={slDistancePips.ToString("F2")}");

                        // Минимальное расстояние для TP от фрактала должно быть хотя бы равно половине SL
                        if (potentialTPDistancePips > slDistancePips * 0.5) 
                        {
                            if (takeProfitPriceRaw == 0 || fractalHighCandidate < takeProfitPriceRaw) 
                            {
                                takeProfitPriceRaw = fractalHighCandidate;
                                Print($"DEBUG: TP (Raw) updated to fractal high: {takeProfitPriceRaw.ToString("F5")}");
                            }
                        } else {
                             Print($"DEBUG: Fractal {fractalHighCandidate.ToString("F5")} не прошел проверку соотношения/минимальной дистанции.");
                        }
                    }
                }
            } else {
                Print($"DEBUG: Недостаточно данных для поиска фракталов H1 ({_h1Series.HighPrices.Count}, требуется > 4)");
            }
            
            Print($"DEBUG: TP (Raw) after fractal search: {takeProfitPriceRaw.ToString("F5")}");

            if (takeProfitPriceRaw == 0 || takeProfitPriceRaw <= currentAskRaw)
            {
                Print($"DEBUG: Подходящий H1 фрактал для TP не найден или слишком близко ({takeProfitPriceRaw.ToString("F5")}). Используется Fallback TP (Raw).");
                double slPips = FixedSLPips; // Используем фиксированный SL для расчета Fallback TP
                
                takeProfitPriceRaw = currentAskRaw + (slPips * FallbackTPRR * Symbol.PipSize);
                Print($"DEBUG: Fallback TP (Raw) calculated: {takeProfitPriceRaw.ToString("F5")} (slPips={slPips.ToString("F2")})");
            }
            
            Print($"DEBUG: TP (Raw) before final adjustment: {takeProfitPriceRaw.ToString("F5")}");

            // Минимальное расстояние для TP от цены входа (например, половина фиксированного SL)
            double minTpDistancePips = FixedSLPips * 0.5; 
            if ((takeProfitPriceRaw - currentAskRaw) / Symbol.PipSize < minTpDistancePips)
            {
                Print($"DEBUG: TP (Raw) ({takeProfitPriceRaw.ToString("F5")}) слишком близко к AskRaw ({currentAskRaw.ToString("F5")}). Корректировка TP на {minTpDistancePips} pips.");
                takeProfitPriceRaw = currentAskRaw + (minTpDistancePips * Symbol.PipSize);
            }
            Print($"DEBUG: TP (Raw) after final adjustment: {takeProfitPriceRaw.ToString("F5")}");


            if (takeProfitPriceRaw <= currentAskRaw) {
                 Print($"ОШИБКА: Финальный TP (Raw) ({takeProfitPriceRaw.ToString("F5")}) ниже или равен AskRaw ({currentAskRaw.ToString("F5")}). Сделка невозможна.");
                 return;
            }
            if (stopLossPriceRaw >= takeProfitPriceRaw) {
                 Print($"ОШИБКА: Финальный SL (Raw) ({stopLossPriceRaw.ToString("F5")}) выше или равен TP (Raw) ({takeProfitPriceRaw.ToString("F5")}). Сделка невозможна.");
                 return;
            }

            // --- Ручное округление цен перед отправкой ---
            double finalExecutionSL = Math.Round(stopLossPriceRaw, Symbol.Digits, MidpointRounding.ToEven); 
            double finalExecutionTP = Math.Round(takeProfitPriceRaw, Symbol.Digits, MidpointRounding.ToEven);

            Print($"DEBUG: Ручное округление SL: {stopLossPriceRaw.ToString("F5")} -> {finalExecutionSL.ToString("F" + Symbol.Digits)}");
            Print($"DEBUG: Ручное округление TP: {takeProfitPriceRaw.ToString("F5")} -> {finalExecutionTP.ToString("F" + Symbol.Digits)}");

            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            // Используем stopLossPriceRaw (не округленный) для более точного расчета объема
            double positionSize = CalculateOptimalVolume(stopLossPriceRaw, riskAmount, currentAskRaw); 
            Print($"DEBUG: RiskAmount: {riskAmount.ToString("F2")}, Calculated PositionSize (from raw prices): {positionSize}");


            if (positionSize < Symbol.VolumeInUnitsMin)
            {
                Print($"Рассчитанный объем {positionSize} меньше минимального {Symbol.VolumeInUnitsMin}. Сделка невозможна.");
                return;
            }
            positionSize = Symbol.NormalizeVolumeInUnits(positionSize, RoundingMode.Down);
             if (positionSize < Symbol.VolumeInUnitsMin)
            {
                Print($"Нормализованный объем {positionSize} меньше минимального {Symbol.VolumeInUnitsMin}. Сделка невозможна.");
                return;
            }
            Print($"DEBUG: Normalized PositionSize: {positionSize}");

            Print($"ИТОГ: Попытка Long сделки: Entry: {currentAskRaw.ToString("F" + Symbol.Digits)}, SL: {finalExecutionSL.ToString("F" + Symbol.Digits)}, TP: {finalExecutionTP.ToString("F" + Symbol.Digits)}, Размер: {positionSize}");
            var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, positionSize, "Ger40SMC_Long", finalExecutionSL, finalExecutionTP);

            if (result.IsSuccessful)
            {
                var pos = result.Position;
                Print($"Long позиция успешно открыта. Position ID: {pos.Id}.");
                Print($"ОТПРАВЛЕНО БРОКЕРУ -> SL: {finalExecutionSL.ToString("F" + Symbol.Digits)}, TP: {finalExecutionTP.ToString("F" + Symbol.Digits)}");
                
                string reportedSLStr = pos.StopLoss.HasValue ? pos.StopLoss.Value.ToString("F" + Symbol.Digits) : "N/A";
                string reportedTPStr = pos.TakeProfit.HasValue ? pos.TakeProfit.Value.ToString("F" + Symbol.Digits) : "N/A";
                Print($"СООБЩЕНО API <- Pos.SL: {reportedSLStr}, Pos.TP: {reportedTPStr}");

                if (pos.StopLoss.HasValue && Math.Abs(pos.StopLoss.Value - finalExecutionSL) > Symbol.TickSize * 2) 
                {
                     double slDistanceSentPips = (currentAskRaw - finalExecutionSL)/Symbol.PipSize; 
                     Print($"ПРЕДУПРЕЖДЕНИЕ: Сообщенная цена SL ({reportedSLStr}) отличается от отправленной ({finalExecutionSL.ToString("F" + Symbol.Digits)}).");
                     Print($"  Отправленное расстояние SL от входа: {slDistanceSentPips.ToString("F2")} пипсов.");
                     
                     if (Math.Abs(pos.StopLoss.Value - slDistanceSentPips) < Symbol.PipSize) { 
                        Print($"  Похоже, API вернуло расстояние SL в пипсах: {pos.StopLoss.Value.ToString("F2")}");
                     }
                }
                 if (pos.TakeProfit.HasValue && Math.Abs(pos.TakeProfit.Value - finalExecutionTP) > Symbol.TickSize * 2) 
                {
                    Print($"ПРЕДУПРЕЖДЕНИЕ: Сообщенная цена TP ({reportedTPStr}) отличается от отправленной ({finalExecutionTP.ToString("F" + Symbol.Digits)}).");
                }
            }
            else
            {
                Print($"Ошибка открытия Long позиции: {result.Error}");
            }
            Print("DEBUG: --- Конец TryOpenLongPosition ---");
        }

        private double CalculateOptimalVolume(double stopLossPriceRaw, double riskAmount, double entryPriceRaw)
        {
            Print($"DEBUG CalcVol: EntryRaw={entryPriceRaw.ToString("F" + Symbol.Digits)}, SLRaw={stopLossPriceRaw.ToString("F" + Symbol.Digits)}, RiskAmt={riskAmount.ToString("F2")}");
            Print($"DEBUG CalcVol: PipSize={Symbol.PipSize}, TickSize={Symbol.TickSize}, TickValue={Symbol.TickValue}, LotSize={Symbol.LotSize}, Digits={Symbol.Digits}");

            double slDistance = entryPriceRaw - stopLossPriceRaw; 
            if (slDistance <= Symbol.TickSize) 
            {
                Print($"ОШИБКА CalcVol: Расстояние SL ({slDistance.ToString("F" + Symbol.Digits)}) слишком мало или неверно. SLRaw ({stopLossPriceRaw.ToString("F" + Symbol.Digits)}) , EntryRaw ({entryPriceRaw.ToString("F" + Symbol.Digits)}). Возвращаем мин. объем.");
                return Symbol.VolumeInUnitsMin;
            }

            double slDistanceInTicks = slDistance / Symbol.TickSize;
            double lossPerLot = slDistanceInTicks * Symbol.TickValue; 
            
            Print($"DEBUG CalcVol: slDistancePrice={slDistance.ToString("F" + Symbol.Digits)}, slDistanceInTicks={slDistanceInTicks.ToString("F2")}, lossPerLot={lossPerLot.ToString("F5")}");

            if (lossPerLot <= 0)
            {
                Print($"ОШИБКА CalcVol: lossPerLot ({lossPerLot.ToString("F5")}) не положительный. Невозможно рассчитать объем. Возвращаем мин. объем.");
                return Symbol.VolumeInUnitsMin;
            }

            double volumeInLots = riskAmount / lossPerLot;
            double finalVolumeInUnits = volumeInLots * Symbol.LotSize;

            Print($"DEBUG CalcVol: volumeInLots={volumeInLots.ToString("F4")}, finalVolumeInUnits={finalVolumeInUnits.ToString("F2")}");
            
            if (finalVolumeInUnits <= 0) return Symbol.VolumeInUnitsMin;
            return finalVolumeInUnits;
        }
        
        private void CloseAllLongPositions()
        {
            foreach (var position in Positions.FindAll("Ger40SMC_Long", SymbolName, TradeType.Buy))
            {
                ClosePosition(position);
                Print($"Позиция {position.Id} закрыта из-за смены рыночного контекста.");
            }
        }

        protected override void OnStop()
        {
            Print("GER40 SMC Bot остановлен.");
        }

        protected override void OnException(Exception exception)
        {
            Print($"Произошло ИСКЛЮЧЕНИЕ: {exception.Message}\nStackTrace: {exception.StackTrace}");
        }
    }
}
