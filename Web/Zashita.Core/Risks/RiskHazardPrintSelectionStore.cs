using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Сохранённый выбор опасностей для печати по организации и объекту.</summary>
public sealed class RiskHazardPrintSelectionStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _sync = new();

    public RiskHazardPrintSelectionStore(RiskDataPaths paths) => _paths = paths;

    /// <summary>null — выбора ещё не сохраняли (нужен автовыбор по картам).</summary>
    public IReadOnlyList<string>? TryGetSelectedIds(string organization) =>
        TryGetSelectedIds(organization, objectId: null);

    /// <summary>
    /// Выбор для объекта. «По организации» / пустой objectId — ключ ByOrganization (как раньше).
    /// </summary>
    public IReadOnlyList<string>? TryGetSelectedIds(string organization, string? objectId)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return null;
        }

        lock (_sync)
        {
            var doc = LoadUnlocked();
            if (RiskObjectAssessmentScope.IsOrganizationWide(objectId))
            {
                foreach (var pair in doc.ByOrganization)
                {
                    if (OrganizationMatcher.Matches(org, pair.Key)
                        || string.Equals(org, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value ?? [];
                    }
                }

                return null;
            }

            // «Все объекты» (all) и конкретные объекты — ByOrganizationObject.
            var oid = objectId!.Trim();
            foreach (var orgPair in doc.ByOrganizationObject)
            {
                if (!OrganizationMatcher.Matches(org, orgPair.Key)
                    && !string.Equals(org, orgPair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (orgPair.Value is null)
                {
                    return null;
                }

                foreach (var objPair in orgPair.Value)
                {
                    if (string.Equals(objPair.Key, oid, StringComparison.OrdinalIgnoreCase))
                    {
                        return objPair.Value ?? [];
                    }
                }
            }
        }

        return null;
    }

    public void SaveSelectedIds(string organization, IEnumerable<string> ids) =>
        SaveSelectedIds(organization, objectId: null, ids);

    public void SaveSelectedIds(string organization, string? objectId, IEnumerable<string> ids)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        var list = ids
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_sync)
        {
            var doc = LoadUnlocked();
            if (RiskObjectAssessmentScope.IsOrganizationWide(objectId))
            {
                var key = doc.ByOrganization.Keys.FirstOrDefault(k =>
                              OrganizationMatcher.Matches(org, k)
                              || string.Equals(org, k, StringComparison.OrdinalIgnoreCase))
                          ?? org;
                doc.ByOrganization[key] = list;
            }
            else
            {
                var oid = objectId!.Trim();
                var orgKey = doc.ByOrganizationObject.Keys.FirstOrDefault(k =>
                                 OrganizationMatcher.Matches(org, k)
                                 || string.Equals(org, k, StringComparison.OrdinalIgnoreCase))
                             ?? org;
                if (!doc.ByOrganizationObject.TryGetValue(orgKey, out var byObject)
                    || byObject is null)
                {
                    byObject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    doc.ByOrganizationObject[orgKey] = byObject;
                }

                var objKey = byObject.Keys.FirstOrDefault(k =>
                                 string.Equals(k, oid, StringComparison.OrdinalIgnoreCase))
                             ?? oid;
                byObject[objKey] = list;
            }

            File.WriteAllText(_paths.HazardPrintSelectionFile, JsonSerializer.Serialize(doc, JsonOpts));
        }
    }

    private RiskHazardPrintSelectionDocument LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.HazardPrintSelectionFile))
            {
                return new RiskHazardPrintSelectionDocument();
            }

            var json = File.ReadAllText(_paths.HazardPrintSelectionFile);
            return JsonSerializer.Deserialize<RiskHazardPrintSelectionDocument>(json, JsonOpts)
                   ?? new RiskHazardPrintSelectionDocument();
        }
        catch
        {
            return new RiskHazardPrintSelectionDocument();
        }
    }
}

