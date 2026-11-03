using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Шаблонные риски по профессии: из других юрлиц и из других объектов / «по организации» той же компании.
/// </summary>
public static class RiskProfessionTemplateBuilder
{
    public sealed record Preview(
        bool Available,
        int SourceWorkplaceCount,
        int LineCount,
        IReadOnlyList<string> Organizations);

    public static Preview BuildPreview(
        IReadOnlyList<WorkplaceRecord> all,
        WorkplaceRecord target,
        string? currentObjectId = null,
        IReadOnlyDictionary<string, string>? objectNames = null)
    {
        var sources = CollectSourceSlices(all, target, currentObjectId, objectNames);
        if (sources.Count == 0)
        {
            return new Preview(false, 0, 0, []);
        }

        var lines = MergeLines(sources.SelectMany(s => s.Lines));
        var labels = sources
            .Select(s => s.Label)
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new Preview(lines.Count > 0, sources.Count, lines.Count, labels);
    }

    /// <summary>
    /// Объединяет заполненные строки рисков по той же профессии из доступных источников.
    /// </summary>
    public static List<WorkplaceRiskLine> BuildTemplateLines(
        IReadOnlyList<WorkplaceRecord> all,
        WorkplaceRecord target,
        string? currentObjectId = null,
        IReadOnlyDictionary<string, string>? objectNames = null) =>
        MergeLines(CollectSourceSlices(all, target, currentObjectId, objectNames).SelectMany(s => s.Lines));

    /// <summary>
    /// Добавляет шаблон к уже имеющимся строкам карты (без повторов).
    /// </summary>
    public static List<WorkplaceRiskLine> MergeIntoExisting(
        IReadOnlyList<WorkplaceRiskLine>? existing,
        IReadOnlyList<WorkplaceRiskLine> template)
    {
        var result = new List<WorkplaceRiskLine>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in existing ?? [])
        {
            if (IsEmptyLine(line))
            {
                continue;
            }

            var key = LineKey(line);
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(CloneLine(line));
        }

        foreach (var line in template)
        {
            if (IsEmptyLine(line))
            {
                continue;
            }

            var key = LineKey(line);
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(CloneLine(line));
        }

