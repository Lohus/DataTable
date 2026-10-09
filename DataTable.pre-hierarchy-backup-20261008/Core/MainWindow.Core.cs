namespace EngineAnalyzer
{
    public partial class MainWindow : Window
    {
        // ============================================================
        // НАСТРАИВАЕМЫЕ ПАРАМЕТРЫ
        // ============================================================

        private double graphTempMin;
        private double graphTempMax;
        private double graphTempStep;
        private double tableTempMin;
        private double tableTempMax;
        private double tableTempStep;

        private double graphPowerMin;
        private double graphPowerMax;
        private double graphPowerStep;
        private double tablePowerMin;
        private double tablePowerMax;
        private double tablePowerStep;

        private double nominalRpm = 1500;
        private double maxAllowedDeviation = 1000;

        private const double RedDeviationThreshold = 12;
        private const int ExcelHeatStyleSteps = 24;

        private static readonly CovColorConverter covColorConverter =
            new CovColorConverter();

        private int graphTempBins;
        private int graphPowerBins;
        private int tableTempBins;
        private int tablePowerBins;


        // ============================================================
        // ДАННЫЕ
        // ============================================================

        private AggregateCell[,] graphCells =
            new AggregateCell[1, 1];

        private AggregateCell[,] tableCells =
            new AggregateCell[1, 1];

        private double[,] surface =
            new double[1, 1];

        private bool[,] surfaceValid =
            new bool[1, 1];

        private double deviationMax = 1;

        private long totalRows;
        private long acceptedRows;
        private long rejectedRows;
        private long sourceRows;
        private double weightedSeconds;

        private long uncachedRejectedIntervals;

        private readonly Dictionary<ObservationKey, CachedObservation>
            observationCache =
                new Dictionary<ObservationKey, CachedObservation>();

        private string lastSource = "";

        private string[] lastCsvFiles =
            Array.Empty<string>();

        private string loadedCsvMappingSignature = "";

        private string loadedPostgresMappingSignature = "";

        private SourceKind lastSourceKind =
            SourceKind.None;

        private string xAxisTitle = "IntTempOut, °C";
        private string yAxisTitle = "Power, kW";

        private int pgXTagId = 569;
        private int pgYTagId = 558;
        private int pgSpeedTagId = 625;
        private bool pgMetricIsDeviation;

        private readonly PlotModel plotModel =
            new PlotModel();

        private sealed class EngineState
        {
            public double? Power;
            public double? Temperature;
            public double? EngineSpeed;
            public double? LegacyDeviation;
            public bool UseLegacyDeviation;
        }

        private readonly record struct ObservationKey(
            double Temperature,
            double Power,
            double SpeedOrDeviation,
            bool IsLegacyDeviation);

        private sealed class CachedObservation
        {
            public double DurationSeconds;
            public long IntervalCount;
        }

        private readonly record struct CsvColumnMapping(
            string DateColumn,
            string XColumn,
            string YColumn,
            string MetricColumn,
            bool MetricIsDeviation);

        private enum SourceKind
        {
            None,
            Csv,
            PostgreSql
        }

        private sealed class CovColorConverter : IValueConverter
        {
            public object Convert(
                object value,
                Type targetType,
                object parameter,
                CultureInfo culture)
            {
                if (value == null ||
                    value == DBNull.Value ||
                    !double.TryParse(
                        value.ToString(),
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out double cov))
                {
                    return Brushes.Transparent;
                }

                (byte r, byte g, byte b) =
                    GetPastelRgb(cov);

                SolidColorBrush brush =
                    new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            r,
                            g,
                            b));

                brush.Freeze();
                return brush;
            }

            public object ConvertBack(
                object value,
                Type targetType,
                object parameter,
                CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }


        // ============================================================
        // КАМЕРА
        // ============================================================

        private double angleX = 25;
        private double angleY = -35;
        private double zoom = 1;

        private bool isDragging;

        private System.Windows.Point lastMousePosition;


        // ============================================================
        // CONSTRUCTOR
        // ============================================================

        public MainWindow()
        {
            InitializeComponent();

            SetupPlot();

            ConfigureGrid();
        }


        // ============================================================
        // GRID CONFIG
        // ============================================================

        private void ConfigureGrid()
        {
            xAxisTitle =
                GetRequiredText(
                    XAxisTitleTextBox.Text,
                    "Название оси X");

            yAxisTitle =
                GetRequiredText(
                    YAxisTitleTextBox.Text,
                    "Название оси Y");

            graphTempMin =
                ParseSetting(
                    GraphTempMinTextBox.Text,
                    "X min графика");

            graphTempMax =
                ParseSetting(
                    GraphTempMaxTextBox.Text,
                    "X max графика");

            graphTempStep =
                ParseSetting(
                    GraphTempStepTextBox.Text,
                    "Шаг X графика");

            tableTempStep =
                ParseSetting(
                    TableTempStepTextBox.Text,
                    "Шаг X таблицы");

            tableTempMin =
                ParseSetting(
                    TableTempMinTextBox.Text,
                    "X min таблицы");

            tableTempMax =
                ParseSetting(
                    TableTempMaxTextBox.Text,
                    "X max таблицы");

            graphPowerMin =
                ParseSetting(
                    GraphPowerMinTextBox.Text,
                    "Y min графика");

            graphPowerMax =
                ParseSetting(
                    GraphPowerMaxTextBox.Text,
                    "Y max графика");

            graphPowerStep =
                ParseSetting(
                    GraphPowerStepTextBox.Text,
                    "Шаг Y графика");

            tablePowerStep =
                ParseSetting(
                    TablePowerStepTextBox.Text,
                    "Шаг Y таблицы");

            tablePowerMin =
                ParseSetting(
                    TablePowerMinTextBox.Text,
                    "Y min таблицы");

            tablePowerMax =
                ParseSetting(
                    TablePowerMaxTextBox.Text,
                    "Y max таблицы");

            nominalRpm =
                ParseSetting(
                    NominalRpmTextBox.Text,
                    "Номинальные обороты");

            maxAllowedDeviation =
                ParseSetting(
                    MaxDeviationTextBox.Text,
                    "Максимальное отклонение");


            if (graphTempMax <= graphTempMin ||
                tableTempMax <= tableTempMin)
                throw new Exception(
                    "X max должна быть больше X min для каждой вкладки.");

            if (graphPowerMax <= graphPowerMin ||
                tablePowerMax <= tablePowerMin)
                throw new Exception(
                    "Y max должна быть больше Y min для каждой вкладки.");

            if (graphTempStep <= 0 ||
                graphPowerStep <= 0 ||
                tableTempStep <= 0 ||
                tablePowerStep <= 0)
            {
                throw new Exception(
                    "Шаг сетки должен быть больше 0.");
            }

            if (nominalRpm <= 0)
            {
                throw new Exception(
                    "Номинальные обороты должны быть больше 0.");
            }

            if (maxAllowedDeviation < 0)
            {
                throw new Exception(
                    "Максимальное отклонение не может быть отрицательным.");
            }


            graphTempBins =
                GetBinCount(
                    graphTempMin,
                    graphTempMax,
                    graphTempStep);

            graphPowerBins =
                GetBinCount(
                    graphPowerMin,
                    graphPowerMax,
                    graphPowerStep);

            tableTempBins =
                GetBinCount(
                    tableTempMin,
                    tableTempMax,
                    tableTempStep);

            tablePowerBins =
                GetBinCount(
                    tablePowerMin,
                    tablePowerMax,
                    tablePowerStep);


            long graphCellCount =
                (long)graphTempBins *
                graphPowerBins;

            long tableCellCount =
                (long)tableTempBins *
                tablePowerBins;


            if (graphTempBins > 2001 ||
                graphPowerBins > 2001 ||
                tableTempBins > 2001 ||
                tablePowerBins > 2001)
            {
                throw new Exception(
                    "На каждой оси допускается не более 2001 точки.");
            }


            // Защита от случайного задания,
            // например 0.01°C x 0.1 kW
            if (graphCellCount > 100_000 ||
                tableCellCount > 100_000)
            {
                throw new Exception(
                    "Сетка слишком мелкая.\n\n" +
                    $"График: {graphCellCount:N0} ячеек.\n" +
                    $"Таблица: {tableCellCount:N0} ячеек.\n" +
                    "Увеличьте соответствующий шаг температуры или мощности.");
            }


            graphCells =
                new AggregateCell[
                    graphTempBins,
                    graphPowerBins];

            tableCells =
                new AggregateCell[
                    tableTempBins,
                    tablePowerBins];

            surface =
                new double[
                    graphTempBins,
                    graphPowerBins];

            surfaceValid =
                new bool[
                    graphTempBins,
                    graphPowerBins];


            for (int t = 0;
                 t < graphTempBins;
                 t++)
            {
                for (int p = 0;
                     p < graphPowerBins;
                     p++)
                {
                    graphCells[t, p] =
                        new AggregateCell();
                }
            }

            for (int t = 0;
                 t < tableTempBins;
                 t++)
            {
                for (int p = 0;
                     p < tablePowerBins;
                     p++)
                {
                    tableCells[t, p] =
                        new AggregateCell();
                }
            }
        }


        private void ClearData()
        {
            ConfigureGrid();

            totalRows = 0;
            acceptedRows = 0;
            rejectedRows = 0;
            sourceRows = 0;
            weightedSeconds = 0;
            uncachedRejectedIntervals = 0;

            observationCache.Clear();

            deviationMax = 1;

            SurfaceMatrixGrid.ItemsSource = null;
            SurfaceMatrixGrid.Columns.Clear();

            Progress.Value = 0;
        }


        // ============================================================
        // PLOT
        // ============================================================

        private void SetupPlot()
        {
            plotModel.Title =
                "Time-weighted RMS Engine Speed Deviation";

            plotModel.Subtitle =
                "Цветовая шкала: 0 — зелёный, 12 rpm и выше — красный";

            plotModel.PlotAreaBorderThickness =
                new OxyThickness(0);

            plotModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Bottom,
                    IsAxisVisible = false,
                    MinimumPadding = 0.08,
                    MaximumPadding = 0.08
                });

            plotModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Left,
                    IsAxisVisible = false,
                    MinimumPadding = 0.08,
                    MaximumPadding = 0.08
                });

            PlotController controller =
                new PlotController();

            controller.UnbindAll();
            Plot.Controller = controller;
            Plot.Model = plotModel;
            Plot.InvalidatePlot(true);
        }
    }
}
