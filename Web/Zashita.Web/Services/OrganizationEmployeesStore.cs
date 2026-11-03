using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Web.Services;

public sealed class OrganizationEmployeesStore
{
    private readonly string _dataFilePath;
    private readonly object _sync = new();
    private Dictionary<string, List<OrganizationEmployee>> _data = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public OrganizationEmployeesStore(IWebHostEnvironment env)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dataFilePath = Path.Combine(appData, "organization-employees.json");
        Load();
        foreach (var pair in OrganizationEmployeesCatalog.SeedData)
        {
            SyncCatalogOrganization(pair.Key, pair.Value);
        }
    }

    public IReadOnlyList<string> GetOrganizations()
    {
        lock (_sync)
        {
            return _data.Keys
                .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<OrganizationEmployee> Get(string organization, bool includeInactive = false)
    {
        lock (_sync)
        {
            var key = ResolveKeyLocked(organization);
            if (key is null || !_data.TryGetValue(key, out var list))
            {
                return [];
            }

            return list
                .Where(e => includeInactive || EmployeeHrStatus.IsEmployed(e))
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>Одна блокировка для списка организаций (отчёты «Все организации»).</summary>
    public IReadOnlyList<(string Organization, IReadOnlyList<OrganizationEmployee> Employees)> GetActiveByOrganizations(
        IEnumerable<string> organizations)
    {
        var wanted = organizations
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        lock (_sync)
        {
            var result = new List<(string Organization, IReadOnlyList<OrganizationEmployee> Employees)>(wanted.Count);
            foreach (var organization in wanted)
            {
                var key = ResolveKeyLocked(organization);
                if (key is null || !_data.TryGetValue(key, out var list))
                {
                    result.Add((organization, []));
                    continue;
                }

                var active = list
                    .Where(e => EmployeeHrStatus.IsEmployed(e))
                    .Select(Clone)
                    .ToList();
                result.Add((organization, active));
            }

            return result;
        }
    }

    /// <summary>Найти ключ организации с учётом сокращений (В.В. ↔ Виталий Валерьевич).</summary>
    public string? ResolveOrganizationKey(string organization)
    {
        lock (_sync)
        {
            return ResolveKeyLocked(organization);
        }
    }

    private string? ResolveKeyLocked(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return null;
        }

        var query = organization.Trim();
        if (_data.TryGetValue(query, out var exactList) && exactList.Count > 0)
        {
            return _data.Keys.First(k => string.Equals(k, query, StringComparison.OrdinalIgnoreCase));
        }

        // Пустой ключ «ИП … Сергей…» при людях под «ИП … С.А.» — ищем алиас с штатом.
        string? matchedWithPeople = null;
        string? matchedAny = null;
        foreach (var key in _data.Keys)
        {
            if (!OrganizationMatcher.MatchesForCatalog(query, key)
                && !OrganizationMatcher.Matches(query, key))
            {
                continue;
            }

            matchedAny ??= key;
            if (_data[key].Count > 0)
            {
                matchedWithPeople = key;
                break;
            }
        }

        return matchedWithPeople ?? matchedAny
               ?? (exactList is not null
                   ? _data.Keys.First(k => string.Equals(k, query, StringComparison.OrdinalIgnoreCase))
                   : null);
    }

    public void Save(string organization, IEnumerable<OrganizationEmployee> employees)
    {
        var preferred = organization.Trim();
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return;
        }

        lock (_sync)
        {
            var matchingKeys = _data.Keys
                .Where(k => string.Equals(k, preferred, StringComparison.OrdinalIgnoreCase)
                            || OrganizationMatcher.MatchesForCatalog(preferred, k)
                            || OrganizationMatcher.Matches(preferred, k))
                .ToList();

            foreach (var alias in matchingKeys)
            {
                _data.Remove(alias);
            }

            _data[preferred] = employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(Clone)
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PersistLocked();
        }
    }

    /// <summary>
    /// Перенести штат с «ИП … О.С.» на полное ФИО («Оксана Сергеевна»), если оно есть
    /// в справочнике или в ключах JSON. Иначе карточка с полным именем пустая.
    /// </summary>
    public int PreferFullIpNameKeys(IEnumerable<string>? preferredFullNames = null)
    {
        lock (_sync)
        {
            var preferredFull = (preferredFullNames ?? OrganizationLegalEntitySplit.PreservedOrganizations)
                .Where(n => !string.IsNullOrWhiteSpace(n)
                            && n.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                            && !OrganizationMatcher.IsIpInitialsForm(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? ResolveFullKey(string initialsKey) =>
                preferredFull.FirstOrDefault(p =>
                    OrganizationMatcher.Matches(p, initialsKey)
                    && !OrganizationLegalEntitySplit.AreKnownDistinct(p, initialsKey))
                ?? _data.Keys.FirstOrDefault(k =>
                    !OrganizationMatcher.IsIpInitialsForm(k)
                    && OrganizationMatcher.Matches(k, initialsKey)
                    && !OrganizationLegalEntitySplit.AreKnownDistinct(k, initialsKey));

            var moved = 0;
            var changed = false;
            foreach (var initialsKey in _data.Keys
                         .Where(OrganizationMatcher.IsIpInitialsForm)
                         .ToList())
            {
                var fullKey = ResolveFullKey(initialsKey);
                if (fullKey is null
                    || string.Equals(fullKey, initialsKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_data.TryGetValue(initialsKey, out var people))
                {
                    continue;
                }

                if (people.Count == 0)
                {
                    _data.Remove(initialsKey);
                    changed = true;
                    continue;
                }

                var already = _data.TryGetValue(fullKey, out var existing)
                    ? existing.ToList()
                    : [];
                foreach (var person in people)
                {
                    var personKey = OneCEmployeeListParser.BuildMatchKey(fullKey, person);
                    var personEmployed = EmployeeHrStatus.IsEmployed(person);
                    var exists = already.Any(e =>
                        string.Equals(
                            OneCEmployeeListParser.BuildMatchKey(fullKey, e),
                            personKey,
                            StringComparison.OrdinalIgnoreCase)
                        && EmployeeHrStatus.IsEmployed(e) == personEmployed);
                    if (!exists)
                    {
                        already.Add(Clone(person));
                        moved++;
                    }
                }

                _data[fullKey] = already
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _data.Remove(initialsKey);
                changed = true;
            }

            if (changed)
            {
                PersistLocked();
            }

            return moved;
        }
    }

    /// <summary>
    /// Разделить ООО КСК и ООО Компания Строй Каркас, если сотрудники слиты в один ключ.
    /// </summary>
    public bool ApplyLegalEntitySplits()
    {
        lock (_sync)
        {
            if (!OrganizationLegalEntitySplit.SplitKskEmployees(_data, out var next))
            {
                return false;
            }

            _data = next;
            PersistLocked();
            return true;
        }
    }

    public void Upsert(string organization, OrganizationEmployee employee)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(employee.Name))
        {
            return;
        }

        lock (_sync)
        {
            if (!_data.TryGetValue(org, out var list))
            {
                list = [];
                _data[org] = list;
            }

            var item = Clone(employee);
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            var idx = list.FindIndex(e => string.Equals(e.Id, item.Id, StringComparison.Ordinal));
            if (idx < 0)
            {
                var employmentKey = OneCEmployeeListParser.BuildMatchKey(org, item);
                idx = list.FindIndex(e =>
                    string.Equals(
                        OneCEmployeeListParser.BuildMatchKey(org, e),
                        employmentKey,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (idx < 0 && !string.IsNullOrWhiteSpace(item.Snils))
            {
                var snils = OneCEmployeeListParser.NormalizeSnils(item.Snils);
                idx = list.FindIndex(e =>
                    OneCEmployeeListParser.NormalizeSnils(e.Snils) == snils && snils.Length > 0
                    && string.Equals(e.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Profession, item.Profession, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.EmploymentType, item.EmploymentType, StringComparison.OrdinalIgnoreCase)
                    && e.EmploymentDate == item.EmploymentDate);
            }

            if (idx < 0 && !string.IsNullOrWhiteSpace(item.ExternalId))
            {
                idx = list.FindIndex(e =>
                    string.Equals(e.ExternalId, item.ExternalId, StringComparison.OrdinalIgnoreCase));
            }

            if (idx < 0)
            {
                idx = list.FindIndex(e =>
                    string.Equals(e.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (idx >= 0)
            {
                item.Id = list[idx].Id;
                // Адрес часто нет в выгрузке 1С — не затираем уже сохранённый.
                if (string.IsNullOrWhiteSpace(item.HomeAddress))
                {
                    item.HomeAddress = list[idx].HomeAddress;
                }

                list[idx] = item;
            }
            else
            {
                list.Add(item);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            PersistLocked();
        }
    }

    public void Delete(string organization, string employeeId)
    {
        lock (_sync)
        {
            if (!_data.TryGetValue(organization, out var list))
            {
                return;
            }

            list.RemoveAll(e => string.Equals(e.Id, employeeId, StringComparison.Ordinal));
            PersistLocked();
        }
    }

    public void RenameOrganization(string oldName, string newName)
    {
        lock (_sync)
        {
            var from = (oldName ?? string.Empty).Trim();
            var to = (newName ?? string.Empty).Trim();
            if (from.Length == 0 || to.Length == 0
                || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var key = _data.Keys.FirstOrDefault(k =>
                string.Equals(k, from, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(from, k)
                || OrganizationMatcher.Matches(k, from));
            if (key is null)
            {
                return;
            }

            var employees = _data[key];
            _data.Remove(key);

            var targetKey = _data.Keys.FirstOrDefault(k =>
                string.Equals(k, to, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(to, k)
                || OrganizationMatcher.Matches(k, to));
            if (targetKey is not null)
            {
                var existing = _data[targetKey];
                var knownIds = existing.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var person in employees)
                {
                    if (!knownIds.Contains(person.Id))
                    {
                        existing.Add(person);
                    }
                }

                if (!string.Equals(targetKey, to, StringComparison.Ordinal))
                {
                    _data.Remove(targetKey);
                    _data[to] = existing;
                }
            }
            else
            {
                _data[to] = employees;
            }

            PersistLocked();
        }
    }

    public void RemoveOrganization(string organization)
    {
        lock (_sync)
        {
            var key = _data.Keys.FirstOrDefault(k => string.Equals(k, organization, StringComparison.OrdinalIgnoreCase));
            if (key is null)
            {
                return;
            }

            _data.Remove(key);
            PersistLocked();
        }
    }

    /// <summary>
    /// Замена штата только по организациям из выгрузки.
    /// Организации, которых нет в текущей партии CSV, не трогаем —
    /// иначе при частичной выгрузке пропадают новые сотрудники других юрлиц.
    /// </summary>
    public void ReplaceAll(IReadOnlyDictionary<string, List<OrganizationEmployee>> byOrganization)
    {
        lock (_sync)
        {
            foreach (var (org, list) in byOrganization)
            {
                if (string.IsNullOrWhiteSpace(org))
                {
                    continue;
                }

                _data[org.Trim()] = list
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(Clone)
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            PersistLocked();
        }
    }

    /// <summary>
    /// Полная перезапись справочника: только организации и строки из выгрузки.
    /// Старые юрлица / «призраки» вне CSV удаляются. Перед записью — копия файла.
    /// </summary>
    public string ReplaceEntire(IReadOnlyDictionary<string, List<OrganizationEmployee>> byOrganization)
    {
        lock (_sync)
        {
            var backupPath = BackupLocked("before-rebuild");
            var next = new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (org, list) in byOrganization)
            {
                if (string.IsNullOrWhiteSpace(org))
                {
                    continue;
                }

                next[org.Trim()] = list
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(Clone)
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            _data = next;
            PersistLocked();
            return backupPath;
        }
    }

    /// <summary>Копия organization-employees.json в App_Data/backups.</summary>
    public string BackupNow(string reason = "manual")
    {
        lock (_sync)
        {
            return BackupLocked(reason);
        }
    }

    private string BackupLocked(string reason)
    {
        var appData = Path.GetDirectoryName(_dataFilePath) ?? ".";
        var backups = Path.Combine(appData, "backups");
        Directory.CreateDirectory(backups);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeReason = string.Join("-", (reason ?? "backup")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim('-');
        if (string.IsNullOrWhiteSpace(safeReason))
        {
            safeReason = "backup";
        }

        var target = Path.Combine(backups, $"organization-employees.{safeReason}-{stamp}.json");
        if (File.Exists(_dataFilePath))
        {
            File.Copy(_dataFilePath, target, overwrite: true);
        }
        else
        {
            File.WriteAllText(target, "{}");
        }

        return target;
    }

    /// <summary>Снимок всего справочника (включая уволенных) — для переноса в архив при синке.</summary>
    public IReadOnlyDictionary<string, List<OrganizationEmployee>> SnapshotIncludingInactive()
    {
        lock (_sync)
        {
            return _data.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(Clone).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Удалить пустые дубли одного человека (ФИО из СИЗ без СНИЛС/дат при наличии полной карточки из 1С).
    /// </summary>
    public int PurgeDuplicatePersonShells()
    {
        lock (_sync)
        {
            var removed = 0;
            foreach (var org in _data.Keys.ToList())
            {
                if (!_data.TryGetValue(org, out var list) || list.Count < 2)
                {
                    continue;
                }

                var removeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in list.GroupBy(EmployeePersonMatcher.PersonKey, StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() < 2)
                    {
                        continue;
                    }

                    var records = group.ToList();
                    var shells = records.Where(EmployeePersonMatcher.IsShellRecord).ToList();
                    if (shells.Count == 0)
                    {
                        continue;
                    }

                    var fullRecords = records.Where(e => !EmployeePersonMatcher.IsShellRecord(e)).ToList();
                    if (fullRecords.Count == 0)
                    {
                        var best = records
                            .OrderByDescending(EmployeePersonMatcher.CompletenessScore)
                            .First();
                        foreach (var duplicate in records.Where(e => !ReferenceEquals(e, best) && e.Id != best.Id))
                        {
                            removeIds.Add(duplicate.Id);
                        }

                        continue;
                    }

                    foreach (var shell in shells)
                    {
                        removeIds.Add(shell.Id);
                    }
                }

                if (removeIds.Count == 0)
                {
                    continue;
                }

                var before = list.Count;
                list.RemoveAll(e => removeIds.Contains(e.Id));
                removed += before - list.Count;
                if (list.Count == 0)
                {
                    _data.Remove(org);
                }
            }

            if (removed > 0)
            {
                PersistLocked();
            }

            return removed;
        }
    }

    /// <summary>Схлопнуть несколько карточек одного человека в одну (по СНИЛС/ФИО).
    /// Работающих и уволенных схлопываем отдельно — уволенные нужны для архива обучения.</summary>
    public int CollapseDuplicatePersonRecords()
    {
        lock (_sync)
        {
            var removed = 0;
            foreach (var org in _data.Keys.ToList())
            {
                if (!_data.TryGetValue(org, out var list) || list.Count < 2)
                {
                    continue;
                }

                var before = list.Count;
                var employed = list.Where(EmployeeHrStatus.IsEmployed).ToList();
                var dismissed = list.Where(e => !EmployeeHrStatus.IsEmployed(e)).ToList();

                var collapsedEmployed = EmployeeReportCollapse.Collapse(employed);
                var employedKeys = collapsedEmployed
                    .Select(EmployeePersonMatcher.PersonKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Уволенный с тем же СНИЛС/ФИО, что и работающий — это повторный приём; архивную копию не копим.
                var dismissedOnly = dismissed
                    .Where(d => !employedKeys.Contains(EmployeePersonMatcher.PersonKey(d)))
                    .ToList();
                var collapsedDismissed = EmployeeReportCollapse.Collapse(dismissedOnly);

                var next = collapsedEmployed
                    .Concat(collapsedDismissed)
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (next.Count >= before && ReferenceEquals(list, _data[org]))
                {
                    // Даже при том же количестве могли поменяться поля — перезаписываем при реальном сжатии.
                    if (next.Count == before)
                    {
                        continue;
                    }
                }

                removed += Math.Max(0, before - next.Count);
                _data[org] = next;
            }

            if (removed > 0)
            {
                PersistLocked();
            }

            return removed;
        }
    }

    /// <summary>Удалить уволенных и неактивных из справочника (остаются только работающие).</summary>
    public int PurgeInactiveEmployees()
    {
        lock (_sync)
        {
            var removed = 0;
            foreach (var org in _data.Keys.ToList())
            {
                if (!_data.TryGetValue(org, out var list))
                {
                    continue;
                }

                var before = list.Count;
                list.RemoveAll(e => !EmployeeHrStatus.IsEmployed(e));
                removed += before - list.Count;
                if (list.Count == 0)
                {
                    _data.Remove(org);
                }
            }

            if (removed > 0)
            {
                PersistLocked();
            }

            return removed;
        }
    }

    public void SyncCatalogOrganization(string organization, IEnumerable<OrganizationEmployee> employees)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            // Seed only if org is empty — do not overwrite 1C sync data.
            if (_data.TryGetValue(org, out var existing) && existing.Count > 0)
            {
                return;
            }

            _data[org] = employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(Clone)
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PersistLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_dataFilePath))
            {
                _data = new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var json = File.ReadAllText(_dataFilePath);
            _data = JsonSerializer.Deserialize<Dictionary<string, List<OrganizationEmployee>>>(json, JsonOpts)
                ?? new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _data = new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistLocked() =>
        File.WriteAllText(_dataFilePath, JsonSerializer.Serialize(_data, JsonOpts));

    private static OrganizationEmployee Clone(OrganizationEmployee source) => new()
    {
        Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
        ExternalId = source.ExternalId?.Trim() ?? string.Empty,
        Name = source.Name?.Trim() ?? string.Empty,
        LastName = source.LastName?.Trim() ?? string.Empty,
        FirstName = source.FirstName?.Trim() ?? string.Empty,
        Patronymic = source.Patronymic?.Trim() ?? string.Empty,
        Profession = source.Profession?.Trim() ?? string.Empty,
        Department = source.Department?.Trim() ?? string.Empty,
        Snils = OneCEmployeeListParser.NormalizeSnils(source.Snils),
        HomeAddress = source.HomeAddress?.Trim() ?? string.Empty,
        BirthDate = source.BirthDate,
        EmploymentDate = source.EmploymentDate,
        TransferDate = source.TransferDate,
        DismissalDate = source.DismissalDate,
        EmploymentType = source.EmploymentType?.Trim() ?? string.Empty,
        HrStatus = source.HrStatus?.Trim() ?? string.Empty,
        IsActive = source.IsActive,
        SyncedAt = source.SyncedAt
    };
}
