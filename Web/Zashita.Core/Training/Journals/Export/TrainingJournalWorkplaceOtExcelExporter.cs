using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training.Journals.Export;

using Zashita.Core.Training.Journals;

/// <summary>Excel-журнал инструктажа ОТ на рабочем месте (10 граф по бланку).</summary>
public static class TrainingJournalWorkplaceOtExcelExporter
{
    private const int ColCount = 10;
    private const int HeaderRow1 = 30;
    private const int HeaderRow2 = 31;
    private const int FirstDataRow = 32;

    private static readonly string[] Headers =
    [
        "Дата проведения инструктажа по охране труда",
        "Фамилия, имя, отчество (при наличии) работника, прошедшего инструктаж по охране труда",
        "Профессия (должность) работника, прошедшего инструктаж по охране труда",
        "Число, месяц, год рождения работника, прошедшего инструктаж по охране труда",
        "Вид инструктажа по охране труда",
        "Причина проведения инструктажа по охране труда (для внепланового или целевого инструктажа по охране труда)",
        "Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего инструктаж по охране труда",
        "Наименование локального акта (локальных актов), в объеме требований которого проведен инструктаж по охране труда",
        "Подпись работника, проводившего инструктаж по охране труда",
        "Подпись работника, прошедшего инструктаж по охране труда"
    ];

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
            sheet.SetColumnWidth(c, c is 1 or 5 or 6 or 7 ? 4800 : 3000);
        }

        TrainingJournalExcelPrintSetup.ApplyLandscapePrintSetup(sheet);
        var printSetup = sheet.PrintSetup;
        printSetup.FitWidth = 1;
        printSetup.FitHeight = 0;

        var lastRow = FirstDataRow + Math.Max(data.Count, 1);
        workbook.SetPrintArea(0, 0, ColCount - 1, 0, lastRow);
        sheet.RepeatingRows = new CellRangeAddress(HeaderRow1, HeaderRow2, 0, ColCount - 1);
        if (parameters.ShowTitlePage)
        {
            sheet.SetRowBreak(HeaderRow1 - 1);
        }

        using var output = File.Create(outputXlsxPath);
        workbook.Write(output, leaveOpen: true);
    }

    private static bool HasContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.Department) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        !string.IsNullOrWhiteSpace(e.BriefingReason) ||
        !string.IsNullOrWhiteSpace(e.LocalActName) ||
        !string.IsNullOrWhiteSpace(e.Workplace) ||
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
        SetCell(sheet, row++, 0, "регистрации инструктажа по охране труда на рабочем месте", titleStyle);
        row += 2;

        if (!string.IsNullOrWhiteSpace(department))
        {
            SetCell(sheet, row++, 0, department, titleStyle);
        }

        SetCell(sheet, 26, 6, $"Начат  {RussianDateText.FullCalendar(from)}", titleStyle);
        SetCell(sheet, 27, 6, $"Окончен  {RussianDateText.FullCalendar(to)}", titleStyle);
    }

    private static void WriteTableHeader(ISheet sheet, ICellStyle headerStyle)
    {
        for (var c = 0; c < ColCount; c++)
        {
            SetCell(sheet, HeaderRow1, c, Headers[c], headerStyle);
            SetCell(sheet, HeaderRow2, c, (c + 1).ToString(), headerStyle);
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
            var localAct = string.IsNullOrWhiteSpace(e.LocalActName) ? e.Workplace : e.LocalActName;

            SetCell(sheet, row, 0, RussianDateText.Short(e.BriefingDate), cellStyle);
            SetCell(sheet, row, 1, e.EmployeeName, cellStyle);
            SetCell(sheet, row, 2, e.Profession, cellStyle);
            SetCell(sheet, row, 3, RussianDateText.Short(e.BirthDate), cellStyle);
            SetCell(sheet, row, 4, e.BriefingType, cellStyle);
            SetCell(sheet, row, 5, e.BriefingReason, cellStyle);
            SetCell(sheet, row, 6, instructor, cellStyle);
            SetCell(sheet, row, 7, localAct, cellStyle);
            SetCell(sheet, row, 8, string.Empty, cellStyle);
            SetCell(sheet, row, 9, string.Empty, cellStyle);
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
        style.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
        style.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
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
        style.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
        style.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
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
        style.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
        style.WrapText = true;
        ApplyBorders(style);
        return style;
    }

    private static void ApplyBorders(ICellStyle style)
    {
        style.BorderTop = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderBottom = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderLeft = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderRight = NPOI.SS.UserModel.BorderStyle.Thin;
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
