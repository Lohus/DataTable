namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
    }
}
