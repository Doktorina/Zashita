using System.Globalization;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Отчёт «Состояние по специальной оценке условий труда» — дерево как в обучении.
/// </summary>
public static class SoutStatusBuilder
{
    public const int ValidityYears = 5;
    public static IReadOnlyList<SoutStatusTreeLine> BuildTree(
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<WorkplaceAssignmentDocument> assignments,
        IReadOnlyList<OrganizationEmployee> employees,
        string organization,
        DateTime reportDate,
        string? department = null,
        string? conditionsClass = null,
        string? statusFilter = null,
        string? workplaceType = null,
        string? profession = null,
        string? needFilter = null,
        IReadOnlySet<string>? plannedWorkplaceIds = null)
    {
        var org = organization?.Trim() ?? string.Empty;
        var asOf = reportDate.Date;
        var needKey = SoutWorkplaceNeedsBuilder.NormalizeNeedFilter(needFilter);
        plannedWorkplaceIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var leaves = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(w))
            .Where(w => string.IsNullOrWhiteSpace(org) ||
                        string.Equals(w.Organization, org, StringComparison.OrdinalIgnoreCase))
            .Where(w => string.IsNullOrWhiteSpace(department) ||
                        string.Equals(w.Department, department, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var workplaceById = leaves
            .Where(w => !string.IsNullOrWhiteSpace(w.Id))
            .ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

        var assignmentIndex = BuildAssignmentIndex(assignments, org);
        var employeeIndex = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var employeeById = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var analogousCounts = leaves
            .Where(w => !string.IsNullOrWhiteSpace(w.MainWorkplaceNumber))
            .GroupBy(w => w.MainWorkplaceNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.WorkerCount > 0 ? x.WorkerCount : 1),
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<SoutStatusRow>();
        var staff = CollapseStaffForReport(employees, department);

        if (staff.Count > 0)
        {
            var claimedWorkplaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var emp in staff)
            {
                var wp = FindWorkplaceForEmployee(leaves, emp, assignmentIndex, claimedWorkplaceIds);
                if (wp is not null && !string.IsNullOrWhiteSpace(wp.Id))
                {
                    claimedWorkplaceIds.Add(wp.Id);
                }

                var row = wp is null
                    ? ToEmployeeOnlyRow(emp, org, asOf)
                    : ToRow(wp, asOf, assignmentIndex, employeeIndex, employeeById, analogousCounts);
                ApplyEmployee(row, emp);
                EnsureLinkedCardFromEmployee(row, emp, leaves);

                if (!string.IsNullOrWhiteSpace(profession))
                {
                    var prof = string.IsNullOrWhiteSpace(row.Profession) ? row.WorkplaceName : row.Profession;
                    if (!string.Equals(prof, profession, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(emp.Profession, profession, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(conditionsClass) &&
                    !string.Equals(row.ConditionsClass, conditionsClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(statusFilter) &&
                    !string.Equals(row.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(workplaceType))
                {
                    if (string.Equals(workplaceType, "analogous", StringComparison.OrdinalIgnoreCase) && !row.IsAnalogous)
                    {
                        continue;
                    }

                    if (string.Equals(workplaceType, "single", StringComparison.OrdinalIgnoreCase) && row.IsAnalogous)
                    {
                        continue;
                    }
                }

                if (!MatchesNeedFilter(row, workplaceById, needKey, asOf, plannedWorkplaceIds))
                {
                    continue;
                }

                rows.Add(row);
            }
        }
        else
        {
            foreach (var wp in leaves)
            {
                var row = ToRow(wp, asOf, assignmentIndex, employeeIndex, employeeById, analogousCounts);
                if (!string.IsNullOrWhiteSpace(profession))
                {
                    var prof = string.IsNullOrWhiteSpace(row.Profession) ? row.WorkplaceName : row.Profession;
                    if (!string.Equals(prof, profession, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(conditionsClass) &&
                    !string.Equals(row.ConditionsClass, conditionsClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(statusFilter) &&
                    !string.Equals(row.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(workplaceType))
                {
                    if (string.Equals(workplaceType, "analogous", StringComparison.OrdinalIgnoreCase) && !row.IsAnalogous)
                    {
                        continue;
                    }

                    if (string.Equals(workplaceType, "single", StringComparison.OrdinalIgnoreCase) && row.IsAnalogous)
                    {
                        continue;
                    }
                }

                if (!MatchesNeedFilter(row, workplaceById, needKey, asOf, plannedWorkplaceIds))
                {
                    continue;
                }

                rows.Add(row);
            }
        }

        return BuildTreeLines(org, rows, workplaces);
    }

    /// <summary>
    /// Одна строка на человека+должность: если в штате две записи (с подразделением и без),
    /// оставляем запись с заполненным подразделением.
    /// </summary>
    private static List<OrganizationEmployee> CollapseStaffForReport(
        IReadOnlyList<OrganizationEmployee> employees,
        string? department)
    {
        return employees
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .Where(e => string.IsNullOrWhiteSpace(department) ||
                        string.Equals(e.Department, department, StringComparison.OrdinalIgnoreCase))
            .GroupBy(StaffGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(PreferEmploymentScore).First())
            .OrderBy(e => e.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string StaffGroupKey(OrganizationEmployee employee)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(employee.Snils);
        var profession = ProfessionMatcher.Normalize(employee.Profession);
        if (!string.IsNullOrWhiteSpace(snils))
        {
            return $"s:{snils}|{profession}";
        }

        return $"n:{employee.Name.Trim()}|{profession}";
    }

    private static int PreferEmploymentScore(OrganizationEmployee employee)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(employee.Department))
        {
            score += 100;
        }

        if (OneCEmployeeListParser.IsPrimaryEmployment(employee.EmploymentType))
        {
            score += 50;
        }

        return score;
    }

    private static bool MatchesNeedFilter(
        SoutStatusRow row,
        IReadOnlyDictionary<string, WorkplaceRecord> workplaceById,
        string filterKey,
        DateTime asOf,
        IReadOnlySet<string> plannedWorkplaceIds)
    {
        if (filterKey == SoutWorkplaceNeedsBuilder.NeedAll)
        {
            return true;
        }

        WorkplaceRecord? workplace = null;
        if (!string.IsNullOrWhiteSpace(row.LinkedSoutCardWorkplaceId)
            && workplaceById.TryGetValue(row.LinkedSoutCardWorkplaceId, out var linkedCard))
        {
            workplace = linkedCard;
        }
        else if (!string.IsNullOrWhiteSpace(row.WorkplaceId))
        {
            workplaceById.TryGetValue(row.WorkplaceId, out workplace);
        }

        return SoutWorkplaceNeedsBuilder.MatchesNeedFilter(
            workplace,
            filterKey,
            asOf,
            plannedWorkplaceIds,
            workplaceById);
    }

    /// <summary>Карты СОУТ организации для выбора в отчёте (по заполненным данным или любым РМ).</summary>
    public static IReadOnlyList<SoutCardPickOption> BuildCardOptions(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization)
    {
        var org = organization?.Trim() ?? string.Empty;
        return workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(w))
            .Where(w => string.IsNullOrWhiteSpace(org) ||
                        string.Equals(w.Organization, org, StringComparison.OrdinalIgnoreCase))
            .Select(w =>
            {
                var profession = string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession;
                var num = string.IsNullOrWhiteSpace(w.SoutReportNumber) ? "без №" : ("№ " + w.SoutReportNumber.Trim());
                var rm = string.IsNullOrWhiteSpace(w.WorkplaceNumber) ? "" : (" · РМ " + w.WorkplaceNumber.Trim());
                var date = w.SoutAssessmentDate is { } d
                    ? " · " + d.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"))
                    : "";
                return new SoutCardPickOption
                {
                    WorkplaceId = w.Id,
                    Profession = ProfessionMatcher.Normalize(profession),
                    ReportNumber = w.SoutReportNumber?.Trim() ?? string.Empty,
                    OkCode = w.OkCode?.Trim() ?? string.Empty,
                    WorkerCount = w.WorkerCount > 0 ? w.WorkerCount : 1,
                    ConductDate = w.SoutAssessmentDate?.Date,
                    ConditionsClass = w.ConditionsClass ?? string.Empty,
                    Label = $"{profession}: {num}{rm}{date}"
                };
            })
            .OrderBy(c => c.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.ReportNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<SoutCardPickOption> CardsForProfession(
        IReadOnlyList<SoutCardPickOption> cards,
        string? profession)
    {
        var want = ProfessionMatcher.Normalize(profession);
        if (string.IsNullOrWhiteSpace(want))
        {
            return cards;
        }

        var exact = cards
            .Where(c => string.Equals(c.Profession, want, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return exact.Count > 0 ? exact : cards;
    }

    /// <summary>Копирует поля карты с источника на целевое РМ.</summary>
    public static void ApplyCardFields(WorkplaceRecord target, WorkplaceRecord source)
    {
        target.LinkedSoutCardWorkplaceId = source.Id;
        target.SoutReportNumber = source.SoutReportNumber ?? string.Empty;
        target.OkCode = source.OkCode ?? string.Empty;
        target.WorkerCount = source.WorkerCount > 0 ? source.WorkerCount : 1;
        // Одна каноническая дата: дата СОУТ из отчёта, иначе дата составления карты.
        var date = source.SoutAssessmentDate?.Date
                   ?? source.SoutCard?.PreparedDate?.Date;
        target.SoutAssessmentDate = date;
        target.SoutCard ??= new SoutCardDetails();
        target.SoutCard.PreparedDate = date;
        if (!string.IsNullOrWhiteSpace(source.ConditionsClass))
        {
            target.ConditionsClass = source.ConditionsClass;
        }

        target.DeclarationOfConformity = source.DeclarationOfConformity;
        target.RequiresUnscheduledSout = source.RequiresUnscheduledSout;
        target.SoutAssessmentNotConducted = false;
        target.ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>Добавляет сотрудника (СНИЛС) в карту СОУТ — строка 021.</summary>
    public static void AttachEmployeeToCard(WorkplaceRecord card, OrganizationEmployee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.Id) && string.IsNullOrWhiteSpace(employee.Snils))
        {
            return;
        }

        card.SoutCard ??= new SoutCardDetails();
        card.SoutCard.EmployeeIds ??= [];
        if (!string.IsNullOrWhiteSpace(employee.Id) &&
            !card.SoutCard.EmployeeIds.Any(id =>
                string.Equals(id, employee.Id, StringComparison.OrdinalIgnoreCase)))
        {
            card.SoutCard.EmployeeIds.Add(employee.Id);
        }

        // Дублируем СНИЛС в ExtraLines["021"] для печати/отображения.
        var snils = OneCEmployeeListParser.FormatSnils(employee.Snils);
        if (!string.IsNullOrWhiteSpace(snils))
        {
            card.SoutCard.ExtraLines ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var current = card.SoutCard.ExtraLines.TryGetValue("021", out var existing)
                ? existing
                : string.Empty;
            if (!current.Contains(snils, StringComparison.OrdinalIgnoreCase))
            {
                card.SoutCard.ExtraLines["021"] = string.IsNullOrWhiteSpace(current)
                    ? snils
                    : current.Trim().TrimEnd(';', ',') + "; " + snils;
            }
        }

        card.ModifiedAt = DateTime.UtcNow;
    }

    public static SoutStatusRow ToRow(
        WorkplaceRecord workplace,
        DateTime asOf,
        IReadOnlyDictionary<string, (string Employee, DateTime? AssignedFrom)> assignments,
        IReadOnlyDictionary<string, OrganizationEmployee> employees,
        IReadOnlyDictionary<string, OrganizationEmployee> employeesById,
        IReadOnlyDictionary<string, int> analogousCounts)
    {
        var employment = ResolveAssignedPerson(workplace, assignments, employees, employeesById);
        // Декларация в UI = бессрочно / «Актуально» (в т.ч. если класс 3.x отметили декларацией вручную).
        // Иначе дата+5 лет красит строку красным при статусе «Актуально».
        var indefinite = workplace.DeclarationOfConformity;
        var assessmentDate = workplace.SoutAssessmentDate?.Date
                             ?? workplace.SoutCard?.PreparedDate?.Date;
        DateTime? next = null;
        if (!indefinite && assessmentDate is { } conduct)
        {
            next = conduct.Date.AddYears(ValidityYears);
        }

        var status = ResolveStatus(workplace, indefinite, next, asOf);
        var traffic = ResolveTraffic(workplace, employment.EmploymentDate, next, indefinite, status, asOf);

        var isAnalogous = !string.IsNullOrWhiteSpace(workplace.MainWorkplaceNumber);
        var workers = workplace.WorkerCount > 0 ? workplace.WorkerCount : 1;
        if (isAnalogous &&
            analogousCounts.TryGetValue(workplace.MainWorkplaceNumber.Trim(), out var groupCount))
        {
            workers = groupCount;
        }

        return new SoutStatusRow
        {
            WorkplaceId = workplace.Id,
            Organization = workplace.Organization,
            Department = workplace.Department,
            WorkplaceName = string.IsNullOrWhiteSpace(workplace.Name) ? workplace.Profession : workplace.Name,
            WorkplaceNumber = workplace.WorkplaceNumber,
            MainWorkplaceNumber = workplace.MainWorkplaceNumber,
            Profession = workplace.Profession,
            EmploymentDate = employment.EmploymentDate,
            EmployeeName = employment.EmployeeName,
            Snils = employment.Snils,
            OkCode = workplace.OkCode?.Trim() ?? string.Empty,
            ConditionsClass = workplace.ConditionsClass,
            Periodicity = indefinite ? "бессрочно" : "1 раз в 5 лет",
            ConductDate = assessmentDate,
            NextDate = next,
            IsIndefinite = indefinite,
            NextDateDisplay = indefinite
                ? "бессрочно"
                : next?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "—",
            ReportNumber = workplace.SoutReportNumber,
            // Карту в отчёте не подставляем с РМ — только явное закрепление человека на карте
            // (EmployeeIds / стр. 021) через EnsureLinkedCardFromEmployee.
            LinkedSoutCardWorkplaceId = string.Empty,
            DeclarationOfConformity = workplace.DeclarationOfConformity,
            RequiresUnscheduledSout = workplace.RequiresUnscheduledSout,
            Status = status,
            Traffic = traffic,
            WorkerCount = workers,
            IsAnalogous = isAnalogous
        };
    }

    private static void ApplyEmployee(SoutStatusRow row, OrganizationEmployee emp)
    {
        row.EmployeeName = emp.Name.Trim();
        row.Snils = OneCEmployeeListParser.FormatSnils(emp.Snils);
        row.EmploymentDate = emp.EmploymentDate?.Date ?? row.EmploymentDate;
        if (!string.IsNullOrWhiteSpace(emp.Profession))
        {
            row.Profession = emp.Profession.Trim();
        }

        if (!string.IsNullOrWhiteSpace(emp.Department))
        {
            row.Department = emp.Department.Trim();
        }
    }

    /// <summary>
    /// Если сотрудник закреплён в карте СОУТ (EmployeeIds / стр. 021),
    /// в отчёте автоматически выбираем эту карту — без ручного выбора в выпадающем списке.
    /// Иначе остаётся «— без карты СОУТ —».
    /// </summary>
    private static void EnsureLinkedCardFromEmployee(
        SoutStatusRow row,
        OrganizationEmployee emp,
        IReadOnlyList<WorkplaceRecord> leaves)
    {
        if (!string.IsNullOrWhiteSpace(row.LinkedSoutCardWorkplaceId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.WorkplaceId))
        {
            var current = leaves.FirstOrDefault(w =>
                string.Equals(w.Id, row.WorkplaceId, StringComparison.OrdinalIgnoreCase));
            if (current is not null && EmployeePinnedOnCard(current, emp))
            {
                row.LinkedSoutCardWorkplaceId = current.Id;
                return;
            }
        }

        var card = leaves.FirstOrDefault(w => EmployeePinnedOnCard(w, emp));
        if (card is not null)
        {
            row.LinkedSoutCardWorkplaceId = card.Id;
            if (string.IsNullOrWhiteSpace(row.WorkplaceId))
            {
                row.WorkplaceId = card.Id;
            }
        }
    }

    /// <summary>Человек явно закреплён на карте СОУТ (не просто сопоставлен с РМ из штата).</summary>
    private static bool EmployeePinnedOnCard(WorkplaceRecord workplace, OrganizationEmployee emp)
    {
        var listedById = false;
        var empId = emp.Id?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(empId)
            && (workplace.SoutCard?.EmployeeIds ?? []).Any(id =>
                string.Equals(id?.Trim(), empId, StringComparison.OrdinalIgnoreCase)))
        {
            listedById = true;
        }

        var line021 = string.Empty;
        if (workplace.SoutCard?.ExtraLines is not null
            && workplace.SoutCard.ExtraLines.TryGetValue("021", out var existing021))
        {
            line021 = existing021 ?? string.Empty;
        }

        var listedBy021 = false;
        if (!string.IsNullOrWhiteSpace(line021))
        {
            var snilsFormatted = OneCEmployeeListParser.FormatSnils(emp.Snils);
            var snilsNorm = OneCEmployeeListParser.NormalizeSnils(emp.Snils);
            if (!string.IsNullOrWhiteSpace(snilsFormatted)
                && line021.Contains(snilsFormatted, StringComparison.OrdinalIgnoreCase))
            {
                listedBy021 = true;
            }
            else if (!string.IsNullOrWhiteSpace(snilsNorm)
                     && snilsNorm.Length >= 11
                     && line021.Replace(" ", "").Replace("-", "")
                         .Contains(snilsNorm, StringComparison.OrdinalIgnoreCase))
            {
                listedBy021 = true;
            }
            else
            {
                var name = emp.Name?.Trim() ?? string.Empty;
                listedBy021 = !string.IsNullOrWhiteSpace(name)
                              && line021.Contains(name, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Стр. 021 заполняется при выборе карты в отчёте / сохранении карты — надёжный признак.
        if (listedBy021)
        {
            return true;
        }

        // EmployeeIds без 021 часто остаются от старой синхронизации штата с пустым РМ —
        // автоподстановку даём только если карта реально заполнена (импорт / дата / №).
        return listedById && LooksLikeImportedSoutCard(workplace);
    }

    private static bool LooksLikeImportedSoutCard(WorkplaceRecord w) =>
        w.SoutAssessmentDate is not null
        || !string.IsNullOrWhiteSpace(w.SoutReportNumber)
        || w.SoutCard?.PreparedDate is not null
        || !string.IsNullOrWhiteSpace(w.SoutCard?.Equipment)
        || !string.IsNullOrWhiteSpace(w.SoutCard?.Materials)
        || (!string.IsNullOrWhiteSpace(w.SoutCard?.FinalClass)
            && !string.Equals(
                SoutConditionsClassStore.ExtractSubclassCode(w.SoutCard.FinalClass),
                "2",
                StringComparison.OrdinalIgnoreCase));

    private static SoutStatusRow ToEmployeeOnlyRow(OrganizationEmployee emp, string organization, DateTime asOf)
    {
        var workplace = new WorkplaceRecord
        {
            // Пустой Id — строки ещё нет в справочнике РМ; привязку карты сохраняем через EmployeeIds.
            Id = string.Empty,
            Organization = organization,
            Department = emp.Department?.Trim() ?? string.Empty,
            Name = emp.Profession?.Trim() ?? string.Empty,
            Profession = emp.Profession?.Trim() ?? string.Empty,
            IsUsed = true,
            SoutAssessmentNotConducted = true
        };
        var emptyAssignments = new Dictionary<string, (string Employee, DateTime? AssignedFrom)>(StringComparer.OrdinalIgnoreCase);
        var emptyPeople = new Dictionary<string, OrganizationEmployee>(StringComparer.OrdinalIgnoreCase);
        var emptyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var row = ToRow(workplace, asOf, emptyAssignments, emptyPeople, emptyPeople, emptyCounts);
        ApplyEmployee(row, emp);
        return row;
    }

    private static WorkplaceRecord? FindWorkplaceForEmployee(
        IReadOnlyList<WorkplaceRecord> leaves,
        OrganizationEmployee emp,
        IReadOnlyDictionary<string, (string Employee, DateTime? AssignedFrom)> assignments,
        ISet<string>? claimedWorkplaceIds = null)
    {
        var empId = emp.Id?.Trim() ?? string.Empty;
        var empName = emp.Name.Trim();
        if (!string.IsNullOrWhiteSpace(empId))
        {
            var byCard = leaves.FirstOrDefault(w =>
                (w.SoutCard?.EmployeeIds ?? []).Any(id =>
                    string.Equals(id?.Trim(), empId, StringComparison.OrdinalIgnoreCase)));
            if (byCard is not null)
            {
                return byCard;
            }
        }

        foreach (var workplace in leaves)
        {
            if (assignments.TryGetValue(workplace.Id, out var assigned) &&
                string.Equals(assigned.Employee?.Trim(), empName, StringComparison.OrdinalIgnoreCase))
            {
                return workplace;
            }

            if (!string.IsNullOrWhiteSpace(workplace.WorkplaceNumber) &&
                assignments.TryGetValue("num:" + workplace.WorkplaceNumber.Trim(), out assigned) &&
                string.Equals(assigned.Employee?.Trim(), empName, StringComparison.OrdinalIgnoreCase))
            {
                return workplace;
            }
        }

        var dept = string.IsNullOrWhiteSpace(emp.Department) ? "Без подразделения" : emp.Department.Trim();
        var prof = ProfessionMatcher.Normalize(emp.Profession);
        var exact = leaves.Where(w =>
        {
            var wDept = string.IsNullOrWhiteSpace(w.Department) ? "Без подразделения" : w.Department.Trim();
            var wProf = ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession);
            return string.Equals(wDept, dept, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(wProf, prof, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (exact.Count > 0)
        {
            var free = exact.FirstOrDefault(w =>
                claimedWorkplaceIds is null ||
                string.IsNullOrWhiteSpace(w.Id) ||
                !claimedWorkplaceIds.Contains(w.Id));
            return free ?? exact[0];
        }

        var byProf = leaves.Where(w =>
        {
            var wProf = ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession);
            return string.Equals(wProf, prof, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (byProf.Count == 0)
        {
            return null;
        }

        var freeByProf = byProf.FirstOrDefault(w =>
            claimedWorkplaceIds is null ||
            string.IsNullOrWhiteSpace(w.Id) ||
            !claimedWorkplaceIds.Contains(w.Id));
        return freeByProf ?? (byProf.Count == 1 ? byProf[0] : null);
    }

    public static string ResolveStatus(
        WorkplaceRecord workplace,
        bool indefinite,
        DateTime? nextDate,
        DateTime asOf)
    {
        if (workplace.SoutAssessmentNotConducted)
        {
            return "Не проведена специальная оценка";
        }

        if (workplace.RequiresUnscheduledSout)
        {
            return "Требуется внеплановая спецоценка";
        }

        // Декларация соответствия (бессрочно) — не «Просрочено», даже без даты проведения.
        if (indefinite || workplace.DeclarationOfConformity)
        {
            return "Актуально";
        }

        // Без даты проведения — «не проведена», а не «просрочена».
        // № карты СОУТ (после импорта Word) — оценка есть; дата могла не распарситься.
        if (workplace.SoutAssessmentDate is null && workplace.SoutCard?.PreparedDate is null)
        {
            if (!string.IsNullOrWhiteSpace(workplace.SoutReportNumber))
            {
                return "Актуально";
            }

            return "Не проведена специальная оценка";
        }

        if (nextDate is { } next && next.Date < asOf.Date)
        {
            return "Просрочено";
        }

        return "Актуально";
    }

    /// <summary>
    /// Плановый год СОУТ для справочника РМ — та же логика, что в отчёте по СОУТ:
    /// внеплановая → текущий год; декларация → бессрочно; иначе дата СОУТ + 5 лет (год очередной).
    /// </summary>
    public static string ResolvePlanYearDisplay(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.IsGroup)
        {
            return string.Empty;
        }

        var today = (asOf ?? DateTime.Today).Date;
        var indefinite = workplace.DeclarationOfConformity;

        if (workplace.RequiresUnscheduledSout)
        {
            return today.Year.ToString();
        }

        if (indefinite)
        {
            return "бессрочно";
        }

        if (workplace.SoutAssessmentNotConducted)
        {
            return today.Year.ToString();
        }

        if (workplace.SoutAssessmentDate is null)
        {
            // Есть № карты без даты — не ставим «текущий год» как будто нужна внеплановая.
            return string.IsNullOrWhiteSpace(workplace.SoutReportNumber)
                ? today.Year.ToString()
                : string.Empty;
        }

        var next = workplace.SoutAssessmentDate.Value.Date.AddYears(ValidityYears);
        if (next.Date < today)
        {
            return today.Year.ToString();
        }

        return next.Year.ToString();
    }

    public static string ResolveTraffic(
        WorkplaceRecord workplace,
        DateTime? employmentDate,
        DateTime? nextDate,
        bool indefinite,
        string status,
        DateTime asOf)
    {
        // Ручная пометка с «Карты СОУТ» — как на справочнике карт.
        var mark = NormalizeTrafficMark(workplace.SoutTrafficMark);
        if (mark == "green")
        {
            return "green";
        }

        if (mark == "red")
        {
            return "red";
        }

        if (mark == "blue")
        {
            return "yellow";
        }

        if (string.Equals(status, "Требуется внеплановая спецоценка", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Требует внеплановой СОУТ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Просрочено", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Не проведена специальная оценка", StringComparison.OrdinalIgnoreCase))
        {
            return "red";
        }

        // Декларация / «Актуально» — не красим красным по истечению 5 лет
        // (иначе при декларации на классе 3.x статус «Актуально», а строка горит «просрочено»).
        if (indefinite
            || workplace.DeclarationOfConformity
            || string.Equals(status, "Актуально", StringComparison.OrdinalIgnoreCase))
        {
            return "green";
        }

        // Нет СОУТ дольше 9 месяцев с даты трудоустройства
        if (workplace.SoutAssessmentDate is null && employmentDate is { } hire)
        {
            if (asOf.Date >= hire.Date.AddMonths(9))
            {
                return "red";
            }

            if (asOf.Date >= hire.Date.AddMonths(7))
            {
                return "yellow";
            }
        }

        // Спустя 4 года 9 месяцев с предыдущей спецоценки — предупреждение
        if (workplace.SoutAssessmentDate is { } conduct)
        {
            var warnFrom = conduct.Date.AddYears(4).AddMonths(9);
            if (asOf.Date >= warnFrom)
            {
                return nextDate is { } n && n.Date < asOf.Date ? "red" : "yellow";
            }
        }

        return string.Empty;
    }

    /// <summary>Нормализация ручной пометки: green / red / blue или пусто (авто).</summary>
    public static string NormalizeTrafficMark(string? mark)
    {
        var value = mark?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "green" or "red" or "blue" ? value : string.Empty;
    }

    /// <summary>
    /// Карта СОУТ ещё не заполнена: нет даты проведения и нет № карты.
    /// Для таких строк в «Карты СОУТ» класс условий показываем как «—».
    /// </summary>
    public static bool IsSoutCardBlank(WorkplaceRecord? workplace)
    {
        if (workplace is null || workplace.IsGroup)
        {
            return false;
        }

        if (workplace.SoutAssessmentDate is not null
            || workplace.SoutCard?.PreparedDate is not null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(workplace.SoutReportNumber);
    }

    /// <summary>
    /// Класс для отображения/сохранения на «Карты СОУТ»: пустая карта → «—» (пустая строка).
    /// </summary>
    public static string ConditionsClassForCards(WorkplaceRecord workplace) =>
        IsSoutCardBlank(workplace) ? string.Empty : (workplace.ConditionsClass ?? string.Empty).Trim();

    /// <summary>
    /// Цвет строки на «Карты СОУТ»: ручная пометка или авто по дате/флагам.
    /// «Истекает» — синий (в отчёте СОУТ тот же срок даёт жёлтый).
    /// </summary>
    public static string ResolveCardsTraffic(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.IsGroup)
        {
            return string.Empty;
        }

        var mark = NormalizeTrafficMark(workplace.SoutTrafficMark);
        if (!string.IsNullOrEmpty(mark))
        {
            return mark;
        }

        var today = (asOf ?? DateTime.Today).Date;
        var indefinite = workplace.DeclarationOfConformity;
        DateTime? next = null;
        if (!indefinite && workplace.SoutAssessmentDate is { } conduct)
        {
            next = conduct.Date.AddYears(ValidityYears);
        }

        var status = ResolveStatus(workplace, indefinite, next, today);
        var traffic = ResolveTraffic(workplace, employmentDate: null, next, indefinite, status, today);
        if (string.Equals(traffic, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            return "blue";
        }

        return traffic;
    }

    public static string CardsTrafficLabel(string? traffic) => traffic?.Trim().ToLowerInvariant() switch
    {
        "green" => "Проведена",
        "red" => "Не проведена / просрочена",
        "blue" => "Истекает",
        _ => "—"
    };

    /// <summary>Подпись «Авто (…)» с учётом внеплановой (не путать с «не проведена»).</summary>
    public static string CardsAutoStatusLabel(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.RequiresUnscheduledSout && !workplace.SoutAssessmentNotConducted)
        {
            return "Внеплановая";
        }

        return CardsTrafficLabel(ResolveCardsTraffic(new WorkplaceRecord
        {
            IsGroup = false,
            ConditionsClass = workplace.ConditionsClass,
            DeclarationOfConformity = workplace.DeclarationOfConformity,
            RequiresUnscheduledSout = workplace.RequiresUnscheduledSout,
            SoutAssessmentNotConducted = workplace.SoutAssessmentNotConducted,
            SoutAssessmentDate = workplace.SoutAssessmentDate ?? workplace.SoutCard?.PreparedDate,
            SoutReportNumber = workplace.SoutReportNumber,
            SoutCard = workplace.SoutCard is null
                ? null
                : new SoutCardDetails { PreparedDate = workplace.SoutCard.PreparedDate }
        }, asOf));
    }

    public static int? ParseClassCode(string? conditionsClass)
    {
        if (string.IsNullOrWhiteSpace(conditionsClass))
        {
            return null;
        }

        var text = conditionsClass.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact) &&
            exact is >= 1 and <= 4)
        {
            return exact;
        }

        // «Оптимальные (класс 1)», «Вредные (класс 3.1)» → 1 / 3
        var m = Regex.Match(text, @"класс\s*(\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var fromLabel))
        {
            return fromLabel;
        }

        m = Regex.Match(text, @"\b([1-4])(?:\.\d)?\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var fromDigit))
        {
            return fromDigit;
        }

        return null;
    }

    private static (string EmployeeName, string Snils, DateTime? EmploymentDate) ResolveAssignedPerson(
        WorkplaceRecord workplace,
        IReadOnlyDictionary<string, (string Employee, DateTime? AssignedFrom)> assignments,
        IReadOnlyDictionary<string, OrganizationEmployee> employees,
        IReadOnlyDictionary<string, OrganizationEmployee> employeesById)
    {
        if (!assignments.TryGetValue(workplace.Id, out var assigned) &&
            !string.IsNullOrWhiteSpace(workplace.WorkplaceNumber))
        {
            assignments.TryGetValue("num:" + workplace.WorkplaceNumber.Trim(), out assigned);
        }

        var assignedName = assigned.Employee?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(assignedName) &&
            employees.TryGetValue(assignedName, out var emp))
        {
            return (
                emp.Name.Trim(),
                OneCEmployeeListParser.FormatSnils(emp.Snils),
                emp.EmploymentDate?.Date ?? assigned.AssignedFrom?.Date);
        }

        // Если в закреплении ФИО из другого юрлица/не найдено в текущей организации,
        // игнорируем это ФИО и пытаемся определить сотрудника корректно.
        var name = string.Empty;

        // Фоллбек: сотрудник выбран в карте СОУТ (поле 021).
        var cardEmployeeIds = workplace.SoutCard?.EmployeeIds ?? [];
        foreach (var employeeId in cardEmployeeIds)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
            {
                continue;
            }

            if (employeesById.TryGetValue(employeeId.Trim(), out var fromCard))
            {
                return (
                    fromCard.Name?.Trim() ?? string.Empty,
                    OneCEmployeeListParser.FormatSnils(fromCard.Snils),
                    fromCard.EmploymentDate?.Date ?? assigned.AssignedFrom?.Date);
            }
        }

        // Нет ФИО в штате по закреплению — если в подразделении ровно один сотрудник
        // с этой профессией, подставляем его СНИЛС из 1С.
        var wantProf = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);
        var byProfession = employees.Values
            .Where(e => e.IsActive)
            .Where(e =>
                string.Equals(
                    ProfessionMatcher.Normalize(e.Profession),
                    wantProf,
                    StringComparison.OrdinalIgnoreCase))
            .Where(e =>
                string.IsNullOrWhiteSpace(workplace.Department) ||
                string.IsNullOrWhiteSpace(e.Department) ||
                string.Equals(e.Department, workplace.Department, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byProfession.Count == 1)
        {
            var only = byProfession[0];
            return (
                string.IsNullOrWhiteSpace(name) ? only.Name.Trim() : name,
                OneCEmployeeListParser.FormatSnils(only.Snils),
                only.EmploymentDate?.Date ?? assigned.AssignedFrom?.Date);
        }

        return (name, string.Empty, assigned.AssignedFrom?.Date);
    }

    private static Dictionary<string, (string Employee, DateTime? AssignedFrom)> BuildAssignmentIndex(
        IReadOnlyList<WorkplaceAssignmentDocument> assignments,
        string organization)
    {
        var map = new Dictionary<string, (string Employee, DateTime? AssignedFrom)>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in assignments.Where(d =>
                     string.IsNullOrWhiteSpace(organization) ||
                     string.Equals(d.Organization, organization, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in doc.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Employee))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line.WorkplaceId) && !map.ContainsKey(line.WorkplaceId))
                {
                    map[line.WorkplaceId] = (line.Employee, line.AssignedFrom);
                }

                if (!string.IsNullOrWhiteSpace(line.WorkplaceNumber))
                {
                    var key = "num:" + line.WorkplaceNumber.Trim();
                    if (!map.ContainsKey(key))
                    {
                        map[key] = (line.Employee, line.AssignedFrom);
                    }
                }
            }
        }

        return map;
    }

    private static List<SoutStatusTreeLine> BuildTreeLines(
        string organization,
        IReadOnlyList<SoutStatusRow> rows,
        IReadOnlyList<WorkplaceRecord> workplaces)
    {
        var lines = new List<SoutStatusTreeLine>();
        if (rows.Count == 0)
        {
            return lines;
        }

        var workplacesById = workplaces
            .Where(w => !string.IsNullOrWhiteSpace(w.Id))
            .GroupBy(w => w.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var orgLabel = string.IsNullOrWhiteSpace(organization) ? "Организация" : organization.Trim();
        var orgId = "org:" + orgLabel;
        lines.Add(new SoutStatusTreeLine
        {
            Id = orgId,
            ParentId = null,
            Level = 0,
            IsGroup = true,
            Label = orgLabel
        });

        foreach (var deptGroup in rows.GroupBy(r =>
                     string.IsNullOrWhiteSpace(r.Department) ? "Без подразделения" : r.Department.Trim(),
                 StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var deptId = orgId + "/dept:" + deptGroup.Key;
            lines.Add(new SoutStatusTreeLine
            {
                Id = deptId,
                ParentId = orgId,
                Level = 1,
                IsGroup = true,
                Label = deptGroup.Key
            });

            foreach (var profGroup in deptGroup.GroupBy(r =>
                         string.IsNullOrWhiteSpace(r.Profession)
                             ? (string.IsNullOrWhiteSpace(r.WorkplaceName) ? "Без профессии" : r.WorkplaceName.Trim())
                             : r.Profession.Trim(),
                     StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var profId = deptId + "/prof:" + profGroup.Key;
                lines.Add(new SoutStatusTreeLine
                {
                    Id = profId,
                    ParentId = deptId,
                    Level = 2,
                    IsGroup = true,
                    Label = profGroup.Key
                });

                foreach (var personGroup in profGroup.GroupBy(r =>
                             string.IsNullOrWhiteSpace(r.EmployeeName) ? "Без закрепления" : r.EmployeeName.Trim(),
                         StringComparer.CurrentCultureIgnoreCase)
                             .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var personRows = personGroup.ToList();
                    var linkedIds = personRows
                        .Select(r => r.LinkedSoutCardWorkplaceId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    // Если Linked ещё пуст, но среди WorkplaceIds есть РМ-карта с этим сотрудником —
                    // подставляем её (после закрепления на карте СОУТ).
                    var linkedCardId = linkedIds.Count == 1
                        ? linkedIds[0]
                        : ResolvePersonLinkedCardId(personRows, workplacesById, personGroup.Key);
                    var workplaceIds = personRows
                        .Select(r => r.WorkplaceId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (!string.IsNullOrWhiteSpace(linkedCardId) &&
                        !workplaceIds.Contains(linkedCardId, StringComparer.OrdinalIgnoreCase))
                    {
                        workplaceIds.Add(linkedCardId);
                    }
                    var employeeKey = ResolveEmployeeFamiliarizationKey(personRows, personGroup.Key);
                    var familiarized = ResolveSoutFamiliarized(
                        workplacesById,
                        linkedCardId,
                        workplaceIds,
                        employeeKey,
                        personGroup.Key);
                    var personId = profId + "/person:" + personGroup.Key;
                    lines.Add(new SoutStatusTreeLine
                    {
                        Id = personId,
                        ParentId = profId,
                        Level = 3,
                        IsGroup = true,
                        IsPerson = true,
                        Label = personGroup.Key,
                        Organization = orgLabel,
                        Department = deptGroup.Key,
                        Profession = profGroup.Key,
                        LinkedSoutCardWorkplaceId = linkedCardId,
                        WorkplaceIds = workplaceIds,
                        EmployeeKey = employeeKey,
                        IsFamiliarized = familiarized
                    });

                    foreach (var row in personRows
                                 .Where(r => !string.IsNullOrWhiteSpace(r.WorkplaceId))
                                 .OrderBy(r => r.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
                                 .ThenBy(r => r.WorkplaceName, StringComparer.CurrentCultureIgnoreCase))
                    {
                        var label = string.IsNullOrWhiteSpace(row.WorkplaceNumber)
                            ? row.WorkplaceName
                            : $"№ {row.WorkplaceNumber} — {row.WorkplaceName}";
                        if (row.IsAnalogous)
                        {
                            label += " (аналогичные)";
                        }

                        lines.Add(new SoutStatusTreeLine
                        {
                            Id = "wp:" + row.WorkplaceId,
                            ParentId = personId,
                            Level = 4,
                            IsGroup = false,
                            Label = label,
                            Row = row
                        });
                    }
                }
            }
        }

        return lines;
    }

    public static string ResolveEmployeeFamiliarizationKey(IReadOnlyList<SoutStatusRow> personRows, string employeeName)
    {
        foreach (var row in personRows)
        {
            var snils = OneCEmployeeListParser.NormalizeSnils(row.Snils);
            if (snils.Length >= 11)
            {
                return snils;
            }
        }

        return "name:" + (employeeName ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Запасной путь: Linked пуст, но РМ человека — карта с закреплёнными сотрудниками
    /// и СНИЛС/ФИО есть в ExtraLines 021 или в списке EmployeeIds (через совпадение СНИЛС в подписи не требуется —
    /// достаточно одного РМ с непустым EmployeeIds, если оно единственное у человека).
    /// </summary>
    private static string ResolvePersonLinkedCardId(
        IReadOnlyList<SoutStatusRow> personRows,
        IReadOnlyDictionary<string, WorkplaceRecord> workplacesById,
        string employeeName)
    {
        var candidates = new List<string>();
        foreach (var row in personRows)
        {
            if (string.IsNullOrWhiteSpace(row.WorkplaceId) ||
                !workplacesById.TryGetValue(row.WorkplaceId, out var wp))
            {
                continue;
            }

            var snilsFormatted = OneCEmployeeListParser.FormatSnils(row.Snils);
            var snilsNorm = OneCEmployeeListParser.NormalizeSnils(row.Snils);
            var line021 = string.Empty;
            if (wp.SoutCard?.ExtraLines is not null &&
                wp.SoutCard.ExtraLines.TryGetValue("021", out var existing021))
            {
                line021 = existing021 ?? string.Empty;
            }

            var listedBySnils =
                (!string.IsNullOrWhiteSpace(snilsFormatted) &&
                 line021.Contains(snilsFormatted, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(snilsNorm) && snilsNorm.Length >= 11 &&
                    line021.Replace(" ", "").Replace("-", "")
                        .Contains(snilsNorm, StringComparison.OrdinalIgnoreCase));

            var listedByName = !string.IsNullOrWhiteSpace(employeeName) &&
                               line021.Contains(employeeName.Trim(), StringComparison.OrdinalIgnoreCase);

            // Только явное закрепление на карте (стр. 021 — СНИЛС или ФИО).
            if (listedBySnils || listedByName)
            {
                candidates.Add(wp.Id);
            }
        }

        var distinct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : string.Empty;
    }

    public static bool IsSoutFamiliarized(WorkplaceRecord? workplace, string employeeKey, string? employeeName)
    {
        var list = workplace?.SoutFamiliarizations;
        if (list is null || list.Count == 0)
        {
            return false;
        }

        foreach (var item in list)
        {
            if (!string.IsNullOrWhiteSpace(employeeKey)
                && string.Equals(item.EmployeeKey, employeeKey, StringComparison.OrdinalIgnoreCase)
                && item.IsFamiliarized)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(employeeName)
                && string.Equals(item.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase)
                && item.IsFamiliarized)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResolveSoutFamiliarized(
        IReadOnlyDictionary<string, WorkplaceRecord> workplacesById,
        string linkedCardId,
        IReadOnlyList<string> workplaceIds,
        string employeeKey,
        string employeeName)
    {
        if (!string.IsNullOrWhiteSpace(linkedCardId)
            && workplacesById.TryGetValue(linkedCardId, out var card)
            && IsSoutFamiliarized(card, employeeKey, employeeName))
        {
            return true;
        }

        foreach (var id in workplaceIds)
        {
            if (workplacesById.TryGetValue(id, out var wp) && IsSoutFamiliarized(wp, employeeKey, employeeName))
            {
                return true;
            }
        }

        return false;
    }
}
