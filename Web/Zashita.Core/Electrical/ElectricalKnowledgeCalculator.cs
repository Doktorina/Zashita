namespace Zashita.Core.Electrical;

public static class ElectricalKnowledgeCalculator
{
    /// <summary>
    /// Мин. срок в группе до следующей (мес.): I→II / II→III / III→IV по образованию.
    /// Для групп IV и V — null («-»).
    /// </summary>
    public static int? ResolveMinMonthsInGroup(string? education, string? currentGroup)
    {
        var group = NormalizeGroup(currentGroup);
        if (ElectricalGroups.IsPeriodicCheckGroup(group) || string.IsNullOrWhiteSpace(group))
        {
            return null;
        }

        var edu = NormalizeEducation(education);
        var (toIi, toIii, toIv) = LookupMinMonths(edu);
        return group switch
        {
            ElectricalGroups.I => toIi,
            ElectricalGroups.II => toIii,
            ElectricalGroups.III => toIv,
            _ => null
        };
    }

    public static ElectricalKnowledgeCheckComputed Compute(
        ElectricalKnowledgeCheckRecord record,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var group = NormalizeGroup(record.CurrentGroup);
        var minMonths = ResolveMinMonthsInGroup(record.Education, group);
        var minDisplay = minMonths.HasValue ? minMonths.Value.ToString() : "—";

        var periodicity = record.PeriodicityMonths is 12 or 36
            ? record.PeriodicityMonths
            : null;
        var periodicityDisplay = periodicity.HasValue ? periodicity.Value.ToString() : "—";
        var usePeriodicCheck = periodicity.HasValue
                               || ElectricalGroups.IsPeriodicCheckGroup(group);

        string nextActionType;
        DateTime? planned = null;
        if (periodicity.HasValue)
        {
            nextActionType = ElectricalNextActionTypes.PeriodicCheck;
            if (record.CurrentGroupAssignedDate.HasValue)
            {
                planned = record.CurrentGroupAssignedDate.Value.Date.AddMonths(periodicity.Value);
            }
        }
        else if (ElectricalGroups.IsPeriodicCheckGroup(group))
        {
            nextActionType = ElectricalNextActionTypes.PeriodicCheck;
        }
        else if (group is ElectricalGroups.I or ElectricalGroups.II or ElectricalGroups.III)
        {
            nextActionType = ElectricalNextActionTypes.PlannedGroupUpgrade;
            if (record.CurrentGroupAssignedDate.HasValue && minMonths.HasValue)
            {
                planned = record.CurrentGroupAssignedDate.Value.Date.AddMonths(minMonths.Value);
            }
        }
        else
        {
            nextActionType = string.Empty;
        }

        int? daysLeft = planned.HasValue
            ? (int)(planned.Value.Date - today).TotalDays
            : null;

        var (status, css) = ResolveStatus(group, daysLeft, usePeriodicCheck);

        return new ElectricalKnowledgeCheckComputed(
            minMonths,
            minDisplay,
            periodicity,
            periodicityDisplay,
            planned,
            nextActionType,
            daysLeft,
            status,
            css);
    }

    public static string FormatTrainingProgram(string? currentGroup)
    {
        var g = NormalizeGroup(currentGroup);
        return string.IsNullOrWhiteSpace(g)
            ? TrainingFilterProgramName
            : $"{TrainingFilterProgramName} {g}";
    }

    /// <summary>Пункт фильтра в планировании / календаре потребностей.</summary>
    public const string TrainingFilterProgramName = "Группа по электробезопасности";

