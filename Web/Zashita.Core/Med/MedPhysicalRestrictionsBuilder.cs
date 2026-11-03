namespace Zashita.Core.Med;

public static class MedPhysicalRestrictionsBuilder
{
    private static readonly HashSet<string> RestrictedConclusions =
        new(StringComparer.OrdinalIgnoreCase) { "Не годен", "Ограниченно годен" };

    public static string PersonKey(string organization, string employeeName) =>
        $"{organization?.Trim() ?? string.Empty}|{employeeName?.Trim() ?? string.Empty}";

    public static IReadOnlyList<MedPhysicalRestrictionRow> Build(
        IReadOnlyList<MedStatusRow> statusRows,
        IReadOnlyDictionary<string, string>? workRestrictions = null)
    {
        workRestrictions ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return statusRows
            .Where(r => RestrictedConclusions.Contains((r.Conclusion ?? string.Empty).Trim()))
            .GroupBy(r => PersonKey(r.Organization, r.EmployeeName), StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildRow(group.ToList(), workRestrictions))
            .OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static MedPhysicalRestrictionRow BuildRow(
        IReadOnlyList<MedStatusRow> rows,
        IReadOnlyDictionary<string, string> workRestrictions)
    {
        var first = rows[0];
        var key = PersonKey(first.Organization, first.EmployeeName);
        var anchor = rows
            .OrderByDescending(r => r.ExamDate ?? DateTime.MinValue)
            .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
            .First();

        var scanRow = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ConclusionScanFileName))
            .OrderByDescending(r => r.ExamDate ?? DateTime.MinValue)
            .FirstOrDefault();

        var conclusion = rows.Any(r =>
                string.Equals(r.Conclusion, "Не годен", StringComparison.OrdinalIgnoreCase))
            ? "Не годен"
            : "Ограниченно годен";

        workRestrictions.TryGetValue(key, out var workRestriction);

        return new MedPhysicalRestrictionRow
        {
            Key = key,
            Organization = first.Organization,
            EmployeeName = first.EmployeeName,
            Profession = anchor.Profession,
            Factors = FormatFactors(rows.Select(r => r.ItemNumber)),
            WorkRestriction = workRestriction ?? string.Empty,
            Conclusion = conclusion,
            ConclusionScanExamId = scanRow?.Id,
            ConclusionScanFileName = scanRow?.ConclusionScanFileName ?? string.Empty
        };
    }

    private static string FormatFactors(IEnumerable<string> itemNumbers)
    {
        var items = itemNumbers
            .Select(MedItemNumber.Normalize)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => $"п {n}")
            .ToList();

        return string.Join(", ", items);
    }
}

public sealed class MedPhysicalRestrictionRow
{
    public string Key { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string Factors { get; init; } = string.Empty;
    public string WorkRestriction { get; set; } = string.Empty;
    public string Conclusion { get; init; } = string.Empty;
    public string? ConclusionScanExamId { get; init; }
    public string ConclusionScanFileName { get; init; } = string.Empty;
}
