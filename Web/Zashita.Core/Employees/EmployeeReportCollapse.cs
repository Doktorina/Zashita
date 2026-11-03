namespace Zashita.Core.Employees;

/// <summary>
/// Схлопывание дублей сотрудника в отчётах: «Главный энергетик» / «Главный энергетик *»,
/// с подразделением и без, разные должности из СИЗ и 1С.
/// Если у человека в одной организации 2+ разных должности — оставляем отдельную строку на каждую.
/// </summary>
public static class EmployeeReportCollapse
{
    public static List<OrganizationEmployee> Collapse(IEnumerable<OrganizationEmployee> employees)
    {
        var result = new List<OrganizationEmployee>();
        foreach (var personGroup in employees
                     .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                     .GroupBy(e => PersonKey(e.Name, e.Snils), StringComparer.OrdinalIgnoreCase))
        {
            if (!HasMultipleProfessions(personGroup.Select(e => e.Profession)))
            {
                result.Add(MergeGroup(personGroup));
                continue;
            }

            foreach (var professionGroup in personGroup.GroupBy(
                         e => ProfessionKey(e.Profession),
                         StringComparer.OrdinalIgnoreCase))
            {
                result.Add(MergeGroup(professionGroup));
            }
        }

        return result
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static List<(string Organization, string Name, string Profession, string Department)> Collapse(
        IEnumerable<(string Organization, string Name, string Profession, string Department)> employees)
    {
        var list = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        if (list.Count == 0)
        {
            return [];
        }

        // «ИП Шварц Б.Э.» и «ИП Шварц Борис Эдуардович» — один юрлицо.
        var canonicalOrgs = OrganizationMatcher.BuildCanonicalNameMap(list.Select(e => e.Organization));
        list = list
            .Select(e =>
            {
                var org = canonicalOrgs.TryGetValue((e.Organization ?? string.Empty).Trim(), out var c)
                    ? c
                    : (e.Organization ?? string.Empty).Trim();
                var dept = SanitizeDepartment(org, e.Department);
                return (org, e.Name, e.Profession, dept);
            })
            .ToList();

        var result = new List<(string Organization, string Name, string Profession, string Department)>();
        foreach (var personGroup in list.GroupBy(
                     e => $"{e.Organization}|{PersonKey(e.Name, null)}",
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!HasMultipleProfessions(personGroup.Select(e => e.Profession)))
            {
                result.Add(MergeTupleGroup(personGroup));
                continue;
            }

            foreach (var professionGroup in personGroup.GroupBy(
                         e => ProfessionKey(e.Profession),
                         StringComparer.OrdinalIgnoreCase))
            {
                result.Add(MergeTupleGroup(professionGroup));
            }
        }

        return result
            .OrderBy(e => e.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Подразделение не должно дублировать название организации (иначе в дереве «вторая копия» людей).</summary>
    public static string SanitizeDepartment(string? organization, string? department)
    {
        var org = (organization ?? string.Empty).Trim();
        var dept = (department ?? string.Empty).Trim();
        if (dept.Length == 0 || dept == "—")
        {
            return string.IsNullOrWhiteSpace(dept) ? "—" : dept;
        }

        if (org.Length > 0
            && (string.Equals(org, dept, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(org, dept)))
        {
            return "—";
        }

        // Название юрлица ошибочно попало в колонку подразделения.
        if (dept.StartsWith("ИП ", StringComparison.OrdinalIgnoreCase)
            || dept.StartsWith("ООО ", StringComparison.OrdinalIgnoreCase)
            || dept.StartsWith("АО ", StringComparison.OrdinalIgnoreCase)
            || dept.StartsWith("ЗАО ", StringComparison.OrdinalIgnoreCase))
        {
            return "—";
        }

        return dept;
    }

    private static bool HasMultipleProfessions(IEnumerable<string?> professions) =>
        professions
            .Select(ProfessionKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;

    private static (string Organization, string Name, string Profession, string Department) MergeTupleGroup(
        IEnumerable<(string Organization, string Name, string Profession, string Department)> group)
    {
        var records = group.ToList();
        var best = records
            .OrderByDescending(e => DepartmentScore(e.Department))
            .ThenByDescending(e => (e.Profession ?? string.Empty).Length)
            .First();
        var department = records
            .Select(e => e.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d) && d != "—")
            .OrderByDescending(DepartmentScore)
            .ThenByDescending(d => (d ?? string.Empty).Length)
            .FirstOrDefault()
            ?? best.Department;
        var profession = records
            .Select(e => e.Profession)
            .OrderByDescending(p => ProfessionMatcher.Normalize(p).Length)
            .ThenByDescending(p => (p ?? string.Empty).Length)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            ?? best.Profession;
        return (best.Organization, best.Name, profession, department);
    }

    private static OrganizationEmployee MergeGroup(IEnumerable<OrganizationEmployee> group)
    {
        var records = group.ToList();
        // Сначала работающие: иначе уволенный с заполненным подразделением
        // «перебивал» нового/повторно принятого и выкидывал его из отчётов.
        var best = records
            .OrderByDescending(e => EmployeeHrStatus.IsEmployed(e) ? 1_000 : 0)
            .ThenByDescending(e => DepartmentScore(e.Department))
            .ThenByDescending(e => OneCEmployeeListParser.IsPrimaryEmployment(e.EmploymentType) ? 1 : 0)
            .ThenByDescending(e => e.IsActive ? 1 : 0)
            .ThenByDescending(e => (e.Profession ?? string.Empty).Length)
            .ThenByDescending(e => e.SyncedAt ?? DateTime.MinValue)
            .First();

        var employed = records.FirstOrDefault(EmployeeHrStatus.IsEmployed);
        if (employed is not null)
        {
            best = employed;
        }

        var department = records
            .Where(EmployeeHrStatus.IsEmployed)
            .Select(e => e.Department)
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
            ?? records.Select(e => e.Department).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
            ?? best.Department;
        var profession = records
            .Where(EmployeeHrStatus.IsEmployed)
            .Select(e => e.Profession)
            .OrderByDescending(p => ProfessionMatcher.Normalize(p).Length)
            .ThenByDescending(p => (p ?? string.Empty).Length)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            ?? records
                .Select(e => e.Profession)
                .OrderByDescending(p => ProfessionMatcher.Normalize(p).Length)
                .ThenByDescending(p => (p ?? string.Empty).Length)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            ?? best.Profession;

        var isEmployed = EmployeeHrStatus.IsEmployed(best);
        return new OrganizationEmployee
        {
            Id = best.Id,
            ExternalId = best.ExternalId,
            Name = best.Name,
            LastName = best.LastName,
            FirstName = best.FirstName,
            Patronymic = best.Patronymic,
            Profession = profession,
            Department = department,
            Snils = best.Snils,
            HomeAddress = best.HomeAddress,
            BirthDate = best.BirthDate,
            EmploymentDate = best.EmploymentDate,
            TransferDate = records.Select(e => e.TransferDate).FirstOrDefault(d => d is not null) ?? best.TransferDate,
            // У работающего не наследуем дату увольнения со старых строк (повторный приём).
            DismissalDate = isEmployed
                ? (best.DismissalDate is { } future && future.Date > DateTime.Today ? future : null)
                : records.Select(e => e.DismissalDate).FirstOrDefault(d => d is not null) ?? best.DismissalDate,
            EmploymentType = best.EmploymentType,
            IsActive = isEmployed,
            HrStatus = isEmployed
                ? (EmployeeHrStatus.IsDismissed(best.HrStatus) || string.IsNullOrWhiteSpace(best.HrStatus)
                    ? "Работа"
                    : best.HrStatus)
                : (string.IsNullOrWhiteSpace(best.HrStatus) ? "Увольнение" : best.HrStatus),
            SyncedAt = best.SyncedAt
        };
    }

    private static string PersonKey(string name, string? snils)
    {
        var snilsKey = OneCEmployeeListParser.NormalizeSnils(snils);
        return !string.IsNullOrWhiteSpace(snilsKey)
            ? $"s:{snilsKey}"
            : $"n:{(name ?? string.Empty).Trim()}";
    }

    private static string ProfessionKey(string? profession)
    {
        var core = ProfessionMatcher.CoreKey(profession);
        return core.Length > 0 ? core : ProfessionMatcher.Normalize(profession);
    }

    private static int DepartmentScore(string? department) =>
        string.IsNullOrWhiteSpace(department) ? 0 : 100;
}
