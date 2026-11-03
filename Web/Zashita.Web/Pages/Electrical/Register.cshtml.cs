using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Electrical;
using Zashita.Core.Employees;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical;

[RequireLogin]
public class RegisterModel : PageModel
{
    private readonly ElectricalKnowledgeCheckStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public RegisterModel(
        ElectricalKnowledgeCheckStore store,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public bool IsAllOrganizations { get; private set; }
    public IReadOnlyList<string> ScopeOrganizations { get; private set; } = [];
    public List<ElectricalKnowledgeCheckRecord> Rows { get; private set; } = [];
    public IReadOnlyList<OrganizationEmployee> EmployeeOptions { get; private set; } = [];
    /// <summary>Сотрудники штата, которых ещё нет в реестре — для выбора «Добавить».</summary>
    public IReadOnlyList<EbStaffPickItem> AvailableToAdd { get; private set; } = [];
    public Dictionary<string, string> HrStatusByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> FilterOrganizationOptions { get; private set; } = [];
    public IReadOnlyList<string> FilterPositionOptions { get; private set; } = [];
    public IReadOnlyList<string> FilterStatusOptions { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "knowledge";

    public IActionResult OnGet()
    {
        ResolveScope();
        // Старые пустые строки от автозагрузки всего штата — убрать при открытии.
        PurgeEmptyAutoSyncedRows();
        var purged = 0;
        try
        {
            var employedNames = BuildEmployedNamesByOrg();
            var employedSnils = BuildEmployedSnilsByOrg();
            // Люди, которых нет в штате этой орг. (в т.ч. после ошибочного кросс-синка).
            purged = ElectricalKnowledgeCrossOrgSync.PurgeNotEmployedInOrganization(
                _store, employedNames, employedSnils);
            purged += ElectricalKnowledgeCrossOrgSync.PurgeNameCollisionArtifacts(
                _store, employedNames);
            purged += ElectricalKnowledgeCrossOrgSync.DeduplicateWithinOrganizations(_store);
            EnrichSnilsFromStaff();
            // Только выровнять уже существующие строки по СНИЛС.
            // Автосоздание в других орг. — при сохранении (там точный индекс занятости).
            ElectricalKnowledgeCrossOrgSync.ReconcileExisting(_store);
            purged += ElectricalKnowledgeCrossOrgSync.DeduplicateWithinOrganizations(_store);
        }
        catch
        {
            // Открытие реестра не должно падать из‑за кросс-синка.
        }

        LoadPage();
        if (purged > 0 && string.IsNullOrWhiteSpace(Message))
        {
            Message = $"Убраны лишние/ошибочные строки ЭБ: {purged}.";
        }

        return Page();
    }

    public IActionResult OnPostSave()
    {
        ResolveScope();
        SetModuleViewData();
        try
        {
            var previousById = _store.LoadAll()
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var deletedIds = CollectDeletedIdsFromForm();
            Rows = BuildFromForm();
            EnsureUniqueRowIds(Rows);
            EnrichRowsSnilsFromStaff(Rows);

            // Слияние, а не полная замена орг. — иначе при фильтре/сбое формы
            // затирались строки, которые Анна и другие уже заполнили.
            _store.MergeUpsert(Rows, deletedIds);
            ElectricalKnowledgeCrossOrgSync.DeduplicateWithinOrganizations(_store);

            var synced = 0;
            try
            {
                synced = ElectricalKnowledgeCrossOrgSync.PropagateFromSaved(
                    _store,
                    Rows,
                    previousById,
                    BuildEmploymentIndexBySnils(Rows));
                ElectricalKnowledgeCrossOrgSync.ReapplySavedCertification(_store, Rows);
                ElectricalKnowledgeCrossOrgSync.DeduplicateWithinOrganizations(_store);
            }
            catch (Exception ex)
            {
                // Данные уже сохранены в текущей орг. — кросс-синк не должен ронять страницу.
                Message = "Изменения сохранены. Синхронизация по компаниям пропущена: " + ex.Message;
                LoadPage();
                return Page();
            }

            Message = synced > 0
                ? "Изменения сохранены. Аттестация ЭБ синхронизирована по СНИЛС во всех компаниях сотрудника."
                : "Изменения сохранены.";
            LoadPage();
            return Page();
        }
        catch (Exception ex)
        {
            Message = "Не удалось сохранить реестр ЭБ: " + ex.Message;
            try
            {
                LoadPage();
            }
            catch
            {
                Rows ??= [];
            }

            return Page();
        }
    }

    private static void EnsureUniqueRowIds(List<ElectricalKnowledgeCheckRecord> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || !seen.Add(row.Id))
            {
                row.Id = Guid.NewGuid().ToString("N");
                seen.Add(row.Id);
            }
        }
    }

