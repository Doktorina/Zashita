using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.PersonList;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly MedPersonListStore _store;
    private readonly MedFactorStore _factors;
    private readonly MedStandardStore _standards;
    private readonly MedContextService _med;

    public EditModel(
        MedPersonListStore store,
        MedFactorStore factors,
        MedStandardStore standards,
        MedContextService med)
    {
        _store = store;
        _factors = factors;
        _standards = standards;
        _med = med;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public MedPersonListRecord Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];
    public IReadOnlyList<EmployeeLookupItem> EmployeeLookup { get; private set; } = [];
    public IReadOnlyList<FactorItemOption> FactorItems { get; private set; } = [];
    public IReadOnlyList<StandardLookupItem> StandardLookup { get; private set; } = [];
    public IReadOnlyList<string> PeriodicityOptions { get; } = MedPeriodicity.Options;
    public IReadOnlyList<string> ExamTypeOptions { get; } =
    [
        "Предварительный",
        "Периодический",
        "Предсменный / Предрейсовый",
        "Внеочередной",
        "Психиатрическое освидетельствование"
    ];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        LoadPage();
        if (!IsNew && (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal)))
        {
            return RedirectToPage("/Med/ContingentLists", new { tab = "named" });
        }

        if (IsNew)
        {
            Document.Number = _store.SuggestNextNumber();
            Document.Date = DateTime.Today;
            Document.ExamType = "Периодический";
            Document.Factors = "Приказ 29н";
            var employees = _med.GetEmployees(Organization);
            var lines = employees.Count > 0
                ? MedPersonListStore.BuildLinesFromEmployees(employees, _med.GetExams(Organization))
                : [];
            ApplyAgeOverrides(lines);
            Document.Lines = lines.Count > 0 ? lines : [new MedPersonListLine()];
        }

        if (TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
        }

        return Page();
    }

    public IActionResult OnPostSave() => SaveDocument(post: false, close: false);

    public IActionResult OnPostPost() => SaveDocument(post: true, close: false);

    public IActionResult OnPostPostAndClose() => SaveDocument(post: true, close: true);

    public IActionResult OnPostFillFromEmployees()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(document.Number) || document.Number == "<Авто>")
        {
            document.Number = _store.SuggestNextNumber();
        }

        IReadOnlyList<OrganizationEmployee> employees = _med.GetEmployees(Organization);
        var deptFilter = document.Department?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(deptFilter))
        {
            employees = employees
                .Where(e => string.Equals(
                    e.Department?.Trim(),
                    deptFilter,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (employees.Count == 0)
        {
            Message = string.IsNullOrWhiteSpace(deptFilter)
                ? "В справочнике сотрудников организации нет записей. Добавьте их в разделе сотрудников или загрузите из Excel."
                : $"Нет сотрудников для подразделения «{deptFilter}». Очистите подразделение или выберите другое.";
            IsError = true;
            Id = document.Id;
            Document = document;
            if (Document.Lines.Count == 0)
            {
                Document.Lines = [new MedPersonListLine()];
            }

            LoadLookups();
            ViewData["ActiveModule"] = "медосмотры";
            ViewData["IsModulePage"] = true;
            return Page();
        }

        document.Lines = MedPersonListStore.BuildLinesFromEmployees(employees, _med.GetExams(Organization));
        ApplyAgeOverrides(document.Lines);
        if (document.Lines.Count == 0)
        {
            Message = string.IsNullOrWhiteSpace(deptFilter)
                ? "Не найдено сотрудников с заполненным пунктом медосмотра. Сначала заполните пункт в отчёте по медосмотрам или выберите стандарт в строке."
                : $"Для подразделения «{deptFilter}» нет сотрудников с заполненным пунктом медосмотра.";
            IsError = true;
            Id = document.Id;
            Document = document;
            Document.Lines = [new MedPersonListLine()];
            LoadLookups();
            ViewData["ActiveModule"] = "медосмотры";
            ViewData["IsModulePage"] = true;
            return Page();
        }

        _store.Upsert(document);
        SyncExamsFromPersonList(document);
        TempData["Message"] = $"Заполнено из сотрудников: {document.Lines.Count}. Данные переданы в отчёт по медосмотрам.";
        return RedirectToPage(new { id = document.Id });
    }

    public IActionResult OnPostDelete()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }

        return RedirectToPage("/Med/ContingentLists", new { tab = "named" });
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        Organization = _med.GetOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Number) || document.Number == "<Авто>")
        {
            document.Number = _store.SuggestNextNumber();
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        _store.Upsert(document);
        SyncExamsFromPersonList(document);

        if (close)
        {
            return RedirectToPage("/Med/ContingentLists", new { tab = "named", printId = document.Id });
        }

        TempData["Message"] = "Сохранено. Пункты и периодичность обновлены в отчёте по медосмотрам.";
        return RedirectToPage(new { id = document.Id });
    }

    private MedPersonListRecord BuildDocumentFromForm()
    {
        Organization = _med.GetOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = existing ?? new MedPersonListRecord
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id
        };

        document.Organization = Organization;
        document.Number = Request.Form["number"].ToString()?.Trim() ?? document.Number;
        if (DateTime.TryParse(Request.Form["date"], out var date))
        {
            document.Date = date.Date;
        }
        else if (existing is null)
        {
            document.Date = DateTime.Today;
        }

        document.ExamType = Request.Form["examType"].ToString()?.Trim() ?? document.ExamType;
        document.Department = Request.Form["department"].ToString()?.Trim() ?? string.Empty;
        document.Factors = Request.Form["factors"].ToString()?.Trim() ?? document.Factors;
        document.Comment = Request.Form["comment"].ToString()?.Trim() ?? string.Empty;

        var lineIds = Request.Form["lineId"];
        var employeeNames = Request.Form["employeeName"];
        var birthDates = Request.Form["birthDate"];
        var professions = Request.Form["profession"];
        var workplaces = Request.Form["workplace"];
        var itemNumbers = Request.Form["itemNumber"];
        var periodicities = Request.Form["periodicity"];
        var lines = new List<MedPersonListLine>();
        for (var i = 0; i < employeeNames.Count; i++)
        {
            var name = employeeNames[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            DateTime? birth = null;
            if (i < birthDates.Count && DateTime.TryParse(birthDates[i], out var parsedBirth))
            {
                birth = parsedBirth.Date;
            }

            var periodicity = MedPeriodicity.ApplyUnder21Override(
                birth,
                i < periodicities.Count ? periodicities[i].ToString() : MedPeriodicity.OnceAYear);

            lines.Add(new MedPersonListLine
            {
                Id = i < lineIds.Count && !string.IsNullOrWhiteSpace(lineIds[i])
                    ? lineIds[i].ToString()!
                    : Guid.NewGuid().ToString("N"),
                EmployeeName = name,
                BirthDate = birth,
                Profession = i < professions.Count ? professions[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                Workplace = i < workplaces.Count ? workplaces[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                ItemNumber = MedItemNumber.Normalize(
                    i < itemNumbers.Count ? itemNumbers[i].ToString()?.Trim() ?? string.Empty : string.Empty),
                Periodicity = periodicity
            });
        }

        document.Lines = lines;
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    /// <summary>Переносит пункты/периодичность/должность/отдел из списка в отчёт по медосмотрам.</summary>
    private void SyncExamsFromPersonList(MedPersonListRecord document)
    {
        var existing = _med.GetExams(Organization).ToList();
        var toUpsert = new List<MedExamRecord>();

        foreach (var line in document.Lines)
        {
            var name = line.EmployeeName?.Trim() ?? string.Empty;
            var item = MedItemNumber.Normalize(line.ItemNumber);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var match = existing.FirstOrDefault(e =>
                string.Equals(e.EmployeeName?.Trim(), name, StringComparison.OrdinalIgnoreCase) &&
                MedItemNumber.EqualsNormalized(e.ItemNumber, item));

            match ??= existing.FirstOrDefault(e =>
                string.Equals(e.EmployeeName?.Trim(), name, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(MedItemNumber.Normalize(e.ItemNumber)));

            match ??= new MedExamRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = Organization,
                ExamDate = DateTime.Today,
                Status = string.Empty
            };

            match.Organization = Organization;
            match.EmployeeName = name;
            match.Profession = line.Profession?.Trim() ?? match.Profession;
            match.Department = line.Workplace?.Trim() ?? match.Department;
            match.ItemNumber = item;
            match.Periodicity = MedPeriodicity.ApplyUnder21Override(line.BirthDate, line.Periodicity);
            match.BirthDate = line.BirthDate ?? match.BirthDate;
            if (!string.IsNullOrWhiteSpace(document.ExamType))
            {
                match.ExamType = document.ExamType.Trim();
            }

            match.RecalculateNextExamDate();
            toUpsert.Add(match);

            var idx = existing.FindIndex(e => string.Equals(e.Id, match.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                existing[idx] = match;
            }
            else
            {
                existing.Add(match);
            }
        }

        if (toUpsert.Count > 0)
        {
            _med.UpsertExams(toUpsert);
        }
    }

    private static void ApplyAgeOverrides(List<MedPersonListLine> lines)
    {
        foreach (var line in lines)
        {
            line.Periodicity = MedPeriodicity.ApplyUnder21Override(line.BirthDate, line.Periodicity);
        }
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;

        Document = !IsNew ? _store.GetById(Id!) ?? new MedPersonListRecord() : new MedPersonListRecord();
        if (Document.Lines.Count == 0)
        {
            Document.Lines = [new MedPersonListLine()];
        }

        ApplyAgeOverrides(Document.Lines);
        LoadLookups();
    }

    private void LoadLookups()
    {
        var fromDocs = _store.LoadByOrganization(Organization)
            .Select(row => row.Department)
            .Where(name => !string.IsNullOrWhiteSpace(name));
        var fromEmployees = _med.GetEmployees(Organization)
            .Select(e => e.Department)
            .Where(name => !string.IsNullOrWhiteSpace(name));
        Departments = fromDocs
            .Concat(fromEmployees)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var employees = _med.GetEmployees(Organization)
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        EmployeeNames = employees.Select(e => e.Name.Trim()).ToList();
        EmployeeLookup = employees
            .Select(e => new EmployeeLookupItem(
                e.Name.Trim(),
                e.BirthDate,
                e.Profession ?? string.Empty,
                e.Department ?? string.Empty))
            .ToList();

        FactorItems = _factors.LoadAll()
            .Where(f => !f.IsGroup && f.IsActive && !string.IsNullOrWhiteSpace(f.ItemNumber))
            .OrderBy(f => f.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                var period = MedPeriodicity.Normalize(f.Periodicity);
                if (!MedPeriodicity.Options.Contains(period, StringComparer.OrdinalIgnoreCase))
                {
                    period = MedPeriodicity.OnceAYear;
                }

                return new FactorItemOption(
                    MedItemNumber.Normalize(f.ItemNumber),
                    period,
                    $"{MedItemNumber.Display(f.ItemNumber)} — {f.Name}");
            })
            .ToList();

        StandardLookup = _standards.LoadByOrganization(Organization)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new StandardLookupItem(
                s.Id,
                string.IsNullOrWhiteSpace(s.Name) ? "Без названия" : s.Name.Trim(),
                s.Profession ?? string.Empty,
                s.Department ?? string.Empty,
                (s.Rules ?? [])
                    .Where(r => r.IsActive)
                    .Select(r => new StandardRuleItem(
                        MedItemNumber.Normalize(r.ItemNumber),
                        string.IsNullOrWhiteSpace(r.FactorName) ? string.Empty : r.FactorName.Trim(),
                        MedPeriodicity.Normalize(r.Periodicity),
                        string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind.Trim()))
                    .ToList()))
            .ToList();
    }

    public sealed record FactorItemOption(string ItemNumber, string Periodicity, string Display);

    public sealed record EmployeeLookupItem(
        string Name,
        DateTime? BirthDate,
        string Profession,
        string Department);

    public sealed record StandardLookupItem(
        string Id,
        string Name,
        string Profession,
        string Department,
        IReadOnlyList<StandardRuleItem> Rules);

    public sealed record StandardRuleItem(
        string ItemNumber,
        string FactorName,
        string Periodicity,
        string ExamKind);
}
