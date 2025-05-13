using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Indicators
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class ZigZag : Indicator
    {
        [Parameter("Depth", DefaultValue = 12)]
        public int Depth { get; set; }

        [Parameter("Deviation", DefaultValue = 5)]
        public double Deviation { get; set; }

        [Parameter("Backstep", DefaultValue = 3)]
        public int Backstep { get; set; }

        [Output("ZigZag", LineColor = "Red", PlotType = PlotType.Points, Thickness = 2)]
        public IndicatorDataSeries ZigZagBuffer { get; set; }

        private int _lastHigh;
        private int _lastLow;

        protected override void Initialize()
        {
            _lastHigh = -1;
            _lastLow = -1;
        }

        public override void Calculate(int index)
        {
            ZigZagBuffer[index] = double.NaN;

            if (index < Depth)
                return;

            double high = MarketSeries.High.Maximum(Depth);
            double low = MarketSeries.Low.Minimum(Depth);

            if (MarketSeries.High[index] == high && (index - _lastHigh) > Backstep)
            {
                ZigZagBuffer[index] = high;
                _lastHigh = index;
            }
            else if (MarketSeries.Low[index] == low && (index - _lastLow) > Backstep)
            {
                ZigZagBuffer[index] = low;
                _lastLow = index;
            }
        }
    }
}
