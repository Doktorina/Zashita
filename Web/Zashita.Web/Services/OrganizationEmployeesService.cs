using Zashita.Core.Employees;

namespace Zashita.Web.Services;

public sealed class OrganizationEmployeesService
{
    private readonly OrganizationEmployeesStore _store;
    private readonly PpeEmployeesService _source;
    private readonly OneCEmployeeSyncService? _oneCSync;
    private OneCDismissedEmployeeIndex? _dismissedIndex;
    private DateTime _dismissedIndexLoadedAt = DateTime.MinValue;
    private string _dismissedIndexStamp = string.Empty;

    public OrganizationEmployeesService(
        OrganizationEmployeesStore store,
        PpeEmployeesService source,
        OneCEmployeeSyncService? oneCSync = null)
    {
        _store = store;
        _source = source;
        _oneCSync = oneCSync;
    }

    /// <summary>
    /// Актуальный штат из папки организации (organization-employees).
    /// Единая точка: тулбар, журналы, обучение, МО, риски, вкладка «Организации».
    /// </summary>
    public IReadOnlyList<OrganizationEmployee> Load(string organization) =>
        LoadCanonicalStaff(organization);

    /// <summary>Тот же штат, что <see cref="Load"/> — для рисков / СОУТ / СИЗ.</summary>
    public IReadOnlyList<OrganizationEmployee> LoadForReport(string organization) =>
        LoadCanonicalStaff(organization);

    /// <summary>Тот же штат, что <see cref="Load"/> — для обучения.</summary>
    public IReadOnlyList<OrganizationEmployee> LoadForTrainingReport(string organization) =>
        LoadCanonicalStaff(organization);

    /// <summary>Тот же штат, что <see cref="Load"/> — для медосмотров.</summary>
    public IReadOnlyList<OrganizationEmployee> LoadForMedReport(string organization) =>
        LoadCanonicalStaff(organization);

    /// <summary>
    /// Штат строго из карточек организации после синка 1С.
    /// Без Excel/СИЗ и без «виртуального» ИП, которого нет в JSON.
    /// </summary>
    private IReadOnlyList<OrganizationEmployee> LoadCanonicalStaff(string organization)
    {
        EnsureImported(organization);
        var all = _store.Get(organization, includeInactive: true)
            .Where(e => IsValidEmployeeName(e.Name))
            .Select(Clone)
            .ToList();
        var employed = FilterReportEmployees(
            organization,
            DeduplicateByEmploymentKey(organization, all));
        // Поднимаем самого ИП только если он уже есть в JSON (активный/архив), не выдумываем.
        return EmployeeReportCollapse.Collapse(
            EnsureIndividualEntrepreneur(
                organization,
                employed,
                inactiveCandidates: all,
                createIfMissing: false));
    }

