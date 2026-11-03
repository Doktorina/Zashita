using Zashita.Core.Risks;

namespace Zashita.Web.Services;

/// <summary>
/// Выбранный объект оценки рисков в сессии (для всего модуля «Риски»).
/// </summary>
public sealed class RisksWorkspaceContext
{
    public const string AllObjectsId = "all";
    public const string AllObjectsName = "Все объекты";
    private const string SessionObjectKey = "Workspace.Risks.ObjectId";

    private readonly RiskAssessmentObjectStore _objects;

    public RisksWorkspaceContext(RiskAssessmentObjectStore objects) => _objects = objects;

    public static bool IsAllObjects(string? objectId) =>
        string.Equals(objectId, AllObjectsId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Id для Project/Save assessment: «все» ведёт себя как оценка по организации.</summary>
    public static string ProjectionObjectId(string? objectId) =>
        IsAllObjects(objectId) || string.IsNullOrWhiteSpace(objectId)
            ? RiskAssessmentObjectIds.OrganizationWide
            : objectId.Trim();

    public static bool ShouldFilterMembership(string? objectId) =>
        !IsAllObjects(objectId) && !RiskObjectAssessmentScope.IsOrganizationWide(objectId);

    public string GetSelectedObjectId(HttpContext http, string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return RiskAssessmentObjectIds.OrganizationWide;
        }

        var raw = http.Session.GetString(SessionObjectKey);
        return Validate(org, raw);
    }

    public void SetSelectedObjectId(HttpContext http, string? organization, string? objectId)
    {
        var org = (organization ?? string.Empty).Trim();
        var validated = Validate(org, objectId);
        http.Session.SetString(SessionObjectKey, validated);
    }

    public void ResetToOrganizationWide(HttpContext http) =>
        http.Session.SetString(SessionObjectKey, RiskAssessmentObjectIds.OrganizationWide);

    /// <summary>
    /// Query/form objectId побеждает и записывается в сессию.
    /// </summary>
    public string ResolveObjectId(HttpContext http, string? organization, string? queryObjectId)
    {
        var org = (organization ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(queryObjectId))
        {
            var fromQuery = Validate(org, queryObjectId);
            http.Session.SetString(SessionObjectKey, fromQuery);
            return fromQuery;
        }

        return GetSelectedObjectId(http, org);
    }

    public string DisplayName(string? organization, string? objectId)
    {
        if (IsAllObjects(objectId))
        {
            return AllObjectsName;
        }

        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return RiskAssessmentObjectIds.OrganizationWideName;
        }

        var id = RiskObjectAssessmentScope.IsOrganizationWide(objectId) || string.IsNullOrWhiteSpace(objectId)
            ? RiskAssessmentObjectIds.OrganizationWide
            : objectId.Trim();

        var name = _objects.Find(org, id)?.Name?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? RiskAssessmentObjectIds.OrganizationWideName
            : name;
    }

    public IReadOnlyList<RiskAssessmentObject> LoadObjects(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        return org.Length == 0 ? [] : _objects.Load(org);
    }

    /// <summary>
    /// Фильтрация состава РМ и проекция полей оценки для выбранного объекта.
    /// </summary>
    public static List<WorkplaceRecord> ApplyWorkplaceScope(
        List<WorkplaceRecord> workplaces,
        string? objectId,
        RiskAssessmentObject? assessmentObject,
        Action<RiskAssessmentObject>? persistMembership = null)
    {
        if (ShouldFilterMembership(objectId))
        {
            if (assessmentObject is not null
                && !assessmentObject.IsOrganizationWide
                && (assessmentObject.WorkplaceIds?.Count ?? 0) == 0)
            {
                RiskObjectMembership.EnsureMembershipInitialized(assessmentObject, workplaces);
                if ((assessmentObject.WorkplaceIds?.Count ?? 0) > 0)
                {
                    persistMembership?.Invoke(assessmentObject);
                }
            }

            workplaces = RiskObjectMembership.FilterForObject(workplaces, objectId, assessmentObject);
        }

        return RiskObjectAssessmentScope
            .ProjectListForObject(workplaces, ProjectionObjectId(objectId))
            .ToList();
    }

    private string Validate(string organization, string? objectId)
    {
        var id = (objectId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return RiskAssessmentObjectIds.OrganizationWide;
        }

        if (IsAllObjects(id))
        {
            return AllObjectsId;
        }

        if (RiskObjectAssessmentScope.IsOrganizationWide(id))
        {
            return RiskAssessmentObjectIds.OrganizationWide;
        }

        if (organization.Length == 0)
        {
            return RiskAssessmentObjectIds.OrganizationWide;
        }

        return _objects.Find(organization, id) is not null
            ? id
            : RiskAssessmentObjectIds.OrganizationWide;
    }
}