    private List<string> CollectDeletedIdsFromForm()
    {
        var ids = Request.Form["Id"];
        var deletes = Request.Form["Delete"];
        var result = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (i < deletes.Count && deletes[i].ToString() == "true")
            {
                var id = ids[i].ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    private void LoadPage()
    {
        ResolveScope();
        SetModuleViewData();

        // С группой ЭБ — всегда выше строк без группы.
        Rows = LoadRowsForScope()
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.CurrentGroup))
            .ThenByDescending(TrainingFillScore)
            .ThenBy(r => r.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Position, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LoadEmployees();
        BuildFilterOptions();
    }

    /// <summary>
    /// Удаляет строки без заполненных данных ЭБ (остаток старой автосинхронизации штата).
    /// Вручную добавленные с данными не трогаем; только что добавленные пустые
    /// не чистим здесь — очистка только в OnGet.
    /// </summary>
    private void PurgeEmptyAutoSyncedRows()
    {
        var removed = 0;
        foreach (var org in ScopeOrganizations)
        {
            var rows = _store.Load(org);
            var kept = rows.Where(HasTrainingData).ToList();
            if (kept.Count == rows.Count)
            {
                continue;
            }

            removed += rows.Count - kept.Count;
            _store.Save(org, kept);
        }

        if (removed > 0 && string.IsNullOrWhiteSpace(Message))
        {
            Message = $"Убраны пустые строки без данных ЭБ: {removed}. Добавляйте сотрудников из списка ниже.";
        }
    }

    private static bool HasTrainingData(ElectricalKnowledgeCheckRecord row) =>
        row.AddedManually
        || !string.IsNullOrWhiteSpace(row.CurrentGroup)
        || row.CurrentGroupAssignedDate.HasValue
        || !string.IsNullOrWhiteSpace(row.LastCheckResult)
        || !string.IsNullOrWhiteSpace(row.ResponsibleNote);

    /// <summary>Добавить выбранного сотрудника из штата в реестр (без автозагрузки всех).</summary>
    public IActionResult OnPostAddEmployee(string? employeeName, string? employeeOrg)
    {
        ResolveScope();
        SetModuleViewData();

        var name = (employeeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "Выберите сотрудника из списка.";
            LoadPage();
            return Page();
        }

        var rowOrg = (employeeOrg ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rowOrg))
        {
            rowOrg = IsAllOrganizations
                ? (ScopeOrganizations.FirstOrDefault() ?? string.Empty)
                : Organization;
        }

        OrganizationEmployee? match = null;
        foreach (var org in ScopeOrganizations)
        {
            match = LoadOrgStaff(org)
                .FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e.Name)
                    && string.Equals(e.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(employeeOrg)
                        || string.Equals(org.Trim(), rowOrg, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.MatchesForCatalog(org, rowOrg)));
            if (match is not null)
            {
                rowOrg = org.Trim();
                break;
            }
        }

        if (match is null)
        {
            Message = $"Сотрудник «{name}» не найден в штате выбранной организации.";
            LoadPage();
            return Page();
        }

