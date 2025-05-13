using System;
using System.Threading.Tasks;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class DailyAsianOpenRisk : Robot
    {
        // --- Параметры бота ---
        [Parameter("Название символа", DefaultValue = "GER40.cash")]
        public new string SymbolName { get; set; }

        [Parameter("Процент риска от эквити (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Направление сделки", DefaultValue = TradeType.Buy)]
        public TradeType OrderTradeType { get; set; }

        [Parameter("Час открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 23)]
        public int TriggerHour { get; set; }

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskRR")]
        public string TradeLabel { get; set; }

        [Parameter("SL Lookback Period (H1 Bars)", DefaultValue = 24, MinValue = 3, MaxValue = 200)]
        public int SlLookbackPeriod { get; set; }

        [Parameter("H4 Lookback Period (Bars)", DefaultValue = 50, MinValue = 10, MaxValue = 200)]
        public int H4LookbackPeriod { get; set; }

        [Parameter("H4 Fractal Lookback", DefaultValue = 5, MinValue = 3, MaxValue = 10)]
        public int H4FractalLookback { get; set; }

        // --- Индикаторы ---
        private Fractals _fractals;

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;
        private TimeFrame _hourlyTimeframe = TimeFrame.Hour;
        private TimeFrame _h4Timeframe = TimeFrame.Hour4;
        // Удалены все переменные и методы, связанные с определением структуры рынка и контекста.

        protected override void OnStart()
        {
            _symbol = Symbols.GetSymbol(SymbolName);
            if (_symbol == null) 
            { 
                Print($"Ошибка: Символ '{SymbolName}' не найден."); 
                Stop(); 
                return; 
            }

            // Инициализация индикатора фракталов для часового таймфрейма
            _fractals = Indicators.Fractals(MarketData.GetBars(_hourlyTimeframe, SymbolName), 5); // Передаем Bars в Fractals

            Print($"Бот запущен для символа: {_symbol.Name}");
            Print($"Время открытия сделки (Сервер): {TriggerHour:D2}:{TriggerMinute:D2}");
            Print($"Риск: {RiskPercent}%");
            Print($"SL Lookback Period: {SlLookbackPeriod} H1 баров"); // Логирование нового параметра
            _lastTradeDate = DateTime.MinValue;
        }

        protected override void OnTick()
        {
            if (_symbol == null) return;

            var serverTime = Server.Time;

            // --- Фиксация всех позиций в конце дня ---
            if (serverTime.Hour == 23 && serverTime.Minute >= 59)
            {
                foreach (var position in Positions)
                {
                    if (position.SymbolName == SymbolName && position.Label == TradeLabel)
                    {
                        ClosePosition(position);
                        Print($"Позиция ID:{position.Id} по {SymbolName} закрыта в конце дня.");
                    }
                }
            }

            bool isTriggerTime = serverTime.Hour == TriggerHour && serverTime.Minute == TriggerMinute;
            bool tradeAlreadyOpenedToday = serverTime.Date == _lastTradeDate.Date; // Сравниваем только даты

            // --- Визуальный анализ контекста ---
            var hourlyBars = MarketData.GetBars(_hourlyTimeframe, SymbolName);
            var visualContext = AnalyzeVisualContext(hourlyBars);

            if (visualContext != VisualContext.Long)
            {
                Print($"Визуальный контекст не лонг ({visualContext}), открытие лонга запрещено.");
                return;
            }

            if (isTriggerTime && !tradeAlreadyOpenedToday)
            {
                _lastTradeDate = serverTime.Date; // Сохраняем дату последней попытки сделки

                if (Account.Equity <= 0) { Print("Ошибка: Экьюти <= 0. Сделка отменена."); return; }

                // hourlyBars уже получен выше и используется повторно
                if (hourlyBars.Count == 0)
                {
                    Print("Ошибка: Не удалось получить исторические данные для часового таймфрейма. Сделка отменена.");
                    return;
                }
              
                // --- Расчет уровней SL и TP ---
                double? stopLossPrice = CalculateStopLossPrice(hourlyBars, OrderTradeType);
                if (!stopLossPrice.HasValue) {
                    // Сообщение об ошибке уже выводится внутри CalculateStopLossPrice
                    return;
                }

                double? takeProfitPrice = CalculateTakeProfitPrice(hourlyBars, OrderTradeType);
                if (!takeProfitPrice.HasValue) {
                    Print("Ошибка: Не удалось определить уровень для Take Profit. Сделка отменена.");
                    return;
                }

                // Текущая цена для точки входа
                double entryPrice = (OrderTradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;
                
                // Рассчитываем расстояние в пунктах до SL и TP
                double stopLossInPips = Math.Abs((entryPrice - stopLossPrice.Value) / _symbol.PipSize);
                double takeProfitInPips = Math.Abs((takeProfitPrice.Value - entryPrice) / _symbol.PipSize);
                
                if (stopLossInPips <= 0)
                {
                    Print($"Ошибка: Рассчитанный Stop Loss ({stopLossInPips:F1} пп) <= 0. Возможно, SL ({stopLossPrice.Value:F5}) находится по ту же сторону от цены входа ({entryPrice:F5}), что и ожидаемая прибыль. Сделка отменена.");
                    return;
                }


                // --- Расчет объема ---
                double riskAmount = Account.Equity * (RiskPercent / 100.0);
                double pipValuePerLot = 0;
                if (_symbol.TickSize > 0 && _symbol.PipSize > 0) {
                     pipValuePerLot = _symbol.TickValue * (_symbol.PipSize / _symbol.TickSize);
                }

                if (pipValuePerLot <= 0) 
                { 
                    Print($"Ошибка: Не удалось рассчитать стоимость пункта (Pip Value = {pipValuePerLot}). Убедитесь, что TickValue и PipSize для символа {_symbol.Name} корректны. Сделка отменена."); 
                    return; 
                }

                // Расчет объема в лотах с учетом минимального объема
                double calculatedVolumeInLots = Math.Max(riskAmount / (stopLossInPips * pipValuePerLot), _symbol.VolumeInUnitsMin / _symbol.QuantityToVolumeInUnits(1.0));
                long volumeInUnits = (long)_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots);
                
                // Нормализация объема с округлением вниз
                // Нормализация объема с округлением вниз
                double normalizedVolume = _symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);
                long normalizedVolumeInUnits = Convert.ToInt64(normalizedVolume);
                
                // Если после нормализации объем стал меньше минимального, используем минимальный
                if (normalizedVolumeInUnits < _symbol.VolumeInUnitsMin) {
                    normalizedVolumeInUnits = Convert.ToInt64(_symbol.VolumeInUnitsMin);
                    Print($"Информация: Рассчитанный объем ({volumeInUnits} юнитов) после нормализации стал меньше минимального. Используем минимальный объем: {normalizedVolumeInUnits} юнитов.");
                }
                
                // Проверка максимального объема
                if (normalizedVolumeInUnits > _symbol.VolumeInUnitsMax) {
                    normalizedVolumeInUnits = Convert.ToInt64(_symbol.VolumeInUnitsMax);
                    Print($"Информация: Рассчитанный объем ({volumeInUnits} юнитов) превышает максимальный. Используем максимальный объем: {normalizedVolumeInUnits} юнитов.");
                }
                // Дублирующая проверка максимального объема (удаляем)
                // (уже проверено выше)

                double finalVolumeInLots = _symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits); // Используем VolumeInUnitsToQuantity

                Print($"Время сделки ({serverTime}). Риск: {RiskPercent}%, Экьюти: {Account.Equity:F2} {Account.Asset.Name}, Сумма риска: {riskAmount:F2} {Account.Asset.Name}.");
                Print($"Расчетный объем: {finalVolumeInLots:F5} лот ({normalizedVolumeInUnits} юнитов).");
                Print($"Цена входа (приблизительно): {entryPrice:F5}");
                Print($"SL: {stopLossInPips:F1} пп ({stopLossPrice.Value:F5}), TP: {takeProfitInPips:F1} пп ({takeProfitPrice.Value:F5}).");
                Print($"Открытие {OrderTradeType} по {SymbolName}...");

                // --- Открытие ордера ---
                try
                {
                    // Используем асинхронный вызов, но не ждем его завершения в OnTick, чтобы не блокировать поток
                    var tradeResult = ExecuteMarketOrder(OrderTradeType, _symbol.Name, normalizedVolumeInUnits, TradeLabel, stopLossPrice, takeProfitPrice);

                    if (tradeResult.IsSuccessful) {
                        Print($"УСПЕХ: Позиция {OrderTradeType} ID:{tradeResult.Position.Id} открыта по {tradeResult.Position.EntryPrice:F5}. Объем: {finalVolumeInLots:F5} лот(а).");
                        Print($"Позиция SL: {tradeResult.Position.StopLoss:F5}, TP: {tradeResult.Position.TakeProfit:F5}");
                    } else {
                        Print($"ОШИБКА ОТКРЫТИЯ: {tradeResult.Error}");
                    }
                }
                catch (Exception ex) {
                    Print($"ИСКЛЮЧЕНИЕ при ExecuteMarketOrder: {ex.Message}");
                }
            }
        }

        // Метод для определения цены Stop Loss на основе уровня ликвидности
        private double? CalculateStopLossPrice(Bars hourlyBars, TradeType tradeType)
        {
            // Используем значение из нового параметра для определения периода поиска
            int actualLookbackBars = SlLookbackPeriod; 

            // Проверяем, достаточно ли баров для расчета. 
            if (hourlyBars.Count < actualLookbackBars)
            {
                Print($"Недостаточно баров ({hourlyBars.Count}) для расчета SL с периодом {actualLookbackBars}. Требуется как минимум {actualLookbackBars} бар(ов). Сделка отменена.");
                return null;
            }

            if (tradeType == TradeType.Buy)
            {
                double lowestLow = double.MaxValue;
                
                // Ищем lowestLow за последние 'actualLookbackBars' баров
                for (int i = 1; i <= actualLookbackBars; i++)
                {
                    double currentLow = hourlyBars.LowPrices[hourlyBars.Count - i];
                    if (currentLow < lowestLow)
                    {
                        lowestLow = currentLow;
                    }
                }
                
                if (lowestLow == double.MaxValue) { 
                    Print("Ошибка: Не удалось найти lowestLow в заданном периоде для SL. Сделка отменена.");
                    return null; 
                }
                
                double slPrice = lowestLow - (10 * _symbol.PipSize);
                
                Print($"Найден уровень ликвидности для Buy (за последние {actualLookbackBars} H1 баров): {lowestLow:F5}, SL установлен на: {slPrice:F5}");
                return slPrice;
            }
            else // TradeType.Sell
            {
                double highestHigh = double.MinValue;
                
                for (int i = 1; i <= actualLookbackBars; i++)
                {
                    double currentHigh = hourlyBars.HighPrices[hourlyBars.Count - i];
                    if (currentHigh > highestHigh)
                    {
                        highestHigh = currentHigh;
                    }
                }
                
                if (highestHigh == double.MinValue) {
                    Print("Ошибка: Не удалось найти highestHigh в заданном периоде для SL. Сделка отменена.");
                    return null;
                }

                double slPrice = highestHigh + (10 * _symbol.PipSize);
                
                Print($"Найден уровень ликвидности для Sell (за последние {actualLookbackBars} H1 баров): {highestHigh:F5}, SL установлен на: {slPrice:F5}");
                return slPrice;
            }
        }

        // Метод для определения цены Take Profit на основе часового фрактала
        private double? CalculateTakeProfitPrice(Bars hourlyBars, TradeType tradeType)
        {
            // Обновляем данные индикатора фракталов перед использованием
             _fractals = Indicators.Fractals(hourlyBars, 5);


            // Определяем количество баров для анализа фракталов, оставляем запас для формирования фрактала
            // Фрактал из 5 свечей: 2 слева, 1 центральная, 2 справа. Значит, самый "свежий" фрактал может быть на индексе Count - 3.
            int barsToAnalyze = hourlyBars.Count; 
            if (barsToAnalyze < 5) // Минимальное количество баров для формирования фрактала
            {
                Print("Недостаточно баров для расчета TP на основе фракталов.");
                // Альтернативная логика, если фрактал не может быть рассчитан
                double fallbackOffset = 50 * _symbol.PipSize; // Например, 50 пипсов
                if (tradeType == TradeType.Buy) return _symbol.Ask + fallbackOffset;
                else return _symbol.Bid - fallbackOffset;
            }
            
            if (tradeType == TradeType.Buy)
            {
                double? highestFractalPrice = null;
                double currentPrice = _symbol.Ask; // Используем Ask для покупок
                
                // Ищем последний (самый свежий) верхний фрактал выше текущей цены
                // Фрактал формируется на баре i, если High[i] > High[i-1], High[i] > High[i-2], High[i] > High[i+1], High[i] > High[i+2]
                // Индикатор Fractals.UpFractal[index] вернет значение High[index] если на index есть верхний фрактал, иначе NaN.
                // Мы ищем фрактал, который уже сформировался, поэтому смотрим на бары до `barsToAnalyze - 3` (включительно)
                // Индексы для _fractals.UpFractal соответствуют индексам hourlyBars
                for (int i = barsToAnalyze - 3; i >= 2; i--) // Идем от более новых баров к старым
                {
                    if (!double.IsNaN(_fractals.UpFractal[i]))
                    {
                        double fractalPrice = _fractals.UpFractal[i]; // Это High цена бара, на котором сформировался фрактал
                        if (fractalPrice > currentPrice) // Фрактал должен быть выше текущей цены входа
                        {
                            highestFractalPrice = fractalPrice;
                            Print($"Найден верхний фрактал для Buy TP: {highestFractalPrice.Value:F5} на баре с индексом {i} (время {hourlyBars.OpenTimes[i]})");
                            return highestFractalPrice; // Используем первый же подходящий (самый свежий)
                        }
                    }
                }
                
                if (highestFractalPrice.HasValue)
                {
                    return highestFractalPrice.Value;
                }
                else
                {
                    // Если фрактал не найден, используем максимум за последние N баров + отступ
                    int fallbackLookback = Math.Min(24, barsToAnalyze -1); // Например, последние 24 бара или сколько есть
                     if (fallbackLookback <=0) fallbackLookback = 1;
                    double highestHighRecent = hourlyBars.HighPrices.Skip(hourlyBars.Count - fallbackLookback).Max();
                if (double.IsNaN(highestHighRecent)) highestHighRecent = 0.0;
                    double tpPrice = highestHighRecent + (20 * _symbol.PipSize); // Увеличенный отступ для fallback
                    Print($"Верхний фрактал выше текущей цены для Buy TP не найден. Используем максимум за {fallbackLookback} баров + отступ: {tpPrice:F5}");
                    return tpPrice;
                }
            }
            else // TradeType.Sell
            {
                double? lowestFractalPrice = null;
                double currentPrice = _symbol.Bid; // Используем Bid для продаж
                
                for (int i = barsToAnalyze - 3; i >= 2; i--) 
                {
                    if (!double.IsNaN(_fractals.DownFractal[i]))
                    {
                        double fractalPrice = _fractals.DownFractal[i]; // Это Low цена бара, на котором сформировался фрактал
                        if (fractalPrice < currentPrice) // Фрактал должен быть ниже текущей цены входа
                        {
                            lowestFractalPrice = fractalPrice;
                            Print($"Найден нижний фрактал для Sell TP: {lowestFractalPrice.Value:F5} на баре с индексом {i} (время {hourlyBars.OpenTimes[i]})");
                            return lowestFractalPrice; // Используем первый же подходящий (самый свежий)
                        }
                    }
                }
                
                if (lowestFractalPrice.HasValue)
                {
                    return lowestFractalPrice.Value;
                }
                else
                {
                    int fallbackLookback = Math.Min(24, barsToAnalyze -1);
                    if (fallbackLookback <=0) fallbackLookback = 1;
                    double lowestLowRecent = hourlyBars.LowPrices.Skip(hourlyBars.Count - fallbackLookback).Min();
                if (double.IsNaN(lowestLowRecent)) lowestLowRecent = double.MaxValue;
                    double tpPrice = lowestLowRecent - (20 * _symbol.PipSize);
                    Print($"Нижний фрактал ниже текущей цены для Sell TP не найден. Используем минимум за {fallbackLookback} баров - отступ: {tpPrice:F5}");
                    return tpPrice;
                }
            }
        }

        protected override void OnStop()
        {
            Print("Бот остановлен.");
        }
        // --- Визуальный анализ: определяет контекст по последним свечам ---
        private enum VisualContext { Long, Short, Neutral }

        private VisualContext AnalyzeVisualContext(Bars bars)
        {
            if (bars == null || bars.Count < 3)
                return VisualContext.Neutral;

            int last = bars.Count - 1;
            bool lastBear = bars.ClosePrices[last] < bars.OpenPrices[last];
            bool prevBear = bars.ClosePrices[last-1] < bars.OpenPrices[last-1];

            // Если две подряд медвежьи — шорт-контекст, иначе лонг-контекст
            if (lastBear && prevBear)
                return VisualContext.Short;
            return VisualContext.Long;
        }
    }
}
