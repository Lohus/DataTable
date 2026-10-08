using Microsoft.Win32;
using Npgsql;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

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

        private static readonly RmsColorConverter rmsColorConverter =
            new RmsColorConverter();

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

        private sealed class RmsColorConverter : IValueConverter
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
                        out double rms))
                {
                    return Brushes.Transparent;
                }

                (byte r, byte g, byte b) =
                    GetPastelRgb(rms);

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


        // ============================================================
        // CSV
        // ============================================================

        private async void OpenCsv_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Filter =
                        "CSV files (*.csv)|*.csv|" +
                        "All files (*.*)|*.*",

                    Title =
                        "Выберите один или несколько CSV",

                    Multiselect = true
                };


            if (dialog.ShowDialog() != true)
                return;


            try
            {
                lastCsvFiles =
                    dialog.FileNames.ToArray();

                PopulateCsvColumnSelectors(
                    lastCsvFiles[0]);

                CsvColumnMapping mapping =
                    GetCsvColumnMapping();

                SetBusy(true);

                ConfigureGrid();

                ClearData();

                lastSource =
                    $"CSV: {lastCsvFiles.Length} файл(ов)";


                long totalBytes =
                    lastCsvFiles
                        .Sum(
                            path =>
                                new FileInfo(path).Length);


                await Task.Run(
                    () =>
                        LoadCsvFiles(
                            lastCsvFiles,
                            totalBytes,
                            mapping));

                loadedCsvMappingSignature =
                    GetCsvMappingSignature(mapping);

                lastSourceKind =
                    SourceKind.Csv;


                FinishProcessing();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                SetBusy(false);
            }
        }


        private void LoadCsvFiles(
            string[] fileNames,
            long totalBytes,
            CsvColumnMapping mapping)
        {
            long completedBytes = 0;
            EngineState state = new EngineState();
            DateTime? stateTimestamp = null;


            foreach (string filePath
                     in fileNames)
            {
                LoadSingleCsv(
                    filePath,
                    totalBytes,
                    completedBytes,
                    state,
                    ref stateTimestamp,
                    mapping);


                completedBytes +=
                    new FileInfo(filePath).Length;
            }
        }


        private void LoadSingleCsv(
            string filePath,
            long totalBytes,
            long previousBytes,
            EngineState state,
            ref DateTime? stateTimestamp,
            CsvColumnMapping mapping)
        {
            using FileStream fileStream =
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.SequentialScan);


            using StreamReader reader =
                new StreamReader(
                    fileStream,
                    bufferSize:
                    1024 * 1024);


            string? headerLine =
                reader.ReadLine();


            if (string.IsNullOrWhiteSpace(
                headerLine))
            {
                return;
            }


            char delimiter =
                DetectDelimiter(
                    headerLine);


            string[] headers =
                SplitCsvLine(
                    headerLine,
                    delimiter);


            if (headers.Length > 0)
            {
                headers[0] =
                    headers[0]
                        .TrimStart('\uFEFF');
            }


            int powerIndex =
                FindColumn(
                    headers,
                    mapping.YColumn);


            int dateIndex =
                FindColumn(
                    headers,
                    mapping.DateColumn);


            int tempIndex =
                FindColumn(
                    headers,
                    mapping.XColumn);


            int speedIndex =
                FindColumn(
                    headers,
                    mapping.MetricColumn);


            if (dateIndex < 0)
                throw new Exception(
                    $"В {Path.GetFileName(filePath)} " +
                    $"не найден столбец {mapping.DateColumn}. " +
                    "Он необходим для веса времени.");

            if (powerIndex < 0)
                throw new Exception(
                    $"В {Path.GetFileName(filePath)} " +
                    $"не найден столбец оси Y: {mapping.YColumn}.");

            if (tempIndex < 0)
                throw new Exception(
                    $"В {Path.GetFileName(filePath)} " +
                    $"не найден столбец оси X: {mapping.XColumn}.");

            if (speedIndex < 0)
            {
                throw new Exception(
                    $"В {Path.GetFileName(filePath)} " +
                    $"не найден столбец RPM/отклонения: {mapping.MetricColumn}.");
            }


            bool useLegacyDeviation =
                mapping.MetricIsDeviation;


            int maxIndex =
                Math.Max(
                    dateIndex,
                    Math.Max(
                        powerIndex,
                        Math.Max(
                            tempIndex,
                            speedIndex)));


            string? line;

            long localRows = 0;


            while (
                (line = reader.ReadLine()) != null)
            {
                localRows++;
                sourceRows++;


                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }


                string[] values =
                    SplitCsvLine(
                        line,
                        delimiter);


                if (values.Length <= maxIndex)
                {
                    throw new FormatException(
                        $"{Path.GetFileName(filePath)}, строка {localRows + 1}: " +
                        "недостаточно столбцов.");
                }


                DateTime timestamp =
                    ParseCsvDateTime(
                        values[dateIndex],
                        filePath,
                        localRows + 1);


                if (stateTimestamp.HasValue &&
                    timestamp < stateTimestamp.Value)
                {
                    throw new InvalidOperationException(
                        $"Нарушен порядок времени в {Path.GetFileName(filePath)}, " +
                        $"строка {localRows + 1}. CSV должны идти по возрастанию Date.");
                }


                if (stateTimestamp.HasValue &&
                    timestamp > stateTimestamp.Value)
                {
                    totalRows++;
                    ProcessState(
                        state,
                        (timestamp - stateTimestamp.Value).TotalSeconds);
                }


                stateTimestamp = timestamp;


                if (state.UseLegacyDeviation != useLegacyDeviation)
                {
                    if (useLegacyDeviation)
                        state.EngineSpeed = null;
                    else
                        state.LegacyDeviation = null;

                    state.UseLegacyDeviation = useLegacyDeviation;
                }


                bool valid =
                    UpdateForwardFilledValue(
                        values[powerIndex],
                        ref state.Power) &
                    UpdateForwardFilledValue(
                        values[tempIndex],
                        ref state.Temperature);


                if (useLegacyDeviation)
                {
                    valid &=
                        UpdateForwardFilledValue(
                            values[speedIndex],
                            ref state.LegacyDeviation);
                }
                else
                {
                    valid &=
                        UpdateForwardFilledValue(
                            values[speedIndex],
                            ref state.EngineSpeed);
                }


                if (!valid)
                {
                    continue;
                }


                // Обновляем UI не на каждой строке.
                if (localRows % 100_000 == 0)
                {
                    long currentBytes =
                        previousBytes +
                        fileStream.Position;


                    double percent =
                        totalBytes <= 0
                            ? 0
                            : currentBytes *
                              100.0 /
                              totalBytes;


                    ReportProgress(
                        percent,
                        $"CSV | " +
                        $"{Path.GetFileName(filePath)} | " +
                        $"строк: {sourceRows:N0} | " +
                        $"интервалов: {totalRows:N0}");
                }
            }
        }


        private static bool UpdateForwardFilledValue(
            string value,
            ref double? currentValue)
        {
            // Equivalent to replacing an empty string with null and applying
            // forward_fill in the former Polars Flow File step.
            if (string.IsNullOrWhiteSpace(value))
                return true;


            if (TryParseDouble(
                value,
                out double parsedValue))
            {
                currentValue = parsedValue;
                return true;
            }


            currentValue = null;
            return false;
        }


        private void ProcessState(
            EngineState state,
            double durationSeconds)
        {
            if (durationSeconds <= 0 ||
                double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds))
            {
                return;
            }


            double? speedOrDeviation =
                state.UseLegacyDeviation
                    ? state.LegacyDeviation
                    : state.EngineSpeed;


            if (!state.Power.HasValue ||
                !state.Temperature.HasValue ||
                !speedOrDeviation.HasValue ||
                double.IsNaN(state.Power.Value) ||
                double.IsInfinity(state.Power.Value) ||
                double.IsNaN(state.Temperature.Value) ||
                double.IsInfinity(state.Temperature.Value) ||
                double.IsNaN(speedOrDeviation.Value) ||
                double.IsInfinity(speedOrDeviation.Value))
            {
                uncachedRejectedIntervals++;
                return;
            }


            ObservationKey key =
                new ObservationKey(
                state.Temperature.Value,
                state.Power.Value,
                    speedOrDeviation.Value,
                    state.UseLegacyDeviation);


            if (!observationCache.TryGetValue(
                    key,
                    out CachedObservation? cached))
            {
                cached = new CachedObservation();
                observationCache.Add(key, cached);
            }


            cached.DurationSeconds += durationSeconds;
            cached.IntervalCount++;
        }


        // ============================================================
        // POSTGRESQL
        // ============================================================

        private async void TestPostgres_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string connectionString =
                    BuildPostgresConnectionString();


                await using NpgsqlDataSource dataSource =
                    NpgsqlDataSource.Create(
                        connectionString);


                await using var connection =
                    await dataSource
                        .OpenConnectionAsync();


                MessageBox.Show(
                    "Подключение к PostgreSQL успешно.",
                    "PostgreSQL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }


        private async void LoadPostgres_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);

                ConfigurePostgresTags();

                ConfigureGrid();

                ClearData();


                DateTime from =
                    ParseDateTime(
                        PgFromTextBox.Text,
                        "Дата начала");

                DateTime to =
                    ParseDateTime(
                        PgToTextBox.Text,
                        "Дата окончания");


                if (to <= from)
                {
                    throw new Exception(
                        "Дата окончания должна быть позже даты начала.");
                }


                string connectionString =
                    BuildPostgresConnectionString();


                lastSource =
                    $"PostgreSQL: {from:yyyy-MM-dd HH:mm:ss} — " +
                    $"{to:yyyy-MM-dd HH:mm:ss}";


                Progress.IsIndeterminate =
                    true;


                await Task.Run(
                    async () =>
                        await LoadFromPostgres(
                            connectionString,
                            from,
                            to));


                Progress.IsIndeterminate =
                    false;

                FinishProcessing();

                loadedPostgresMappingSignature =
                    GetPostgresMappingSignature();

                lastSourceKind =
                    SourceKind.PostgreSql;
            }
            catch (Exception ex)
            {
                Progress.IsIndeterminate =
                    false;

                ShowError(ex);
            }
            finally
            {
                SetBusy(false);
            }
        }


        private async Task LoadFromPostgres(
            string connectionString,
            DateTime from,
            DateTime to)
        {
            await using NpgsqlDataSource dataSource =
                NpgsqlDataSource.Create(
                    connectionString);


            await using var connection =
                await dataSource.OpenConnectionAsync();


            await using var transaction =
                await connection.BeginTransactionAsync(
                    IsolationLevel.RepeatableRead);


            DateTime fromValue =
                DateTime.SpecifyKind(
                    from,
                    DateTimeKind.Unspecified);


            DateTime toValue =
                DateTime.SpecifyKind(
                    to,
                    DateTimeKind.Unspecified);


            EngineState state =
                new EngineState
                {
                    UseLegacyDeviation =
                        pgMetricIsDeviation
                };


            // Empty values have the same meaning as null in the Flow File.
            // Therefore the seed must be the last actual value of each tag.
            const string seedSql =
                """
                SELECT DISTINCT ON (id)
                    t,
                    id,
                    v
                FROM public.trends
                WHERE id = ANY($2)
                  AND t < $1
                  AND v IS NOT NULL
                  AND btrim(v::text) <> ''
                ORDER BY id, t DESC, ctid DESC;
                """;


            await using (
                var seedCommand =
                    new NpgsqlCommand(
                        seedSql,
                        connection,
                        transaction))
            {
                seedCommand.CommandTimeout = 0;
                seedCommand.Parameters.AddWithValue(
                    fromValue);

                seedCommand.Parameters.AddWithValue(
                    new[]
                    {
                        pgXTagId,
                        pgYTagId,
                        pgSpeedTagId
                    });


                await using var seedReader =
                    await seedCommand.ExecuteReaderAsync();


                while (await seedReader.ReadAsync())
                {
                    ApplyTrendEvent(
                        state,
                        seedReader.GetInt32(1),
                        seedReader.IsDBNull(2)
                            ? null
                            : seedReader.GetValue(2));
                }
            }


            // Events are read in time order. The state before each event time
            // receives the duration since the previous event as its weight.
            // All changes with the same t are applied as one state transition.
            const string eventsSql =
                """
                SELECT
                    t,
                    id,
                    v
                FROM public.trends
                WHERE id = ANY($3)
                  AND t >= $1
                  AND t < $2
                ORDER BY t, id, ctid;
                """;


            long eventCount = 0;
            DateTime? currentTimestamp = null;
            DateTime intervalStart = fromValue;


            await using (
                var eventsCommand =
                    new NpgsqlCommand(
                        eventsSql,
                        connection,
                        transaction))
            {
                eventsCommand.CommandTimeout = 0;
                eventsCommand.Parameters.AddWithValue(
                    fromValue);
                eventsCommand.Parameters.AddWithValue(
                    toValue);

                eventsCommand.Parameters.AddWithValue(
                    new[]
                    {
                        pgXTagId,
                        pgYTagId,
                        pgSpeedTagId
                    });


                await using var reader =
                    await eventsCommand.ExecuteReaderAsync();


                while (await reader.ReadAsync())
                {
                    DateTime timestamp =
                        reader.GetDateTime(0);


                    if (timestamp < intervalStart)
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL вернул события не по возрастанию времени.");
                    }


                    if (!currentTimestamp.HasValue ||
                        timestamp != currentTimestamp.Value)
                    {
                        if (timestamp > intervalStart)
                        {
                            totalRows++;
                            ProcessState(
                                state,
                                (timestamp - intervalStart).TotalSeconds);
                        }


                        intervalStart = timestamp;
                        currentTimestamp = timestamp;
                    }


                    ApplyTrendEvent(
                        state,
                        reader.GetInt32(1),
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetValue(2));


                    eventCount++;
                    sourceRows++;


                    if (eventCount % 100_000 == 0)
                    {
                        ReportStatus(
                            $"PostgreSQL | событий: " +
                            $"{eventCount:N0} | состояний: " +
                            $"{totalRows:N0} | кэш: " +
                            $"{observationCache.Count:N0}");
                    }
                }
            }


            if (toValue > intervalStart)
            {
                totalRows++;
                ProcessState(
                    state,
                    (toValue - intervalStart).TotalSeconds);
            }


            await transaction.CommitAsync();
        }


        private void ApplyTrendEvent(
            EngineState state,
            int id,
            object? rawValue)
        {
            string value =
                Convert.ToString(
                    rawValue,
                    CultureInfo.InvariantCulture) ??
                string.Empty;


            if (id == pgYTagId)
            {
                UpdateForwardFilledValue(
                    value,
                    ref state.Power);
            }
            else if (id == pgXTagId)
            {
                UpdateForwardFilledValue(
                    value,
                    ref state.Temperature);
            }
            else if (id == pgSpeedTagId)
            {
                if (pgMetricIsDeviation)
                {
                    UpdateForwardFilledValue(
                        value,
                        ref state.LegacyDeviation);
                }
                else
                {
                    UpdateForwardFilledValue(
                        value,
                        ref state.EngineSpeed);
                }
            }
        }


        private void ConfigurePostgresTags()
        {
            pgXTagId =
                ParseTagId(
                    PgXTagIdTextBox.Text,
                    "PostgreSQL ID оси X");

            pgYTagId =
                ParseTagId(
                    PgYTagIdTextBox.Text,
                    "PostgreSQL ID оси Y");

            pgSpeedTagId =
                ParseTagId(
                    PgSpeedTagIdTextBox.Text,
                    "PostgreSQL ID RPM/отклонения");

            pgMetricIsDeviation =
                PgMetricIsDeviationCheckBox.IsChecked == true;

            if (pgXTagId == pgYTagId ||
                pgXTagId == pgSpeedTagId ||
                pgYTagId == pgSpeedTagId)
            {
                throw new Exception(
                    "PostgreSQL ID оси X, оси Y и RPM должны различаться.");
            }
        }


        private string GetPostgresMappingSignature()
        {
            return string.Join(
                "|",
                pgXTagId,
                pgYTagId,
                pgSpeedTagId,
                pgMetricIsDeviation,
                PgHostTextBox.Text.Trim(),
                PgPortTextBox.Text.Trim(),
                PgDatabaseTextBox.Text.Trim(),
                PgUserTextBox.Text.Trim(),
                PgFromTextBox.Text.Trim(),
                PgToTextBox.Text.Trim());
        }


        private void PopulateCsvColumnSelectors(
            string filePath)
        {
            using StreamReader reader =
                new StreamReader(filePath);

            string? headerLine =
                reader.ReadLine();

            if (string.IsNullOrWhiteSpace(headerLine))
            {
                throw new Exception(
                    $"В {Path.GetFileName(filePath)} отсутствует строка заголовков.");
            }

            char delimiter =
                DetectDelimiter(headerLine);

            string[] headers =
                SplitCsvLine(
                    headerLine,
                    delimiter)
                .Select(
                    (header, index) =>
                        index == 0
                            ? header.TrimStart('\uFEFF').Trim()
                            : header.Trim())
                .Where(header =>
                    !string.IsNullOrWhiteSpace(header))
                .ToArray();

            SelectCsvColumn(
                CsvDateColumnComboBox,
                headers,
                "Date");

            SelectCsvColumn(
                CsvXColumnComboBox,
                headers,
                "IntTempOut",
                "Themp Outdoor",
                "Temp Outdoor",
                "Temp Outdor");

            SelectCsvColumn(
                CsvYColumnComboBox,
                headers,
                "Power");

            SelectCsvColumn(
                CsvMetricColumnComboBox,
                headers,
                "Engine Speed",
                "Engine Speed Deviation");

            CsvMetricIsDeviationCheckBox.IsChecked =
                CsvMetricColumnComboBox.Text.Contains(
                    "Deviation",
                    StringComparison.OrdinalIgnoreCase);
        }


        private static void SelectCsvColumn(
            ComboBox comboBox,
            string[] headers,
            params string[] preferredNames)
        {
            string current =
                comboBox.Text.Trim();

            comboBox.ItemsSource = headers;

            string? selected =
                headers.FirstOrDefault(
                    header =>
                        string.Equals(
                            header,
                            current,
                            StringComparison.OrdinalIgnoreCase));

            selected ??=
                preferredNames
                    .Select(name =>
                        headers.FirstOrDefault(
                            header =>
                                string.Equals(
                                    header,
                                    name,
                                    StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(match => match != null);

            selected ??=
                headers.FirstOrDefault();

            comboBox.SelectedItem = selected;
            comboBox.Text = selected ?? string.Empty;
        }


        private CsvColumnMapping GetCsvColumnMapping()
        {
            CsvColumnMapping mapping =
                new CsvColumnMapping(
                    GetRequiredText(
                        CsvDateColumnComboBox.Text,
                        "CSV Date"),
                    GetRequiredText(
                        CsvXColumnComboBox.Text,
                        "Столбец оси X"),
                    GetRequiredText(
                        CsvYColumnComboBox.Text,
                        "Столбец оси Y"),
                    GetRequiredText(
                        CsvMetricColumnComboBox.Text,
                        "Столбец RPM/отклонения"),
                    CsvMetricIsDeviationCheckBox.IsChecked == true);

            if (string.Equals(
                    mapping.XColumn,
                    mapping.YColumn,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Для осей X и Y нужно выбрать разные столбцы.");
            }

            return mapping;
        }


        private void CsvXColumn_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (XAxisTitleTextBox == null)
                return;

            string name =
                GetComboSelectionText(CsvXColumnComboBox);

            if (name.Length > 0)
            {
                XAxisTitleTextBox.Text =
                    name.Contains(
                        "Temp",
                        StringComparison.OrdinalIgnoreCase)
                        ? $"{name}, °C"
                        : name;
            }
        }


        private void CsvYColumn_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (YAxisTitleTextBox == null)
                return;

            string name =
                GetComboSelectionText(CsvYColumnComboBox);

            if (name.Length > 0)
            {
                YAxisTitleTextBox.Text =
                    name.Equals(
                        "Power",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Power, kW"
                        : name;
            }
        }


        private void CsvMetricColumn_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (CsvMetricIsDeviationCheckBox == null)
                return;

            CsvMetricIsDeviationCheckBox.IsChecked =
                GetComboSelectionText(
                    CsvMetricColumnComboBox)
                .Contains(
                    "Deviation",
                    StringComparison.OrdinalIgnoreCase);
        }


        private static string GetComboSelectionText(
            ComboBox comboBox)
        {
            return (comboBox.SelectedItem?.ToString() ??
                    comboBox.Text)
                .Trim();
        }


        private static string GetCsvMappingSignature(
            CsvColumnMapping mapping)
        {
            return string.Join(
                "|",
                mapping.DateColumn,
                mapping.XColumn,
                mapping.YColumn,
                mapping.MetricColumn,
                mapping.MetricIsDeviation);
        }


        private string BuildPostgresConnectionString()
        {
            if (!int.TryParse(
                PgPortTextBox.Text,
                out int port))
            {
                throw new Exception(
                    "Неверный порт PostgreSQL.");
            }


            if (string.IsNullOrWhiteSpace(
                PgHostTextBox.Text))
            {
                throw new Exception(
                    "Укажите Host PostgreSQL.");
            }


            if (string.IsNullOrWhiteSpace(
                PgDatabaseTextBox.Text))
            {
                throw new Exception(
                    "Укажите Database.");
            }


            if (string.IsNullOrWhiteSpace(
                PgUserTextBox.Text))
            {
                throw new Exception(
                    "Укажите User.");
            }


            NpgsqlConnectionStringBuilder builder =
                new NpgsqlConnectionStringBuilder
                {
                    Host =
                        PgHostTextBox.Text.Trim(),

                    Port =
                        port,

                    Database =
                        PgDatabaseTextBox.Text.Trim(),

                    Username =
                        PgUserTextBox.Text.Trim(),

                    Password =
                        PgPasswordBox.Password,

                    Timeout =
                        15,

                    Pooling =
                        true
                };


            return builder.ConnectionString;
        }


        // ============================================================
        // ОБЩАЯ ОБРАБОТКА ТОЧКИ
        // ============================================================

        private void ProcessWeightedPoint(
            double temperature,
            double power,
            double deviation,
            double durationSeconds,
            long intervalCount)
        {
            if (double.IsNaN(temperature) ||
                double.IsInfinity(temperature) ||
                double.IsNaN(power) ||
                double.IsInfinity(power) ||
                double.IsNaN(deviation) ||
                double.IsInfinity(deviation) ||
                double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds) ||
                durationSeconds <= 0)
            {
                rejectedRows += intervalCount;
                return;
            }


            // Требуемый фильтр:
            // |RPM - nominal| <= 1000 по умолчанию.
            if (deviation >
                maxAllowedDeviation)
            {
                rejectedRows += intervalCount;
                return;
            }


            bool addedToGraph =
                temperature >= graphTempMin &&
                temperature <= graphTempMax &&
                power >= graphPowerMin &&
                power <= graphPowerMax;

            bool addedToTable =
                temperature >= tableTempMin &&
                temperature <= tableTempMax &&
                power >= tablePowerMin &&
                power <= tablePowerMax;


            if (!addedToGraph &&
                !addedToTable)
            {
                rejectedRows += intervalCount;
                return;
            }


            if (addedToGraph)
            {
                int tempBin =
                    Math.Clamp(
                        (int)Math.Round(
                            (temperature - graphTempMin) /
                            graphTempStep),
                        0,
                        graphTempBins - 1);

                int powerBin =
                    Math.Clamp(
                        (int)Math.Round(
                            (power - graphPowerMin) /
                            graphPowerStep),
                        0,
                        graphPowerBins - 1);

                graphCells[tempBin, powerBin]
                    .Add(deviation, durationSeconds);
            }


            if (addedToTable)
            {
                int tempBin =
                    Math.Clamp(
                        (int)Math.Round(
                            (temperature - tableTempMin) /
                            tableTempStep),
                        0,
                        tableTempBins - 1);

                int powerBin =
                    Math.Clamp(
                        (int)Math.Round(
                            (power - tablePowerMin) /
                            tablePowerStep),
                        0,
                        tablePowerBins - 1);

                tableCells[tempBin, powerBin]
                    .Add(deviation, durationSeconds);
            }


            acceptedRows += intervalCount;
            weightedSeconds += durationSeconds;
        }


        private void RebuildFromCache()
        {
            acceptedRows = 0;
            rejectedRows = uncachedRejectedIntervals;
            weightedSeconds = 0;


            foreach (var item in observationCache)
            {
                double deviation =
                    item.Key.IsLegacyDeviation
                        ? Math.Abs(item.Key.SpeedOrDeviation)
                        : Math.Abs(
                            item.Key.SpeedOrDeviation -
                            nominalRpm);


                ProcessWeightedPoint(
                    item.Key.Temperature,
                    item.Key.Power,
                    deviation,
                    item.Value.DurationSeconds,
                    item.Value.IntervalCount);
            }
        }


        // ============================================================
        // FINISH
        // ============================================================

        private void FinishProcessing()
        {
            RebuildFromCache();


            if (acceptedRows == 0)
            {
                throw new Exception(
                    "Не найдено подходящих полных состояний.");
            }


            BuildSurface();

            BuildSurfaceMatrix();

            Draw3D();


            Progress.IsIndeterminate =
                false;

            Progress.Value =
                100;


            StatusText.Text =
                $"{lastSource} | " +
                $"исходных строк/событий: {sourceRows:N0} | " +
                $"кэш состояний: {observationCache.Count:N0} | " +
                $"интервалов: {totalRows:N0} | " +
                $"принято: {acceptedRows:N0} | отброшено: {rejectedRows:N0} | " +
                $"вес: {TimeSpan.FromSeconds(weightedSeconds):c} | " +
                $"график: {graphTempBins} × {graphPowerBins} | " +
                $"таблица: {tableTempBins} × {tablePowerBins} | " +
                $"Max RMS: {deviationMax:F2}";
        }


        // ============================================================
        // SURFACE
        // ============================================================

        private void BuildSurface()
        {
            deviationMax = 0;


            for (int t = 0;
                 t < graphTempBins;
                 t++)
            {
                for (int p = 0;
                     p < graphPowerBins;
                     p++)
                {
                    AggregateCell cell =
                        graphCells[t, p];


                    if (cell.TotalWeightSeconds <= 0)
                    {
                        surfaceValid[t, p] =
                            false;

                        surface[t, p] =
                            0;

                        continue;
                    }


                    double rms =
                        cell.Rms;


                    surface[t, p] =
                        rms;

                    surfaceValid[t, p] =
                        true;


                    if (rms > deviationMax)
                    {
                        deviationMax =
                            rms;
                    }
                }
            }


            if (deviationMax <= 0)
                deviationMax = 1;
        }


        // ============================================================
        // RMS MATRIX
        // ============================================================

        private void BuildSurfaceMatrix()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Temperature", typeof(double));

            SurfaceMatrixGrid.Columns.Clear();
            SurfaceMatrixGrid.Columns.Add(
                new DataGridTextColumn
                {
                    Header = xAxisTitle,
                    Binding = new Binding("[Temperature]")
                    {
                        StringFormat = "0.###"
                    },
                    Width = 90
                });

            for (int p = 0; p < tablePowerBins; p++)
            {
                string columnName = $"P{p}";
                table.Columns.Add(columnName, typeof(double));

                Style cellStyle =
                    new Style(typeof(DataGridCell));

                cellStyle.Setters.Add(
                    new Setter(
                        DataGridCell.BackgroundProperty,
                        new Binding($"[{columnName}]")
                        {
                            Converter = rmsColorConverter
                        }));

                SurfaceMatrixGrid.Columns.Add(
                    new DataGridTextColumn
                    {
                        Header =
                            $"{GetTablePowerValue(p):0.###}",
                        Binding = new Binding($"[{columnName}]")
                        {
                            StringFormat = "F3"
                        },
                        CellStyle = cellStyle,
                        Width = 90
                    });
            }

            for (int t = 0; t < tableTempBins; t++)
            {
                DataRow row = table.NewRow();
                row[0] = GetTableTempValue(t);

                for (int p = 0; p < tablePowerBins; p++)
                {
                    row[p + 1] =
                        tableCells[t, p].TotalWeightSeconds <= 0
                            ? DBNull.Value
                            : tableCells[t, p].Rms;
                }

                table.Rows.Add(row);
            }

            SurfaceMatrixGrid.ItemsSource = table.DefaultView;

            MatrixDescriptionTextBlock.Text =
                $"Строки — {xAxisTitle}; столбцы — {yAxisTitle}; " +
                "ячейки — взвешенный по времени " +
                "RMS(|Engine Speed − Nominal RPM|). " +
                "Пустая ячейка означает отсутствие данных.";
        }


        private double GetGraphTempValue(
            int index)
        {
            return Math.Min(
                graphTempMin +
                index * graphTempStep,
                graphTempMax);
        }


        private double GetGraphPowerValue(
            int index)
        {
            return Math.Min(
                graphPowerMin +
                index * graphPowerStep,
                graphPowerMax);
        }


        private double GetTableTempValue(
            int index)
        {
            return Math.Min(
                tableTempMin +
                index * tableTempStep,
                tableTempMax);
        }


        private double GetTablePowerValue(
            int index)
        {
            return Math.Min(
                tablePowerMin +
                index * tablePowerStep,
                tablePowerMax);
        }


        // ============================================================
        // PRINT / PDF
        // ============================================================

        private void PrintPdf_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (acceptedRows == 0)
            {
                MessageBox.Show(
                    "Сначала загрузите и обработайте данные.",
                    "Печать",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }


            PrintDialog printDialog =
                new PrintDialog();


            if (printDialog.ShowDialog() != true)
                return;


            FlowDocument document =
                CreatePrintDocument(
                    printDialog.PrintableAreaWidth,
                    printDialog.PrintableAreaHeight);


            IDocumentPaginatorSource source =
                document;


            printDialog.PrintDocument(
                source.DocumentPaginator,
                "Engine Stability Analysis");
        }


        private void PrintGraph_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (acceptedRows == 0 ||
                Plot.ActualWidth <= 0 ||
                Plot.ActualHeight <= 0)
            {
                MessageBox.Show(
                    "Сначала загрузите и постройте график.",
                    "Печать графика",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }


            PrintDialog printDialog =
                new PrintDialog();


            if (printDialog.ShowDialog() != true)
                return;


            double pageWidth =
                printDialog.PrintableAreaWidth;

            double pageHeight =
                printDialog.PrintableAreaHeight;

            double scale =
                Math.Min(
                    pageWidth / Plot.ActualWidth,
                    pageHeight / Plot.ActualHeight);

            double graphWidth =
                Plot.ActualWidth * scale;

            double graphHeight =
                Plot.ActualHeight * scale;

            Rect target =
                new Rect(
                    (pageWidth - graphWidth) / 2,
                    (pageHeight - graphHeight) / 2,
                    graphWidth,
                    graphHeight);


            DrawingVisual page =
                new DrawingVisual();


            using (DrawingContext drawing = page.RenderOpen())
            {
                drawing.DrawRectangle(
                    Brushes.White,
                    null,
                    new Rect(
                        0,
                        0,
                        pageWidth,
                        pageHeight));

                drawing.DrawRectangle(
                    new VisualBrush(Plot)
                    {
                        Stretch = Stretch.Uniform
                    },
                    null,
                    target);
            }


            printDialog.PrintVisual(
                page,
                "Engine Stability RMS graph");
        }


        private void ExportExcel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (acceptedRows == 0)
            {
                MessageBox.Show(
                    "Сначала загрузите и обработайте данные.",
                    "Экспорт в Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Filter =
                        "Книга Excel (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    FileName =
                        $"EngineAnalyzer_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                CreateExcelWorkbook(dialog.FileName);

                StatusText.Text =
                    $"Матрица выгружена: {dialog.FileName}";

                MessageBox.Show(
                    "Матрица RMS успешно выгружена в Excel.",
                    "Экспорт в Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }


        private void CreateExcelWorkbook(
            string fileName)
        {
            using FileStream file =
                new FileStream(
                    fileName,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None);

            using ZipArchive archive =
                new ZipArchive(
                    file,
                    ZipArchiveMode.Create);

            WriteContentTypes(archive);
            WritePackageRelationships(archive);
            WriteWorkbook(archive);
            WriteWorkbookRelationships(archive);
            WriteExcelStyles(archive);
            WriteExcelWorksheet(archive);
        }


        private static void WriteContentTypes(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "[Content_Types].xml",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/package/2006/content-types";

                    writer.WriteStartElement("Types", ns);

                    writer.WriteStartElement("Default", ns);
                    writer.WriteAttributeString("Extension", "rels");
                    writer.WriteAttributeString(
                        "ContentType",
                        "application/vnd.openxmlformats-package.relationships+xml");
                    writer.WriteEndElement();

                    writer.WriteStartElement("Default", ns);
                    writer.WriteAttributeString("Extension", "xml");
                    writer.WriteAttributeString(
                        "ContentType",
                        "application/xml");
                    writer.WriteEndElement();

                    WriteContentTypeOverride(
                        writer,
                        ns,
                        "/xl/workbook.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");

                    WriteContentTypeOverride(
                        writer,
                        ns,
                        "/xl/worksheets/sheet1.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");

                    WriteContentTypeOverride(
                        writer,
                        ns,
                        "/xl/styles.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

                    writer.WriteEndElement();
                });
        }


        private static void WriteContentTypeOverride(
            XmlWriter writer,
            string ns,
            string partName,
            string contentType)
        {
            writer.WriteStartElement("Override", ns);
            writer.WriteAttributeString("PartName", partName);
            writer.WriteAttributeString("ContentType", contentType);
            writer.WriteEndElement();
        }


        private static void WritePackageRelationships(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "_rels/.rels",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/package/2006/relationships";

                    writer.WriteStartElement("Relationships", ns);
                    writer.WriteStartElement("Relationship", ns);
                    writer.WriteAttributeString("Id", "rId1");
                    writer.WriteAttributeString(
                        "Type",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
                    writer.WriteAttributeString(
                        "Target",
                        "xl/workbook.xml");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
        }


        private static void WriteWorkbook(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "xl/workbook.xml",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    const string relNs =
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

                    writer.WriteStartElement("workbook", ns);
                    writer.WriteAttributeString("xmlns", "r", null, relNs);
                    writer.WriteStartElement("sheets", ns);
                    writer.WriteStartElement("sheet", ns);
                    writer.WriteAttributeString("name", "RMS Matrix");
                    writer.WriteAttributeString("sheetId", "1");
                    writer.WriteAttributeString("r", "id", relNs, "rId1");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
        }


        private static void WriteWorkbookRelationships(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/package/2006/relationships";

                    writer.WriteStartElement("Relationships", ns);
                    WriteRelationship(
                        writer,
                        ns,
                        "rId1",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                        "worksheets/sheet1.xml");
                    WriteRelationship(
                        writer,
                        ns,
                        "rId2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
                        "styles.xml");
                    writer.WriteEndElement();
                });
        }


        private static void WriteRelationship(
            XmlWriter writer,
            string ns,
            string id,
            string type,
            string target)
        {
            writer.WriteStartElement("Relationship", ns);
            writer.WriteAttributeString("Id", id);
            writer.WriteAttributeString("Type", type);
            writer.WriteAttributeString("Target", target);
            writer.WriteEndElement();
        }


        private static void WriteExcelStyles(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "xl/styles.xml",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                    writer.WriteStartElement("styleSheet", ns);

                    writer.WriteStartElement("numFmts", ns);
                    writer.WriteAttributeString("count", "1");
                    writer.WriteStartElement("numFmt", ns);
                    writer.WriteAttributeString("numFmtId", "164");
                    writer.WriteAttributeString("formatCode", "0.000");
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement("fonts", ns);
                    writer.WriteAttributeString("count", "2");
                    WriteExcelFont(writer, ns, false);
                    WriteExcelFont(writer, ns, true);
                    writer.WriteEndElement();

                    int heatFillCount =
                        ExcelHeatStyleSteps + 1;

                    writer.WriteStartElement("fills", ns);
                    writer.WriteAttributeString(
                        "count",
                        (heatFillCount + 2).ToString(
                            CultureInfo.InvariantCulture));
                    WritePatternFill(writer, ns, "none", null);
                    WritePatternFill(writer, ns, "gray125", null);

                    for (int i = 0;
                         i <= ExcelHeatStyleSteps;
                         i++)
                    {
                        double value =
                            RedDeviationThreshold *
                            i /
                            ExcelHeatStyleSteps;

                        (byte r, byte g, byte b) =
                            GetPastelRgb(value);

                        WritePatternFill(
                            writer,
                            ns,
                            "solid",
                            $"FF{r:X2}{g:X2}{b:X2}");
                    }

                    writer.WriteEndElement();

                    writer.WriteStartElement("borders", ns);
                    writer.WriteAttributeString("count", "2");
                    WriteExcelBorder(writer, ns, false);
                    WriteExcelBorder(writer, ns, true);
                    writer.WriteEndElement();

                    writer.WriteStartElement("cellStyleXfs", ns);
                    writer.WriteAttributeString("count", "1");
                    WriteExcelXf(writer, ns, 0, 0, 0, 0, false);
                    writer.WriteEndElement();

                    writer.WriteStartElement("cellXfs", ns);
                    writer.WriteAttributeString(
                        "count",
                        (heatFillCount + 2).ToString(
                            CultureInfo.InvariantCulture));
                    WriteExcelXf(writer, ns, 0, 0, 0, 0, false);
                    WriteExcelXf(writer, ns, 0, 1, 0, 1, false);

                    for (int i = 0;
                         i <= ExcelHeatStyleSteps;
                         i++)
                    {
                        WriteExcelXf(
                            writer,
                            ns,
                            164,
                            0,
                            i + 2,
                            1,
                            true);
                    }

                    writer.WriteEndElement();

                    writer.WriteStartElement("cellStyles", ns);
                    writer.WriteAttributeString("count", "1");
                    writer.WriteStartElement("cellStyle", ns);
                    writer.WriteAttributeString("name", "Normal");
                    writer.WriteAttributeString("xfId", "0");
                    writer.WriteAttributeString("builtinId", "0");
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                });
        }


        private static void WriteExcelFont(
            XmlWriter writer,
            string ns,
            bool bold)
        {
            writer.WriteStartElement("font", ns);
            if (bold)
                writer.WriteElementString("b", ns, "");
            writer.WriteStartElement("sz", ns);
            writer.WriteAttributeString("val", "11");
            writer.WriteEndElement();
            writer.WriteStartElement("name", ns);
            writer.WriteAttributeString("val", "Calibri");
            writer.WriteEndElement();
            writer.WriteEndElement();
        }


        private static void WritePatternFill(
            XmlWriter writer,
            string ns,
            string patternType,
            string? rgb)
        {
            writer.WriteStartElement("fill", ns);
            writer.WriteStartElement("patternFill", ns);
            writer.WriteAttributeString("patternType", patternType);

            if (rgb != null)
            {
                writer.WriteStartElement("fgColor", ns);
                writer.WriteAttributeString("rgb", rgb);
                writer.WriteEndElement();
                writer.WriteStartElement("bgColor", ns);
                writer.WriteAttributeString("indexed", "64");
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }


        private static void WriteExcelBorder(
            XmlWriter writer,
            string ns,
            bool thin)
        {
            writer.WriteStartElement("border", ns);

            foreach (string side in
                     new[] { "left", "right", "top", "bottom" })
            {
                writer.WriteStartElement(side, ns);

                if (thin)
                {
                    writer.WriteAttributeString("style", "thin");
                    writer.WriteStartElement("color", ns);
                    writer.WriteAttributeString("rgb", "FFD9D9D9");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteElementString("diagonal", ns, "");
            writer.WriteEndElement();
        }


        private static void WriteExcelXf(
            XmlWriter writer,
            string ns,
            int numberFormatId,
            int fontId,
            int fillId,
            int borderId,
            bool applyNumberFormat)
        {
            writer.WriteStartElement("xf", ns);
            writer.WriteAttributeString(
                "numFmtId",
                numberFormatId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "fontId",
                fontId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "fillId",
                fillId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "borderId",
                borderId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("xfId", "0");
            writer.WriteAttributeString("applyFill", "1");
            writer.WriteAttributeString("applyBorder", "1");

            if (applyNumberFormat)
                writer.WriteAttributeString("applyNumberFormat", "1");

            writer.WriteEndElement();
        }


        private void WriteExcelWorksheet(
            ZipArchive archive)
        {
            WriteXmlEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                writer =>
                {
                    const string ns =
                        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                    writer.WriteStartElement("worksheet", ns);

                    writer.WriteStartElement("sheetViews", ns);
                    writer.WriteStartElement("sheetView", ns);
                    writer.WriteAttributeString("workbookViewId", "0");
                    writer.WriteStartElement("pane", ns);
                    writer.WriteAttributeString("xSplit", "1");
                    writer.WriteAttributeString("ySplit", "1");
                    writer.WriteAttributeString("topLeftCell", "B2");
                    writer.WriteAttributeString("activePane", "bottomRight");
                    writer.WriteAttributeString("state", "frozen");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement("cols", ns);
                    writer.WriteStartElement("col", ns);
                    writer.WriteAttributeString("min", "1");
                    writer.WriteAttributeString("max", "1");
                    writer.WriteAttributeString("width", "14");
                    writer.WriteAttributeString("customWidth", "1");
                    writer.WriteEndElement();
                    writer.WriteStartElement("col", ns);
                    writer.WriteAttributeString("min", "2");
                    writer.WriteAttributeString(
                        "max",
                        (tablePowerBins + 1).ToString(
                            CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("width", "13");
                    writer.WriteAttributeString("customWidth", "1");
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement("sheetData", ns);

                    writer.WriteStartElement("row", ns);
                    writer.WriteAttributeString("r", "1");
                    WriteInlineStringCell(
                        writer,
                        ns,
                        "A1",
                        xAxisTitle,
                        1);

                    for (int p = 0;
                         p < tablePowerBins;
                         p++)
                    {
                        WriteInlineStringCell(
                            writer,
                            ns,
                            $"{GetExcelColumnName(p + 2)}1",
                            $"{yAxisTitle}: {GetTablePowerValue(p):0.###}",
                            1);
                    }

                    writer.WriteEndElement();

                    for (int t = 0;
                         t < tableTempBins;
                         t++)
                    {
                        int rowNumber = t + 2;

                        writer.WriteStartElement("row", ns);
                        writer.WriteAttributeString(
                            "r",
                            rowNumber.ToString(
                                CultureInfo.InvariantCulture));

                        WriteNumericCell(
                            writer,
                            ns,
                            $"A{rowNumber}",
                            GetTableTempValue(t),
                            1);

                        for (int p = 0;
                             p < tablePowerBins;
                             p++)
                        {
                            if (tableCells[t, p].TotalWeightSeconds <= 0)
                                continue;

                            double rms =
                                tableCells[t, p].Rms;

                            int heatIndex =
                                (int)Math.Round(
                                    Math.Clamp(
                                        rms /
                                        RedDeviationThreshold,
                                        0,
                                        1) *
                                    ExcelHeatStyleSteps);

                            WriteNumericCell(
                                writer,
                                ns,
                                $"{GetExcelColumnName(p + 2)}{rowNumber}",
                                rms,
                                heatIndex + 2);
                        }

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
        }


        private static void WriteInlineStringCell(
            XmlWriter writer,
            string ns,
            string reference,
            string value,
            int styleIndex)
        {
            writer.WriteStartElement("c", ns);
            writer.WriteAttributeString("r", reference);
            writer.WriteAttributeString(
                "s",
                styleIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", ns);
            writer.WriteElementString("t", ns, value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }


        private static void WriteNumericCell(
            XmlWriter writer,
            string ns,
            string reference,
            double value,
            int styleIndex)
        {
            writer.WriteStartElement("c", ns);
            writer.WriteAttributeString("r", reference);
            writer.WriteAttributeString(
                "s",
                styleIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString(
                "v",
                ns,
                value.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }


        private static string GetExcelColumnName(
            int oneBasedIndex)
        {
            StringBuilder name = new StringBuilder();
            int value = oneBasedIndex;

            while (value > 0)
            {
                value--;
                name.Insert(0, (char)('A' + value % 26));
                value /= 26;
            }

            return name.ToString();
        }


        private static void WriteXmlEntry(
            ZipArchive archive,
            string path,
            Action<XmlWriter> writeBody)
        {
            ZipArchiveEntry entry =
                archive.CreateEntry(
                    path,
                    CompressionLevel.Optimal);

            using Stream stream = entry.Open();
            using XmlWriter writer =
                XmlWriter.Create(
                    stream,
                    new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(false),
                        Indent = true
                    });

            writer.WriteStartDocument();
            writeBody(writer);
            writer.WriteEndDocument();
        }


        private FlowDocument CreatePrintDocument(
            double pageWidth,
            double pageHeight)
        {
            FlowDocument document =
                new FlowDocument
                {
                    PageWidth = pageWidth,
                    PageHeight = pageHeight,
                    PagePadding =
                        new Thickness(25),

                    ColumnWidth =
                        pageWidth,

                    FontFamily =
                        new System.Windows.Media.FontFamily(
                            "Segoe UI"),

                    FontSize =
                        9
                };


            int powerColumnsPerPage =
                Math.Max(
                    1,
                    (int)((pageWidth - 135) / 75));


            int temperatureRowsPerPage =
                Math.Max(
                    1,
                    (int)((pageHeight - 210) / 24));


            for (int firstPower = 0;
                 firstPower < tablePowerBins;
                 firstPower += powerColumnsPerPage)
            {
                int lastPower =
                    Math.Min(
                        tablePowerBins,
                        firstPower + powerColumnsPerPage);


                Paragraph title =
                    new Paragraph(
                        new Run(
                            "Матрица RMS отклонения оборотов"))
                    {
                        FontSize = 16,
                        FontWeight = System.Windows.FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        BreakPageBefore = firstPower > 0
                    };


                document.Blocks.Add(title);


                document.Blocks.Add(
                    new Paragraph(
                        new Run(
                            $"Источник: {lastSource}")));


                document.Blocks.Add(
                    new Paragraph(
                        new Run(
                            $"Номинальные обороты: {nominalRpm:F0} rpm | " +
                            $"Max deviation: {maxAllowedDeviation:F0} rpm | " +
                            $"взвешенный период: {TimeSpan.FromSeconds(weightedSeconds):c}")));


                document.Blocks.Add(
                    new Paragraph(
                        new Run(
                            $"Сетка таблицы: {xAxisTitle} " +
                            $"{tableTempMin:F2}...{tableTempMax:F2}, " +
                            $"шаг {tableTempStep:F2} | {yAxisTitle} " +
                            $"{tablePowerMin:F2}...{tablePowerMax:F2}, " +
                            $"шаг {tablePowerStep:F2}")));


                for (int firstTemp = 0;
                     firstTemp < tableTempBins;
                     firstTemp += temperatureRowsPerPage)
                {
                    if (firstTemp > 0)
                    {
                        document.Blocks.Add(
                            new Paragraph(
                                new Run(
                                    "Матрица RMS (продолжение)"))
                            {
                                FontWeight = System.Windows.FontWeights.Bold,
                                BreakPageBefore = true
                            });
                    }


                    var table =
                        new System.Windows.Documents.Table
                        {
                            CellSpacing = 0
                        };


                    table.Columns.Add(
                        new TableColumn
                        {
                            Width = new GridLength(85)
                        });


                    for (int p = firstPower;
                         p < lastPower;
                         p++)
                    {
                        table.Columns.Add(
                            new TableColumn
                            {
                                Width = new GridLength(75)
                            });
                    }


                    TableRowGroup group =
                        new TableRowGroup();


                    TableRow header =
                        new TableRow();


                    AddPrintCell(
                        header,
                        xAxisTitle,
                        true);


                    for (int p = firstPower;
                         p < lastPower;
                         p++)
                    {
                        AddPrintCell(
                            header,
                            $"{GetTablePowerValue(p):0.###}",
                            true);
                    }


                    group.Rows.Add(header);


                    int lastTemp =
                        Math.Min(
                            tableTempBins,
                            firstTemp + temperatureRowsPerPage);


                    for (int t = firstTemp;
                         t < lastTemp;
                         t++)
                    {
                        TableRow row =
                            new TableRow();


                        AddPrintCell(
                            row,
                            GetTableTempValue(t)
                                .ToString(
                                    "0.###",
                                    CultureInfo.CurrentCulture),
                            true);


                        for (int p = firstPower;
                             p < lastPower;
                             p++)
                        {
                            AddPrintCell(
                                row,
                                tableCells[t, p].TotalWeightSeconds <= 0
                                    ? "—"
                                    : tableCells[t, p].Rms.ToString(
                                        "F3",
                                        CultureInfo.CurrentCulture));
                        }


                        group.Rows.Add(row);
                    }


                    table.RowGroups.Add(group);
                    document.Blocks.Add(table);
                }
            }


            return document;
        }


        private static void AddPrintCell(
            TableRow row,
            string text,
            bool bold = false)
        {
            Paragraph paragraph =
                new Paragraph(
                    new Run(text))
                {
                    Margin =
                        new Thickness(4)
                };


            if (bold)
            {
                paragraph.FontWeight =
                    System.Windows.FontWeights.Bold;
            }


            TableCell cell =
                new TableCell(
                    paragraph)
                {
                    BorderBrush =
                        System.Windows.Media.Brushes.Gray,

                    BorderThickness =
                        new Thickness(0.5)
                };


            row.Cells.Add(
                cell);
        }


        // ============================================================
        // 3D COORDINATES
        // ============================================================

        private ScenePoint ToScene(
            double temperature,
            double power,
            double deviation)
        {
            double x =
                Map(
                    temperature,
                    graphTempMin,
                    graphTempMax,
                    -1,
                    1);


            double y =
                Map(
                    deviation,
                    0,
                    deviationMax,
                    -1,
                    1);


            double z =
                Map(
                    power,
                    graphPowerMin,
                    graphPowerMax,
                    0,
                    1.7);


            return new ScenePoint(
                x,
                y,
                z);
        }


        private ProjectedPoint ProjectPoint(
            double x,
            double y,
            double z)
        {
            double radX =
                angleX *
                Math.PI /
                180.0;

            double radY =
                angleY *
                Math.PI /
                180.0;


            double y1 =
                y *
                Math.Cos(radX) -
                z *
                Math.Sin(radX);


            double z1 =
                y *
                Math.Sin(radX) +
                z *
                Math.Cos(radX);


            double x2 =
                x *
                Math.Cos(radY) +
                z1 *
                Math.Sin(radY);


            double depth =
                -x *
                Math.Sin(radY) +
                z1 *
                Math.Cos(radY);


            const double cameraDistance =
                5.0;


            double perspective =
                cameraDistance /
                (cameraDistance - depth);


            return new ProjectedPoint(
                x2 *
                    perspective *
                    zoom,

                y1 *
                    perspective *
                    zoom,

                depth);
        }


        // ============================================================
        // DRAW
        // ============================================================

        private void Draw3D()
        {
            plotModel.Series.Clear();
            plotModel.Annotations.Clear();

            plotModel.Title =
                "Time-weighted RMS Engine Speed Deviation";

            plotModel.Subtitle =
                "Цветовая шкала: 0 — зелёный, 12 rpm и выше — красный";

            DrawSurface();

            DrawCoordinateSystem();

            plotModel.ResetAllAxes();
            Plot.InvalidatePlot(true);
        }


        private void DrawSurface()
        {
            List<RenderSurfaceCell> renderCells =
                new List<RenderSurfaceCell>();


            for (int t = 0;
                 t < graphTempBins - 1;
                 t++)
            {
                for (int p = 0;
                     p < graphPowerBins - 1;
                     p++)
                {
                    if (!surfaceValid[t, p] ||
                        !surfaceValid[t + 1, p] ||
                        !surfaceValid[t, p + 1] ||
                        !surfaceValid[
                            t + 1,
                            p + 1])
                    {
                        continue;
                    }


                    double temp1 =
                        GetGraphTempValue(t);

                    double temp2 =
                        GetGraphTempValue(t + 1);

                    double power1 =
                        GetGraphPowerValue(p);

                    double power2 =
                        GetGraphPowerValue(p + 1);


                    ScenePoint s00 =
                        ToScene(
                            temp1,
                            power1,
                            surface[t, p]);


                    ScenePoint s10 =
                        ToScene(
                            temp2,
                            power1,
                            surface[t + 1, p]);


                    ScenePoint s11 =
                        ToScene(
                            temp2,
                            power2,
                            surface[
                                t + 1,
                                p + 1]);


                    ScenePoint s01 =
                        ToScene(
                            temp1,
                            power2,
                            surface[t, p + 1]);


                    ProjectedPoint p00 =
                        ProjectPoint(
                            s00.X,
                            s00.Y,
                            s00.Z);

                    ProjectedPoint p10 =
                        ProjectPoint(
                            s10.X,
                            s10.Y,
                            s10.Z);

                    ProjectedPoint p11 =
                        ProjectPoint(
                            s11.X,
                            s11.Y,
                            s11.Z);

                    ProjectedPoint p01 =
                        ProjectPoint(
                            s01.X,
                            s01.Y,
                            s01.Z);


                    double depth =
                        (
                            p00.Depth +
                            p10.Depth +
                            p11.Depth +
                            p01.Depth
                        ) / 4.0;


                    double value =
                        (
                            surface[t, p] +
                            surface[t + 1, p] +
                            surface[
                                t + 1,
                                p + 1] +
                            surface[t, p + 1]
                        ) / 4.0;


                    renderCells.Add(
                        new RenderSurfaceCell
                        {
                            P00 = p00,
                            P10 = p10,
                            P11 = p11,
                            P01 = p01,
                            Depth = depth,
                            Value = value
                        });
                }
            }


            foreach (
                RenderSurfaceCell cell
                in renderCells
                    .OrderBy(
                        x => x.Depth))
            {
                DrawSurfaceCell(
                    cell);
            }
        }


        private void DrawSurfaceCell(
            RenderSurfaceCell cell)
        {
            PolygonAnnotation polygon =
                new PolygonAnnotation
                {
                    Fill = GetSurfaceColor(cell.Value),
                    Stroke = OxyColor.FromRgb(40, 40, 40),
                    StrokeThickness = 0.3,
                    Layer = AnnotationLayer.BelowSeries
                };


            polygon.Points.Add(
                new DataPoint(cell.P00.X, cell.P00.Y));
            polygon.Points.Add(
                new DataPoint(cell.P10.X, cell.P10.Y));
            polygon.Points.Add(
                new DataPoint(cell.P11.X, cell.P11.Y));
            polygon.Points.Add(
                new DataPoint(cell.P01.X, cell.P01.Y));


            plotModel.Annotations.Add(polygon);
        }


        private OxyColor GetSurfaceColor(
            double value)
        {
            (byte r, byte g, byte b) =
                GetPastelRgb(value);

            return OxyColor.FromRgb(r, g, b);
        }


        private static (byte R, byte G, byte B) GetPastelRgb(
            double value)
        {
            double t =
                Math.Clamp(
                    value / RedDeviationThreshold,
                    0,
                    1);

            (byte R, byte G, byte B) green =
                (183, 228, 199);

            (byte R, byte G, byte B) yellow =
                (255, 241, 184);

            (byte R, byte G, byte B) red =
                (244, 182, 182);

            return t <= 0.5
                ? InterpolateColor(
                    green,
                    yellow,
                    t * 2)
                : InterpolateColor(
                    yellow,
                    red,
                    (t - 0.5) * 2);
        }


        private static (byte R, byte G, byte B) InterpolateColor(
            (byte R, byte G, byte B) from,
            (byte R, byte G, byte B) to,
            double amount)
        {
            byte Mix(byte a, byte b) =>
                (byte)Math.Round(
                    a +
                    (b - a) * amount);

            return (
                Mix(from.R, to.R),
                Mix(from.G, to.G),
                Mix(from.B, to.B));
        }


        // ============================================================
        // AXES
        // ============================================================

        private void DrawCoordinateSystem()
        {
            DrawXAxis();
            DrawYAxis();
            DrawZAxis();
        }


        private void DrawXAxis()
        {
            ScenePoint start =
                ToScene(
                    graphTempMin,
                    graphPowerMin,
                    0);


            ScenePoint end =
                ToScene(
                    graphTempMax,
                    graphPowerMin,
                    0);


            Draw3DLine(
                start,
                end,
                2);


            double tickStep =
                Math.Max(
                    1,
                    Math.Round(
                        (graphTempMax - graphTempMin) /
                        10));


            for (double temp =
                     graphTempMin;
                 temp <= graphTempMax;
                 temp += tickStep)
            {
                ScenePoint tick =
                    ToScene(
                        temp,
                        graphPowerMin,
                        0);


                Add3DText(
                    temp.ToString("0.#"),
                    tick,
                    -0.04,
                    -0.08);
            }


            Add3DText(
                xAxisTitle,
                end,
                0.10,
                -0.10);
        }


        private void DrawYAxis()
        {
            ScenePoint start =
                ToScene(
                    graphTempMin,
                    graphPowerMin,
                    0);


            ScenePoint end =
                ToScene(
                    graphTempMin,
                    graphPowerMin,
                    deviationMax);


            Draw3DLine(
                start,
                end,
                2);


            const int tickCount =
                5;


            for (int i = 0;
                 i <= tickCount;
                 i++)
            {
                double value =
                    deviationMax *
                    i /
                    tickCount;


                ScenePoint tick =
                    ToScene(
                        graphTempMin,
                        graphPowerMin,
                        value);


                Add3DText(
                    value.ToString("0.00"),
                    tick,
                    -0.12,
                    0);
            }


            Add3DText(
                "RMS Speed Deviation, rpm",
                end,
                -0.15,
                0.08);
        }


        private void DrawZAxis()
        {
            ScenePoint start =
                ToScene(
                    graphTempMin,
                    graphPowerMin,
                    0);


            ScenePoint end =
                ToScene(
                    graphTempMin,
                    graphPowerMax,
                    0);


            Draw3DLine(
                start,
                end,
                2);


            double tickStep =
                Math.Max(
                    1,
                    Math.Round(
                        (graphPowerMax - graphPowerMin) /
                        10));


            for (double power =
                     graphPowerMin;
                 power <= graphPowerMax;
                 power += tickStep)
            {
                ScenePoint tick =
                    ToScene(
                        graphTempMin,
                        power,
                        0);


                Add3DText(
                    power.ToString("0"),
                    tick,
                    -0.10,
                    0);
            }


            Add3DText(
                yAxisTitle,
                end,
                -0.10,
                0.10);
        }


        private void Draw3DLine(
            ScenePoint a,
            ScenePoint b,
            float width)
        {
            ProjectedPoint p1 =
                ProjectPoint(
                    a.X,
                    a.Y,
                    a.Z);


            ProjectedPoint p2 =
                ProjectPoint(
                    b.X,
                    b.Y,
                    b.Z);


            LineSeries line =
                new LineSeries
                {
                    MarkerType = MarkerType.None,
                    StrokeThickness = width,
                    Color = OxyColors.Black
                };


            line.Points.Add(
                new DataPoint(p1.X, p1.Y));
            line.Points.Add(
                new DataPoint(p2.X, p2.Y));


            plotModel.Series.Add(line);
        }


        private void Add3DText(
            string text,
            ScenePoint position,
            double offsetX,
            double offsetY)
        {
            ProjectedPoint p =
                ProjectPoint(
                    position.X,
                    position.Y,
                    position.Z);


            plotModel.Annotations.Add(
                new TextAnnotation
                {
                    Text = text,
                    TextPosition =
                        new DataPoint(
                            p.X + offsetX,
                            p.Y + offsetY),
                    Stroke = OxyColors.Transparent,
                    TextColor = OxyColors.Black,
                    FontSize = 11,
                    TextHorizontalAlignment =
                        OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment =
                        OxyPlot.VerticalAlignment.Middle
                });
        }


        // ============================================================
        // CAMERA
        // ============================================================

        private void Plot_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            isDragging = true;

            lastMousePosition =
                e.GetPosition(Plot);

            Plot.CaptureMouse();

            e.Handled = true;
        }


        private void Plot_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            isDragging = false;

            Plot.ReleaseMouseCapture();

            e.Handled = true;
        }


        private void Plot_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!isDragging)
                return;


            System.Windows.Point current =
                e.GetPosition(Plot);


            double dx =
                current.X -
                lastMousePosition.X;

            double dy =
                current.Y -
                lastMousePosition.Y;


            angleY +=
                dx * 0.5;

            angleX -=
                dy * 0.5;


            angleX =
                Math.Clamp(
                    angleX,
                    -85,
                    85);


            lastMousePosition =
                current;


            Draw3D();

            e.Handled = true;
        }


        private void Plot_MouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                zoom *= 1.10;
            else
                zoom /= 1.10;


            zoom =
                Math.Clamp(
                    zoom,
                    0.5,
                    4);


            Draw3D();

            e.Handled = true;
        }


        private void ResetView_Click(
            object sender,
            RoutedEventArgs e)
        {
            angleX = 25;
            angleY = -35;
            zoom = 1;

            Draw3D();
        }


        private async void Update_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (lastSourceKind == SourceKind.None)
            {
                MessageBox.Show(
                    "Сначала загрузите CSV или данные PostgreSQL.",
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }


            try
            {
                SetBusy(true);
                ConfigureGrid();

                bool sourceReloaded = false;

                if (lastSourceKind == SourceKind.Csv)
                {
                    CsvColumnMapping mapping =
                        GetCsvColumnMapping();

                    string signature =
                        GetCsvMappingSignature(mapping);

                    if (!string.Equals(
                            signature,
                            loadedCsvMappingSignature,
                            StringComparison.Ordinal))
                    {
                        ClearData();

                        long totalBytes =
                            lastCsvFiles.Sum(path =>
                                new FileInfo(path).Length);

                        await Task.Run(() =>
                            LoadCsvFiles(
                                lastCsvFiles,
                                totalBytes,
                                mapping));

                        loadedCsvMappingSignature =
                            signature;

                        sourceReloaded = true;
                    }
                }
                else if (lastSourceKind == SourceKind.PostgreSql)
                {
                    ConfigurePostgresTags();

                    string signature =
                        GetPostgresMappingSignature();

                    if (!string.Equals(
                            signature,
                            loadedPostgresMappingSignature,
                            StringComparison.Ordinal))
                    {
                        DateTime from =
                            ParseDateTime(
                                PgFromTextBox.Text,
                                "Дата начала");

                        DateTime to =
                            ParseDateTime(
                                PgToTextBox.Text,
                                "Дата окончания");

                        if (to <= from)
                        {
                            throw new Exception(
                                "Дата окончания должна быть позже даты начала.");
                        }

                        string connectionString =
                            BuildPostgresConnectionString();

                        lastSource =
                            $"PostgreSQL: {from:yyyy-MM-dd HH:mm:ss} — " +
                            $"{to:yyyy-MM-dd HH:mm:ss}";

                        ClearData();
                        Progress.IsIndeterminate = true;

                        await Task.Run(async () =>
                            await LoadFromPostgres(
                                connectionString,
                                from,
                                to));

                        loadedPostgresMappingSignature =
                            signature;

                        sourceReloaded = true;
                    }
                }

                FinishProcessing();

                StatusText.Text =
                    (sourceReloaded
                        ? "Источник перечитан | "
                        : "Обновлено из кэша | ") +
                    StatusText.Text;
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                SetBusy(false);
            }
        }


        // ============================================================
        // UI
        // ============================================================

        private void SetBusy(
            bool busy)
        {
            OpenCsvButton.IsEnabled =
                !busy;

            PostgresButton.IsEnabled =
                !busy;

            UpdateButton.IsEnabled =
                !busy;

            if (!busy)
            {
                Progress.IsIndeterminate =
                    false;
            }
        }


        private void ReportProgress(
            double percent,
            string message)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    Progress.IsIndeterminate =
                        false;

                    Progress.Value =
                        Math.Clamp(
                            percent,
                            0,
                            100);

                    StatusText.Text =
                        message;
                });
        }


        private void ReportStatus(
            string message)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    StatusText.Text =
                        message;
                });
        }


        private void ShowError(
            Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Ошибка";
        }


        // ============================================================
        // HELPERS
        // ============================================================

        private static int GetBinCount(
            double minimum,
            double maximum,
            double step)
        {
            return
                (int)Math.Ceiling(
                    (maximum - minimum) /
                    step) + 1;
        }


        private static double ParseSetting(
            string text,
            string parameterName)
        {
            if (TryParseDouble(
                text,
                out double value) &&
                !double.IsNaN(value) &&
                !double.IsInfinity(value))
            {
                return value;
            }


            throw new Exception(
                $"Неверное значение: {parameterName}");
        }


        private static string GetRequiredText(
            string text,
            string parameterName)
        {
            string value = text.Trim();

            if (value.Length == 0)
            {
                throw new Exception(
                    $"Не заполнено поле: {parameterName}");
            }

            return value;
        }


        private static int ParseTagId(
            string text,
            string parameterName)
        {
            if (int.TryParse(
                    text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value) &&
                value > 0)
            {
                return value;
            }

            throw new Exception(
                $"{parameterName}: ожидается положительное целое число.");
        }


        private static DateTime ParseCsvDateTime(
            string text,
            string filePath,
            long lineNumber)
        {
            string value =
                text.Trim().Trim('"');


            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out DateTimeOffset invariantResult) ||
                DateTimeOffset.TryParse(
                    value,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out invariantResult))
            {
                return invariantResult.UtcDateTime;
            }


            throw new FormatException(
                $"{Path.GetFileName(filePath)}, строка {lineNumber}: " +
                $"не удалось прочитать Date '{value}'.");
        }


        private static DateTime ParseDateTime(
            string text,
            string parameterName)
        {
            if (DateTime.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime value))
            {
                return value;
            }


            if (DateTime.TryParse(
                text,
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out value))
            {
                return value;
            }


            throw new Exception(
                $"{parameterName}: ожидается " +
                "yyyy-MM-dd HH:mm:ss");
        }


        private static int FindTemperatureColumn(
            string[] headers)
        {
            int index =
                FindColumn(
                    headers,
                    "Themp Outdoor");


            if (index < 0)
            {
                index =
                    FindColumn(
                        headers,
                        "Temp Outdoor");
            }


            if (index < 0)
            {
                index =
                    FindColumn(
                        headers,
                        "Temp Outdor");
            }


            return index;
        }


        private static string[] SplitCsvLine(
            string line,
            char delimiter)
        {
            List<string> fields =
                new List<string>();


            StringBuilder field =
                new StringBuilder();


            bool quoted = false;


            for (int i = 0;
                 i < line.Length;
                 i++)
            {
                char current = line[i];


                if (current == '"')
                {
                    if (quoted &&
                        i + 1 < line.Length &&
                        line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (current == delimiter &&
                         !quoted)
                {
                    fields.Add(
                        field.ToString().Trim());
                    field.Clear();
                }
                else
                {
                    field.Append(current);
                }
            }


            if (quoted)
            {
                throw new FormatException(
                    "В CSV обнаружена незакрытая кавычка. " +
                    "Многострочные поля не поддерживаются.");
            }


            fields.Add(
                field.ToString().Trim());


            return fields.ToArray();
        }


        private static int FindColumn(
            string[] headers,
            string name)
        {
            for (int i = 0;
                 i < headers.Length;
                 i++)
            {
                if (headers[i]
                    .Trim()
                    .Equals(
                        name,
                        StringComparison
                            .OrdinalIgnoreCase))
                {
                    return i;
                }
            }


            return -1;
        }


        private static char DetectDelimiter(
            string header)
        {
            int semicolon =
                header.Count(
                    c => c == ';');

            int comma =
                header.Count(
                    c => c == ',');

            int tab =
                header.Count(
                    c => c == '\t');


            if (semicolon >= comma &&
                semicolon >= tab)
            {
                return ';';
            }


            if (tab >= comma)
                return '\t';


            return ',';
        }


        private static bool TryParseDouble(
            string value,
            out double result)
        {
            value =
                value
                    .Trim()
                    .Trim('"');


            if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result))
            {
                return true;
            }


            if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out result))
            {
                return true;
            }


            string normalized =
                value.Replace(
                    ',',
                    '.');


            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }


        private static double Map(
            double value,
            double sourceMin,
            double sourceMax,
            double targetMin,
            double targetMax)
        {
            if (sourceMax ==
                sourceMin)
            {
                return targetMin;
            }


            return
                targetMin +
                (
                    (value - sourceMin) /
                    (sourceMax - sourceMin)
                ) *
                (
                    targetMax -
                    targetMin
                );
        }
    }


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