        var orgRows = _store.Load(rowOrg);
        if (orgRows.Any(r =>
                string.Equals(r.EmployeeName?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"«{name}» уже есть в реестре.";
            LoadPage();
            return Page();
        }

        var snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(match.Snils);
        var created = new ElectricalKnowledgeCheckRecord
        {
            Organization = rowOrg,
            EmployeeName = name,
            Snils = snils,
            Position = match.Profession?.Trim() ?? string.Empty,
            AddedManually = true
        };
        ElectricalKnowledgeCrossOrgSync.TryFillFromPeers(_store, created);
        orgRows.Add(created);
        _store.Save(rowOrg, orgRows);

        if (ElectricalKnowledgeCrossOrgSync.PropagateFromSaved(
                _store,
                [created],
                previousById: null,
                BuildEmploymentIndexBySnils([created])) > 0
            || !string.IsNullOrWhiteSpace(created.CurrentGroup))
        {
            Message = $"Добавлен: {name}. Аттестация ЭБ подтянута по СНИЛС из других компаний.";
        }
        else
        {
            Message = $"Добавлен: {name}.";
        }

        LoadPage();
        return Page();
    }

    /// <summary>Индекс занятости по СНИЛС (для синка ЭБ без путаницы однофамильцев).</summary>
    private Dictionary<string, IReadOnlyList<ElectricalKnowledgeCrossOrgSync.EmploymentSite>> BuildEmploymentIndexBySnils(
        IEnumerable<ElectricalKnowledgeCheckRecord> rows)
    {
        var wanted = rows
            .Select(r => ElectricalKnowledgeCrossOrgSync.NormalizeSnils(r.Snils))
            .Where(s => s.Length == 11)
            .ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, List<ElectricalKnowledgeCrossOrgSync.EmploymentSite>>(
            StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<ElectricalKnowledgeCrossOrgSync.EmploymentSite>>(
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (var org in _orgs.AllOrganizations)
        {
            try
            {
                foreach (var e in LoadOrgStaff(org))
                {
                    var snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils);
                    if (snils.Length != 11 || !wanted.Contains(snils))
                    {
                        continue;
                    }

                    var key = "snils:" + snils;
                    if (!map.TryGetValue(key, out var list))
                    {
                        list = [];
                        map[key] = list;
                    }

                    var orgName = org.Trim();
                    // Алиасы одного юрлица («ВИКТОРИЯ СТРОЙ» / с кавычками) — одно место работы.
                    if (list.Any(s =>
                            string.Equals(s.Organization, orgName, StringComparison.OrdinalIgnoreCase)
                            || OrganizationMatcher.MatchesForCatalog(s.Organization, orgName)))
                    {
                        continue;
                    }

                    list.Add(new ElectricalKnowledgeCrossOrgSync.EmploymentSite(
                        orgName,
                        e.Profession?.Trim() ?? string.Empty,
                        snils,
                        e.Name?.Trim() ?? string.Empty));
                }
            }
            catch
            {
                // Битый штат одной орг. не должен ронять сохранение реестра ЭБ.
            }
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<ElectricalKnowledgeCrossOrgSync.EmploymentSite>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, HashSet<string>> BuildEmployedNamesByOrg()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in _orgs.AllOrganizations)
        {
            try
            {
                var names = LoadOrgStaff(org)
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(e => e.Name.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (names.Count > 0)
                {
                    map[org.Trim()] = names;
                }
            }
            catch
            {
                // ignore broken staff file for one org
            }
        }

        return map;
    }

    private Dictionary<string, HashSet<string>> BuildEmployedSnilsByOrg()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in _orgs.AllOrganizations)
        {
            try
            {
                var snils = LoadOrgStaff(org)
                    .Select(e => ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils))
                    .Where(s => s.Length == 11)
                    .ToHashSet(StringComparer.Ordinal);
                if (snils.Count > 0)
                {
                    map[org.Trim()] = snils;
                }
            }
            catch
            {
                // ignore
            }
        }

        return map;
    }

