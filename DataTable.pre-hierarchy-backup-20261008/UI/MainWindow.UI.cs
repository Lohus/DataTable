namespace EngineAnalyzer
{
    public partial class MainWindow
    {
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
}
