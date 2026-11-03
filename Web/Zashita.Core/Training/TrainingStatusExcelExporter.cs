using System.Globalization;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training;

/// <summary>Выгрузка отчёта / архива обучения в Excel (.xlsx).</summary>
public static class TrainingStatusExcelExporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);

    public static byte[] Export(
        IReadOnlyList<TrainingStatusRow> rows,
        string organization,
        bool forArchive,
        IReadOnlyDictionary<string, DateTime?>? dismissalByEmployee = null,
        IReadOnlySet<string>? transferArchiveKeys = null)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet(forArchive ? "Архив обучений" : "Отчёт по обучению");

        var headerStyle = CreateHeaderStyle(workbook);
        var cellStyle = CreateCellStyle(workbook);

        var headers = forArchive
            ? new[]
            {
                "Организация", "Подразделение", "ФИО", "Должность",
                "Увольнение / перевод", "Дата увольнения / перевода",
                "Программа", "Периодичность", "Номер протокола",
                "Дата обучения", "Дата следующего", "Движение протокола",
                "УПД", "Комментарий", "Объект", "Статус"
            }
            : new[]
            {
                "Организация", "Подразделение", "ФИО", "Должность",
                "Программа", "Периодичность", "Номер протокола",
                "Дата обучения", "Дата следующего", "Движение протокола",
                "УПД", "Комментарий", "Объект", "Статус"
            };

        var headerRow = sheet.CreateRow(0);
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = headerRow.CreateCell(c);
            cell.SetCellValue(headers[c]);
            cell.CellStyle = headerStyle;
        }

        var titleOrg = (organization ?? string.Empty).Trim();
        var r = 1;
        foreach (var row in rows)
        {
            var excelRow = sheet.CreateRow(r++);
            var col = 0;
            void Write(string? value)
            {
                var cell = excelRow.CreateCell(col++);
                cell.SetCellValue(Plain(value));
                cell.CellStyle = cellStyle;
            }

            Write(string.IsNullOrWhiteSpace(row.Organization) ? titleOrg : row.Organization);
            Write(row.Department);
            Write(row.Person);
            Write(row.Profession);

            if (forArchive)
            {
                var key = EmployeeKey(row.Organization, row.Person, row.Profession);
                var isTransfer = transferArchiveKeys is not null && transferArchiveKeys.Contains(key);
                Write(isTransfer ? "Перевод" : "Увольнение");
                DateTime? when = null;
                if (dismissalByEmployee is not null)
                {
                    if (!dismissalByEmployee.TryGetValue(key, out when))
                    {
                        var personKey = $"{(row.Organization ?? string.Empty).Trim()}|{(row.Person ?? string.Empty).Trim()}";
                        dismissalByEmployee.TryGetValue(personKey, out when);
                    }
                }

                Write(when?.ToString("dd.MM.yyyy", Ru) ?? string.Empty);
            }

            Write(row.Program);
            Write(row.Periodicity);
            // Только номер протокола — не подставлять Traffic/статус (ломает обратный импорт).
            Write(row.CertificateNumber);
            Write(row.TrainingDate);
            Write(row.NextTrainingDate);
            Write(row.ProtocolMovement);
            Write(row.UpdStatus);
            Write(row.Comment);
            Write(row.ObjectNote);
            Write(string.IsNullOrWhiteSpace(row.Status) ? row.Result : row.Status);
        }

        // Фиксированные ширины: AutoSizeColumn тянет SkiaSharp и падает на Linux.
        int[] widths =
        [
            5000, 4500, 5500, 4500,
            5500, 3500, 4500,
            3500, 3500, 4000,
            3000, 5000, 4000, 3500
        ];
        if (forArchive)
        {
            widths =
            [
                5000, 4500, 5500, 4500,
                4000, 3500,
                5500, 3500, 4500,
                3500, 3500, 4000,
                3000, 5000, 4000, 3500
            ];
        }

        for (var c = 0; c < headers.Length && c < widths.Length; c++)
        {
            sheet.SetColumnWidth(c, widths[c]);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    public static string BuildFileName(string organization, bool forArchive, DateTime asOf)
    {
        var org = SanitizeFilePart(organization);
        var kind = forArchive ? "архив-обучений" : "отчет-по-обучению";
        return $"{kind}_{org}_{asOf:yyyy-MM-dd}.xlsx";
    }

    private static string EmployeeKey(string? organization, string? person, string? profession)
    {
        var prof = (profession ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prof) || prof == "—")
        {
            prof = "—";
        }

        return $"{(organization ?? string.Empty).Trim()}|{(person ?? string.Empty).Trim()}|{prof}";
    }

    private static string Plain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = TagRx.Replace(value, " ");
        t = System.Net.WebUtility.HtmlDecode(t);
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SanitizeFilePart(string? value)
    {
        var t = (value ?? "организация").Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            t = t.Replace(ch, '_');
        }

        return t.Length == 0 ? "организация" : t;
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        var font = workbook.CreateFont();
        font.IsBold = true;
        style.SetFont(font);
        style.WrapText = true;
        style.VerticalAlignment = VerticalAlignment.Center;
        return style;
    }

    private static ICellStyle CreateCellStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        style.WrapText = true;
        style.VerticalAlignment = VerticalAlignment.Top;
        return style;
    }
}
