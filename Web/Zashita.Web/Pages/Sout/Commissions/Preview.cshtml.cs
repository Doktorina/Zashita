using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Commissions;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly SoutCommissionStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationPrintDetailsStore _printDetails;
    private readonly OrganizationRequisitesStore _requisites;

    public PreviewModel(
        SoutCommissionStore store,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        OrganizationPrintDetailsStore printDetails,
        OrganizationRequisitesStore requisites)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
        _printDetails = printDetails;
        _requisites = requisites;
    }

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string Title { get; private set; } = "Приказ о создании комиссии СОУТ";
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;

        // Без id — сначала выбор комиссии (как в рисках), а не автопечать первой попавшейся.
        if (string.IsNullOrWhiteSpace(Id))
        {
            return RedirectToPage("/Sout/Commissions/SelectCommission");
        }

        var commission = ResolveCommission();
        if (commission is null)
        {
            NotFound = true;
            return Page();
        }

        Title = string.IsNullOrWhiteSpace(commission.Name)
            ? "Приказ о создании комиссии СОУТ"
            : $"Приказ — {commission.Name}";
        PreviewHtml = BuildHtml(commission);
        return Page();
    }

    public IActionResult OnPostSave(string? id)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        Id = id;

        var commission = ResolveCommission();
        if (commission is null)
        {
            NotFound = true;
            return Page();
        }

        commission.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
            commission.PrintOverrides[key] = value;
            if (string.Equals(key, "order-bank", StringComparison.OrdinalIgnoreCase))
            {
                _printDetails.SaveBankLetterhead(Organization, value);
            }
        }

        commission.ModifiedAt = DateTime.UtcNow;
        _store.Upsert(Organization, commission);
        Message = "Реквизиты и правки формы сохранены. Они подставятся в другие приказы этой организации.";
        Title = string.IsNullOrWhiteSpace(commission.Name)
            ? "Приказ о создании комиссии СОУТ"
            : $"Приказ — {commission.Name}";
        PreviewHtml = BuildHtml(commission);
        return Page();
    }

    private SoutCommissionRecord? ResolveCommission()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return null;
        }

        var commission = _store.Find(Organization, Id!);
        if (commission is null || commission.IsGroup)
        {
            return null;
        }

        return commission;
    }

    private string BuildHtml(SoutCommissionRecord commission)
    {
        var bank = ResolveBankLetterhead(commission);
        return SoutCommissionOrderPrintBuilder.Build(
            commission,
            Organization,
            _employees.Load(Organization),
            bank);
    }

    private string? ResolveBankLetterhead(SoutCommissionRecord commission)
    {
        if (commission.PrintOverrides is not null
            && commission.PrintOverrides.TryGetValue("order-bank", out var own)
            && RiskPrintChrome.IsFilledBankLetterhead(own))
        {
            _printDetails.SaveBankLetterhead(Organization, own);
            return own.Trim();
        }

        return _requisites.ResolveBankLetterhead(Organization)
               ?? _printDetails.GetBankLetterhead(Organization);
    }
}
