using System;
using System.Threading.Tasks;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System.Collections.Generic; // Добавлено для использования List<T>

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

        // Параметр OrderTradeType теперь используется как fallback или если логика структуры отключена
        [Parameter("Направление сделки (если структура не определена)", DefaultValue = TradeType.Buy)]
        public TradeType FallbackOrderTradeType { get; set; }

        [Parameter("Час открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 23)]
        public int TriggerHour { get; set; }

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskRR")]
        public string TradeLabel { get; set; }

        [Parameter("SL Lookback Period (H1 Bars)", DefaultValue = 24, MinValue = 3, MaxValue = 200)]
        public int SlLookbackPeriod { get; set; }

        [Parameter("Market Structure Lookback (Bars)", DefaultValue = 30, MinValue = 10, MaxValue = 100)]
        public int MarketStructureLookbackPeriod { get; set; }

        [Parameter("Min Points for Structure", DefaultValue = 2, MinValue = 2, MaxValue = 5)]
        public int MinPointsForStructure { get; set; }


        // --- Индикаторы ---
        private Fractals _fractalsH1; // Фракталы для H1 (используются для TP и могут быть для структуры)
        private cAlgo.Indicators.ZigZag _zigZagH1;
        private cAlgo.Indicators.ZigZag _zigZagH4;

        [Parameter("ZigZag Depth", DefaultValue = 12)]
        public int ZigZagDepth { get; set; }
        [Parameter("ZigZag Deviation", DefaultValue = 5)]
        public double ZigZagDeviation { get; set; }
        [Parameter("ZigZag Backstep", DefaultValue = 3)]
        public int ZigZagBackstep { get; set; }

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;
        private TimeFrame _hourlyTimeframe = TimeFrame.Hour; // H1
        private TimeFrame _h4Timeframe = TimeFrame.Hour4;   // H4

        // Данные по барам
        private Bars _h1Bars;
        private Bars _h4Bars;

        // Списки для хранения недавних значимых максимумов и минимумов
        private List<double> _h1RecentHighs = new List<double>();
        private List<double> _h1RecentLows = new List<double>();
        private List<double> _h4RecentHighs = new List<double>();
        private List<double> _h4RecentLows = new List<double>();

        // Текущая определенная структура рынка
        private MarketStructure _currentH1Structure = MarketStructure.Undetermined;
        private MarketStructure _currentH4Structure = MarketStructure.Undetermined;

        // Время последнего обновления структуры
        private DateTime _lastH1BarTime = DateTime.MinValue;
        private DateTime _lastH4BarTime = DateTime.MinValue;

        // Enum для представления рыночной структуры
        public enum MarketStructure
        {
            Bullish,    // Бычья
            Bearish,    // Медвежья
            Sideways,   // Боковая
            Undetermined // Неопределенная
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

            // Инициализация данных по таймфреймам H1 и H4
            _h1Bars = MarketData.GetBars(_hourlyTimeframe, SymbolName);
            _h4Bars = MarketData.GetBars(_h4Timeframe, SymbolName);

            // Инициализация индикатора фракталов для часового таймфрейма
            _fractalsH1 = Indicators.Fractals(_h1Bars, 5);

            // Инициализация ZigZag для H1 и H4
            _zigZagH1 = new cAlgo.Indicators.ZigZag(_h1Bars, ZigZagDepth, ZigZagDeviation, ZigZagBackstep);
            _zigZagH4 = new cAlgo.Indicators.ZigZag(_h4Bars, ZigZagDepth, ZigZagDeviation, ZigZagBackstep);

            _lastTradeDate = DateTime.MinValue;

            // Первоначальное определение структуры
            if (_h1Bars.Count > MarketStructureLookbackPeriod)
            {
                UpdateRecentHighsLows(_h1Bars, _h1RecentHighs, _h1RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure +1); // +1 для сравнения n-1 и n-2
                _currentH1Structure = DetermineMarketStructureLogic(_h1RecentHighs, _h1RecentLows, _h1Bars, "H1");
                Print($"Начальная структура H1: {_currentH1Structure}. Highs: {string.Join(", ", _h1RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h1RecentLows.Select(l => l.ToString("F5")))}");
            }
            if (_h4Bars.Count > MarketStructureLookbackPeriod)
            {
                UpdateRecentHighsLows(_h4Bars, _h4RecentHighs, _h4RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure +1);
                _currentH4Structure = DetermineMarketStructureLogic(_h4RecentHighs, _h4RecentLows, _h4Bars, "H4");
                Print($"Начальная структура H4: {_currentH4Structure}. Highs: {string.Join(", ", _h4RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h4RecentLows.Select(l => l.ToString("F5")))}");
            }
        }

        protected override void OnTick()
        {
            if (_symbol == null) return;

            var serverTime = Server.Time;

            // Обновляем данные баров (лучше делать это здесь, чтобы всегда иметь актуальные)
            _h1Bars = MarketData.GetBars(_hourlyTimeframe, SymbolName);
            _h4Bars = MarketData.GetBars(_h4Timeframe, SymbolName);
            _fractalsH1 = Indicators.Fractals(_h1Bars, 5); // Обновляем фракталы H1
            _zigZagH1 = new cAlgo.Indicators.ZigZag(_h1Bars, ZigZagDepth, ZigZagDeviation, ZigZagBackstep);
            _zigZagH4 = new cAlgo.Indicators.ZigZag(_h4Bars, ZigZagDepth, ZigZagDeviation, ZigZagBackstep);

            // --- Обновление анализа структуры при открытии нового бара ---
            if (_h1Bars.Count > 0 && _h1Bars.OpenTimes.Last() != _lastH1BarTime)
            {
                _lastH1BarTime = _h1Bars.OpenTimes.Last();
                if (_h1Bars.Count > MarketStructureLookbackPeriod)
                {
                    UpdateRecentHighsLows(_h1Bars, _h1RecentHighs, _h1RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure +1);
                    _currentH1Structure = DetermineMarketStructureLogic(_h1RecentHighs, _h1RecentLows, _h1Bars, "H1");
                    Print($"Обновлена структура H1: {_currentH1Structure} в {serverTime}. Highs: {string.Join(", ", _h1RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h1RecentLows.Select(l => l.ToString("F5")))}");
                }
            }

            if (_h4Bars.Count > 0 && _h4Bars.OpenTimes.Last() != _lastH4BarTime)
            {
                _lastH4BarTime = _h4Bars.OpenTimes.Last();
                 if (_h4Bars.Count > MarketStructureLookbackPeriod)
                {
                    UpdateRecentHighsLows(_h4Bars, _h4RecentHighs, _h4RecentLows, MarketStructureLookbackPeriod, MinPointsForStructure +1);
                    _currentH4Structure = DetermineMarketStructureLogic(_h4RecentHighs, _h4RecentLows, _h4Bars, "H4");
                    Print($"Обновлена структура H4: {_currentH4Structure} в {serverTime}. Highs: {string.Join(", ", _h4RecentHighs.Select(h => h.ToString("F5")))}, Lows: {string.Join(", ", _h4RecentLows.Select(l => l.ToString("F5")))}");
                }
            }

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
            bool tradeAlreadyOpenedToday = serverTime.Date == _lastTradeDate.Date;

            if (isTriggerTime && !tradeAlreadyOpenedToday)
            {
                _lastTradeDate = serverTime.Date;

                if (Account.Equity <= 0) { Print("Ошибка: Экьюти <= 0. Сделка отменена."); return; }
                if (_h1Bars.Count == 0) // Используем _h1Bars, так как они обновляются в OnTick
                {
                    Print("Ошибка: Не удалось получить исторические данные для часового таймфрейма. Сделка отменена.");
                    return;
                }

                // --- Принятие решения на основе структуры ---
                // Бот должен открывать длинные позиции, когда структура бычья.
                // Используем логику: H4 бычья И (H1 бычья ИЛИ H1 не медвежья)
                // ИЛИ H1 бычья И (H4 бычья ИЛИ H4 не медвежья)
                bool isH4Supportive = _currentH4Structure == MarketStructure.Bullish || _currentH4Structure == MarketStructure.Sideways || _currentH4Structure == MarketStructure.Undetermined;
                bool isH1Supportive = _currentH1Structure == MarketStructure.Bullish || _currentH1Structure == MarketStructure.Sideways || _currentH1Structure == MarketStructure.Undetermined;

                bool canOpenLong = (_currentH4Structure == MarketStructure.Bullish && isH1Supportive) ||
                                   (_currentH1Structure == MarketStructure.Bullish && isH4Supportive);


                if (!canOpenLong)
                {
                    Print($"Условие для Long не выполнено в {serverTime}. H4: {_currentH4Structure}, H1: {_currentH1Structure}. Сделка не открывается.");
                    return;
                }

                Print($"Условие для Long выполнено в {serverTime}. H4: {_currentH4Structure}, H1: {_currentH1Structure}. Попытка открытия Long.");
                TradeType currentTradeType = TradeType.Buy; // Открываем только Long согласно условию

                // --- Расчет уровней SL и TP ---
                double? stopLossPrice = CalculateStopLossPrice(_h1Bars, currentTradeType);
                if (!stopLossPrice.HasValue)
                {
                    // Сообщение об ошибке уже выводится внутри CalculateStopLossPrice
                    return;
                }

                double? takeProfitPrice = CalculateTakeProfitPrice(_h1Bars, currentTradeType);
                if (!takeProfitPrice.HasValue)
                {
                    Print("Ошибка: Не удалось определить уровень для Take Profit. Сделка отменена.");
                    return;
                }

                double entryPrice = (currentTradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;

                double stopLossInPips = Math.Abs((entryPrice - stopLossPrice.Value) / _symbol.PipSize);
                double takeProfitInPips = Math.Abs((takeProfitPrice.Value - entryPrice) / _symbol.PipSize);

                if (stopLossInPips <= 0)
                {
                    Print($"Ошибка: Рассчитанный Stop Loss ({stopLossInPips:F1} пп) <= 0. SL ({stopLossPrice.Value:F5}), Цена входа ({entryPrice:F5}). Сделка отменена.");
                    return;
                }

                // --- Расчет объема ---
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
                Print($"SL: {stopLossInPips:F1} пп ({stopLossPrice.Value:F5}), TP: {takeProfitInPips:F1} пп ({takeProfitPrice.Value:F5}).");
                Print($"Открытие {currentTradeType} по {SymbolName}...");

                // --- Открытие ордера ---
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

        // Метод для обновления списков недавних значимых Highs и Lows
        // lookbackPeriod - количество баров для анализа фракталов
        // maxPointsToStore - максимальное количество последних Highs/Lows для хранения
        private void UpdateRecentHighsLows(Bars bars, List<double> recentHighs, List<double> recentLows, int lookbackPeriod, int maxPointsToStore)
        {
            recentHighs.Clear();
            recentLows.Clear();

            // Фракталу нужно минимум 5 баров (2 слева, 1 центр, 2 справа).
            // Для анализа `lookbackPeriod` баров, нам нужно `lookbackPeriod + 2` баров, чтобы последний фрактал мог сформироваться.
            if (bars.Count < Math.Max(5, lookbackPeriod + 2) ) 
            {
                //Print($"Недостаточно баров ({bars.Count}) на {bars.TimeFrame} для определения Highs/Lows с lookback {lookbackPeriod}. Требуется {Math.Max(5, lookbackPeriod + 2)}.");
                return;
            }

            Fractals localFractals = Indicators.Fractals(bars, 3);

            // Идем по барам в обратном порядке, чтобы найти последние фракталы
            // Фрактал на баре `i` подтверждается, когда бар `i+2` закрылся.
            // Поэтому самый "свежий" фрактал, который мы можем рассматривать, находится на `bars.Count - 1 - 2 = bars.Count - 3`.
            for (int i = bars.Count - 3; i >= Math.Max(0, bars.Count - 1 - lookbackPeriod - 2) ; i--)
            {
                if (recentHighs.Count < maxPointsToStore && !double.IsNaN(localFractals.UpFractal[i]))
                {
                    // Добавляем, только если это новый High (отличается от последнего добавленного)
                    // или если список пуст. Это помогает избежать дублирования одинаковых фрактальных уровней подряд.
                    if (recentHighs.Count == 0 || Math.Abs(localFractals.UpFractal[i] - recentHighs.First()) > _symbol.PipSize * 0.1) // Небольшой допуск
                    {
                         recentHighs.Insert(0, localFractals.UpFractal[i]); // Добавляем в начало списка (самый свежий первым)
                    }
                }
                if (recentLows.Count < maxPointsToStore && !double.IsNaN(localFractals.DownFractal[i]))
                {
                    if (recentLows.Count == 0 || Math.Abs(localFractals.DownFractal[i] - recentLows.First()) > _symbol.PipSize * 0.1)
                    {
                        recentLows.Insert(0, localFractals.DownFractal[i]); // Добавляем в начало списка
                    }
                }
                
                if (recentHighs.Count >= maxPointsToStore && recentLows.Count >= maxPointsToStore) break;
            }
            // Print($"Для {bars.TimeFrame} найдено: {recentHighs.Count} Highs, {recentLows.Count} Lows.");
        }

        // Метод для определения рыночной структуры
        private MarketStructure DetermineMarketStructureLogic(List<double> highs, List<double> lows, Bars relevantBars, string tfName)
        {
            // Требуется MinPointsForStructure (например, 2) Highs и Lows для определения структуры.
            // Списки хранят MinPointsForStructure+1 элементов, чтобы можно было сравнить [n-1] с [n-2]
            if (highs.Count < MinPointsForStructure || lows.Count < MinPointsForStructure)
            {
                //Print($"{tfName} - Недостаточно данных для определения структуры (нужно минимум {MinPointsForStructure} High и {MinPointsForStructure} Low). Найдено H:{highs.Count}, L:{lows.Count}");
                return MarketStructure.Undetermined;
            }

            // Берем последние MinPointsForStructure точек. Списки упорядочены от старых к новым.
            // lastHigh/Low - самый свежий, prevHigh/Low - перед ним.
            double lastHigh = highs[highs.Count - 1];
            double prevHigh = highs[highs.Count - MinPointsForStructure]; // highs[highs.Count - 2] if MinPointsForStructure is 2
            double lastLow = lows[lows.Count - 1];
            double prevLow = lows[lows.Count - MinPointsForStructure];   // lows[lows.Count - 2] if MinPointsForStructure is 2

            // Проверка на последовательность для бычьего тренда (HH и HL)
            bool isConsistentlyHigherHighs = true;
            bool isConsistentlyHigherLows = true;
            for (int i = 1; i < MinPointsForStructure; i++)
            {
                if (highs[highs.Count - i] <= highs[highs.Count - i - 1]) isConsistentlyHigherHighs = false;
                if (lows[lows.Count - i] <= lows[lows.Count - i - 1]) isConsistentlyHigherLows = false;
            }

            if (isConsistentlyHigherHighs && isConsistentlyHigherLows)
            {
                //Print($"{tfName} - Бычья структура: последовательные HH и HL.");
                return MarketStructure.Bullish;
            }

            // Проверка на последовательность для медвежьего тренда (LH и LL)
            bool isConsistentlyLowerHighs = true;
            bool isConsistentlyLowerLows = true;
            for (int i = 1; i < MinPointsForStructure; i++)
            {
                if (highs[highs.Count - i] >= highs[highs.Count - i - 1]) isConsistentlyLowerHighs = false;
                if (lows[lows.Count - i] >= lows[lows.Count - i - 1]) isConsistentlyLowerLows = false;
            }

            if (isConsistentlyLowerHighs && isConsistentlyLowerLows)
            {
                //Print($"{tfName} - Медвежья структура: последовательные LH и LL.");
                return MarketStructure.Bearish;
            }
            
            // Слом структуры: если был предыдущий HL, и цена его пробила
            // Это более сложная логика, требующая идентификации "значимого" HL.
            // Пока упрощенный вариант: если последний Low ниже предыдущего Low, а последний High не смог стать HH
            if (lows.Count >= 2 && highs.Count >= 2) { // Нужны хотя бы две точки для сравнения
                 double currentPrice = relevantBars.ClosePrices.Last();
                 // Ищем последний значимый HL. Это Low[n-2], если Low[n-1] его пробил.
                 // И при этом High[n-1] не смог обновить High[n-2] (т.е. не было HH перед пробоем HL)
                 if (lows[lows.Count -1] < lows[lows.Count -2] && // LL (пробой предыдущего Low)
                     highs[highs.Count -1] < highs[highs.Count -2]) // LH (не смогли сделать HH)
                 {
                    //Print($"{tfName} - Медвежья структура: слом предыдущего HL (LL: {lows[lows.Count -1]:F5} < {lows[lows.Count -2]:F5}) при LH ({highs[highs.Count -1]:F5} < {highs[highs.Count -2]:F5}).");
                    return MarketStructure.Bearish;
                 }
            }


            //Print($"{tfName} - Структура боковая или не определена.");
            return MarketStructure.Sideways; // Если ни бычья, ни медвежья - считаем боковой/неопределенной
        }


        // Метод для определения цены Stop Loss на основе уровня ликвидности
        private double? CalculateStopLossPrice(Bars bars, TradeType tradeType)
        {
            int actualLookbackBars = SlLookbackPeriod;
            if (bars.Count < actualLookbackBars)
            {
                Print($"Недостаточно баров ({bars.Count}) для расчета SL с периодом {actualLookbackBars}. Требуется {actualLookbackBars}. Сделка отменена.");
                return null;
            }

            if (tradeType == TradeType.Buy)
            {
                double lowestLow = double.MaxValue;
                for (int i = 1; i <= actualLookbackBars; i++) // Смотрим на последние actualLookbackBars, включая текущий незавершенный (индекс Count-1) до Count-actualLookbackBars
                {
                    // Индекс бара: bars.Count - i. Самый свежий полный бар это bars.Count - 2.
                    // Если i=1, это bars.LowPrices[bars.Count - 1] (текущий, если он есть)
                    // Если i=actualLookbackBars, это bars.LowPrices[bars.Count - actualLookbackBars]
                    if (bars.Count -i < 0) break; // Предохранитель
                    double currentLow = bars.LowPrices[bars.Count - i];
                    if (currentLow < lowestLow) lowestLow = currentLow;
                }
                if (lowestLow == double.MaxValue) { Print("Ошибка: Не удалось найти lowestLow для SL. Сделка отменена."); return null; }
                double slPrice = lowestLow - (10 * _symbol.PipSize); // Отступ в 10 пипсов
                //Print($"Найден уровень ликвидности для Buy (за последние {actualLookbackBars} H1 баров): {lowestLow:F5}, SL установлен на: {slPrice:F5}");
                return slPrice;
            }
            else // TradeType.Sell
            {
                double highestHigh = double.MinValue;
                for (int i = 1; i <= actualLookbackBars; i++)
                {
                    if (bars.Count -i < 0) break;
                    double currentHigh = bars.HighPrices[bars.Count - i];
                    if (currentHigh > highestHigh) highestHigh = currentHigh;
                }
                if (highestHigh == double.MinValue) { Print("Ошибка: Не удалось найти highestHigh для SL. Сделка отменена."); return null; }
                double slPrice = highestHigh + (10 * _symbol.PipSize);
                //Print($"Найден уровень ликвидности для Sell (за последние {actualLookbackBars} H1 баров): {highestHigh:F5}, SL установлен на: {slPrice:F5}");
                return slPrice;
            }
        }

        // Метод для определения цены Take Profit на основе часового фрактала
        private double? CalculateTakeProfitPrice(Bars bars, TradeType tradeType)
        {
            // _fractalsH1 уже обновлен в OnTick
            int barsToAnalyze = bars.Count;
            if (barsToAnalyze < 5) // Минимальное количество баров для формирования фрактала
            {
                Print("Недостаточно баров для расчета TP на основе фракталов H1. Используется fallback.");
                double fallbackOffset = 50 * _symbol.PipSize;
                if (tradeType == TradeType.Buy) return _symbol.Ask + fallbackOffset;
                else return _symbol.Bid - fallbackOffset;
            }

            double currentEntryPrice = (tradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;

            if (tradeType == TradeType.Buy)
            {
                // Ищем последний (самый свежий) верхний фрактал ВЫШЕ текущей цены входа
                // Фрактал формируется на баре i, если High[i] > High[i-1], High[i] > High[i-2], High[i] > High[i+1], High[i] > High[i+2]
                // Индикатор Fractals.UpFractal[index] вернет значение High[index] если на index есть верхний фрактал, иначе NaN.
                // Мы ищем фрактал, который уже сформировался, поэтому смотрим на бары до `barsToAnalyze - 3` (включительно)
                for (int i = barsToAnalyze - 3; i >= 2; i--) // Идем от более новых баров к старым
                {
                    if (!double.IsNaN(_fractalsH1.UpFractal[i]))
                    {
                        double fractalPrice = _fractalsH1.UpFractal[i];
                        if (fractalPrice > currentEntryPrice) // Фрактал должен быть выше текущей цены входа
                        {
                            //Print($"Найден верхний фрактал для Buy TP: {fractalPrice:F5} на баре H1 (время {bars.OpenTimes[i]})");
                            return fractalPrice; // Используем первый же подходящий (самый свежий из тех, что выше входа)
                        }
                    }
                }
                // Если фрактал выше текущей цены не найден, используем максимум за N баров + отступ
                int fallbackLookback = Math.Min(24, barsToAnalyze -1);
                if (fallbackLookback <=0) fallbackLookback = 1;
                double highestHighRecent = bars.HighPrices.Skip(Math.Max(0,bars.Count - fallbackLookback)).DefaultIfEmpty(_symbol.Ask).Max();
                double tpPrice = highestHighRecent + (20 * _symbol.PipSize);
                Print($"Верхний фрактал выше текущей цены для Buy TP не найден. Используем максимум за {fallbackLookback} H1 баров + отступ: {tpPrice:F5}");
                return tpPrice;
            }
            else // TradeType.Sell
            {
                // Ищем последний (самый свежий) нижний фрактал НИЖЕ текущей цены входа
                for (int i = barsToAnalyze - 3; i >= 2; i--)
                {
                    if (!double.IsNaN(_fractalsH1.DownFractal[i]))
                    {
                        double fractalPrice = _fractalsH1.DownFractal[i];
                        if (fractalPrice < currentEntryPrice) // Фрактал должен быть ниже текущей цены входа
                        {
                            //Print($"Найден нижний фрактал для Sell TP: {fractalPrice:F5} на баре H1 (время {bars.OpenTimes[i]})");
                            return fractalPrice;
                        }
                    }
                }
                int fallbackLookback = Math.Min(24, barsToAnalyze -1);
                if (fallbackLookback <=0) fallbackLookback = 1;
                double lowestLowRecent = bars.LowPrices.Skip(Math.Max(0,bars.Count - fallbackLookback)).DefaultIfEmpty(_symbol.Bid).Min();
                double tpPrice = lowestLowRecent - (20 * _symbol.PipSize);
                Print($"Нижний фрактал ниже текущей цены для Sell TP не найден. Используем минимум за {fallbackLookback} H1 баров - отступ: {tpPrice:F5}");
                return tpPrice;
            }
        }

        protected override void OnStop()
        {
            Print("Бот остановлен.");
        }
    }
}

// --- Реализация ZigZag как обычного класса (без атрибута [Indicator]) ---
namespace cAlgo.Indicators
{
    public class ZigZag
    {
        public int Depth { get; set; }
        public double Deviation { get; set; }
        public int Backstep { get; set; }
        public double[] ZigZagBuffer { get; private set; }

        private int _lastHigh;
        private int _lastLow;
        private Bars _bars;

        public ZigZag(Bars bars, int depth, double deviation, int backstep)
        {
            _bars = bars;
            Depth = depth;
            Deviation = deviation;
            Backstep = backstep;
            ZigZagBuffer = new double[bars.Count];
            _lastHigh = -1;
            _lastLow = -1;
            CalculateAll();
        }

        public void CalculateAll()
        {
            for (int i = 0; i < _bars.Count; i++)
                Calculate(i);
        }

        private void Calculate(int index)
        {
            ZigZagBuffer[index] = double.NaN;
            if (index < Depth)
                return;
            double high = double.MinValue;
            double low = double.MaxValue;
            for (int j = index - Depth + 1; j <= index; j++)
            {
                if (_bars.HighPrices[j] > high) high = _bars.HighPrices[j];
                if (_bars.LowPrices[j] < low) low = _bars.LowPrices[j];
            }
            if (_bars.HighPrices[index] == high && (index - _lastHigh) > Backstep)
            {
                ZigZagBuffer[index] = high;
                _lastHigh = index;
            }
            else if (_bars.LowPrices[index] == low && (index - _lastLow) > Backstep)
            {
                ZigZagBuffer[index] = low;
                _lastLow = index;
            }
        }
    }
}

