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
