using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MarketStructure : Indicator
    {
        [Parameter("Swing Period", DefaultValue = 5, MinValue = 2)]
        public int SwingPeriod { get; set; }

        private readonly List<SwingPoint> _structurePoints = new List<SwingPoint>();
        private readonly List<SwingPoint> _allPivots = new List<SwingPoint>();
        private readonly HashSet<string> _processedPivots = new HashSet<string>();
        private readonly List<BosEvent> _bosEvents = new List<BosEvent>();

        protected override void Initialize()
        {
            // The indicator will recalculate and redraw completely on each new bar
            // to correctly handle the repainting nature of ZigZag-like indicators.
        }

        public override void Calculate(int index)
        {
            // A pivot at 'pivotIndex' is identified using bars from 'pivotIndex - SwingPeriod' to 'pivotIndex + SwingPeriod'.
            // In Calculate(index), we can at most check for a pivot at 'index - SwingPeriod'.
            int pivotIndex = index - SwingPeriod;

            if (pivotIndex < SwingPeriod)
            {
                return;
            }

            // --- Find Pivots ---
            if (IsHighPivot(pivotIndex))
            {
                AddPivot(new SwingPoint { Index = pivotIndex, Price = Bars.HighPrices[pivotIndex], Type = SwingType.High });
            }
            else if (IsLowPivot(pivotIndex))
            {
                AddPivot(new SwingPoint { Index = pivotIndex, Price = Bars.LowPrices[pivotIndex], Type = SwingType.Low });
            }

            // To optimize, we only rebuild and redraw everything on the most recent bar.
            // This is efficient for live data but means the history will be drawn at the end of loading.
            if (IsLastBar)
            {
                BuildStructure();
                DetectBos();
                DrawStructure();
            }
        }

        private bool IsHighPivot(int index)
        {
            var price = Bars.HighPrices[index];
            for (int i = 1; i <= SwingPeriod; i++)
            {
                if (price < Bars.HighPrices[index - i] || price < Bars.HighPrices[index + i])
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsLowPivot(int index)
        {
            var price = Bars.LowPrices[index];
            for (int i = 1; i <= SwingPeriod; i++)
            {
                if (price > Bars.LowPrices[index - i] || price > Bars.LowPrices[index + i])
                {
                    return false;
                }
            }
            return true;
        }

        private void AddPivot(SwingPoint newPivot)
        {
            var key = $"{(newPivot.Type == SwingType.High ? "H" : "L")}_{newPivot.Index}";
            if (_processedPivots.Add(key))
            {
                _allPivots.Add(newPivot);
                // Keep pivots sorted by index for performance
                _allPivots.Sort((a, b) => a.Index.CompareTo(b.Index));
            }
        }
        
        private void BuildStructure()
        {
            _structurePoints.Clear();
            if (_allPivots.Count == 0) return;

            var lastPoint = _allPivots[0];
            _structurePoints.Add(lastPoint);

            for (int i = 1; i < _allPivots.Count; i++)
            {
                var currentPoint = _allPivots[i];
                if (currentPoint.Type != lastPoint.Type)
                {
                    _structurePoints.Add(currentPoint);
                    lastPoint = currentPoint;
                }
                else
                {
                    if ((currentPoint.Type == SwingType.High && currentPoint.Price > lastPoint.Price) ||
                        (currentPoint.Type == SwingType.Low && currentPoint.Price < lastPoint.Price))
                    {
                        _structurePoints[_structurePoints.Count - 1] = currentPoint;
                        lastPoint = currentPoint;
                    }
                }
            }
        }

        private void DetectBos()
        {
            _bosEvents.Clear();

            for (int i = 2; i < _structurePoints.Count; i++)
            {
                var prevPoint = _structurePoints[i - 2];
                var midPoint = _structurePoints[i - 1];
                var currentPoint = _structurePoints[i];
                
                // Bullish BOS: New high is higher than the previous high.
                if (currentPoint.Type == SwingType.High && midPoint.Type == SwingType.Low && prevPoint.Type == SwingType.High)
                {
                    if (currentPoint.Price > prevPoint.Price)
                    {
                        var bos = new BosEvent
                        {
                            Level = prevPoint.Price,
                            StartIndex = prevPoint.Index,
                            EndIndex = currentPoint.Index,
                            IsConfirmed = true,
                            IsBullish = true
                        };
                        _bosEvents.Add(bos);
                    }
                }
                // Bearish BOS: New low is lower than the previous low.
                else if (currentPoint.Type == SwingType.Low && midPoint.Type == SwingType.High && prevPoint.Type == SwingType.Low)
                {
                    if (currentPoint.Price < prevPoint.Price)
                    {
                         var bos = new BosEvent
                        {
                            Level = prevPoint.Price,
                            StartIndex = prevPoint.Index,
                            EndIndex = currentPoint.Index,
                            IsConfirmed = true,
                            IsBullish = false
                        };
                        _bosEvents.Add(bos);
                    }
                }
            }
            
            // Check for unconfirmed BOS at the very end of the structure
            if (_structurePoints.Count > 1)
            {
                var lastStructurePoint = _structurePoints[_structurePoints.Count - 1];
                var lastPriceBarIndex = Bars.Count - 1;
                var lastPrice = Bars.ClosePrices.LastValue;

                // Find the last relevant swing high/low to be broken
                SwingPoint lastMajorPivot = null;
                if(lastStructurePoint.Type == SwingType.Low) // Looking for bullish BOS
                {
                    lastMajorPivot = _structurePoints.Where(p => p.Type == SwingType.High).LastOrDefault();
                }
                else // Looking for bearish BOS
                {
                    lastMajorPivot = _structurePoints.Where(p => p.Type == SwingType.Low).LastOrDefault();
                }

                if (lastMajorPivot != null)
                {
                    bool isBullishBos = lastStructurePoint.Type == SwingType.Low && lastPrice > lastMajorPivot.Price;
                    bool isBearishBos = lastStructurePoint.Type == SwingType.High && lastPrice < lastMajorPivot.Price;

                    if (isBullishBos || isBearishBos)
                    {
                        _bosEvents.Add(new BosEvent
                        {
                            Level = lastMajorPivot.Price,
                            StartIndex = lastMajorPivot.Index,
                            EndIndex = lastPriceBarIndex,
                            IsConfirmed = false, // Unconfirmed as no new swing has formed yet
                            IsBullish = isBullishBos
                        });
                    }
                }
            }
        }
        
        private void DrawStructure()
        {
            Chart.RemoveAllObjects();

            for (int i = 0; i < _structurePoints.Count; i++)
            {
                var point = _structurePoints[i];
                var iconName = $"swing_{point.Index}";
                Chart.DrawIcon(iconName, ChartIconType.Circle, point.Index, point.Price, Color.Gray);

                if (i > 0)
                {
                    var prevPoint = _structurePoints[i - 1];
                    var lineName = $"line_{prevPoint.Index}_{point.Index}";
                    Chart.DrawTrendLine(lineName, prevPoint.Index, prevPoint.Price, point.Index, point.Price, Color.Gray, 1, LineStyle.Lines);
                }
            }

            foreach (var bos in _bosEvents)
            {
                var lineName = $"bos_{bos.StartIndex}";
                var textName = $"bos_text_{bos.StartIndex}";
                var color = bos.IsBullish ? Color.Green : Color.Red;
                var text = bos.IsConfirmed ? "cBOS" : "BOS";

                Chart.DrawTrendLine(lineName, bos.StartIndex, bos.Level, bos.EndIndex, bos.Level, color, 1, LineStyle.Dots);
                Chart.DrawText(textName, text, bos.EndIndex, bos.Level, color);
            }
        }
    }

    public class SwingPoint
    {
        public int Index { get; set; }
        public double Price { get; set; }
        public SwingType Type { get; set; }
    }

    public enum SwingType
    {
        High,
        Low
    }

    public class BosEvent
    {
        public double Level { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public bool IsConfirmed { get; set; }
        public bool IsBullish { get; set; }
    }
} 