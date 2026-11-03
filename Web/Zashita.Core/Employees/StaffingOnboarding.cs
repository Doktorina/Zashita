using Zashita.Core.Med;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Core.Employees;

/// <summary>
/// Приём/перевод остаётся на главной, пока не заполнены обучение, медосмотры, СОУТ и риски.
/// В разделах список дублируется и пропадает по мере заполнения конкретного модуля.
/// </summary>
public static class StaffingOnboarding
{
    public sealed record Gap(bool Training, bool Med, bool Sout, bool Risks)
    {
        public bool IsComplete => !Training && !Med && !Sout && !Risks;

        public bool NeedsModule(string? moduleKey)
        {
            var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "обучение" or "training" => Training,
                "медосмотры" or "med" => Med,
                "специальная-оценка" or "спецоценка" or "sout" => Sout,
                "риски" or "risks" => Risks,
                _ => !IsComplete
            };
        }

        public string Label
        {
            get
            {
                var parts = new List<string>();
                if (Training) parts.Add("обучение");
                if (Med) parts.Add("медосмотры");
                if (Sout) parts.Add("СОУТ");
                if (Risks) parts.Add("риски");
                return parts.Count == 0 ? "заполнено" : "не хватает: " + string.Join(", ", parts);
            }
        }

        public string ModuleGapLabel(string? moduleKey)
        {
            var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "обучение" or "training" => Training ? "нужно заполнить обучение" : "обучение заполнено",
                "медосмотры" or "med" => Med ? "нужно заполнить медосмотры" : "медосмотры заполнены",
                "специальная-оценка" or "спецоценка" or "sout" => Sout ? "нужно заполнить СОУТ" : "СОУТ заполнен",
                "риски" or "risks" => Risks ? "нужно заполнить риски" : "риски заполнены",
                _ => Label
            };
        }
    }

    public static Gap Evaluate(
        string organization,
        string person,
        string? profession,
        IReadOnlyList<TrainingConductRecord> conducts,
        IReadOnlyList<MedExamRecord> exams,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<WorkplaceRecord>? riskWorkplaces = null,
        string? snils = null)
    {
        var name = (person ?? string.Empty).Trim();
        var org = (organization ?? string.Empty).Trim();
        var prof = ProfessionMatcher.Normalize(profession);
        var snilsNorm = OneCEmployeeListParser.NormalizeSnils(snils ?? string.Empty);

        var hasTraining = conducts.Any(c =>
            !c.IsArchivedSnapshot
            && c.TrainingDate is { Year: >= 1900 }
            && NamesMatch(c.Person, name)
            && (string.IsNullOrWhiteSpace(org) || OrganizationMatcher.Matches(org, c.Organization)));

        var hasMed = exams.Any(e =>
            NamesMatch(e.EmployeeName, name) &&
            (string.IsNullOrWhiteSpace(org) || OrganizationMatcher.Matches(org, e.Organization)) &&
            (!string.IsNullOrWhiteSpace(e.Conclusion) || e.ExamDate.Year >= 1900));

        var relatedSout = workplaces
            .Where(w => !w.IsGroup)
            .Where(w => string.IsNullOrWhiteSpace(org) || OrganizationMatcher.Matches(org, w.Organization))
            .Where(w => WorkplaceMatchesPerson(w, name, snilsNorm, prof))
            .ToList();

        var relatedRisks = (riskWorkplaces ?? [])
            .Where(w => !w.IsGroup)
            .Where(w => string.IsNullOrWhiteSpace(org) || OrganizationMatcher.Matches(org, w.Organization))
            .Where(w => WorkplaceMatchesPerson(w, name, snilsNorm, prof))
            .ToList();

        // РМ нет или только заготовка из штата (класс «2» без даты/№) — раздел не заполнен.
        var hasSout = relatedSout.Any(HasRealSoutData);
        var hasRisks = relatedRisks.Any(HasRealRiskData);

        return new Gap(
            Training: !hasTraining,
            Med: !hasMed,
            Sout: !hasSout,
            Risks: !hasRisks);
    }

    private static bool WorkplaceMatchesPerson(
        WorkplaceRecord w,
        string personName,
        string snilsNorm,
        string professionNorm)
    {
        if (WorkplaceLinkedToPerson(w, personName, snilsNorm))
        {
            return true;
        }

        if (professionNorm.Length == 0)
        {
            return false;
        }

        var wpProf = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession);
        return ProfessionMatcher.Matches(wpProf, professionNorm)
               || ProfessionMatcher.Matches(professionNorm, wpProf);
    }

    private static bool WorkplaceLinkedToPerson(WorkplaceRecord w, string personName, string snilsNorm)
    {
        if (w.SoutCard?.ExtraLines is { Count: > 0 } lines)
        {
            foreach (var value in lines.Values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (snilsNorm.Length == 11
                    && value.Contains(snilsNorm, StringComparison.Ordinal))
                {
                    return true;
                }

                if (NamesMatch(value, personName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Реальные данные СОУТ, не дефолт класса «2» из синхронизации штата.</summary>
    public static bool HasRealSoutData(WorkplaceRecord w)
    {
        if (w.SoutAssessmentDate is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.SoutReportNumber))
        {
            return true;
        }

        var card = w.SoutCard;
        if (card is null)
        {
            return false;
        }

        if (card.PreparedDate is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(card.Equipment)
            || !string.IsNullOrWhiteSpace(card.Materials)
            || !string.IsNullOrWhiteSpace(card.AnalogousWorkplaces))
        {
            return true;
        }

        var final = (card.FinalClass ?? string.Empty).Trim();
        return final.Length > 0
               && !string.Equals(final, "2", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(final, "2.0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasRealRiskData(WorkplaceRecord w) =>
        w.RiskAssessmentDate is not null
        || !string.IsNullOrWhiteSpace(w.RiskCardNumber)
        || w.RiskLines is { Count: > 0 };

    /// <summary>Сотрудник есть в актуальном штате организации (как в отчёте по обучению).</summary>
    public static bool IsInActiveRoster(
        IReadOnlyList<OrganizationEmployee> roster,
        string person,
        string? snils = null)
    {
        var name = (person ?? string.Empty).Trim();
        if (name.Length == 0 || roster.Count == 0)
        {
            return false;
        }

        var snilsNorm = OneCEmployeeListParser.NormalizeSnils(snils ?? string.Empty);
        foreach (var employee in roster)
        {
            if (!EmployeeHrStatus.IsEmployed(employee))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snilsNorm)
                && !string.IsNullOrWhiteSpace(employee.Snils)
                && string.Equals(
                    OneCEmployeeListParser.NormalizeSnils(employee.Snils),
                    snilsNorm,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (EmployeeNameMatcher.Matches(employee.Name, name))
            {
                return true;
            }
        }

        return false;
    }

    public static DateTime? ResolveDisplayDate(
        StaffingChangeEvent alert,
        IReadOnlyList<OrganizationEmployee>? roster = null) =>
        ResolveEventDate(alert, roster);

    public static string FormatDisplayDate(DateTime? date) =>
        date is { Year: >= 1900 } d ? d.ToString("dd.MM.yyyy") : string.Empty;

    public static DateTime? ResolveEventDate(
        StaffingChangeEvent alert,
        IReadOnlyList<OrganizationEmployee>? roster = null)
    {
        var employee = FindInRoster(roster, alert.Person, alert.Snils);
        if (employee is not null)
        {
            if (alert.Kind == StaffingChangeKind.Transferred)
            {
                if (employee.TransferDate is { Year: >= 1900 } transferDate)
                {
                    return transferDate.Date;
                }
            }

            if (employee.EmploymentDate is { Year: >= 1900 } employmentDate)
            {
                return employmentDate.Date;
            }
        }

        // Дата события синхронизации — запасной вариант (локальный день).
        if (alert.OccurredAt != default)
        {
            return alert.OccurredAt.Kind == DateTimeKind.Utc
                ? alert.OccurredAt.ToLocalTime().Date
                : alert.OccurredAt.Date;
        }

        return null;
    }

    public static string FormatEventDateLabel(StaffingChangeKind kind, DateTime? date)
    {
        if (date is null || date.Value.Year < 1900)
        {
            return string.Empty;
        }

        var prefix = kind == StaffingChangeKind.Transferred ? "перевод" : "приём";
        return $"{prefix} от {date.Value:dd.MM.yyyy}";
    }

    private static OrganizationEmployee? FindInRoster(
        IReadOnlyList<OrganizationEmployee>? roster,
        string? person,
        string? snils)
    {
        if (roster is null || roster.Count == 0)
        {
            return null;
        }

        var name = (person ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var snilsNorm = OneCEmployeeListParser.NormalizeSnils(snils ?? string.Empty);
        OrganizationEmployee? fallback = null;
        foreach (var employee in roster)
        {
            if (!string.IsNullOrWhiteSpace(snilsNorm)
                && !string.IsNullOrWhiteSpace(employee.Snils)
                && string.Equals(
                    OneCEmployeeListParser.NormalizeSnils(employee.Snils),
                    snilsNorm,
                    StringComparison.OrdinalIgnoreCase))
            {
                return employee;
            }

            if (EmployeeNameMatcher.Matches(employee.Name, name))
            {
                fallback ??= employee;
            }
        }

        return fallback;
    }

    private static bool NamesMatch(string? a, string? b) =>
        EmployeeNameMatcher.Matches(a, b);
}
