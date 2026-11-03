using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.UnscheduledOrders;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly RiskUnscheduledOrderStore _store;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationPrintDetailsStore _orgDetails;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly SoutCommissionStore _commissions;

    public PreviewModel(
        RiskUnscheduledOrderStore store,
        OrganizationEmployeesService employees,
        OrganizationPrintDetailsStore orgDetails,
        OrganizationRequisitesStore requisites,
        SoutCommissionStore commissions)
    {
        _store = store;
        _employees = employees;
        _orgDetails = orgDetails;
        _requisites = requisites;
        _commissions = commissions;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = RiskUnscheduledOrderPrintModes.Order;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
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
        Mode = RiskUnscheduledOrderPrintModes.Normalize(mode);
        var document = _store.GetById(id);
        if (document is null)
        {
            NotFound = true;
            return Page();
        }

        Title = $"{document.Number} от {document.Date:dd.MM.yyyy HH:mm:ss}";
        PreviewHtml = BuildHtml(document);
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
        Mode = RiskUnscheduledOrderPrintModes.Normalize(mode);
        var document = _store.GetById(id);
        if (document is null)
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
            document.PrintOverrides[key] = value;
            if (string.Equals(key, "order-bank", StringComparison.OrdinalIgnoreCase))
            {
                _orgDetails.SaveBankLetterhead(document.Organization, value);
            }
        }

        _store.Upsert(document);
        Message = "Изменения в форме сохранены.";
        Title = $"{document.Number} от {document.Date:dd.MM.yyyy HH:mm:ss}";
        PreviewHtml = BuildHtml(document);
        return Page();
    }

    private string BuildHtml(RiskUnscheduledOrderDocument document)
    {
        SanitizeChairmanOverride(document);
        var staff = _employees.Load(document.Organization);
        var bank = ResolveBankLetterhead(document);
        var allCommissions = _commissions.Load(document.Organization);
        var commission = RiskCommissionResolver.Resolve(
            document.Organization,
            allCommissions,
            document.CommissionId);
        SyncOrderRequisitesFromCommission(document, commission);
        return RiskUnscheduledOrderPrintBuilder.Build(document, Mode, staff, bank, commission);
    }

    /// <summary>
    /// Номер и дата приказа — из выбранной комиссии («№ приказа о создании комиссии»).
    /// Старый автозаголовок «ПРИКАЗ № …» в PrintOverrides сбрасывается, чтобы не перекрывал номер.
    /// </summary>
    private void SyncOrderRequisitesFromCommission(
        RiskUnscheduledOrderDocument document,
        SoutCommissionRecord? commission)
    {
        if (commission is null)
        {
            return;
        }

        var overrides = document.PrintOverrides;
        var dateLocked = overrides is not null
                         && overrides.TryGetValue("order-date", out var dateOv)
                         && !string.IsNullOrWhiteSpace(dateOv);

        var changed = false;
        var fromCommission = (commission.OrderNumber ?? string.Empty).Trim();
        if (fromCommission.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            fromCommission = fromCommission[1..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(fromCommission))
        {
            if (!string.Equals(document.Number?.Trim(), fromCommission, StringComparison.OrdinalIgnoreCase))
            {
                document.Number = fromCommission;
                changed = true;
            }

            // Сбрасываем сохранённый заголовок — иначе остаётся старый «ПРИКАЗ № OT-…».
            if (overrides is not null
                && overrides.ContainsKey("order-title"))
            {
                overrides.Remove("order-title");
                changed = true;
            }
        }

        if (!dateLocked
            && commission.OrderDate is { Year: >= 2000 } d
            && document.Date.Date != d.Date)
        {
            document.Date = d.Date;
            changed = true;
        }

        if (changed)
        {
            _store.Upsert(document);
        }
    }

    private string? ResolveBankLetterhead(RiskUnscheduledOrderDocument document)
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

        return FindBankInOrders(org, document.Id) ?? known;
    }

    private string? FindBankInOrders(string organization, string excludeId)
    {
        foreach (var row in _store.LoadByOrganization(organization))
        {
            if (string.Equals(row.Id, excludeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row.PrintOverrides is not null
                && row.PrintOverrides.TryGetValue("order-bank", out var bank)
                && RiskPrintChrome.IsFilledBankLetterhead(bank))
            {
                _orgDetails.SaveBankLetterhead(organization, bank);
                return bank.Trim();
            }
        }

        return null;
    }

    private static void SanitizeChairmanOverride(RiskUnscheduledOrderDocument document)
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