    private void EnrichSnilsFromStaff()
    {
        _store.MutateAll(all =>
        {
            var changed = false;
            foreach (var row in all)
            {
                if (ElectricalKnowledgeCrossOrgSync.NormalizeSnils(row.Snils).Length == 11)
                {
                    continue;
                }

                var snils = ResolveSnilsFromStaff(row.Organization, row.EmployeeName);
                if (snils.Length != 11)
                {
                    continue;
                }

                row.Snils = snils;
                row.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }

            return changed;
        });
    }

    private void EnrichRowsSnilsFromStaff(List<ElectricalKnowledgeCheckRecord> rows)
    {
        foreach (var row in rows)
        {
            if (ElectricalKnowledgeCrossOrgSync.NormalizeSnils(row.Snils).Length == 11)
            {
                continue;
            }

            var snils = ResolveSnilsFromStaff(row.Organization, row.EmployeeName);
            if (snils.Length == 11)
            {
                row.Snils = snils;
            }
        }
    }

    private string ResolveSnilsFromStaff(string? organization, string? employeeName)
    {
        var org = (organization ?? string.Empty).Trim();
        var name = (employeeName ?? string.Empty).Trim();
        if (org.Length == 0 || name.Length == 0)
        {
            return string.Empty;
        }

        var matches = LoadOrgStaff(org)
            .Where(e => !string.IsNullOrWhiteSpace(e.Name)
                        && string.Equals(e.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                        && ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils).Length == 11)
            .Select(e => ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return matches.Count == 1 ? matches[0] : string.Empty;
    }

    private void BuildFilterOptions()
    {
        FilterOrganizationOptions = Rows
            .Select(r => (r.Organization ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FilterPositionOptions = Rows
            .Select(r => (r.Position ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FilterStatusOptions = Rows
            .Select(r => ElectricalKnowledgeCalculator.Compute(r).Status?.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void ResolveScope()
    {
        IsAllOrganizations = _orgs.IsAllOrganizationsSelected(HttpContext);
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ScopeOrganizations = IsAllOrganizations
            ? _orgs.GetVisibleOrganizations(HttpContext).ToList()
            : string.IsNullOrWhiteSpace(Organization)
                ? []
                : [Organization];
    }

    /// <summary>
    /// Тот же список, что на вкладке «Организации → Сотрудники» (без чужих юрлиц / СИЗ).
    /// </summary>
    private IReadOnlyList<OrganizationEmployee> LoadOrgStaff(string organization) =>
        _employees.LoadDirectory(organization);

    private List<ElectricalKnowledgeCheckRecord> LoadRowsForScope()
    {
        List<ElectricalKnowledgeCheckRecord> raw;
        if (IsAllOrganizations)
        {
            var allowed = ScopeOrganizations.ToHashSet(StringComparer.OrdinalIgnoreCase);
            raw = _store.LoadAll()
                .Where(r => allowed.Count == 0
                            || allowed.Contains((r.Organization ?? string.Empty).Trim()))
                .ToList();
        }
        else
        {
            raw = _store.Load(Organization);
        }

        // Показываем только людей из списка сотрудников организации.
        var staffByOrg = new Dictionary<string, (HashSet<string> Names, HashSet<string> Snils)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var org in ScopeOrganizations)
        {
            var staff = LoadOrgStaff(org);
            staffByOrg[org.Trim()] = (
                staff.Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(e => e.Name.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                staff.Select(e => ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils))
                    .Where(s => s.Length == 11)
                    .ToHashSet(StringComparer.Ordinal));
        }

        return raw.Where(r =>
        {
            var org = (r.Organization ?? string.Empty).Trim();
            if (!staffByOrg.TryGetValue(org, out var staff))
            {
                // Чужое написание юрлица — ищем канон в scope.
                var match = staffByOrg.Keys.FirstOrDefault(k =>
                    string.Equals(k, org, StringComparison.OrdinalIgnoreCase)
                    || OrganizationMatcher.MatchesForCatalog(k, org));
                if (match is null || !staffByOrg.TryGetValue(match, out staff))
                {
                    return false;
                }
            }

            var snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(r.Snils);
            if (snils.Length == 11)
            {
                return staff.Snils.Contains(snils);
            }

            var name = (r.EmployeeName ?? string.Empty).Trim();
            return name.Length > 0 && staff.Names.Contains(name);
        }).ToList();
    }

    private void LoadEmployees()
    {
        var options = new List<OrganizationEmployee>();
        var picks = new List<EbStaffPickItem>();
        foreach (var org in ScopeOrganizations)
        {
            foreach (var e in LoadOrgStaff(org)
                         .Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                options.Add(e);
                picks.Add(new EbStaffPickItem
                {
                    Organization = org.Trim(),
                    Name = e.Name.Trim(),
                    Position = e.Profession?.Trim() ?? string.Empty,
                    Snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(e.Snils)
                });
            }
        }

        EmployeeOptions = options
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AvailableToAdd = picks
            .GroupBy(p => $"{p.Organization}|{p.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(p => !Rows.Any(r =>
                string.Equals(r.EmployeeName?.Trim(), p.Name, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(r.Organization)
                    || string.Equals(r.Organization.Trim(), p.Organization, StringComparison.OrdinalIgnoreCase)
                    || OrganizationMatcher.MatchesForCatalog(r.Organization, p.Organization))))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        HrStatusByName = EmployeeHrStatus.BuildMap(
            ScopeOrganizations.SelectMany(LoadOrgStaff));
    }

    public sealed class EbStaffPickItem
    {
        public string Organization { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Snils { get; set; } = string.Empty;
    }

    /// <summary>
    /// Чем больше заполнено обучение (группа / дата / результат), тем выше строка.
    /// </summary>
    private static int TrainingFillScore(ElectricalKnowledgeCheckRecord row)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(row.CurrentGroup))
        {
            score += 8;
        }

        if (row.CurrentGroupAssignedDate.HasValue)
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(row.LastCheckResult))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(row.ResponsibleNote))
        {
            score += 1;
        }

        return score;
    }

    private void SetModuleViewData()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Реестр электробезопасность";
        ViewData["Subtitle"] = IsAllOrganizations ? "Все организации" : Organization;
    }

    private List<ElectricalKnowledgeCheckRecord> BuildFromForm()
    {
        // Дубликаты Id в файле раньше роняли ToDictionary → Error page при сохранении.
        var existing = (IsAllOrganizations ? _store.LoadAll() : _store.Load(Organization))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var list = new List<ElectricalKnowledgeCheckRecord>();
        var ids = Request.Form["Id"];
        var orgs = Request.Form["RowOrganization"];
        var names = Request.Form["EmployeeName"];
        var snilsList = Request.Form["Snils"];
        var positions = Request.Form["Position"];
        var educations = Request.Form["Education"];
        var groups = Request.Form["CurrentGroup"];
        var dates = Request.Form["CurrentGroupAssignedDate"];
        var results = Request.Form["LastCheckResult"];
        var periods = Request.Form["PeriodicityMonths"];
        var notes = Request.Form["ResponsibleNote"];
        var deletes = Request.Form["Delete"];

        for (var i = 0; i < names.Count; i++)
        {
            if (i < deletes.Count && deletes[i].ToString() == "true")
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i])
                ? ids[i].ToString()!
                : Guid.NewGuid().ToString("N");
            existing.TryGetValue(id, out var prev);

            var rowOrg = i < orgs.Count ? orgs[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(rowOrg))
            {
                rowOrg = prev?.Organization?.Trim()
                         ?? (IsAllOrganizations
                             ? ScopeOrganizations.FirstOrDefault() ?? string.Empty
                             : Organization);
            }

            DateTime? assigned = null;
            if (i < dates.Count && DateTime.TryParse(dates[i], out var d))
            {
                assigned = d;
            }

            int? period = null;
            if (i < periods.Count && int.TryParse(periods[i], out var p) && (p == 12 || p == 36))
            {
                period = p;
            }

            var group = ElectricalKnowledgeCalculator.NormalizeGroup(
                i < groups.Count ? groups[i].ToString() : null);

            var snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(
                i < snilsList.Count ? snilsList[i].ToString() : null);
            if (snils.Length != 11)
            {
                snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(prev?.Snils);
            }

            var education = ElectricalKnowledgeCalculator.NormalizeEducation(
                i < educations.Count ? educations[i].ToString() : null);
            var lastResult = i < results.Count ? results[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var note = i < notes.Count ? notes[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var hasEbData = !string.IsNullOrWhiteSpace(group)
                            || assigned.HasValue
                            || !string.IsNullOrWhiteSpace(lastResult)
                            || !string.IsNullOrWhiteSpace(note)
                            || !string.IsNullOrWhiteSpace(education);
            // Временные строки штата из фильтра «все» / «без группы» — не сохраняем пустыми.
            if (prev is null && !hasEbData)
            {
                continue;
            }

            list.Add(new ElectricalKnowledgeCheckRecord
            {
                Id = id,
                Organization = rowOrg,
                EmployeeName = name,
                Snils = snils,
                Position = i < positions.Count ? positions[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                Education = education,
                CurrentGroup = group,
                CurrentGroupAssignedDate = assigned,
                LastCheckResult = lastResult,
                PeriodicityMonths = period,
                ResponsibleNote = note,
                AddedManually = prev?.AddedManually == true || prev is null,
                CreatedAt = prev?.CreatedAt ?? DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            });
        }

        // Одна форма не должна сохранить Франчука 10 раз в одной компании.
        return CollapseDuplicatePeopleInForm(list);
    }

    private static List<ElectricalKnowledgeCheckRecord> CollapseDuplicatePeopleInForm(
        List<ElectricalKnowledgeCheckRecord> rows)
    {
        if (rows.Count < 2)
        {
            return rows;
        }

        static string Key(ElectricalKnowledgeCheckRecord r)
        {
            var org = (r.Organization ?? string.Empty).Trim().ToLowerInvariant();
            var snils = ElectricalKnowledgeCrossOrgSync.NormalizeSnils(r.Snils);
            if (snils.Length == 11)
            {
                return org + "|snils:" + snils;
            }

            return org + "|name:" + (r.EmployeeName ?? string.Empty).Trim().ToLowerInvariant();
        }

        return rows
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.CurrentGroup))
                .ThenByDescending(r =>
                    r.CurrentGroupAssignedDate.HasValue
                    || !string.IsNullOrWhiteSpace(r.LastCheckResult)
                    || !string.IsNullOrWhiteSpace(r.Education))
                .ThenByDescending(TrainingFillScore)
                .ThenByDescending(r => r.ModifiedAt)
                .First())
            .ToList();
    }

    public string EmployeesJson => JsonSerializer.Serialize(
        EmployeeOptions.Select(e => new { name = e.Name, position = e.Profession ?? string.Empty }));

    public string AvailableToAddJson => JsonSerializer.Serialize(
        AvailableToAdd.Select(e => new
        {
            name = e.Name,
            org = e.Organization,
            position = e.Position,
            snils = e.Snils,
            label = IsAllOrganizations && !string.IsNullOrWhiteSpace(e.Organization)
                ? $"{e.Name} — {e.Organization}"
                : (string.IsNullOrWhiteSpace(e.Position) ? e.Name : $"{e.Name} ({e.Position})")
        }));
}
