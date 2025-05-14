using System;
using System.Threading.Tasks;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System.Collections.Generic; 

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class DailyAsianOpenRiskH4 : Robot
    {
        // --- Параметры бота ---
        [Parameter("Название символа", DefaultValue = "GER40.cash")]
        public new string SymbolName { get; set; }

        [Parameter("Процент риска от эквити (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Направление сделки (если структура не определена)", DefaultValue = TradeType.Buy)]
        public TradeType FallbackOrderTradeType { get; set; }

        [Parameter("Час открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 23)]
        public int TriggerHour { get; set; }

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskH4_ZZ_2R")] // Обновлена метка
        public string TradeLabel { get; set; }

        [Parameter("SL Lookback Period (H4 Bars)", DefaultValue = 6, MinValue = 1, MaxValue = 50)]
        public int SlLookbackPeriod { get; set; }

        [Parameter("Market Structure Lookback (H4 Bars)", DefaultValue = 30, MinValue = 10, MaxValue = 100)]
        public int MarketStructureLookbackPeriod { get; set; } // Используется для фильтрации точек ZigZag

        [Parameter("Min Points for Structure (H4)", DefaultValue = 2, MinValue = 2, MaxValue = 5)]
        public int MinPointsForStructure { get; set; }

        // --- Индикаторы ---
        private cAlgo.Indicators.ZigZag _zigZagH4;

        [Parameter("ZigZag Depth (H4)", DefaultValue = 12)]
        public int ZigZagDepth { get; set; }
        [Parameter("ZigZag Deviation (H4 Pips)", DefaultValue = 5)]
        public double ZigZagDeviation { get; set; }
        [Parameter("ZigZag Backstep (H4)", DefaultValue = 3)] // Этот параметр не используется в текущей реализации ZigZag, но оставлен
        public int ZigZagBackstep { get; set; }

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;
        private TimeFrame _h4Timeframe = TimeFrame.Hour4;   

        private Bars _h4Bars;

        private List<double> _h4RecentHighs = new List<double>();
        private List<double> _h4RecentLows = new List<double>();

        private MarketStructure _currentH4Structure = MarketStructure.Undetermined;

        private DateTime _lastH4BarTime = DateTime.MinValue;

        public enum MarketStructure
        {
            Bullish,    
            Bearish,    
            Sideways,   
            Undetermined 
        }


        protected override void OnStart()
        {
            _symbol = Symbols.GetSymbol(SymbolName);
            if (_symbol == null)
            {
                Print($"Ошибка: Символ '{SymbolName}' не найден.");
                Stop();
                return;
            }

            _h4Bars = MarketData.GetBars(_h4Timeframe, SymbolName);
            _zigZagH4 = new cAlgo.Indicators.ZigZag(_h4Bars, _symbol, ZigZagDepth, ZigZagDeviation, ZigZagBackstep);

            _lastTradeDate = DateTime.MinValue;

            // Первоначальное определение структуры
            // Убедимся, что достаточно баров для MarketStructureLookbackPeriod перед вызовом UpdateRecentHighsLows
            if (_h4Bars.Count > MarketStructureLookbackPeriod) 
            {
                UpdateRecentHighsLows(_h4Bars, _h4RecentHighs, _h4RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure + 1);
                _currentH4Structure = DetermineMarketStructureLogic(_h4RecentHighs, _h4RecentLows, _h4Bars, "H4");
                Print($"Начальная структура H4 (по ZigZag): {_currentH4Structure}. Highs: {string.Join(", ", _h4RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h4RecentLows.Select(l => l.ToString("F5")))}");
            }
            else
            {
                Print($"Недостаточно баров ({_h4Bars.Count}) для начального определения структуры с периодом {MarketStructureLookbackPeriod}.");
            }
        }

        protected override void OnTick()
        {
            if (_symbol == null) return;

            var serverTime = Server.Time;

            _h4Bars = MarketData.GetBars(_h4Timeframe, SymbolName);
            // Каждый тик создается новый экземпляр ZigZag, что может быть ресурсоемко.
            // Рассмотрите возможность обновления ZigZag без пересоздания, если его реализация это позволяет.
            // В данной реализации ZigZag.CalculateAll() вызывается в конструкторе.
            _zigZagH4 = new cAlgo.Indicators.ZigZag(_h4Bars, _symbol, ZigZagDepth, ZigZagDeviation, ZigZagBackstep); 

            if (_h4Bars.Count > 0 && _h4Bars.OpenTimes.Last() != _lastH4BarTime)
            {
                _lastH4BarTime = _h4Bars.OpenTimes.Last();
                 if (_h4Bars.Count > MarketStructureLookbackPeriod) // Убедимся, что достаточно баров
                {
                    UpdateRecentHighsLows(_h4Bars, _h4RecentHighs, _h4RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure + 1);
                    _currentH4Structure = DetermineMarketStructureLogic(_h4RecentHighs, _h4RecentLows, _h4Bars, "H4");
                    Print($"Обновлена структура H4 (по ZigZag): {_currentH4Structure} в {serverTime}. Highs: {string.Join(", ", _h4RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h4RecentLows.Select(l => l.ToString("F5")))}");
                }
            }

            // --- Логика закрытия позиций и открытия новых сделок (остается без изменений) ---
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
            bool tradeAlreadyOpenedToday = serverTime.Date == _lastTradeDate.Date;

            if (isTriggerTime && !tradeAlreadyOpenedToday)
            {
                _lastTradeDate = serverTime.Date;

                if (Account.Equity <= 0) { Print("Ошибка: Экьюти <= 0. Сделка отменена."); return; }
                if (_h4Bars.Count == 0) 
                {
                    Print("Ошибка: Не удалось получить исторические данные для таймфрейма H4. Сделка отменена.");
                    return;
                }
                if (_currentH4Structure == MarketStructure.Undetermined && (_h4RecentHighs.Count == 0 || _h4RecentLows.Count == 0) )
                {
                    Print($"Структура H4 не определена или отсутствуют точки ZigZag для анализа в {serverTime}. Попытка обновить структуру.");
                     if (_h4Bars.Count > MarketStructureLookbackPeriod)
                    {
                        UpdateRecentHighsLows(_h4Bars, _h4RecentHighs, _h4RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure + 1);
                        _currentH4Structure = DetermineMarketStructureLogic(_h4RecentHighs, _h4RecentLows, _h4Bars, "H4");
                        Print($"Повторное обновление структуры H4 (по ZigZag): {_currentH4Structure}. Highs: {string.Join(", ", _h4RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h4RecentLows.Select(l => l.ToString("F5")))}");
                    }
                    if (_currentH4Structure == MarketStructure.Undetermined)
                    {
                         Print($"Структура H4 все еще не определена в {serverTime}. Сделка не открывается.");
                         return;
                    }
                }


                bool canOpenLong = _currentH4Structure == MarketStructure.Bullish;
                bool canOpenShort = _currentH4Structure == MarketStructure.Bearish;
                
                TradeType currentTradeType = FallbackOrderTradeType; 
                bool shouldOpenTrade = false;

                if (canOpenLong)
                {
                    Print($"Условие для Long выполнено в {serverTime}. H4 структура (ZigZag): {_currentH4Structure}. Попытка открытия Long.");
                    currentTradeType = TradeType.Buy;
                    shouldOpenTrade = true;
                }
                else if (canOpenShort)
                {
                    Print($"Условие для Short выполнено в {serverTime}. H4 структура (ZigZag): {_currentH4Structure}. Попытка открытия Short.");
                    currentTradeType = TradeType.Sell;
                    shouldOpenTrade = true;
                }
                else
                {
                    Print($"Условия для Long или Short на H4 (ZigZag) не выполнены в {serverTime}. H4 структура: {_currentH4Structure}. Сделка не открывается.");
                    return; 
                }

                if (!shouldOpenTrade)
                {
                    return;
                }
                
                double? stopLossPrice = CalculateStopLossPrice(_h4Bars, currentTradeType); 
                if (!stopLossPrice.HasValue)
                {
                    return; 
                }

                double entryPrice = (currentTradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;
                double stopLossInPips = Math.Abs((entryPrice - stopLossPrice.Value) / _symbol.PipSize);

                if (stopLossInPips <= 0)
                {
                    Print($"Ошибка: Рассчитанный Stop Loss ({stopLossInPips:F1} пп) <= 0. SL ({stopLossPrice.Value:F5}), Цена входа ({entryPrice:F5}). Сделка отменена.");
                    return;
                }

                double takeProfitInPips = stopLossInPips * 2.0;
                double? takeProfitPrice;

                if (currentTradeType == TradeType.Buy)
                {
                    takeProfitPrice = entryPrice + (takeProfitInPips * _symbol.PipSize);
                }
                else 
                {
                    takeProfitPrice = entryPrice - (takeProfitInPips * _symbol.PipSize);
                }

                if (!takeProfitPrice.HasValue || takeProfitInPips <=0) 
                {
                    Print($"Ошибка: Не удалось рассчитать Take Profit (TP pips: {takeProfitInPips:F1}, TP price: {takeProfitPrice}). Сделка отменена.");
                    return;
                }
                
                double riskAmount = Account.Equity * (RiskPercent / 100.0);
                double pipValuePerLot = 0;
                if (_symbol.TickSize > 0 && _symbol.PipSize > 0)
                {
                    pipValuePerLot = _symbol.TickValue * (_symbol.PipSize / _symbol.TickSize);
                }

                if (pipValuePerLot <= 0)
                {
                    Print($"Ошибка: Не удалось рассчитать стоимость пункта (Pip Value = {pipValuePerLot}). Сделка отменена.");
                    return;
                }

                double calculatedVolumeInLots = Math.Max(riskAmount / (stopLossInPips * pipValuePerLot), _symbol.VolumeInUnitsToQuantity(_symbol.VolumeInUnitsMin));
                long volumeInUnits = (long)_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots);
                long normalizedVolumeInUnits = (long)_symbol.NormalizeVolumeInUnits((double)volumeInUnits, RoundingMode.Down);

                if (normalizedVolumeInUnits < _symbol.VolumeInUnitsMin)
                {
                    normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMin;
                    Print($"Информация: Рассчитанный объем после нормализации стал меньше минимального. Используем минимальный объем: {_symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits):F5} лотов ({normalizedVolumeInUnits} юнитов).");
                }

                if (normalizedVolumeInUnits > _symbol.VolumeInUnitsMax)
                {
                    normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMax;
                    Print($"Информация: Рассчитанный объем превышает максимальный. Используем максимальный объем: {_symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits):F5} лотов ({normalizedVolumeInUnits} юнитов).");
                }
                
                double finalVolumeInLots = _symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits);

                Print($"Время сделки ({serverTime}). Риск: {RiskPercent}%, Экьюти: {Account.Equity:F2} {Account.Asset.Name}, Сумма риска: {riskAmount:F2} {Account.Asset.Name}.");
                Print($"Расчетный объем: {finalVolumeInLots:F5} лот ({normalizedVolumeInUnits} юнитов).");
                Print($"Цена входа (приблизительно): {entryPrice:F5}");
                Print($"SL: {stopLossInPips:F1} пп ({stopLossPrice.Value:F5}), TP (2R): {takeProfitInPips:F1} пп ({takeProfitPrice.Value:F5}).");
                Print($"Открытие {currentTradeType} по {SymbolName} на H4 (структура по ZigZag)...");

                try
                {
                    var tradeResult = ExecuteMarketOrder(currentTradeType, _symbol.Name, normalizedVolumeInUnits, TradeLabel, stopLossPrice, takeProfitPrice);

                    if (tradeResult.IsSuccessful)
                    {
                        Print($"УСПЕХ: Позиция {currentTradeType} ID:{tradeResult.Position.Id} открыта по {tradeResult.Position.EntryPrice:F5}. Объем: {finalVolumeInLots:F5} лот(а).");
                        Print($"Позиция SL: {tradeResult.Position.StopLoss:F5}, TP: {tradeResult.Position.TakeProfit:F5}");
                    }
                    else
                    {
                        Print($"ОШИБКА ОТКРЫТИЯ: {tradeResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"ИСКЛЮЧЕНИЕ при ExecuteMarketOrder: {ex.Message}");
                }
            }
        }

        // Метод для обновления списков недавних значимых Highs и Lows с использованием ZigZag
        // lookbackPeriod - количество баров H4 для анализа (фильтрация точек ZigZag)
        // maxPointsToStore - максимальное количество последних Highs/Lows для хранения (для DetermineMarketStructureLogic)
        private void UpdateRecentHighsLows(Bars bars, List<double> recentHighs, List<double> recentLows, int lookbackPeriod, int maxPointsToStore)
        {
            recentHighs.Clear();
            recentLows.Clear();

            if (_zigZagH4 == null || _zigZagH4.ZigZagPoints.Count < 2)
            {
                Print($"UpdateRecentHighsLows: Недостаточно точек ZigZag ({_zigZagH4?.ZigZagPoints?.Count ?? 0}) для анализа.");
                return;
            }

            var allZigZagPoints = _zigZagH4.ZigZagPoints;
            
            // Фильтруем точки ZigZag, чтобы они попадали в заданный lookbackPeriod
            // Точка ZigZag (Tuple<int, double>) - Item1 это индекс бара
            int firstBarIndexInLookback = Math.Max(0, bars.Count - lookbackPeriod);
            var relevantZigZagPoints = allZigZagPoints.Where(p => p.Item1 >= firstBarIndexInLookback).ToList();

            if (relevantZigZagPoints.Count < 2)
            {
                Print($"UpdateRecentHighsLows: Недостаточно точек ZigZag ({relevantZigZagPoints.Count}) в пределах периода {lookbackPeriod} баров (начиная с индекса {firstBarIndexInLookback}). Всего точек ZigZag: {allZigZagPoints.Count}.");
                return;
            }

            // Определяем, является ли последняя точка ZigZag максимумом или минимумом относительно предпоследней
            // Это предполагает, что точки ZigZag чередуются
            bool lastRelevantPointIsHigh = relevantZigZagPoints.Last().Item2 > relevantZigZagPoints[relevantZigZagPoints.Count - 2].Item2;
            
            // Если последние две точки равны (маловероятно с Deviation > 0, но возможно), пытаемся определить по предыдущим
            if (relevantZigZagPoints.Last().Item2 == relevantZigZagPoints[relevantZigZagPoints.Count - 2].Item2 && relevantZigZagPoints.Count >=3)
            {
                 lastRelevantPointIsHigh = relevantZigZagPoints[relevantZigZagPoints.Count - 2].Item2 > relevantZigZagPoints[relevantZigZagPoints.Count - 3].Item2;
            }


            // Заполняем списки recentHighs и recentLows, двигаясь от новых точек к старым
            for (int i = relevantZigZagPoints.Count - 1; i >= 0; i--)
            {
                double currentPointValue = relevantZigZagPoints[i].Item2;
                bool isCurrentPointHighType;

                // Определяем тип текущей точки (High или Low) на основе ее чередования от последней точки
                if ((relevantZigZagPoints.Count - 1 - i) % 2 == 0) // последняя, третья с конца и т.д.
                {
                    isCurrentPointHighType = lastRelevantPointIsHigh;
                }
                else // вторая с конца, четвертая с конца и т.д.
                {
                    isCurrentPointHighType = !lastRelevantPointIsHigh;
                }

                if (isCurrentPointHighType)
                {
                    if (recentHighs.Count < maxPointsToStore)
                    {
                        if (recentHighs.Count == 0 || Math.Abs(currentPointValue - recentHighs.First()) > _symbol.PipSize * 0.01) // Проверка на почти дубликат
                           recentHighs.Insert(0, currentPointValue); // Вставляем в начало, чтобы сохранить порядок от старых к новым после Reverse()
                    }
                }
                else
                {
                    if (recentLows.Count < maxPointsToStore)
                    {
                         if (recentLows.Count == 0 || Math.Abs(currentPointValue - recentLows.First()) > _symbol.PipSize * 0.01)
                            recentLows.Insert(0, currentPointValue);
                    }
                }

                if (recentHighs.Count >= maxPointsToStore && recentLows.Count >= maxPointsToStore)
                    break; 
            }
            
            // DetermineMarketStructureLogic ожидает списки, отсортированные от старых к новым
            // Так как мы вставляли в начало (Insert(0,...)), списки уже отсортированы от старых к новым, если мы итерировали от старых ZigZag точек.
            // Но мы итерировали от НОВЫХ ZigZag точек к СТАРЫМ и вставляли в НАЧАЛО списка.
            // Пример: ZZ = [z1,z2,z3,z4 (newest)]. Iteration: z4, z3, z2, z1.
            // highs.Insert(0, z4_val), highs.Insert(0, z2_val) -> highs = [z2_val, z4_val] (старый, новый) - это то что нужно.

            // Print($"UpdateRecentHighsLows (ZigZag): Собрано Highs: {recentHighs.Count}, Lows: {recentLows.Count}");
        }


        // Метод для определения рыночной структуры (остается без изменений, т.к. работает с готовыми списками Highs/Lows)
        private MarketStructure DetermineMarketStructureLogic(List<double> highs, List<double> lows, Bars relevantBars, string tfName)
        {
            if (highs.Count < MinPointsForStructure || lows.Count < MinPointsForStructure)
            {
                // Print($"{tfName} - Недостаточно данных для определения структуры (нужно минимум {MinPointsForStructure} High и {MinPointsForStructure} Low). Найдено H:{highs.Count}, L:{lows.Count}");
                return MarketStructure.Undetermined;
            }

            bool isConsistentlyHigherHighs = true;
            bool isConsistentlyHigherLows = true;
            // Списки УЖЕ отсортированы от старых к новым благодаря Insert(0,...) при обходе ZigZag от новых к старым.
            // highs[0] - самый старый, highs[highs.Count-1] - самый новый.
            for (int i = 1; i < MinPointsForStructure; i++) // Сравниваем highs[i] с highs[i-1]
            {
                // Для бычьего тренда: High[i] > High[i-1] (последовательно повышающиеся максимумы)
                // highs.Count-i - новый, highs.Count-i-1 - старый перед ним.
                // Если MinPointsForStructure = 2, мы сравниваем последний High (index Count-1) с предпоследним (index Count-2)
                // И также второй с конца (Count-2) с третьим с конца (Count-3), если MinPointsForStructure = 3
                // Логика должна быть: highs[k] > highs[k-1] для k от 1 до highs.Count-1
                // Переписываем для ясности, используя последние MinPointsForStructure точек
                // highs[highs.Count - 1] vs highs[highs.Count - 2]
                // highs[highs.Count - 2] vs highs[highs.Count - 3] ...
                if (highs[highs.Count - i] <= highs[highs.Count - i - 1]) isConsistentlyHigherHighs = false;
                if (lows[lows.Count - i] <= lows[lows.Count - i - 1]) isConsistentlyHigherLows = false;
            }


            if (isConsistentlyHigherHighs && isConsistentlyHigherLows)
            {
                return MarketStructure.Bullish;
            }

            bool isConsistentlyLowerHighs = true;
            bool isConsistentlyLowerLows = true;
            for (int i = 1; i < MinPointsForStructure; i++)
            {
                // Для медвежьего тренда: High[i] < High[i-1]
                if (highs[highs.Count - i] >= highs[highs.Count - i - 1]) isConsistentlyLowerHighs = false;
                if (lows[lows.Count - i] >= lows[lows.Count - i - 1]) isConsistentlyLowerLows = false;
            }

            if (isConsistentlyLowerHighs && isConsistentlyLowerLows)
            {
                return MarketStructure.Bearish;
            }
            
            // Проверка на слом структуры (упрощенная)
            if (lows.Count >= 2 && highs.Count >= 2) { 
                 // Медвежий слом: последний Low ниже предпоследнего Low, И последний High ниже предпоследнего High (LL и LH)
                 if (lows[lows.Count -1] < lows[lows.Count -2] && 
                     highs[highs.Count -1] < highs[highs.Count -2]) 
                 {
                    return MarketStructure.Bearish;
                 }
                 // Бычий слом: последний High выше предпоследнего High, И последний Low выше предпоследнего Low (HH и HL)
                 // Это уже покрывается isConsistentlyHigherHighs && isConsistentlyHigherLows, но можно оставить для явности
                 if (highs[highs.Count -1] > highs[highs.Count -2] &&
                     lows[lows.Count -1] > lows[lows.Count -2]) 
                 {
                    return MarketStructure.Bullish;
                 }
            }

            // Print($"{tfName} - Структура боковая или не определена после проверок.");
            return MarketStructure.Sideways; 
        }

        // Метод CalculateStopLossPrice (остается без изменений)
        private double? CalculateStopLossPrice(Bars bars, TradeType tradeType) 
        {
            int actualLookbackBars = SlLookbackPeriod; 
            if (bars.Count < actualLookbackBars)
            {
                Print($"Недостаточно баров H4 ({bars.Count}) для расчета SL с периодом {actualLookbackBars}. Требуется {actualLookbackBars}. Сделка отменена.");
                return null;
            }

            if (tradeType == TradeType.Buy)
            {
                double lowestLow = double.MaxValue;
                for (int i = 1; i <= actualLookbackBars; i++) 
                {
                    if (bars.Count -i < 0) break; 
                    double currentLow = bars.LowPrices[bars.Count - i];
                    if (currentLow < lowestLow) lowestLow = currentLow;
                }
                if (lowestLow == double.MaxValue) { Print("Ошибка: Не удалось найти lowestLow для SL на H4. Сделка отменена."); return null; }
                double slPrice = lowestLow - (10 * _symbol.PipSize); 
                return slPrice;
            }
            else 
            {
                double highestHigh = double.MinValue;
                for (int i = 1; i <= actualLookbackBars; i++)
                {
                    if (bars.Count -i < 0) break;
                    double currentHigh = bars.HighPrices[bars.Count - i];
                    if (currentHigh > highestHigh) highestHigh = currentHigh;
                }
                if (highestHigh == double.MinValue) { Print("Ошибка: Не удалось найти highestHigh для SL на H4. Сделка отменена."); return null; }
                double slPrice = highestHigh + (10 * _symbol.PipSize);
                return slPrice;
            }
        }


        protected override void OnStop()
        {
            Print("Бот остановлен.");
        }
    }
}

// --- Реализация ZigZag (остается без изменений) ---
namespace cAlgo.Indicators
{
    public class ZigZag 
    {
        public int Depth { get; set; }
        public double DeviationInPips { get; set; } 
        public int Backstep { get; set; } // Не используется в текущей логике CalculateAll, но параметр оставлен
        public double[] ZigZagBuffer { get; private set; }
        public List<Tuple<int, double>> ZigZagPoints { get; private set; } 

        private Bars _bars;
        private Symbol _symbol; 
        private double _pipSizeValue;

        public ZigZag(Bars bars, Symbol symbol, int depth, double deviationInPips, int backstep) 
        {
            _bars = bars;
            _symbol = symbol; 
            Depth = depth;
            DeviationInPips = deviationInPips; 
            Backstep = backstep; 
            _pipSizeValue = _symbol.PipSize; 

            ZigZagBuffer = new double[bars.Count];
            ZigZagPoints = new List<Tuple<int, double>>();
            CalculateAll();
        }

        public void CalculateAll()
        {
            if (_bars.Count < Depth) return; // Требуется минимальное количество баров для расчета

            for (int i = 0; i < _bars.Count; i++) ZigZagBuffer[i] = double.NaN;
            ZigZagPoints.Clear(); // Очищаем предыдущие точки перед новым расчетом

            int lastPivotIdx = -1;
            double lastPivotVal = 0;
            int currentTrend = 0; // 0 = undefined, 1 = up, -1 = down

            // Инициализация первой точки (если возможно)
            if (_bars.Count > 0)
            {
                // Пытаемся найти первую точку, чтобы начать с нее
                // Можно просто взять первый бар как начальную точку или более сложную логику
                // Для простоты, начнем определение тренда с первого значимого движения
            }


            for (int i = 0; i < _bars.Count; i++)
            {
                double high = _bars.HighPrices[i];
                double low = _bars.LowPrices[i];

                if (currentTrend == 0) 
                {
                    // Начальное определение тренда
                    // Ищем первый значимый экстремум после Depth баров
                    if (i < Depth) continue; // Пропускаем первые Depth баров для стабилизации

                    // Проверяем, является ли текущий бар локальным максимумом или минимумом за Depth период
                    double maxHighInDepth = 0;
                    double minLowInDepth = double.MaxValue;
                    for(int k=i-Depth+1; k<=i; k++) {
                        if(_bars.HighPrices[k] > maxHighInDepth) maxHighInDepth = _bars.HighPrices[k];
                        if(_bars.LowPrices[k] < minLowInDepth) minLowInDepth = _bars.LowPrices[k];
                    }

                    if (high == maxHighInDepth) { // Потенциальный первый High
                        AddZigZagPoint(i, high);
                        lastPivotVal = high;
                        lastPivotIdx = i;
                        currentTrend = -1; // Ожидаем Low
                    } else if (low == minLowInDepth) { // Потенциальный первый Low
                        AddZigZagPoint(i, low);
                        lastPivotVal = low;
                        lastPivotIdx = i;
                        currentTrend = 1; // Ожидаем High
                    }
                }
                else if (currentTrend == 1) // Текущий тренд вверх, ищем High
                {
                    if (high > lastPivotVal) { // Новый максимум в текущем восходящем движении
                        lastPivotVal = high;
                        lastPivotIdx = i;
                        // Обновляем последнюю точку ZigZag, если она того же типа (High)
                        if (ZigZagPoints.Any() && ZigZagPoints.Last().Item1 == lastPivotIdx) ZigZagPoints[ZigZagPoints.Count-1] = Tuple.Create(lastPivotIdx, lastPivotVal);
                        else if (ZigZagPoints.Any() && ZigZagPoints.Last().Item2 < lastPivotVal) { // Если последняя точка была Low, а эта High выше
                             // Это условие не совсем корректно для обновления, AddZigZagPoint должен справиться
                        }
                         // Не добавляем каждую новую свечу, только когда тренд меняется или точка обновляется
                    } else if (low < lastPivotVal - DeviationInPips * _pipSizeValue) { // Смена тренда на Down
                        // Добавляем предыдущий максимум перед сменой тренда
                        if(lastPivotIdx != -1 && (!ZigZagPoints.Any() || ZigZagPoints.Last().Item1 != lastPivotIdx || ZigZagPoints.Last().Item2 != _bars.HighPrices[lastPivotIdx])) {
                             AddZigZagPoint(lastPivotIdx, _bars.HighPrices[lastPivotIdx]); // Фиксируем последний High
                        }
                        currentTrend = -1; // Тренд сменился на нисходящий
                        lastPivotVal = low;  // Новый экстремум (Low) для нового тренда
                        lastPivotIdx = i;
                        AddZigZagPoint(i, lastPivotVal); // Добавляем новую точку Low
                    }
                }
                else // currentTrend == -1, текущий тренд вниз, ищем Low
                {
                     if (low < lastPivotVal) { // Новый минимум в текущем нисходящем движении
                        lastPivotVal = low;
                        lastPivotIdx = i;
                        // Обновляем последнюю точку ZigZag, если она того же типа (Low)
                         if (ZigZagPoints.Any() && ZigZagPoints.Last().Item1 == lastPivotIdx) ZigZagPoints[ZigZagPoints.Count-1] = Tuple.Create(lastPivotIdx, lastPivotVal);
                         else if (ZigZagPoints.Any() && ZigZagPoints.Last().Item2 > lastPivotVal) {
                            // Аналогично
                         }
                    } else if (high > lastPivotVal + DeviationInPips * _pipSizeValue) { // Смена тренда на Up
                        // Добавляем предыдущий минимум
                        if(lastPivotIdx != -1 && (!ZigZagPoints.Any() || ZigZagPoints.Last().Item1 != lastPivotIdx || ZigZagPoints.Last().Item2 != _bars.LowPrices[lastPivotIdx])) {
                            AddZigZagPoint(lastPivotIdx, _bars.LowPrices[lastPivotIdx]); // Фиксируем последний Low
                        }
                        currentTrend = 1; // Тренд сменился на восходящий
                        lastPivotVal = high; // Новый экстремум (High)
                        lastPivotIdx = i;
                        AddZigZagPoint(i, lastPivotVal); // Добавляем новую точку High
                    }
                }
            }
            // Добавляем последнюю незафиксированную точку, если она есть
             if (lastPivotIdx != -1 && ZigZagPoints.Any() && ZigZagPoints.Last().Item1 != lastPivotIdx)
            {
                 AddZigZagPoint(lastPivotIdx, lastPivotVal);
            }
             else if (lastPivotIdx != -1 && !ZigZagPoints.Any()) // Если вообще не было точек
            {
                 AddZigZagPoint(lastPivotIdx, lastPivotVal);
            }
        }

        // Логика AddZigZagPoint немного упрощена для ясности, основная логика формирования ZigZag в CalculateAll
         private void AddZigZagPoint(int index, double value)
        {
            // Если последняя точка на том же баре, заменяем ее, если новое значение "лучше"
            if (ZigZagPoints.Any() && ZigZagPoints.Last().Item1 == index) 
            {
                bool isReplacingHigher = value > ZigZagPoints.Last().Item2;
                bool isReplacingLower = value < ZigZagPoints.Last().Item2;

                // Определяем, была ли последняя точка High или Low (приблизительно)
                bool lastPointWasHigh = false;
                if (ZigZagPoints.Count >= 2) {
                    lastPointWasHigh = ZigZagPoints.Last().Item2 > ZigZagPoints[ZigZagPoints.Count - 2].Item2;
                } else if (ZigZagPoints.Any()) { // Если только одна точка, сравниваем с ценой открытия
                    lastPointWasHigh = ZigZagPoints.Last().Item2 > _bars.OpenPrices[ZigZagPoints.Last().Item1];
                }


                if ((lastPointWasHigh && isReplacingHigher) || (!lastPointWasHigh && isReplacingLower)) {
                    ZigZagPoints[ZigZagPoints.Count - 1] = Tuple.Create(index, value);
                }
                return; // Не добавляем новую, если на том же баре и не лучше
            }
            
            // Предотвращение добавления точки того же типа подряд, если значение не лучше
            if (ZigZagPoints.Count >= 1) {
                var lastZPoint = ZigZagPoints.Last();
                bool lastWasHigh = false;
                 if (ZigZagPoints.Count >= 2) {
                    lastWasHigh = lastZPoint.Item2 > ZigZagPoints[ZigZagPoints.Count - 2].Item2;
                } else { // Если только одна точка
                    lastWasHigh = lastZPoint.Item2 > _bars.OpenPrices[lastZPoint.Item1]; // Примерное определение
                }

                bool currentIsHigh = value > lastZPoint.Item2; // Это не тип, а сравнение значений

                // Если последняя точка была High и новая точка тоже High (value > lastZPoint.Value)
                if (lastWasHigh && value > lastZPoint.Item2) {
                    ZigZagPoints[ZigZagPoints.Count -1] = Tuple.Create(index, value); // Заменяем последнюю High на более высокую High
                    if(index < ZigZagBuffer.Length) ZigZagBuffer[index] = value;
                    return;
                }
                // Если последняя точка была Low и новая точка тоже Low (value < lastZPoint.Value)
                if (!lastWasHigh && value < lastZPoint.Item2) {
                     ZigZagPoints[ZigZagPoints.Count -1] = Tuple.Create(index, value); // Заменяем последнюю Low на более низкую Low
                     if(index < ZigZagBuffer.Length) ZigZagBuffer[index] = value;
                     return;
                }
            }


            ZigZagPoints.Add(Tuple.Create(index, value));
            if(index < ZigZagBuffer.Length) ZigZagBuffer[index] = value;
        }
    }
}
