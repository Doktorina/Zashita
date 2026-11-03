using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Web.Services;

public sealed class OrganizationGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Organizations { get; set; } = [];
}

public sealed class OrganizationDataFile
{
    public List<string> CustomOrganizations { get; set; } = [];
    public Dictionary<string, string> Renames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Только список из 1С / custom — без встроенного и Kristina.</summary>
    public bool CustomOnly { get; set; }
    /// <summary>Скрытые из выпадающего списка организации (для обычных пользователей).</summary>
    public List<string> HiddenOrganizations { get; set; } = [];
    /// <summary>Именованные группы компаний для фильтра в тулбаре.</summary>
    public List<OrganizationGroup> Groups { get; set; } = [];
}

public sealed class OrganizationStore
{
    private const string SessionOrgKey = "Workspace.Organization";
    private const string SessionTypeKey = "Workspace.OrganizationType";
    private const string SessionGroupKey = "Workspace.OrganizationGroup";

    public const string AllGroupsId = "";
    public const string AllOrganizationsToken = "__all__";
    /// <summary>Явный сброс выбора в тулбаре («— выберите —»).</summary>
    public const string UnselectedOrganizationToken = "__none__";

    private readonly string _dataFilePath;
    private readonly KristinaProfessionsStore? _kristina;
    private readonly OrganizationEmployeesStore? _employees;
    private readonly object _sync = new();
    private List<string> _organizations = [];
    private Dictionary<string, string> _renames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hiddenOrganizations = new(StringComparer.OrdinalIgnoreCase);
    private List<OrganizationGroup> _groups = [];
    private bool _customOnly;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public OrganizationStore(
        IWebHostEnvironment env,
        KristinaProfessionsStore? kristina = null,
        OrganizationEmployeesStore? employees = null)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dataFilePath = Path.Combine(appData, "organizations.json");
        _kristina = kristina;
        _employees = employees;
        Load();
    }

    public static IReadOnlyList<string> OrganizationTypes { get; } = ["Все", "Все Юр лица", "ИП", "ООО"];

    public static IReadOnlyList<string> CreatableOrganizationTypes { get; } = ["ИП", "ООО", "Другое"];

    public string DefaultOrganization => "ИП Алешкевич Александр Дмитриевич";

    public IReadOnlyList<string> AllOrganizations
    {
        get
        {
            lock (_sync)
            {
                return _organizations.ToList();
            }
        }
    }

    public bool IsAllOrganizationsSelected(HttpContext http)
    {
        var value = http.Session.GetString(SessionOrgKey);
        return string.Equals(value, AllOrganizationsToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Пользователь ещё не выбрал организацию (первый заход или сброс).</summary>
    public bool IsOrganizationUnselected(HttpContext http) =>
        string.IsNullOrWhiteSpace(http.Session.GetString(SessionOrgKey));

    /// <summary>Выбрана конкретная организация (не «Все» и не «— выберите —»).</summary>
    public bool HasSpecificOrganization(HttpContext http)
    {
        var value = http.Session.GetString(SessionOrgKey);
        return !string.IsNullOrWhiteSpace(value)
               && !string.Equals(value, AllOrganizationsToken, StringComparison.OrdinalIgnoreCase);
    }

    public void ClearSelectedOrganization(HttpContext http) =>
        http.Session.Remove(SessionOrgKey);

    public string GetSelectedOrganization(HttpContext http)
    {
        var value = http.Session.GetString(SessionOrgKey);
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, AllOrganizationsToken, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var visible = GetVisibleOrganizations(http);
        var match = visible.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            // Скрыли организацию или сменили группу — не подставляем другую (раньше — Алешкевич).
            http.Session.Remove(SessionOrgKey);
            return string.Empty;
        }

        return ResolveCanonicalOrganization(match) ?? match;
    }

    /// <summary>Сопоставить сокращённое/устаревшее имя с записью из справочника.</summary>
    public string? ResolveCanonicalOrganization(string? organization)
    {
        var query = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        lock (_sync)
        {
            return OrganizationMatcher.ResolveOrganizationName(query, _organizations);
        }
    }

    public string GetOrganizationType(HttpContext http) =>
        http.Session.GetString(SessionTypeKey) is { Length: > 0 } value && OrganizationTypes.Contains(value)
            ? value
            : "Все";

    public string GetSelectedGroupId(HttpContext http)
    {
        var id = http.Session.GetString(SessionGroupKey) ?? AllGroupsId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return AllGroupsId;
        }

        lock (_sync)
        {
            return _groups.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))
                ? id
                : AllGroupsId;
        }
    }

    public IReadOnlyList<OrganizationGroup> GetGroups()
    {
        lock (_sync)
        {
            return _groups
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(CloneGroup)
                .ToList();
        }
    }

    public OrganizationGroup? GetGroup(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return null;
        }

        lock (_sync)
        {
            var found = _groups.FirstOrDefault(g =>
                string.Equals(g.Id, groupId.Trim(), StringComparison.OrdinalIgnoreCase));
            return found is null ? null : CloneGroup(found);
        }
    }

    public void SetWorkspaceContext(
        HttpContext http,
        string? organization,
        string? organizationType,
        string? organizationGroup = null)
    {
        if (organizationGroup is not null)
        {
            SetSelectedGroupId(http, organizationGroup);
        }

        var type = !string.IsNullOrWhiteSpace(organizationType) && OrganizationTypes.Contains(organizationType)
            ? organizationType
            : GetOrganizationType(http);

        http.Session.SetString(SessionTypeKey, type);

        var orgArg = (organization ?? string.Empty).Trim();
        if (string.Equals(orgArg, UnselectedOrganizationToken, StringComparison.OrdinalIgnoreCase))
        {
            http.Session.Remove(SessionOrgKey);
            return;
        }

        if (string.IsNullOrWhiteSpace(orgArg)
            || string.Equals(orgArg, "Все", StringComparison.OrdinalIgnoreCase)
            || string.Equals(orgArg, AllOrganizationsToken, StringComparison.OrdinalIgnoreCase))
        {
            http.Session.SetString(SessionOrgKey, AllOrganizationsToken);
            return;
        }

        var filtered = GetVisibleOrganizations(http, type);
        if (filtered.Count == 0)
        {
            http.Session.Remove(SessionOrgKey);
            return;
        }

        if (!AllOrganizations.Contains(orgArg, StringComparer.OrdinalIgnoreCase))
        {
            http.Session.Remove(SessionOrgKey);
            return;
        }

        var canonical = AllOrganizations.First(v =>
            string.Equals(v, orgArg, StringComparison.OrdinalIgnoreCase));
        if (!filtered.Contains(canonical, StringComparer.OrdinalIgnoreCase))
        {
            http.Session.Remove(SessionOrgKey);
            return;
        }

        http.Session.SetString(SessionOrgKey, canonical);
    }

    /// <summary>
    /// Переключает фильтр на организацию записи (карта СОУТ / РМ), сбрасывая группу на «Все»,
    /// чтобы запись гарантированно открывалась и совпадала с тулбаром.
    /// </summary>
    public bool SelectOrganizationForRecord(HttpContext http, string? organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return false;
        }

        var value = organization.Trim();
        if (IsOrganizationHidden(value))
        {
            return false;
        }

        // Каноническое имя из справочника, если есть; иначе — как в записи (карта всё равно откроется).
        var canonical = AllOrganizations.FirstOrDefault(v =>
            string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ?? value;

        SetSelectedGroupId(http, AllGroupsId);
        http.Session.SetString(SessionTypeKey, "Все");
        http.Session.SetString(SessionOrgKey, canonical);
        return true;
    }

    public void SetSelectedGroupId(HttpContext http, string? groupId)
    {
        var id = (groupId ?? AllGroupsId).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            http.Session.SetString(SessionGroupKey, AllGroupsId);
            return;
        }

        lock (_sync)
        {
            if (!_groups.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                http.Session.SetString(SessionGroupKey, AllGroupsId);
                return;
            }
        }

        http.Session.SetString(SessionGroupKey, id);
    }

    public string? UpsertGroup(string? id, string? name, IEnumerable<string>? organizations)
    {
        var groupName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return null;
        }

        var members = (organizations ?? [])
            .Select(o => (o ?? string.Empty).Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_sync)
        {
            members = members
                .Select(m => _organizations.FirstOrDefault(o =>
                    string.Equals(o, m, StringComparison.OrdinalIgnoreCase)))
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (members.Count == 0)
            {
                return null;
            }

            OrganizationGroup target;
            if (!string.IsNullOrWhiteSpace(id))
            {
                target = _groups.FirstOrDefault(g =>
                    string.Equals(g.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? new OrganizationGroup { Id = Guid.NewGuid().ToString("N") };
                if (!_groups.Contains(target))
                {
                    _groups.Add(target);
                }
            }
            else
            {
                target = new OrganizationGroup { Id = Guid.NewGuid().ToString("N") };
                _groups.Add(target);
            }

            target.Name = groupName;
            target.Organizations = members;
            SaveLocked();
            return target.Id;
        }
    }

    public bool DeleteGroup(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_sync)
        {
            var removed = _groups.RemoveAll(g =>
                string.Equals(g.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return false;
            }

            SaveLocked();
            return true;
        }
    }

    /// <summary>
    /// Синхронизирует фильтр отчёта с глобальным контекстом (сессия / тулбар).
    /// null — параметр не передан, подставить организацию из сессии.
    /// пустая строка — явный режим «все организации» (только если allowAll).
    /// </summary>
    public void ApplyReportOrganizationFilter(HttpContext http, ref string? selectedOrganization, bool allowAll = false)
    {
        if (selectedOrganization is null)
        {
            selectedOrganization = GetSelectedOrganization(http);
            return;
        }

        var trimmed = selectedOrganization.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            selectedOrganization = allowAll ? string.Empty : GetSelectedOrganization(http);
            return;
        }

        if (AllOrganizations.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = AllOrganizations.First(v =>
                string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase));
            var visible = GetVisibleOrganizations(http);
            if (visible.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                SetWorkspaceContext(http, canonical, null);
                selectedOrganization = canonical;
                return;
            }
        }

        selectedOrganization = GetSelectedOrganization(http);
    }

    public IReadOnlyList<string> GetFilteredOrganizations(string organizationType) =>
        (organizationType switch
        {
            "ИП" => AllOrganizations.Where(v => v.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)),
            "ООО" => AllOrganizations.Where(v => v.StartsWith("ООО", StringComparison.OrdinalIgnoreCase)),
            "Все Юр лица" => AllOrganizations.Where(v => !v.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)),
            _ => AllOrganizations
        })
        .OrderBy(OrganizationSortKey)
        .ThenBy(v => v, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Организации без учёта скрытых (для тулбара, поиска, фильтров).</summary>
    public IReadOnlyList<string> GetNonHiddenOrganizations(string organizationType = "Все")
    {
        var filtered = GetFilteredOrganizations(organizationType);
        lock (_sync)
        {
            return filtered
                .Where(org => !IsHiddenLocked(org))
                .ToList();
        }
    }

    /// <summary>
    /// Список для тулбара и отчётов: скрытые никогда не показываются (в т.ч. главному админу).
    /// Если выбрана группа — только компании из группы.
    /// Управление скрытием — только на странице «Организации».
    /// </summary>
    public IReadOnlyList<string> GetVisibleOrganizations(HttpContext http, string? organizationType = null)
    {
        var type = "Все";
        var list = GetNonHiddenOrganizations(type);
        var groupId = GetSelectedGroupId(http);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return list;
        }

        lock (_sync)
        {
            var group = _groups.FirstOrDefault(g =>
                string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (group is null || group.Organizations.Count == 0)
            {
                return list;
            }

            var allowed = group.Organizations.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return list.Where(org => allowed.Contains(org)).ToList();
        }
    }

    private static OrganizationGroup CloneGroup(OrganizationGroup source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Organizations = source.Organizations.ToList()
        };

    public bool IsOrganizationHidden(string organization)
    {
        lock (_sync)
        {
            return IsHiddenLocked(organization);
        }
    }

    private bool IsHiddenLocked(string organization)
    {
        var value = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) || _hiddenOrganizations.Count == 0)
        {
            return false;
        }

        if (_hiddenOrganizations.Contains(value))
        {
            return true;
        }

        return _hiddenOrganizations.Any(hidden =>
            OrganizationMatcher.MatchesForCatalog(hidden, value)
            || string.Equals(
                OrganizationMatcher.Normalize(hidden),
                OrganizationMatcher.Normalize(value),
                StringComparison.OrdinalIgnoreCase));
    }

    public bool SetOrganizationHidden(string organization, bool hidden)
    {
        var value = organization.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_organizations.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var canonical = _organizations.First(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            var aliases = _hiddenOrganizations
                .Where(h => OrganizationMatcher.MatchesForCatalog(h, canonical)
                            || string.Equals(
                                OrganizationMatcher.Normalize(h),
                                OrganizationMatcher.Normalize(canonical),
                                StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (hidden)
            {
                foreach (var alias in aliases)
                {
                    _hiddenOrganizations.Remove(alias);
                }

                _hiddenOrganizations.Add(canonical);
            }
            else
            {
                if (aliases.Count == 0 && !_hiddenOrganizations.Contains(canonical))
                {
                    return false;
                }

                foreach (var alias in aliases)
                {
                    _hiddenOrganizations.Remove(alias);
                }

                _hiddenOrganizations.Remove(canonical);
            }

            SaveLocked();
            return true;
        }
    }

    public int HideOrganizationsOutsideVictoriaContour()
    {
        lock (_sync)
        {
            var changed = 0;
            foreach (var org in _organizations)
            {
                if (OrganizationMatcher.IsVictoriaContourOrganization(org)
                    || _hiddenOrganizations.Contains(org))
                {
                    continue;
                }

                _hiddenOrganizations.Add(org);
                changed++;
            }

            if (changed > 0)
            {
                SaveLocked();
            }

            return changed;
        }
    }

    public bool AddOrganization(string name, string? organizationType = null)
    {
        var value = FormatOrganizationName(name, organizationType);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        lock (_sync)
        {
            if (_organizations.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)
                                        || OrganizationMatcher.Matches(v, value)))
            {
                return false;
            }

            _organizations.Add(value);
            _organizations = SortOrganizations(_organizations);
            SaveLocked();
            return true;
        }
    }

    /// <summary>
    /// Добавить организацию из выгрузки 1С.
    /// Предпочитает канон из организации.xlsx (ССТРОЙ → ООО «СтандартСтрой»).
    /// Разные ЮЛ из списка никогда не схлопываются.
    /// </summary>
    public string EnsureSyncedOrganization(string nameFromFile)
    {
        var raw = (nameFromFile ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var preferred = OrganizationCanonicalRegistry.ResolveCanonicalName(raw) ?? raw;

        lock (_sync)
        {
            var existing = _organizations.FirstOrDefault(v =>
                string.Equals(v, preferred, StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, raw, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.MatchesForCatalog(v, preferred)
                || OrganizationMatcher.MatchesForCatalog(v, raw)
                || OrganizationMatcher.Matches(v, preferred)
                || OrganizationMatcher.Matches(v, raw));

            if (existing is not null)
            {
                var canon = OrganizationCanonicalRegistry.ResolveCanonicalName(existing)
                            ?? OrganizationCanonicalRegistry.ResolveCanonicalName(raw);

                // CSV полное ФИО важнее инициалов в списке («О.С.» → «Оксана Сергеевна»).
                // Иначе штат пишется под «О.С.», а в UI после EnsurePreserved видна полная карточка без людей.
                if (raw.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                    && !OrganizationMatcher.IsIpInitialsForm(raw)
                    && OrganizationMatcher.IsIpInitialsForm(existing)
                    && OrganizationMatcher.Matches(existing, raw))
                {
                    var idxFull = _organizations.FindIndex(v =>
                        string.Equals(v, existing, StringComparison.OrdinalIgnoreCase));
                    if (idxFull >= 0)
                    {
                        _organizations[idxFull] = raw;
                        _organizations = SortOrganizations(_organizations);
                        SaveLocked();
                    }

                    return raw;
                }

                // Не переименовывать «ИП … Сергей Андреевич» → «ИП … С.А.»:
                // в UI остаётся полное имя, а штат уезжает под инициалы → пустая карточка.
                if (canon is not null
                    && OrganizationMatcher.IsIpInitialsForm(canon)
                    && existing.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                    && !OrganizationMatcher.IsIpInitialsForm(existing))
                {
                    return existing;
                }

                if (canon is not null
                    && !string.Equals(existing, canon, StringComparison.OrdinalIgnoreCase)
                    && OrganizationMatcher.MatchesForCatalog(existing, canon))
                {
                    var idx = _organizations.FindIndex(v =>
                        string.Equals(v, existing, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        _organizations[idx] = canon;
                        _organizations = SortOrganizations(_organizations);
                        SaveLocked();
                    }

                    return canon;
                }

                return existing;
            }

            // Новая ИП из CSV — полное ФИО, не инициалы из xlsx.
            var toAdd = preferred;
            if (OrganizationMatcher.IsIpInitialsForm(preferred)
                && raw.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                && !OrganizationMatcher.IsIpInitialsForm(raw))
            {
                toAdd = raw;
            }

            _organizations.Add(toAdd);
            _organizations = SortOrganizations(_organizations);
            SaveLocked();
            return toAdd;
        }
    }

    /// <summary>Подтянуть организации, для которых уже есть сотрудники в JSON.</summary>
    public void MergeEmployeeOrganizations()
    {
        if (_employees is null)
        {
            return;
        }

        lock (_sync)
        {
            var changed = false;
            foreach (var org in _employees.GetOrganizations())
            {
                if (string.IsNullOrWhiteSpace(org))
                {
                    continue;
                }

                if (_organizations.Any(v => string.Equals(v, org, StringComparison.OrdinalIgnoreCase)
                                            || OrganizationMatcher.Matches(v, org)))
                {
                    continue;
                }

                _organizations.Add(org.Trim());
                changed = true;
            }

            if (changed)
            {
                _organizations = SortOrganizations(_organizations);
                SaveLocked();
            }
        }
    }

    /// <summary>
    /// Перенести сотрудников с ключей вида «ИП Кирюшкин Виталий…»
    /// на канонические имена из списка («ИП Кирюшкин В.В.»).
    /// </summary>
    public void AlignEmployeeKeysToOrganizationList()
    {
        if (_employees is null)
        {
            return;
        }

        IReadOnlyList<string> listed;
        lock (_sync)
        {
            listed = _organizations.ToList();
        }

        foreach (var empOrg in _employees.GetOrganizations().ToList())
        {
            var candidates = listed
                .Where(o =>
                    string.Equals(o, empOrg, StringComparison.OrdinalIgnoreCase)
                    || OrganizationMatcher.Matches(o, empOrg))
                .ToList();
            var canonical = candidates.Count == 0
                ? null
                : candidates.Aggregate(
                    (a, b) => OrganizationMatcher.PickPreferredOrganizationName(a, b));

            if (canonical is null || string.Equals(canonical, empOrg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Не склеивать явно разные юрлица (КСК≠Строй Каркас, пары ИП).
            if (OrganizationLegalEntitySplit.AreKnownDistinct(canonical, empOrg))
            {
                continue;
            }

            // Не переносить штат с полного ФИО ИП на инициалы.
            if (OrganizationMatcher.IsIpInitialsForm(canonical)
                && !OrganizationMatcher.IsIpInitialsForm(empOrg))
            {
                continue;
            }

            var people = _employees.Get(empOrg, includeInactive: true);
            if (people.Count == 0)
            {
                continue;
            }

            var already = _employees.Get(canonical, includeInactive: true).ToList();
            foreach (var person in people)
            {
                var personKey = OneCEmployeeListParser.BuildMatchKey(canonical, person);
                var personEmployed = EmployeeHrStatus.IsEmployed(person);
                // Уволенного и работающего с одним ключом оставляем оба —
                // уволенный нужен в архиве обучения, работающий — в штате.
                var exists = already.Any(e =>
                    string.Equals(
                        OneCEmployeeListParser.BuildMatchKey(canonical, e),
                        personKey,
                        StringComparison.OrdinalIgnoreCase)
                    && EmployeeHrStatus.IsEmployed(e) == personEmployed);
                if (!exists)
                {
                    already.Add(person);
                }
            }

            _employees.Save(canonical, already);
            if (!string.Equals(canonical, empOrg, StringComparison.OrdinalIgnoreCase))
            {
                _employees.RemoveOrganization(empOrg);
            }
        }
    }

    /// <summary>Схлопнуть дубли ИП в списке организаций (инициалы ↔ полное ФИО).</summary>
    public void ConsolidateOrganizationAliases()
    {
        lock (_sync)
        {
            _organizations = SortOrganizations(
                OrganizationMatcher.DeduplicateOrganizations(
                    _organizations,
                    OrganizationLegalEntitySplit.PreservedOrganizations));
            SaveLocked();
        }

        _employees?.PreferFullIpNameKeys();
        AlignEmployeeKeysToOrganizationList();
    }

    /// <summary>
    /// Удалить пустые ООО из старого ручного списка (СтандартСтрой, Созвездие и т.п.),
    /// если для них нет сотрудников и есть каноническая запись из 1С.
    /// </summary>
    public int PurgeLegacyManualShellOrganizations()
    {
        if (_employees is null)
        {
            return 0;
        }

        lock (_sync)
        {
            var removed = 0;
            foreach (var org in _organizations.ToList())
            {
                if (!OrganizationCatalog.IsLegacyManualShell(org))
                {
                    continue;
                }

                if (_employees.Get(org, includeInactive: true).Count > 0)
                {
                    continue;
                }

                var hasCanonicalWithData = _organizations.Any(other =>
                    !string.Equals(other, org, StringComparison.OrdinalIgnoreCase)
                    && OrganizationMatcher.MatchesForCatalog(other, org)
                    && _employees.Get(other, includeInactive: true).Count > 0);
                if (hasCanonicalWithData)
                {
                    RemoveOrganizationLocked(org);
                    removed++;
                    continue;
                }

                if (_employees.GetOrganizations().All(empOrg =>
                        !OrganizationMatcher.MatchesForCatalog(empOrg, org)))
                {
                    RemoveOrganizationLocked(org);
                    removed++;
                }
            }

            if (removed > 0)
            {
                SaveLocked();
            }

            return removed;
        }
    }

    private void RemoveOrganizationLocked(string name)
    {
        _organizations.RemoveAll(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        _hiddenOrganizations.RemoveWhere(v =>
            string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        foreach (var group in _groups)
        {
            group.Organizations.RemoveAll(o =>
                string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
        }

        _groups.RemoveAll(g => g.Organizations.Count == 0);
    }

    public static string FormatOrganizationName(string rawName, string? organizationType)
    {
        var name = rawName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var type = string.IsNullOrWhiteSpace(organizationType) ? "Другое" : organizationType.Trim();

        return type switch
        {
            "ИП" => name.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"ИП {name}",
            "ООО" => FormatOooName(name),
            _ => name
        };
    }

    public static string GetDisplayType(string organizationName)
    {
        if (organizationName.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return "ИП";
        }

        if (organizationName.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            return "ООО";
        }

        return "Другое";
    }

    private static string FormatOooName(string name)
    {
        if (name.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var inner = name.Trim().Trim('«', '»', '"', ' ');
        return string.IsNullOrWhiteSpace(inner) ? string.Empty : $"ООО «{inner}»";
    }

    public bool RemoveOrganization(string name)
    {
        lock (_sync)
        {
            if (!_organizations.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            RemoveOrganizationLocked(name);
            SaveLocked();
            return true;
        }
    }

    public bool RenameOrganization(string oldName, string newName)
    {
        var from = oldName.Trim();
        var to = newName.Trim();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return false;
        }

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (_sync)
        {
            var idx = _organizations.FindIndex(v => string.Equals(v, from, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                return false;
            }

            if (_organizations.Any(v => string.Equals(v, to, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _organizations[idx] = to;
            _organizations = SortOrganizations(_organizations);

            if (_hiddenOrganizations.Remove(from))
            {
                _hiddenOrganizations.Add(to);
            }

            foreach (var group in _groups)
            {
                for (var i = 0; i < group.Organizations.Count; i++)
                {
                    if (string.Equals(group.Organizations[i], from, StringComparison.OrdinalIgnoreCase))
                    {
                        group.Organizations[i] = to;
                    }
                }
            }

            var builtInKey = OrganizationCatalog.BuiltInOrganizations
                .FirstOrDefault(org => string.Equals(ResolveBuiltInName(org), from, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(org, from, StringComparison.OrdinalIgnoreCase));
            if (builtInKey is not null)
            {
                _renames[builtInKey] = to;
            }

            SaveLocked();
            return true;
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            SaveLocked();
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _organizations = [];
            _groups = [];
            var customOnly = false;

            void Add(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                var value = raw.Trim();
                if (!seen.Add(value))
                {
                    return;
                }

                _organizations.Add(value);
            }

            OrganizationDataFile? fileData = null;
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    if (json.TrimStart().StartsWith('['))
                    {
                        var legacy = JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
                        foreach (var item in legacy)
                        {
                            Add(item);
                        }
                    }
                    else
                    {
                        fileData = JsonSerializer.Deserialize<OrganizationDataFile>(json, JsonOpts) ?? new OrganizationDataFile();
                        customOnly = fileData.CustomOnly;
                        _renames = fileData.Renames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _hiddenOrganizations = (fileData.HiddenOrganizations ?? [])
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Select(v => v.Trim())
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        _groups = (fileData.Groups ?? [])
                            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Name))
                            .Select(g => new OrganizationGroup
                            {
                                Id = string.IsNullOrWhiteSpace(g.Id) ? Guid.NewGuid().ToString("N") : g.Id.Trim(),
                                Name = g.Name.Trim(),
                                Organizations = (g.Organizations ?? [])
                                    .Where(o => !string.IsNullOrWhiteSpace(o))
                                    .Select(o => o.Trim())
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList()
                            })
                            .Where(g => g.Organizations.Count > 0)
                            .ToList();
                        foreach (var item in fileData.CustomOrganizations)
                        {
                            Add(item);
                        }
                    }
                }
            }
            catch
            {
                // ignore broken file
            }

            if (!customOnly)
            {
                foreach (var org in OrganizationCatalog.BuiltInOrganizations)
                {
                    Add(ResolveBuiltInName(org));
                }

                if (_kristina is { IsAvailable: true })
                {
                    try
                    {
                        foreach (var org in _kristina.GetOrganizations())
                        {
                            Add(org);
                        }
                    }
                    catch
                    {
                        // ignore broken Kristina file on server
                    }
                }
            }

            if (_employees is not null)
            {
                foreach (var org in _employees.GetOrganizations())
                {
                    if (_organizations.Any(v => string.Equals(v, org, StringComparison.OrdinalIgnoreCase)
                                                || OrganizationMatcher.Matches(v, org)))
                    {
                        continue;
                    }

                    Add(org);
                }
            }

            _organizations = SortOrganizations(
                OrganizationMatcher.DeduplicateOrganizations(_organizations));
            foreach (var group in _groups)
            {
                group.Organizations = group.Organizations
                    .Select(m => _organizations.FirstOrDefault(o =>
                        string.Equals(o, m, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(o, m)))
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            _groups.RemoveAll(g => g.Organizations.Count == 0 || string.IsNullOrWhiteSpace(g.Name));
            _customOnly = customOnly;
        }
    }

    /// <summary>Полная замена списка организаций данными из 1С + каталог + Kristina.</summary>
    public void ReplaceAllFromOneC(IEnumerable<string> organizations)
    {
        lock (_sync)
        {
            // Скрытые сохраняем по нормализованному имени — после синка снова применяем.
            var previouslyHidden = _hiddenOrganizations
                .Select(OrganizationMatcher.Normalize)
                .Where(v => v.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<string> kristinaOrganizations = [];
            if (_kristina is { IsAvailable: true })
            {
                try
                {
                    kristinaOrganizations = _kristina.GetOrganizations();
                }
                catch
                {
                    // ignore broken Kristina file on server
                }
            }

            _organizations = SortOrganizations(
                OrganizationCatalog.MergeWithCatalog(
                    organizations
                        .Where(o => !string.IsNullOrWhiteSpace(o))
                        .Select(o => o.Trim()),
                    kristinaOrganizations));
            _renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _customOnly = true;

            _hiddenOrganizations = _organizations
                .Where(org => previouslyHidden.Contains(OrganizationMatcher.Normalize(org)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            SaveLocked();
        }
    }

    /// <summary>Гарантировать наличие разделённых юрлиц в выпадающем списке.</summary>
    public void EnsurePreservedLegalEntities()
    {
        lock (_sync)
        {
            var before = _organizations.Count;
            _organizations = SortOrganizations(
                OrganizationLegalEntitySplit.EnsurePreservedOrganizations(_organizations));
            if (_organizations.Count != before)
            {
                SaveLocked();
            }
        }
    }

    private void SaveLocked()
    {
        List<string> custom;
        if (_customOnly)
        {
            custom = _organizations
                .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        else
        {
            var builtInDisplay = OrganizationCatalog.BuiltInOrganizations
                .Select(ResolveBuiltInName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            custom = _organizations
                .Where(org => !builtInDisplay.Contains(org))
                .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        var payload = new OrganizationDataFile
        {
            CustomOrganizations = custom,
            CustomOnly = _customOnly,
            HiddenOrganizations = _hiddenOrganizations
                .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Groups = _groups
                .Select(CloneGroup)
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Renames = _customOnly
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : _renames
                    .Where(pair => OrganizationCatalog.BuiltInOrganizations.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        File.WriteAllText(_dataFilePath, json, System.Text.Encoding.UTF8);
    }

    private string ResolveBuiltInName(string builtInName) =>
        _renames.TryGetValue(builtInName, out var renamed) && !string.IsNullOrWhiteSpace(renamed)
            ? renamed.Trim()
            : builtInName;

    private static List<string> SortOrganizations(IEnumerable<string> source) =>
        source
            .OrderBy(OrganizationSortKey)
            .ThenBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static int OrganizationSortKey(string name)
    {
        if (name.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

}
