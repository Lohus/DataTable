namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
    }
}
