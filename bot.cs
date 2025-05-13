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

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 1, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskRR")]
        public string TradeLabel { get; set; }

        // --- Индикаторы ---
        private Fractals _fractals;

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;
        private TimeFrame _hourlyTimeframe = TimeFrame.Hour;

        // --- Методы cBot ---
        protected override void OnStart()
        {
            _symbol = Symbols.GetSymbol(SymbolName);
            if (_symbol == null) { Print($"Ошибка: Символ '{SymbolName}' не найден."); Stop(); return; }

            // Инициализация индикатора фракталов для часового таймфрейма
            _fractals = Indicators.Fractals(5);

            Print($"Бот запущен для символа: {_symbol.Name}");
            Print($"Время открытия сделки (Сервер): {TriggerHour:D2}:{TriggerMinute:D2}");
            Print($"Риск: {RiskPercent}%");
            _lastTradeDate = DateTime.MinValue;
        }

        protected override void OnTick()
        {
            if (_symbol == null) return;

            var serverTime = Server.Time;
            bool isTriggerTime = serverTime.Hour == TriggerHour && serverTime.Minute == TriggerMinute;
            bool tradeAlreadyOpenedToday = serverTime.Date == _lastTradeDate;

            if (isTriggerTime && !tradeAlreadyOpenedToday)
            {
                _lastTradeDate = serverTime.Date;

                if (Account.Equity <= 0) { Print("Ошибка: Экьюти <= 0. Сделка отменена."); return; }

                // Получаем данные часового таймфрейма
                var hourlyBars = MarketData.GetBars(_hourlyTimeframe);
              
                // --- Расчет уровней SL и TP ---
                double? stopLossPrice = CalculateStopLossPrice(hourlyBars, OrderTradeType);
                if (!stopLossPrice.HasValue) {
                    Print("Ошибка: Не удалось определить уровень ликвидности для Stop Loss. Сделка отменена.");
                    return;
                }

                double? takeProfitPrice = CalculateTakeProfitPrice(hourlyBars, OrderTradeType);
                if (!takeProfitPrice.HasValue) {
                    Print("Ошибка: Не удалось определить фрактал для Take Profit. Сделка отменена.");
                    return;
                }

                // Текущая цена для точки входа
                double entryPrice = (OrderTradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;
                
                // Рассчитываем расстояние в пунктах до SL и TP
                double stopLossInPips = Math.Abs((entryPrice - stopLossPrice.Value) / _symbol.PipSize);
                double takeProfitInPips = Math.Abs((takeProfitPrice.Value - entryPrice) / _symbol.PipSize);
                


                // --- Расчет объема ---
                double riskAmount = Account.Equity * (RiskPercent / 100.0);
                double pipValuePerLot = 0;
                if (_symbol.TickSize > 0 && _symbol.PipSize > 0) {
                     pipValuePerLot = _symbol.TickValue * (_symbol.PipSize / _symbol.TickSize);
                }
                if (pipValuePerLot <= 0) { Print($"Ошибка: Не удалось рассчитать стоимость пункта (Pip Value = {pipValuePerLot}). Сделка отменена."); return; }

                double calculatedVolumeInLots = riskAmount / (stopLossInPips * pipValuePerLot);
                long volumeInUnits = Convert.ToInt64(_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots));
                var normalizedVolumeResult = _symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);
                long normalizedVolumeInUnits = Convert.ToInt64(normalizedVolumeResult);

                if (normalizedVolumeInUnits < _symbol.VolumeInUnitsMin) { Print($"Предупреждение: Рассчитанный объем < мин. Сделка отменена."); return; }
                if (normalizedVolumeInUnits > (long)_symbol.VolumeInUnitsMax) { Print($"Предупреждение: Рассчитанный объем > макс. Используется макс."); normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMax; }

                double finalVolumeInLots = _symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits);

                Print($"Время сделки ({serverTime}). Риск: {RiskPercent}%, Экьюти: {Account.Equity:F2} {Account.Asset.Name}, Сумма: {riskAmount:F2} {Account.Asset.Name}.");
                Print($"Расчетный объем: {finalVolumeInLots:F5} лот ({normalizedVolumeInUnits} юнитов).");
                Print($"SL: {stopLossInPips:F1} пп ({stopLossPrice:F5}), TP: {takeProfitInPips:F1} пп ({takeProfitPrice:F5}).");
                Print($"Открытие {OrderTradeType} по {SymbolName}...");

                // --- Открытие ордера ---
                try
                {
                    TradeOperation operation = ExecuteMarketOrderAsync(OrderTradeType, _symbol.Name, normalizedVolumeInUnits, TradeLabel, stopLossPrice, takeProfitPrice);

                    if (operation.TradeResult != null) {
                        if (operation.TradeResult.IsSuccessful) {
                            Print($"УСПЕХ: Позиция {OrderTradeType} ID:{operation.TradeResult.Position.Id} открыта по {operation.TradeResult.Position.EntryPrice}. Объем: {finalVolumeInLots:F5} лот(а).");
                            Print($"Позиция SL: {operation.TradeResult.Position.StopLoss}, TP: {operation.TradeResult.Position.TakeProfit}");
                        } else {
                            Print($"ОШИБКА ОТКРЫТИЯ: {operation.TradeResult.Error}");
                        }
                    } else {
                        Print($"ОШИБКА: TradeResult is null после вызова ExecuteMarketOrderAsync. Статус операции: {operation.ToString()}");
                    }
                }
                catch (Exception ex) {
                    Print($"ИСКЛЮЧЕНИЕ при ExecuteMarketOrderAsync: {ex.Message}");
                }
            }
        }

        // Метод для определения цены Stop Loss на основе уровня ликвидности
        private double? CalculateStopLossPrice(Bars hourlyBars, TradeType tradeType)
        {
            if (tradeType == TradeType.Buy)
            {
                // Для Buy - ищем минимум за последние N часовых свечей (уровень ликвидности ниже текущей цены)
                int lookbackBars = hourlyBars.Count - 1;
                double lowestLow = double.MaxValue;
                
                for (int i = 1; i <= lookbackBars; i++)
                {
                    double currentLow = hourlyBars.LowPrices[hourlyBars.Count - i];
                    if (currentLow < lowestLow)
                    {
                        lowestLow = currentLow;
                    }
                }
                
                // Добавляем отступ для надежности (10 пипсов)
                double slPrice = lowestLow - (10 * _symbol.PipSize);
                
                Print($"Найден уровень ликвидности для Buy: {lowestLow}, SL установлен на: {slPrice}");
                return slPrice;
            }
            else // TradeType.Sell
            {
                // Для Sell - ищем максимум за последние N часовых свечей (уровень ликвидности выше текущей цены)
                int lookbackBars = hourlyBars.Count - 1;
                double highestHigh = double.MinValue;
                
                for (int i = 1; i <= lookbackBars; i++)
                {
                    double currentHigh = hourlyBars.HighPrices[hourlyBars.Count - i];
                    if (currentHigh > highestHigh)
                    {
                        highestHigh = currentHigh;
                    }
                }
                
                // Добавляем отступ для надежности (10 пипсов)
                double slPrice = highestHigh + (10 * _symbol.PipSize);
                
                Print($"Найден уровень ликвидности для Sell: {highestHigh}, SL установлен на: {slPrice}");
                return slPrice;
            }
        }

        // Метод для определения цены Take Profit на основе часового фрактала
        private double? CalculateTakeProfitPrice(Bars hourlyBars, TradeType tradeType)
        {
            // Определяем количество баров для анализа фракталов
            int bars = hourlyBars.Count - 3;
            
            if (tradeType == TradeType.Buy)
            {
                // Для Buy - ищем верхний фрактал (потенциальное сопротивление)
                double? highestFractal = null;
                double currentPrice = _symbol.Ask;
                
                // Проверяем фракталы начиная с более поздних (ближайшие к текущему времени)
                for (int i = 2; i < bars; i++)
                {
                    int index = hourlyBars.Count - i;
                    
                    // Проверяем, есть ли верхний фрактал
                    if (!double.IsNaN(_fractals.UpFractal[index]) && _fractals.UpFractal[index] > 0)
                    
                    if (!double.IsNaN(_fractals.UpFractal[index]) && _fractals.UpFractal[index] > 0)
                    {
                        double fractalPrice = hourlyBars.HighPrices[index];
                        // Фрактал должен быть выше текущей цены
                        if (fractalPrice > currentPrice)
                        {
                            if (!highestFractal.HasValue || fractalPrice > highestFractal.Value)
                            {
                                highestFractal = fractalPrice;
                            }
                        }
                    }
                }
                
                if (highestFractal.HasValue)
                {
                    Print($"Найден верхний фрактал для Buy TP: {highestFractal.Value}");
                    return highestFractal.Value;
                }
                else
                {
                    // Если фрактал не найден, используем максимум + отступ
                    double highest = hourlyBars.HighPrices.Maximum(bars);
                    double tpPrice = highest + (10 * _symbol.PipSize);
                    Print($"Фрактал не найден для Buy TP, используем максимум + отступ: {tpPrice}");
                    return tpPrice;
                }
            }
            else // TradeType.Sell
            {
                // Для Sell - ищем нижний фрактал (потенциальная поддержка)
                double? lowestFractal = null;
                double currentPrice = _symbol.Bid;
                
                // Проверяем фракталы начиная с более поздних (ближайшие к текущему времени)
                for (int i = 2; i < bars; i++)
                {
                    int index = hourlyBars.Count - i;
                    
                    // Проверяем, есть ли нижний фрактал
                    if (!double.IsNaN(_fractals.DownFractal[index]) && _fractals.DownFractal[index] > 0)
                    
                    if (!double.IsNaN(_fractals.DownFractal[index]) && _fractals.DownFractal[index] > 0)
                    {
                        double fractalPrice = hourlyBars.LowPrices[index];
                        // Фрактал должен быть ниже текущей цены
                        if (fractalPrice < currentPrice)
                        {
                            if (!lowestFractal.HasValue || fractalPrice < lowestFractal.Value)
                            {
                                lowestFractal = fractalPrice;
                            }
                        }
                    }
                }
                
                if (lowestFractal.HasValue)
                {
                    Print($"Найден нижний фрактал для Sell TP: {lowestFractal.Value}");
                    return lowestFractal.Value;
                }
                else
                {
                    // Если фрактал не найден, используем минимум - отступ
                    double lowest = hourlyBars.LowPrices.Minimum(bars);
                    double tpPrice = lowest - (10 * _symbol.PipSize);
                    Print($"Фрактал не найден для Sell TP, используем минимум - отступ: {tpPrice}");
                    return tpPrice;
                }
            }
        }

        protected override void OnStop()
        {
            Print("Бот остановлен.");
        }
    }
}