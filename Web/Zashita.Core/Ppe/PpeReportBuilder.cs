namespace Zashita.Core.Ppe;

public static class PpeReportBuilder
{
    private const string DefaultLegalBasis = "Пункт 1.2.1 Приложение 2. Приказ N 767н от 29 октября 2021 г.";

    public sealed record NormsRow(
        int Number,
        string Organization,
        string Department,
        string Profession,
        string PpeType,
        string PpeName,
        string NormText,
        string LegalBasis);

    public sealed record ProcurementRow(
        int Number,
        string Organization,
        string PpeName,
        string Category,
        string Justification);

    /// <param name="departmentByProfession">Подразделение по профессии (из справочника сотрудников организации).</param>
    public static IReadOnlyList<NormsRow> BuildNormsRegister(
        IEnumerable<IssueStandard> standards,
        IEnumerable<PpeNomenclatureItem>? nomenclature = null,
        IReadOnlyDictionary<string, string>? departmentByProfession = null)
    {
        var lookup = BuildNomenclatureLookup(nomenclature);
        var rows = new List<NormsRow>();
        var n = 0;
        foreach (var s in standards
                     .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
                     .OrderBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Profession, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var org = (s.Organization ?? "").Trim();
            var prof = (s.Profession ?? "").Trim();
            var department = ResolveDepartment(prof, departmentByProfession);
            foreach (var raw in s.Items)
            {
                var name = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                lookup.TryGetValue(name, out var nom);
                var ppeType = ResolvePpeType(nom, s.StandardCategory);
                var normText = ResolveNormText(nom, s);
                var legalBasis = ResolveLegalBasis(nom);

                rows.Add(new NormsRow(
                    ++n,
                    org.Length > 0 ? org : "—",
                    department,
                    prof.Length > 0 ? prof : "—",
                    ppeType,
                    name,
                    normText,
                    legalBasis));
            }
        }

        return rows;
    }

    private static string ResolveDepartment(
        string profession,
        IReadOnlyDictionary<string, string>? departmentByProfession)
    {
        if (departmentByProfession is null || string.IsNullOrWhiteSpace(profession))
        {
            return "—";
        }

        if (departmentByProfession.TryGetValue(profession.Trim(), out var exact) &&
            !string.IsNullOrWhiteSpace(exact))
        {
            return exact.Trim();
        }

        foreach (var pair in departmentByProfession)
        {
            if (pair.Key.Contains(profession, StringComparison.OrdinalIgnoreCase) ||
                profession.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return "—";
    }

    private static Dictionary<string, PpeNomenclatureItem> BuildNomenclatureLookup(IEnumerable<PpeNomenclatureItem>? nomenclature)
    {
        var lookup = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
        if (nomenclature is null)
        {
            return lookup;
        }

        foreach (var item in nomenclature)
        {
            var name = item.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            lookup.TryAdd(name, item);
        }

        return lookup;
    }

    private static string ResolvePpeType(PpeNomenclatureItem? item, string fallbackCategory)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.DisplayProtectionType))
        {
            return item.DisplayProtectionType;
        }

        return string.IsNullOrWhiteSpace(fallbackCategory) ? "СИЗ" : fallbackCategory;
    }

    private static string ResolveNormText(PpeNomenclatureItem? item, IssueStandard standard)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.Norm) &&
            !item.Norm.Equals("По норме", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeNormText(item.Norm.Trim());
        }

        return "1 раз в год по 1 шт";
    }

    private static string NormalizeNormText(string norm)
    {
        // Приводим к виду «1 раз в год по 1 шт», как в печатной форме 1С
        var t = norm.Trim();
        if (t.Contains("раз в год", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("по", StringComparison.OrdinalIgnoreCase))
        {
            return t;
        }

        if (int.TryParse(t, out var qty) && qty > 0)
        {
            return $"1 раз в год по {qty} шт";
        }

        return t;
    }

    private static string ResolveLegalBasis(PpeNomenclatureItem? item)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.NormParagraph))
        {
            var paragraph = item.NormParagraph.Trim().TrimEnd('.');
            if (paragraph.Contains("767н", StringComparison.OrdinalIgnoreCase) ||
                paragraph.Contains("Приказ", StringComparison.OrdinalIgnoreCase))
            {
                return paragraph;
            }

            return $"{paragraph}. {DefaultLegalBasis}";
        }

        return DefaultLegalBasis;
    }

    public static IReadOnlyList<ProcurementRow> BuildProcurementRequest(IEnumerable<IssueStandard> standards)
    {
        var orgs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var types = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in standards)
        {
            var org = (s.Organization ?? "").Trim();
            var type = s.StandardCategory;
            var line = $"{s.Name} — проф.: {s.Profession}";
            foreach (var raw in s.Items ?? [])
            {
                var name = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!orgs.ContainsKey(name))
                {
                    orgs[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    types[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    lines[name] = [];
                }

                if (!string.IsNullOrWhiteSpace(org)) orgs[name].Add(org);
                types[name].Add(type);
                if (!lines[name].Any(x => string.Equals(x, line, StringComparison.OrdinalIgnoreCase)))
                {
                    lines[name].Add(line);
                }
            }
        }

        var rows = new List<ProcurementRow>();
        var i = 0;
        foreach (var name in orgs.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
        {
            rows.Add(new ProcurementRow(
                ++i,
                string.Join("; ", orgs[name].OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)),
                name,
                string.Join(", ", types[name].OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)),
                string.Join("; ", lines[name])));
        }

        return rows;
    }

    public static string NormTextFromStandard(IssueStandard s) =>
        $"по стандарту «{s.Name.Trim()}» (позиций: {s.Items?.Count ?? 0})";
}
