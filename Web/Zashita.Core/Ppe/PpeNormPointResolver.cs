using System.Text.RegularExpressions;

namespace Zashita.Core.Ppe;

/// <summary>Пункт типовых норм для карточки СИЗ из номенклатуры / Приложения 1 ЕТН.</summary>
public static class PpeNormPointResolver
{
    /// <summary>Пункт типовых норм только из номенклатуры (колонка «Пункт типовых норм»).</summary>
    public static string ResolveFromNomenclature(
        string itemName,
        IEnumerable<PpeNomenclatureItem>? nomenclature) =>
        Resolve(itemName, profession: null, nomenclature, appendixEntries: []);

    /// <summary>Форматирует значение поля номенклатуры «Пункт типовых норм» для бланка карточки.</summary>
    public static string FormatParagraphForCard(string? normParagraph) => FormatForCard(normParagraph);

    /// <summary>
    /// Описание для колонки 2 оборота: полное наименование из номенклатуры (без вымышленных моделей).
    /// </summary>
    public static string ResolveModelDescription(
        string itemName,
        IEnumerable<PpeNomenclatureItem>? nomenclature)
    {
        var name = (itemName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || nomenclature is null)
        {
            return string.Empty;
        }

        var exact = nomenclature.FirstOrDefault(n =>
            string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact?.Name))
        {
            return exact!.Name.Trim();
        }

        var partial = nomenclature
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .Select(n => n.Name.Trim())
            .Where(n =>
                n.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(n, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.Length)
            .FirstOrDefault();

        return partial ?? string.Empty;
    }

    /// <summary>Срок/период выдачи из текста нормы номенклатуры («1 раз на 2 года», «до износа»…).</summary>
    public static string DeriveShelfLife(string? norm, bool isDsiz)
    {
        if (isDsiz)
        {
            return "1 год";
        }

        var text = (norm ?? string.Empty).Trim();
        if (text.Length == 0 ||
            text.Equals("По норме", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Определяется документами", StringComparison.OrdinalIgnoreCase))
        {
            return "До износа";
        }

        if (text.Contains("износ", StringComparison.OrdinalIgnoreCase))
        {
            return "До износа";
        }

        var oncePer = Regex.Match(
            text,
            @"раз(?:а|ов)?\s*(?:на|в)\s*(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (oncePer.Success)
        {
            return FormatShelfLife(int.Parse(oncePer.Groups[1].Value), oncePer.Groups[2].Value);
        }

        var years = Regex.Match(
            text,
            @"(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (years.Success)
        {
            return FormatShelfLife(int.Parse(years.Groups[1].Value), years.Groups[2].Value);
        }

        return "До износа";
    }

    private static string FormatShelfLife(int n, string unitRaw)
    {
        var unit = (unitRaw ?? string.Empty).Trim().ToLowerInvariant();
        if (unit.StartsWith("месяц", StringComparison.Ordinal))
        {
            return n switch
            {
                1 => "1 месяц",
                >= 2 and <= 4 => $"{n} месяца",
                _ => $"{n} месяцев"
            };
        }

        if (n == 1)
        {
            return "1 год";
        }

        var mod100 = n % 100;
        var mod10 = n % 10;
        if (mod100 is >= 11 and <= 14)
        {
            return $"{n} лет";
        }

        return mod10 switch
        {
            1 => $"{n} год",
            2 or 3 or 4 => $"{n} года",
            _ => $"{n} лет"
        };
    }

    public static string Resolve(
        string itemName,
        string? profession,
        IEnumerable<PpeNomenclatureItem>? nomenclature,
        IReadOnlyList<Appendix1NormEntry>? appendixEntries)
    {
        var name = (itemName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        if (nomenclature is not null)
        {
            var exact = nomenclature.FirstOrDefault(n =>
                string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact?.NormParagraph))
            {
                return FormatForCard(exact!.NormParagraph);
            }

            var partial = nomenclature.FirstOrDefault(n =>
                !string.IsNullOrWhiteSpace(n.NormParagraph) &&
                (n.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                 name.Contains(n.Name, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(partial?.NormParagraph))
            {
                return FormatForCard(partial!.NormParagraph);
            }
        }

        var forProfession = FilterByProfession(appendixEntries, profession).ToList();
        if (forProfession.Count > 0)
        {
            var byName = forProfession.FirstOrDefault(e =>
                e.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return FormatEntry(byName);
            }

            return FormatEntry(forProfession[0]);
        }

        if (appendixEntries is { Count: > 0 })
        {
            var any = appendixEntries.FirstOrDefault(e =>
                e.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
            if (any is not null)
            {
                return FormatEntry(any);
            }
        }

        return string.Empty;
    }

    public static string ResolveNorm(
        string itemName,
        string? profession,
        IEnumerable<PpeNomenclatureItem>? nomenclature,
        IReadOnlyList<Appendix1NormEntry>? appendixEntries)
    {
        var name = (itemName ?? string.Empty).Trim();
        if (nomenclature is not null)
        {
            var exact = nomenclature.FirstOrDefault(n =>
                string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact?.Norm) &&
                !exact.Norm.Equals("По норме", StringComparison.OrdinalIgnoreCase))
            {
                return exact.Norm.Trim();
            }
        }

        var forProfession = FilterByProfession(appendixEntries, profession).ToList();
        var byName = forProfession.FirstOrDefault(e =>
            e.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byName?.Norm))
        {
            return byName!.Norm.Trim();
        }

        return "По норме";
    }

    private static IEnumerable<Appendix1NormEntry> FilterByProfession(
        IReadOnlyList<Appendix1NormEntry>? entries,
        string? profession)
    {
        if (entries is null || entries.Count == 0 || string.IsNullOrWhiteSpace(profession))
        {
            return [];
        }

        var p = profession.Trim();
        return entries.Where(e =>
            string.Equals(e.Profession, p, StringComparison.OrdinalIgnoreCase) ||
            e.Profession.Contains(p, StringComparison.OrdinalIgnoreCase) ||
            p.Contains(e.Profession, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatEntry(Appendix1NormEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ProfessionNumber))
        {
            return entry.ProfessionNumber.Trim();
        }

        return FormatForCard(entry.NormParagraph);
    }

    /// <summary>В карточке-образце в колонке «Пункт Норм» указан номер, например «11».</summary>
    private static string FormatForCard(string? normParagraph)
    {
        if (string.IsNullOrWhiteSpace(normParagraph))
        {
            return string.Empty;
        }

        var text = normParagraph.Trim();
        var match = Regex.Match(text, @"п\.?\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = Regex.Match(text, @"^\d+");
        return match.Success ? match.Value : text;
    }
}
