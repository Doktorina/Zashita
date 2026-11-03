using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Состав карт оценки рисков по объектам: объекты не связаны между собой
/// (создание/удаление карты на одном объекте не меняет другой).
/// </summary>
public static class RiskObjectMembership
{
    public static bool IsOwnedByOrganization(WorkplaceRecord workplace) =>
        string.IsNullOrWhiteSpace(workplace.OwnerObjectId)
        || RiskObjectAssessmentScope.IsOrganizationWide(workplace.OwnerObjectId);

    public static bool IsOwnedByObject(WorkplaceRecord workplace, string? objectId) =>
        !string.IsNullOrWhiteSpace(objectId)
        && !RiskObjectAssessmentScope.IsOrganizationWide(objectId)
        && string.Equals(workplace.OwnerObjectId?.Trim(), objectId.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsVisibleOnOrganizationWide(WorkplaceRecord workplace)
    {
        if (workplace.IsGroup)
        {
            return IsOwnedByOrganization(workplace);
        }

        return IsOwnedByOrganization(workplace) && !workplace.HiddenOnOrganizationWide;
    }

    public static bool IsLeafVisibleOnObject(
        WorkplaceRecord workplace,
        string? objectId,
        IReadOnlySet<string>? membershipIds)
    {
        if (workplace.IsGroup)
        {
            return false;
        }

        if (RiskObjectAssessmentScope.IsOrganizationWide(objectId))
        {
            return IsVisibleOnOrganizationWide(workplace);
        }

        if (IsOwnedByObject(workplace, objectId))
        {
            return true;
        }

        if (!IsOwnedByOrganization(workplace))
        {
            return false;
        }

        return membershipIds is not null
               && membershipIds.Contains(workplace.Id);
    }

    /// <summary>
    /// Снимок id карт, видимых на вкладке «по организации» (для нового объекта).
    /// Одна профессия в папке — один id (дубли не копируем в новый объект).
    /// </summary>
    public static List<string> SnapshotOrganizationWideWorkplaceIds(
        IEnumerable<WorkplaceRecord> workplaces)
    {
        var leaves = workplaces
            .Where(w => !w.IsGroup && IsVisibleOnOrganizationWide(w))
            .ToList();

        var chosen = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in leaves
                     .OrderByDescending(ProfessionSnapshotKeepScore)
                     .ThenBy(w => w.CreatedAt == default ? DateTime.MaxValue : w.CreatedAt))
        {
            var prof = ProfessionMatcher.Normalize(
                string.IsNullOrWhiteSpace(leaf.Profession) ? leaf.Name : leaf.Profession);
            if (string.IsNullOrWhiteSpace(prof))
            {
                continue;
            }

            var folder = !string.IsNullOrWhiteSpace(leaf.ParentId)
                ? "p:" + leaf.ParentId.Trim()
                : "d:" + (string.IsNullOrWhiteSpace(leaf.Department) ? "Без подразделения" : leaf.Department.Trim());
            var key = folder + "\u001f" + prof;
            if (!seen.Add(key))
            {
                continue;
            }

            chosen.Add(leaf.Id);
        }

        return chosen;
    }

    private static int ProfessionSnapshotKeepScore(WorkplaceRecord w)
    {
        var score = 0;
        if (w.RiskAssessmentDate is not null)
        {
            score += 50;
        }

        if (!string.IsNullOrWhiteSpace(w.RiskCardNumber))
        {
            score += 40;
        }

        score += (w.RiskLines?.Count ?? 0) * 10;
        score += Math.Min(Math.Max(w.WorkerCount, 0), 100);
        return score;
    }

    public static HashSet<string> MembershipSet(RiskAssessmentObject? assessmentObject)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (assessmentObject is null || assessmentObject.IsOrganizationWide)
        {
            return set;
        }

        foreach (var id in assessmentObject.WorkplaceIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                set.Add(id.Trim());
            }
        }

        return set;
    }

    /// <summary>
    /// Оставляет карты и группы, видимые на выбранном объекте
    /// (группы — если есть видимые потомки или группа принадлежит объекту).
    /// </summary>
    public static List<WorkplaceRecord> FilterForObject(
        IEnumerable<WorkplaceRecord> nodes,
        string? objectId,
        RiskAssessmentObject? assessmentObject)
    {
        var list = nodes.ToList();
        if (list.Count == 0)
        {
            return list;
        }

        var membership = RiskObjectAssessmentScope.IsOrganizationWide(objectId)
            ? null
            : MembershipSet(assessmentObject);

        var visibleLeafIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in list.Where(n => !n.IsGroup))
        {
            if (IsLeafVisibleOnObject(node, objectId, membership))
            {
                visibleLeafIds.Add(node.Id);
            }
        }

        var byId = list.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var keep = new HashSet<string>(visibleLeafIds, StringComparer.OrdinalIgnoreCase);

        foreach (var leafId in visibleLeafIds)
        {
            if (!byId.TryGetValue(leafId, out var node))
            {
                continue;
            }

            var parentId = node.ParentId;
            while (!string.IsNullOrWhiteSpace(parentId)
                   && byId.TryGetValue(parentId, out var parent))
            {
                if (!keep.Add(parent.Id))
                {
                    break;
                }

                parentId = parent.ParentId;
            }
        }

        // Пустые группы «по организации» / свои группы объекта — тоже показываем.
        foreach (var group in list.Where(n => n.IsGroup))
        {
            if (RiskObjectAssessmentScope.IsOrganizationWide(objectId))
            {
                if (IsOwnedByOrganization(group))
                {
                    keep.Add(group.Id);
                }
            }
            else if (IsOwnedByObject(group, objectId) || IsOwnedByOrganization(group))
            {
                // Орг-группы оставляем как каркас; лишние без потомков уберём ниже по keep листьев —
                // если уже добавлены через предков видимых листьев. Свои группы объекта — всегда.
                if (IsOwnedByObject(group, objectId))
                {
                    keep.Add(group.Id);
                }
            }
        }

        return list.Where(n => keep.Contains(n.Id)).ToList();
    }

    public static void EnsureMembershipInitialized(
        RiskAssessmentObject assessmentObject,
        IEnumerable<WorkplaceRecord> workplaces)
    {
        if (assessmentObject.IsOrganizationWide)
        {
            return;
        }

        assessmentObject.WorkplaceIds ??= [];
        if (assessmentObject.WorkplaceIds.Count > 0)
        {
            return;
        }

        // Старые объекты без состава: один раз заполняем снимком «по организации».
        assessmentObject.WorkplaceIds = SnapshotOrganizationWideWorkplaceIds(workplaces);
    }

    public static void AddWorkplace(RiskAssessmentObject assessmentObject, string workplaceId)
    {
        if (assessmentObject.IsOrganizationWide || string.IsNullOrWhiteSpace(workplaceId))
        {
            return;
        }

        assessmentObject.WorkplaceIds ??= [];
        if (!assessmentObject.WorkplaceIds.Any(id =>
                string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase)))
        {
            assessmentObject.WorkplaceIds.Add(workplaceId.Trim());
        }
    }

    public static void RemoveWorkplace(RiskAssessmentObject assessmentObject, string workplaceId)
    {
        if (assessmentObject.IsOrganizationWide || string.IsNullOrWhiteSpace(workplaceId))
        {
            return;
        }

        assessmentObject.WorkplaceIds ??= [];
        assessmentObject.WorkplaceIds.RemoveAll(id =>
            string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase));
    }
}
