namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
    }
}