    /// <summary>ЮЛ, в которых есть актуальный штат (для отчёта «Все» — не терять людей вне выпадающего списка).</summary>
    public IReadOnlyList<string> GetOrganizationsWithEmployedStaff()
    {
        return _store.GetOrganizations()
            .Where(org => _store.Get(org, includeInactive: false)
                .Any(e => IsValidEmployeeName(e.Name) && EmployeeHrStatus.IsEmployed(e)))
            .OrderBy(org => org, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<(string Organization, OrganizationEmployee Employee)> LoadPairsForReport(
        IEnumerable<string> organizations) =>
        LoadPairs(organizations, LoadForReport);

    public IReadOnlyList<(string Organization, OrganizationEmployee Employee)> LoadPairsForMedReport(
        IEnumerable<string> organizations) =>
        LoadPairs(organizations, LoadForMedReport);

    /// <summary>
    /// Список ЮЛ для отчёта «Все»: видимые + со штатом, без дублей алиасов
    /// («СтандартСтрой» / «ССТРОЙ» один раз).
    /// </summary>
    public IReadOnlyList<string> ResolveOrganizationsForAllReports(
        IEnumerable<string> visibleOrganizations,
        Func<string, bool>? isHidden = null)
    {
        var combined = visibleOrganizations
            .Concat(GetOrganizationsWithEmployedStaff())
            .Where(org => !string.IsNullOrWhiteSpace(org))
            .Select(org => org.Trim())
            .Where(org => isHidden is null || !isHidden(org));

        // Ключ склада 1С — канон; иначе два написания одного ЮЛ → люди ×2 в счётчике.
        var resolved = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in OrganizationMatcher.DeduplicateOrganizations(
                     combined,
                     OrganizationCanonicalRegistry.CanonicalNames))
        {
            var key = _store.ResolveOrganizationKey(org) ?? org;
            if (!seenKeys.Add(key))
            {
                continue;
            }

            resolved.Add(key);
        }

        return resolved
            .OrderBy(org => org, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private List<(string Organization, OrganizationEmployee Employee)> LoadPairs(
        IEnumerable<string> organizations,
        Func<string, IReadOnlyList<OrganizationEmployee>> loader)
    {
        var result = new List<(string Organization, OrganizationEmployee Employee)>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var organization in organizations
                     .Where(o => !string.IsNullOrWhiteSpace(o))
                     .Select(o => o.Trim()))
        {
            var key = _store.ResolveOrganizationKey(organization) ?? organization;
            if (!seenKeys.Add(key))
            {
                continue;
            }

            foreach (var employee in loader(key))
            {
                result.Add((key, employee));
            }
        }

        return result;
    }

    /// <summary>Все сотрудники организации, включая уволенных — для справочника профессий.</summary>
    public IReadOnlyList<OrganizationEmployee> LoadIncludingInactive(string organization)
    {
        var stored = _store.Get(organization, includeInactive: true)
            .Where(e => IsValidEmployeeName(e.Name))
            .Select(Clone)
            .ToList();
        if (stored.Count > 0)
        {
            return EmployeeReportCollapse.Collapse(
                DeduplicateByEmploymentKey(organization, stored));
        }

        EnsureImported(organization);
        return EmployeeReportCollapse.Collapse(
            DeduplicateByEmploymentKey(organization, _store.Get(organization, includeInactive: true)));
    }

    /// <summary>Сотрудники организации — тот же список, что <see cref="Load"/>.</summary>
    public IReadOnlyList<OrganizationEmployee> LoadDirectory(string organization) =>
        LoadCanonicalStaff(organization);

    /// <summary>
    /// Актуальный штат: берём из карточек организации (store).
    /// Один человек может быть в двух ЮЛ — по каждому ЮЛ своя карточка.
    /// CSV 1С только снимает явно уволенных в этом ЮЛ; отсутствие строки в индексе
    /// (рассинхрон имени ЮЛ / СНИЛС) больше не прячет людей из штата.
    /// </summary>
    private bool IsDirectoryEmployed(string organization, OrganizationEmployee employee)
    {
        // Карточка должна быть «в штате» по статусу и датам (как в ETL).
        if (!OneCStaffEligibility.IsEmployedRecord(employee)
            && !EmployeeHrStatus.IsEmployed(employee))
        {
            return false;
        }

        if (!OneCStaffEligibility.IsOpenByDismissalDate(employee.DismissalDate))
        {
            return false;
        }

        // ИП Шубин / Васильев и т.п. — не режем по whitelist CSV.
        if (!string.IsNullOrWhiteSpace(OrganizationMatcher.InferIndividualEntrepreneurEmployee(organization)))
        {
            return true;
        }

        var index = TryGetDismissedIndex();
        if (index is null)
        {
            return true;
        }

        // Работает в этом ЮЛ по CSV — оставляем.
        if (index.IsWorkingInOrganization(organization, employee.Name, employee.Snils))
        {
            return true;
        }

        // Уволен именно в этом ЮЛ по CSV — скрываем только здесь.
        // В другом ЮЛ тот же человек может оставаться в штате.
        if (index.IsDismissed(organization, employee.Name, employee.Snils))
        {
            return false;
        }

        // Нет точного совпадения в индексе CSV — доверяем карточке организации.
        return true;
    }

    public IReadOnlyDictionary<string, List<string>> GetByProfession(string organization) =>
        ToProfessionMap(LoadCanonicalStaff(organization));

    public IReadOnlyList<string> GetEmployeeNames(string organization) =>
        GetByProfession(organization)
            .SelectMany(pair => pair.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveEmployee(
        string organization,
        string? employeeId,
        string? name,
        string? profession,
        string? department = null,
        string? snils = null,
        DateTime? birthDate = null,
        DateTime? employmentDate = null,
        string? homeAddress = null,
        DateTime? transferDate = null,
        string? employmentType = null,
        string? hrStatus = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var employee = new OrganizationEmployee
        {
            Id = string.IsNullOrWhiteSpace(employeeId) ? Guid.NewGuid().ToString("N") : employeeId,
            Name = name.Trim(),
            Profession = profession?.Trim() ?? string.Empty,
            Department = department?.Trim() ?? string.Empty,
            Snils = snils?.Trim() ?? string.Empty,
            BirthDate = birthDate,
            EmploymentDate = employmentDate,
            TransferDate = transferDate,
            EmploymentType = employmentType?.Trim() ?? string.Empty,
            HrStatus = string.IsNullOrWhiteSpace(hrStatus) ? string.Empty : hrStatus.Trim(),
            HomeAddress = homeAddress?.Trim() ?? string.Empty,
            IsActive = string.IsNullOrWhiteSpace(hrStatus)
                ? true
                : !EmployeeHrStatus.IsDismissed(hrStatus)
        };
        // В штате организации дата перевода не ведётся — только рождение и приём.
        employee.TransferDate = null;
        EmployeeFio.ApplyNameParts(employee);
        _store.Upsert(organization, employee);
    }

    /// <summary>Сохраняет домашний адрес в карточку сотрудника (для автоподстановки в направлениях).</summary>
    public void SaveHomeAddress(string organization, string? employeeId, string? name, string homeAddress)
    {
        if (string.IsNullOrWhiteSpace(homeAddress))
        {
            return;
        }

        var existing = !string.IsNullOrWhiteSpace(employeeId)
            ? LoadIncludingInactive(organization).FirstOrDefault(e =>
                string.Equals(e.Id, employeeId, StringComparison.Ordinal))
            : null;
        existing ??= FindByName(organization, name ?? string.Empty);
        if (existing is null)
        {
            return;
        }

        existing.HomeAddress = homeAddress.Trim();
        _store.Upsert(organization, existing);
    }

    public OrganizationEmployee? FindByName(string organization, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Load(organization)
            .Where(employee => string.Equals(employee.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(SelectBestEmploymentRecord)
            .FirstOrDefault();
    }

    public void DeleteEmployee(string organization, string employeeId) =>
        _store.Delete(organization, employeeId);

    public void RenameOrganization(string oldName, string newName) =>
        _store.RenameOrganization(oldName, newName);

    public void RemoveOrganization(string organization) =>
        _store.RemoveOrganization(organization);

    private void EnsureImported(string organization)
    {
        // Важно: включая уволенных. Иначе Save после дотягивания из Excel/СИЗ
        // затирает архив обучения (уволенных из 1С).
        var existing = _store.Get(organization, includeInactive: true)
            .Where(e => IsValidEmployeeName(e.Name))
            .Select(Clone)
            .ToList();
        var fromSource = DeduplicateByEmploymentKey(organization, ImportFromSource(organization));
        if (existing.Count == 0)
        {
            if (fromSource.Count > 0)
            {
                _store.Save(organization, fromSource);
                _store.ApplyLegalEntitySplits();
            }

            return;
        }

        // Разделённые юрлица (КСК / Строй Каркас и пары ИП) не дотягиваем из
        // побочных источников — иначе снова сольются.
        if (OrganizationLegalEntitySplit.PreservedOrganizations.Any(p =>
                OrganizationMatcher.MatchesForCatalog(p, organization)
                || string.Equals(
                    OrganizationMatcher.Normalize(p),
                    OrganizationMatcher.Normalize(organization),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (fromSource.Count == 0)
        {
            return;
        }

        // Дотягиваем недостающих из Excel/СИЗ, не выкидывая уволенных из 1С.
        // Если в организации уже есть карточки из 1С — новых людей из СИЗ не добавляем
        // (иначе уволенные из выгрузки снова появляются в штате под другим написанием ФИО).
        var hasOneCStaff = existing.Any(e => e.SyncedAt is not null);
        var dismissedIndex = TryGetDismissedIndex();
        var byKey = existing
            .ToDictionary(
                e => OneCEmployeeListParser.BuildMatchKey(organization, e),
                e => e,
                StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var employee in fromSource)
        {
            var key = OneCEmployeeListParser.BuildMatchKey(organization, employee);
            if (byKey.TryGetValue(key, out var current))
            {
                // Не перетираем карточку уволенного полями из побочного источника.
                if (!EmployeeHrStatus.IsEmployed(current))
                {
                    continue;
                }

                changed |= MergeMissingFields(current, employee);
                continue;
            }

            var existingByPerson = byKey.Values.FirstOrDefault(e => EmployeePersonMatcher.SamePerson(e, employee));
            if (existingByPerson is not null)
            {
                if (!EmployeeHrStatus.IsEmployed(existingByPerson))
                {
                    continue;
                }

                changed |= MergeMissingFields(existingByPerson, employee);
                continue;
            }

            if (hasOneCStaff)
            {
                continue;
            }

            if (dismissedIndex?.IsDismissed(organization, employee.Name, employee.Snils) == true)
            {
                continue;
            }

            byKey[key] = Clone(employee);
            changed = true;
        }

        if (changed)
        {
            _store.Save(organization, byKey.Values
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
            _store.ApplyLegalEntitySplits();
        }
    }

    private static bool MergeMissingFields(OrganizationEmployee current, OrganizationEmployee employee)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(current.Profession) && !string.IsNullOrWhiteSpace(employee.Profession))
        {
            current.Profession = employee.Profession;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.Department) && !string.IsNullOrWhiteSpace(employee.Department))
        {
            current.Department = employee.Department;
            changed = true;
        }

        if (current.BirthDate is null && employee.BirthDate is not null)
        {
            current.BirthDate = employee.BirthDate;
            changed = true;
        }

        if (current.EmploymentDate is null && employee.EmploymentDate is not null)
        {
            current.EmploymentDate = employee.EmploymentDate;
            changed = true;
        }

        if (current.TransferDate is null && employee.TransferDate is not null)
        {
            current.TransferDate = employee.TransferDate;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.EmploymentType) && !string.IsNullOrWhiteSpace(employee.EmploymentType))
        {
            current.EmploymentType = employee.EmploymentType;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.HrStatus) && !string.IsNullOrWhiteSpace(employee.HrStatus))
        {
            current.HrStatus = employee.HrStatus;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.HomeAddress) && !string.IsNullOrWhiteSpace(employee.HomeAddress))
        {
            current.HomeAddress = employee.HomeAddress;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.Snils) && !string.IsNullOrWhiteSpace(employee.Snils))
        {
            current.Snils = employee.Snils;
            changed = true;
        }

        return changed;
    }

    private static bool IsReportable(OrganizationEmployee employee) =>
        EmployeeHrStatus.IsEmployed(employee);

    private List<OrganizationEmployee> BuildEmployeeList(string organization)
    {
        var list = new List<OrganizationEmployee>();
        var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dismissedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var employee in _store.Get(organization, includeInactive: true))
        {
            if (!EmployeeHrStatus.IsEmployed(employee))
            {
                var name = employee.Name?.Trim() ?? string.Empty;
                if (name.Length > 0)
                {
                    dismissedNames.Add(name);
                }

                continue;
            }

            if (!IsReportable(employee))
            {
                continue;
            }

            activeNames.Add(employee.Name.Trim());
            list.Add(Clone(employee));
        }

        // PPE/старый Excel не должен «воскрешать» уволенных из 1С.
        var dismissedIndex = TryGetDismissedIndex();
        foreach (var pair in _source.GetByProfession(organization))
        {
            foreach (var person in pair.Value)
            {
                if (!IsValidEmployeeName(person))
                {
                    continue;
                }

                var trimmed = person.Trim();
                if (activeNames.Contains(trimmed) || dismissedNames.Contains(trimmed))
                {
                    continue;
                }

                if (dismissedIndex?.IsDismissed(organization, trimmed) == true)
                {
                    dismissedNames.Add(trimmed);
                    continue;
                }

                list.Add(new OrganizationEmployee
                {
                    Name = trimmed,
                    Profession = pair.Key,
                    IsActive = true
                });
                activeNames.Add(trimmed);
            }
        }

        return list
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, List<string>> ToProfessionMap(IEnumerable<OrganizationEmployee> employees)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var employee in employees)
        {
            if (!IsValidEmployeeName(employee.Name))
            {
                continue;
            }

            var profession = string.IsNullOrWhiteSpace(employee.Profession) ? "Сотрудники" : employee.Profession.Trim();
            if (!map.TryGetValue(profession, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[profession] = set;
            }

            set.Add(employee.Name);
        }

        return map
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private List<OrganizationEmployee> ImportFromSource(string organization)
    {
        return BuildEmployeeListFromSourceOnly(organization);
    }

    private List<OrganizationEmployee> BuildEmployeeListFromSourceOnly(string organization)
    {
        var list = new List<OrganizationEmployee>();

        foreach (var pair in _source.GetByProfession(organization))
        {
            foreach (var person in pair.Value)
            {
                if (!IsValidEmployeeName(person))
                {
                    continue;
                }

                list.Add(new OrganizationEmployee
                {
                    Name = person,
                    Profession = pair.Key
                });
            }
        }

        return list
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new OrganizationEmployee
                {
                    Name = first.Name,
                    Profession = g.Select(x => x.Profession).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty
                };
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsValidEmployeeName(string? name) =>
        EmployeeNameMatcher.IsPlausiblePersonName(name);

    private bool HasActiveStaffInStore(string organization) =>
        _store.Get(organization, includeInactive: false)
            .Any(e => IsValidEmployeeName(e.Name) && EmployeeHrStatus.IsEmployed(e));

    private static OrganizationEmployee Clone(OrganizationEmployee source) => new()
    {
        Id = source.Id,
        ExternalId = source.ExternalId,
        Name = source.Name,
        LastName = source.LastName,
        FirstName = source.FirstName,
        Patronymic = source.Patronymic,
        Profession = source.Profession,
        Department = source.Department,
        Snils = source.Snils,
        HomeAddress = source.HomeAddress,
        BirthDate = source.BirthDate,
        EmploymentDate = source.EmploymentDate,
        TransferDate = source.TransferDate,
        DismissalDate = source.DismissalDate,
        EmploymentType = source.EmploymentType,
        IsActive = source.IsActive,
        HrStatus = source.HrStatus,
        SyncedAt = source.SyncedAt
    };

    private static int SelectBestEmploymentRecord(OrganizationEmployee employee) =>
        (employee.IsActive ? 100 : 0)
        + (OneCEmployeeListParser.IsPrimaryEmployment(employee.EmploymentType) ? 10 : 0)
        + Math.Min((employee.EmploymentDate ?? DateTime.MinValue).Year - 2000, 99);

    private static List<OrganizationEmployee> DeduplicateByEmploymentKey(
        string organization,
        IEnumerable<OrganizationEmployee> employees) =>
        employees
            .Where(e => IsValidEmployeeName(e.Name))
            .GroupBy(
                e => OneCEmployeeListParser.BuildMatchKey(organization, e),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ranked = group
                    .OrderByDescending(e => EmployeeHrStatus.IsEmployed(e) ? 1_000 : 0)
                    .ThenByDescending(SelectBestEmploymentRecord)
                    .ThenByDescending(e => e.SyncedAt ?? DateTime.MinValue)
                    .ToList();
                var best = ranked[0];
                // Если есть хотя бы одна работающая запись — не оставляем уволенную.
                var employed = ranked.FirstOrDefault(EmployeeHrStatus.IsEmployed);
                if (employed is not null)
                {
                    best = employed;
                    best.TransferDate ??= ranked.Select(e => e.TransferDate).FirstOrDefault(d => d is not null);
                    return best;
                }

                best.TransferDate ??= ranked.Select(e => e.TransferDate).FirstOrDefault(d => d is not null);
                if (!EmployeeHrStatus.IsEmployed(best))
                {
                    best.DismissalDate ??= ranked.Select(e => e.DismissalDate).FirstOrDefault(d => d is not null);
                    if (string.IsNullOrWhiteSpace(best.HrStatus))
                    {
                        best.HrStatus = ranked
                            .Select(e => e.HrStatus)
                            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                            ?? "Увольнение";
                    }
                }
                else
                {
                    // Работающему не подставляем «Увольнение» со старых дублей.
                    if (string.IsNullOrWhiteSpace(best.HrStatus)
                        || EmployeeHrStatus.IsDismissed(best.HrStatus))
                    {
                        best.HrStatus = "Работа";
                    }

                    if (best.DismissalDate is { } past && past.Date <= DateTime.Today)
                    {
                        best.DismissalDate = null;
                    }
                }

                return best;
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private List<OrganizationEmployee> FilterReportEmployees(
        string organization,
        IEnumerable<OrganizationEmployee> employees)
    {
        return employees
            .Where(e => IsDirectoryEmployed(organization, e))
            .Select(Clone)
            .ToList();
    }

    /// <summary>
    /// Для ИП сам предприниматель — действующий участник отчётов обучения (даже если его нет в выгрузке 1С
    /// или он ошибочно помечен уволенным). Иначе протоколы уводят его в архив.
    /// </summary>
    private static List<OrganizationEmployee> EnsureIndividualEntrepreneur(
        string organization,
        List<OrganizationEmployee> employees,
        IReadOnlyList<OrganizationEmployee>? inactiveCandidates = null,
        bool createIfMissing = true)
    {
        var ipName = OrganizationMatcher.InferIndividualEntrepreneurEmployee(organization);
        if (string.IsNullOrWhiteSpace(ipName))
        {
            return employees;
        }

        OrganizationEmployee Revive(OrganizationEmployee source)
        {
            source.IsActive = true;
            if (string.IsNullOrWhiteSpace(source.HrStatus)
                || EmployeeHrStatus.IsDismissed(source.HrStatus))
            {
                source.HrStatus = "Работа";
            }

            source.DismissalDate = null;
            if (string.IsNullOrWhiteSpace(source.Profession)
                || source.Profession.Trim() == "—")
            {
                source.Profession = "Индивидуальный предприниматель";
            }

            if (string.IsNullOrWhiteSpace(source.Department)
                || source.Department.Trim() == "—"
                || source.Department.Contains("администрац", StringComparison.OrdinalIgnoreCase))
            {
                // В отчёте группа по подразделению — как должность ИП.
                source.Department = "Индивидуальный предприниматель";
            }

            return source;
        }

        var existing = employees.FirstOrDefault(e =>
            OrganizationMatcher.IsIndividualEntrepreneurPerson(organization, e.Name, e.Profession));
        if (existing is not null)
        {
            Revive(existing);
            return employees;
        }

        // В активном штате нет — поднимаем из неактивных по ФИО ИП.
        var fromInactive = (inactiveCandidates ?? [])
            .FirstOrDefault(e =>
                OrganizationMatcher.IsIndividualEntrepreneurPerson(organization, e.Name, e.Profession));
        if (fromInactive is not null)
        {
            var listWithInactive = employees.ToList();
            listWithInactive.Add(Revive(Clone(fromInactive)));
            return listWithInactive;
        }

        if (!createIfMissing)
        {
            return employees;
        }

        var list = employees.ToList();
        list.Add(new OrganizationEmployee
        {
            Name = ipName.Trim(),
            Profession = "Индивидуальный предприниматель",
            Department = "Администрация",
            IsActive = true,
            HrStatus = "Работа"
        });
        return list;
    }

    /// <summary>Сброс кэша CSV-индекса после полной перезаписи штата.</summary>
    public void InvalidateStaffCaches()
    {
        _dismissedIndex = null;
        _dismissedIndexLoadedAt = DateTime.MinValue;
        _dismissedIndexStamp = string.Empty;
    }

    private OneCDismissedEmployeeIndex? TryGetDismissedIndex()
    {
        if (_oneCSync is null)
        {
            return null;
        }

        var stamp = BuildCsvStamp();
        if (_dismissedIndex is not null
            && string.Equals(_dismissedIndexStamp, stamp, StringComparison.Ordinal)
            && DateTime.UtcNow - _dismissedIndexLoadedAt < TimeSpan.FromMinutes(5))
        {
            return _dismissedIndex;
        }

        _dismissedIndex = _oneCSync.BuildDismissedIndex();
        _dismissedIndexLoadedAt = DateTime.UtcNow;
        _dismissedIndexStamp = stamp;
        return _dismissedIndex;
    }

    private string BuildCsvStamp()
    {
        if (_oneCSync is null)
        {
            return string.Empty;
        }

        try
        {
            var files = OneCEmployeeSyncService.SelectCurrentCsvBatch(_oneCSync.FindSharedFiles());
            if (files.Count == 0)
            {
                return "empty";
            }

            return string.Join('|', files.Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc:o}"));
        }
        catch
        {
            return "err";
        }
    }
}
