using System.Text.Json;

namespace Zashita.Core.Employees;

public sealed class StaffingChangeStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly string _filePath;
    private List<StaffingChangeEvent> _events = [];

    public StaffingChangeStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "staffing-changes.json");
        LoadLocked();
    }

    public IReadOnlyList<StaffingChangeEvent> LoadRecent(int take = 50, bool onlyOpen = true)
    {
        lock (_sync)
        {
            IEnumerable<StaffingChangeEvent> query = _events
                .OrderByDescending(e => e.OccurredAt);
            if (onlyOpen)
            {
                query = query.Where(e => !e.Acknowledged);
            }

            return query.Take(Math.Clamp(take, 1, 500)).ToList();
        }
    }

    /// <summary>События за интервал (для расшифровки старых прогонов синка).</summary>
    public IReadOnlyList<StaffingChangeEvent> LoadAround(DateTime fromUtc, DateTime toUtc, int take = 4000)
    {
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        lock (_sync)
        {
            return _events
                .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt <= toUtc)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Organization, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Person, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Clamp(take, 1, 8000))
                .ToList();
        }
    }

    public IReadOnlyList<StaffingChangeEvent> LoadForOrganization(
        string organization,
        int take = 200,
        bool onlyHiredOrTransferred = true)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return [];
        }

        lock (_sync)
        {
            IEnumerable<StaffingChangeEvent> query = _events
                .Where(e => OrganizationMatcher.Matches(org, e.Organization)
                            || string.Equals(e.Organization, org, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.OccurredAt);
            if (onlyHiredOrTransferred)
            {
                query = query.Where(e => e.Kind is StaffingChangeKind.Hired or StaffingChangeKind.Transferred);
            }

            return query.Take(Math.Clamp(take, 1, 500)).ToList();
        }
    }

    public void Append(IEnumerable<StaffingChangeEvent> events)
    {
        var batch = events?.Where(e => e is not null).ToList() ?? [];
        if (batch.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            foreach (var incoming in batch)
            {
                if (TryMergeDuplicateOpen(incoming))
                {
                    continue;
                }

                _events.Add(incoming);
            }

            CollapseDuplicateOpenTransfersLocked();

            if (_events.Count > 2000)
            {
                _events = _events
                    .OrderByDescending(e => e.OccurredAt)
                    .Take(2000)
                    .ToList();
            }

            SaveLocked();
        }
    }

    /// <summary>
    /// Если уже есть открытый такой же приём/перевод — не плодим копии при каждой синхронизации 1С.
    /// </summary>
    private bool TryMergeDuplicateOpen(StaffingChangeEvent incoming)
    {
        if (incoming.Kind is not (StaffingChangeKind.Hired or StaffingChangeKind.Transferred))
        {
            return false;
        }

        var key = IdentityKey(incoming);
        if (key.Length == 0)
        {
            return false;
        }

        var existing = _events
            .Where(e => !e.Acknowledged
                        && e.Kind is StaffingChangeKind.Hired or StaffingChangeKind.Transferred
                        && string.Equals(IdentityKey(e), key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();

        if (existing is null)
        {
            return false;
        }

        // Тот же человек уже в открытых уведомлениях — обновляем поля, без новой строки.
        if (incoming.Kind == StaffingChangeKind.Transferred)
        {
            existing.Kind = StaffingChangeKind.Transferred;
        }

        if (!string.IsNullOrWhiteSpace(incoming.FromOrganization))
        {
            existing.FromOrganization = incoming.FromOrganization;
        }

        if (!string.IsNullOrWhiteSpace(incoming.NewProfession))
        {
            existing.NewProfession = incoming.NewProfession;
        }

        if (!string.IsNullOrWhiteSpace(incoming.NewDepartment))
        {
            existing.NewDepartment = incoming.NewDepartment;
        }

        if (!string.IsNullOrWhiteSpace(incoming.OldProfession) &&
            string.IsNullOrWhiteSpace(existing.OldProfession))
        {
            existing.OldProfession = incoming.OldProfession;
        }

        if (!string.IsNullOrWhiteSpace(incoming.OldDepartment) &&
            string.IsNullOrWhiteSpace(existing.OldDepartment))
        {
            existing.OldDepartment = incoming.OldDepartment;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Message))
        {
            existing.Message = incoming.Message;
        }

        existing.OccurredAt = incoming.OccurredAt > existing.OccurredAt
            ? incoming.OccurredAt
            : existing.OccurredAt;
        existing.ResetApplied = existing.ResetApplied || incoming.ResetApplied;
        return true;
    }

    private void CollapseDuplicateOpenTransfersLocked()
    {
        var groups = _events
            .Where(e => !e.Acknowledged && e.Kind == StaffingChangeKind.Transferred)
            .GroupBy(IdentityKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0 && g.Count() > 1);

        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(e => e.OccurredAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                ordered[i].Acknowledged = true;
            }
        }
    }

    private static string IdentityKey(StaffingChangeEvent e)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(e.Snils);
        var org = (e.Organization ?? string.Empty).Trim().ToLowerInvariant();
        var person = (e.Person ?? string.Empty).Trim().ToLowerInvariant();
        if (snils.Length == 11)
        {
            return $"s:{snils}|{org}";
        }

        return person.Length == 0 ? string.Empty : $"p:{person}|{org}";
    }

    public int AcknowledgeAll()
    {
        lock (_sync)
        {
            var count = 0;
            foreach (var item in _events.Where(e => !e.Acknowledged))
            {
                item.Acknowledged = true;
                count++;
            }

            if (count > 0)
            {
                SaveLocked();
            }

            return count;
        }
    }

    public int AcknowledgeByIds(IEnumerable<string>? ids)
    {
        var wanted = new HashSet<string>(
            (ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return 0;
        }

        lock (_sync)
        {
            var count = 0;
            foreach (var item in _events)
            {
                if (item.Acknowledged || !wanted.Contains(item.Id))
                {
                    continue;
                }

                item.Acknowledged = true;
                count++;
            }

            if (count > 0)
            {
                SaveLocked();
            }

            return count;
        }
    }

    /// <summary>Недавние переводы для уведомлений на главной.</summary>
    public IReadOnlyList<StaffingChangeEvent> LoadTransferNotices(
        string? organization = null,
        int take = 12,
        int maxAgeDays = 45)
    {
        var org = (organization ?? string.Empty).Trim();
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(maxAgeDays, 1, 365));

        lock (_sync)
        {
            var beforeAck = _events.Count(e => e.Acknowledged);
            CollapseDuplicateOpenTransfersLocked();
            if (_events.Count(e => e.Acknowledged) != beforeAck)
            {
                SaveLocked();
            }

            return _events
                .Where(e => !e.Acknowledged)
                .Where(e => e.Kind == StaffingChangeKind.Transferred)
                .Where(e => e.OccurredAt >= since)
                .Where(e =>
                {
                    if (org.Length == 0)
                    {
                        return true;
                    }

                    return OrganizationMatcher.Matches(org, e.Organization)
                           || string.Equals(e.Organization, org, StringComparison.OrdinalIgnoreCase)
                           || OrganizationMatcher.Matches(org, e.FromOrganization)
                           || string.Equals(e.FromOrganization, org, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(e => e.OccurredAt)
                .GroupBy(e =>
                {
                    var key = IdentityKey(e);
                    return key.Length == 0 ? $"id:{e.Id}" : key;
                }, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(Math.Clamp(take, 1, 100))
                .ToList();
        }
    }

    private void LoadLocked()
    {
        if (!File.Exists(_filePath))
        {
            _events = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _events = JsonSerializer.Deserialize<List<StaffingChangeEvent>>(json, JsonOpts) ?? [];
        }
        catch
        {
            _events = [];
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(_events, JsonOpts);
        File.WriteAllText(_filePath, json);
    }
}
