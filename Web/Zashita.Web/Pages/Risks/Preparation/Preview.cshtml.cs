using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Preparation;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly RiskPreparationStore _store;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationPrintDetailsStore _orgDetails;
    private readonly OrganizationRequisitesStore _requisites;

    public PreviewModel(
        RiskPreparationStore store,
        RiskWorkplaceStore workplaces,
        OrganizationEmployeesService employees,
        OrganizationPrintDetailsStore orgDetails,
        OrganizationRequisitesStore requisites)
    {
        _store = store;
        _workplaces = workplaces;
        _employees = employees;
        _orgDetails = orgDetails;
        _requisites = requisites;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = RiskPreparationPrintModes.Schedule;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public RiskPreparationDocument? Document { get; private set; }
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id, string? mode)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = RiskPreparationPrintModes.Normalize(mode);
        Document = _store.GetById(id);
        if (Document is null)
        {
            NotFound = true;
            return Page();
        }

        Title = $"{Document.Number} от {Document.Date:dd.MM.yyyy HH:mm:ss}";
        PreviewHtml = BuildPreview(Document, Mode);
        return Page();
    }

    public IActionResult OnPostSave(string id, string? mode)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = RiskPreparationPrintModes.Normalize(mode);
        Document = _store.GetById(id);
        if (Document is null)
        {
            NotFound = true;
            return Page();
        }

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
            Document.PrintOverrides[key] = value;
            if (string.Equals(key, "order-bank", StringComparison.OrdinalIgnoreCase))
            {
                _orgDetails.SaveBankLetterhead(Document.Organization, value);
            }
        }

        _store.Upsert(Document);
        Message = "Изменения в форме сохранены.";
        Title = $"{Document.Number} от {Document.Date:dd.MM.yyyy HH:mm:ss}";
        PreviewHtml = BuildPreview(Document, Mode);
        return Page();
    }

    private string BuildPreview(RiskPreparationDocument document, string mode)
    {
        SanitizeChairmanOverride(document);
        var bank = ResolveBankLetterhead(document);
        return RiskPreparationPrintBuilder.Build(
            document,
            mode,
            _workplaces.LoadAll(),
            _employees.Load(document.Organization),
            bank);
    }

    private string? ResolveBankLetterhead(RiskPreparationDocument document)
    {
        var org = document.Organization;
        if (document.PrintOverrides is not null
            && document.PrintOverrides.TryGetValue("order-bank", out var own)
            && RiskPrintChrome.IsFilledBankLetterhead(own))
        {
            _orgDetails.SaveBankLetterhead(org, own);
            return own.Trim();
        }

        var known = _requisites.ResolveBankLetterhead(org)
                    ?? _orgDetails.GetBankLetterhead(org);
        if (RiskPrintChrome.IsFilledBankLetterhead(known))
        {
            return known;
        }

        foreach (var row in _store.LoadByOrganization(org))
        {
            if (string.Equals(row.Id, document.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row.PrintOverrides is not null
                && row.PrintOverrides.TryGetValue("order-bank", out var bank)
                && RiskPrintChrome.IsFilledBankLetterhead(bank))
            {
                _orgDetails.SaveBankLetterhead(org, bank);
                return bank.Trim();
            }
        }

        return known;
    }

    private static void SanitizeChairmanOverride(RiskPreparationDocument document)
    {
        if (document.PrintOverrides is null)
        {
            return;
        }

        foreach (var key in new[] { "order-chairman", "order-sign-fio" })
        {
            if (!document.PrintOverrides.TryGetValue(key, out var value))
            {
                continue;
            }

            if (!RiskPrintChrome.IsUsablePersonChairman(value, document.Organization))
            {
                document.PrintOverrides.Remove(key);
            }
        }
    }
}
