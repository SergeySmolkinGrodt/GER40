using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using System.Globalization;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AdvancedAutomatedBot : Robot 
    {
        // Приватные поля для хранения TimeFrame объектов
        private TimeFrame _fractalTPTimeFrame;
        
        #region Parameters

        // --- Общие торговые параметры ---
        [Parameter("Lot Size (Risk % if SL used)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0, Step = 0.1, Group = "Управление Рисками")]
        public double RiskPercent { get; set; } // Заменил LotSize на RiskPercent

        // --- Параметры Тейк-Профита ---
        // EnableFractalTakeProfit удален, фрактальный TP теперь всегда приоритет
        [Parameter("Fractal TP TimeFrame (1=M1, 5=M5, 15=M15, 30=M30, 60=H1, 240=H4)", DefaultValue = 60, Group = "Тейк-Профит")]
        public int FractalTPTimeFrameMinutes { get; set; }
        [Parameter("Fractal TP Window (свечей с каждой стороны)", DefaultValue = 2, MinValue = 1, Group = "Тейк-Профит")]
        public int FractalTPWindow { get; set; }
        [Parameter("Fractal TP Offset Pips (от уровня фрактала)", DefaultValue = 0, Group = "Тейк-Профит")]
        public int FractalTPOffsetPips { get; set; }
        [Parameter("Fractal TP Max Lookback Bars", DefaultValue = 100, MinValue = 10, Group = "Тейк-Профит")]
        public int FractalTPMaxLookbackBars { get; set; }
        [Parameter("Pips-Based Take Profit (если фрактальный TP не найден)", DefaultValue = 200, MinValue = 0, Group = "Тейк-Профит")]
        public int FallbackTakeProfitPips { get; set; }

        // --- Параметры Стоп-Лосса ---
        public enum StopLossMode { Pips, FVG, LiquidityLevel }
        [Parameter("Stop Loss Mode", DefaultValue = StopLossMode.Pips, Group = "Стоп-Лосс")]
        public StopLossMode SLMode { get; set; }

        [Parameter("Pips-Based Stop Loss", DefaultValue = 100, MinValue = 0, Group = "Стоп-Лосс - Pips")]
        public int StopLossPips { get; set; }

        [Parameter("FVG TimeFrame", DefaultValue = "TimeFrame.Hour4", Group = "Стоп-Лосс - FVG")]
        public TimeFrame FVGTimeFrame { get; set; }
        [Parameter("FVG Max Lookback Bars", DefaultValue = 50, MinValue = 5, Group = "Стоп-Лосс - FVG")]
        public int FVGMaxLookbackBars { get; set; }
        [Parameter("FVG SL Offset (Pips от края FVG)", DefaultValue = 5, MinValue = 0, Group = "Стоп-Лосс - FVG")]
        public int FVGStopLossOffsetPips { get; set; }
        [Parameter("Min Sensible FVG Level (Ratio of Entry)", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 0.95, Step = 0.05, Group = "Стоп-Лосс - FVG")]
        public double MinSensibleFVGRatio { get; set; } // Например, 0.5 означает, что FVG не должен быть ниже 50% от цены входа

        [Parameter("Liquidity SL TimeFrame", DefaultValue = "TimeFrame.Hour1", Group = "Стоп-Лосс - Liquidity")]
        public TimeFrame LiquiditySLTimeFrame { get; set; }
        [Parameter("Liquidity SL Lookback Bars", DefaultValue = 20, MinValue = 3, Group = "Стоп-Лосс - Liquidity")]
        public int LiquiditySLLookbackBars { get; set; }
        [Parameter("Liquidity SL Offset (Pips от Low)", DefaultValue = 5, MinValue = 0, Group = "Стоп-Лосс - Liquidity")]
        public int LiquiditySLOffsetPips { get; set; }
        [Parameter("Min Sensible Liquidity Level (Ratio of Entry)", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 0.95, Step = 0.05, Group = "Стоп-Лосс - Liquidity")]
        public double MinSensibleLiquidityRatio { get; set; }

        // --- Параметры Времени Торговли ---
        [Parameter("Opening Hour (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 23, Group = "Время Торговли")]
        public int OpeningHourUtc { get; set; }
        [Parameter("Opening Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Group = "Время Торговли")]
        public int OpeningMinuteUtc { get; set; }
        // EnableEndOfDayClose удален, логика EOD теперь всегда активна
        [Parameter("Closing Hour (UTC)", DefaultValue = 21, MinValue = 0, MaxValue = 23, Group = "Время Торговли")]
        public int ClosingHourUtc { get; set; }
        [Parameter("Closing Minute (UTC)", DefaultValue = 50, MinValue = 0, MaxValue = 59, Group = "Время Торговли")]
        public int ClosingMinuteUtc { get; set; }

        #endregion

        private string _botLabel;
        private DateTime _lastTradeAttemptDate;

        protected override void OnStart()
        {
            // Инициализация TimeFrame объектов
            _fractalTPTimeFrame = TimeFrame.FromMinutes(FractalTPTimeFrameMinutes);
            
            _botLabel = "AdvancedAutomatedBot_" + Account.Number + "_" + this.SymbolName;
            _lastTradeAttemptDate = DateTime.MinValue;
            
            Timer.Start(TimeSpan.FromMinutes(1));
            Print("Бот AdvancedAutomatedBot запущен. Символ: {0}. Метка: {1}.", this.SymbolName, _botLabel);
            PrintDetailedSettings();
        }
        
        private void PrintDetailedSettings() // Доработать вывод настроек
        {
            Print("--- Управление Рисками ---");
            Print("Процент риска на сделку: {0}%", RiskPercent);
            Print("--- Настройки Тейк-Профита ---");
            Print("Фрактальный TP: Всегда активен. TF: {0}, Окно: {1}, Отступ: {2} пипс, Поиск: {3} бар.", FractalTPTimeFrame, FractalTPWindow, FractalTPOffsetPips, FractalTPMaxLookbackBars);
            Print("Резервный TP (пипсы): {0}", FallbackTakeProfitPips > 0 ? FallbackTakeProfitPips.ToString() : "нет");

            Print("--- Настройки Стоп-Лосса ---");
            Print("Режим SL: {0}", SLMode);
            switch (SLMode)
            {
                case StopLossMode.Pips:
                    Print("SL по пипсам: {0}", StopLossPips > 0 ? StopLossPips.ToString() : "нет");
                    break;
                case StopLossMode.FVG:
                    Print("SL по FVG: TF: {0}, Поиск: {1} бар, Отступ: {2} пипс, Мин. уровень от цены входа: {3}%", FVGTimeFrame, FVGMaxLookbackBars, FVGStopLossOffsetPips, MinSensibleFVGRatio * 100);
                    break;
                case StopLossMode.LiquidityLevel:
                    Print("SL по ликвидности (Low): TF: {0}, Поиск: {1} бар, Отступ: {2} пипс, Мин. уровень от цены входа: {3}%", LiquiditySLTimeFrame, LiquiditySLLookbackBars, LiquiditySLOffsetPips, MinSensibleLiquidityRatio * 100);
                    break;
            }
            Print("--- Настройки Времени Торговли ---");
            Print("Закрытие в конце дня: Всегда активно. Время закрытия UTC: {0:D2}:{1:D2}", ClosingHourUtc, ClosingMinuteUtc);
            Print("Время открытия UTC: {0:D2}:{1:D2}", OpeningHourUtc, OpeningMinuteUtc);
        }

        protected override void OnTimer()
        {
            var serverTime = Server.Time;
            CheckAndOpenPosition(serverTime);
            // EnableEndOfDayClose удален, логика EOD теперь всегда активна
            CheckAndClosePositionsEndOfDay(serverTime); 
        }

        #region StopLoss Logic

        private bool TryGetFVGStopLoss(double entryPrice, out double slPrice)
        {
            slPrice = 0;
            double fvgLevel;
            // Передаем entryPrice для расчета минимально допустимого уровня FVG
            if (TryGetNearestBullishFVGBelow(FVGTimeFrame, entryPrice, FVGMaxLookbackBars, entryPrice * MinSensibleFVGRatio, out fvgLevel))
            {
                slPrice = Math.Round(fvgLevel - (FVGStopLossOffsetPips * Symbol.PipSize), Symbol.Digits);
                Print("SL по FVG: Рассчитан на {0} (FVG поддержка на {1}, отступ {2} пипс)", 
                      slPrice.ToString(CultureInfo.InvariantCulture), fvgLevel.ToString(CultureInfo.InvariantCulture), FVGStopLossOffsetPips);
                return true;
            }
            Print("SL по FVG: Подходящий FVG (выше минимально допустимого уровня) не найден.");
            return false;
        }

        private bool TryGetNearestBullishFVGBelow(TimeFrame timeFrame, double belowPrice, int maxLookback, double minAcceptableLevel, out double fvgSupportLevel)
        {
            fvgSupportLevel = double.MinValue; 
            var series = MarketData.GetBars(timeFrame, this.SymbolName);
            if (series.Count < 3) return false;

            int loopStart = series.Count - 3;
            int loopEnd = Math.Max(0, series.Count - maxLookback - 3); 

            for (int i = loopStart; i >= loopEnd; i--)
            {
                var candle1 = series[i];
                var candle3 = series[i + 2];
                if (candle1.High < candle3.Low) // Бычий FVG
                {
                    double currentFvgBottom = candle1.High; 
                    // FVG должен быть ниже цены входа, выше ранее найденного И выше минимально допустимого уровня
                    if (currentFvgBottom < belowPrice && currentFvgBottom > fvgSupportLevel && currentFvgBottom > minAcceptableLevel)
                    {
                        fvgSupportLevel = currentFvgBottom;
                    }
                }
            }
            return fvgSupportLevel != double.MinValue; 
        }

        private bool TryGetLiquidityLevelStopLoss(double entryPrice, out double slPrice)
        {
            slPrice = 0;
            var series = MarketData.GetBars(LiquiditySLTimeFrame, this.SymbolName);
            if (series.Count < LiquiditySLLookbackBars)
            {
                Print("SL по ликвидности: Недостаточно баров на {0} ({1} < {2})", LiquiditySLTimeFrame, series.Count, LiquiditySLLookbackBars);
                return false;
            }

            double lowestLowInPeriod = series.Skip(series.Count - LiquiditySLLookbackBars).Min(b => b.Low);
            double minAcceptableLevel = entryPrice * MinSensibleLiquidityRatio;
            
            // Уровень ликвидности должен быть ниже цены входа И выше минимально допустимого уровня
            if (lowestLowInPeriod < entryPrice && lowestLowInPeriod > minAcceptableLevel) 
            {
                slPrice = Math.Round(lowestLowInPeriod - (LiquiditySLOffsetPips * Symbol.PipSize), Symbol.Digits);
                Print("SL по ликвидности: Рассчитан на {0} (Low за {1} бар на {2} был {3}, отступ {4} пипс)",
                    slPrice.ToString(CultureInfo.InvariantCulture), LiquiditySLLookbackBars, LiquiditySLTimeFrame,
                    lowestLowInPeriod.ToString(CultureInfo.InvariantCulture), LiquiditySLOffsetPips);
                return true;
            }
            Print("SL по ликвидности: Недавний Low ({0}) не ниже цены входа ({1}) или ниже мин. допуст. уровня ({2}).", 
                lowestLowInPeriod.ToString(CultureInfo.InvariantCulture), 
                entryPrice.ToString(CultureInfo.InvariantCulture),
                minAcceptableLevel.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        #endregion

        #region TakeProfit Logic

        private bool TryGetFractalTakeProfit(double entryPrice, out double tpPrice)
        {
            tpPrice = 0;
            double fractalLevel;

            if (TryGetNearestUpFractalAbovePrice(FractalTPTimeFrame, entryPrice, FractalTPWindow, FractalTPMaxLookbackBars, out fractalLevel))
            {
                // Убедимся, что фрактал действительно выше цены входа перед добавлением отступа
                if (fractalLevel > entryPrice) 
                {
                    tpPrice = Math.Round(fractalLevel + (FractalTPOffsetPips * Symbol.PipSize), Symbol.Digits);
                    Print("TP по фракталу: Рассчитан на {0} (фрактал на {1}, отступ {2} пипс)", 
                          tpPrice.ToString(CultureInfo.InvariantCulture), fractalLevel.ToString(CultureInfo.InvariantCulture), FractalTPOffsetPips);
                    return true;
                }
                else {
                    Print("TP по фракталу: Найденный фрактал ({0}) не выше цены входа ({1}).", fractalLevel, entryPrice);
                }
            }
            Print("TP по фракталу: Подходящий фрактал не найден.");
            return false;
        }

        private bool TryGetNearestUpFractalAbovePrice(TimeFrame timeFrame, double abovePrice, int window, int maxLookback, out double fractalHighPrice)
        {
            fractalHighPrice = double.MaxValue; 
            var series = MarketData.GetBars(timeFrame, this.SymbolName);

            if (series.Count < (2 * window + 1)) return false; 

            int loopStart = series.Count - 1 - window; 
            int loopEnd = Math.Max(window, series.Count - 1 - maxLookback - window); 

            for (int i = loopStart; i >= loopEnd; i--)
            {
                bool isUpFractal = true;
                double currentBarHigh = series[i].High;

                for (int j = 1; j <= window; j++) 
                {
                    if (series[i - j].High >= currentBarHigh || series[i + j].High >= currentBarHigh)
                    {
                        isUpFractal = false;
                        break;
                    }
                }

                if (isUpFractal)
                {
                    if (currentBarHigh > abovePrice && currentBarHigh < fractalHighPrice) 
                    {
                        fractalHighPrice = currentBarHigh;
                    }
                }
            }
            return fractalHighPrice != double.MaxValue;
        }

        #endregion


        // Внутри класса AdvancedAutomatedBot
        private int GetVolumeDecimalPlaces(Symbol symbol)
        {
            if (symbol == null || symbol.VolumeInUnitsStep <= 0) return 2; // Значение по умолчанию или для случая ошибки
            if (symbol.VolumeInUnitsStep >= 1) return 0; // Для шагов объема 1, 2, 10 и т.д.
        
            // Рассчитываем количество десятичных знаков на основе шага объема
            // Например, если шаг 0.01, то 1/0.01 = 100, Log10(100) = 2 знака.
            // Если шаг 0.1, то 1/0.1 = 10, Log10(10) = 1 знак.
            double logValue = Math.Log10(1.0 / symbol.VolumeInUnitsStep);
            // Округляем до ближайшего целого и гарантируем, что не отрицательное
            int decimalPlaces = (int)Math.Round(logValue > 0 ? logValue : 0); 
            return Math.Min(decimalPlaces, 8); // Ограничим максимальное количество знаков для разумности
        }   

        // Внутри класса AdvancedAutomatedBot

        private void CheckAndOpenPosition(DateTime serverTime) 
        {
            if (serverTime.Hour == OpeningHourUtc && serverTime.Minute == OpeningMinuteUtc)
            {
                if (_lastTradeAttemptDate.Date == serverTime.Date) return; 
                _lastTradeAttemptDate = serverTime; 
        
                Print("Время для открытия ({0:D2}:{1:D2} UTC) по {2}. Попытка...", OpeningHourUtc, OpeningMinuteUtc, this.SymbolName);
                var symbolInfo = Symbols.GetSymbol(this.SymbolName);
                if (symbolInfo == null) { Print("Символ {0} не найден.", this.SymbolName); return; }
                
                double entryPrice = symbolInfo.Ask;
                double stopLossPrice = 0;
                double takeProfitPrice = 0;
                int volumeDecimalPlaces = GetVolumeDecimalPlaces(symbolInfo); // Получаем кол-во знаков для объема
        
                // --- Расчет Стоп-Лосса --- (логика без изменений)
                bool slSuccessfullyCalculated = false; 
                switch (SLMode)
                {
                    case StopLossMode.FVG:
                        if (TryGetFVGStopLoss(entryPrice, out stopLossPrice) && stopLossPrice > 0 && stopLossPrice < entryPrice)
                            slSuccessfullyCalculated = true;
                        break;
                    case StopLossMode.LiquidityLevel:
                        if (TryGetLiquidityLevelStopLoss(entryPrice, out stopLossPrice) && stopLossPrice > 0 && stopLossPrice < entryPrice)
                            slSuccessfullyCalculated = true;
                        break;
                    case StopLossMode.Pips:
                    default:
                        if (StopLossPips > 0)
                        {
                            stopLossPrice = Math.Round(entryPrice - (StopLossPips * symbolInfo.PipSize), symbolInfo.Digits);
                             if (stopLossPrice > 0 && stopLossPrice < entryPrice) {
                                Print("SL Стандартный: Установлен на {0} ({1} пипс)", stopLossPrice.ToString(CultureInfo.InvariantCulture), StopLossPips);
                                slSuccessfullyCalculated = true;
                             } else {
                                Print("SL Стандартный: Некорректный расчет ({0}) для входа {1}.", stopLossPrice, entryPrice);
                             }
                        }
                        break; 
                }
                if (!slSuccessfullyCalculated && SLMode != StopLossMode.Pips && StopLossPips > 0) 
                {
                    stopLossPrice = Math.Round(entryPrice - (StopLossPips * symbolInfo.PipSize), symbolInfo.Digits);
                    if (stopLossPrice > 0 && stopLossPrice < entryPrice) {
                        Print("SL Фоллбэк (Pips): Установлен на {0} ({1} пипс)", stopLossPrice.ToString(CultureInfo.InvariantCulture), StopLossPips);
                        slSuccessfullyCalculated = true;
                    } else {
                         Print("SL Фоллбэк (Pips): Некорректный расчет ({0}) для входа {1}.", stopLossPrice, entryPrice);
                    }
                }
                if (!slSuccessfullyCalculated) 
                {
                    Print("ОШИБКА: Стоп-лосс не может быть рассчитан корректно. Сделка отменена.");
                    return;
                }
        
                // --- Расчет Тейк-Профита --- (логика без изменений)
                bool tpSet = false;
                if (TryGetFractalTakeProfit(entryPrice, out takeProfitPrice) && takeProfitPrice > entryPrice)
                {
                    tpSet = true;
                }
                if (!tpSet && FallbackTakeProfitPips > 0) 
                {
                    double fallbackTp = Math.Round(entryPrice + (FallbackTakeProfitPips * symbolInfo.PipSize), symbolInfo.Digits);
                    if (fallbackTp > entryPrice) {
                        takeProfitPrice = fallbackTp;
                        Print("TP Стандартный: Установлен на {0} ({1} пипс)", takeProfitPrice.ToString(CultureInfo.InvariantCulture), FallbackTakeProfitPips);
                        tpSet = true;
                    } else {
                        Print("TP Стандартный: Некорректный расчет ({0}) для входа {1}.", fallbackTp, entryPrice);
                    }
                } 
                if (!tpSet) {
                     Print("TP не установлен (TakeProfitPrice = 0).");
                     takeProfitPrice = 0; 
                }
                
                // --- Расчет размера лота/объема --- (логика без изменений, кроме форматирования в Print)
                double riskAmountInAccountCurrency = Account.Balance * (RiskPercent / 100.0);
                double stopLossDistanceInPrice = entryPrice - stopLossPrice; 
                if (stopLossDistanceInPrice <= 0) 
                {
                    Print("ОШИБКА: Дистанция стоп-лосса некорректна ({0}). Сделка отменена.", stopLossDistanceInPrice);
                    return;
                }
                double valueOfOnePointPerUnitVolume = symbolInfo.TickValue / symbolInfo.TickSize; 
                double stopLossDistanceInPoints = stopLossDistanceInPrice / symbolInfo.TickSize; 
                double monetaryRiskPerUnitVolume = stopLossDistanceInPoints * valueOfOnePointPerUnitVolume;
                double calculatedVolumeInUnits = 0;
                if (monetaryRiskPerUnitVolume > 1e-9)
                {
                    calculatedVolumeInUnits = riskAmountInAccountCurrency / monetaryRiskPerUnitVolume;
                }
                else
                {
                    Print("ОШИБКА: Денежный риск на единицу объема слишком мал или равен нулю. Сделка отменена.");
                    return;
                }
                double normalizedVolumeInUnits = symbolInfo.NormalizeVolumeInUnits(calculatedVolumeInUnits, RoundingMode.Down); 
        
                if (normalizedVolumeInUnits < symbolInfo.VolumeInUnitsMin)
                {
                    Print("ПРЕДУПРЕЖДЕНИЕ: Рассчитанный объем ({0}) меньше минимального ({1}). Используется минимальный объем. Фактический риск может быть выше.", 
                        normalizedVolumeInUnits.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture), // ИЗМЕНЕНО
                        symbolInfo.VolumeInUnitsMin.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture)); // ИЗМЕНЕНО
                    normalizedVolumeInUnits = symbolInfo.VolumeInUnitsMin;
                }
                if (normalizedVolumeInUnits > symbolInfo.VolumeInUnitsMax)
                {
                    Print("ПРЕДУПРЕЖДЕНИЕ: Рассчитанный объем ({0}) больше максимального ({1}). Используется максимальный объем.", 
                        normalizedVolumeInUnits.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture), // ИЗМЕНЕНО
                        symbolInfo.VolumeInUnitsMax.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture)); // ИЗМЕНЕНО
                    normalizedVolumeInUnits = symbolInfo.VolumeInUnitsMax;
                }
                if (normalizedVolumeInUnits < symbolInfo.VolumeInUnitsMin) { 
                    Print("ОШИБКА: Итоговый объем ({0}) для сделки меньше минимально допустимого ({1}). Сделка отменена.", 
                        normalizedVolumeInUnits.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture), // ИЗМЕНЕНО
                        symbolInfo.VolumeInUnitsMin.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture)); // ИЗМЕНЕНО
                    return;
                }
        
                Print("Расчет объема: Баланс={0:F2} {1}, Риск={2}%, Сумма риска={3:F2} {1}, SL_dist_price={4:F5}, SL_dist_points={5:F2}, Норм.Объем={6}",
                    Account.Balance, Account.Asset.Name, RiskPercent, riskAmountInAccountCurrency,
                    stopLossDistanceInPrice, stopLossDistanceInPoints, 
                    normalizedVolumeInUnits.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture) // ИЗМЕНЕНО
                    );
        
                var result = ExecuteMarketOrder(TradeType.Buy, this.SymbolName, normalizedVolumeInUnits, _botLabel, 
                                                stopLossPrice, 
                                                takeProfitPrice == 0 ? (double?)null : takeProfitPrice); 
        
                if (result.IsSuccessful)
                {
                    Print("УСПЕХ: Открыта длинная позиция #{0} по {1} объемом {2}. SL: {3}, TP: {4}",
                          result.Position.Id, this.SymbolName, 
                          result.Position.VolumeInUnits.ToString("F" + volumeDecimalPlaces, CultureInfo.InvariantCulture), // ИЗМЕНЕНО
                          stopLossPrice.ToString(CultureInfo.InvariantCulture), 
                          takeProfitPrice == 0 ? "нет" : takeProfitPrice.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    Print("ОШИБКА открытия позиции по {0}: {1}", this.SymbolName, result.Error);
                }
            }
        }
        
        // Убедитесь, что этот новый метод GetVolumeDecimalPlaces() добавлен в класс AdvancedAutomatedBot,
        // и остальные методы (OnStart, OnTimer, TryGetFVGStopLoss, TryGetLiquidityLevelStopLoss, 
        // TryGetFractalTakeProfit, TryGetNearestUpFractalAbovePrice, CheckAndClosePositionsEndOfDay, OnStop)
        // остаются такими же, как в предыдущей полной версии кода.
        // Вам нужно будет вставить этот обновленный метод CheckAndOpenPosition и новый GetVolumeDecimalPlaces
        // в тот полный код.

        private void CheckAndClosePositionsEndOfDay(DateTime serverTime) 
        {
            if (serverTime.Hour == ClosingHourUtc && serverTime.Minute == ClosingMinuteUtc)
            {
                Print("EOD: Время закрытия ({0:D2}:{1:D2} UTC) по {2}. Проверка...", ClosingHourUtc, ClosingMinuteUtc, this.SymbolName);
                var positions = Positions.FindAll(_botLabel, this.SymbolName);
                bool closedAny = false;
                foreach (var position in positions)
                {
                    if (position.EntryTime.Date == serverTime.Date) 
                    {
                        Print("EOD: Позиция #{0} (вход: {1}) была открыта сегодня. Закрытие...", 
                              position.Id, position.EntryTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        var opResult = ClosePosition(position);
                        if (opResult.IsSuccessful) { Print("EOD УСПЕХ: Позиция #{0} закрыта.", position.Id); closedAny = true; }
                        else { Print("EOD ОШИБКА закрытия позиции #{0}: {1}", position.Id, opResult.Error); }
                    }
                }
                 if (!closedAny && !positions.Any(p => p.EntryTime.Date == serverTime.Date && p.SymbolName == this.SymbolName && p.Label == _botLabel))
                {
                    Print("EOD: Нет позиций, открытых сегодня этим ботом ({0}), для закрытия.", this.SymbolName);
                }
            }
        }

        protected override void OnStop()
        {
            Timer.Stop();
            Print("Бот AdvancedAutomatedBot остановлен.");
        }
    }
}