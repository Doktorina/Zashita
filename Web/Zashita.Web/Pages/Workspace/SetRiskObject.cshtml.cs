using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Workspace;

[RequireLogin]
[IgnoreAntiforgeryToken]
public class SetRiskObjectModel : PageModel
{
    private readonly OrganizationStore _organizations;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly RisksWorkspaceContext _risksWorkspace;
    private readonly RiskWorkplaceStore _workplaces;

    public SetRiskObjectModel(
        OrganizationStore organizations,
        RiskAssessmentObjectStore objects,
        RisksWorkspaceContext risksWorkspace,
        RiskWorkplaceStore workplaces)
    {
        _organizations = organizations;
        _objects = objects;
        _risksWorkspace = risksWorkspace;
        _workplaces = workplaces;
    }

    public IActionResult OnPost(
        string? objectId,
        string? newObjectName,
        string? newObjectOfficialName,
        string? renameObjectName,
        string? renameObjectOfficialName,
        string? deleteObject,
        string? returnUrl)
    {
        var org = _organizations.GetSelectedOrganization(HttpContext);
        if (_organizations.IsAllOrganizationsSelected(HttpContext) || string.IsNullOrWhiteSpace(org))
        {
            return RedirectBack(returnUrl);
        }

        if (IsTruthy(deleteObject))
        {
            var id = (objectId ?? string.Empty).Trim();
            if (RisksWorkspaceContext.IsAllObjects(id)
                || RiskObjectAssessmentScope.IsOrganizationWide(id)
                || string.IsNullOrWhiteSpace(id))
            {
                TempData["Message"] = "Системный объект «Оценка рисков по организации» удалить нельзя.";
                return RedirectBack(returnUrl);
            }

            if (_objects.Delete(org, id))
            {
                _risksWorkspace.ResetToOrganizationWide(HttpContext);
                TempData["Message"] = "Объект удалён из списка.";
            }
            else
            {
                TempData["Message"] = "Не удалось удалить объект.";
            }

            return RedirectBack(returnUrl);
        }

        var rename = (renameObjectName ?? string.Empty).Trim();
        if (rename.Length > 0)
        {
            var id = (objectId ?? string.Empty).Trim();
            if (RisksWorkspaceContext.IsAllObjects(id) || string.IsNullOrWhiteSpace(id))
            {
                TempData["Message"] = "Выберите объект для переименования.";
                return RedirectBack(returnUrl);
            }

            // «org-wide» тоже можно переименовать (название вкладки / официальное).
            var renameId = RiskObjectAssessmentScope.IsOrganizationWide(id)
                ? RiskAssessmentObjectIds.OrganizationWide
                : id;

            if (_objects.Rename(org, renameId, rename, renameObjectOfficialName))
            {
                _risksWorkspace.SetSelectedObjectId(HttpContext, org, renameId);
                TempData["Message"] = $"Объект переименован в «{rename}».";
            }
            else
            {
                TempData["Message"] = "Не удалось переименовать: объект не найден или такое имя уже есть.";
            }

            return RedirectBack(returnUrl);
        }

        var name = (newObjectName ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            var orgWorkplaceIds = RiskObjectMembership.SnapshotOrganizationWideWorkplaceIds(
                _workplaces.LoadByOrganization(org));
            var created = _objects.Create(org, name, orgWorkplaceIds, newObjectOfficialName);
            _risksWorkspace.SetSelectedObjectId(HttpContext, org, created.Id);
            return RedirectBack(returnUrl);
        }

        _risksWorkspace.SetSelectedObjectId(HttpContext, org, objectId);
        return RedirectBack(returnUrl);
    }

    private IActionResult RedirectBack(string? returnUrl)
    {
        var target = WorkspaceReturnUrl.Sanitize(returnUrl);
        if (Url.IsLocalUrl(target))
        {
            return LocalRedirect(target);
        }

        return Redirect("/module/риски");
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}
