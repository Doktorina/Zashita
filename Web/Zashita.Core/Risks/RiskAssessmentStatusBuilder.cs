namespace Zashita.Core.Risks;

/// <summary>
/// Состояние по оценке рисков на основе справочника рабочих мест (не демо).
/// </summary>
public static class RiskAssessmentStatusBuilder
{
    public const int ValidityYears = 5;

    public static List<RiskAssessmentStatusRow> Build(
        string organization,
        string? department = null,
        string? assessmentObject = null) =>
        Build([], organization, department, assessmentObject, DateTime.Today);

    public static List<RiskAssessmentStatusRow> Build(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        string? department = null,
        string? assessmentObject = null,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var source = workplaces
            .Where(w => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(w.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (source.Count == 0)
        {
            return [];
        }

        var rows = source
            .Select(w => ToRow(w, today))
            .ToList();

        if (!string.IsNullOrWhiteSpace(department))
        {
            rows = FilterByDepartment(rows, department.Trim());
        }

        if (!string.IsNullOrWhiteSpace(assessmentObject))
        {
            var query = assessmentObject.Trim();
            rows = rows
                .Where(row => row.ObjectName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              row.ObjectCode.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // Keep ancestors of matched leaves for tree display
            var keep = new HashSet<string>(rows.Select(r => r.Id), StringComparer.Ordinal);
            foreach (var row in rows.Where(r => !r.IsGroup).ToList())
            {
                AddAncestors(source.Select(w => ToRow(w, today)).ToList(), row, keep);
            }

            rows = source.Select(w => ToRow(w, today)).Where(r => keep.Contains(r.Id)).ToList();
        }

        return rows
            .OrderBy(r => r.IsGroup ? 0 : 1)
            .ThenBy(r => r.ObjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool HasValidRiskAssessment(WorkplaceRecord workplace, DateTime today)
    {
        if (workplace.IsGroup || !workplace.IsUsed)
        {
            return false;
        }

        if (workplace.RiskAssessmentNotConducted)
        {
            return false;
        }

        // «Проведена» (зелёный) — только если на карте заполнены риски, не дата/класс сами по себе.
        if (!HasFilledRiskLines(workplace))
        {
            return false;
        }

        if (workplace.RiskAssessmentDate is { } date)
        {
            return date.Date.AddYears(ValidityYears) >= today.Date;
        }

        return true;
    }

    public static bool IsWithoutRiskAssessment(WorkplaceRecord workplace, DateTime today) =>
        !workplace.IsGroup &&
        workplace.IsUsed &&
        !HasValidRiskAssessment(workplace, today);

    /// <summary>Есть хотя бы одна строка карты с заполненным риском (не прочерки).</summary>
    public static bool HasFilledRiskLines(WorkplaceRecord workplace)
    {
        var lines = workplace.RiskLines ?? [];
        if (lines.Count == 0)
        {
            return false;
        }

        return lines.Any(line =>
            !IsDash(line.HazardTitle) ||
            !IsDash(line.HazardNumber) ||
            !IsDash(line.ControlMeasures) ||
            !IsDash(line.Severity) ||
            !IsDash(line.Probability));
    }

    private static bool IsDash(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(t) || t is "-" or "—" or "–";
    }

    public static bool HasValidSout(WorkplaceRecord workplace, DateTime today)
    {
        if (workplace.IsGroup || !workplace.IsUsed)
        {
            return false;
        }

        var classCode = SoutStatusBuilder.ParseClassCode(workplace.ConditionsClass);
        if ((classCode is 1 or 2) && workplace.DeclarationOfConformity && !workplace.RequiresUnscheduledSout)
        {
            return true;
        }

        if (workplace.SoutAssessmentDate is { } date)
        {
            return date.Date.AddYears(ValidityYears) >= today.Date && !workplace.RequiresUnscheduledSout;
        }

        return false;
    }

    private static RiskAssessmentStatusRow ToRow(WorkplaceRecord workplace, DateTime today)
    {
        var hasAssessment = !workplace.IsGroup && HasValidRiskAssessment(workplace, today);
        var quantity = workplace.IsGroup ? 0 : Math.Max(1, workplace.WorkerCount > 0 ? 1 : 1);
        return new RiskAssessmentStatusRow
        {
            Id = workplace.Id,
            ParentId = workplace.ParentId,
            IsGroup = workplace.IsGroup,
            ObjectName = workplace.Name,
            ObjectCode = workplace.WorkplaceNumber,
            AssessmentResult = workplace.IsGroup
                ? string.Empty
                : hasAssessment
                    ? "Оценка проведена"
                    : workplace.RiskAssessmentNotConducted
                        ? "Не проведена оценка рисков"
                        : "Не проходило оценку рисков",
            CreatedDate = workplace.CreatedAt == default ? null : workplace.CreatedAt.Date,
            ConductedDate = workplace.RiskAssessmentNotConducted ? null : workplace.RiskAssessmentDate?.Date,
            Value = workplace.IsGroup ? string.Empty : (hasAssessment ? "Учтено" : string.Empty),
            Significance = workplace.IsGroup ? string.Empty : workplace.ConditionsClass,
            Conclusion = string.Empty,
            Comment = workplace.IsUsed
                ? (workplace.RiskAssessmentNotConducted ? "Не проведена оценка рисков" : string.Empty)
                : "Не используется",
            Quantity = workplace.IsGroup ? 0 : quantity,
            WithoutAssessment = workplace.IsGroup || hasAssessment ? 0 : quantity,
            WithAssessment = workplace.IsGroup || !hasAssessment ? 0 : quantity,
            MeasuresCurrent = 0,
            MeasuresFuture = 0,
            IsHighlighted = !workplace.IsGroup && workplace.IsUsed && !hasAssessment
        };
    }

    private static List<RiskAssessmentStatusRow> FilterByDepartment(List<RiskAssessmentStatusRow> rows, string department)
    {
        var matchedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.IsGroup && row.ObjectName.Contains(department, StringComparison.OrdinalIgnoreCase))
            {
                matchedIds.Add(row.Id);
                AddDescendants(rows, row.Id, matchedIds);
                AddAncestors(rows, row, matchedIds);
            }
            else if (!row.IsGroup && row.ObjectName.Contains(department, StringComparison.OrdinalIgnoreCase))
            {
                matchedIds.Add(row.Id);
                AddAncestors(rows, row, matchedIds);
            }
        }

        if (matchedIds.Count == 0)
        {
            return rows;
        }

        return rows.Where(row => matchedIds.Contains(row.Id)).ToList();
    }

    private static void AddDescendants(List<RiskAssessmentStatusRow> rows, string parentId, HashSet<string> matchedIds)
    {
        foreach (var child in rows.Where(row => row.ParentId == parentId))
        {
            if (matchedIds.Add(child.Id))
            {
                AddDescendants(rows, child.Id, matchedIds);
            }
        }
    }

    private static void AddAncestors(List<RiskAssessmentStatusRow> rows, RiskAssessmentStatusRow node, HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = rows.FirstOrDefault(row => row.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(rows, parent, matchedIds);
    }

    /// <summary>Нормализация ручной пометки: green / red / blue или пусто (авто).</summary>
    public static string NormalizeTrafficMark(string? mark)
    {
        var value = mark?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "green" or "red" or "blue" ? value : string.Empty;
    }

    /// <summary>
    /// Цвет строки на «Карта оценки рисков»: ручная пометка или авто по дате/флагам.
    /// «Истекает» — синий (в отчёте тот же срок даёт жёлтый).
    /// </summary>
    public static string ResolveCardsTraffic(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.IsGroup)
        {
            return string.Empty;
        }

        var mark = NormalizeTrafficMark(workplace.RiskTrafficMark);
        if (!string.IsNullOrEmpty(mark))
        {
            return mark;
        }

        return ResolveAutoCardsTraffic(workplace, asOf);
    }

    /// <summary>Авто-цвет без учёта ручной пометки (для подписи «Авто (…)»).</summary>
    public static string ResolveAutoCardsTraffic(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.IsGroup)
        {
            return string.Empty;
        }

        var today = (asOf ?? DateTime.Today).Date;
        var bucket = RiskWorkplaceNeedsBuilder.ClassifySummaryBucket(workplace, today);
        return bucket switch
        {
            RiskWorkplaceNeedsBuilder.NeedAssessed => "green",
            RiskWorkplaceNeedsBuilder.NeedExpiring => "blue",
            RiskWorkplaceNeedsBuilder.NeedOverdue => "red",
            RiskWorkplaceNeedsBuilder.NeedNotConducted => "red",
            RiskWorkplaceNeedsBuilder.NeedPlanned => "red",
            _ => ResolveFallbackTraffic(workplace, today)
        };
    }

    private static string ResolveFallbackTraffic(WorkplaceRecord workplace, DateTime today)
    {
        if (workplace.RiskAssessmentNotConducted || !HasFilledRiskLines(workplace))
        {
            return "red";
        }

        var expires = RiskWorkplaceNeedsBuilder.ResolveExpiryDate(workplace);
        if (expires is { } due)
        {
            if (due.Date < today)
            {
                return "red";
            }

            if (due.Date <= today.AddDays(30))
            {
                return "blue";
            }
        }

        return HasValidRiskAssessment(workplace, today) ? "green" : "red";
    }

    public static string CardsTrafficLabel(string? traffic) => traffic?.Trim().ToLowerInvariant() switch
    {
        "green" => "Проведена",
        "red" => "Не проведена / просрочена",
        "blue" => "Истекает",
        _ => "—"
    };
}
