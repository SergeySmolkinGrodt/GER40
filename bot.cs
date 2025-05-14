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

        [Parameter("Час открытия (Время Сервера)", DefaultValue = 8, MinValue = 0, MaxValue = 23)]
        public int TriggerHour { get; set; }

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskH4")]
        public string TradeLabel { get; set; }

        // --- Параметры EMA ---
        [Parameter("EMA Fast Period", DefaultValue = 8, MinValue = 2, MaxValue = 50)]
        public int EmaFastPeriod { get; set; }

        [Parameter("EMA Slow Period", DefaultValue = 21, MinValue = 5, MaxValue = 100)]
        public int EmaSlowPeriod { get; set; }

        // --- Параметры управления рисками ---
        [Parameter("Stop Loss (в пунктах)", DefaultValue = 50, MinValue = 10, MaxValue = 200)]
        public double StopLossInPips { get; set; }

        [Parameter("Take Profit (в пунктах)", DefaultValue = 100, MinValue = 20, MaxValue = 400)]
        public double TakeProfitInPips { get; set; }

        [Parameter("Trailing Stop (в пунктах)", DefaultValue = 30, MinValue = 10, MaxValue = 100)]
        public double TrailingStopInPips { get; set; }

        [Parameter("Trailing Start (в пунктах)", DefaultValue = 50, MinValue = 20, MaxValue = 200)]
        public double TrailingStartInPips { get; set; }

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;
        private TimeFrame _h4Timeframe = TimeFrame.Hour4;   
        private Bars _h4Bars;
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private MarketContext _currentContext = MarketContext.Undetermined;

        public enum MarketContext
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
            _emaFast = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, EmaFastPeriod);
            _emaSlow = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, EmaSlowPeriod);
            _lastTradeDate = DateTime.MinValue;

            Print($"Бот запущен для {SymbolName}");
            Print($"EMA Fast: {EmaFastPeriod}, EMA Slow: {EmaSlowPeriod}");
            Print($"Stop Loss: {StopLossInPips} пп, Take Profit: {TakeProfitInPips} пп");
            Print($"Trailing Stop: {TrailingStopInPips} пп, Trailing Start: {TrailingStartInPips} пп");
        }

        protected override void OnTick()
        {
            if (_symbol == null) return;

            var serverTime = Server.Time;
            _h4Bars = MarketData.GetBars(_h4Timeframe, SymbolName);

            // Обновляем контекст рынка
            UpdateMarketContext();

            // Управление открытыми позициями
            ManageOpenPositions();

            // Проверяем время для открытия новых позиций
            bool isTriggerTime = serverTime.Hour == TriggerHour && serverTime.Minute == TriggerMinute;
            bool tradeAlreadyOpenedToday = serverTime.Date == _lastTradeDate.Date;

            if (isTriggerTime && !tradeAlreadyOpenedToday)
            {
                _lastTradeDate = serverTime.Date;

                if (Account.Equity <= 0) 
                { 
                    Print("Ошибка: Экьюти <= 0. Сделка отменена."); 
                    return; 
                }

                if (_h4Bars.Count == 0) 
                {
                    Print("Ошибка: Не удалось получить исторические данные для таймфрейма H4. Сделка отменена.");
                    return;
                }

                // Определяем направление сделки на основе контекста
                TradeType tradeType = DetermineTradeType();
                if (tradeType == TradeType.Buy && _currentContext != MarketContext.Bullish)
                {
                    Print($"Текущий контекст ({_currentContext}) не поддерживает Long позицию");
                    return;
                }
                if (tradeType == TradeType.Sell && _currentContext != MarketContext.Bearish)
                {
                    Print($"Текущий контекст ({_currentContext}) не поддерживает Short позицию");
                    return;
                }

                double entryPrice = (tradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;
                double stopLossPrice = CalculateStopLossPrice(tradeType, entryPrice);
                double takeProfitPrice = CalculateTakeProfitPrice(tradeType, entryPrice);
                
                double riskAmount = Account.Equity * (RiskPercent / 100.0);
                double pipValue = _symbol.PipValue;
                double stopLossInPips = Math.Abs(entryPrice - stopLossPrice) / _symbol.PipSize;
                
                double calculatedVolumeInLots = riskAmount / (stopLossInPips * pipValue);
                long volumeInUnits = (long)_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots);
                long normalizedVolumeInUnits = (long)_symbol.NormalizeVolumeInUnits((double)volumeInUnits, RoundingMode.Down);

                if (normalizedVolumeInUnits < _symbol.VolumeInUnitsMin)
                {
                    normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMin;
                }

                if (normalizedVolumeInUnits > _symbol.VolumeInUnitsMax)
                {
                    normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMax;
                }
                
                double finalVolumeInLots = _symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits);

                Print($"Время сделки ({serverTime}). Риск: {RiskPercent}%, Экьюти: {Account.Equity:F2} {Account.Asset.Name}");
                Print($"Расчетный объем: {finalVolumeInLots:F5} лот ({normalizedVolumeInUnits} юнитов)");
                Print($"Цена входа: {entryPrice:F5}, SL: {stopLossPrice:F5}, TP: {takeProfitPrice:F5}");
                Print($"Открытие {tradeType} по {SymbolName}...");

                try
                {
                    var tradeResult = ExecuteMarketOrder(tradeType, _symbol.Name, normalizedVolumeInUnits, TradeLabel, stopLossPrice, takeProfitPrice);

                    if (tradeResult.IsSuccessful)
                    {
                        Print($"УСПЕХ: Позиция {tradeType} ID:{tradeResult.Position.Id} открыта по {tradeResult.Position.EntryPrice:F5}");
                        Print($"Объем: {finalVolumeInLots:F5} лот(а), SL: {stopLossPrice:F5}, TP: {takeProfitPrice:F5}");
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

        private void UpdateMarketContext()
        {
            if (_emaFast.Result.Last(0) > _emaSlow.Result.Last(0) && 
                _emaFast.Result.Last(1) > _emaSlow.Result.Last(1))
            {
                _currentContext = MarketContext.Bullish;
            }
            else if (_emaFast.Result.Last(0) < _emaSlow.Result.Last(0) && 
                     _emaFast.Result.Last(1) < _emaSlow.Result.Last(1))
            {
                _currentContext = MarketContext.Bearish;
            }
            else
            {
                _currentContext = MarketContext.Sideways;
            }
        }

        private TradeType DetermineTradeType()
        {
            // Дополнительные условия для определения направления
            bool isAboveEma = _h4Bars.ClosePrices.Last(0) > _emaFast.Result.Last(0);
            bool isBelowEma = _h4Bars.ClosePrices.Last(0) < _emaFast.Result.Last(0);

            if (_currentContext == MarketContext.Bullish && isAboveEma)
            {
                return TradeType.Buy;
            }
            else if (_currentContext == MarketContext.Bearish && isBelowEma)
            {
                return TradeType.Sell;
            }

            return TradeType.Buy; // По умолчанию
        }

        private double CalculateStopLossPrice(TradeType tradeType, double entryPrice)
        {
            if (tradeType == TradeType.Buy)
            {
                return entryPrice - (StopLossInPips * _symbol.PipSize);
            }
            else
            {
                return entryPrice + (StopLossInPips * _symbol.PipSize);
            }
        }

        private double CalculateTakeProfitPrice(TradeType tradeType, double entryPrice)
        {
            if (tradeType == TradeType.Buy)
            {
                return entryPrice + (TakeProfitInPips * _symbol.PipSize);
            }
            else
            {
                return entryPrice - (TakeProfitInPips * _symbol.PipSize);
            }
        }

        private void ManageOpenPositions()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName != SymbolName || position.Label != TradeLabel)
                    continue;

                // Закрытие в конце дня
                if (Server.Time.Hour == 23 && Server.Time.Minute >= 59)
                {
                    ClosePosition(position);
                    Print($"Позиция ID:{position.Id} закрыта в конце дня");
                    continue;
                }

                // Трейлинг стоп
                if (position.TradeType == TradeType.Buy)
                {
                    double distance = position.EntryPrice - position.StopLoss.Value;
                    if (distance >= TrailingStartInPips * _symbol.PipSize)
                    {
                        double newStopLoss = position.EntryPrice - (TrailingStopInPips * _symbol.PipSize);
                        if (newStopLoss > position.StopLoss.Value)
                        {
                            ModifyPosition(position, newStopLoss, position.TakeProfit.Value);
                        }
                    }
                }
                else
                {
                    double distance = position.StopLoss.Value - position.EntryPrice;
                    if (distance >= TrailingStartInPips * _symbol.PipSize)
                    {
                        double newStopLoss = position.EntryPrice + (TrailingStopInPips * _symbol.PipSize);
                        if (newStopLoss < position.StopLoss.Value)
                        {
                            ModifyPosition(position, newStopLoss, position.TakeProfit.Value);
                        }
                    }
                }
            }
        }

        protected override void OnStop()
        {
            Print("Бот остановлен");
        }
    }
}
