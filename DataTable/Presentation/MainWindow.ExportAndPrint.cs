namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
    }
}