public sealed class RiskHazardPrintSelectionDocument
{
    public Dictionary<string, List<string>> ByOrganization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>org → objectId → выбранные id опасностей.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> ByOrganizationObject { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Опасности из заполненных карт оценки рисков организации.</summary>
public static class RiskHazardPrintSelectionBuilder
{
    public static HashSet<string> CollectUsedCatalogIds(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<RiskHazardCatalogItem> catalog) =>
        CollectUsedCatalogIds(organization, workplaces, catalog, objectId: null, scopeAllObjectsWhenNull: true);

    /// <summary>
    /// Опасности из карт оценки. При заданном <paramref name="objectId"/> —
    /// только линии оценки этого объекта («по организации» = корневые RiskLines).
    /// </summary>
    public static HashSet<string> CollectUsedCatalogIds(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<RiskHazardCatalogItem> catalog,
        string? objectId) =>
        CollectUsedCatalogIds(organization, workplaces, catalog, objectId, scopeAllObjectsWhenNull: false);

    private static HashSet<string> CollectUsedCatalogIds(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<RiskHazardCatalogItem> catalog,
        string? objectId,
        bool scopeAllObjectsWhenNull)
    {
        var org = (organization ?? string.Empty).Trim();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (org.Length == 0 || catalog.Count == 0)
        {
            return used;
        }

        var byId = catalog
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var byNumber = catalog
            .Where(c => !string.IsNullOrWhiteSpace(c.Number))
            .GroupBy(c => NormalizeKey(c.Number), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byTitle = catalog
            .Where(c => !string.IsNullOrWhiteSpace(c.Title))
            .GroupBy(c => NormalizeKey(c.Title), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var useAllObjects = scopeAllObjectsWhenNull && objectId is null;
        var orgWide = RiskObjectAssessmentScope.IsOrganizationWide(objectId);

        foreach (var wp in workplaces)
        {
            if (wp.IsGroup || !wp.IsUsed)
            {
                continue;
            }

            var wpOrg = (wp.Organization ?? string.Empty).Trim();
            if (wpOrg.Length == 0
                || (!string.Equals(wpOrg, org, StringComparison.OrdinalIgnoreCase)
                    && !OrganizationMatcher.Matches(wpOrg, org)
                    && !OrganizationMatcher.Matches(org, wpOrg)))
            {
                continue;
            }

            IEnumerable<WorkplaceRiskLine> lines = useAllObjects
                ? EnumerateRiskLines(wp)
                : EnumerateRiskLinesForObject(wp, objectId, orgWide);

            foreach (var line in lines)
            {
                if (IsEmptyLine(line))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line.HazardCatalogId)
                    && byId.ContainsKey(line.HazardCatalogId))
                {
                    used.Add(line.HazardCatalogId);
                    continue;
                }

                var numberKey = NormalizeKey(line.HazardNumber);
                if (numberKey.Length > 0 && byNumber.TryGetValue(numberKey, out var byNum))
                {
                    used.Add(byNum.Id);
                    continue;
                }

                var titleKey = NormalizeKey(line.HazardTitle);
                if (titleKey.Length > 0 && byTitle.TryGetValue(titleKey, out var byName))
                {
                    used.Add(byName.Id);
                }
            }
        }

        return used;
    }

    private static IEnumerable<WorkplaceRiskLine> EnumerateRiskLinesForObject(
        WorkplaceRecord wp,
        string? objectId,
        bool orgWide)
    {
        if (orgWide)
        {
            foreach (var line in wp.RiskLines ?? [])
            {
                yield return line;
            }

            yield break;
        }

        var data = RiskObjectAssessmentScope.GetObjectData(wp, objectId!);
        foreach (var line in data.RiskLines ?? [])
        {
            yield return line;
        }
    }

    private static IEnumerable<WorkplaceRiskLine> EnumerateRiskLines(WorkplaceRecord wp)
    {
        foreach (var line in wp.RiskLines ?? [])
        {
            yield return line;
        }

        if (wp.ObjectAssessments is null || wp.ObjectAssessments.Count == 0)
        {
            yield break;
        }

        foreach (var data in wp.ObjectAssessments.Values)
        {
            if (data?.RiskLines is null)
            {
                continue;
            }

            foreach (var line in data.RiskLines)
            {
                yield return line;
            }
        }
    }

    private static bool IsEmptyLine(WorkplaceRiskLine line)
    {
        static bool Dash(string? v)
        {
            var t = (v ?? string.Empty).Trim();
            return t.Length == 0 || t is "-" or "—" or "–";
        }

        return Dash(line.HazardTitle)
               && Dash(line.HazardNumber)
               && Dash(line.ControlMeasures)
               && Dash(line.Severity)
               && Dash(line.Probability);
    }

    private static string NormalizeKey(string? value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0 || t is "-" or "—" or "–")
        {
            return string.Empty;
        }

        return t;
    }
}
