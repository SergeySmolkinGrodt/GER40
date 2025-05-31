using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Collections.Generic;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GER40Bot : Robot
    {
        [Parameter("Source")]
        public DataSeries Source { get; set; }

        // Параметры для Стоп Лосс на основе Ордер Блока
        // (потребуется доработка логики определения ОБ)
        [Parameter("Stop Loss ATR Period (OB)", DefaultValue = 14, Group = "Stop Loss", MinValue = 1, MaxValue = 100, Step = 1)]
        public int StopLossAtrPeriodOB { get; set; }
        [Parameter("Stop Loss ATR Multiplier (OB)", DefaultValue = 2.0, Group = "Stop Loss", MinValue = 0.1, MaxValue = 5.0, Step = 0.1)]
        public double StopLossAtrMultiplierOB { get; set; }


        // Параметры для Тейк Профита на основе Зон Ликвидности
        // (потребуется доработка логики определения зон ликвидности)
        [Parameter("Take Profit Risk/Reward Ratio 1", DefaultValue = 1.5, Group = "Take Profit", MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
        public double TakeProfitRR1 { get; set; }
        [Parameter("Take Profit Risk/Reward Ratio 2", DefaultValue = 3.0, Group = "Take Profit", MinValue = 1.0, MaxValue = 10.0, Step = 0.1)]
        public double TakeProfitRR2 { get; set; }
        [Parameter("Volume Percentage for TP1", DefaultValue = 50, MinValue = 1, MaxValue = 100, Step = 1, Group = "Take Profit")]
        public int VolumePercentageTP1 { get; set; }


        // Параметры для Входа от Ордер Блока
        // (потребуется доработка логики определения ОБ и конфлюенса)
        [Parameter("Lookback Period for OB (H1)", DefaultValue = 20, Group = "Entry - Order Block", MinValue = 5, MaxValue = 100, Step = 1)]
        public int LookbackPeriodOBH1 { get; set; }


        // Параметры для Определения Контекста (Структура Рынка H4/H1)
        [Parameter("Market Structure Lookback (H1)", DefaultValue = 50, Group = "Context - Market Structure", MinValue = 10, MaxValue = 200, Step = 1)]
        public int MarketStructureLookbackH1 { get; set; }
        [Parameter("Market Structure Lookback (H4)", DefaultValue = 50, Group = "Context - Market Structure", MinValue = 10, MaxValue = 200, Step = 1)]
        public int MarketStructureLookbackH4 { get; set; }
        [Parameter("SMC Swing Strength", DefaultValue = 3, Group = "Context - Market Structure", MinValue = 1, MaxValue = 10, Step = 1)]
        public int SmcSwingStrength { get; set; }

        // ---- ТЕЙК ПРОФИТ ПАРАМЕТРЫ ----
        [Parameter("Take Profit Strategy", DefaultValue = TakeProfitStrategyType.RiskRewardRatio, Group = "Take Profit")]
        public TakeProfitStrategyType TPStrategy { get; set; }

        // Параметры для Тейк Профита на основе Соотношения Риск/Прибыль (Уже есть TakeProfitRR1, TakeProfitRR2)

        // Параметры для Тейк Профита на основе ATR
        [Parameter("TP ATR Period", DefaultValue = 14, Group = "TP - ATR", MinValue = 1, MaxValue = 100, Step = 1)]
        public int TpAtrPeriod { get; set; }
        [Parameter("TP ATR Multiplier", DefaultValue = 3.0, Group = "TP - ATR", MinValue = 0.5, MaxValue = 10.0, Step = 0.1)]
        public double TpAtrMultiplier { get; set; }

        // Параметры для Тейк Профита на основе Полос Боллинджера
        [Parameter("TP Bollinger Bands Period", DefaultValue = 20, Group = "TP - Bollinger Bands", MinValue = 5, MaxValue = 100, Step = 1)]
        public int TpBollingerPeriod { get; set; }
        [Parameter("TP Bollinger Bands StdDev", DefaultValue = 2.0, Group = "TP - Bollinger Bands", MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
        public double TpBollingerStdDev { get; set; }
        [Parameter("TP Bollinger MA Type", DefaultValue = MovingAverageType.Simple, Group = "TP - Bollinger Bands")]
        public MovingAverageType TpBollingerMaType { get; set; }

        // Параметры для Тейк Профита на основе RSI
        [Parameter("TP RSI Period", DefaultValue = 14, Group = "TP - RSI", MinValue = 5, MaxValue = 50, Step = 1)]
        public int TpRsiPeriod { get; set; }
        [Parameter("TP RSI Overbought Level", DefaultValue = 70, Group = "TP - RSI", MinValue = 50, MaxValue = 90, Step = 1)]
        public double TpRsiOverbought { get; set; }
        [Parameter("TP RSI Oversold Level", DefaultValue = 30, Group = "TP - RSI", MinValue = 10, MaxValue = 50, Step = 1)]
        public double TpRsiOversold { get; set; }

        // Параметры для Тейк Профита на основе Пересечения MA
        [Parameter("TP Fast MA Period", DefaultValue = 10, Group = "TP - MA Crossover", MinValue = 1, MaxValue = 50, Step = 1)]
        public int TpFastMaPeriod { get; set; }
        [Parameter("TP Slow MA Period", DefaultValue = 20, Group = "TP - MA Crossover", MinValue = 5, MaxValue = 100, Step = 1)]
        public int TpSlowMaPeriod { get; set; }
        [Parameter("TP MA Type", DefaultValue = MovingAverageType.Simple, Group = "TP - MA Crossover")]
        public MovingAverageType TpMaType { get; set; }

        // TODO: Добавить параметры для уровней поддержки/сопротивления, экстремумов, каналов, паттернов, Фибоначчи, если они будут реализованы с настройками

        // ---- ПАРАМЕТРЫ ОПРЕДЕЛЕНИЯ КОНТЕКСТА ----
        [Parameter("Context Definition Strategy", DefaultValue = ContextStrategyType.MarketStructureSMC, Group = "Context Definition")]
        public ContextStrategyType ActiveContextStrategy { get; set; }

        // Параметры для MarketStructureSMC (уже есть MarketStructureLookbackH1, MarketStructureLookbackH4)

        // Параметры для EMA/SMA Context
        [Parameter("Context MA Period", DefaultValue = 50, Group = "Context - MA", MinValue = 5, MaxValue = 200, Step = 1)]
        public int ContextMaPeriod { get; set; }
        [Parameter("Context MA Type", DefaultValue = MovingAverageType.Simple, Group = "Context - MA")]
        public MovingAverageType ContextMaType { get; set; }

        // Параметры для Bollinger Bands Context
        [Parameter("Context BB Period", DefaultValue = 20, Group = "Context - Bollinger Bands", MinValue = 5, MaxValue = 100, Step = 1)]
        public int ContextBollingerPeriod { get; set; }
        [Parameter("Context BB StdDev", DefaultValue = 2.0, Group = "Context - Bollinger Bands", MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
        public double ContextBollingerStdDev { get; set; }
        [Parameter("Context BB MA Type", DefaultValue = MovingAverageType.Simple, Group = "Context - Bollinger Bands")]
        public MovingAverageType ContextBollingerMaType { get; set; }

        // Параметры для RSI Context
        [Parameter("Context RSI Period", DefaultValue = 14, Group = "Context - RSI", MinValue = 5, MaxValue = 50, Step = 1)]
        public int ContextRsiPeriod { get; set; }
        [Parameter("Context RSI Overbought", DefaultValue = 70, Group = "Context - RSI", MinValue = 50, MaxValue = 90, Step = 1)]
        public double ContextRsiOverbought { get; set; }
        [Parameter("Context RSI Oversold", DefaultValue = 30, Group = "Context - RSI", MinValue = 10, MaxValue = 50, Step = 1)]
        public double ContextRsiOversold { get; set; }
        
        // ---- ПАРАМЕТРЫ СТОП ЛОССА ----
        [Parameter("Stop Loss Strategy", DefaultValue = StopLossStrategyType.OrderBlockSMC, Group = "Stop Loss")]
        public StopLossStrategyType ActiveStopLossStrategy { get; set; }

        // Параметры для OrderBlockSMC (уже есть StopLossAtrPeriodOB, StopLossAtrMultiplierOB для возможного буфера)
        // Можно добавить параметр для точного определения "за" ОБ, например, % от высоты ОБ или фикс. пипсы

        // Параметры для ATR Stop Loss
        [Parameter("SL ATR Period", DefaultValue = 14, Group = "SL - ATR", MinValue = 1, MaxValue = 100, Step = 1)]
        public int SlAtrPeriod { get; set; }
        [Parameter("SL ATR Multiplier", DefaultValue = 2.0, Group = "SL - ATR", MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double SlAtrMultiplier { get; set; }

        // Параметры для Fixed Pips Stop Loss
        [Parameter("SL Fixed Pips", DefaultValue = 50, Group = "SL - Fixed Pips", MinValue = 5, MaxValue = 500, Step = 1)]
        public int SlFixedPips { get; set; }

        // Параметры для Bollinger Bands Stop Loss
        [Parameter("SL BB Period", DefaultValue = 20, Group = "SL - Bollinger Bands", MinValue = 5, MaxValue = 100, Step = 1)]
        public int SlBollingerPeriod { get; set; }
        [Parameter("SL BB StdDev", DefaultValue = 2.0, Group = "SL - Bollinger Bands", MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
        public double SlBollingerStdDev { get; set; }
        [Parameter("SL BB MA Type", DefaultValue = MovingAverageType.Simple, Group = "SL - Bollinger Bands")]
        public MovingAverageType SlBollingerMaType { get; set; }
        [Parameter("SL BB Offset Pips", DefaultValue = 5, Group = "SL - Bollinger Bands", MinValue = 0, MaxValue = 50, Step = 1)]
        public int SlBollingerOffsetPips {get; set; }

        // ---- ПАРАМЕТРЫ ТОЧЕК ВХОДА ----
        [Parameter("Entry Strategy", DefaultValue = EntryStrategyType.OrderBlockSMC, Group = "Entry Signal")]
        public EntryStrategyType ActiveEntryStrategy { get; set; }

        // Параметры для OrderBlockSMC (уже есть LookbackPeriodOBH1)

        // Параметры для FVG Entry (SMC)
        [Parameter("FVG Lookback Period (H1)", DefaultValue = 10, Group = "Entry - FVG", MinValue = 3, MaxValue = 50, Step = 1)]
        public int FvgLookbackH1 { get; set; }
        [Parameter("FVG Min Size Pips", DefaultValue = 5, Group = "Entry - FVG", MinValue = 1, MaxValue = 50, Step = 1)]
        public double FvgMinSizePips { get; set; } 

        // Параметры для MACD Crossover Entry
        [Parameter("Entry MACD Fast EMA", DefaultValue = 12, Group = "Entry - MACD", MinValue = 1, MaxValue = 50, Step = 1)]
        public int EntryMacdFastEma { get; set; }
        [Parameter("Entry MACD Slow EMA", DefaultValue = 26, Group = "Entry - MACD", MinValue = 5, MaxValue = 100, Step = 1)]
        public int EntryMacdSlowEma { get; set; }
        [Parameter("Entry MACD Signal Period", DefaultValue = 9, Group = "Entry - MACD", MinValue = 1, MaxValue = 50, Step = 1)]
        public int EntryMacdSignalPeriod { get; set; }

        // Параметры для LiquiditySweepPlusReactionSMC (LSPR)
        [Parameter("LSPR Reaction Pips", DefaultValue = 20.0, Group = "Entry - LSPR", MinValue = 1.0, MaxValue = 100.0, Step = 0.1)]
        public double LsprReactionPips { get; set; }
        [Parameter("LSPR Sweep Candle Lookback H1", DefaultValue = 1, Group = "Entry - LSPR", MinValue = 1, MaxValue = 5, Step = 1)]
        public int LsprSweepCandleLookbackH1 { get; set; } // Со скольких предыдущих H1 свечей ищем снятие
        [Parameter("LSPR Max Bars For Reaction H1", DefaultValue = 3, Group = "Entry - LSPR", MinValue = 1, MaxValue = 10, Step = 1)]
        public int LsprMaxBarsForReactionH1 { get; set; } // Сколько H1 свечей ждем реакцию

        // Параметры для BreakOfStructureM3SMC (BOSM3)
        [Parameter("BOSM3 Lookback Period", DefaultValue = 50, Group = "Entry - BOS M3", MinValue = 10, MaxValue = 200, Step = 1)]
        public int Bosm3LookbackPeriod { get; set; }
        [Parameter("BOSM3 Swing Strength", DefaultValue = 2, Group = "Entry - BOS M3", MinValue = 1, MaxValue = 5, Step = 1)]
        public int Bosm3SwingStrength { get; set; }
        // Можно добавить параметры для M3 ATR SL, если потребуется отдельный расчет
        // [Parameter("BOSM3 SL ATR Period", DefaultValue = 14, Group = "Entry - BOS M3", MinValue = 1, MaxValue = 50, Step = 1)]
        // public int Bosm3SlAtrPeriod { get; set; }
        // [Parameter("BOSM3 SL ATR Multiplier", DefaultValue = 2.0, Group = "Entry - BOS M3", MinValue = 0.1, MaxValue = 5.0, Step = 0.1)]
        // public double Bosm3SlAtrMultiplier { get; set; }

        // Параметры для Ichimoku Entry
        [Parameter("Ichimoku Tenkan-sen Period", DefaultValue = 9, Group = "Entry - Ichimoku", MinValue = 1, MaxValue = 50, Step = 1)]
        public int IchimokuTenkanPeriod { get; set; }
        [Parameter("Ichimoku Kijun-sen Period", DefaultValue = 26, Group = "Entry - Ichimoku", MinValue = 1, MaxValue = 100, Step = 1)]
        public int IchimokuKijunPeriod { get; set; }
        [Parameter("Ichimoku Senkou Span B Period", DefaultValue = 52, Group = "Entry - Ichimoku", MinValue = 1, MaxValue = 200, Step = 1)]
        public int IchimokuSenkouSpanBPeriod { get; set; }

        // Параметры для ADX Фильтра
        [Parameter("Enable ADX Filter", DefaultValue = false, Group = "Entry Filter - ADX")]
        public bool EnableADXFilter { get; set; }
        [Parameter("ADX Period", DefaultValue = 14, Group = "Entry Filter - ADX", MinValue = 1, MaxValue = 100, Step = 1)]
        public int ADXFilterPeriod { get; set; }
        [Parameter("ADX Threshold", DefaultValue = 25, Group = "Entry Filter - ADX", MinValue = 10, MaxValue = 50, Step = 1)]
        public double ADXFilterThreshold { get; set; }

        // Параметры для LiquiditySweepSMC (LS)
        [Parameter("LS Sweep Candle Lookback H1", DefaultValue = 1, Group = "Entry - LS", MinValue = 1, MaxValue = 5, Step = 1)]
        public int LsSweepCandleLookbackH1 { get; set; } // Со скольких предыдущих H1 свечей ищем снятие
        // [Parameter("LS Min Retracement Pips", DefaultValue = 5, Group = "Entry - LS", MinValue = 0, MaxValue = 50, Step = 1)]
        // public int LsMinRetracementPips { get; set; } // Минимальный откат после свипа перед входом

        private MarketSeries _h1Series;
        private MarketSeries _h4Series;
        private MarketSeries _m3Series; // Для BreakOfStructureM3SMC
        // Поля для хранения состояния и индикаторов, если нужны

        protected override void OnStart()
        {
            // Получение исторических данных для других таймфреймов
            _h1Series = MarketData.GetSeries(TimeFrame.Hour);
            _h4Series = MarketData.GetSeries(TimeFrame.Hour4);
            _m3Series = MarketData.GetSeries(TimeFrame.Minute3);

            Print("GER40Bot started.");
            Print("Symbol: {0}", Symbol.Name);
            Print("TimeFrame: {0}", TimeFrame);
            Print("Initial Stop Loss ATR Period (OB): {0}", StopLossAtrPeriodOB);
            Print("Initial Stop Loss ATR Multiplier (OB): {0}", StopLossAtrMultiplierOB);
            Print("Initial Take Profit Risk/Reward Ratio 1: {0}", TakeProfitRR1);
            Print("Initial Take Profit Risk/Reward Ratio 2: {0}", TakeProfitRR2);
            Print("Initial Volume Percentage for TP1: {0}%", VolumePercentageTP1);
            Print("Initial Lookback Period for OB (H1): {0}", LookbackPeriodOBH1);
            Print("Initial Market Structure Lookback (H1): {0}", MarketStructureLookbackH1);
            Print("Initial Market Structure Lookback (H4): {0}", MarketStructureLookbackH4);
        }

        protected override void OnTick()
        {
            // Логика, выполняемая на каждом тике
            var marketContext = DefineMarketContext();
            
            // Если контекст не определен (например, недостаточно данных), не продолжаем
            if (marketContext == MarketContext.Neutral)
            {
                // Print("Market context is Neutral or Undefined. Skipping further processing.");
                // В зависимости от стратегии, можно либо ничего не делать, либо искать контртрендовые входы.
                // Пока что пропускаем.
                return; 
            }

            ManageTrades(marketContext);
            LookForEntry(marketContext);
        }

        private MarketContext DefineMarketContext()
        {
            switch (ActiveContextStrategy)
            {
                case ContextStrategyType.MarketStructureSMC:
                    bool isH4Bullish = IsBullishStructure(_h4Series, MarketStructureLookbackH4, "H4");
                    bool isH1Bullish = IsBullishStructure(_h1Series, MarketStructureLookbackH1, "H1");
                    bool isH4Bearish = IsBearishStructure(_h4Series, MarketStructureLookbackH4, "H4");
                    bool isH1Bearish = IsBearishStructure(_h1Series, MarketStructureLookbackH1, "H1");

                    // Логика определения общего контекста на основе H1 и H4
                    // Пример: Сильный бычий, если H4 и H1 бычьи. Слабый, если один из них.
                    // Для начала простая логика: если оба ТФ согласны, то это контекст.
                    if (isH4Bullish && isH1Bullish)
                    {
                        Print("SMC Context: H4 Bullish, H1 Bullish -> Bullish");
                        return MarketContext.Bullish;
                    }
                    if (isH4Bearish && isH1Bearish)
                    {
                        Print("SMC Context: H4 Bearish, H1 Bearish -> Bearish");
                        return MarketContext.Bearish;
                    }
                    // Можно добавить логику для случаев, когда H4 и H1 противоречат друг другу (например, Ranging или Neutral)
                    Print("SMC Context: H4/H1 structure inconclusive or conflicting. Context: Neutral.");
                    return MarketContext.Neutral; 

                case ContextStrategyType.MovingAverage:
                    MovingAverage ma;
                    // Используем основной таймфрейм робота для MA контекста
                    if (ContextMaType == MovingAverageType.Simple)
                        ma = Indicators.SimpleMovingAverage(Bars.ClosePrices, ContextMaPeriod);
                    else
                        ma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, ContextMaPeriod);

                    if (Bars.ClosePrices.Last(1) > ma.Result.Last(1) && Bars.ClosePrices.Last(0) > ma.Result.Last(0))
                    {
                        Print("MA Context: Price above MA ({0} {1}) -> Bullish", ContextMaPeriod, ContextMaType);
                        return MarketContext.Bullish;
                    }
                    if (Bars.ClosePrices.Last(1) < ma.Result.Last(1) && Bars.ClosePrices.Last(0) < ma.Result.Last(0))
                    {
                        Print("MA Context: Price below MA ({0} {1}) -> Bearish", ContextMaPeriod, ContextMaType);
                        return MarketContext.Bearish;
                    }
                    Print("MA Context: Price is around MA ({0} {1}). Context: Neutral.", ContextMaPeriod, ContextMaType);
                    return MarketContext.Neutral;

                case ContextStrategyType.BollingerBands:
                    BollingerBands bb = Indicators.BollingerBands(Bars, ContextBollingerPeriod, ContextBollingerStdDev, ContextBollingerMaType);
                    double lastClose = Bars.ClosePrices.Last(0);
                    double middleBand = bb.Middle.Last(0);
                    //double upperBand = bb.Top.Last(0);
                    //double lowerBand = bb.Bottom.Last(0);

                    // Упрощенная логика: выше средней линии - бычий, ниже - медвежий
                    // Можно добавить анализ наклона средней линии или ширины канала для определения флэта.
                    if (lastClose > middleBand) // Можно добавить && Bars.ClosePrices.Last(1) > bb.Middle.Last(1) для подтверждения
                    {
                        Print("BB Context: Price {0} > Middle Band {1} -> Bullish", lastClose, middleBand);
                        return MarketContext.Bullish;
                    }
                    else if (lastClose < middleBand) // Можно добавить && Bars.ClosePrices.Last(1) < bb.Middle.Last(1)
                    {
                        Print("BB Context: Price {0} < Middle Band {1} -> Bearish", lastClose, middleBand);
                        return MarketContext.Bearish;
                    }
                    else
                    {
                        Print("BB Context: Price {0} is near Middle Band {1}. Context: Neutral.", lastClose, middleBand);
                        return MarketContext.Neutral;
                    }

                case ContextStrategyType.RSI:
                    RelativeStrengthIndex rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, ContextRsiPeriod);
                    double rsiValue = rsi.Result.Last(0);

                    if (rsiValue > ContextRsiOverbought)
                    {
                        Print("RSI Context: RSI ({0:F2}) > Overbought ({1}) -> Bullish", rsiValue, ContextRsiOverbought);
                        return MarketContext.Bullish;
                    }
                    else if (rsiValue < ContextRsiOversold)
                    {
                        Print("RSI Context: RSI ({0:F2}) < Oversold ({1}) -> Bearish", rsiValue, ContextRsiOversold);
                        return MarketContext.Bearish;
                    }
                    else
                    {
                        Print("RSI Context: RSI ({0:F2}) is between {1} and {2}. Context: Neutral.", rsiValue, ContextRsiOversold, ContextRsiOverbought);
                        return MarketContext.Neutral;
                    }

                default:
                    Print("Unknown or not implemented Context Definition Strategy: {0}", ActiveContextStrategy);
                    return MarketContext.Neutral;
            }
        }

        private void LookForEntry(MarketContext context)
        {
            if (Positions.Count > 0) 
            {
                return;
            }

            OrderBlockInfo? ob = null;
            double entryPrice = 0;
            TradeType tradeType = TradeType.Buy; // Default, will be overridden
            double stopLossPips = 0;
            double takeProfitPips1 = 0;
            double volumeInUnits = 0;
            string signalLabelPrefix = "GER40";

            switch (ActiveEntryStrategy)
            {
                case EntryStrategyType.OrderBlockSMC:
                    if (context == MarketContext.Bullish)
                    {
                        ob = FindBullishOrderBlock(_h1Series, LookbackPeriodOBH1, "H1");
                        if (ob.HasValue && ob.Value.IsMitigated) ob = null;
                        else if (ob.HasValue) Print("LookForEntry (OB SMC): Found UNMITIGATED Bullish OB on H1. {0}", ob.Value.ToString());
                    }
                    else if (context == MarketContext.Bearish)
                    {
                        ob = FindBearishOrderBlock(_h1Series, LookbackPeriodOBH1, "H1");
                        if (ob.HasValue && ob.Value.IsMitigated) ob = null;
                        else if (ob.HasValue) Print("LookForEntry (OB SMC): Found UNMITIGATED Bearish OB on H1. {0}", ob.Value.ToString());
                    }

                    if (!ob.HasValue) return;

                    tradeType = ob.Value.IsBullish ? TradeType.Buy : TradeType.Sell;
                    signalLabelPrefix = "OB_SMC";

                    // ADX Filter for OrderBlockSMC
                    if (!IsADXTrendStrongEnough(Bars, tradeType)) // ADX on main timeframe
                    {
                        Print("LookForEntry (OB SMC): ADX Filter FAILED for {0}. Skipping order.", tradeType);
                        return;
                    }
                    Print("LookForEntry (OB SMC): ADX Filter PASSED for {0}.\", tradeType);

                    double currentPrice = Bars.ClosePrices.Last(0);
                    double obEntryLevel = ob.Value.IsBullish ? ob.Value.High : ob.Value.Low;

                    if ((ob.Value.IsBullish && currentPrice < obEntryLevel - Symbol.PipSize * 2) ||
                        (!ob.Value.IsBullish && currentPrice > obEntryLevel + Symbol.PipSize * 2))
                    {
                        Print("LookForEntry (OB SMC): Price not at OB entry level. Consider pending. Price:{0}, Level:{1}", currentPrice, obEntryLevel);
                        return; // Пока не ставим лимитки
                    }
                    entryPrice = obEntryLevel;
                    
                    stopLossPips = CalculateStopLossPips(tradeType, entryPrice, ob, null, _h1Series);
                    takeProfitPips1 = CalculateTakeProfitPips(tradeType, entryPrice, stopLossPips, ob, _h1Series);
                    volumeInUnits = CalculateVolume(stopLossPips);
                    break;
                
                case EntryStrategyType.FairValueGapSMC:
                    Print("EntryStrategyType.FairValueGapSMC - NOT IMPLEMENTED");
                    return;

                case EntryStrategyType.BreakOfStructureSMC:
                    {
                        SwingPoint? bosSignalH1 = null;
                        if (context == MarketContext.Bullish) bosSignalH1 = FindLastBreakOfStructure(_h1Series, MarketStructureLookbackH1, SmcSwingStrength, true, "H1 BOS");
                        else if (context == MarketContext.Bearish) bosSignalH1 = FindLastBreakOfStructure(_h1Series, MarketStructureLookbackH1, SmcSwingStrength, false, "H1 BOS");
                        
                        if (bosSignalH1.HasValue) Print("LookForEntry (BOS H1): {0} BOS identified on H1. {1}", context, bosSignalH1.Value.ToString());
                        // TODO: Implement trading logic for H1 BOS
                        return; 
                    }

                case EntryStrategyType.ChangeOfCharacterSMC:
                    {
                        SwingPoint? chochSignalH1 = null;
                        if (context == MarketContext.Bearish) chochSignalH1 = FindLastChangeOfCharacter(_h1Series, MarketStructureLookbackH1, SmcSwingStrength, false, "H1 Bullish CHoCH");
                        else if (context == MarketContext.Bullish) chochSignalH1 = FindLastChangeOfCharacter(_h1Series, MarketStructureLookbackH1, SmcSwingStrength, true, "H1 Bearish CHoCH");

                        if (chochSignalH1.HasValue) Print("LookForEntry (CHoCH H1): Potential {0} CHoCH on H1. {1}", context, chochSignalH1.Value.ToString());
                        // TODO: Implement trading logic for H1 CHoCH
                        return;
                    }

                case EntryStrategyType.MACDCrossover:
                    Print("EntryStrategyType.MACDCrossover - NOT IMPLEMENTED");
                    return;

                case EntryStrategyType.LiquiditySweepPlusReactionSMC:
                    {
                        TradeSignalInfo? lsprSignal = FindLiquiditySweepPlusReaction(_h1Series, LsprSweepCandleLookbackH1, LsprReactionPips, LsprMaxBarsForReactionH1, "H1 LSPR");
                        if (!lsprSignal.HasValue) return;

                        Print("LookForEntry (LSPR): Signal found - {0}\", lsprSignal.Value.SignalType);
                        tradeType = lsprSignal.Value.SignalType;
                        entryPrice = lsprSignal.Value.EntryPrice;
                        signalLabelPrefix = "LSPR";

                        // ADX Filter for LSPR
                        if (!IsADXTrendStrongEnough(_h1Series, tradeType)) // ADX on H1 for LSPR
                        {
                            Print("LookForEntry (LSPR): ADX Filter FAILED for {0}. Skipping order.\", tradeType);
                            return;
                        }
                        Print("LookForEntry (LSPR): ADX Filter PASSED for {0}.\", tradeType);
                        
                        SwingPoint? pseudoBrokenSwingLSPR = new SwingPoint { Price = lsprSignal.Value.ProtectiveStopLevel, IsHigh = (tradeType == TradeType.Sell), Index = lsprSignal.Value.EventBarIndex };
                        stopLossPips = CalculateStopLossPips(tradeType, entryPrice, null, pseudoBrokenSwingLSPR, _h1Series);
                        takeProfitPips1 = CalculateTakeProfitPips(tradeType, entryPrice, stopLossPips, null, _h1Series);
                        volumeInUnits = CalculateVolume(stopLossPips);
                    }
                    break;

                case EntryStrategyType.BreakOfStructureM3SMC:
                    {
                        if (context == MarketContext.Neutral) return;

                        bool lookingForBullishBosM3 = (context == MarketContext.Bullish);
                        SwingPoint? bosM3Signal = FindLastBreakOfStructure(_m3Series, Bosm3LookbackPeriod, Bosm3SwingStrength, lookingForBullishBosM3, "M3 BOS");
                        
                        if (!bosM3Signal.HasValue) return;

                        Print("LookForEntry (BOS M3): Signal found! Context: {0}, M3 BOS: {1}\", context, bosM3Signal.Value.ToString());
                        tradeType = lookingForBullishBosM3 ? TradeType.Buy : TradeType.Sell;
                        entryPrice = Bars.ClosePrices.Last(0);
                        signalLabelPrefix = "BOSM3";

                        // ADX Filter for BOS M3
                        if (!IsADXTrendStrongEnough(_m3Series, tradeType)) // ADX on M3 for BOS M3
                        {
                            Print("LookForEntry (BOS M3): ADX Filter FAILED for {0}. Skipping order.\", tradeType);
                            return;
                        }
                        Print("LookForEntry (BOS M3): ADX Filter PASSED for {0}.\", tradeType);

                        stopLossPips = CalculateStopLossPips(tradeType, entryPrice, null, bosM3Signal, _m3Series);
                        takeProfitPips1 = CalculateTakeProfitPips(tradeType, entryPrice, stopLossPips, null, _m3Series);
                        volumeInUnits = CalculateVolume(stopLossPips);
                    }
                    break;

                case EntryStrategyType.IchimokuSignal:
                    {
                        IchimokuKinkoHyo ichimoku = Indicators.IchimokuKinkoHyo(IchimokuTenkanPeriod, IchimokuKijunPeriod, IchimokuSenkouSpanBPeriod);
                        double tenkanSen = ichimoku.TenkanSen.Last(1), kijunSen = ichimoku.KijunSen.Last(1);
                        double spanA = ichimoku.SenkouSpanA.Last(1), spanB = ichimoku.SenkouSpanB.Last(1);
                        double closePrice = Bars.ClosePrices.Last(1), prevTenkan = ichimoku.TenkanSen.Last(2), prevKijun = ichimoku.KijunSen.Last(2);

                        bool isBullishSignal = false, isBearishSignal = false;
                        bool priceAboveCloud = closePrice > Math.Max(spanA, spanB), priceBelowCloud = closePrice < Math.Min(spanA, spanB);
                        bool tenkanCrossedKijunUp = tenkanSen > kijunSen && prevTenkan <= prevKijun, tenkanCrossedKijunDown = tenkanSen < kijunSen && prevTenkan >= prevKijun;
                        bool priceAboveLines = closePrice > tenkanSen && closePrice > kijunSen, priceBelowLines = closePrice < tenkanSen && closePrice < kijunSen;

                        if ((priceAboveCloud || (tenkanCrossedKijunUp && priceAboveLines)) && (context == MarketContext.Bullish || context == MarketContext.Neutral)) isBullishSignal = true;
                        if ((priceBelowCloud || (tenkanCrossedKijunDown && priceBelowLines)) && (context == MarketContext.Bearish || context == MarketContext.Neutral)) isBearishSignal = true;

                        if (!isBullishSignal && !isBearishSignal) return;

                        tradeType = isBullishSignal ? TradeType.Buy : TradeType.Sell;
                        Print("LookForEntry (Ichimoku): {0} Signal. PriceAboveCloud:{1}, TenkanCrossUp:{2}, PriceBelowCloud:{3}, TenkanCrossDown:{4}\", tradeType, priceAboveCloud, tenkanCrossedKijunUp, priceBelowCloud, tenkanCrossedKijunDown);
                        entryPrice = Bars.ClosePrices.Last(0);
                        signalLabelPrefix = "Ichimoku";

                        // ADX Filter for Ichimoku
                        if (!IsADXTrendStrongEnough(Bars, tradeType)) // ADX on main timeframe
                        {
                            Print("LookForEntry (Ichimoku): ADX Filter FAILED for {0}. Skipping order.\", tradeType);
                            return;
                        }
                        Print("LookForEntry (Ichimoku): ADX Filter PASSED for {0}.\", tradeType);
                        
                        SwingPoint? slReferencePoint = new SwingPoint { Price = kijunSen, IsHigh = (tradeType == TradeType.Sell), Index = Bars.ClosePrices.Count - 1 };
                        stopLossPips = CalculateStopLossPips(tradeType, entryPrice, null, slReferencePoint, Bars);
                        takeProfitPips1 = CalculateTakeProfitPips(tradeType, entryPrice, stopLossPips, null, Bars);
                        volumeInUnits = CalculateVolume(stopLossPips);
                    }
                    break;

                case EntryStrategyType.LiquiditySweepSMC:
                    {
                        LiquiditySweepEventInfo? lsEvent = FindLiquiditySweepEvent(_h1Series, LsSweepCandleLookbackH1, "H1 LS");
                        if (!lsEvent.HasValue) return;

                        tradeType = lsEvent.Value.AnticipatedSignal;
                        Print("LookForEntry (LS SMC): Liquidity Sweep Event found. Anticipating {0} based on sweep of H1@{1}", tradeType, lsEvent.Value.SweptCandleTime.ToString("HH:mm dd.MM"));

                        // Проверка контекста: Входим, если контекст соответствует ожидаемому развороту или нейтрален
                        bool contextMatches = false;
                        if (tradeType == TradeType.Buy && (context == MarketContext.Bullish || context == MarketContext.Neutral))
                            contextMatches = true;
                        else if (tradeType == TradeType.Sell && (context == MarketContext.Bearish || context == MarketContext.Neutral))
                            contextMatches = true;

                        if (!contextMatches)
                        {
                            Print("LookForEntry (LS SMC): Sweep event found for {0}, but market context {1} does not align. Skipping.", tradeType, context);
                            return;
                        }
                        Print("LookForEntry (LS SMC): Context {0} aligns with anticipated {1} signal.", context, tradeType);

                        entryPrice = Bars.ClosePrices.Last(0); // Вход по текущей цене основного ТФ робота
                        signalLabelPrefix = "LS_SMC";

                        // ADX Filter
                        if (!IsADXTrendStrongEnough(_h1Series, tradeType)) // ADX на H1, т.к. сигнал с H1
                        {
                            Print("LookForEntry (LS SMC): ADX Filter FAILED for {0}. Skipping order.", tradeType);
                            return;
                        }
                        Print("LookForEntry (LS SMC): ADX Filter PASSED for {0}.", tradeType);

                        // SL за экстремум свечи, совершившей свип
                        SwingPoint sweepPointForSL = new SwingPoint 
                        {
                            Price = lsEvent.Value.SweepExtremePrice, 
                            IsHigh = (tradeType == TradeType.Sell), // Если ждем Sell, SL за High свипа
                            Index = lsEvent.Value.SweepCandleIndex, 
                            Time = lsEvent.Value.SweepOccurredTime 
                        };

                        stopLossPips = CalculateStopLossPips(tradeType, entryPrice, null, sweepPointForSL, _h1Series);
                        takeProfitPips1 = CalculateTakeProfitPips(tradeType, entryPrice, stopLossPips, null, _h1Series);
                        volumeInUnits = CalculateVolume(stopLossPips);
                    }
                    break;

                default:
                    Print("Unknown or not implemented Entry Strategy: {0}\", ActiveEntryStrategy);
                    return;
            }

            // --- Common Order Execution (if a signal was processed and not returned early) ---
            if (volumeInUnits > 0) // Indicates a valid signal was processed and calculations were made
            {
                Print("Attempting to execute order: Strategy={0}, Type={1}, Entry={2:F5}, SL Pips={3:F1}, TP1 Pips={4:F1}, Volume={5}\", 
                    ActiveEntryStrategy, tradeType, entryPrice, stopLossPips, takeProfitPips1, volumeInUnits);
                
                string label = string.Format("GER40_{0}_{1}_{2}\", signalLabelPrefix, tradeType == TradeType.Buy ? "L" : "S", DateTime.UtcNow.Ticks);
                var result = ExecuteMarketOrder(tradeType, Symbol.Name, volumeInUnits, label, stopLossPips, takeProfitPips1);

                if (result.IsSuccessful)
                {
                    Print("Order executed successfully. Position ID: {0}\", result.Position.Id);
                }
                else
                {
                    Print("Order execution failed: {0}\", result.Error);
                }
            }
            // else if (ActiveEntryStrategy != EntryStrategyType.FairValueGapSMC && ActiveEntryStrategy != EntryStrategyType.MACDCrossover && ActiveEntryStrategy != EntryStrategyType.BreakOfStructureSMC && ActiveEntryStrategy != EntryStrategyType.ChangeOfCharacterSMC) // Don't print for not implemented
            // {
            // Print("LookForEntry: No valid entry signal or volume for {0} strategy in {1} context after all checks.\", ActiveEntryStrategy, context);
            // }
        }

        private void ManageTrades(MarketContext context)
        {
            // TODO: Реализовать логику управления открытыми позициями
            // - Управление частичным закрытием по TP1
            // - Перевод СЛ в безубыток
            // - Возможно, трейлинг стоп для TP2
             foreach (var position in Positions)
            {
                if (position.SymbolName == Symbol.Name && position.Label.StartsWith("GER40_OB"))
                {
                    // Пример управления частичным тейк-профитом
                    // Эту логику нужно будет доработать для работы с несколькими целями
                    if (position.GrossProfit > 0) // Упрощенное условие, нужно конкретнее
                    {
                         // Логика для частичного закрытия и переноса СЛ в БУ
                         // Например, если TP1 достигнут для части объема
                         // if (!IsTP1Taken(position) && IsTP1Reached(position))
                         // {
                         //    ClosePartialPosition(position, VolumePercentageTP1);
                         //    ModifyPositionStopLossToBreakEven(position);
                         //    MarkTP1AsTaken(position); // Нужно будет хранить состояние
                         //    Print("Position {0}: TP1 taken, SL moved to Breakeven.", position.Id);
                         // }
                    }
                }
            }
            Print("ManageTrades: Trade management logic not yet implemented.");
        }

        // Пример функции для расчета объема позиции (требует доработки)
        private double CalculateVolume(double stopLossPips)
        {
            // Пример: риск 2% от баланса
            double riskPerTrade = Account.Balance * 0.02; 
            double pipValue = Symbol.PipValue; // Для GER40 может быть TickValue
            if (Symbol.Name.ToUpper().Contains("GER") || Symbol.Name.ToUpper().Contains("DAX")) // Для индексов типа DAX/GER40
            {
                 pipValue = Symbol.TickValue * Symbol.LotSize / Symbol.TickSize; // Это более корректно для индексов
            }

            if (pipValue == 0 || stopLossPips == 0) return Symbol.VolumeInUnitsMin;

            double volumeInUnits = riskPerTrade / (stopLossPips * pipValue);
            
            // Округляем до минимального шага объема и проверяем мин/макс ограничения
            volumeInUnits = Math.Round(volumeInUnits / Symbol.VolumeInUnitsStep) * Symbol.VolumeInUnitsStep;
            volumeInUnits = Math.Max(Symbol.VolumeInUnitsMin, volumeInUnits);
            volumeInUnits = Math.Min(Symbol.VolumeInUnitsMax, volumeInUnits);
            
            return volumeInUnits;
        }

        private double CalculateStopLossPips(TradeType tradeType, double entryPrice, OrderBlockInfo? obInfo = null, SwingPoint? brokenSwing = null, MarketSeries atrSeries = null)
        {
            double stopLossPips = 0;
            atrSeries = atrSeries ?? _h1Series; // По умолчанию H1 для ATR, если не указано иное

            switch (ActiveStopLossStrategy)
            {
                case StopLossStrategyType.OrderBlockSMC:
                    if (!obInfo.HasValue)
                    {
                        Print("CalculateStopLossPips (OB SMC): OrderBlockInfo is null. Using default 100 pips SL.");
                        return 100;
                    }
                    double obStopLevel = tradeType == TradeType.Buy ? obInfo.Value.Low : obInfo.Value.High;
                    stopLossPips = Math.Abs(entryPrice - obStopLevel) / Symbol.PipSize;
                    
                    // Добавляем буфер на основе ATR, если задан период
                    if (StopLossAtrPeriodOB > 0 && atrSeries.Close.Count > StopLossAtrPeriodOB)
                    {
                        AverageTrueRange atr = Indicators.AverageTrueRange(atrSeries, StopLossAtrPeriodOB, MovingAverageType.Simple); // Можно сделать MA Type параметром
                        double atrValuePips = atr.Result.Last(1) / Symbol.PipSize; // Берем предпоследнее значение
                        stopLossPips += atrValuePips * StopLossAtrMultiplierOB;
                        Print("SL (OB SMC): Base SL={0:F1} pips. ATR Buffer ({1} * {2:F1}) = {3:F1} pips. Total SL = {4:F1} pips", 
                            Math.Abs(entryPrice - obStopLevel) / Symbol.PipSize, StopLossAtrMultiplierOB, atrValuePips, atrValuePips * StopLossAtrMultiplierOB, stopLossPips);
                    }
                    else
                    {
                         stopLossPips += 2; // Небольшой фиксированный буфер, если ATR не используется
                         Print("SL (OB SMC): Base SL={0:F1} pips. Fixed Buffer = 2 pips. Total SL = {1:F1} pips", Math.Abs(entryPrice - obStopLevel) / Symbol.PipSize, stopLossPips);
                    }
                    break;

                case StopLossStrategyType.ATR:
                    if (SlAtrPeriod > 0 && atrSeries.Close.Count > SlAtrPeriod)
                    {
                        AverageTrueRange atr = Indicators.AverageTrueRange(atrSeries, SlAtrPeriod, MovingAverageType.Simple); // Можно сделать MA Type параметром
                        double atrValuePips = atr.Result.Last(1) / Symbol.PipSize;
                        stopLossPips = atrValuePips * SlAtrMultiplier;
                        Print("SL (ATR): ATR({0})={1:F1} pips * Multiplier({2}) = {3:F1} pips", SlAtrPeriod, atrValuePips, SlAtrMultiplier, stopLossPips);
                    }
                    else
                    {
                        Print("CalculateStopLossPips (ATR): SlAtrPeriod is 0 or not enough data. Using default 100 pips SL.");
                        return 100;
                    }
                    break;

                case StopLossStrategyType.FixedPips:
                    stopLossPips = SlFixedPips;
                    Print("SL (FixedPips): {0} pips", stopLossPips);
                    break;

                case StopLossStrategyType.BollingerBands:
                    if (SlBollingerPeriod > 0 && Bars.Close.Count > SlBollingerPeriod) // Используем основной ТФ робота для BB SL
                    {
                        BollingerBands bb = Indicators.BollingerBands(Bars, SlBollingerPeriod, SlBollingerStdDev, SlBollingerMaType);
                        double bandLevel = tradeType == TradeType.Buy ? bb.Bottom.Last(1) : bb.Top.Last(1);
                        double distanceToBand = Math.Abs(entryPrice - bandLevel) / Symbol.PipSize;
                        stopLossPips = distanceToBand + SlBollingerOffsetPips;
                        Print("SL (BollingerBands): Distance to Band ({0:F2})={1:F1} pips + Offset({2}) = {3:F1} pips", 
                            bandLevel, distanceToBand, SlBollingerOffsetPips, stopLossPips);
                    }
                    else
                    {
                        Print("CalculateStopLossPips (BollingerBands): SlBollingerPeriod is 0 or not enough data. Using default 100 pips SL.");
                        return 100;
                    }
                    break;
                
                case StopLossStrategyType.SwingHighLow:
                    if (brokenSwing.HasValue)
                    {
                        double swingLevel = brokenSwing.Value.Price;
                        stopLossPips = Math.Abs(entryPrice - swingLevel) / Symbol.PipSize;
                        Print("SL (SwingHighLow): Based on provided swing at {0:F5}. Distance = {1:F1} pips.", swingLevel, stopLossPips);
                        // Можно добавить буфер ATR к этому, как для OB
                        if (StopLossAtrPeriodOB > 0 && atrSeries != null && atrSeries.Close.Count > StopLossAtrPeriodOB) // Используем параметры от OB для буфера
                        {
                            AverageTrueRange atr = Indicators.AverageTrueRange(atrSeries, StopLossAtrPeriodOB, MovingAverageType.Simple);
                            double atrValuePips = atr.Result.Last(1) / Symbol.PipSize;
                            double buffer = atrValuePips * StopLossAtrMultiplierOB;
                            stopLossPips += buffer;
                            Print("SL (SwingHighLow): Added ATR Buffer ({0:F1} * {1:F1}) = {2:F1} pips. Total SL = {3:F1} pips", 
                                StopLossAtrMultiplierOB, atrValuePips, buffer, stopLossPips);
                        }
                        else
                        {
                            stopLossPips += 2; // Небольшой фиксированный буфер
                            Print("SL (SwingHighLow): Added Fixed Buffer 2 pips. Total SL = {0:F1} pips", stopLossPips);
                        }
                    }
                    else
                    {
                        Print("CalculateStopLossPips (SwingHighLow): brokenSwing is null. Using default 100 pips SL.");
                        return 100;
                    }
                    break;

                default:
                    Print("CalculateStopLossPips: Unknown or not implemented Stop Loss Strategy: {0}. Using default 100 pips SL.", ActiveStopLossStrategy);
                    return 100; // Заглушка
            }

            // Минимальный SL
            double minSlPips = Symbol.PipSize * 5 / Symbol.PipSize; // 5 пипс в единицах пипсов
             if (Symbol.Name.ToUpper().Contains("GER") || Symbol.Name.ToUpper().Contains("DAX")) minSlPips = 5; // Для индексов просто 5 пунктов
            else minSlPips = 5 * Symbol.PipSize / Symbol.TickSize * Symbol.TickSize / Symbol.PipSize; // Для Forex более точный расчет 5 пипс
            
            if (stopLossPips < minSlPips)            
            {
                Print("Calculated SL ({0:F1} pips) is too small. Setting to min ({1:F1} pips).", stopLossPips, minSlPips);
                stopLossPips = minSlPips;
            }
            return Math.Round(stopLossPips, 1);
        }

        private double CalculateTakeProfitPips(TradeType tradeType, double entryPrice, double calculatedStopLossPips, OrderBlockInfo? obInfo = null, MarketSeries targetSeries = null)
        {
            double takeProfitPips = 0;
            targetSeries = targetSeries ?? _h1Series; // По умолчанию H1 для индикаторов ТП, если не указано иное

            switch (TPStrategy) // Используем параметр TPStrategy
            {
                case TakeProfitStrategyType.RiskRewardRatio:
                    // Для TP1 используем TakeProfitRR1, для TP2 будет другая логика или параметр
                    takeProfitPips = calculatedStopLossPips * TakeProfitRR1;
                    Print("TP (RiskRewardRatio): SL Pips ({0:F1}) * RR1 ({1}) = {2:F1} pips", calculatedStopLossPips, TakeProfitRR1, takeProfitPips);
                    break;

                case TakeProfitStrategyType.ATR:
                    if (TpAtrPeriod > 0 && targetSeries.Close.Count > TpAtrPeriod)
                    {
                        AverageTrueRange atr = Indicators.AverageTrueRange(targetSeries, TpAtrPeriod, MovingAverageType.Simple);
                        double atrValuePips = atr.Result.Last(1) / Symbol.PipSize;
                        takeProfitPips = atrValuePips * TpAtrMultiplier;
                        Print("TP (ATR): ATR({0})={1:F1} pips * Multiplier({2}) = {3:F1} pips", TpAtrPeriod, atrValuePips, TpAtrMultiplier, takeProfitPips);
                    }
                    else
                    {
                        Print("CalculateTakeProfitPips (ATR): TpAtrPeriod is 0 or not enough data. Using default 100 pips TP.");
                        return 100;
                    }
                    break;

                case TakeProfitStrategyType.BollingerBands:
                    if (TpBollingerPeriod > 0 && targetSeries.Close.Count > TpBollingerPeriod)
                    {
                        BollingerBands bb = Indicators.BollingerBands(targetSeries, TpBollingerPeriod, TpBollingerStdDev, TpBollingerMaType);
                        double bandLevel = tradeType == TradeType.Buy ? bb.Top.Last(1) : bb.Bottom.Last(1);
                        // Для ТП по BB, мы хотим, чтобы цена достигла противоположной ленты
                        takeProfitPips = Math.Abs(bandLevel - entryPrice) / Symbol.PipSize;
                        Print("TP (BollingerBands): Distance from Entry ({0:F2}) to Opposite Band ({1:F2}) = {2:F1} pips", 
                            entryPrice, bandLevel, takeProfitPips);
                    }
                    else
                    {
                        Print("CalculateTakeProfitPips (BollingerBands): TpBollingerPeriod is 0 or not enough data. Using default 100 pips TP.");
                        return 100;
                    }
                    break;
                
                // --- Заглушки для более сложных стратегий ТП ---
                case TakeProfitStrategyType.SupportResistance:
                case TakeProfitStrategyType.PreviousExtremes:
                case TakeProfitStrategyType.ChannelBoundaries:
                case TakeProfitStrategyType.ChartPatterns:
                case TakeProfitStrategyType.FibonacciExtensions:
                case TakeProfitStrategyType.FibonacciRetracement:
                    Print("TP Strategy '{0}' - NOT IMPLEMENTED. Using default 100 pips TP.", TPStrategy);
                    return 100;

                case TakeProfitStrategyType.OscillatorSignal: // RSI, Stoch и т.д.
                    // Это больше подходит для динамического закрытия в ManageTrades, а не для установки фиксированного TP.
                    Print("TP Strategy 'OscillatorSignal' is intended for dynamic closing, not fixed TP. Using RR TP.");
                    takeProfitPips = calculatedStopLossPips * TakeProfitRR1;
                    break;
                case TakeProfitStrategyType.MACrossover:
                    Print("TP Strategy 'MACrossover' is intended for dynamic closing, not fixed TP. Using RR TP.");
                    takeProfitPips = calculatedStopLossPips * TakeProfitRR1;
                    break;

                default:
                    Print("CalculateTakeProfitPips: Unknown or not implemented TP Strategy: {0}. Using RR TP.", TPStrategy);
                    takeProfitPips = calculatedStopLossPips * TakeProfitRR1;
                    break;
            }
            
            // Минимальный TP
            double minTpPips = Symbol.PipSize * 10 / Symbol.PipSize; // 10 пипс в единицах пипсов
            if (Symbol.Name.ToUpper().Contains("GER") || Symbol.Name.ToUpper().Contains("DAX")) minTpPips = 10;
            else minTpPips = 10 * Symbol.PipSize / Symbol.TickSize * Symbol.TickSize / Symbol.PipSize;

            if (takeProfitPips < minTpPips)
            {
                Print("Calculated TP ({0:F1} pips) is too small. Setting to min ({1:F1} pips).", takeProfitPips, minTpPips);
                takeProfitPips = minTpPips;
            }
            return Math.Round(takeProfitPips, 1);
        }

        // Пример функции для расчета СЛ в пипсах для ОБ (требует доработки)
        private double CalculateStopLossPipsOB(double entryPrice, TradeType tradeType, OrderBlockInfo ob)
        {
            // Логика расчета СЛ за Ордер Блоком
            // Например, для покупки СЛ ниже минимума ОБ + некий буфер (например, на основе ATR)
            // Для продажи СЛ выше максимума ОБ + буфер
            // double atrValue = Indicators.AverageTrueRange(StopLossAtrPeriodOB).Result.LastValue;
            // double buffer = atrValue * StopLossAtrMultiplierOB;
            // if (tradeType == TradeType.Buy)
            // {
            //     return (entryPrice - (ob.Low - buffer)) / Symbol.PipSize;
            // }
            // else
            // {
            //     return ((ob.High + buffer) - entryPrice) / Symbol.PipSize;
            // }
            Print("CalculateStopLossPipsOB: Stop loss calculation for OB not yet implemented.");
            return 100; // Заглушка
        }

        // Пример структуры для информации об Ордер Блоке
        private struct OrderBlockInfo
        {
            public double High;         // Максимум свечи ОБ
            public double Low;          // Минимум свечи ОБ
            public double Open;         // Цена открытия свечи ОБ
            public double Close;        // Цена закрытия свечи ОБ
            public DateTime Time;       // Время открытия свечи ОБ
            public bool IsBullish;      // true для бычьего ОБ (ожидаем от него покупку), false для медвежьего (ожидаем продажу)
            public int Index;           // Индекс бара ОБ в MarketSeries
            public bool IsMitigated;    // Был ли ОБ уже протестирован/митигирован (упрощенно)

            public double RangePips(Symbol symbol)
            {
                return Math.Round((High - Low) / symbol.PipSize, 1);
            }

            public double FiftyPercent
            {
                get { return Low + (High - Low) / 2; }
            }

            public override string ToString()
            {
                return string.Format("{0} OB at {1:s}: O={2}, H={3}, L={4}, C={5}, Range={6} pips, Mitigated={7}", 
                                     IsBullish ? "Bullish Demand" : "Bearish Supply", 
                                     Time, Open, High, Low, Close, RangePips(Symbol.Current), IsMitigated // Assuming Symbol is accessible or passed
                                     );
            }
        }

        // Заглушки для функций определения структуры и ОБ
        private bool IsBullishStructure(MarketSeries series, int lookback, string seriesName = "") 
        {
            if (series.Close.Count < lookback + SmcSwingStrength * 2 + 1) 
            {
                Print("IsBullishStructure ({0}): Not enough data. Bars: {1}, Need for lookback & strength: {2}", 
                    seriesName, series.Close.Count, lookback + SmcSwingStrength * 2 + 1);
                return false;
            }

            var swingPoints = FindSwingPoints(series, lookback, SmcSwingStrength, seriesName);

            if (swingPoints.Count < 4) // Нужно как минимум 2 Highs и 2 Lows для сравнения (L0, H0, L1, H1)
            {
                //Print("IsBullishStructure ({0}): Not enough swing points ({1}) found to determine structure.", seriesName, swingPoints.Count);
                return false;
            }

            // Берем последние 4 свинга. Они должны чередоваться (L, H, L, H или H, L, H, L)
            // Для бычьей структуры нам нужны последние: Low, High, Low, High (L0, H0, L1, H1)
            // Где H1 > H0 и L1 > L0

            // Отфильтровываем, чтобы получить последние два Low и два High
            var swingLows = swingPoints.Where(sp => !sp.IsHigh).OrderByDescending(sp => sp.Index).Take(2).ToList();
            var swingHighs = swingPoints.Where(sp => sp.IsHigh).OrderByDescending(sp => sp.Index).Take(2).ToList();

            if (swingLows.Count < 2 || swingHighs.Count < 2)
            {
                //Print("IsBullishStructure ({0}): Not enough distinct swing highs ({1}) and lows ({2}) found.", seriesName, swingHighs.Count, swingLows.Count);
                return false;
            }

            // Сортируем по возрастанию индекса, чтобы H1 был последним, H0 предпоследним и т.д.
            swingLows.Reverse(); // L0, L1
            swingHighs.Reverse(); // H0, H1

            SwingPoint h1 = swingHighs[1]; // Последний High
            SwingPoint h0 = swingHighs[0]; // Предпоследний High
            SwingPoint l1 = swingLows[1];  // Последний Low
            SwingPoint l0 = swingLows[0];  // Предпоследний Low

            // Проверяем правильность чередования и последовательности для бычьей структуры
            // H1 должен быть после L1, L1 после H0, H0 после L0
            bool correctSequence = h1.Index > l1.Index && l1.Index > h0.Index && h0.Index > l0.Index;
            
            bool isHigherHigh = h1.Price > h0.Price;
            bool isHigherLow = l1.Price > l0.Price;

            if (correctSequence && isHigherHigh && isHigherLow)
            {
                Print("IsBullishStructure ({0}): PASSED. H1({1}@{2}) > H0({3}@{4}) AND L1({5}@{6}) > L0({7}@{8})", 
                    seriesName, h1.Price, h1.Index, h0.Price, h0.Index, l1.Price, l1.Index, l0.Price, l0.Index);
                return true;
            }
            // else
            // {
            //     Print("IsBullishStructure ({0}): FAILED. CorrectSeq: {1}, HH: {2} (H1({3}) > H0({4})), HL: {5} (L1({6}) > L0({7}))", 
            //         seriesName, correctSequence, isHigherHigh, h1.Price, h0.Price, isHigherLow, l1.Price, l0.Price);
            // }

            return false; 
        }

        private bool IsBearishStructure(MarketSeries series, int lookback, string seriesName = "") 
        {
            if (series.Close.Count < lookback + SmcSwingStrength * 2 + 1) 
            {
                Print("IsBearishStructure ({0}): Not enough data. Bars: {1}, Need for lookback & strength: {2}", 
                    seriesName, series.Close.Count, lookback + SmcSwingStrength * 2 + 1);
                return false;
            }

            var swingPoints = FindSwingPoints(series, lookback, SmcSwingStrength, seriesName);

            if (swingPoints.Count < 4) // H0, L0, H1, L1
            {
                //Print("IsBearishStructure ({0}): Not enough swing points ({1}) found to determine structure.", seriesName, swingPoints.Count);
                return false;
            }

            // Для медвежьей структуры: High, Low, High, Low (H0, L0, H1, L1)
            // Где L1 < L0 и H1 < H0
            var swingLows = swingPoints.Where(sp => !sp.IsHigh).OrderByDescending(sp => sp.Index).Take(2).ToList();
            var swingHighs = swingPoints.Where(sp => sp.IsHigh).OrderByDescending(sp => sp.Index).Take(2).ToList();

            if (swingLows.Count < 2 || swingHighs.Count < 2)
            {
                //Print("IsBearishStructure ({0}): Not enough distinct swing highs ({1}) and lows ({2}) found.", seriesName, swingHighs.Count, swingLows.Count);
                return false;
            }

            swingLows.Reverse(); // L0, L1
            swingHighs.Reverse(); // H0, H1

            SwingPoint h1 = swingHighs[1]; // Последний High
            SwingPoint h0 = swingHighs[0]; // Предпоследний High
            SwingPoint l1 = swingLows[1];  // Последний Low
            SwingPoint l0 = swingLows[0];  // Предпоследний Low

            // Проверяем правильность чередования и последовательности для медвежьей структуры
            // L1 должен быть после H1, H1 после L0, L0 после H0
            bool correctSequence = l1.Index > h1.Index && h1.Index > l0.Index && l0.Index > h0.Index;

            bool isLowerLow = l1.Price < l0.Price;
            bool isLowerHigh = h1.Price < h0.Price;

            if (correctSequence && isLowerLow && isLowerHigh)
            {
                Print("IsBearishStructure ({0}): PASSED. L1({1}@{2}) < L0({3}@{4}) AND H1({5}@{6}) < H0({7}@{8})", 
                    seriesName, l1.Price, l1.Index, l0.Price, l0.Index, h1.Price, h1.Index, h0.Price, h0.Index);
                return true;
            }
            // else 
            // {
            //     Print("IsBearishStructure ({0}): FAILED. CorrectSeq: {1}, LL: {2} (L1({3}) < L0({4})), LH: {5} (H1({6}) < H0({7}))", 
            //         seriesName, correctSequence, isLowerLow, l1.Price, l0.Price, isLowerHigh, h1.Price, h0.Price);
            // }
            return false; 
        }

        private OrderBlockInfo? FindBullishOrderBlock(MarketSeries series, int lookbackBars, string seriesName = "")
        {
            if (series.Close.Count < lookbackBars + 5) // +5 для некоторого контекста и последующих свечей
            {
                Print("FindBullishOrderBlock ({0}): Not enough data ({1} bars, need >{2})", seriesName, series.Close.Count, lookbackBars + 5);
                return null;
            }

            // Ищем с конца (самые свежие бары) в пределах lookbackBars
            // Индекс последнего доступного бара series.Close.Count - 1
            // Мы ищем ОБ, поэтому смотрим на бары ДО текущего момента (не включая самый последний формирующийся бар)
            for (int i = series.Close.Count - 2; i >= series.Close.Count - 1 - lookbackBars && i > 1; i--)
            {
                // Ищем последнюю медвежью свечу (Bullish OB - это зона спроса, часто формируется медвежьей свечой)
                if (series.Close[i] < series.Open[i]) // Это медвежья свеча
                {
                    // Проверяем, было ли после этой свечи сильное бычье движение
                    // Упрощенно: следующая свеча должна быть бычьей и ее High должен быть выше High нашей кандидат-ОБ свечи
                    // Или, следующая свеча пробивает максимум ОБ и закрывается выше него.
                    if (i + 1 < series.Close.Count)
                    {
                        var obCandidateBar = i;
                        var breakoutCandle = i + 1;

                        // Условие 1: Резкий вынос ликвидности вверх после медвежьей свечи
                        bool strongBullishMove = series.High[breakoutCandle] > series.High[obCandidateBar] && 
                                                 series.Close[breakoutCandle] > series.Open[breakoutCandle] && // следующая свеча бычья
                                                 (series.High[breakoutCandle] - series.Low[breakoutCandle]) > (series.High[obCandidateBar] - series.Low[obCandidateBar]) * 0.7; // следующая свеча имеет сопоставимый/больший диапазон
                        
                        // Условие 2 (альтернативное): Пробой максимума ОБ и закрытие выше
                        bool breakoutAndCloseAbove = series.High[breakoutCandle] > series.High[obCandidateBar] && 
                                                   series.Close[breakoutCandle] > series.High[obCandidateBar];

                        if (strongBullishMove || breakoutAndCloseAbove)
                        {
                            // Проверка на митигацию (очень упрощенно): не было ли касания Low ОБ последующими свечами до текущего момента
                            bool isMitigated = false;
                            for (int k = breakoutCandle + 1; k < series.Close.Count -1; k++)
                            {
                                if (series.Low[k] <= series.High[obCandidateBar]) // Если цена зашла в диапазон ОБ (до High)
                                {
                                     // Более точная митигация - если цена коснулась Low или 50% ОБ
                                     if (series.Low[k] <= series.Low[obCandidateBar]) 
                                     {
                                        isMitigated = true;
                                        // Print("Bullish OB at {0} considered mitigated by bar at {1} (Low {2} <= OB Low {3})", 
                                        //    series.OpenTime[obCandidateBar], series.OpenTime[k], series.Low[k], series.Low[obCandidateBar]);
                                        break;
                                     }
                                }
                            }

                            // if (isMitigated) continue; // Пропускаем митигированные ОБ

                            var ob = new OrderBlockInfo
                            {
                                High = series.High[obCandidateBar],
                                Low = series.Low[obCandidateBar],
                                Open = series.Open[obCandidateBar],
                                Close = series.Close[obCandidateBar],
                                Time = series.OpenTime[obCandidateBar],
                                IsBullish = true, // Это бычий ОБ (зона спроса)
                                Index = obCandidateBar,
                                IsMitigated = isMitigated 
                            };
                            Print("Found Potential Bullish OB ({0}): {1}", seriesName, ob.ToString());
                            return ob; // Возвращаем первый найденный (самый свежий)
                        }
                    }
                }
            }
            //Print("FindBullishOrderBlock ({0}): No bullish OB found in the last {1} bars.", seriesName, lookbackBars);
            return null;
        }

        private OrderBlockInfo? FindBearishOrderBlock(MarketSeries series, int lookbackBars, string seriesName = "")
        {
            if (series.Close.Count < lookbackBars + 5)
            {
                Print("FindBearishOrderBlock ({0}): Not enough data ({1} bars, need >{2})", seriesName, series.Close.Count, lookbackBars + 5);
                return null;
            }

            for (int i = series.Close.Count - 2; i >= series.Close.Count - 1 - lookbackBars && i > 1; i--)
            {
                // Ищем последнюю бычью свечу (Bearish OB - это зона предложения, часто формируется бычьей свечой)
                if (series.Close[i] > series.Open[i]) // Это бычья свеча
                {
                    if (i + 1 < series.Close.Count)
                    {
                        var obCandidateBar = i;
                        var breakoutCandle = i + 1;

                        bool strongBearishMove = series.Low[breakoutCandle] < series.Low[obCandidateBar] && 
                                                 series.Close[breakoutCandle] < series.Open[breakoutCandle] && // следующая свеча медвежья
                                                 (series.High[breakoutCandle] - series.Low[breakoutCandle]) > (series.High[obCandidateBar] - series.Low[obCandidateBar]) * 0.7;

                        bool breakoutAndCloseBelow = series.Low[breakoutCandle] < series.Low[obCandidateBar] && 
                                                   series.Close[breakoutCandle] < series.Low[obCandidateBar];

                        if (strongBearishMove || breakoutAndCloseBelow)
                        {
                            bool isMitigated = false;
                            for (int k = breakoutCandle + 1; k < series.Close.Count-1; k++)
                            {
                                if (series.High[k] >= series.Low[obCandidateBar]) // Если цена зашла в диапазон ОБ (до Low)
                                {
                                    // Более точная митигация - если цена коснулась High или 50% ОБ
                                    if (series.High[k] >= series.High[obCandidateBar])
                                    {
                                        isMitigated = true;
                                        // Print("Bearish OB at {0} considered mitigated by bar at {1} (High {2} >= OB High {3})", 
                                        //    series.OpenTime[obCandidateBar], series.OpenTime[k], series.High[k], series.High[obCandidateBar]);
                                        break;
                                    }
                                }
                            }
                            // if (isMitigated) continue;

                            var ob = new OrderBlockInfo
                            {
                                High = series.High[obCandidateBar],
                                Low = series.Low[obCandidateBar],
                                Open = series.Open[obCandidateBar],
                                Close = series.Close[obCandidateBar],
                                Time = series.OpenTime[obCandidateBar],
                                IsBullish = false, // Это медвежий ОБ (зона предложения)
                                Index = obCandidateBar,
                                IsMitigated = isMitigated
                            };
                            Print("Found Potential Bearish OB ({0}): {1}", seriesName, ob.ToString());
                            return ob; 
                        }
                    }
                }
            }
            //Print("FindBearishOrderBlock ({0}): No bearish OB found in the last {1} bars.", seriesName, lookbackBars);
            return null;
        }

        private List<SwingPoint> FindSwingPoints(MarketSeries series, int lookbackBars, int swingStrength, string seriesName = "")
        {
            var swingPoints = new List<SwingPoint>();
            if (series.Close.Count < lookbackBars || series.Close.Count < swingStrength * 2 + 1)
            {
                Print("FindSwingPoints ({0}): Not enough data. Bars: {1}, Need for lookback: {2}, Need for strength: {3}", 
                    seriesName, series.Close.Count, lookbackBars, swingStrength * 2 + 1);
                return swingPoints;
            }

            // Итерируем по барам, пропуская края, где нельзя определить свинг из-за swingStrength
            // lookbackBars определяет, сколько последних баров мы анализируем
            // Начальный индекс для анализа: series.Close.Count - lookbackBars
            // Конечный индекс: series.Close.Count - 1 (текущий бар не может быть подтвержденным свингом)
            
            int startIndex = Math.Max(swingStrength, series.Close.Count - lookbackBars);
            int endIndex = series.Close.Count - 1 - swingStrength;

            // Print("FindSwingPoints ({0}): Analyzing from index {1} to {2} (Total bars: {3}, Strength: {4})", 
            //       seriesName, startIndex, endIndex, series.Close.Count, swingStrength);

            for (int i = startIndex; i <= endIndex; i++)
            {
                bool isSwingHigh = true;
                for (int j = 1; j <= swingStrength; j++)
                {
                    if (series.High[i] < series.High[i - j] || series.High[i] < series.High[i + j])
                    {
                        isSwingHigh = false;
                        break;
                    }
                }

                if (isSwingHigh)
                {
                    // Дополнительная проверка: если предыдущий свинг тоже High, берем тот, что выше
                    // или если это первый свинг.
                    if (!swingPoints.Any() || !swingPoints.Last().IsHigh || series.High[i] > swingPoints.Last().Price)
                    {
                        if (swingPoints.Any() && swingPoints.Last().IsHigh) swingPoints.RemoveAt(swingPoints.Count-1); // Удаляем предыдущий, более низкий High
                        swingPoints.Add(new SwingPoint { Index = i, Price = series.High[i], Time = series.OpenTime[i], IsHigh = true });
                        // Print("Found Swing High at {0} ({1}) for {2}", series.High[i], series.OpenTime[i], seriesName);
                        continue; // Не может быть одновременно High и Low
                    }
                }

                bool isSwingLow = true;
                for (int j = 1; j <= swingStrength; j++)
                {
                    if (series.Low[i] > series.Low[i - j] || series.Low[i] > series.Low[i + j])
                    {
                        isSwingLow = false;
                        break;
                    }
                }

                if (isSwingLow)
                {
                    if (!swingPoints.Any() || swingPoints.Last().IsHigh || series.Low[i] < swingPoints.Last().Price)
                    {
                        if (swingPoints.Any() && !swingPoints.Last().IsHigh) swingPoints.RemoveAt(swingPoints.Count-1); // Удаляем предыдущий, более высокий Low
                        swingPoints.Add(new SwingPoint { Index = i, Price = series.Low[i], Time = series.OpenTime[i], IsHigh = false });
                        // Print("Found Swing Low at {0} ({1}) for {2}", series.Low[i], series.OpenTime[i], seriesName);
                    }
                }
            }
            // Print("FindSwingPoints ({0}): Found {1} swing points.", seriesName, swingPoints.Count);
            return swingPoints;
        }

        private struct SwingPoint
        {
            public int Index; // Bar index in the MarketSeries
            public double Price;
            public DateTime Time;
            public bool IsHigh; // True if swing high, false if swing low

            public override string ToString()
            {
                return string.Format("{0} at {1} ({2:s})", IsHigh ? "High" : "Low", Price, Time);
            }
        }

        protected override void OnStop()
        {
            Print("GER40Bot stopped.");
        }
        
        protected override void OnError(Error error)
        {
            Print("An error occurred: {0}", error.Code);
            if (error.Code == ErrorCode.NoMoney)
            {
                Print("Not enough money for operation.");
                // Можно остановить бота или предпринять другие действия
                // Stop(); 
            }
        }

        private enum LiquiditySweepType { None, HighSwept, LowSwept }

        private TradeSignalInfo? FindLiquiditySweepPlusReaction(MarketSeries series, int candleLookback, double reactionPips, int maxBarsForReaction, string seriesName = "")
        {
            // Мы будем анализировать последние N H1 свечей (N = candleLookback)
            // И смотреть, не было ли снятия их High/Low текущими или недавними свечами
            // Затем ждем реакцию

            if (series.Close.Count < candleLookback + maxBarsForReaction + 5) // Достаточно данных для анализа
            {
                Print("FindLSPR ({0}): Not enough data. Bars: {1}, Need > {2}", seriesName, series.Close.Count, candleLookback + maxBarsForReaction + 5);
                return null;
            }

            // Итерируем по последним 'maxBarsForReaction' барам, так как снятие могло произойти на них, 
            // а реакция развивается сейчас.
            // Индекс последнего закрытого бара: series.Close.Count - 2 (индексация с 0, последний series.Close.Count - 1)
            for (int currentBarIdx = series.Close.Count - 2; currentBarIdx >= series.Close.Count - 1 - maxBarsForReaction && currentBarIdx >= candleLookback; currentBarIdx--)
            {
                // 'currentBarIdx' - это бар, на котором мы ПОТЕНЦИАЛЬНО видим завершение реакции
                // Снятие ликвидности должно было произойти РАНЬШЕ

                for (int prevCandleOffset = 1; prevCandleOffset <= candleLookback; prevCandleOffset++)
                {
                    int sweptCandleIdx = currentBarIdx - prevCandleOffset; // Индекс свечи, с которой могла быть снята ликвидность
                     // Важно: sweptCandleIdx должен быть ДО currentBarIdx, и также ранее, чем начало окна реакции
                    if (sweptCandleIdx < 0) continue;

                    double prevCandleHigh = series.High[sweptCandleIdx];
                    double prevCandleLow = series.Low[sweptCandleIdx];
                    LiquiditySweepType sweepType = LiquiditySweepType.None;
                    double sweepPrice = 0;
                    int sweepOccurredOnBarIdx = -1;

                    // Проверяем, был ли High этой свечи пробит (снята ликвидность сверху)
                    // в промежутке между sweptCandleIdx и currentBarIdx
                    for (int checkSweepBarIdx = sweptCandleIdx + 1; checkSweepBarIdx <= currentBarIdx; checkSweepBarIdx++)
                    {
                        if (series.High[checkSweepBarIdx] > prevCandleHigh)
                        {
                            sweepType = LiquiditySweepType.HighSwept;
                            sweepPrice = prevCandleHigh; // Уровень, с которого сняли ликвидность
                            sweepOccurredOnBarIdx = checkSweepBarIdx;
                            // Print("LSPR ({0}): High {1} of H1 candle {2} swept by H1 candle {3} (High {4})", 
                            //    seriesName, prevCandleHigh, sweptCandleIdx, checkSweepBarIdx, series.High[checkSweepBarIdx]);
                            break; 
                        }
                    }

                    if (sweepType == LiquiditySweepType.HighSwept) // Если High был снят, ищем реакцию ВНИЗ
                    {
                        // Реакция: цена должна упасть на reactionPips от prevCandleHigh (уровня снятия)
                        // на свече currentBarIdx или между sweepOccurredOnBarIdx и currentBarIdx
                        if (series.Low[currentBarIdx] < sweepPrice - reactionPips * Symbol.PipSize) 
                        {
                            Print("LSPR ({0}): SELL Signal. High {1} of H1@{2} swept by H1@{3}. Reaction: Low {4} on H1@{5} < {6} (SweepPrice - ReactionPips)",
                                seriesName, prevCandleHigh, series.OpenTime[sweptCandleIdx].ToString("HH:mm"), series.OpenTime[sweepOccurredOnBarIdx].ToString("HH:mm"), 
                                series.Low[currentBarIdx], series.OpenTime[currentBarIdx].ToString("HH:mm"), sweepPrice - reactionPips * Symbol.PipSize);
                            return new TradeSignalInfo { 
                                SignalType = TradeType.Sell, 
                                EntryPrice = series.Close[currentBarIdx], // Пример входа по закрытию реакционной свечи
                                ProtectiveStopLevel = series.High[sweepOccurredOnBarIdx], // SL за максимум свечи, снявшей ликвидность
                                TargetLevel = series.Close[currentBarIdx] - (series.High[sweepOccurredOnBarIdx] - series.Close[currentBarIdx]) * TakeProfitRR1, // Примерный TP
                                EventPrice = sweepPrice,
                                EventBarIndex = sweptCandleIdx
                            };
                        }
                        continue; // Если High был снят, Low уже не проверяем для этой prevCandle
                    }

                    // Проверяем, был ли Low этой свечи пробит (снята ликвидность снизу)
                    for (int checkSweepBarIdx = sweptCandleIdx + 1; checkSweepBarIdx <= currentBarIdx; checkSweepBarIdx++)
                    {
                        if (series.Low[checkSweepBarIdx] < prevCandleLow)
                        {
                            sweepType = LiquiditySweepType.LowSwept;
                            sweepPrice = prevCandleLow;
                            sweepOccurredOnBarIdx = checkSweepBarIdx;
                            // Print("LSPR ({0}): Low {1} of H1 candle {2} swept by H1 candle {3} (Low {4})", 
                            //    seriesName, prevCandleLow, sweptCandleIdx, checkSweepBarIdx, series.Low[checkSweepBarIdx]);
                            break;
                        }
                    }

                    if (sweepType == LiquiditySweepType.LowSwept) // Если Low был снят, ищем реакцию ВВЕРХ
                    {
                        // Реакция: цена должна вырасти на reactionPips от prevCandleLow (уровня снятия)
                        if (series.High[currentBarIdx] > sweepPrice + reactionPips * Symbol.PipSize)
                        {
                            Print("LSPR ({0}): BUY Signal. Low {1} of H1@{2} swept by H1@{3}. Reaction: High {4} on H1@{5} > {6} (SweepPrice + ReactionPips)",
                                seriesName, prevCandleLow, series.OpenTime[sweptCandleIdx].ToString("HH:mm"), series.OpenTime[sweepOccurredOnBarIdx].ToString("HH:mm"), 
                                series.High[currentBarIdx], series.OpenTime[currentBarIdx].ToString("HH:mm"), sweepPrice + reactionPips * Symbol.PipSize);
                            return new TradeSignalInfo { 
                                SignalType = TradeType.Buy, 
                                EntryPrice = series.Close[currentBarIdx],
                                ProtectiveStopLevel = series.Low[sweepOccurredOnBarIdx], // SL за минимум свечи, снявшей ликвидность
                                TargetLevel = series.Close[currentBarIdx] + (series.Close[currentBarIdx] - series.Low[sweepOccurredOnBarIdx]) * TakeProfitRR1,
                                EventPrice = sweepPrice,
                                EventBarIndex = sweptCandleIdx
                            };
                        }
                    }
                }
            }
            return null;
        }

        // Вспомогательная структура для возврата информации о сигнале (может понадобиться для других типов входа)
        private struct TradeSignalInfo
        {
            public TradeType SignalType;        // Buy or Sell
            public double EntryPrice;           // Предполагаемая цена входа
            public double ProtectiveStopLevel;  // Уровень для установки стоп-лосса (например, за High/Low свипа)
            public double TargetLevel;          // Возможный первоначальный таргет (если применим к стратегии)
            public double EventPrice;           // Цена события (например, уровень снятия ликвидности)
            public int EventBarIndex;        // Индекс бара, где произошло ключевое событие (напр. свеча, чью ликвидность сняли)
        }

        private SwingPoint? FindLastBreakOfStructure(MarketSeries series, int lookback, int swingStrength, bool isBullishContext, string seriesName = "")
        {
            if (series.Close.Count < lookback + swingStrength * 2 + 1)
            {
                Print("FindBOS ({0}): Not enough data. Bars: {1}, Need for lookback & strength: {2}", 
                    seriesName, series.Close.Count, lookback + swingStrength * 2 + 1);
                return null;
            }

            // Ищем последний свинг, который соответствует контексту
            for (int i = series.Close.Count - 1; i >= lookback + swingStrength * 2 + 1; i--)
            {
                bool isSwingHigh = true;
                for (int j = 1; j <= swingStrength; j++)
                {
                    if (series.High[i] < series.High[i - j] || series.High[i] < series.High[i + j])
                    {
                        isSwingHigh = false;
                        break;
                    }
                }

                if (isSwingHigh)
                {
                    // Проверяем, соответствует ли последний свинг контексту
                    if (isBullishContext)
                    {
                        if (series.High[i] > series.High[i - swingStrength] && series.High[i - swingStrength] > series.High[i - 2 * swingStrength])
                        {
                            return new SwingPoint { Index = i, Price = series.High[i], Time = series.OpenTime[i], IsHigh = true };
                        }
                    }
                    else
                    {
                        if (series.Low[i] < series.Low[i - swingStrength] && series.Low[i - swingStrength] < series.Low[i - 2 * swingStrength])
                        {
                            return new SwingPoint { Index = i, Price = series.Low[i], Time = series.OpenTime[i], IsHigh = false };
                        }
                    }
                }
            }
            return null;
        }

        private bool IsADXTrendStrongEnough(MarketSeries series, TradeType tradeTypeSignal)
        {
            if (!EnableADXFilter) return true; // Если фильтр отключен, всегда считаем, что тренд сильный

            if (series.Close.Count < ADXFilterPeriod + 1) 
            {
                Print("IsADXTrendStrongEnough: Not enough data for ADX ({0} bars, need >{1}). Assuming weak trend.", series.Close.Count, ADXFilterPeriod + 1);
                return false; // Недостаточно данных, считаем тренд слабым
            }

            DirectionalMovementSystem adx = Indicators.DirectionalMovementSystem(series, ADXFilterPeriod);
            double adxValue = adx.ADX.Last(1);       // ADX на предыдущей закрытой свече
            double pdiValue = adx.DIPlus.Last(1);   // +DI
            double ndiValue = adx.DIMinus.Last(1);  // -DI

            bool isTrendStrong = adxValue > ADXFilterThreshold;
            bool isDirectionConfirmed = false;

            if (tradeTypeSignal == TradeType.Buy && pdiValue > ndiValue)
            {
                isDirectionConfirmed = true;
            }
            else if (tradeTypeSignal == TradeType.Sell && ndiValue > pdiValue)
            {
                isDirectionConfirmed = true;
            }

            if (isTrendStrong && isDirectionConfirmed)
            {
                Print("ADX Filter: PASSED. ADX({0:F2}) > Threshold({1}), Direction confirmed for {2} (+DI:{3:F2}, -DI:{4:F2})", 
                    adxValue, ADXFilterThreshold, tradeTypeSignal, pdiValue, ndiValue);
                return true;
            }
            else
            {
                Print("ADX Filter: FAILED. ADX({0:F2}) vs Threshold({1}), TrendStrong: {2}, DirectionConfirmed for {3}: {4} (+DI:{5:F2}, -DI:{6:F2})", 
                    adxValue, ADXFilterThreshold, isTrendStrong, tradeTypeSignal, isDirectionConfirmed, pdiValue, ndiValue);
                return false;
            }
        }

        private struct LiquiditySweepEventInfo
        {
            public TradeType AnticipatedSignal; // Buy (если Low был сметен), Sell (если High был сметен)
            public double SweptLevelPrice;     // Цена уровня, с которого сняли ликвидность (High/Low свечи-цели)
            public double SweepExtremePrice;   // Экстремум свечи, которая совершила свип (Low для Buy, High для Sell)
            public DateTime SweptCandleTime;   // Время свечи, с которой сняли ликвидность
            public DateTime SweepOccurredTime; // Время свечи, которая совершила свип
            public int SweepCandleIndex;     // Индекс свечи, которая совершила свип
        }

        private LiquiditySweepEventInfo? FindLiquiditySweepEvent(MarketSeries series, int candleLookback, string seriesName = "")
        {
            // Ищем снятие ликвидности с High/Low одной из `candleLookback` предыдущих свечей
            // Анализируем последнюю закрытую свечу (`series.Close.Count - 2`) как ту, что МОГЛА совершить свип.
            if (series.Close.Count < candleLookback + 3) // +2 для свечи-цели и свечи-свипа, +1 для запаса
            {
                Print("FindLSEvent ({0}): Not enough data. Bars: {1}, Need > {2}", seriesName, series.Close.Count, candleLookback + 3);
                return null;
            }

            int sweepAttemptCandleIdx = series.Close.Count - 2; // Последняя закрытая свеча, которая могла совершить свип

            for (int prevCandleOffset = 1; prevCandleOffset <= candleLookback; prevCandleOffset++)
            {
                int targetCandleIdx = sweepAttemptCandleIdx - prevCandleOffset;
                if (targetCandleIdx < 0) continue;

                double targetHigh = series.High[targetCandleIdx];
                double targetLow = series.Low[targetCandleIdx];

                // Проверка снятия High (ожидаем сигнал на Sell)
                if (series.High[sweepAttemptCandleIdx] > targetHigh && series.Close[sweepAttemptCandleIdx] < series.High[sweepAttemptCandleIdx]) // Цена прошла выше и откатила немного или закрылась ниже
                {
                    Print("FindLSEvent ({0}): Potential High Sweep. TargetH1@{1} High={2:F5}. SweepCandleH1@{3} High={4:F5}, Close={5:F5}", 
                        seriesName, series.OpenTime[targetCandleIdx].ToString("HH:mm dd.MM"), targetHigh, 
                        series.OpenTime[sweepAttemptCandleIdx].ToString("HH:mm dd.MM"), series.High[sweepAttemptCandleIdx], series.Close[sweepAttemptCandleIdx]);
                    return new LiquiditySweepEventInfo
                    {
                        AnticipatedSignal = TradeType.Sell,
                        SweptLevelPrice = targetHigh,
                        SweepExtremePrice = series.High[sweepAttemptCandleIdx], // SL будет за этим хаем
                        SweptCandleTime = series.OpenTime[targetCandleIdx],
                        SweepOccurredTime = series.OpenTime[sweepAttemptCandleIdx],
                        SweepCandleIndex = sweepAttemptCandleIdx
                    };
                }

                // Проверка снятия Low (ожидаем сигнал на Buy)
                if (series.Low[sweepAttemptCandleIdx] < targetLow && series.Close[sweepAttemptCandleIdx] > series.Low[sweepAttemptCandleIdx]) // Цена прошла ниже и откатила немного или закрылась выше
                {
                     Print("FindLSEvent ({0}): Potential Low Sweep. TargetH1@{1} Low={2:F5}. SweepCandleH1@{3} Low={4:F5}, Close={5:F5}", 
                        seriesName, series.OpenTime[targetCandleIdx].ToString("HH:mm dd.MM"), targetLow, 
                        series.OpenTime[sweepAttemptCandleIdx].ToString("HH:mm dd.MM"), series.Low[sweepAttemptCandleIdx], series.Close[sweepAttemptCandleIdx]);
                    return new LiquiditySweepEventInfo
                    {
                        AnticipatedSignal = TradeType.Buy,
                        SweptLevelPrice = targetLow,
                        SweepExtremePrice = series.Low[sweepAttemptCandleIdx], // SL будет за этим лоу
                        SweptCandleTime = series.OpenTime[targetCandleIdx],
                        SweepOccurredTime = series.OpenTime[sweepAttemptCandleIdx],
                        SweepCandleIndex = sweepAttemptCandleIdx
                    };
                }
            }
            return null;
        }
    }

    // Перечисление для определения контекста рынка
    public enum MarketContext
    {
        Bullish,
        Bearish,
        Neutral,
        Ranging // Возможно, понадобится для других стратегий
    }

    public enum TakeProfitStrategyType
    {
        RiskRewardRatio, // На Основе Соотношения Риск/Прибыль
        SupportResistance, // Уровни Поддержки и Сопротивления (Требует логики определения уровней)
        PreviousExtremes,  // Предыдущие Экстремумы (Swing Highs/Lows) (Требует логики определения экстремумов)
        ChannelBoundaries, // Границы Каналов или Трендовых Линий (Требует логики определения каналов/линий)
        ChartPatterns,     // Цели по Графическим Паттернам (Требует сложной логики распознавания паттернов)
        FibonacciExtensions, // Расширения Фибоначчи (Требует логики определения импульса и коррекции)
        FibonacciRetracement, // Уровни Коррекции Фибоначчи (Требует логики определения основного тренда)
        ATR,               // На Основе Волатильности (ATR)
        BollingerBands,    // Полосы Боллинджера / Каналы Кельтнера (для Кельтнера можно использовать ATR Bands)
        OscillatorSignal,  // Сигналы Осцилляторов (например, RSI)
        MACrossover        // Пересечение Скользящих Средних
    }

    public enum ContextStrategyType
    {
        MarketStructureSMC, // Анализ структуры HH/HL, LH/LL (H4/H1)
        MovingAverage,      // Выше/ниже EMA/SMA
        BollingerBands,     // Цена внутри/вне полос, наклон полос
        RSI,                // Зоны перекупленности/перепроданности, дивергенции
        // --- Более сложные или требующие детальной проработки ---
        // ZigZagStructure, // MS + ZigZag
        // STB_BTS_SMC,     // STB/BTS (Sweep Then Break / Break Then Sweep)
        // OrderFlowSMC,    // Анализ потока ордеров (Очень сложно)
        // MACDtrend,       // MACD гистограмма, положение линий
        // ParabolicSAR,    // Положение точек SAR
        // ADX,             // Сила тренда (ADX > порога)
        // IchimokuCloud,   // Положение цены относительно облака, линий
        // Stochastic,      // Зоны, пересечения
        // CCI,             // Зоны, пересечение нуля
        // WilliamsPercentR,// Зоны
        // KeltnerChannels, // Положение цены относительно канала
        // DonchianChannels,// Положение цены, пробои
        // MAEnvelopes,     // Положение цены относительно конвертов
        // PriceImpulse     // Резкое движение цены за N баров (логика из вашего примера)
    }

    public enum StopLossStrategyType
    {
        OrderBlockSMC,    // За Ордер Блоком (возможно с буфером ATR)
        ATR,              // На основе ATR
        FixedPips,        // Фиксированное количество пунктов
        BollingerBands,   // За внешней границей Полос Боллинджера + отступ
        // --- Более сложные или требующие детальной проработки ---
        // SupportResistance, // За уровнем поддержки/сопротивления
        // SwingHighLow,    // За предыдущим экстремумом
        // TrendLine,       // За трендовой линией
        // MovingAverage,   // N пипс за скользящей средней
        // ParabolicSAR,    // По точкам SAR
        // IchimokuCloud,   // За Кумо или линиями Ишимоку
        // LiquidityGrabSMC, // За Зоной Захвата Ликвидности
        // StructureInvalidationSMC // Уровень инвалидации структуры
        SwingHighLow,
    }

    public enum EntryStrategyType
    {
        OrderBlockSMC,     // Вход от Ордер Блока (РЕАЛИЗОВАНО)
        FairValueGapSMC,   // Вход от FVG (Имбаланс) (ЗАГЛУШКА)
        MACDCrossover,     // Пересечение линий MACD или нулевой линии (ЗАГЛУШКА)
        BreakOfStructureSMC, // Вход на Break of Structure (на основном ТФ) (РЕАЛИЗОВАНО - только логгирование)
        ChangeOfCharacterSMC, // Вход после Change of Character (РЕАЛИЗОВАНО - только логгирование)
        // --- Более сложные или требующие детальной проработки ---
        LiquiditySweepPlusReactionSMC, // Снятие ликвидности + реакция (РЕАЛИЗОВАНО)
        BreakOfStructureM3SMC, // BOS на младшем ТФ (M3) (РЕАЛИЗОВАНО)
        IchimokuSignal,  // Сигналы Ишимоку (пробой Кумо, пересечение линий) (РЕАЛИЗОВАНО)
        // ADXConfirmation, // ADX для подтверждения тренда + другой сигнал (РЕАЛИЗОВАНО КАК ФИЛЬТР)
        LiquiditySweepSMC, // Вход после Liquidity Sweep
        // ChoCHSMC,        // Вход после Change of Character
        // BOS_SMC,         // Вход на Break of Structure (на основном ТФ)
        // BreakerBlockSMC, // Вход от Брейкер Блока
        // InducementSMC    // Вход после захвата Inducement
    }
}
