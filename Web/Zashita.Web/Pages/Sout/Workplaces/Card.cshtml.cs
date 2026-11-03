using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Workplaces;

[RequireLogin]
public class CardModel : PageModel
{
    private readonly WorkplaceStore _store;
    private readonly OrganizationStore _orgs;
    private readonly SoutConditionsClassStore _classes;
    private readonly SoutCatalogStore _catalog;
    private readonly OrganizationEmployeesService _employees;
    private readonly SoutCardDocxImportService _wordImport;

    public CardModel(
        WorkplaceStore store,
        OrganizationStore orgs,
        SoutConditionsClassStore classes,
        SoutCatalogStore catalog,
        OrganizationEmployeesService employees,
        SoutCardDocxImportService wordImport)
    {
        _store = store;
        _orgs = orgs;
        _classes = classes;
        _catalog = catalog;
        _employees = employees;
        _wordImport = wordImport;
    }

    public string Id { get; private set; } = string.Empty;
    public WorkplaceRecord Workplace { get; private set; } = new();
    public IReadOnlyList<string> ConditionsClassOptions { get; private set; } = [];
    public IReadOnlyList<SoutCatalogRecord> EquipmentOptions { get; private set; } = [];
    public IReadOnlyList<SoutCatalogRecord> MaterialsOptions { get; private set; } = [];
    public IReadOnlyList<OrganizationEmployee> EmployeeOptions { get; private set; } = [];
    public HashSet<string> SelectedEquipmentIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedMaterialsIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedEmployeeIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet(string id) => LoadPage(id);

