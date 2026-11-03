using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zashita.Core.Ppe;

/// <summary>
/// Разбор «Приложение N1 — профессии, связанные со строительством» (ЕТН):
/// профессия → тип средства защиты → наименование → норма выдачи.
/// </summary>
public static class Appendix1NormsParser
{
    public static IReadOnlyList<Appendix1NormEntry> Parse(string docxPath)
    {
        if (string.IsNullOrWhiteSpace(docxPath) || !File.Exists(docxPath))
        {
            return [];
        }

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var result = new List<Appendix1NormEntry>();
        string currentProfession = string.Empty;
        string currentProfessionNumber = string.Empty;
        string currentType = string.Empty;

        foreach (var table in body.Elements<Table>())
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count < 2)
            {
                continue;
            }

            var header = GetCells(rows[0]);
            if (!LooksLikeAppendixTable(header))
            {
                continue;
            }

            for (var i = 1; i < rows.Count; i++)
            {
                var cells = GetCells(rows[i]);
                while (cells.Count < 5)
                {
                    cells.Add(string.Empty);
                }

                var number = cells[0].Trim();
                var profession = cells[1].Trim();
                var type = cells[2].Trim();
                var name = cells[3].Trim();
                var norm = cells[4].Trim();

                if (!string.IsNullOrWhiteSpace(profession))
                {
                    currentProfession = profession;
                    currentProfessionNumber = number;
                    currentType = string.Empty;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(type))
                {
                    currentType = type;
                }

                if (!IsValidItemName(name))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentProfession))
                {
                    continue;
                }

                result.Add(new Appendix1NormEntry
                {
                    ProfessionNumber = currentProfessionNumber,
                    Profession = currentProfession,
                    ProtectionType = string.IsNullOrWhiteSpace(currentType) ? "СИЗ" : currentType,
                    Name = CollapseSpaces(name),
                    Norm = string.IsNullOrWhiteSpace(norm) ? "По норме" : CollapseSpaces(norm),
                    NormParagraph = string.IsNullOrWhiteSpace(currentProfessionNumber)
                        ? "приложение 1 к ЕТН"
                        : $"п.{currentProfessionNumber} приложение 1 к ЕТН"
                });
            }
        }

        return result;
    }

    public static IReadOnlyList<PpeNomenclatureItem> ToNomenclatureItems(IEnumerable<Appendix1NormEntry> entries)
    {
        var byKey = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = $"{entry.ProtectionType}|{entry.Name}";
            if (byKey.TryGetValue(key, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Norm) || existing.Norm == "По норме")
                {
                    existing.Norm = entry.Norm;
                }

                if (string.IsNullOrWhiteSpace(existing.NormParagraph))
                {
                    existing.NormParagraph = entry.NormParagraph;
                }

                continue;
            }

            byKey[key] = new PpeNomenclatureItem
            {
                Category = GuessSizCategory(entry.ProtectionType, entry.Name),
                ProtectionType = entry.ProtectionType,
                Name = entry.Name,
                Norm = entry.Norm,
                NormParagraph = entry.NormParagraph
            };
        }

        return byKey.Values
            .OrderBy(i => i.ProtectionType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeAppendixTable(IReadOnlyList<string> header)
    {
        var joined = string.Join(' ', header).ToLowerInvariant();
        return joined.Contains("наименование профессий") ||
               joined.Contains("тип средства защиты") ||
               (joined.Contains("нормы выдачи") && joined.Contains("средств"));
    }

    private static bool IsValidItemName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        if (value.Equals("или", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-", StringComparison.OrdinalIgnoreCase) ||
            value.Length < 3)
        {
            return false;
        }

        return true;
    }

    public static string GuessSizCategoryPublic(string protectionType, string name) =>
        GuessSizCategory(protectionType, name);

    private static string GuessSizCategory(string protectionType, string name)
    {
        var text = $"{protectionType} {name}".ToLowerInvariant();
        if (text.Contains("дерматолог") ||
            text.Contains("смыва") ||
            text.Contains("мыл") ||
            text.Contains("крем") ||
            text.Contains("очища"))
        {
            return "ДСИЗ";
        }

        return "СИЗ";
    }

    private static List<string> GetCells(TableRow row) =>
        row.Elements<TableCell>()
            .Select(cell => CollapseSpaces(cell.InnerText ?? string.Empty))
            .ToList();

    private static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed class Appendix1NormEntry
{
    public string ProfessionNumber { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string ProtectionType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Norm { get; set; } = string.Empty;
    public string NormParagraph { get; set; } = string.Empty;
}
