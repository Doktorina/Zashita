namespace Zashita.Core.Med;

/// <summary>Сопоставление направлений на медосмотр с факторами сотрудника.</summary>
public static class MedDirectionMatcher
{
    private const char KeySep = '\u001e';

    public static string DirectionFullName(MedDirectionRecord direction)
    {
        var parts = new[]
        {
            direction.LastName?.Trim() ?? string.Empty,
            direction.FirstName?.Trim() ?? string.Empty,
            direction.MiddleName?.Trim() ?? string.Empty
        }.Where(p => p.Length > 0);
        return string.Join(' ', parts);
    }

    public static IReadOnlySet<string> BuildIssuedKeys(IEnumerable<MedDirectionRecord> directions)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var direction in directions)
        {
            var org = (direction.Organization ?? string.Empty).Trim();
            var name = DirectionFullName(direction);
            if (name.Length == 0)
            {
                continue;
            }

            var examType = MedStandardAssignmentBuilder.NormalizeExamType(direction.ExamType);
            var factors = direction.Factors ?? [];
            if (factors.Count == 0)
            {
                keys.Add(IssuedKey(org, name, examType, string.Empty));
                continue;
            }

            foreach (var factor in factors)
            {
                var item = MedItemNumber.Normalize(factor.Code29n);
                keys.Add(IssuedKey(org, name, examType, item));
            }
        }

        return keys;
    }

    public static bool HasDirection(
        IReadOnlySet<string> keys,
        string? organization,
        string? employeeName,
        string? examType,
        string? itemNumber)
    {
        if (keys.Count == 0)
        {
            return false;
        }

        var org = (organization ?? string.Empty).Trim();
        var name = NormalizePersonName(employeeName);
        if (name.Length == 0)
        {
            return false;
        }

        var kind = MedStandardAssignmentBuilder.NormalizeExamType(examType);
        var item = MedItemNumber.Normalize(itemNumber);
        if (keys.Contains(IssuedKey(org, name, kind, item)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item) && keys.Contains(IssuedKey(org, name, kind, string.Empty)))
        {
            return true;
        }

        if (MedStandardAssignmentBuilder.IsPsychiatricKind(kind) &&
            keys.Contains(IssuedKey(org, name, kind, string.Empty)))
        {
            return true;
        }

        return false;
    }

    public static bool HasDirection(IReadOnlySet<string> keys, MedStatusRow row) =>
        HasDirection(keys, row.Organization, row.EmployeeName, row.ExamType, row.ItemNumber);

    private static string IssuedKey(string organization, string employeeName, string examType, string itemNumber) =>
        string.Join(KeySep, organization, employeeName, examType, itemNumber);

    private static string NormalizePersonName(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static IReadOnlySet<string> BuildIssuedKeysForOrganizations(
        IEnumerable<MedDirectionRecord> directions,
        IEnumerable<string>? organizations = null)
    {
        var orgList = (organizations ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orgList.Count == 0)
        {
            return BuildIssuedKeys(directions);
        }

        var orgSet = orgList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return BuildIssuedKeys(directions.Where(d => orgSet.Contains((d.Organization ?? string.Empty).Trim())));
    }
}
