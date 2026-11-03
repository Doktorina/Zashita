using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Restrictions;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly MedRestrictionStore _store;
    private readonly MedContextService _med;

    public PreviewModel(MedRestrictionStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Id { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id) => LoadPage(id);

    public IActionResult OnPostSave(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        var document = _store.GetById(id);
        if (document is null)
        {
            NotFound = true;
            return Page();
        }

        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        }

        _store.Upsert(document);
        Message = "Изменения в форме сохранены.";
        return LoadPage(id);
    }

    private IActionResult LoadPage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        var organization = _med.GetOrganization(HttpContext);
        var document = _store.GetById(id);
        if (document is null ||
            !string.Equals(document.Organization, organization, StringComparison.OrdinalIgnoreCase))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        if (string.IsNullOrWhiteSpace(document.Profession) && !string.IsNullOrWhiteSpace(document.EmployeeName))
        {
            var employee = _med.GetEmployees(organization)
                .FirstOrDefault(e => string.Equals(e.Name?.Trim(), document.EmployeeName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(employee?.Profession))
            {
                document.Profession = employee.Profession.Trim();
            }
        }

        if (document.SuspensionFrom is null)
        {
            document.SuspensionFrom = document.Date.Date;
        }

        Title = $"Приказ об отстранении от работы — {document.Number}";
        PreviewHtml = MedRestrictionPrintBuilder.Build(document);
        return Page();
    }
}
