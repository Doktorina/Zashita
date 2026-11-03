using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Workplaces;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly RiskWorkplaceStore _store;
    private readonly OrganizationStore _orgs;
    private readonly RiskAssessmentObjectStore _objects;

    public PreviewModel(
        RiskWorkplaceStore store,
        OrganizationStore orgs,
        RiskAssessmentObjectStore objects)
    {
        _store = store;
        _orgs = orgs;
        _objects = objects;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = RiskWorkplacePrintModes.Identification;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public IActionResult OnGet(string id, string? mode) => LoadPage(id, mode);

    public IActionResult OnPostSave(string id, string? mode)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        var workplace = _store.GetById(id);
        if (workplace is null || workplace.IsGroup)
        {
            NotFound = true;
            return Page();
        }

        workplace.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            workplace.PrintOverrides[key] = value;
        }

        _store.Upsert(workplace);
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

        var organization = _orgs.GetSelectedOrganization(HttpContext);
        var workplace = _store.GetById(id);
        if (workplace is null || workplace.IsGroup ||
            !string.Equals(workplace.Organization, organization, StringComparison.OrdinalIgnoreCase))
        {
            NotFound = true;
            return Page();
        }

        var objects = _objects.Load(organization);
        SelectedObjectId = !string.IsNullOrWhiteSpace(ObjectId)
            && objects.Any(o => string.Equals(o.Id, ObjectId, StringComparison.OrdinalIgnoreCase))
            ? ObjectId!
            : RiskAssessmentObjectIds.OrganizationWide;
        ObjectId = SelectedObjectId;

        var projected = RiskObjectAssessmentScope.ProjectForObject(workplace, SelectedObjectId);
        Id = id;
        Mode = RiskWorkplacePrintModes.Normalize(mode);
        Title = $"{RiskWorkplacePrintModes.Title(Mode)} — {projected.Name}";
        PreviewHtml = RiskWorkplacePrintBuilder.Build(projected, organization, Mode);
        return Page();
    }
}
