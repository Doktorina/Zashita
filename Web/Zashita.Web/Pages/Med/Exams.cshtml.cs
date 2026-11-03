using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ExamsModel : PageModel
{
    private readonly MedExamStore _store;
    private readonly MedFactorStore _factors;
    private readonly MedContextService _med;

    public ExamsModel(MedExamStore store, MedFactorStore factors, MedContextService med)
    {
        _store = store;
        _factors = factors;
        _med = med;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedExamRecord> Rows { get; private set; } = [];
    public IReadOnlyList<FactorItemOption> FactorItems { get; private set; } = [];
    public IReadOnlyList<string> PeriodicityOptions { get; } = MedPeriodicity.Options;
    public DateTime? ComputedNextExamDate { get; private set; }
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty]
    public string EmployeeName { get; set; } = string.Empty;

    [BindProperty]
    public DateTime? BirthDate { get; set; }

    [BindProperty]
    public string Profession { get; set; } = string.Empty;

    [BindProperty]
    public string ItemNumber { get; set; } = string.Empty;

    [BindProperty]
    public string Periodicity { get; set; } = MedPeriodicity.OnceAYear;

    [BindProperty]
    public string ExamType { get; set; } = "Периодический";

    [BindProperty]
    public DateTime ExamDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string Status { get; set; } = "Запланирован";

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;

        FactorItems = _factors.LoadAll()
            .Where(f => !f.IsGroup && f.IsActive && !string.IsNullOrWhiteSpace(f.ItemNumber))
            .OrderBy(f => f.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FactorItemOption(
                f.ItemNumber,
                MedPeriodicity.Normalize(f.Periodicity) is var p && MedPeriodicity.Options.Contains(p)
                    ? p
                    : MedPeriodicity.OnceAYear,
                $"{f.ItemNumber} — {f.Name}"))
            .ToList();

        Rows = _med.GetExams(Organization)
            .Where(r => string.Equals(Type, "shift", StringComparison.OrdinalIgnoreCase)
                ? r.ExamType.Contains("смен", StringComparison.OrdinalIgnoreCase)
                : !r.ExamType.Contains("смен", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.ExamDate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = Rows.FirstOrDefault(r => r.Id == EditId) ?? _store.LoadAll().FirstOrDefault(r => r.Id == EditId);
            if (row is not null)
            {
                EmployeeName = row.EmployeeName;
                BirthDate = row.BirthDate;
                Profession = row.Profession;
                ItemNumber = row.ItemNumber;
                Periodicity = MedPeriodicity.Normalize(row.Periodicity);
                ExamType = row.ExamType;
                ExamDate = row.ExamDate;
                Status = row.Status;
            }
        }
        else if (string.Equals(Type, "shift", StringComparison.OrdinalIgnoreCase))
        {
            ExamType = "Предсменный";
        }
        else
        {
            ExamType = "Периодический";
        }

        Periodicity = MedPeriodicity.Normalize(Periodicity);
        ComputedNextExamDate = string.Equals(Status, "Пройден", StringComparison.OrdinalIgnoreCase)
            ? MedPeriodicity.NextExamDate(ExamDate, Periodicity)
            : ExamDate.Date;
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(EmployeeName))
        {
            Message = "Укажите ФИО.";
            OnGet();
            return Page();
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId!;
        var record = new MedExamRecord
        {
            Id = id,
            Organization = Organization,
            EmployeeName = EmployeeName.Trim(),
            Profession = Profession.Trim(),
            ExamType = string.IsNullOrWhiteSpace(ExamType) ? "Периодический" : ExamType.Trim(),
            ExamDate = ExamDate.Date,
            Status = string.IsNullOrWhiteSpace(Status) ? "Запланирован" : Status.Trim(),
            ItemNumber = ItemNumber?.Trim() ?? string.Empty,
            Periodicity = MedPeriodicity.Normalize(Periodicity),
            BirthDate = BirthDate
        };
        _store.Upsert(record);

        return RedirectToPage(new { type = Type, editId = id });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new { type = Type });
    }

    public sealed record FactorItemOption(string ItemNumber, string Periodicity, string Display);
}
