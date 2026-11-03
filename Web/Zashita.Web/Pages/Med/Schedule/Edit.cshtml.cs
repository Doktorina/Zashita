using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Schedule;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly MedScheduleStore _store;
    private readonly MedContextService _med;
    private readonly MedStandardStore _standards;
    private readonly OrganizationEmployeesService _employees;

    public EditModel(
        MedScheduleStore store,
        MedContextService med,
        MedStandardStore standards,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _med = med;
        _standards = standards;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public MedScheduleDocument Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> PeriodicityOptions { get; } = MedPeriodicity.Options;
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];
    public string EmployeeLookupJson { get; private set; } = "[]";
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        LoadPage();
        if (IsNew)
        {
            Document.Number = _store.SuggestNextNumber();
            Document.Date = DateTime.Now;
            Document.ExamType = "Периодический";
            Document.Lines = [new MedScheduleLine()];
        }

        if (Document.Lines.Count == 0)
        {
            Document.Lines = [new MedScheduleLine()];
        }

        MedDashboardBuilder.EnrichScheduleLinesFromEmployees(Document, _med.GetEmployees(Organization));
        return Page();
    }

    public IActionResult OnPostSave() => Save(false, false);
    public IActionResult OnPostPost() => Save(true, false);
    public IActionResult OnPostPostAndClose() => Save(true, true);

    public IActionResult OnPostFillFromDates()
    {
        Organization = _med.GetOrganization(HttpContext);
        var doc = Build();
        var exams = _med.GetExams(Organization);
        var employees = _med.GetEmployees(Organization);
        doc.Lines = MedDashboardBuilder.BuildScheduleLinesFromExams(
            exams,
            DateTime.Today,
            doc.Department,
            employees).ToList();
        if (doc.Lines.Count == 0)
        {
            doc.Lines = [new MedScheduleLine()];
            Message = "Нет записей в журнале медосмотров — добавьте людей вручную или в «Проведение медосмотра».";
            Document = doc;
            LoadPageBasics();
            return Page();
        }

        _store.Upsert(doc);
        return RedirectToPage(new { id = doc.Id });
    }

    public IActionResult OnPostDelete()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }

        return RedirectToPage("/Med/Schedule");
    }

    private IActionResult Save(bool post, bool close)
    {
        Organization = _med.GetOrganization(HttpContext);
        var doc = Build();
        MedDashboardBuilder.EnrichScheduleLinesFromEmployees(doc, _med.GetEmployees(Organization));
        EnrichLinesFromExamsAndStandards(doc);
        doc.IsPosted = post || (_store.GetById(doc.Id)?.IsPosted ?? false);
        _store.Upsert(doc);
        return close
            ? RedirectToPage("/Med/Schedule", new { printId = doc.Id })
            : RedirectToPage(new { id = doc.Id });
    }

    private void EnrichLinesFromExamsAndStandards(MedScheduleDocument doc)
    {
        var exams = _med.GetExams(Organization);
        var standards = _standards.LoadByOrganization(Organization);
        var staff = _employees.LoadForMedReport(Organization);

        foreach (var line in doc.Lines)
        {
            var name = line.EmployeeName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var employee = staff.FirstOrDefault(e =>
                string.Equals(e.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (employee is not null)
            {
                if (string.IsNullOrWhiteSpace(line.Department))
                {
                    line.Department = employee.Department?.Trim() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(line.Profession))
                {
                    line.Profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
                }

                if (line.BirthDate is null || line.BirthDate.Value.Year < 1900)
                {
                    line.BirthDate = employee.BirthDate;
                }
            }

            if (string.IsNullOrWhiteSpace(line.ItemNumber) || string.IsNullOrWhiteSpace(line.Periodicity))
            {
                var exam = exams
                    .Where(e => string.Equals(e.EmployeeName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
                    .OrderByDescending(e => e.ExamDate)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(MedItemNumber.Normalize(e.ItemNumber)));

                if (exam is not null)
                {
                    if (string.IsNullOrWhiteSpace(line.ItemNumber))
                    {
                        line.ItemNumber = MedItemNumber.Normalize(exam.ItemNumber);
                    }

                    if (string.IsNullOrWhiteSpace(line.Periodicity) ||
                        string.Equals(line.Periodicity, MedPeriodicity.OnceAYear, StringComparison.OrdinalIgnoreCase))
                    {
                        line.Periodicity = MedPeriodicity.Normalize(exam.Periodicity);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(line.ItemNumber) && employee is not null)
            {
                var standard = MedStandardAssignmentBuilder.ResolveForEmployee(employee, standards);
                var rule = (standard?.Rules ?? [])
                    .Where(r => r.IsActive)
                    .Where(r => !MedStandardAssignmentBuilder.IsPsychiatricKind(r.ExamKind))
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(MedItemNumber.Normalize(r.ItemNumber)));
                if (rule is not null)
                {
                    line.ItemNumber = MedItemNumber.Normalize(rule.ItemNumber);
                    if (string.IsNullOrWhiteSpace(line.Periodicity))
                    {
                        line.Periodicity = MedPeriodicity.Normalize(rule.Periodicity);
                    }
                }
            }
        }
    }

    private MedScheduleDocument Build()
    {
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var doc = existing ?? new MedScheduleDocument
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!
        };
        doc.Organization = _med.GetOrganization(HttpContext);
        doc.Number = Request.Form["number"].ToString()?.Trim() ?? doc.Number;
        if (string.IsNullOrWhiteSpace(doc.Number) || doc.Number == "<Авто>")
        {
            doc.Number = _store.SuggestNextNumber();
        }

        if (DateTime.TryParse(Request.Form["date"], out var date))
        {
            doc.Date = date;
        }

        doc.ExamType = Request.Form["examType"].ToString()?.Trim() ?? doc.ExamType;
        doc.Department = Request.Form["department"].ToString()?.Trim() ?? "";
        doc.MedicalInstitution = Request.Form["medicalInstitution"].ToString()?.Trim() ?? "";
        doc.EmployerOwnership = Request.Form["employerOwnership"].ToString()?.Trim() ?? "РОССИЙСКАЯ СОБСТВЕННОСТЬ";
        doc.EmployerAddress = Request.Form["employerAddress"].ToString()?.Trim() ?? "";
        doc.EmployerInn = Request.Form["employerInn"].ToString()?.Trim() ?? "";
        doc.EmployerOkved = Request.Form["employerOkved"].ToString()?.Trim() ?? "";
        doc.Comment = Request.Form["comment"].ToString()?.Trim() ?? "";

        var lineIds = Request.Form["lineId"];
        var names = Request.Form["employeeName"];
        var professions = Request.Form["profession"];
        var departments = Request.Form["lineDepartment"];
        var birthDates = Request.Form["birthDate"];
        var genders = Request.Form["gender"];
        var activityKinds = Request.Form["activityKinds"];
        var itemNumbers = Request.Form["itemNumber"];
        var periodicities = Request.Form["periodicity"];
        var examDates = Request.Form["examDate"];
        var comments = Request.Form["lineComment"];
        var lines = new List<MedScheduleLine>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var examDate = DateTime.Today;
            if (i < examDates.Count && DateTime.TryParse(examDates[i], out var parsed))
            {
                examDate = parsed.Date;
            }

            DateTime? birthDate = null;
            if (i < birthDates.Count && DateTime.TryParse(birthDates[i], out var birthParsed) && birthParsed.Year > 1900)
            {
                birthDate = birthParsed.Date;
            }

            var lineDept = i < departments.Count ? departments[i].ToString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(lineDept))
            {
                lineDept = doc.Department;
            }

            lines.Add(new MedScheduleLine
            {
                Id = i < lineIds.Count && !string.IsNullOrWhiteSpace(lineIds[i])
                    ? lineIds[i].ToString()!
                    : Guid.NewGuid().ToString("N"),
                EmployeeName = name,
                Profession = i < professions.Count ? professions[i].ToString()?.Trim() ?? "" : "",
                Department = lineDept,
                BirthDate = birthDate,
                Gender = i < genders.Count ? genders[i].ToString()?.Trim() ?? "" : "",
                ActivityKinds = i < activityKinds.Count ? activityKinds[i].ToString()?.Trim() ?? "" : "",
                ItemNumber = MedItemNumber.Normalize(
                    i < itemNumbers.Count ? itemNumbers[i].ToString() : ""),
                Periodicity = MedPeriodicity.Normalize(
                    i < periodicities.Count ? periodicities[i].ToString() : MedPeriodicity.OnceAYear),
                ExamDate = examDate,
                Comment = i < comments.Count ? comments[i].ToString()?.Trim() ?? "" : ""
            });
        }

        doc.Lines = lines;
        doc.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return doc;
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        LoadPageBasics();
        Document = !IsNew ? _store.GetById(Id!) ?? new MedScheduleDocument() : new MedScheduleDocument();
    }

    private void LoadPageBasics()
    {
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        var employees = _employees.LoadForMedReport(Organization);
        var exams = _med.GetExams(Organization);
        var standards = _standards.LoadByOrganization(Organization);

        EmployeeNames = employees
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Departments = DepartmentSelect.Merge(
            employees.Select(e => e.Department),
            _store.LoadByOrganization(Organization).Select(d => d.Department));

        var examByPerson = exams
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeName))
            .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
            .Where(e => !string.IsNullOrWhiteSpace(MedItemNumber.Normalize(e.ItemNumber)))
            .GroupBy(e => e.EmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.ExamDate).First(),
                StringComparer.OrdinalIgnoreCase);

        EmployeeLookupJson = JsonSerializer.Serialize(employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e =>
            {
                var name = e.Name.Trim();
                examByPerson.TryGetValue(name, out var exam);
                var item = exam is not null ? MedItemNumber.Normalize(exam.ItemNumber) : string.Empty;
                var period = exam is not null
                    ? MedPeriodicity.Normalize(exam.Periodicity)
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(item))
                {
                    var standard = MedStandardAssignmentBuilder.ResolveForEmployee(e, standards);
                    var rule = (standard?.Rules ?? [])
                        .Where(r => r.IsActive)
                        .Where(r => !MedStandardAssignmentBuilder.IsPsychiatricKind(r.ExamKind))
                        .FirstOrDefault(r => !string.IsNullOrWhiteSpace(MedItemNumber.Normalize(r.ItemNumber)));
                    if (rule is not null)
                    {
                        item = MedItemNumber.Normalize(rule.ItemNumber);
                        period = MedPeriodicity.Normalize(rule.Periodicity);
                    }
                }

                return new
                {
                    name,
                    department = e.Department?.Trim() ?? string.Empty,
                    profession = ProfessionMatcher.Normalize(e.Profession ?? string.Empty),
                    birth = e.BirthDate is { Year: > 1900 } b ? b.ToString("yyyy-MM-dd") : string.Empty,
                    gender = string.Empty,
                    itemNumber = item,
                    periodicity = string.IsNullOrWhiteSpace(period) ? MedPeriodicity.OnceAYear : period
                };
            }));
    }
}
