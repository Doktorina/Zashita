using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training.Journals.Export;

using Zashita.Core.Training.Journals;

/// <summary>Excel-журнал вводного инструктажа ОТ (8 граф по бланку) без внешнего шаблона.</summary>
public static class TrainingJournalIntroOtExcelExporter
{
    private const int ColCount = 8;
    private const int HeaderRow1 = 30;
    private const int HeaderRow2 = 31;
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

        if (parameters.ShowTitlePage)
        {
            WriteTitle(sheet, definition, org ?? string.Empty, department, from, to, titleStyle);
        }

        WriteTableHeader(sheet, headerStyle);
        WriteDataRows(sheet, entries, cellStyle);

        if (definition.ShowResponsibleBlock)
        {
            var respRow = FirstDataRow + Math.Max(entries.Count, 1) + 2;
            SetCell(sheet, respRow, 0, "Ответственный:", titleStyle);
            SetCell(sheet, respRow, 2, "________________    ________________    ________________", cellStyle);
            SetCell(sheet, respRow + 1, 2, "(должность)              (подпись)         (расшифровка подписи)", cellStyle);
        }

        for (var c = 0; c < ColCount; c++)
        {
            sheet.SetColumnWidth(c, c is 1 or 4 or 5 ? 5200 : 3400);
        }

        TrainingJournalExcelPrintSetup.ApplyLandscapePrintSetup(sheet);
        var printSetup = sheet.PrintSetup;
        printSetup.FitWidth = 1;
        printSetup.FitHeight = 0;

        var lastRow = FirstDataRow + Math.Max(entries.Count, 1);
        workbook.SetPrintArea(0, 0, ColCount - 1, 0, lastRow);
        sheet.RepeatingRows = new CellRangeAddress(HeaderRow1, HeaderRow2, 0, ColCount - 1);
        if (parameters.ShowTitlePage)
        {
            sheet.SetRowBreak(HeaderRow1 - 1);
        }

        using var output = File.Create(outputXlsxPath);
        workbook.Write(output, leaveOpen: true);
    }

    private static void WriteTitle(
        ISheet sheet,
        TrainingJournalDefinition definition,
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

        SetCell(sheet, row++, 0, "(наименование организации)", titleStyle);
        row += 2;

        foreach (var line in definition.TitlePageHeadingLines)
        {
            SetCell(sheet, row++, 0, line, titleStyle);
        }

        SetCell(sheet, 26, 5, $"Начат  {RussianDateText.FullCalendar(from)}", titleStyle);
        SetCell(sheet, 27, 5, $"Окончен  {RussianDateText.FullCalendar(to)}", titleStyle);
    }

    private static void WriteTableHeader(ISheet sheet, ICellStyle headerStyle)
    {
        var h1 = new[]
        {
            "Дата проведения вводного инструктажа по охране труда",
            "Фамилия, имя, отчество (при наличии) работника, прошедшего вводный инструктаж по охране труда",
            "Профессия (должность) работника, прошедшего вводный инструктаж по охране труда",
            "Число, месяц, год рождения работника, прошедшего вводный инструктаж по охране труда",
            "Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший вводный инструктаж по охране труда",
            "Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего вводный инструктаж по охране труда",
            "Подпись работника, проводившего инструктаж по охране труда",
            "Подпись работника, прошедшего вводный инструктаж по охране труда"
        };

        for (var c = 0; c < ColCount; c++)
        {
            SetCell(sheet, HeaderRow1, c, h1[c], headerStyle);
            SetCell(sheet, HeaderRow2, c, (c + 1).ToString(), headerStyle);
        }
    }

    private static void WriteDataRows(
        ISheet sheet,
        IReadOnlyList<TrainingJournalEntry> entries,
        ICellStyle cellStyle)
    {
        var count = Math.Max(entries.Count, 1);
        for (var i = 0; i < count; i++)
        {
            var row = FirstDataRow + i;
            if (i >= entries.Count)
            {
                for (var c = 0; c < ColCount; c++)
                {
                    SetCell(sheet, row, c, string.Empty, cellStyle);
                }

                continue;
            }

            var e = entries[i];
            var instructor = string.IsNullOrWhiteSpace(e.InstructorQualification)
                ? e.InstructorName
                : $"{e.InstructorName}, {e.InstructorQualification}";

            SetCell(sheet, row, 0, RussianDateText.Short(e.BriefingDate), cellStyle);
            SetCell(sheet, row, 1, e.EmployeeName, cellStyle);
            SetCell(sheet, row, 2, e.Profession, cellStyle);
            SetCell(sheet, row, 3, RussianDateText.Short(e.BirthDate), cellStyle);
            SetCell(sheet, row, 4, e.Department, cellStyle);
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
        style.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
        style.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
        style.WrapText = true;
        return style;
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        var font = workbook.CreateFont();
        font.FontName = TrainingJournalWordTypography.FontName;
        font.FontHeightInPoints = 9;
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