    public IActionResult OnPostSave(string id)
    {
        var workplace = ResolveWorkplace(id);
        if (workplace is null)
        {
            NotFound = true;
            return Page();
        }

        ApplyFromForm(workplace);
        workplace.SoutAssessmentNotConducted = false;
        workplace.ModifiedAt = DateTime.UtcNow;
        _store.Upsert(workplace);
        TempData["Message"] = "Карта СОУТ сохранена.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostMarkNotConducted(string id)
    {
        var workplace = ResolveWorkplace(id);
        if (workplace is null)
        {
            NotFound = true;
            return Page();
        }

        workplace.SoutAssessmentNotConducted = true;
        workplace.SoutAssessmentDate = null;
        workplace.SoutReportNumber = string.Empty;
        workplace.ConditionsClass = string.Empty;
        workplace.DeclarationOfConformity = false;
        workplace.RequiresUnscheduledSout = false;
        workplace.SoutCard ??= new SoutCardDetails();
        workplace.SoutCard.PreparedDate = null;
        workplace.SoutCard.FinalClass = string.Empty;
        workplace.SoutCard.EmployeeIds = [];
        workplace.SoutCard.ExtraLines ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        workplace.SoutCard.ExtraLines.Remove("021");
        if (string.Equals(workplace.LinkedSoutCardWorkplaceId, workplace.Id, StringComparison.OrdinalIgnoreCase))
        {
            workplace.LinkedSoutCardWorkplaceId = string.Empty;
        }
        workplace.ModifiedAt = DateTime.UtcNow;
        _store.Upsert(workplace);

        TempData["Message"] = $"«{workplace.Name}»: специальная оценка не проведена.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostImportWord(string id, List<IFormFile>? wordFiles)
    {
        var workplace = ResolveWorkplace(id);
        if (workplace is null)
        {
            NotFound = true;
            return Page();
        }

        // Чекбокс multipart: читаем явно (model binder часто оставляет false).
        var overwriteExisting = IsFormFlag("overwriteExisting");

        var files = (wordFiles ?? []).Where(f => f is { Length: > 0 }).ToList();
        if (files.Count == 0)
        {
            TempData["Message"] = "Выберите один или несколько файлов Word (.docx).";
            TempData["IsError"] = "1";
            return RedirectToPage(new { id });
        }

        if (files.Any(f => !string.Equals(Path.GetExtension(f.FileName), ".docx", StringComparison.OrdinalIgnoreCase)))
        {
            TempData["Message"] = "Нужны файлы .docx (не скан и не .doc).";
            TempData["IsError"] = "1";
            return RedirectToPage(new { id });
        }

        try
        {
            var opened = new List<MemoryStream>();
            try
            {
                var payloads = new List<(Stream Stream, string FileName)>();
                foreach (var f in files)
                {
                    var ms = new MemoryStream();
                    f.CopyTo(ms);
                    ms.Position = 0;
                    opened.Add(ms);
                    payloads.Add((ms, f.FileName));
                }

                var staff = _employees.LoadForReport(workplace.Organization);
                var (ok, message) = _wordImport.ApplyMatchingCardToWorkplaceFromFiles(
                    workplace, payloads, overwriteExisting, staff);
                TempData["Message"] = message;
                if (!ok)
                {
                    TempData["IsError"] = "1";
                }
            }
            finally
            {
                foreach (var ms in opened)
                {
                    ms.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Не удалось разобрать Word: {ex.Message}";
            TempData["IsError"] = "1";
        }

        return RedirectToPage(new { id });
    }

    private void ApplyFromForm(WorkplaceRecord workplace)
    {
        workplace.SoutReportNumber = Request.Form["soutReportNumber"].ToString()?.Trim() ?? string.Empty;
        workplace.OkCode = Request.Form["okCode"].ToString()?.Trim() ?? string.Empty;
        workplace.Profession = Request.Form["profession"].ToString()?.Trim() ?? workplace.Profession;
        workplace.Department = Request.Form["department"].ToString()?.Trim() ?? workplace.Department;
        workplace.ConditionsClass = Request.Form["conditionsClass"].ToString()?.Trim() ?? string.Empty;
        workplace.DeclarationOfConformity = Request.Form["declaration"].Count > 0
            || string.Equals(Request.Form["declaration"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Request.Form["declaration"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        workplace.RequiresUnscheduledSout = Request.Form["unscheduled"].Count > 0
            || string.Equals(Request.Form["unscheduled"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Request.Form["unscheduled"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(workplace.Profession))
        {
            workplace.Name = workplace.Profession;
        }

        if (int.TryParse(Request.Form["workerCount"].ToString(), out var workers) && workers >= 0)
        {
            workplace.WorkerCount = workers;
        }

        // Справочники общие; организация — из самой карты (фильтр уже синхронизирован).
        var organization = workplace.Organization;
        var equipmentSelected = Request.Form["equipmentIds"]
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var materialsSelected = Request.Form["materialsIds"]
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var employeeSelected = Request.Form["employeeIds"]
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        workplace.SoutCard ??= new SoutCardDetails();
        // Пустое поле даты = сброс (не подставляем «сегодня»).
        var preparedRaw = Request.Form["preparedDate"].ToString();
        var prepared = string.IsNullOrWhiteSpace(preparedRaw) ? null : ParseDate(preparedRaw);
        workplace.SoutCard.PreparedDate = prepared;
        workplace.SoutAssessmentDate = prepared;
        workplace.SoutCard.AnalogousWorkplaces = Request.Form["analogousWorkplaces"].ToString()?.Trim() ?? string.Empty;
        workplace.SoutCard.AnalogousWorkerCount = ParseNonNegative(Request.Form["analogousWorkerCount"]);
        workplace.SoutCard.WorkersWomen = ParseNonNegative(Request.Form["workersWomen"]);
        workplace.SoutCard.WorkersUnder18 = ParseNonNegative(Request.Form["workersUnder18"]);
        workplace.SoutCard.WorkersDisabled = ParseNonNegative(Request.Form["workersDisabled"]);
        workplace.SoutCard.EmployeeIds = employeeSelected;
        workplace.SoutCard.Equipment = JoinCatalogNames(_catalog.LoadEquipment(organization), equipmentSelected);
        workplace.SoutCard.Materials = JoinCatalogNames(_catalog.LoadMaterials(organization), materialsSelected);
        workplace.SoutCard.FinalClass = workplace.ConditionsClass;

        // Стр. 021 — явный признак закрепления для автовыбора карты в отчёте.
        SyncCardLine021(workplace, organization, employeeSelected);

        // Карта = это же РМ: без самопривязки в отчёте закреплённый сотрудник
        // остаётся с пустым LinkedSoutCardWorkplaceId («без карты СОУТ»).
        if (employeeSelected.Count > 0 &&
            string.IsNullOrWhiteSpace(workplace.LinkedSoutCardWorkplaceId))
        {
            workplace.LinkedSoutCardWorkplaceId = workplace.Id;
        }
        else if (employeeSelected.Count == 0
                 && string.Equals(workplace.LinkedSoutCardWorkplaceId, workplace.Id, StringComparison.OrdinalIgnoreCase))
        {
            workplace.LinkedSoutCardWorkplaceId = string.Empty;
        }

        LinkCatalogToWorkplace(_catalog.LoadEquipment(organization), organization, workplace.Id, equipmentSelected, isEquipment: true);
        LinkCatalogToWorkplace(_catalog.LoadMaterials(organization), organization, workplace.Id, materialsSelected, isEquipment: false);
    }

    private void SyncCardLine021(
        WorkplaceRecord workplace,
        string organization,
        IReadOnlyList<string> employeeIds)
    {
        workplace.SoutCard ??= new SoutCardDetails();
        workplace.SoutCard.ExtraLines ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (employeeIds.Count == 0)
        {
            workplace.SoutCard.ExtraLines.Remove("021");
            return;
        }

        var staff = _employees.Load(organization);
        var byId = staff
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        foreach (var id in employeeIds)
        {
            if (!byId.TryGetValue(id, out var emp))
            {
                continue;
            }

            var snils = OneCEmployeeListParser.FormatSnils(emp.Snils);
            if (!string.IsNullOrWhiteSpace(snils))
            {
                parts.Add(snils);
            }
            else if (!string.IsNullOrWhiteSpace(emp.Name))
            {
                parts.Add(emp.Name.Trim());
            }
        }

        if (parts.Count == 0)
        {
            workplace.SoutCard.ExtraLines.Remove("021");
        }
        else
        {
            workplace.SoutCard.ExtraLines["021"] = string.Join("; ", parts);
        }
    }

    private void LinkCatalogToWorkplace(
        List<SoutCatalogRecord> all,
        string organization,
        string workplaceId,
        HashSet<string> selectedIds,
        bool isEquipment)
    {
        var changed = false;
        foreach (var item in all.Where(x => !x.IsGroup))
        {
            item.WorkplaceIds ??= [];
            var has = item.WorkplaceIds.Any(id => string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase));
            var should = selectedIds.Contains(item.Id);
            if (should && !has)
            {
                item.WorkplaceIds.Add(workplaceId);
                item.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }
            else if (!should && has)
            {
                item.WorkplaceIds.RemoveAll(id => string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase));
                item.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        if (isEquipment)
        {
            _catalog.SaveEquipment(organization, all);
        }
        else
        {
            _catalog.SaveMaterials(organization, all);
        }
    }

    private static string JoinCatalogNames(IEnumerable<SoutCatalogRecord> items, HashSet<string> selectedIds) =>
        string.Join(
            Environment.NewLine,
            items
                .Where(x => !x.IsGroup && selectedIds.Contains(x.Id))
                .Select(DisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n)));

    private static string DisplayName(SoutCatalogRecord item) =>
        !string.IsNullOrWhiteSpace(item.FullName) ? item.FullName.Trim()
        : !string.IsNullOrWhiteSpace(item.Name) ? item.Name.Trim()
        : item.Code?.Trim() ?? string.Empty;

    private static int ParseNonNegative(Microsoft.Extensions.Primitives.StringValues raw) =>
        int.TryParse(raw.ToString(), out var n) && n >= 0 ? n : 0;

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var iso))
        {
            return iso.Date;
        }

        return DateTime.TryParse(
            text,
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
            System.Globalization.DateTimeStyles.None,
            out var any)
            ? any.Date
            : null;
    }

    private WorkplaceRecord? ResolveWorkplace(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var workplace = _store.GetById(id.Trim());
        if (workplace is null || workplace.IsGroup)
        {
            return null;
        }

        // Подстраиваем фильтр под организацию карты — иначе при другой ЮЛ в тулбаре карта не открывалась.
        if (!_orgs.SelectOrganizationForRecord(HttpContext, workplace.Organization))
        {
            return null;
        }

        return workplace;
    }

    private IActionResult LoadPage(string id)
    {
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        Message = TempData["Message"] as string;
        IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        ConditionsClassOptions = _classes.Load();

        var workplace = ResolveWorkplace(id);
        if (workplace is null)
        {
            NotFound = true;
            Id = id;
            return Page();
        }

        Id = workplace.Id;
        // Не подставляем «сегодня» при открытии — иначе нельзя стереть дату.
        SoutCardPrintBuilder.SyncAssessmentDates(workplace);

        Workplace = workplace;
        var organization = workplace.Organization;
        ViewData["Organization"] = organization;
        ViewData["Subtitle"] = organization;
        EquipmentOptions = _catalog.LoadEquipment(organization).Where(x => !x.IsGroup).OrderBy(DisplayName).ToList();
        MaterialsOptions = _catalog.LoadMaterials(organization).Where(x => !x.IsGroup).OrderBy(DisplayName).ToList();
        SelectedEquipmentIds = ResolveSelectedIds(EquipmentOptions, workplace.Id, workplace.SoutCard?.Equipment);
        SelectedMaterialsIds = ResolveSelectedIds(MaterialsOptions, workplace.Id, workplace.SoutCard?.Materials);
        EmployeeOptions = _employees.LoadForReport(organization)
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SelectedEmployeeIds = new HashSet<string>(
            workplace.SoutCard?.EmployeeIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        return Page();
    }

    private static HashSet<string> ResolveSelectedIds(
        IReadOnlyList<SoutCatalogRecord> options,
        string workplaceId,
        string? savedText)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in options)
        {
            if (item.WorkplaceIds?.Any(id => string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                selected.Add(item.Id);
            }
        }

        if (selected.Count > 0 || string.IsNullOrWhiteSpace(savedText))
        {
            return selected;
        }

        var parts = savedText
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var match = options.FirstOrDefault(o =>
                string.Equals(DisplayName(o), part, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Name, part, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.FullName, part, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                selected.Add(match.Id);
            }
        }

        return selected;
    }

    private bool IsFormFlag(string name)
    {
        foreach (var raw in Request.Form[name])
        {
            var v = (raw ?? string.Empty).Trim();
            if (v.Length == 0)
            {
                continue;
            }

            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class CatalogMultiSelectModel
{
    public string FieldName { get; set; } = string.Empty;
    public string Placeholder { get; set; } = "Выберите";
    public IReadOnlyList<SoutCatalogRecord> Items { get; set; } = [];
    public HashSet<string> SelectedIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
