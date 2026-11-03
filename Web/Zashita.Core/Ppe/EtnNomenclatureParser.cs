using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Zashita.Core.Ppe;

/// <summary>
/// Парсер Excel «Номенклатура ЕТН» через Open XML (без NPOI/SkiaSharp).
/// Колонки: № п/п | Тип | Наименование | Норма | Основание.
/// </summary>
public static class EtnNomenclatureParser
{
    private static readonly XNamespace Ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static IReadOnlyList<PpeNomenclatureItem> Parse(string xlsxPath)
    {
        if (string.IsNullOrWhiteSpace(xlsxPath) || !File.Exists(xlsxPath))
        {
            return [];
        }

        using var zip = ZipFile.OpenRead(xlsxPath);
        var shared = ReadSharedStrings(zip);
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry is null)
        {
            return [];
        }

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);
        var items = new List<PpeNomenclatureItem>();
        var rowIndex = 0;

        foreach (var row in sheetDoc.Root?.Element(Ss + "sheetData")?.Elements(Ss + "row") ?? [])
        {
            var cells = ReadRowCells(row, shared);
            var col0 = Get(cells, 0);
            var protectionType = Get(cells, 1);
            var name = Get(cells, 2);
            var norm = Get(cells, 3);
            var basis = Get(cells, 4);

            if (rowIndex == 0 && LooksLikeHeader(col0, protectionType, name))
            {
                rowIndex++;
                continue;
            }

            rowIndex++;
            if (string.IsNullOrWhiteSpace(name) || !PpeNomenclatureStore.IsValidNomenclatureName(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(basis) &&
                !string.IsNullOrWhiteSpace(col0) &&
                int.TryParse(col0.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                basis = col0.Trim();
            }

            items.Add(new PpeNomenclatureItem
            {
                Category = GuessCategory(protectionType, name),
                ProtectionType = string.IsNullOrWhiteSpace(protectionType) ? "СИЗ" : protectionType.Trim(),
                Name = name.Trim(),
                Norm = string.IsNullOrWhiteSpace(norm) ? "По норме" : norm.Trim(),
                NormParagraph = basis?.Trim() ?? string.Empty
            });
        }

        return items;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var list = new List<string>();
        foreach (var si in doc.Root?.Elements(Ss + "si") ?? [])
        {
            var texts = si.Descendants(Ss + "t").Select(t => t.Value);
            list.Add(string.Concat(texts));
        }

        return list;
    }

    private static Dictionary<int, string> ReadRowCells(XElement row, IReadOnlyList<string> shared)
    {
        var map = new Dictionary<int, string>();
        foreach (var c in row.Elements(Ss + "c"))
        {
            var refAttr = (string?)c.Attribute("r") ?? "";
            var col = ColumnIndex(refAttr);
            if (col < 0)
            {
                continue;
            }

            var type = (string?)c.Attribute("t");
            var v = c.Element(Ss + "v")?.Value ?? "";
            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) &&
                idx >= 0 && idx < shared.Count)
            {
                map[col] = shared[idx];
            }
            else if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                map[col] = string.Concat(c.Descendants(Ss + "t").Select(t => t.Value));
            }
            else
            {
                map[col] = v;
            }
        }

        return map;
    }

    private static string Get(IReadOnlyDictionary<int, string> cells, int index) =>
        cells.TryGetValue(index, out var value) ? value?.Trim() ?? "" : "";

    private static int ColumnIndex(string cellRef)
    {
        if (string.IsNullOrWhiteSpace(cellRef))
        {
            return -1;
        }

        var letters = new StringBuilder();
        foreach (var ch in cellRef)
        {
            if (char.IsLetter(ch))
            {
                letters.Append(char.ToUpperInvariant(ch));
            }
            else
            {
                break;
            }
        }

        if (letters.Length == 0)
        {
            return -1;
        }

        var col = 0;
        foreach (var ch in letters.ToString())
        {
            col = col * 26 + (ch - 'A' + 1);
        }

        return col - 1;
    }

    private static bool LooksLikeHeader(string col0, string protectionType, string name)
    {
        var probe = $"{col0}|{protectionType}|{name}".ToLowerInvariant();
        return probe.Contains("п/п") ||
               probe.Contains("тип средства") ||
               probe.Contains("наименование");
    }

    private static string GuessCategory(string protectionType, string name)
    {
        var blob = $"{protectionType} {name}".ToLowerInvariant();
        if (blob.Contains("дерматолог") ||
            blob.Contains("очища") ||
            blob.Contains("смыва") ||
            blob.Contains("регенерир") ||
            blob.Contains("защитного типа") ||
            blob.Contains("мыл") ||
            blob.Contains("крем"))
        {
            return "ДСИЗ";
        }

        return "СИЗ";
    }
}
