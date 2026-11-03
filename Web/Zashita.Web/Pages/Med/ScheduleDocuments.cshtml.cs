using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ScheduleDocumentsModel : PageModel
{
    private readonly MedScheduleStore _store;
    private readonly MedContextService _med;

    public ScheduleDocumentsModel(MedScheduleStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    [BindProperty(SupportsGet = true)] public string? Department { get; set; }
    [BindProperty(SupportsGet = true)] public string? ExamType { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? PrintId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedScheduleDocument> Rows { get; private set; } = [];

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Документы графика медосмотров";
        ViewData["Subtitle"] = Organization;

        var rows = _store.LoadByOrganization(Organization);
        if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = rows.Where(r => r.Department.Contains(Department.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(ExamType))
        {
            rows = rows.Where(r => r.ExamType.Contains(ExamType.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows.Where(r =>
                r.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.MedicalInstitution.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.ExamType.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Rows = rows;
        if (string.IsNullOrWhiteSpace(PrintId) && rows.Count > 0)
        {
            PrintId = rows[0].Id;
        }
    }
}
