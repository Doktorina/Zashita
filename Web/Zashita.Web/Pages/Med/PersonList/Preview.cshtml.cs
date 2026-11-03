using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.PersonList;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly MedPersonListStore _store;
    private readonly MedExamStore _exams;
    private readonly MedFactorStore _factors;
    private readonly OrganizationEmployeesService _employees;

    public PreviewModel(
        MedPersonListStore store,
        MedExamStore exams,
        MedFactorStore factors,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _exams = exams;
        _factors = factors;
        _employees = employees;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = MedPersonListPrintModes.Standard;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id, string? mode) => LoadPage(id, mode);

    public IActionResult OnPostSave(string id, string? mode)
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
        return LoadPage(id, mode);
    }

    private IActionResult LoadPage(string id, string? mode)
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

        var org = document.Organization;
        MedPersonListStore.EnrichLinesFromSources(
            document,
            string.IsNullOrWhiteSpace(org) ? _exams.LoadAll() : _exams.LoadByOrganization(org),
            string.IsNullOrWhiteSpace(org) ? null : _employees.LoadForMedReport(org));

        var factorsByItem = _factors.LoadAll()
            .Where(f => !f.IsGroup && !string.IsNullOrWhiteSpace(f.ItemNumber))
            .GroupBy(f => MedItemNumber.Normalize(f.ItemNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Id = id;
        Mode = MedPersonListPrintModes.Normalize(mode);
        Title = $"{MedPersonListPrintModes.Title(Mode)} — {document.Number}";
        PreviewHtml = MedPersonListPrintBuilder.Build(document, Mode, factorsByItem);
        return Page();
    }
}
