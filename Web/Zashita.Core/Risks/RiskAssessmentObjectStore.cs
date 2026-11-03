using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public sealed class RiskAssessmentObjectStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _sync = new();

    public RiskAssessmentObjectStore(RiskDataPaths paths) => _paths = paths;

    public IReadOnlyList<RiskAssessmentObject> Load(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return [];
        }

        lock (_sync)
        {
            EnsureOrganizationWideUnlocked(org);
            return LoadUnlocked()
                .Where(x => OrganizationMatcher.Matches(org, x.Organization)
                            || string.Equals(org, x.Organization, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.IsOrganizationWide ? 0 : 1)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public RiskAssessmentObject? Find(string organization, string id)
    {
        var list = Load(organization);
        return list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public RiskAssessmentObject EnsureOrganizationWide(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        lock (_sync)
        {
            return EnsureOrganizationWideUnlocked(org);
        }
    }

    public RiskAssessmentObject Create(
        string organization,
        string name,
        IEnumerable<string>? workplaceIds = null,
        string? officialName = null)
    {
        var org = (organization ?? string.Empty).Trim();
        var title = (name ?? string.Empty).Trim();
        var official = (officialName ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            throw new ArgumentException("Организация не задана.", nameof(organization));
        }

        if (title.Length == 0)
        {
            throw new ArgumentException("Укажите название объекта.", nameof(name));
        }

        lock (_sync)
        {
            EnsureOrganizationWideUnlocked(org);
            var all = LoadUnlocked();
            var existing = all.FirstOrDefault(x =>
                (OrganizationMatcher.Matches(org, x.Organization)
                 || string.Equals(org, x.Organization, StringComparison.OrdinalIgnoreCase))
                && string.Equals(x.Name, title, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (official.Length > 0 && string.IsNullOrWhiteSpace(existing.OfficialName))
                {
                    existing.OfficialName = official;
                    existing.ModifiedAt = DateTime.UtcNow;
                    SaveUnlocked(all);
                }

                return existing;
            }

            var maxSort = all
                .Where(x => OrganizationMatcher.Matches(org, x.Organization)
                            || string.Equals(org, x.Organization, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.SortOrder)
                .DefaultIfEmpty(0)
                .Max();

            var created = new RiskAssessmentObject
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = org,
                Name = title,
                OfficialName = official,
                IsOrganizationWide = false,
                WorkplaceIds = (workplaceIds ?? [])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SortOrder = maxSort + 1,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };
            all.Add(created);
            SaveUnlocked(all);
            return created;
        }
    }

    public bool SaveMembership(string organization, string id, IEnumerable<string> workplaceIds)
    {
        lock (_sync)
        {
            var all = LoadUnlocked();
            var item = FindInOrgUnlocked(all, organization, id);
            if (item is null || item.IsOrganizationWide)
            {
                return false;
            }

            item.WorkplaceIds = (workplaceIds ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            item.ModifiedAt = DateTime.UtcNow;
            SaveUnlocked(all);
            return true;
        }
    }

    /// <summary>Сохраняет объект целиком (состав карт и метаданные).</summary>
    public bool Upsert(RiskAssessmentObject item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            return false;
        }

        lock (_sync)
        {
            var all = LoadUnlocked();
            var idx = all.FindIndex(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            item.WorkplaceIds ??= [];
            item.ModifiedAt = DateTime.UtcNow;
            if (idx >= 0)
            {
                all[idx] = item;
            }
            else
            {
                all.Add(item);
            }

            SaveUnlocked(all);
            return true;
        }
    }

    public bool Rename(string organization, string id, string name, string? officialName = null)
    {
        var title = (name ?? string.Empty).Trim();
        var org = (organization ?? string.Empty).Trim();
        if (title.Length == 0 || string.IsNullOrWhiteSpace(id) || org.Length == 0)
        {
            return false;
        }

        lock (_sync)
        {
            var all = LoadUnlocked();
            // Id «org-wide» общий у всех юрлиц — ищем строго по организации.
            var item = FindInOrgUnlocked(all, org, id);
            if (item is null)
            {
                return false;
            }

            // Дубли имени среди объектов той же организации — не даём.
            var clash = all.Any(x =>
                !ReferenceEquals(x, item)
                && SameOrganization(org, x.Organization)
                && string.Equals(x.Name, title, StringComparison.OrdinalIgnoreCase));
            if (clash)
            {
                return false;
            }

            item.Name = title;
            if (officialName is not null)
            {
                item.OfficialName = officialName.Trim();
            }

            item.ModifiedAt = DateTime.UtcNow;
            SaveUnlocked(all);
            return true;
        }
    }

    public bool Delete(string organization, string id)
    {
        lock (_sync)
        {
            var all = LoadUnlocked();
            var item = FindInOrgUnlocked(all, organization, id);
            if (item is null || item.IsOrganizationWide)
            {
                return false;
            }

            all.Remove(item);
            SaveUnlocked(all);
            return true;
        }
    }

    private static bool SameOrganization(string organization, string? candidate) =>
        OrganizationMatcher.Matches(organization, candidate)
        || string.Equals(organization, candidate, StringComparison.OrdinalIgnoreCase);

    private static RiskAssessmentObject? FindInOrgUnlocked(
        List<RiskAssessmentObject> all,
        string organization,
        string id)
    {
        var org = (organization ?? string.Empty).Trim();
        var key = (id ?? string.Empty).Trim();
        if (org.Length == 0 || key.Length == 0)
        {
            return null;
        }

        return all.FirstOrDefault(x =>
            string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase)
            && SameOrganization(org, x.Organization));
    }

    private RiskAssessmentObject EnsureOrganizationWideUnlocked(string org)
    {
        var all = LoadUnlocked();
        var existing = all.FirstOrDefault(x =>
            x.IsOrganizationWide
            && (OrganizationMatcher.Matches(org, x.Organization)
                || string.Equals(org, x.Organization, StringComparison.OrdinalIgnoreCase)));
        if (existing is not null)
        {
            if (!string.Equals(existing.Id, RiskAssessmentObjectIds.OrganizationWide, StringComparison.OrdinalIgnoreCase))
            {
                existing.Id = RiskAssessmentObjectIds.OrganizationWide;
                // Имя не затираем — пользователь мог переименовать вкладку.
                if (string.IsNullOrWhiteSpace(existing.Name))
                {
                    existing.Name = RiskAssessmentObjectIds.OrganizationWideName;
                }

                existing.ModifiedAt = DateTime.UtcNow;
                SaveUnlocked(all);
            }

            return existing;
        }

        var created = new RiskAssessmentObject
        {
            Id = RiskAssessmentObjectIds.OrganizationWide,
            Organization = org,
            Name = RiskAssessmentObjectIds.OrganizationWideName,
            IsOrganizationWide = true,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
        all.Add(created);
        SaveUnlocked(all);
        return created;
    }

    private List<RiskAssessmentObject> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.AssessmentObjectsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.AssessmentObjectsFile);
            return JsonSerializer.Deserialize<List<RiskAssessmentObject>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveUnlocked(List<RiskAssessmentObject> items)
    {
        var dir = Path.GetDirectoryName(_paths.AssessmentObjectsFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_paths.AssessmentObjectsFile, JsonSerializer.Serialize(items, JsonOpts));
    }
}
