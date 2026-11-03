using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class WorkplacesModel : PageModel
{
    private readonly RiskWorkplaceStore _store;
    private readonly RiskPreparationStore _preparation;
    private readonly OrganizationStore _orgs;
    private readonly SoutConditionsClassStore _classes;
    private readonly OrganizationEmployeesService _employees;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public WorkplacesModel(
        RiskWorkplaceStore store,
        RiskPreparationStore preparation,
        OrganizationStore orgs,
        SoutConditionsClassStore classes,
        OrganizationEmployeesService employees,
        RiskAssessmentObjectStore objects,
        RisksWorkspaceContext risksWorkspace)
    {
        _store = store;
        _preparation = preparation;
        _orgs = orgs;
        _classes = classes;
        _employees = employees;
        _objects = objects;
        _risksWorkspace = risksWorkspace;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> ConditionsClassOptions { get; private set; } = [];
    public List<WorkplaceRecord> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<WorkplaceRecord>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public List<WorkplaceRecord> DepartmentGroups { get; private set; } = [];
    public string? Message { get; private set; }
    public WorkplaceRecord? SelectedPrintWorkplace =>
        string.IsNullOrWhiteSpace(PrintId) ? null : Nodes.FirstOrDefault(n => n.Id == PrintId);
    public IReadOnlyList<RiskAssessmentObject> AssessmentObjects { get; private set; } = [];
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public RiskAssessmentObject? SelectedObject =>
        AssessmentObjects.FirstOrDefault(o =>
            string.Equals(o.Id, SelectedObjectId, StringComparison.OrdinalIgnoreCase));
    public string SelectedObjectDisplayName =>
        _risksWorkspace.DisplayName(Organization, SelectedObjectId);
    public bool CanManageSelectedObject =>
        RisksWorkspaceContext.ShouldFilterMembership(SelectedObjectId);

    [BindProperty(SupportsGet = true)]
    public bool UsedOnly { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public bool IsAssessmentView =>
        string.Equals(View, "assessment", StringComparison.OrdinalIgnoreCase);

    public void OnGet()
    {
        Organization = _orgs.ResolveCanonicalOrganization(_orgs.GetSelectedOrganization(HttpContext))
            ?? _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _store, _preparation);
        ConditionsClassOptions = _classes.Load();
        LoadPage();
        if (string.IsNullOrWhiteSpace(Message) && TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
        }
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ConditionsClassOptions = _classes.Load();
        var existingById = _store.LoadByOrganization(Organization)
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var workplaceNumbers = Request.Form["WorkplaceNumber"];
        var mainNumbers = Request.Form["MainWorkplaceNumber"];
        var conditionsClasses = Request.Form["ConditionsClass"];
        var professions = Request.Form["Profession"];
        var workerCounts = Request.Form["WorkerCount"];
        var planYears = Request.Form["PlanYear"];
        var departments = Request.Form["Department"];
        var isUseds = Request.Form["IsUsed"];
        var deletes = Request.Form["Delete"];
        var trafficMarks = Request.Form["RiskTrafficMark"];
        var riskCardNumbers = Request.Form["RiskCardNumber"];
        var savingAssessmentView = string.Equals(
            Request.Form["View"].ToString(),
            "assessment",
            StringComparison.OrdinalIgnoreCase);
        if (savingAssessmentView)
        {
            View = "assessment";
            var formObjectId = Request.Form["ObjectId"].ToString();
            ObjectId = string.IsNullOrWhiteSpace(formObjectId)
                ? RiskAssessmentObjectIds.OrganizationWide
                : formObjectId.Trim();
        }

        if (savingAssessmentView)
        {
            ObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
        }

        var saveObjectId = RisksWorkspaceContext.ProjectionObjectId(ObjectId);
        var saveToObjectBucket = savingAssessmentView
            && RisksWorkspaceContext.ShouldFilterMembership(ObjectId);

        RiskAssessmentObject? membershipObject = null;
        var membershipDirty = false;
        if (saveToObjectBucket)
        {
            membershipObject = _objects.Find(Organization, saveObjectId!);
            if (membershipObject is not null)
            {
                var wasEmpty = (membershipObject.WorkplaceIds?.Count ?? 0) == 0;
                RiskObjectMembership.EnsureMembershipInitialized(
                    membershipObject,
                    existingById.Values);
                if (wasEmpty && (membershipObject.WorkplaceIds?.Count ?? 0) > 0)
                {
                    membershipDirty = true;
                }
            }
        }

        // В режиме оценки объекты независимы: в форме только видимые карты,
        // остальные РМ организации сохраняем без изменений.
        var finalById = savingAssessmentView
            ? existingById.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WorkplaceRecord>(StringComparer.OrdinalIgnoreCase);
        var hardDeleteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < names.Count; i++)
        {
            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i])
                ? ids[i].ToString()!
                : Guid.NewGuid().ToString("N");
            existingById.TryGetValue(id, out var existing);
            var isNew = existing is null;

            if (i < deletes.Count && deletes[i].ToString() == "true")
            {
                if (!savingAssessmentView)
                {
                    hardDeleteIds.Add(id);
                    continue;
                }

                if (saveToObjectBucket && membershipObject is not null)
                {
                    if (existing is not null && RiskObjectMembership.IsOwnedByObject(existing, saveObjectId))
                    {
                        hardDeleteIds.Add(id);
                    }
                    else
                    {
                        RiskObjectMembership.RemoveWorkplace(membershipObject, id);
                        membershipDirty = true;
                    }
                }
                else if (existing is not null && RiskObjectMembership.IsOwnedByOrganization(existing))
                {
                    existing.HiddenOnOrganizationWide = true;
                    finalById[id] = existing;
                }
                else if (existing is not null)
                {
                    hardDeleteIds.Add(id);
                }

                continue;
            }

            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isGroup = i < isGroups.Count && isGroups[i].ToString() == "true";
            _ = int.TryParse(i < workerCounts.Count ? workerCounts[i].ToString() : "0", out var workerCount);
            var department = i < departments.Count ? departments[i].ToString()?.Trim() ?? "" : "";
            var isUsed = i < isUseds.Count && isUseds[i].ToString() == "true";
            var profession = i < professions.Count ? professions[i].ToString()?.Trim() ?? "" : "";
            var parentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null);

            // Новая строка с уже существующей профессией в той же папке — не дублируем карту.
            if (isNew && !isGroup)
            {
                var profKey = ProfessionMatcher.Normalize(
                    string.IsNullOrWhiteSpace(profession) ? name : profession);
                if (!string.IsNullOrWhiteSpace(profKey))
                {
                    var duplicate = finalById.Values.FirstOrDefault(w =>
                        !w.IsGroup
                        && !hardDeleteIds.Contains(w.Id)
                        && string.Equals(
                            TrainingFormHelpers.NormalizeParentId(w.ParentId) ?? "",
                            parentId ?? "",
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            ProfessionMatcher.Normalize(
                                string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                            profKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (duplicate is not null)
                    {
                        if (workerCount > duplicate.WorkerCount)
                        {
                            duplicate.WorkerCount = workerCount;
                        }

                        if (saveToObjectBucket && membershipObject is not null)
                        {
                            RiskObjectMembership.AddWorkplace(membershipObject, duplicate.Id);
                            membershipDirty = true;
                        }

                        continue;
                    }
                }
            }

            var objectAssessments = CloneObjectAssessments(existing?.ObjectAssessments);
            var rootTrafficMark = RiskAssessmentStatusBuilder.NormalizeTrafficMark(existing?.RiskTrafficMark);
            var rootCardNumber = existing?.RiskCardNumber ?? "";
            var formCardNumber = savingAssessmentView && i < riskCardNumbers.Count
                ? riskCardNumbers[i].ToString()?.Trim() ?? ""
                : rootCardNumber;

            if (savingAssessmentView && i < trafficMarks.Count)
            {
                var formMark = RiskAssessmentStatusBuilder.NormalizeTrafficMark(trafficMarks[i].ToString());
                if (saveToObjectBucket && !isGroup)
                {
                    var objectData = existing is null
                        ? new WorkplaceRiskAssessmentData()
                        : RiskObjectAssessmentScope.GetObjectData(existing, saveObjectId!);
                    objectData.RiskTrafficMark = formMark;
                    objectData.RiskCardNumber = formCardNumber;
                    objectAssessments[saveObjectId!.Trim()] = objectData;
                }
                else
                {
                    rootTrafficMark = formMark;
                    rootCardNumber = formCardNumber;
                }
            }
            else if (savingAssessmentView && !isGroup)
            {
                if (saveToObjectBucket)
                {
                    var objectData = existing is null
                        ? new WorkplaceRiskAssessmentData()
                        : RiskObjectAssessmentScope.GetObjectData(existing, saveObjectId!);
                    objectData.RiskCardNumber = formCardNumber;
                    objectAssessments[saveObjectId!.Trim()] = objectData;
                }
                else
                {
                    rootCardNumber = formCardNumber;
                }
            }

            var ownerObjectId = existing?.OwnerObjectId ?? string.Empty;
            var hiddenOnOrg = existing?.HiddenOnOrganizationWide ?? false;
            if (isNew)
            {
                if (saveToObjectBucket)
                {
                    ownerObjectId = saveObjectId!;
                    hiddenOnOrg = false;
                    if (!isGroup && membershipObject is not null)
                    {
                        RiskObjectMembership.AddWorkplace(membershipObject, id);
                        membershipDirty = true;
                    }
                }
                else
                {
                    ownerObjectId = string.Empty;
                    hiddenOnOrg = false;
                }
            }
            else if (saveToObjectBucket && !isGroup && membershipObject is not null
                     && RiskObjectMembership.IsOwnedByOrganization(existing!))
            {
                // Карта с вкладки объекта остаётся в составе объекта.
                RiskObjectMembership.AddWorkplace(membershipObject, id);
                membershipDirty = true;
            }

            finalById[id] = new WorkplaceRecord
            {
                Id = id,
                ParentId = parentId,
                IsGroup = isGroup,
                Name = name,
                WorkplaceNumber = i < workplaceNumbers.Count ? workplaceNumbers[i].ToString()?.Trim() ?? "" : "",
                MainWorkplaceNumber = i < mainNumbers.Count ? mainNumbers[i].ToString()?.Trim() ?? "" : "",
                ConditionsClass = i < conditionsClasses.Count ? conditionsClasses[i].ToString()?.Trim() ?? "" : "",
                Profession = profession,
                WorkerCount = workerCount < 0 ? 0 : workerCount,
                PlanYear = i < planYears.Count ? planYears[i].ToString()?.Trim() ?? "" : "",
                OkCode = existing?.OkCode ?? "",
                Department = string.IsNullOrWhiteSpace(department) && isGroup ? name : department,
                IsUsed = isUsed,
                OwnerObjectId = ownerObjectId,
                HiddenOnOrganizationWide = hiddenOnOrg,
                RiskAssessmentDate = existing?.RiskAssessmentDate,
                RiskCardNumber = rootCardNumber,
                OverallRisk = existing?.OverallRisk ?? "",
                RiskPeriodicity = existing?.RiskPeriodicity ?? "",
                RiskAssessmentNotConducted = existing?.RiskAssessmentNotConducted ?? false,
                RiskLines = existing?.RiskLines ?? [],
                SoutAssessmentDate = existing?.SoutAssessmentDate,
                SoutReportNumber = existing?.SoutReportNumber ?? "",
                LinkedSoutCardWorkplaceId = existing?.LinkedSoutCardWorkplaceId ?? "",
                SoutCard = existing?.SoutCard ?? new SoutCardDetails(),
                DeclarationOfConformity = existing?.DeclarationOfConformity ?? false,
                SoutAssessmentNotConducted = existing?.SoutAssessmentNotConducted ?? false,
                RequiresUnscheduledSout = existing?.RequiresUnscheduledSout ?? false,
                SoutTrafficMark = existing?.SoutTrafficMark ?? "",
                RiskTrafficMark = rootTrafficMark,
                RiskFamiliarizations = existing?.RiskFamiliarizations ?? [],
                SoutFamiliarizations = existing?.SoutFamiliarizations ?? [],
                PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ObjectAssessments = objectAssessments,
                CreatedAt = existing?.CreatedAt ?? default
            };
        }

        foreach (var deleteId in hardDeleteIds)
        {
            finalById.Remove(deleteId);
        }

        // Не-assessment и assessment: итоговый список уже в finalById.
        _store.SaveForOrganization(Organization, finalById.Values);

        if (membershipDirty && membershipObject is not null)
        {
            _objects.Upsert(membershipObject);
        }

        Message = "Рабочие места сохранены.";
        LoadPage();
        return Page();
    }

    private static Dictionary<string, WorkplaceRiskAssessmentData> CloneObjectAssessments(
        Dictionary<string, WorkplaceRiskAssessmentData>? source)
    {
        var result = new Dictionary<string, WorkplaceRiskAssessmentData>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            result[key] = RiskObjectAssessmentScope.SnapshotFromWorkplace(new WorkplaceRecord
            {
                RiskAssessmentDate = value.RiskAssessmentDate,
                RiskCardNumber = value.RiskCardNumber,
                OverallRisk = value.OverallRisk,
                RiskPeriodicity = value.RiskPeriodicity,
                RiskAssessmentNotConducted = value.RiskAssessmentNotConducted,
                RiskLines = value.RiskLines,
                RiskTrafficMark = value.RiskTrafficMark,
                RiskFamiliarizations = value.RiskFamiliarizations
            });
        }

        return result;
    }

    public IActionResult OnPostDeleteObject(string? objectId)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        View = "assessment";
        if (string.IsNullOrWhiteSpace(objectId)
            || RiskObjectAssessmentScope.IsOrganizationWide(objectId))
        {
            TempData["Message"] = "Вкладку «Оценка рисков по организации» удалить нельзя.";
            return RedirectToPage(new { view = "assessment", objectId = RiskAssessmentObjectIds.OrganizationWide });
        }

        if (_objects.Delete(Organization, objectId))
        {
            // Данные по объекту на РМ оставляем (на случай восстановления) — только убираем вкладку.
            TempData["Message"] = "Объект удалён из списка.";
        }

        _risksWorkspace.ResetToOrganizationWide(HttpContext);
        return RedirectToPage(new { view = "assessment" });
    }

    public IActionResult OnPostRenameObject(string? objectId, string? objectName, string? officialName)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        View = "assessment";
        var id = (objectId ?? string.Empty).Trim();
        var name = (objectName ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            TempData["Message"] = "Выберите объект.";
            return RedirectToPage(new { view = "assessment" });
        }

        if (name.Length == 0)
        {
            TempData["Message"] = "Укажите новое название объекта.";
            return RedirectToPage(new { view = "assessment" });
        }

        if (_objects.Rename(Organization, id, name, officialName))
        {
            TempData["Message"] = $"Название изменено на «{name}».";
        }
        else
        {
            TempData["Message"] = "Не удалось переименовать: объект не найден или такое имя уже есть.";
        }

        _risksWorkspace.SetSelectedObjectId(HttpContext, Organization, id);
        return RedirectToPage(new { view = "assessment" });
    }

    public IActionResult OnPostSyncFromStaff()
    {
        // Кнопка больше не используется: перечень подтягивается автоматически при открытии.
        // Оставляем handler безопасным (только добавление недостающих), без Replace.
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _store, _preparation);
        SyncFromStaffSafe();
        ConditionsClassOptions = _classes.Load();
        LoadPage();
        return Page();
    }

    private static DateTime _lastStaffSyncUtc = DateTime.MinValue;
    private static DateTime _lastWorkerCountSyncUtc = DateTime.MinValue;
    private static readonly object StaffSyncGate = new();

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsAssessmentView
            ? "Карта оценки рисков"
            : "Рабочие места";
        ViewData["Subtitle"] = Organization;

        var leaves = _store.LoadByOrganization(Organization).Count(w => !w.IsGroup);
        var collapsed = SoutWorkplaceStaffSync.CollapseEmptyProfessionDuplicates(
            Organization, _store, out var redirectedIds);
        if (redirectedIds.Count > 0)
        {
            RemapObjectMembershipIds(Organization, redirectedIds);
        }

        var needSync = leaves == 0 || collapsed > 0;
        lock (StaffSyncGate)
        {
            var now = DateTime.UtcNow;
            if (needSync || now - _lastStaffSyncUtc >= TimeSpan.FromMinutes(30))
            {
                SyncFromStaffSafe(updateWorkerCounts: needSync || now - _lastWorkerCountSyncUtc >= TimeSpan.FromHours(6));
                _lastStaffSyncUtc = now;
                if (needSync || now - _lastWorkerCountSyncUtc >= TimeSpan.FromHours(6))
                {
                    _lastWorkerCountSyncUtc = now;
                }
            }
        }

        Nodes = _store.LoadTreeByOrganization(Organization).ToList();
        Nodes = Nodes
            .Where(n => n.IsGroup || !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(n))
            .ToList();

        if (IsAssessmentView)
        {
            AssessmentObjects = _objects.Load(Organization).ToList();
            SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
            ObjectId = SelectedObjectId;
            var selectedObject = SelectedObject;
            Nodes = RisksWorkspaceContext.ApplyWorkplaceScope(
                Nodes,
                SelectedObjectId,
                selectedObject,
                obj =>
                {
                    _objects.Upsert(obj);
                    AssessmentObjects = _objects.Load(Organization).ToList();
                });
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes)
            {
                if (MatchesQuery(node, query))
                {
                    matchedIds.Add(node.Id);
                    AddAncestors(node, matchedIds);
                }
            }

            Nodes = Nodes.Where(node => matchedIds.Contains(node.Id)).ToList();
        }

        if (UsedOnly)
        {
            Nodes = Nodes.Where(node => node.IsGroup || node.IsUsed).ToList();
        }

        // DepartmentId только подсвечивает/прокручивает в UI — перечень подразделений не сворачиваем.
        if (string.IsNullOrWhiteSpace(PrintId))
        {
            PrintId = Nodes.FirstOrDefault(n => !n.IsGroup)?.Id;
        }

        DepartmentGroups = SoutWorkplaceDepartmentConsolidator.DistinctRootDepartmentGroups(Nodes);
    }

    /// <summary>
    /// Только работающие; только недостающие профессии. Никогда не затирает уже введённые карты.
    /// </summary>
    private void SyncFromStaffSafe(bool updateWorkerCounts = true)
    {
        var org = _orgs.ResolveCanonicalOrganization(Organization) ?? Organization;
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        Organization = org;
        var staff = _employees.LoadForReport(org);
        var added = SoutWorkplaceStaffSync.SyncMissingFromStaff(org, staff, _store);
        var updated = 0;
        if (updateWorkerCounts)
        {
            updated = SoutWorkplaceStaffSync.SyncWorkerCountsFromStaff(org, staff, _store);
        }

        if ((added > 0 || updated > 0) && string.IsNullOrWhiteSpace(Message))
        {
            Message = added > 0
                ? $"Из штата 1С добавлено рабочих мест: {added}."
                : "Количество работников обновлено из штата 1С.";
        }
    }

    /// <summary>После схлопывания дублей профессий — id в составе объектов указывают на оставшуюся карту.</summary>
    private void RemapObjectMembershipIds(string organization, IReadOnlyDictionary<string, string> redirectedIds)
    {
        if (redirectedIds.Count == 0)
        {
            return;
        }

        foreach (var obj in _objects.Load(organization))
        {
            if (obj.IsOrganizationWide || obj.WorkplaceIds is null || obj.WorkplaceIds.Count == 0)
            {
                continue;
            }

            var changed = false;
            var next = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in obj.WorkplaceIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    changed = true;
                    continue;
                }

                var mapped = redirectedIds.TryGetValue(id.Trim(), out var keep) ? keep : id.Trim();
                if (!string.Equals(mapped, id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                }

                if (!seen.Add(mapped))
                {
                    changed = true;
                    continue;
                }

                next.Add(mapped);
            }

            if (changed)
            {
                obj.WorkplaceIds = next;
                _objects.Upsert(obj);
            }
        }
    }

    private static bool MatchesQuery(WorkplaceRecord node, string query) =>
        node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.WorkplaceNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.Department.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void AddAncestors(WorkplaceRecord node, HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = Nodes.FirstOrDefault(item => item.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, matchedIds);
    }
}
