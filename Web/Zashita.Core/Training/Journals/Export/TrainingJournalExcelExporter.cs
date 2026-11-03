using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Training.Journals.Export;

using Zashita.Core.Training.Journals;

public static class TrainingJournalExcelExporter
{
    public static void ExportToExcel(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        string outputXlsxPath,
        string templatesDirectory)
    {
        if (definition.TableKind == TrainingJournalTableKind.OccupationalSafetyIntro9)
        {
            TrainingJournalIntroOtExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.TableKind == TrainingJournalTableKind.OccupationalSafety9)
        {
            TrainingJournalWorkplaceOtExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.TableKind == TrainingJournalTableKind.ContractorOccupationalSafety9)
        {
            TrainingJournalContractorOtExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.Kind == TrainingJournalKind.IntroductoryFireSafety)
        {
            TrainingJournalFireIntroExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.Kind == TrainingJournalKind.WorkplaceFireSafety)
        {
            TrainingJournalWorkplaceFireExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.TableKind == TrainingJournalTableKind.FireSafetyContractors12)
        {
            TrainingJournalFireContractorExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        if (definition.Kind == TrainingJournalKind.ElectroSafetyGroupI)
        {
            TrainingJournalElectroExcelExporter.Export(definition, entries, parameters, outputXlsxPath);
            return;
        }

        var templatePath = definition.ResolveExcelTemplatePath(templatesDirectory)
            ?? throw new FileNotFoundException("Не найден шаблон журнала Excel.", definition.ExcelTemplateFile);

        var layout = definition.Layout;
        using var input = File.OpenRead(templatePath);
        var workbook = new XSSFWorkbook(input);
        var sheet = workbook.GetSheetAt(0) ?? throw new InvalidOperationException("Лист шаблона не найден.");

        var org = parameters.Organization;
        if (string.IsNullOrWhiteSpace(org) && entries.Count > 0)
        {
            org = entries[0].Organization;
        }

        SetCell(sheet, layout.OrganizationRowIndex, 0, org ?? string.Empty);

        var from = parameters.DateFrom.Date;
        var to = parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        SetCell(sheet, layout.PeriodStartRowIndex, layout.PeriodDateColumnIndex, RussianDateText.FullCalendar(from));
        SetCell(sheet, layout.PeriodEndRowIndex, layout.PeriodDateColumnIndex, RussianDateText.FullCalendar(to));

        var templateRows = CountTemplateDataRows(sheet, layout);
        var requiredRows = Math.Max(templateRows, entries.Count);
        EnsureDataRowCapacity(sheet, workbook, layout, requiredRows);

        for (var i = 0; i < entries.Count; i++)
        {
            var rowIndex = layout.FirstDataRowIndex + i;
            var e = entries[i];
            SetCell(sheet, rowIndex, layout.ColDate, RussianDateText.Short(e.BriefingDate));
            SetCell(sheet, rowIndex, layout.ColEmployee, e.EmployeeName);
            SetCell(sheet, rowIndex, layout.ColBirth, RussianDateText.Short(e.BirthDate));
            SetCell(sheet, rowIndex, layout.ColProfession, e.Profession);
            SetCell(sheet, rowIndex, layout.ColDepartment, e.Department);
            SetCell(sheet, rowIndex, layout.ColInstructor, e.InstructorName);
            SetCell(sheet, rowIndex, layout.ColSignInstructor, string.Empty);
            SetCell(sheet, rowIndex, layout.ColSignEmployee, string.Empty);
        }

        for (var i = entries.Count; i < requiredRows; i++)
        {
            var rowIndex = layout.FirstDataRowIndex + i;
            for (var c = layout.ColDate; c <= layout.ColSignEmployee; c++)
            {
                SetCell(sheet, rowIndex, c, string.Empty);
            }
        }

        if (!parameters.ShowTitlePage)
        {
            ClearTitleBlock(sheet, layout);
        }

        TrainingJournalExcelPrintSetup.ApplyLandscapePrintSetup(sheet);
        ApplyTitleAndTablePageLayout(workbook, sheet, layout, parameters.ShowTitlePage, entries.Count);

        using var output = File.Create(outputXlsxPath);
        workbook.Write(output, leaveOpen: true);
    }

    private static void ApplyTitleAndTablePageLayout(
        IWorkbook workbook,
        ISheet sheet,
        TrainingJournalLayout layout,
        bool showTitlePage,
        int entryCount)
    {
        foreach (var rowBreak in sheet.RowBreaks.ToArray())
        {
            sheet.RemoveRowBreak(rowBreak);
        }

        var lastDataRow = Math.Max(
            layout.FirstDataRowIndex + Math.Max(entryCount, 1) - 1,
            layout.TableHeaderLastRowIndex + 4);
        var sheetIndex = workbook.GetSheetIndex(sheet);
        workbook.SetPrintArea(
            sheetIndex,
            0,
            layout.ColSignEmployee,
            0,
            lastDataRow);

        sheet.RepeatingRows = new CellRangeAddress(
            layout.TableHeaderFirstRowIndex,
            layout.TableHeaderLastRowIndex,
            0,
            layout.ColSignEmployee);

        if (showTitlePage)
        {
            sheet.SetRowBreak(layout.PageBreakAfterTitleRowIndex);
        }
    }

    private static int CountTemplateDataRows(ISheet sheet, TrainingJournalLayout layout)
    {
        var count = 0;
        for (var r = layout.FirstDataRowIndex; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            var hasStyle = false;
            for (var c = layout.ColDate; c <= layout.ColSignEmployee; c++)
            {
                if (row.GetCell(c) is not null)
                {
                    hasStyle = true;
                    break;
                }
            }

            if (hasStyle)
            {
                count++;
            }
            else if (count > 0)
            {
                break;
            }
        }

        return Math.Max(count, 4);
    }

    private static void EnsureDataRowCapacity(ISheet sheet, IWorkbook workbook, TrainingJournalLayout layout, int dataRowCount)
    {
        var templateRow = sheet.GetRow(layout.FirstDataRowIndex) ?? sheet.CreateRow(layout.FirstDataRowIndex);
        for (var i = 0; i < dataRowCount; i++)
        {
            var rowIndex = layout.FirstDataRowIndex + i;
            var row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
            for (var c = layout.ColDate; c <= layout.ColSignEmployee; c++)
            {
                if (row.GetCell(c) is null)
                {
                    var cell = row.CreateCell(c);
                    var templateCell = templateRow.GetCell(c);
                    if (templateCell is not null)
                    {
                        cell.CellStyle = templateCell.CellStyle;
                    }
                    else
                    {
                        cell.CellStyle = CreateBorderedStyle(workbook);
                    }
                }
            }
        }
    }

    private static ICellStyle CreateBorderedStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        style.BorderTop = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderBottom = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderLeft = NPOI.SS.UserModel.BorderStyle.Thin;
        style.BorderRight = NPOI.SS.UserModel.BorderStyle.Thin;
        style.WrapText = true;
        style.VerticalAlignment = VerticalAlignment.Center;
        return style;
    }

    private static void ClearTitleBlock(ISheet sheet, TrainingJournalLayout layout)
    {
        for (var r = layout.OrganizationRowIndex - 1; r <= layout.TableHeaderLastRowIndex; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            for (var c = 0; c <= layout.ColSignEmployee; c++)
            {
                var cell = row.GetCell(c);
                cell?.SetCellValue(string.Empty);
            }
        }
    }

    private static void SetCell(ISheet sheet, int rowIndex, int colIndex, string value)
    {
        var row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
        var cell = row.GetCell(colIndex) ?? row.CreateCell(colIndex);
        cell.SetCellValue(value);
    }
}

