namespace EngineAnalyzer
{
    // ================================================================
    // AGGREGATE
    // ================================================================

    public class AggregateCell
    {
        public long Count
        {
            get;
            private set;
        }


        public double TotalWeightSeconds
        {
            get;
            private set;
        }


        public double WeightedSum
        {
            get;
            private set;
        }


        public double WeightedSumAbs
        {
            get;
            private set;
        }


        public double WeightedSumSquares
        {
            get;
            private set;
        }


        public double Min
        {
            get;
            private set;
        } =
            double.PositiveInfinity;


        public double Max
        {
            get;
            private set;
        } =
            double.NegativeInfinity;


        public void Add(
            double value,
            double weightSeconds)
        {
            if (weightSeconds <= 0)
                return;


            Count++;

            TotalWeightSeconds +=
                weightSeconds;

            WeightedSum +=
                value *
                weightSeconds;

            WeightedSumAbs +=
                Math.Abs(value) *
                weightSeconds;

            WeightedSumSquares +=
                value *
                value *
                weightSeconds;


            Min =
                Math.Min(
                    Min,
                    value);

            Max =
                Math.Max(
                    Max,
                    value);
        }


        public double Mean =>
            TotalWeightSeconds <= 0
                ? double.NaN
                : WeightedSum /
                  TotalWeightSeconds;


        public double MeanAbs =>
            TotalWeightSeconds <= 0
                ? double.NaN
                : WeightedSumAbs /
                  TotalWeightSeconds;


        public double Rms =>
            TotalWeightSeconds <= 0
                ? double.NaN
                : Math.Sqrt(
                    WeightedSumSquares /
                    TotalWeightSeconds);


        public double StandardDeviation
        {
            get
            {
                if (TotalWeightSeconds <= 0)
                    return double.NaN;

                double mean = Mean;

                double variance =
                    WeightedSumSquares /
                    TotalWeightSeconds -
                    mean * mean;

                return Math.Sqrt(
                    Math.Max(0, variance));
            }
        }


        public double CoefficientOfVariationPercent
        {
            get
            {
                if (TotalWeightSeconds <= 0)
                    return double.NaN;

                double mean = Mean;
                double standardDeviation =
                    StandardDeviation;

                // For a perfectly stable zero-deviation cell both μ and σ
                // are zero. Treat it as 0% variation for display purposes.
                if (Math.Abs(mean) < 1e-12)
                {
                    return standardDeviation < 1e-12
                        ? 0
                        : double.NaN;
                }

                return standardDeviation /
                       mean *
                       100.0;
            }
        }
    }


    // ================================================================
    // 3D
    // ================================================================

    public readonly struct ScenePoint
    {
        public double X { get; }

        public double Y { get; }

        public double Z { get; }


        public ScenePoint(
            double x,
            double y,
            double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }


    public readonly struct ProjectedPoint
    {
        public double X { get; }

        public double Y { get; }

        public double Depth { get; }


        public ProjectedPoint(
            double x,
            double y,
            double depth)
        {
            X = x;
            Y = y;
            Depth = depth;
        }
    }


    public class RenderSurfaceCell
    {
        public ProjectedPoint P00
        {
            get;
            set;
        }

        public ProjectedPoint P10
        {
            get;
            set;
        }

        public ProjectedPoint P11
        {
            get;
            set;
        }

        public ProjectedPoint P01
        {
            get;
            set;
        }

        public double Depth
        {
            get;
            set;
        }

        public double Value
        {
            get;
            set;
        }
    }
}
