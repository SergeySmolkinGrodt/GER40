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
        [Parameter("Название символа", DefaultValue = "GER40")]
        public new string SymbolName { get; set; }

        [Parameter("Процент риска от эквити (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Направление сделки", DefaultValue = TradeType.Buy)]
        public TradeType OrderTradeType { get; set; }

        [Parameter("Час открытия (Время Сервера)", DefaultValue = 0, MinValue = 0, MaxValue = 23)]
        public int TriggerHour { get; set; }

        [Parameter("Минута открытия (Время Сервера)", DefaultValue = 1, MinValue = 0, MaxValue = 59)]
        public int TriggerMinute { get; set; }

        [Parameter("Stop Loss (пункты)", DefaultValue = 50, MinValue = 1)]
        public double StopLossInPips { get; set; }

        [Parameter("Reward Ratio (RR)", DefaultValue = 3.0, MinValue = 0.1, Step = 0.1)]
        public double RewardRatio { get; set; }

        [Parameter("Метка сделки (Label)", DefaultValue = "DailyAsianOpenRiskRR")]
        public string TradeLabel { get; set; }

        // --- Внутренние переменные ---
        private DateTime _lastTradeDate;
        private Symbol _symbol;

        // --- Методы cBot ---
        protected override void OnStart()
        {
            _symbol = Symbols.GetSymbol(SymbolName);
            if (_symbol == null) { Print($"Ошибка: Символ '{SymbolName}' не найден."); Stop(); return; }
            if (StopLossInPips <= 0) { Print("Внимание: 'Stop Loss (пункты)' должен быть > 0."); }
            if (RewardRatio <= 0) { Print("Внимание: 'Reward Ratio (RR)' должен быть > 0."); }
            Print($"Бот запущен для символа: {_symbol.Name}");
            Print($"Время открытия сделки (Сервер): {TriggerHour:D2}:{TriggerMinute:D2}");
            Print($"Риск: {RiskPercent}%, SL: {StopLossInPips} пп, RR: 1:{RewardRatio}");
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

                if (StopLossInPips <= 0) { Print($"Ошибка: Stop Loss ({StopLossInPips} пп) должен быть > 0. Сделка отменена."); return; }
                if (RewardRatio <= 0) { Print($"Ошибка: Reward Ratio ({RewardRatio}) должен быть > 0. Сделка отменена."); return; }
                if (Account.Equity <= 0) { Print("Ошибка: Экьюти <= 0. Сделка отменена."); return; }

                // --- Расчет объема ---
                double riskAmount = Account.Equity * (RiskPercent / 100.0);
                double pipValuePerLot = 0;
                if (_symbol.TickSize > 0 && _symbol.PipSize > 0) {
                     pipValuePerLot = _symbol.TickValue * (_symbol.PipSize / _symbol.TickSize);
                }
                if (pipValuePerLot <= 0) { Print($"Ошибка: Не удалось рассчитать стоимость пункта (Pip Value = {pipValuePerLot}). Сделка отменена."); return; }

                double calculatedVolumeInLots = riskAmount / (StopLossInPips * pipValuePerLot);

                // !! ИЗМЕНЕНИЕ: Используем Convert.ToInt64() для результата QuantityToVolumeInUnits !!
                // Вместо: long volumeInUnits = (long)_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots);
                long volumeInUnits = Convert.ToInt64(_symbol.QuantityToVolumeInUnits(calculatedVolumeInLots));


                var normalizedVolumeResult = _symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);
                long normalizedVolumeInUnits = Convert.ToInt64(normalizedVolumeResult); // Оставляем Convert.ToInt64 здесь тоже

                if (normalizedVolumeInUnits < _symbol.VolumeInUnitsMin) { Print($"Предупреждение: Рассчитанный объем < мин. Сделка отменена."); return; }
                if (normalizedVolumeInUnits > (long)_symbol.VolumeInUnitsMax) { Print($"Предупреждение: Рассчитанный объем > макс. Используется макс."); normalizedVolumeInUnits = (long)_symbol.VolumeInUnitsMax; }

                double finalVolumeInLots = _symbol.VolumeInUnitsToQuantity(normalizedVolumeInUnits);

                Print($"Время сделки ({serverTime}). Риск: {RiskPercent}%, Экьюти: {Account.Equity:F2} {Account.Asset.Name}, Сумма: {riskAmount:F2} {Account.Asset.Name}.");
                Print($"Расчетный объем: {finalVolumeInLots:F5} лот ({normalizedVolumeInUnits} юнитов).");

                // --- Расчет SL и TP цен ---
                double? stopLossPrice = null;
                double? takeProfitPrice = null;
                double entryPrice = (OrderTradeType == TradeType.Buy) ? _symbol.Ask : _symbol.Bid;
                stopLossPrice = (OrderTradeType == TradeType.Buy) ? entryPrice - StopLossInPips * _symbol.PipSize : entryPrice + StopLossInPips * _symbol.PipSize;
                double calculatedTakeProfitPips = StopLossInPips * RewardRatio;
                if(calculatedTakeProfitPips > 0) {
                    takeProfitPrice = (OrderTradeType == TradeType.Buy) ? entryPrice + calculatedTakeProfitPips * _symbol.PipSize : entryPrice - calculatedTakeProfitPips * _symbol.PipSize;
                }
                Print($"SL: {StopLossInPips} пп ({stopLossPrice:F5}), TP: {calculatedTakeProfitPips:F1} пп ({takeProfitPrice:F5}) (RR 1:{RewardRatio}).");
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

        protected override void OnStop()
        {
            Print("Бот остановлен.");
        }
    }
}