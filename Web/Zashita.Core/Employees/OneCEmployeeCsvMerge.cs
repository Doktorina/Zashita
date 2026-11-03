namespace Zashita.Core.Employees;

/// <summary>
/// Слияние строк из нескольких CSV 1С (Employees_zup + Employees_zup_anzhela).
/// Алгоритм: по [человек + ЮЛ] — одна актуальная по датам запись (основное место,
/// иначе свежайший приём); если актуальных нет — последняя уволенная в архив.
/// </summary>
public static class OneCEmployeeCsvMerge
{
    public static Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> ConsolidateOrganizations(
        Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> byOrg)
    {
        if (byOrg.Count == 0)
        {
            return byOrg;
        }

        var canonicalOrgs = OrganizationMatcher.DeduplicateOrganizations(byOrg.Keys);
        var result = new Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (org, rows) in byOrg)
        {
            var canonical = canonicalOrgs.FirstOrDefault(c => OrganizationMatcher.Matches(c, org)) ?? org;
            if (!result.TryGetValue(canonical, out var list))
            {
                list = [];
                result[canonical] = list;
            }

            list.AddRange(rows);
        }

        return result;
    }

    public static Dictionary<string, (OneCEmployeeRow Row, bool IsActive)> ResolveRows(
        string organization,
        IEnumerable<(OneCEmployeeRow Row, bool IsActive)> rows)
    {
        var latest = SelectLatestRows(organization, rows);
        return latest.ToDictionary(
            x => OneCStaffEligibility.PersonStableKey(x.Row) + "|" + OrganizationMatcher.Normalize(organization),
            x => x,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Какая строка «свежее» для одного человека в одном ЮЛ
    /// (ДатаПриема → ДатаПеревода → пустая ДатаУвольнения выше).
    /// </summary>
    public static bool PreferIncomingEmployee(
        OneCEmployeeRow incoming,
        bool incomingActive,
        OneCEmployeeRow existing,
        bool existingActive)
    {
        if (incomingActive != existingActive)
        {
            return incomingActive;
        }

        return OneCStaffEligibility.CompareFreshness(incoming, existing) >= 0;
    }

    /// <summary>
    /// По каждому ЮЛ карточка на [человек + должность + ЮЛ]:
    /// совместительство с разными профессиями (слесарь + стропальщик) — две карточки.
    /// 1) если есть актуальные по датам на эту должность — взять одну (основное / max приём);
    /// 2) иначе — последняя уволенная (архив).
    /// Нельзя брать «последний приём с увольнением» вместо ещё действующего места.
    /// </summary>
    public static Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> ApplyPerLegalEntityDismissalRule(
        Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> byOrg)
    {
        if (byOrg.Count == 0)
        {
            return byOrg;
        }

        var result = new Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (org, rows) in byOrg)
        {
            result[org] = SelectLatestRows(org, rows);
        }

        return result;
    }

    /// <summary>Повторный приём: дата приёма строго позже даты увольнения в том же ЮЛ.</summary>
    public static bool IsRehireAfterDismissal(DateTime? hireDate, DateTime? dismissalDate)
    {
        if (dismissalDate is null)
        {
            return true;
        }

        if (hireDate is null)
        {
            return false;
        }

        return hireDate.Value.Date > dismissalDate.Value.Date;
    }

    public static bool PreferIncomingEmployee(OrganizationEmployee incoming, OrganizationEmployee existing) =>
        PreferIncomingEmployee(ToRow(incoming), incoming.IsActive, ToRow(existing), existing.IsActive);

    public static bool IsWorkingByDismissalDate(DateTime? dismissalDate) =>
        OneCStaffEligibility.IsOpenByDismissalDate(dismissalDate);

    public static bool IsCsvRowEmployed(OneCEmployeeRow row, bool parserActive) =>
        parserActive
        && OneCStaffEligibility.IsActiveByDates(row.DismissalDate, row.EmploymentDate);

    /// <summary>Совместимость вызовов: несколько ЮЛ для одного человека разрешены.</summary>
    public static int KeepOnlyCurrentEmploymentAcrossOrganizations(
        Dictionary<string, List<OrganizationEmployee>> byOrg)
    {
        _ = byOrg;
        return 0;
    }

    private static List<(OneCEmployeeRow Row, bool IsActive)> SelectLatestRows(
        string organization,
        IEnumerable<(OneCEmployeeRow Row, bool IsActive)> rows)
    {
        _ = organization;
        var list = rows
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Row.Name))
            .ToList();

        var kept = new List<(OneCEmployeeRow Row, bool IsActive)>();
        // Человек + должность: внутреннее совместительство (слесарь + стропальщик) — обе карточки.
        foreach (var group in list.GroupBy(
                     pair => PersonProfessionKey(pair.Row),
                     StringComparer.OrdinalIgnoreCase))
        {
            // Два CSV по одному приёму/должности: zup без даты увольнения + anzhela с датой
            // (Цыбин/Коровина в Бетон Эксперт) — берём более полную строку с увольнением.
            var professionRows = CollapseSameHireConflicts(group.Select(pair => pair.Row).ToList());
            var activeCandidates = professionRows
                .Where(r => OneCStaffEligibility.IsActiveByDates(r.DismissalDate, r.EmploymentDate))
                .ToList();

            if (activeCandidates.Count > 0)
            {
                var best = activeCandidates
                    .OrderByDescending(r => OneCEmployeeListParser.IsPrimaryEmployment(r.EmploymentType) ? 1 : 0)
                    .ThenByDescending(r => r.EmploymentDate ?? DateTime.MinValue)
                    .ThenByDescending(r => r.TransferDate ?? DateTime.MinValue)
                    .First();
                kept.Add((NormalizeActiveStatus(best), true));
                continue;
            }

            // Нет актуальных — архивная последняя уволенная по этой должности в ЮЛ.
            var latest = professionRows
                .OrderByDescending(r => r.EmploymentDate ?? DateTime.MinValue)
                .ThenByDescending(r => r.TransferDate ?? DateTime.MinValue)
                .ThenByDescending(r => r.DismissalDate ?? DateTime.MinValue)
                .First();
            kept.Add((NormalizeInactiveStatus(latest), false));
        }

        return kept;
    }

    private static string PersonProfessionKey(OneCEmployeeRow row)
    {
        var person = OneCStaffEligibility.PersonStableKey(row);
        var profession = ProfessionMatcher.CoreKey(row.Profession);
        if (string.IsNullOrWhiteSpace(profession))
        {
            profession = ProfessionMatcher.Normalize(row.Profession);
        }

        return person + "|" + profession;
    }

    /// <summary>
    /// Один и тот же приём (одинаковая ДатаПриема) в двух выгрузках по одной должности:
    /// строка с датой увольнения побеждает «пустую» (неполная вторая база).
    /// Разные даты приёма = разные периоды (повторный приём) — не склеиваем.
    /// Разные должности на одну дату уже разведены в <see cref="SelectLatestRows"/>.
    /// </summary>
    private static List<OneCEmployeeRow> CollapseSameHireConflicts(IReadOnlyList<OneCEmployeeRow> personRows)
    {
        if (personRows.Count <= 1)
        {
            return personRows.ToList();
        }

        return personRows
            .GroupBy(r => r.EmploymentDate?.Date ?? DateTime.MinValue.Date)
            .Select(MergeSameHireGroup)
            .ToList();
    }

    private static OneCEmployeeRow MergeSameHireGroup(IEnumerable<OneCEmployeeRow> group)
    {
        var list = group.ToList();
        if (list.Count == 1)
        {
            return list[0];
        }

        var withDismissal = list.Where(r => r.DismissalDate is not null).ToList();
        if (withDismissal.Count > 0)
        {
            // Полная строка с увольнением важнее «Работа» без даты из другого CSV.
            return withDismissal
                .OrderByDescending(r => r.DismissalDate ?? DateTime.MinValue)
                .ThenByDescending(r => r.TransferDate ?? DateTime.MinValue)
                .ThenByDescending(r => OneCEmployeeListParser.IsPrimaryEmployment(r.EmploymentType) ? 1 : 0)
                .First();
        }

        return list
            .OrderByDescending(r => r.TransferDate ?? DateTime.MinValue)
            .ThenByDescending(r => OneCEmployeeListParser.IsPrimaryEmployment(r.EmploymentType) ? 1 : 0)
            .First();
    }

    private static OneCEmployeeRow NormalizeActiveStatus(OneCEmployeeRow row)
    {
        if (string.IsNullOrWhiteSpace(row.HrStatus)
            || OneCStaffEligibility.IsDismissedStatusText(row.HrStatus))
        {
            return row with { HrStatus = "Работа" };
        }

        return row;
    }

    private static OneCEmployeeRow NormalizeInactiveStatus(OneCEmployeeRow row)
    {
        if (!OneCStaffEligibility.IsDismissedStatusText(row.HrStatus))
        {
            return row with { HrStatus = "Увольнение" };
        }

        return row;
    }

    private static OneCEmployeeRow ToRow(OrganizationEmployee employee) =>
        new(
            employee.Name,
            employee.Profession,
            employee.Department,
            employee.Snils,
            employee.ExternalId,
            employee.BirthDate,
            employee.EmploymentDate,
            employee.HomeAddress,
            employee.HrStatus,
            employee.TransferDate,
            employee.EmploymentType,
            employee.DismissalDate);
}
