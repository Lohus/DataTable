namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
    }
}
