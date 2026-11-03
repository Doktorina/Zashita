using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training;

/// <summary>Выгрузка стандартов обучения организации в Excel (.xlsx).</summary>
public static class TrainingStandardsExcelExporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static byte[] Export(IReadOnlyList<TrainingStandard> standards, string organization)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Стандарты обучения");
        var headerStyle = CreateHeaderStyle(workbook);
        var cellStyle = CreateCellStyle(workbook);

        string[] headers =
        [
            "Организация",
            "Название",
            "Категория персонала",
            "Профессии",
            "Программы",
            "Способ обучения",
            "Категория",
            "Версия",
            "Действует с",
            "Создан"
        ];

        var headerRow = sheet.CreateRow(0);
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = headerRow.CreateCell(c);
            cell.SetCellValue(headers[c]);
            cell.CellStyle = headerStyle;
        }

        var titleOrg = (organization ?? string.Empty).Trim();
        var r = 1;
        foreach (var standard in standards)
        {
            var excelRow = sheet.CreateRow(r++);
            var col = 0;
            void Write(string? value)
            {
                var cell = excelRow.CreateCell(col++);
                cell.SetCellValue(value ?? string.Empty);
                cell.CellStyle = cellStyle;
            }

            var org = string.IsNullOrWhiteSpace(standard.Organization) ? titleOrg : standard.Organization.Trim();
            var professions = string.Join("; ", standard.AllProfessions);
            var programs = string.Join("; ", (standard.Items ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim()));
            var effective = (standard.EffectiveFrom ?? standard.CreatedAt).ToString("dd.MM.yyyy", Ru);

            Write(org);
            Write(standard.Name);
            Write(string.IsNullOrWhiteSpace(standard.PersonnelCategory)
                ? standard.TrainingCategory
                : standard.PersonnelCategory.Trim());
            Write(professions);
            Write(programs);
            Write(standard.TrainingMethod);
            Write(standard.TrainingCategory);
            Write(standard.Version.ToString(Ru));
            Write(effective);
            Write(standard.CreatedAt.ToString("dd.MM.yyyy HH:mm", Ru));
        }

        // Фиксированные ширины: AutoSizeColumn тянет SkiaSharp и падает вне Windows.
        int[] widths = [5000, 5000, 4000, 5500, 8000, 4000, 3500, 2500, 3500, 4000];
        for (var c = 0; c < headers.Length && c < widths.Length; c++)
        {
            sheet.SetColumnWidth(c, widths[c]);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    public static string BuildFileName(string organization, DateTime asOf)
    {
        var org = SanitizeFilePart(organization);
        return $"стандарты-обучения_{org}_{asOf:yyyy-MM-dd}.xlsx";
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
