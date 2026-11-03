using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training.Journals.Export;

using Zashita.Core.Training.Journals;

/// <summary>Excel: вводный противопожарный для подрядных (8 граф по бланку).</summary>
public static class TrainingJournalFireContractorExcelExporter
{
    private const int ColCount = 8;
    private const int HeaderRow0 = 28;
    private const int HeaderRow1 = 29;
    private const int HeaderRow2 = 30;
    private const int HeaderRowNum = 31;
    private const int FirstDataRow = 32;

    public static void Export(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        string outputXlsxPath)
    {
        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Журнал");

        var org = parameters.Organization;
        if (string.IsNullOrWhiteSpace(org) && entries.Count > 0)
        {
            org = entries[0].Organization;
        }

        var department = parameters.Department?.Trim() ?? string.Empty;
        if (department.Contains("не указано", StringComparison.OrdinalIgnoreCase) ||
            department is "—" or "-")
        {
            department = string.Empty;
        }

        var from = parameters.DateFrom.Date;
        var to = parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var titleStyle = CreateTitleStyle(workbook);
        var headerStyle = CreateHeaderStyle(workbook);
        var cellStyle = CreateCellStyle(workbook);
        var data = entries.Where(HasContent).ToList();

        if (parameters.ShowTitlePage)
        {
            WriteTitle(sheet, org ?? string.Empty, department ?? string.Empty, from, to, titleStyle);
        }

        WriteTableHeader(sheet, headerStyle);
        WriteDataRows(sheet, data, cellStyle);

        for (var c = 0; c < ColCount; c++)
        {
            sheet.SetColumnWidth(c, c is 3 or 5 ? 5200 : 2800);
        }

        TrainingJournalExcelPrintSetup.ApplyLandscapePrintSetup(sheet);
        sheet.PrintSetup.FitWidth = 1;
        sheet.PrintSetup.FitHeight = 0;

        var lastRow = FirstDataRow + Math.Max(data.Count, 1);
        workbook.SetPrintArea(0, 0, ColCount - 1, 0, lastRow);
        sheet.RepeatingRows = new CellRangeAddress(HeaderRow0, HeaderRowNum, 0, ColCount - 1);
        if (parameters.ShowTitlePage)
        {
            sheet.SetRowBreak(HeaderRow0 - 1);
        }

        using var output = File.Create(outputXlsxPath);
        workbook.Write(output, leaveOpen: true);
    }

    private static bool HasContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.LegalEntity) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    private static void WriteTitle(
        ISheet sheet,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        ICellStyle titleStyle)
    {
        var row = 12;
        if (!string.IsNullOrWhiteSpace(organization))
        {
            SetCell(sheet, row++, 0, organization, titleStyle);
        }

        row += 2;
        SetCell(sheet, row++, 0, "ЖУРНАЛ", titleStyle);
        SetCell(sheet, row++, 0, "регистрации вводного противопожарного инструктажа", titleStyle);
        SetCell(sheet, row++, 0, "(для подрядных организаций)", titleStyle);
        row += 1;
        if (!string.IsNullOrWhiteSpace(department))
        {
            SetCell(sheet, row, 0, department, titleStyle);
        }

        SetCell(sheet, 26, 5, $"Начат  {RussianDateText.FullCalendar(from)}", titleStyle);
        SetCell(sheet, 27, 5, $"Окончен  {RussianDateText.FullCalendar(to)}", titleStyle);
    }

    private static void WriteTableHeader(ISheet sheet, ICellStyle headerStyle)
    {
        // Упрощённый плоский заголовок (лист без точных merge как в Word/HTML).
        var leaf =
            new[]
            {
                "Дата",
                "Вид проводимого инструктажа",
                "Наименование подрядной организации",
                "Фамилия, имя отчество (при наличии)",
                "Профессия, должность",
                "Фамилия, имя отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего",
                "Подпись инструктируемого",
                "Подпись инструктирующего"
            };

        for (var c = 0; c < ColCount; c++)
        {
            SetCell(sheet, HeaderRow0, c, leaf[c], headerStyle);
            SetCell(sheet, HeaderRowNum, c, (c + 1).ToString(), headerStyle);
        }

        // Пустые промежуточные строки для совместимости разметки печати
        for (var c = 0; c < ColCount; c++)
        {
            SetCell(sheet, HeaderRow1, c, string.Empty, headerStyle);
            SetCell(sheet, HeaderRow2, c, string.Empty, headerStyle);
        }
    }

    private static void WriteDataRows(
        ISheet sheet,
        IReadOnlyList<TrainingJournalEntry> entries,
        ICellStyle cellStyle)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var row = FirstDataRow + i;
            var e = entries[i];
            var instructor = string.IsNullOrWhiteSpace(e.InstructorQualification)
                ? e.InstructorName
                : $"{e.InstructorName}, {e.InstructorQualification}";

            SetCell(sheet, row, 0, RussianDateText.Short(e.BriefingDate), cellStyle);
            SetCell(sheet, row, 1, e.BriefingType, cellStyle);
            SetCell(sheet, row, 2, e.LegalEntity, cellStyle);
            SetCell(sheet, row, 3, e.EmployeeName, cellStyle);
            SetCell(sheet, row, 4, e.Profession, cellStyle);
            SetCell(sheet, row, 5, instructor, cellStyle);
            SetCell(sheet, row, 6, string.Empty, cellStyle);
            SetCell(sheet, row, 7, string.Empty, cellStyle);
        }
    }

    private static ICellStyle CreateTitleStyle(IWorkbook workbook)
    {
        var font = workbook.CreateFont();
        font.FontName = TrainingJournalWordTypography.FontName;
        font.FontHeightInPoints = 11;
        font.IsBold = true;
        var style = workbook.CreateCellStyle();
        style.SetFont(font);
        style.Alignment = HorizontalAlignment.Center;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.WrapText = true;
        return style;
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        var font = workbook.CreateFont();
        font.FontName = TrainingJournalWordTypography.FontName;
        font.FontHeightInPoints = 8;
        font.IsBold = true;
        var style = workbook.CreateCellStyle();
        style.SetFont(font);
        style.Alignment = HorizontalAlignment.Center;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.WrapText = true;
        ApplyBorders(style);
        return style;
    }

    private static ICellStyle CreateCellStyle(IWorkbook workbook)
    {
        var font = workbook.CreateFont();
        font.FontName = TrainingJournalWordTypography.FontName;
        font.FontHeightInPoints = 9;
        var style = workbook.CreateCellStyle();
        style.SetFont(font);
        style.VerticalAlignment = VerticalAlignment.Center;
        style.WrapText = true;
        ApplyBorders(style);
        return style;
    }

    private static void ApplyBorders(ICellStyle style)
    {
        style.BorderTop = BorderStyle.Thin;
        style.BorderBottom = BorderStyle.Thin;
        style.BorderLeft = BorderStyle.Thin;
        style.BorderRight = BorderStyle.Thin;
    }

    private static void SetCell(ISheet sheet, int rowIndex, int colIndex, string value, ICellStyle? style = null)
    {
        var row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
        var cell = row.GetCell(colIndex) ?? row.CreateCell(colIndex);
        cell.SetCellValue(value);
        if (style is not null)
        {
            cell.CellStyle = style;
        }
    }
}