        return result;
    }

    private sealed record SourceSlice(string Label, IReadOnlyList<WorkplaceRiskLine> Lines);

    private static List<SourceSlice> CollectSourceSlices(
        IReadOnlyList<WorkplaceRecord> all,
        WorkplaceRecord target,
        string? currentObjectId,
        IReadOnlyDictionary<string, string>? objectNames)
    {
        var profession = ResolveProfession(target);
        if (string.IsNullOrWhiteSpace(profession))
        {
            return [];
        }

        var targetOrg = (target.Organization ?? string.Empty).Trim();
        var targetId = (target.Id ?? string.Empty).Trim();
        var currentObj = (currentObjectId ?? string.Empty).Trim();
        var currentIsOrgWide = RiskObjectAssessmentScope.IsOrganizationWide(currentObj);
        var result = new List<SourceSlice>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string label, IReadOnlyList<WorkplaceRiskLine>? lines, string dedupeKey)
        {
            if (lines is null || lines.Count == 0 || !HasFilledLines(lines))
            {
                return;
            }

            if (!seenKeys.Add(dedupeKey))
            {
                return;
            }

            result.Add(new SourceSlice(label.Trim(), lines));
        }

        string ObjectLabel(string objectId)
        {
            var id = objectId.Trim();
            if (objectNames is not null
                && objectNames.TryGetValue(id, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return RiskObjectAssessmentScope.IsOrganizationWide(id)
                ? RiskAssessmentObjectIds.OrganizationWideName
                : "объект";
        }

        // Та же карта: «по организации» и другие объекты (кроме текущего).
        var stored = all.FirstOrDefault(w =>
            string.Equals(w.Id, targetId, StringComparison.OrdinalIgnoreCase)) ?? target;

        if (!currentIsOrgWide
            && !stored.RiskAssessmentNotConducted
            && RiskAssessmentStatusBuilder.HasFilledRiskLines(stored))
        {
            TryAdd(
                ObjectLabel(RiskAssessmentObjectIds.OrganizationWide),
                stored.RiskLines,
                $"self:{targetId}:org-wide");
        }

        foreach (var (objectId, data) in stored.ObjectAssessments ?? [])
        {
            var oid = (objectId ?? string.Empty).Trim();
            if (oid.Length == 0)
            {
                continue;
            }

            if (string.Equals(oid, currentObj, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (data is null || data.RiskAssessmentNotConducted || !HasFilledLines(data.RiskLines))
            {
                continue;
            }

            TryAdd(ObjectLabel(oid), data.RiskLines, $"self:{targetId}:{oid}");
        }

        foreach (var wp in all)
        {
            if (wp.IsGroup || !wp.IsUsed)
            {
                continue;
            }

            if (string.Equals(wp.Id, targetId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var org = (wp.Organization ?? string.Empty).Trim();
            if (org.Length == 0)
            {
                continue;
            }

            if (!ProfessionMatches(ResolveProfession(wp), profession))
            {
                continue;
            }

            var sameOrg = string.Equals(org, targetOrg, StringComparison.OrdinalIgnoreCase)
                          || OrganizationMatcher.Matches(org, targetOrg)
                          || OrganizationMatcher.Matches(targetOrg, org);

            if (!wp.RiskAssessmentNotConducted && RiskAssessmentStatusBuilder.HasFilledRiskLines(wp))
            {
                var label = sameOrg
                    ? $"{ObjectLabel(RiskAssessmentObjectIds.OrganizationWide)} · {DisplayWorkplace(wp)}"
                    : org;
                TryAdd(label, wp.RiskLines, $"wp:{wp.Id}:org-wide");
            }

            foreach (var (objectId, data) in wp.ObjectAssessments ?? [])
            {
                var oid = (objectId ?? string.Empty).Trim();
                if (oid.Length == 0 || data is null || data.RiskAssessmentNotConducted)
                {
                    continue;
                }

                if (!HasFilledLines(data.RiskLines))
                {
                    continue;
                }

                var label = sameOrg
                    ? $"{ObjectLabel(oid)} · {DisplayWorkplace(wp)}"
                    : $"{org} · {ObjectLabel(oid)}";
                TryAdd(label, data.RiskLines, $"wp:{wp.Id}:{oid}");
            }
        }

        return result;
    }

    private static string DisplayWorkplace(WorkplaceRecord wp)
    {
        var name = (wp.Name ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            return name;
        }

        return ResolveProfession(wp);
    }

    private static bool HasFilledLines(IReadOnlyList<WorkplaceRiskLine>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return false;
        }

        return lines.Any(line =>
            !IsDash(line.HazardTitle) ||
            !IsDash(line.HazardNumber) ||
            !IsDash(line.ControlMeasures) ||
            !IsDash(line.Severity) ||
            !IsDash(line.Probability));
    }

    private static List<WorkplaceRiskLine> MergeLines(IEnumerable<WorkplaceRiskLine> lines)
    {
        var byKey = new Dictionary<string, WorkplaceRiskLine>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (IsEmptyLine(line))
            {
                continue;
            }

            var key = LineKey(line);
            if (!byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = CloneLine(line);
                continue;
            }

            if (LineScore(line) > LineScore(existing))
            {
                byKey[key] = CloneLine(line);
            }
        }

        return byKey.Values
            .OrderBy(l => ParseHazardOrder(l.HazardNumber))
            .ThenBy(l => l.HazardTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveProfession(WorkplaceRecord workplace)
    {
        var profession = (workplace.Profession ?? string.Empty).Trim();
        if (profession.Length > 0)
        {
            return profession;
        }

        return (workplace.Name ?? string.Empty).Trim();
    }

    private static bool ProfessionMatches(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ProfessionMatcher.Matches(left, right) || ProfessionMatcher.Matches(right, left);
    }

    private static string LineKey(WorkplaceRiskLine line)
    {
        var number = NormalizeKeyPart(line.HazardNumber);
        var title = NormalizeKeyPart(line.HazardTitle);
        if (number.Length > 0 && title.Length > 0)
        {
            return number + "|" + title;
        }

        if (title.Length > 0)
        {
            return "t:" + title;
        }

        if (number.Length > 0)
        {
            return "n:" + number;
        }

        var catalog = (line.HazardCatalogId ?? string.Empty).Trim();
        return catalog.Length > 0 ? "c:" + catalog : "id:" + line.Id;
    }

    private static string NormalizeKeyPart(string? value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0 || t is "-" or "—" or "–")
        {
            return string.Empty;
        }

        return t;
    }

    private static int LineScore(WorkplaceRiskLine line)
    {
        var score = 0;
        if (!IsDash(line.HazardTitle)) score += 4;
        if (!IsDash(line.HazardNumber)) score += 2;
        if (!IsDash(line.ControlMeasures)) score += 3;
        if (!IsDash(line.Severity)) score += 2;
        if (!IsDash(line.Probability)) score += 2;
        if (!IsDash(line.Source)) score += 1;
        if (!IsDash(line.ResidualRisk)) score += 1;
        return score;
    }

    private static int ParseHazardOrder(string? number)
    {
        var t = (number ?? string.Empty).Trim();
        if (t.Length == 0 || t is "-" or "—" or "–")
        {
            return int.MaxValue;
        }

        var digits = new string(t.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    private static bool IsEmptyLine(WorkplaceRiskLine line) =>
        IsDash(line.HazardTitle)
        && IsDash(line.HazardNumber)
        && IsDash(line.ControlMeasures)
        && IsDash(line.Severity)
        && IsDash(line.Probability)
        && IsDash(line.Source);

    private static bool IsDash(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return t.Length == 0 || t is "-" or "—" or "–";
    }

    private static WorkplaceRiskLine CloneLine(WorkplaceRiskLine source) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        HazardCatalogId = source.HazardCatalogId,
        HazardNumber = source.HazardNumber,
        HazardTitle = source.HazardTitle,
        Source = source.Source,
        Consequences = source.Consequences,
        WhoMaySuffer = source.WhoMaySuffer,
        InitialRisk = source.InitialRisk,
        ControlMeasures = source.ControlMeasures,
        ResidualRisk = source.ResidualRisk,
        Responsible = source.Responsible,
        Probability = source.Probability,
        Severity = source.Severity,
        Significance = source.Significance
    };
}
