using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class RegistryModel : PageModel
{
    private readonly MedRegistryStore _store;
    private readonly MedContextService _med;

    public RegistryModel(MedRegistryStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Organization { get; private set; } = string.Empty;
    public string PageTitle { get; private set; } = "Реестр";
    public IReadOnlyList<MedRegistryRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Employees { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Title { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty]
    public DateTime Date { get; set; } = DateTime.Today;

    [BindProperty]
    public string Department { get; set; } = string.Empty;

    [BindProperty]
    public string EmployeeName { get; set; } = string.Empty;

    [BindProperty]
    public string Status { get; set; } = "Черновик";

    [BindProperty]
    public string Comment { get; set; } = string.Empty;

    public void OnGet()
    {
        var kind = MedRegistryStore.NormalizeKind(Kind ?? "general");
        Kind = kind;
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        PageTitle = string.IsNullOrWhiteSpace(Title) ? ResolveTitle(Kind) : Title!;
        Employees = _med.GetEmployeeNames(Organization);
        Rows = _store.LoadByKind(Kind, Organization);

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = Rows.FirstOrDefault(r => r.Id == EditId);
            if (row is not null)
            {
                Date = row.Date;
                Department = row.Department;
                EmployeeName = row.EmployeeName;
                Status = row.Status;
                Comment = row.Comment;
            }
        }
        else if (Employees.Count > 0)
        {
            EmployeeName = Employees[0];
        }
    }

    public IActionResult OnPostSave(string? kind)
    {
        Kind = MedRegistryStore.NormalizeKind(kind ?? "general");
        Organization = _med.GetOrganization(HttpContext);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new MedRegistryRow(id, Kind, Date.Date, Organization, Department.Trim(),
            EmployeeName.Trim(), Status.Trim(), Comment.Trim()));
        return RedirectToPage(new { kind = Kind, title = Title, editId = id });
    }

    public IActionResult OnPostDelete(string deleteId, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new { kind = MedRegistryStore.NormalizeKind(kind ?? "general"), title = Title });
    }

    private static string ResolveTitle(string kind) => kind switch
    {
        "schedule" => "Планирование медосмотров",
        "direction" => "Направления на медосмотр",
        "cost" => "Расчёты стоимости медосмотров",
        "restrictions" => "Ограничения физ. лиц",
        "experience" => "Записи о стаже для медосмотров",
        "contingent" => "Перечень контингентов",
        "shift-contingent" => "Перечень контингентов ежесменного медосмотра",
        "exceptions" => "Исключения из списка медосмотра",
        "prevention" => "Профилактика и лечение",
        "shift-list" => "Список лиц ежесменного медосмотра",
        "pfz" => "Профзаболевания",
        "pfz-survey" => "Опросы персонала по профзаболеваниям",
        "pfz-investigation" => "Расследования профзаболеваний",
        "pfz-actions" => "Мероприятия по профзаболеваниям",
        _ => "Реестр медосмотров"
    };
}
