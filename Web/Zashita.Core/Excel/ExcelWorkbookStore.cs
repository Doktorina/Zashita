using System.Data;
using System.Globalization;
using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Zashita.Core.Excel;

public sealed class ExcelWorkbookStore
{
    private readonly Dictionary<string, DataTable> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private string? _fileName;
    private DateTime _loadedAt;

    public bool HasWorkbook => _sheets.Count > 0;
    public string? FileName => _fileName;
    public DateTime LoadedAt => _loadedAt;
    public IReadOnlyDictionary<string, DataTable> Sheets => _sheets;

    public void LoadFromStream(Stream stream, string fileName)
    {
        _sheets.Clear();
        _fileName = fileName;
        _loadedAt = DateTime.UtcNow;

        IWorkbook workbook = fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);

        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            var sheet = workbook.GetSheetAt(i);
            var name = sheet.SheetName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _sheets[name] = ReadSheet(sheet);
        }
    }

    public void LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        LoadFromStream(stream, Path.GetFileName(path));
    }

    public DataTable? GetSheet(string name) =>
        _sheets.TryGetValue(name, out var table) ? table : null;

    public IReadOnlyList<string> SheetNames => _sheets.Keys.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static DataTable ReadSheet(ISheet sheet)
    {
        var table = new DataTable(sheet.SheetName);
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = sheet.Workbook.GetCreationHelper().CreateFormulaEvaluator();

        IRow? headerRow = null;
        for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            if (row.Cells.Any(c => !string.IsNullOrWhiteSpace(formatter.FormatCellValue(c, evaluator))))
            {
                headerRow = row;
                break;
            }
        }

        if (headerRow is null)
        {
            return table;
        }

        var colCount = headerRow.LastCellNum;
        for (var c = 0; c < colCount; c++)
        {
            var title = formatter.FormatCellValue(headerRow.GetCell(c), evaluator).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = $"Column{c + 1}";
            }

            if (table.Columns.Contains(title))
            {
                title = $"{title}_{c + 1}";
            }

            table.Columns.Add(title);
        }

        for (var r = headerRow.RowNum + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            if (!row.Cells.Any(c => !string.IsNullOrWhiteSpace(formatter.FormatCellValue(c, evaluator))))
            {
                continue;
            }

            var values = new object?[table.Columns.Count];
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var cell = row.GetCell(c);
                values[c] = cell is null ? DBNull.Value : formatter.FormatCellValue(cell, evaluator);
            }

            table.Rows.Add(values);
        }

        return table;
    }

    public string BuildHtmlTable(string sheetName, int maxRows = 500)
    {
        if (!_sheets.TryGetValue(sheetName, out var table))
        {
            return "<p>Лист не найден.</p>";
        }

        var sb = new StringBuilder();
        sb.Append("<table class=\"data-grid\"><thead><tr>");
        foreach (DataColumn col in table.Columns)
        {
            sb.Append("<th>").Append(System.Net.WebUtility.HtmlEncode(col.ColumnName)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        var count = 0;
        foreach (DataRow row in table.Rows)
        {
            if (count++ >= maxRows)
            {
                break;
            }

            sb.Append("<tr>");
            foreach (DataColumn col in table.Columns)
            {
                var text = row[col]?.ToString() ?? string.Empty;
                sb.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(text)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        if (table.Rows.Count > maxRows)
        {
            sb.Append($"<p class=\"hint\">Показаны первые {maxRows} из {table.Rows.Count} строк.</p>");
        }

        return sb.ToString();
    }
}