    public static bool IsTrainingFilterProgram(string? programOrFilter)
    {
        var p = (programOrFilter ?? string.Empty).Trim();
        if (p.Length == 0)
        {
            return false;
        }

        return p.Equals(TrainingFilterProgramName, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(TrainingFilterProgramName + " ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesTrainingKindFilter(string? program, string? selectedKind)
    {
        var prog = (program ?? string.Empty).Trim();
        var kind = (selectedKind ?? string.Empty).Trim();
        if (prog.Length == 0 || kind.Length == 0)
        {
            return false;
        }

        if (prog.Equals(kind, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Фильтр «Группа по электробезопасности» покрывает все группы I–V из реестра ЭБ.
        return kind.Equals(TrainingFilterProgramName, StringComparison.OrdinalIgnoreCase)
               && IsTrainingFilterProgram(prog);
    }

    /// <summary>Текст периодичности для отчёта по обучению.</summary>
    public static string FormatTrainingPeriodicity(ElectricalKnowledgeCheckComputed computed, string? currentGroup)
    {
        if (computed.PeriodicityMonths.HasValue)
        {
            return $"1 раз в {computed.PeriodicityMonths.Value} мес.";
        }

        if (computed.MinMonthsInGroup.HasValue)
        {
            return $"Через {computed.MinMonthsInGroup.Value} мес.";
        }

        return "—";
    }

    private static (string Status, string Css) ResolveStatus(
        string group,
        int? daysLeft,
        bool usePeriodicCheck = false)
    {
        if (!daysLeft.HasValue)
        {
            return (string.Empty, "");
        }

        if (usePeriodicCheck || ElectricalGroups.IsPeriodicCheckGroup(group))
        {
            return daysLeft.Value >= 0
                ? (ElectricalStatuses.Normal, "eb-status-ok")
                : (ElectricalStatuses.Overdue, "eb-status-overdue");
        }

        if (group is ElectricalGroups.I or ElectricalGroups.II or ElectricalGroups.III)
        {
            if (daysLeft.Value <= 0)
            {
                return (ElectricalStatuses.ExperienceReady, "eb-status-ready");
            }

            return (ElectricalStatuses.ExperienceGaining, "eb-status-gaining");
        }

        return (string.Empty, "");
    }

    /// <summary>Мин. месяцы: I→II, II→III, III→IV. Для I→II берём тот же ориентир, что II→III.</summary>
    private static (int ToIi, int ToIii, int ToIv) LookupMinMonths(string education)
    {
        if (string.Equals(education, ElectricalEducationLevels.BasicGeneral, StringComparison.OrdinalIgnoreCase))
        {
            return (3, 3, 6);
        }

        if (string.Equals(education, ElectricalEducationLevels.SecondaryGeneral, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(education, ElectricalEducationLevels.SecondaryVocationalOrHigherTechnical, StringComparison.OrdinalIgnoreCase))
        {
            return (2, 2, 3);
        }

        if (string.Equals(education, ElectricalEducationLevels.HigherElectricPower, StringComparison.OrdinalIgnoreCase))
        {
            return (1, 1, 2);
        }

        // Неизвестное образование — консервативно как основное общее.
        return (3, 3, 6);
    }

    public static string NormalizeGroup(string? group)
    {
        var raw = (group ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        // Убрать невидимые/юникод-римские символы до сравнения.
        var g = raw.ToUpperInvariant()
            .Replace("ГРУППА", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\u2160", "I")   // Ⅰ
            .Replace("\u2161", "II")  // Ⅱ
            .Replace("\u2162", "III") // Ⅲ
            .Replace("\u2163", "IV")  // Ⅳ
            .Replace("\u2164", "V")   // Ⅴ
            .Replace("\u0412", "V");  // кириллическая «В»

        // Сначала более длинные римские числа (IV до V, III до I).
        if (g is "4" or "IV" or "ІV")
        {
            return ElectricalGroups.IV;
        }

        if (g is "3" or "III" or "ІІІ")
        {
            return ElectricalGroups.III;
        }

        if (g is "2" or "II" or "ІІ")
        {
            return ElectricalGroups.II;
        }

        if (g is "1" or "I" or "І")
        {
            return ElectricalGroups.I;
        }

        if (g is "5" or "V" or "У")
        {
            return ElectricalGroups.V;
        }

        return raw;
    }

    public static string NormalizeEducation(string? education)
    {
        var e = (education ?? string.Empty).Trim();
        foreach (var known in ElectricalEducationLevels.All)
        {
            if (string.Equals(known, e, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return e;
    }
}

public static class ElectricalCommissionCalculator
{
    public static ElectricalCommissionComputed Compute(ElectricalCommissionRecord record, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        DateTime? planned = null;
        if (record.CompositionApprovalDate.HasValue &&
            record.CompositionCheckPeriodMonths is > 0)
        {
            planned = record.CompositionApprovalDate.Value.Date
                .AddMonths(record.CompositionCheckPeriodMonths.Value);
        }

        int? daysLeft = planned.HasValue
            ? (int)(planned.Value.Date - today).TotalDays
            : null;

        string status;
        string css;
        if (!daysLeft.HasValue)
        {
            status = "—";
            css = "";
        }
        else if (daysLeft.Value < 0)
        {
            status = "Просрочено";
            css = "eb-status-overdue";
        }
        else if (daysLeft.Value <= 30)
        {
            status = "Скоро проверка";
            css = "eb-status-ready";
        }
        else
        {
            status = ElectricalStatuses.Normal;
            css = "eb-status-ok";
        }

        return new ElectricalCommissionComputed(planned, daysLeft, status, css);
    }

    public static string SuggestCompliance(ElectricalCommissionRecord record)
    {
        var members = (record.Members ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .ToList();
        var total = members.Count;
        if (total <= 0)
        {
            return ElectricalCommissionCompliance.NonCompliant;
        }

        // Соответствует только если все члены комиссии аттестованы в Ростехнадзоре.
        var certified = record.CertifiedInRostechnadzorCount
            ?? members.Count(m => m.CertifiedInRostechnadzor);
        return certified >= total
            ? ElectricalCommissionCompliance.Compliant
            : ElectricalCommissionCompliance.NonCompliant;
    }
}
