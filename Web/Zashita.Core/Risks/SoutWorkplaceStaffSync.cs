using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Создание / дополнение рабочих мест СОУТ из штата (выгрузка 1С).
/// Одна строка на подразделение+профессию (без «двоения»).
/// </summary>
public static class SoutWorkplaceStaffSync
{
    /// <summary>
    /// Очищает перечень РМ организации и заново строит его из штата 1С.
    /// Одна строка на подразделение+профессию; доп. карты с данными СОУТ (после импорта) сохраняются.
    /// </summary>
    public static int ReplaceFromStaff(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        WorkplaceStore workplaces)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var previousLeaves = workplaces.LoadByOrganization(org)
            .Where(w => !w.IsGroup)
            .ToList();

        var previousPools = previousLeaves
            .GroupBy(w => (
                Department: string.IsNullOrWhiteSpace(w.Department) ? "Без подразделения" : w.Department.Trim(),
                Profession: ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)),
                new DeptProfComparer())
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.WorkplaceNumber, StringComparer.OrdinalIgnoreCase).ToList(),
                new DeptProfComparer());

        var staffGroups = BuildActiveStaffRows(org, employees)
            .GroupBy(r => (r.Department, r.Profession), new DeptProfComparer())
            .ToList();

        var built = new List<WorkplaceRecord>();
        var groupsByDept = new Dictionary<string, WorkplaceRecord>(StringComparer.OrdinalIgnoreCase);
        var nextNumber = 1;
        var usedPriorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var staffGroup in staffGroups)
        {
            var dept = staffGroup.Key.Department;
            var profession = staffGroup.Key.Profession;
            var staffForKey = staffGroup.ToList();

            if (!groupsByDept.TryGetValue(dept, out var folder))
            {
                folder = new WorkplaceRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Organization = org,
                    IsGroup = true,
                    Name = dept,
                    Department = dept,
                    IsUsed = true
                };
                built.Add(folder);
                groupsByDept[dept] = folder;
            }

            previousPools.TryGetValue((dept, profession), out var pool);
            pool ??= [];

            var primaryPrior = TakePrimaryPrior(pool, usedPriorIds, staffForKey);
            var number = !string.IsNullOrWhiteSpace(primaryPrior?.WorkplaceNumber)
                ? primaryPrior!.WorkplaceNumber.Trim()
                : nextNumber.ToString("00000000");
            if (int.TryParse(new string(number.Where(char.IsDigit).ToArray()), out var parsed)
                && parsed >= nextNumber)
            {
                nextNumber = parsed + 1;
            }
            else
            {
                nextNumber++;
            }

            var leaf = BuildLeafFromPrior(
                primaryPrior,
                org,
                folder.Id,
                profession,
                dept,
                number,
                workerCount: staffForKey.Count);
            foreach (var row in staffForKey)
            {
                EnsureEmployeeOnCard(leaf, row.EmployeeId);
            }

            built.Add(leaf);

            // Доп. карты той же профессии с данными СОУТ (импорт) — не выбрасываем.
            foreach (var extra in pool.Where(w =>
                         !usedPriorIds.Contains(w.Id) && LooksLikeImportedSoutCard(w)))
            {
                usedPriorIds.Add(extra.Id);
                var extraNumber = !string.IsNullOrWhiteSpace(extra.WorkplaceNumber)
                    ? extra.WorkplaceNumber.Trim()
                    : nextNumber.ToString("00000000");
                if (int.TryParse(new string(extraNumber.Where(char.IsDigit).ToArray()), out var extraParsed)
                    && extraParsed >= nextNumber)
                {
                    nextNumber = extraParsed + 1;
                }
                else
                {
                    nextNumber++;
                }

                var extraLeaf = BuildLeafFromPrior(
                    extra,
                    org,
                    folder.Id,
                    profession,
                    dept,
                    extraNumber,
                    workerCount: 1);
                built.Add(extraLeaf);
            }
        }

        workplaces.SaveForOrganization(org, built);
        return built.Count(w => !w.IsGroup);
    }

    private static WorkplaceRecord? TakePrimaryPrior(
        List<WorkplaceRecord> pool,
        HashSet<string> usedPriorIds,
        List<StaffRow> staffForKey)
    {
        foreach (var row in staffForKey)
        {
            var byEmployee = pool.FirstOrDefault(w =>
                !usedPriorIds.Contains(w.Id)
                && (w.SoutCard?.EmployeeIds ?? []).Any(id =>
                    string.Equals(id, row.EmployeeId, StringComparison.OrdinalIgnoreCase)));
            if (byEmployee is not null)
            {
                usedPriorIds.Add(byEmployee.Id);
                return byEmployee;
            }
        }

        var withSout = pool.FirstOrDefault(w => !usedPriorIds.Contains(w.Id) && LooksLikeImportedSoutCard(w));
        if (withSout is not null)
        {
            usedPriorIds.Add(withSout.Id);
            return withSout;
        }

        var next = pool.FirstOrDefault(w => !usedPriorIds.Contains(w.Id));
        if (next is not null)
        {
            usedPriorIds.Add(next.Id);
        }

        return next;
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

    private static WorkplaceRecord BuildLeafFromPrior(
        WorkplaceRecord? prior,
        string org,
        string folderId,
        string profession,
        string dept,
        string number,
        int workerCount) =>
        new()
            {
                Id = prior?.Id ?? Guid.NewGuid().ToString("N"),
                Organization = org,
            ParentId = folderId,
                IsGroup = false,
                Name = profession,
                Profession = profession,
            Department = dept,
                WorkplaceNumber = number,
                MainWorkplaceNumber = !string.IsNullOrWhiteSpace(prior?.MainWorkplaceNumber)
                    ? prior!.MainWorkplaceNumber
                    : number,
            WorkerCount = workerCount > 0 ? workerCount : 1,
                ConditionsClass = prior is not null && !SoutStatusBuilder.IsSoutCardBlank(prior)
                    ? (prior.ConditionsClass ?? string.Empty)
                    : string.Empty,
                PlanYear = string.IsNullOrWhiteSpace(prior?.PlanYear)
                    ? DateTime.Today.Year.ToString()
                    : prior!.PlanYear,
                OkCode = prior?.OkCode ?? string.Empty,
                IsUsed = prior?.IsUsed ?? true,
                RiskAssessmentDate = prior?.RiskAssessmentDate,
                RiskCardNumber = prior?.RiskCardNumber ?? string.Empty,
                OverallRisk = prior?.OverallRisk ?? string.Empty,
                RiskPeriodicity = prior?.RiskPeriodicity ?? string.Empty,
                RiskAssessmentNotConducted = prior?.RiskAssessmentNotConducted ?? true,
                RiskLines = prior?.RiskLines ?? [],
                SoutAssessmentDate = prior?.SoutAssessmentDate,
                SoutReportNumber = prior?.SoutReportNumber ?? string.Empty,
                LinkedSoutCardWorkplaceId = prior?.LinkedSoutCardWorkplaceId ?? string.Empty,
                SoutCard = prior?.SoutCard ?? new SoutCardDetails(),
                DeclarationOfConformity = prior?.DeclarationOfConformity ?? false,
                RequiresUnscheduledSout = prior?.RequiresUnscheduledSout ?? false,
                RiskFamiliarizations = prior?.RiskFamiliarizations ?? [],
                SoutFamiliarizations = prior?.SoutFamiliarizations ?? [],
                PrintOverrides = prior?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                CreatedAt = prior?.CreatedAt ?? DateTime.UtcNow
        };

    public static int SyncMissingFromStaff(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        WorkplaceStore workplaces)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var all = workplaces.LoadByOrganization(org);
        if (all.Count == 0)
        {
            all = [];
        }

        var consolidated = SoutWorkplaceDepartmentConsolidator.CollapseOrganization(all, org);
        // Ручные / импортированные доп. карты одной профессии не схлопываем —
        // иначе «Создать карту» не сохраняется. Схлопывание только при ReplaceFromStaff.

        var groupsByDept = new Dictionary<string, WorkplaceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in all.Where(w => w.IsGroup))
        {
            var key = SoutWorkplaceDepartmentConsolidator.NormalizeDepartmentName(group.Name, group.Department);
            if (!groupsByDept.ContainsKey(key))
            {
                groupsByDept[key] = group;
            }
        }

        var existingLeaves = all.Where(w => !w.IsGroup).ToList();
        var nextNumber = NextWorkplaceNumber(existingLeaves);
        var added = 0;
        var changed = 0;

        // Штат → строго одна карта на профессию в организации.
        // Доп. карты той же профессии — только импорт Word / «Создать карту» вручную.
        foreach (var staffGroup in BuildActiveStaffRows(org, employees)
                     .GroupBy(r => ProfessionMatcher.CanonicalKey(r.Profession), StringComparer.OrdinalIgnoreCase)
                     .Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
            var staffForKey = staffGroup.ToList();
            // Отображаемое имя — самое частое написание в штате.
            var profession = staffForKey
                .GroupBy(r => r.Profession, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .First();
            // Подразделение для новой карты — самое частое в штате по этой профессии.
            var dept = staffForKey
                .GroupBy(r => r.Department, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "Без подразделения";

            var leaves = existingLeaves
                .Where(w => LeafProfessionMatches(w, profession))
                .OrderByDescending(HasProfessionCardContent)
                .ThenByDescending(ProfessionCardKeepScore)
                .ThenBy(w => w.CreatedAt == default ? DateTime.MaxValue : w.CreatedAt)
                .ThenBy(w => w.WorkplaceNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (leaves.Count == 0)
            {
                if (!groupsByDept.TryGetValue(dept, out var folder))
            {
                folder = new WorkplaceRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Organization = org,
                    IsGroup = true,
                        Name = dept,
                        Department = dept,
                    IsUsed = true
                };
                all.Add(folder);
                    groupsByDept[dept] = folder;
            }

            var number = nextNumber.ToString("00000000");
            nextNumber++;
            var leaf = new WorkplaceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = org,
                ParentId = folder.Id,
                IsGroup = false,
                Name = profession,
                Profession = profession,
                    Department = dept,
                WorkplaceNumber = number,
                MainWorkplaceNumber = number,
                    WorkerCount = staffForKey.Count,
                    ConditionsClass = string.Empty,
                PlanYear = DateTime.Today.Year.ToString(),
                IsUsed = true,
                    RiskAssessmentNotConducted = true,
                    SoutCard = new SoutCardDetails()
            };
                foreach (var row in staffForKey)
                {
                    EnsureEmployeeOnCard(leaf, row.EmployeeId);
                }

            all.Add(leaf);
            existingLeaves.Add(leaf);
            added++;
                continue;
            }

            // Уже есть карта этой профессии — новые из штата НЕ создаём.
            var primary = leaves[0];
            foreach (var row in staffForKey)
            {
                if (FindLeafForEmployee(leaves, row.EmployeeId) is not null)
                {
                    continue;
                }

                EnsureEmployeeOnCard(primary, row.EmployeeId);
                changed++;
            }

            if (primary.WorkerCount != staffForKey.Count && staffForKey.Count > 0)
            {
                primary.WorkerCount = staffForKey.Count;
                primary.ModifiedAt = DateTime.UtcNow;
                changed++;
            }
        }

        // После синка снова убрать пустые оболочки, если успели появиться.
        var shellsRemoved = CollapseBlankProfessionShells(all, org);

        if (added > 0 || changed > 0 || consolidated > 0 || shellsRemoved > 0)
        {
            workplaces.SaveForOrganization(org, all);
        }

        return added;
    }

    /// <summary>
    /// Схлопывает листья одной профессии в одной папке/подразделении и сохраняет.
    /// Правило: одна профессия — одна карта (дубли убираем всегда).
    /// </summary>
    public static int CollapseEmptyProfessionDuplicates(
        string organization,
        WorkplaceStore workplaces) =>
        CollapseEmptyProfessionDuplicates(organization, workplaces, out _);

    /// <summary>
    /// То же, что <see cref="CollapseEmptyProfessionDuplicates(string, WorkplaceStore)"/>,
    /// плюс карта «удалённый id → оставшийся» для правки состава объектов.
    /// </summary>
    public static int CollapseEmptyProfessionDuplicates(
        string organization,
        WorkplaceStore workplaces,
        out Dictionary<string, string> redirectedIds)
    {
        redirectedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var all = workplaces.LoadByOrganization(org).ToList();
        if (all.Count == 0)
        {
            return 0;
        }

        var removed = CollapseProfessionDuplicates(all, org, redirectedIds);
        if (removed > 0)
        {
            workplaces.SaveForOrganization(org, all);
        }

        return removed;
    }

    /// <summary>
    /// Схлопывает дубли одной профессии в папке. Оставляет карту с наибольшим
    /// наполнением (риски/СОУТ/номер), переносит сотрудников и удаляет остальные.
    /// </summary>
    public static int CollapseEmptyProfessionDuplicates(List<WorkplaceRecord> all, string organization) =>
        CollapseProfessionDuplicates(all, organization, null);

    /// <summary>
    /// Убирает пустые оболочки профессии, если рядом уже есть заполненная карта, и сохраняет.
    /// </summary>
    public static int CollapseBlankProfessionShells(
        string organization,
        WorkplaceStore workplaces)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var all = workplaces.LoadByOrganization(org).ToList();
        if (all.Count == 0)
        {
            return 0;
        }

        var removed = CollapseBlankProfessionShells(all, org);
        if (removed > 0)
        {
            workplaces.SaveForOrganization(org, all);
        }

        return removed;
    }

    /// <summary>
    /// Убирает пустые оболочки профессии по всей организации, если уже есть
    /// заполненная карта этой профессии (в любой папке).
    /// Несколько заполненных карт (импорт / «Создать карту») не трогаем.
    /// </summary>
    public static int CollapseBlankProfessionShells(List<WorkplaceRecord> all, string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || all.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        var leafGroups = all
            .Where(w => !w.IsGroup
                        && (string.Equals(
                                (w.Organization ?? string.Empty).Trim(),
                                org,
                                StringComparison.OrdinalIgnoreCase)
                            || OrganizationMatcher.Matches(org, w.Organization ?? string.Empty)
                            || OrganizationMatcher.Matches(w.Organization ?? string.Empty, org)))
            .GroupBy(ProfessionGroupKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        foreach (var group in leafGroups)
        {
            var items = group.ToList();
            var filled = items
                .Where(HasProfessionCardContent)
                .OrderByDescending(ProfessionCardKeepScore)
                .ToList();
            if (filled.Count == 0)
            {
                // Все «пустые» по полям СОУТ — оставляем одну оболочку штата.
                // Ручные карты уже отфильтрованы через IsManualSoutCard → HasProfessionCardContent.
                var keep = items
                    .OrderByDescending(ProfessionCardKeepScore)
                    .ThenBy(w => w.CreatedAt == default ? DateTime.MaxValue : w.CreatedAt)
                    .First();
                foreach (var dead in items.Where(w => !ReferenceEquals(w, keep)))
                {
                    MergeEmployeeIds(keep, dead);
                    all.RemoveAll(w => string.Equals(w.Id, dead.Id, StringComparison.OrdinalIgnoreCase));
                    removed++;
                }

                continue;
            }

            // Есть хотя бы одна заполненная — все пустые оболочки убираем.
            var target = filled[0];
            foreach (var dead in items.Where(w => !HasProfessionCardContent(w)))
            {
                MergeEmployeeIds(target, dead);
                if (dead.WorkerCount > target.WorkerCount)
                {
                    target.WorkerCount = dead.WorkerCount;
                }

                all.RemoveAll(w => string.Equals(w.Id, dead.Id, StringComparison.OrdinalIgnoreCase));
                removed++;
            }
        }

        return removed;
    }

    private static int CollapseProfessionDuplicates(
        List<WorkplaceRecord> all,
        string organization,
        Dictionary<string, string>? redirectedIds)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || all.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        var leafGroups = all
            .Where(w => !w.IsGroup
                        && string.Equals(
                            (w.Organization ?? string.Empty).Trim(),
                            org,
                            StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                w => (
                    Folder: ProfessionFolderKey(w),
                    Profession: ProfessionMatcher.Normalize(
                        string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)),
                new FolderProfComparer())
            .Where(g => g.Count() > 1
                        && !string.IsNullOrWhiteSpace(g.Key.Profession))
            .ToList();

        foreach (var group in leafGroups)
        {
            var items = group
                .OrderByDescending(ProfessionCardKeepScore)
                .ThenBy(w => w.CreatedAt == default ? DateTime.MaxValue : w.CreatedAt)
                .ThenBy(w => w.WorkplaceNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mergeTarget = items[0];
            foreach (var dead in items.Skip(1))
            {
                MergeEmployeeIds(mergeTarget, dead);
                MergeRiskAssessmentIfRicher(mergeTarget, dead);
                if (dead.WorkerCount > mergeTarget.WorkerCount)
                {
                    mergeTarget.WorkerCount = dead.WorkerCount;
                }

                if (string.IsNullOrWhiteSpace(mergeTarget.RiskCardNumber)
                    && !string.IsNullOrWhiteSpace(dead.RiskCardNumber))
                {
                    mergeTarget.RiskCardNumber = dead.RiskCardNumber;
                }

                redirectedIds?.TryAdd(dead.Id, mergeTarget.Id);
                all.RemoveAll(w => string.Equals(w.Id, dead.Id, StringComparison.OrdinalIgnoreCase));
                removed++;
            }

            if (items.Count > 1)
            {
                mergeTarget.ModifiedAt = DateTime.UtcNow;
                // Выровнять profession/name, чтобы дальше не плодились варианты написания.
                var prof = group.Key.Profession;
                if (!string.IsNullOrWhiteSpace(prof))
                {
                    mergeTarget.Profession = prof;
                    if (string.IsNullOrWhiteSpace(mergeTarget.Name)
                        || ProfessionMatcher.Normalize(mergeTarget.Name) == prof)
                    {
                        mergeTarget.Name = prof;
                    }
                }
            }
        }

        return removed;
    }

    /// <summary>Ключ папки: ParentId, иначе подразделение — чтобы дубли в одной ветке схлопнулись.</summary>
    private static string ProfessionFolderKey(WorkplaceRecord w)
    {
        if (!string.IsNullOrWhiteSpace(w.ParentId))
        {
            return "p:" + w.ParentId.Trim();
        }

        var dept = string.IsNullOrWhiteSpace(w.Department) ? "Без подразделения" : w.Department.Trim();
        return "d:" + dept;
    }

    private static int ProfessionCardKeepScore(WorkplaceRecord w)
    {
        var score = 0;
        if (HasProfessionCardContent(w))
        {
            score += 1000;
        }

        score += (w.RiskLines?.Count(l =>
            !string.IsNullOrWhiteSpace(l.HazardTitle)
            || !string.IsNullOrWhiteSpace(l.HazardNumber)
            || !string.IsNullOrWhiteSpace(l.ControlMeasures)) ?? 0) * 10;

        if (w.RiskAssessmentDate is not null)
        {
            score += 50;
        }

        if (!string.IsNullOrWhiteSpace(w.RiskCardNumber))
        {
            score += 40;
        }

        if (LooksLikeImportedSoutCard(w))
        {
            score += 30;
        }

        score += Math.Min(Math.Max(w.WorkerCount, 0), 100);
        return score;
    }

    /// <summary>Если у цели пустая оценка рисков, а у дубля есть данные — переносим.</summary>
    private static void MergeRiskAssessmentIfRicher(WorkplaceRecord target, WorkplaceRecord source)
    {
        var targetHas = (target.RiskLines?.Count(l =>
            !string.IsNullOrWhiteSpace(l.HazardTitle)
            || !string.IsNullOrWhiteSpace(l.HazardNumber)
            || !string.IsNullOrWhiteSpace(l.ControlMeasures)) ?? 0) > 0;
        var sourceHas = (source.RiskLines?.Count(l =>
            !string.IsNullOrWhiteSpace(l.HazardTitle)
            || !string.IsNullOrWhiteSpace(l.HazardNumber)
            || !string.IsNullOrWhiteSpace(l.ControlMeasures)) ?? 0) > 0;

        if (!targetHas && sourceHas)
        {
            target.RiskLines = source.RiskLines ?? [];
            target.RiskAssessmentDate = source.RiskAssessmentDate;
            target.OverallRisk = source.OverallRisk ?? target.OverallRisk;
            target.RiskPeriodicity = source.RiskPeriodicity ?? target.RiskPeriodicity;
            target.RiskAssessmentNotConducted = source.RiskAssessmentNotConducted;
            target.RiskTrafficMark = source.RiskTrafficMark ?? target.RiskTrafficMark;
            target.RiskFamiliarizations = source.RiskFamiliarizations ?? target.RiskFamiliarizations;
        }

        if ((target.ObjectAssessments is null || target.ObjectAssessments.Count == 0)
            && source.ObjectAssessments is { Count: > 0 })
        {
            target.ObjectAssessments = source.ObjectAssessments;
        }
        else if (source.ObjectAssessments is { Count: > 0 })
        {
            target.ObjectAssessments ??= new Dictionary<string, WorkplaceRiskAssessmentData>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in source.ObjectAssessments)
            {
                if (!target.ObjectAssessments.ContainsKey(key))
                {
                    target.ObjectAssessments[key] = value;
                }
            }
        }
    }

    private static bool HasProfessionCardContent(WorkplaceRecord w)
    {
        // Ручная «Создать карту» / доп. карта — не оболочка штата, не схлопываем.
        if (w.IsManualSoutCard)
        {
            return true;
        }

        if (LooksLikeImportedSoutCard(w))
        {
            return true;
        }

        // Дата / № карты СОУТ / нестандартный класс — уже «живая» карта, не оболочка из штата.
        if (w.SoutAssessmentDate is not null
            || !string.IsNullOrWhiteSpace(w.SoutReportNumber)
            || (!string.IsNullOrWhiteSpace(w.ConditionsClass)
                && !string.Equals(
                    SoutConditionsClassStore.ExtractSubclassCode(w.ConditionsClass),
                    "2",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.OkCode))
        {
            return true;
        }

        if (w.RiskAssessmentDate is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.RiskCardNumber))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.OverallRisk))
        {
            return true;
        }

        if ((w.RiskLines?.Count(l =>
                !string.IsNullOrWhiteSpace(l.HazardTitle)
                || !string.IsNullOrWhiteSpace(l.HazardNumber)
                || !string.IsNullOrWhiteSpace(l.ControlMeasures)) ?? 0) > 0)
        {
            return true;
        }

        return false;
    }

    private static void MergeEmployeeIds(WorkplaceRecord target, WorkplaceRecord source)
    {
        foreach (var id in source.SoutCard?.EmployeeIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            target.SoutCard ??= new SoutCardDetails();
            target.SoutCard.EmployeeIds ??= [];
            if (!target.SoutCard.EmployeeIds.Any(x =>
                    string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
            {
                target.SoutCard.EmployeeIds.Add(id);
            }
        }
    }

    /// <summary>
    /// Одна карта на профессию → Чел. = число сотрудников;
    /// несколько карт (после импорта) → у каждой Чел.=1, если так уже заведено.
    /// </summary>
    public static int SyncWorkerCountsFromStaff(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        WorkplaceStore workplaces)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var all = workplaces.LoadByOrganization(org);
        if (all.Count == 0)
        {
            return 0;
        }

        var staffRows = BuildActiveStaffRows(org, employees);
        var byKey = staffRows
            .GroupBy(r => (r.Department, r.Profession), new DeptProfComparer())
            .ToDictionary(g => g.Key, g => g.ToList(), new DeptProfComparer());

        var changed = 0;
        foreach (var group in all.Where(w => !w.IsGroup)
                     .GroupBy(w => (
                         Department: string.IsNullOrWhiteSpace(w.Department) ? "Без подразделения" : w.Department.Trim(),
                         Profession: ProfessionMatcher.Normalize(
                             string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)),
                         new DeptProfComparer()))
        {
            if (!byKey.TryGetValue(group.Key, out var staffForKey) || staffForKey.Count == 0)
            {
                continue;
            }

            var leaves = group.ToList();
            // Несколько карт одной профессии (импорт) — не схлопываем; Чел.=1 у каждой.
            if (leaves.Count >= 2)
            {
                foreach (var leaf in leaves)
                {
                    if (leaf.WorkerCount == 1)
                    {
                        continue;
                    }

                    leaf.WorkerCount = 1;
                    leaf.ModifiedAt = DateTime.UtcNow;
                    changed++;
                }

                continue;
            }

            // Одна карта — численность = активные в штате.
            var count = staffForKey.Count;
            var only = leaves[0];
            if (count > 0 && only.WorkerCount != count)
            {
                only.WorkerCount = count;
                only.ModifiedAt = DateTime.UtcNow;
            changed++;
            }
        }

        if (changed > 0)
        {
            workplaces.SaveForOrganization(org, all);
        }

        return changed;
    }

    public static WorkplaceRecord AddWorkplace(
        string organization,
        WorkplaceStore workplaces,
        string name,
        string profession,
        string department,
        string? workplaceNumber,
        string? conditionsClass)
    {
        var org = organization.Trim();
        var all = workplaces.LoadByOrganization(org).ToList();

        var dept = string.IsNullOrWhiteSpace(department) ? "Без подразделения" : department.Trim();
        var prof = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(profession) ? name : profession);
        var displayName = string.IsNullOrWhiteSpace(name) ? prof : name.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Новое рабочее место";
        }

        var folder = all.FirstOrDefault(w =>
            w.IsGroup
            && string.Equals(
                SoutWorkplaceDepartmentConsolidator.NormalizeDepartmentName(w.Name, w.Department),
                dept,
                StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            folder = new WorkplaceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = org,
                IsGroup = true,
                Name = dept,
                Department = dept,
                IsUsed = true
            };
            all.Add(folder);
        }

        var number = string.IsNullOrWhiteSpace(workplaceNumber)
            ? NextWorkplaceNumber(all.Where(w => !w.IsGroup).ToList()).ToString("00000000")
            : workplaceNumber.Trim();

        // Одна профессия в подразделении — одна карта: не плодим дубли.
        var existingLeaf = all.FirstOrDefault(w =>
            !w.IsGroup && LeafMatches(w, dept, prof));
        if (existingLeaf is not null)
        {
            if (string.IsNullOrWhiteSpace(existingLeaf.WorkplaceNumber)
                && !string.IsNullOrWhiteSpace(number))
            {
                existingLeaf.WorkplaceNumber = number;
                existingLeaf.MainWorkplaceNumber = number;
            }

            existingLeaf.ModifiedAt = DateTime.UtcNow;
            workplaces.SaveForOrganization(org, all);
            return existingLeaf;
        }

        var leaf = new WorkplaceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = org,
            ParentId = folder.Id,
            IsGroup = false,
            Name = displayName,
            Profession = string.IsNullOrWhiteSpace(prof) ? displayName : prof,
            Department = dept,
            WorkplaceNumber = number,
            MainWorkplaceNumber = number,
            WorkerCount = 1,
            ConditionsClass = string.IsNullOrWhiteSpace(conditionsClass) ? "2" : conditionsClass.Trim(),
            PlanYear = DateTime.Today.Year.ToString(),
            IsUsed = true,
            RiskAssessmentNotConducted = true
        };
        var folderIdx = all.FindIndex(w => string.Equals(w.Id, folder.Id, StringComparison.OrdinalIgnoreCase));
        if (folderIdx >= 0)
        {
            all.Insert(folderIdx + 1, leaf);
        }
        else
        {
            all.Insert(0, leaf);
        }

        workplaces.SaveForOrganization(org, all);
        return leaf;
    }

    public static bool DeleteWorkplace(string organization, WorkplaceStore workplaces, string workplaceId)
    {
        var org = organization.Trim();
        var all = workplaces.LoadByOrganization(org);
        var target = all.FirstOrDefault(w => string.Equals(w.Id, workplaceId, StringComparison.Ordinal));
        if (target is null)
        {
            return false;
        }

        all.RemoveAll(w => string.Equals(w.Id, workplaceId, StringComparison.Ordinal));
        workplaces.SaveForOrganization(org, all);
        return true;
    }

    private static List<StaffRow> BuildActiveStaffRows(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees) =>
        employees
            .Where(EmployeeHrStatus.IsEmployed)
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Where(e => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                organization, e.Name, e.Profession))
            .Where(e => !RiskPrintChrome.IsIndividualEntrepreneurProfessionLabel(e.Profession))
            .Select(e => new StaffRow(
                e.Id,
                ProfessionMatcher.Normalize(e.Profession),
                string.IsNullOrWhiteSpace(e.Department) ? "Без подразделения" : e.Department.Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.Profession))
            .Where(x => !RiskPrintChrome.IsIndividualEntrepreneurProfessionLabel(x.Profession))
            .OrderBy(x => x.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static WorkplaceRecord? TakePriorForEmployee(
        Dictionary<(string Department, string Profession), List<WorkplaceRecord>> pools,
        HashSet<string> usedPriorIds,
        StaffRow row)
    {
        if (!pools.TryGetValue((row.Department, row.Profession), out var pool) || pool.Count == 0)
        {
            return null;
        }

        var byEmployee = pool.FirstOrDefault(w =>
            !usedPriorIds.Contains(w.Id)
            && (w.SoutCard?.EmployeeIds ?? []).Any(id =>
                string.Equals(id, row.EmployeeId, StringComparison.OrdinalIgnoreCase)));
        if (byEmployee is not null)
        {
            usedPriorIds.Add(byEmployee.Id);
            return byEmployee;
        }

        var next = pool.FirstOrDefault(w => !usedPriorIds.Contains(w.Id));
        if (next is not null)
        {
            usedPriorIds.Add(next.Id);
        }

        return next;
    }

    private static WorkplaceRecord? FindLeafForEmployee(IEnumerable<WorkplaceRecord> leaves, string employeeId) =>
        leaves.FirstOrDefault(w =>
            (w.SoutCard?.EmployeeIds ?? []).Any(id =>
                string.Equals(id, employeeId, StringComparison.OrdinalIgnoreCase)));

    private static bool HasOtherEmployeeAssigned(WorkplaceRecord leaf, string employeeId)
    {
        var ids = leaf.SoutCard?.EmployeeIds ?? [];
        return ids.Any(id =>
            !string.IsNullOrWhiteSpace(id)
            && !string.Equals(id, employeeId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LeafMatches(WorkplaceRecord leaf, string department, string profession) =>
        LeafProfessionMatches(leaf, profession)
        && string.Equals(
            string.IsNullOrWhiteSpace(leaf.Department) ? "Без подразделения" : leaf.Department.Trim(),
            department,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Совпадение только по профессии — для правила «1 профессия → 1 карта из штата».</summary>
    private static bool LeafProfessionMatches(WorkplaceRecord leaf, string profession)
    {
        var leafProfession = string.IsNullOrWhiteSpace(leaf.Profession) ? leaf.Name : leaf.Profession;
        return ProfessionMatcher.Matches(profession, leafProfession)
               || ProfessionMatcher.Matches(leafProfession, profession);
    }

    private static string ProfessionGroupKey(WorkplaceRecord w) =>
        ProfessionMatcher.CanonicalKey(
                string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession);

    private static void EnsureEmployeeOnCard(WorkplaceRecord workplace, string employeeId)
    {
        // Не пишем EmployeeIds / LinkedSoutCard при синхронизации штата —
        // иначе в отчёте СОУТ «карта» выбирается у всех, кого просто сопоставили с РМ.
        // Закрепление человека — только в карте СОУТ или при выборе карты в отчёте.
        _ = workplace;
        _ = employeeId;
    }

    private static int NextWorkplaceNumber(IReadOnlyList<WorkplaceRecord> leaves)
    {
        var max = 0;
        foreach (var leaf in leaves)
        {
            var digits = new string((leaf.WorkplaceNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > max)
            {
                max = n;
            }
        }

        return max + 1;
    }

    private sealed record StaffRow(string EmployeeId, string Profession, string Department);

    private sealed class DeptProfComparer : IEqualityComparer<(string Department, string Profession)>
    {
        public bool Equals((string Department, string Profession) x, (string Department, string Profession) y) =>
            string.Equals(x.Department, y.Department, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Profession, y.Profession, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Department, string Profession) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Department ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Profession ?? string.Empty));
    }

    private sealed class FolderProfComparer : IEqualityComparer<(string Folder, string Profession)>
    {
        public bool Equals((string Folder, string Profession) x, (string Folder, string Profession) y) =>
            string.Equals(x.Folder, y.Folder, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Profession, y.Profession, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Folder, string Profession) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Folder ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Profession ?? string.Empty));
    }
}
