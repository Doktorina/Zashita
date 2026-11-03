using System.Security.Cryptography;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public static class MedStandardAssignmentBuilder
{
    public static MedStandardRecord? ResolveForEmployee(
        OrganizationEmployee employee,
        IReadOnlyList<MedStandardRecord> standards)
    {
        var matches = ResolveMatchingStandards(employee, standards);
        if (matches.Count == 0)
        {
            return null;
        }

        return matches.Count == 1
            ? matches[0]
            : MergeMatchingStandards(matches);
    }

    public static List<MedStandardRule> ResolveRulesByExamKind(MedStandardRecord standard, string examKind)
    {
        var normalizedExamKind = NormalizeExamType(examKind);
        var rules = (standard.Rules ?? [])
            .Where(r => r.IsActive)
            .Where(r => IsExamKindEnabled(standard, string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind))
            .ToList();

        rules = ExpandRulesForEnabledKinds(standard, rules);

        if (string.IsNullOrWhiteSpace(normalizedExamKind))
        {
            return rules;
        }

        return rules
            .Where(r => string.Equals(
                NormalizeExamType(string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind),
                normalizedExamKind,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Добавляет в список осмотров недостающие пункты из стандартов по профессии сотрудника.
    /// Уже сохранённые записи с пунктом не перезаписываются (кроме пустой периодичности).
    /// Пункты, уже есть в «Периодический», не дублируются в «Психиатрическое» / «Предварительный».
    /// </summary>
    public static List<MedExamRecord> ExpandExamsFromStandards(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<MedExamRecord> exams,
        IReadOnlyList<MedStandardRecord> standards)
    {
        var merged = CollapseDuplicateExams(exams);
        if (standards.Count == 0)
        {
            return merged;
        }

        var byKey = new Dictionary<string, MedExamRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var exam in merged)
        {
            var key = BuildExamKey(exam);
            if (string.IsNullOrWhiteSpace(MedItemNumber.Normalize(exam.ItemNumber)))
            {
                // Пустые пункты не участвуют в ключевом индексе как «занятые» факторы.
                continue;
            }

            byKey.TryAdd(key, exam);
        }

        // Пустые «заглушки» без пункта не блокируют подстановку стандарта.
        var emptyPlaceholders = merged
            .Where(e => string.IsNullOrWhiteSpace(MedItemNumber.Normalize(e.ItemNumber)))
            .ToList();
        var placeholderByPersonKind = BuildPlaceholderIndex(emptyPlaceholders);

        var employeeList = employees as IReadOnlyList<(string Organization, OrganizationEmployee Employee)>
            ?? employees.ToList();
        var standardsByOrg = IndexStandardsByOrganization(
            employeeList.Select(p => p.Organization),
            standards);

        foreach (var (org, employee) in employeeList)
        {
            var name = employee.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!standardsByOrg.TryGetValue(org, out var orgStandards) || orgStandards.Count == 0)
            {
                continue;
            }

            var standard = ResolveForEmployee(employee, orgStandards);
            if (standard is null)
            {
                continue;
            }

            var rules = (standard.Rules ?? [])
                .Where(r => r.IsActive)
                .Where(r => !IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
                .Where(r => IsExamKindEnabled(
                    standard,
                    string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind!))
                .ToList();

            // Флаги вида осмотра на стандарте (Предсменный и т.п.) размножают факторы
            // из периодических правил — отдельно вводить те же пункты не нужно.
            rules = ExpandRulesForEnabledKinds(standard, rules);

            var periodicItemsInRules = new HashSet<string>(
                rules
                    .Where(r => IsStrictPeriodicKind(string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind))
                    .Select(r => MedItemNumber.Normalize(r.ItemNumber))
                    .Where(i => !string.IsNullOrWhiteSpace(i)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rules)
            {
                var item = MedItemNumber.Normalize(rule.ItemNumber);
                if (string.IsNullOrWhiteSpace(item) && string.IsNullOrWhiteSpace(rule.FactorName))
                {
                    continue;
                }

                var examKind = NormalizeExamType(rule.ExamKind);

                // Не плодим те же пункты под предварительный, если они уже есть как периодические.
                if (!string.IsNullOrWhiteSpace(item) &&
                    IsPreliminaryKind(examKind) &&
                    (periodicItemsInRules.Contains(item) ||
                     byKey.ContainsKey(BuildExamKey(org, name, "Периодический", item))))
                {
                    continue;
                }

                var key = BuildExamKey(org, name, examKind, item);
                if (byKey.TryGetValue(key, out var existing))
                {
                    EnrichFromRule(existing, rule, employee);
                    continue;
                }

                var placeholder = TryTakePlaceholder(
                    placeholderByPersonKind,
                    emptyPlaceholders,
                    org,
                    name,
                    examKind);

                if (placeholder is not null)
                {
                    placeholder.ItemNumber = item;
                    EnrichFromRule(placeholder, rule, employee);
                    if (string.IsNullOrWhiteSpace(placeholder.ExamType))
                    {
                        placeholder.ExamType = examKind;
                    }

                    emptyPlaceholders.Remove(placeholder);
                    byKey[key] = placeholder;
                    continue;
                }

                var record = new MedExamRecord
                {
                    Id = StableStandardExamId(org, name, examKind, item),
                    Organization = org,
                    EmployeeName = name,
                    Profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty),
                    Department = employee.Department?.Trim() ?? string.Empty,
                    ExamType = examKind,
                    ExamDate = DateTime.MinValue,
                    Status = string.Empty,
                    ItemNumber = item,
                    Periodicity = MedPeriodicity.ApplyUnder21Override(employee.BirthDate, rule.Periodicity),
                    BirthDate = employee.BirthDate,
                    NextExamDate = null,
                    Conclusion = string.Empty
                };
                merged.Add(record);
                byKey[key] = record;
            }

            // Психиатрия — один раз при приёме по профессии (флаг стандарта), не по факторам.
            EnsurePsychiatricOnceOnHire(merged, byKey, emptyPlaceholders, org, employee, standard);
        }

        // Сначала схлопываем дубли, затем prune — иначе пустая психиатрия
        // может потеряться до проверки HasPsychiatric.
        merged = CollapseDuplicateExams(merged);
        merged = PruneOrphanedStandardExams(merged, employees, standards);
        merged = CollapseDuplicateExams(merged);

        // После prune заново гарантируем профессию-уровневую психиатрию —
        // prune мог убрать факторные строки с пунктом, а «пустая» ещё не была создана.
        byKey = new Dictionary<string, MedExamRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var exam in merged)
        {
            var item = MedItemNumber.Normalize(exam.ItemNumber);
            if (!string.IsNullOrWhiteSpace(item))
            {
                byKey.TryAdd(BuildExamKey(exam), exam);
            }
        }

        emptyPlaceholders = merged
            .Where(e => string.IsNullOrWhiteSpace(MedItemNumber.Normalize(e.ItemNumber)))
            .ToList();

        foreach (var (org, employee) in employeeList)
        {
            if (!standardsByOrg.TryGetValue(org, out var orgStandards) || orgStandards.Count == 0)
            {
                continue;
            }

            var standard = ResolveForEmployee(employee, orgStandards);
            if (standard is null || !standard.HasPsychiatric)
            {
                continue;
            }

            EnsurePsychiatricOnceOnHire(merged, byKey, emptyPlaceholders, org, employee, standard);
        }

        return CollapseDuplicateExams(merged);
    }

    /// <summary>
    /// Убирает из отчёта запланированные строки, которых уже нет в актуальном стандарте
    /// (снятый флаг психиатрии, удалённый пункт, выключенный вид осмотра).
    /// Пройденные осмотры с датой/заключением сохраняются.
    /// </summary>
    public static List<MedExamRecord> PruneOrphanedStandardExams(
        IReadOnlyList<MedExamRecord> exams,
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<MedStandardRecord> standards)
    {
        if (standards.Count == 0)
        {
            return exams.ToList();
        }

        var employeeList = employees as IReadOnlyList<(string Organization, OrganizationEmployee Employee)>
            ?? employees.ToList();
        var standardsByOrg = IndexStandardsByOrganization(
            employeeList.Select(p => p.Organization),
            standards);
        var standardByPerson = new Dictionary<string, MedStandardRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var (org, employee) in employeeList)
        {
            var name = employee.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!standardsByOrg.TryGetValue(org, out var orgStandards) || orgStandards.Count == 0)
            {
                continue;
            }

            var standard = ResolveForEmployee(employee, orgStandards);
            if (standard is null)
            {
                continue;
            }

            standardByPerson[PersonKey(org, name)] = standard;
        }

        var result = new List<MedExamRecord>();
        foreach (var exam in exams)
        {
            var personKey = PersonKey(exam.Organization, exam.EmployeeName);
            if (!standardByPerson.TryGetValue(personKey, out var standard))
            {
                // Нет стандарта на профессию — строки не трогаем.
                result.Add(exam);
                continue;
            }

            if (HasRecordedProgress(exam))
            {
                result.Add(exam);
                continue;
            }

            if (IsPsychiatricKind(exam.ExamType))
            {
                if (!standard.HasPsychiatric)
                {
                    continue;
                }

                // Только одна профессия-уровневая строка без пункта приказа.
                if (!string.IsNullOrWhiteSpace(MedItemNumber.Normalize(exam.ItemNumber)))
                {
                    continue;
                }

                result.Add(exam);
                continue;
            }

            var examKind = NormalizeExamType(exam.ExamType);
            if (!IsExamKindEnabled(standard, examKind))
            {
                continue;
            }

            var item = MedItemNumber.Normalize(exam.ItemNumber);
            if (string.IsNullOrWhiteSpace(item))
            {
                // Пустая заглушка без пункта — оставляем только если стандарт ещё подставит правила позже.
                // Если у стандарта есть правила этого вида, пустышку можно убрать (её заменит Expand).
                var hasRulesForKind = (standard.Rules ?? []).Any(r =>
                    r.IsActive &&
                    !IsPsychiatricKind(r.ExamKind) &&
                    string.Equals(
                        NormalizeExamType(string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind),
                        examKind,
                        StringComparison.OrdinalIgnoreCase));
                if (!hasRulesForKind)
                {
                    result.Add(exam);
                }

                continue;
            }

            var allowed = (standard.Rules ?? [])
                .Where(r => r.IsActive)
                .Where(r => !IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
                .Where(r => IsExamKindEnabled(
                    standard,
                    string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind!))
                .Any(r =>
                    string.Equals(MedItemNumber.Normalize(r.ItemNumber), item, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeExamType(string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind),
                        examKind,
                        StringComparison.OrdinalIgnoreCase));

            // Периодический пункт мог быть сохранён и как предварительный — допускаем, если пункт есть в периодических правилах.
            if (!allowed && IsPreliminaryKind(examKind))
            {
                allowed = (standard.Rules ?? [])
                    .Where(r => r.IsActive && IsStrictPeriodicKind(r.ExamKind))
                    .Any(r => string.Equals(MedItemNumber.Normalize(r.ItemNumber), item, StringComparison.OrdinalIgnoreCase));
            }

            // Внеочередной / предсменный размножаются из периодических правил по флагу стандарта —
            // в Rules их может не быть, иначе prune сразу выкидывает только что созданные строки.
            if (!allowed && IsExtraordinaryKind(examKind) && standard.IsExtraordinary)
            {
                allowed = (standard.Rules ?? [])
                    .Where(r => r.IsActive)
                    .Where(r => !IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
                    .Any(r =>
                        string.Equals(MedItemNumber.Normalize(r.ItemNumber), item, StringComparison.OrdinalIgnoreCase) &&
                        IsStrictPeriodicKind(string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind));
            }

            // Предсменный создаётся из тех же пунктов, что и периодический, при флаге стандарта.
            if (!allowed && IsPreShiftKind(examKind) && standard.IsPreShift)
            {
                allowed = (standard.Rules ?? [])
                    .Where(r => r.IsActive)
                    .Where(r => !IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
                    .Where(r =>
                    {
                        var kind = string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind!;
                        return IsStrictPeriodicKind(kind) || IsPreShiftKind(kind);
                    })
                    .Any(r => string.Equals(MedItemNumber.Normalize(r.ItemNumber), item, StringComparison.OrdinalIgnoreCase));
            }

            if (allowed)
            {
                result.Add(exam);
            }
        }

        return result;
    }

    /// <summary>
    /// Id записей без прогресса, которые нужно удалить из файла после синхронизации со стандартом.
    /// </summary>
    public static List<string> CollectPrunedPersistedIds(
        IReadOnlyList<MedExamRecord> before,
        IReadOnlyList<MedExamRecord> after)
    {
        var keep = new HashSet<string>(
            after.Select(e => e.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        return before
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Where(e => !e.Id.StartsWith("std", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.Id.StartsWith("plist-", StringComparison.OrdinalIgnoreCase))
            .Where(e => !keep.Contains(e.Id))
            .Where(e => !HasRecordedProgress(e))
            .Select(e => e.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool HasRecordedProgress(MedExamRecord exam) =>
        exam.ExamDate.Year >= 1900 ||
        !string.IsNullOrWhiteSpace(exam.Conclusion) ||
        string.Equals(exam.Status, "Пройден", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(exam.Status, "Не пройден", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Если на стандарте включён «Предсменный» / «Внеочередной», те же пункты/факторы,
    /// что в периодическом, автоматически появляются и под этими видами осмотра.
    /// </summary>
    private static List<MedStandardRule> ExpandRulesForEnabledKinds(
        MedStandardRecord standard,
        List<MedStandardRule> rules)
    {
        var derived = new List<MedStandardRule>(rules);

        if (standard.IsPreShift)
        {
            AppendDerivedKindRules(
                derived,
                rules,
                "Предсменный / Предрейсовый",
                IsPreShiftKind,
                "-preshift");
        }

        if (standard.IsExtraordinary)
        {
            AppendDerivedKindRules(
                derived,
                rules,
                "Внеочередной",
                IsExtraordinaryKind,
                "-extra");
        }

        return derived;
    }

    private static void AppendDerivedKindRules(
        List<MedStandardRule> derived,
        List<MedStandardRule> sourceRules,
        string targetExamKind,
        Func<string?, bool> isTargetKind,
        string idSuffix)
    {
        var existingItems = new HashSet<string>(
            derived
                .Where(r => isTargetKind(r.ExamKind))
                .Select(r =>
                {
                    var item = MedItemNumber.Normalize(r.ItemNumber);
                    return string.IsNullOrWhiteSpace(item) ? (r.FactorName ?? string.Empty).Trim() : item;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in sourceRules)
        {
            var kind = string.IsNullOrWhiteSpace(rule.ExamKind) ? "Периодический" : rule.ExamKind!;
            if (!IsStrictPeriodicKind(kind))
            {
                continue;
            }

            var item = MedItemNumber.Normalize(rule.ItemNumber);
            var key = string.IsNullOrWhiteSpace(item) ? (rule.FactorName ?? string.Empty).Trim() : item;
            if (string.IsNullOrWhiteSpace(key) || !existingItems.Add(key))
            {
                continue;
            }

            derived.Add(new MedStandardRule
            {
                Id = rule.Id + idSuffix,
                ItemNumber = rule.ItemNumber,
                FactorName = rule.FactorName,
                ExamKind = targetExamKind,
                Periodicity = string.IsNullOrWhiteSpace(rule.Periodicity)
                    ? MedPeriodicity.OnceAYear
                    : rule.Periodicity,
                IsPsychiatricActivity = false,
                IsActive = true
            });
        }
    }

    private static void EnsurePsychiatricOnceOnHire(
        List<MedExamRecord> merged,
        Dictionary<string, MedExamRecord> byKey,
        List<MedExamRecord> emptyPlaceholders,
        string org,
        OrganizationEmployee employee,
        MedStandardRecord standard)
    {
        if (!standard.HasPsychiatric)
        {
            return;
        }

        var name = employee.Name?.Trim() ?? string.Empty;
        var examKind = "Психиатрическое освидетельствование";
        var existing = merged.FirstOrDefault(e =>
            SamePerson(e.EmployeeName, name) &&
            (OrganizationMatcher.Matches(e.Organization, org) ||
             string.Equals(e.Organization, org, StringComparison.OrdinalIgnoreCase)) &&
            IsPsychiatricKind(e.ExamType));

        if (existing is not null)
        {
            existing.ExamType = examKind;
            existing.Periodicity = MedPeriodicity.OnceOnHire;
            existing.ItemNumber = string.Empty;
            existing.NextExamDate = null;
            if (string.IsNullOrWhiteSpace(existing.Profession))
            {
                existing.Profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
            }

            if (string.IsNullOrWhiteSpace(existing.Department))
            {
                existing.Department = employee.Department?.Trim() ?? string.Empty;
            }

            existing.BirthDate ??= employee.BirthDate;
            return;
        }

        var placeholder = emptyPlaceholders.FirstOrDefault(e =>
            SamePerson(e.EmployeeName, name) &&
            IsPsychiatricKind(e.ExamType));
        if (placeholder is not null)
        {
            placeholder.ExamType = examKind;
            placeholder.Periodicity = MedPeriodicity.OnceOnHire;
            placeholder.ItemNumber = string.Empty;
            placeholder.NextExamDate = null;
            emptyPlaceholders.Remove(placeholder);
            byKey[BuildExamKey(org, name, examKind, string.Empty)] = placeholder;
            return;
        }

        var record = new MedExamRecord
        {
            Id = StableStandardExamId(org, name, examKind, "hire"),
            Organization = org,
            EmployeeName = name,
            Profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty),
            Department = employee.Department?.Trim() ?? string.Empty,
            ExamType = examKind,
            ExamDate = DateTime.MinValue,
            Status = string.Empty,
            ItemNumber = string.Empty,
            Periodicity = MedPeriodicity.OnceOnHire,
            BirthDate = employee.BirthDate,
            NextExamDate = null,
            Conclusion = string.Empty
        };
        merged.Add(record);
        byKey[BuildExamKey(org, name, examKind, string.Empty)] = record;
    }

    /// <summary>
    /// Схлопывает полные дубли (один и тот же пункт/вид у человека) и убирает
    /// предварительный, если тот же пункт уже есть как периодический.
    /// Психиатрия по факторам с пунктом убирается, если есть профессия-уровневая (без пункта)
    /// или периодический с тем же пунктом.
    /// </summary>
    public static List<MedExamRecord> CollapseDuplicateExams(IEnumerable<MedExamRecord> exams)
    {
        var deduped = DeduplicateByExamKey(exams.ToList());
        return DropSecondaryDuplicatesOfPeriodic(deduped);
    }

    /// <summary>
    /// Оставляет одну запись на ключ организация+ФИО+вид+пункт (лучший экземпляр).
    /// </summary>
    public static List<MedExamRecord> DeduplicateByExamKey(List<MedExamRecord> exams)
    {
        var bestByKey = new Dictionary<string, MedExamRecord>(StringComparer.OrdinalIgnoreCase);
        var withoutItem = new List<MedExamRecord>();

        foreach (var exam in exams
                     .OrderByDescending(ExamPreferenceScore)
                     .ThenByDescending(e => e.ExamDate)
                     .ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            exam.ExamType = NormalizeExamType(exam.ExamType);
            exam.ItemNumber = MedItemNumber.Normalize(exam.ItemNumber);

            if (string.IsNullOrWhiteSpace(exam.ItemNumber))
            {
                withoutItem.Add(exam);
                continue;
            }

            var key = BuildExamKey(exam);
            if (!bestByKey.ContainsKey(key))
            {
                bestByKey[key] = exam;
            }
        }

        // Пустые строки без пункта: не больше одной «заглушки» на человека+вид.
        var emptyBest = new Dictionary<string, MedExamRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var exam in withoutItem
                     .OrderByDescending(ExamPreferenceScore)
                     .ThenByDescending(e => e.ExamDate))
        {
            var key = BuildExamKey(exam.Organization, exam.EmployeeName, exam.ExamType, string.Empty);
            emptyBest.TryAdd(key, exam);
        }

        return bestByKey.Values.Concat(emptyBest.Values).ToList();
    }

    /// <summary>
    /// Убирает уже сохранённые дубли: тот же пункт у человека под психиатрией / предварительным,
    /// если он уже есть как периодический.
    /// </summary>
    public static List<MedExamRecord> DropPsychiatricDuplicatesOfPeriodic(List<MedExamRecord> exams) =>
        DropSecondaryDuplicatesOfPeriodic(exams);

    public static List<MedExamRecord> DropSecondaryDuplicatesOfPeriodic(List<MedExamRecord> exams)
    {
        var periodicItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var professionPsych = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exam in exams)
        {
            var person = PersonKey(exam.Organization, exam.EmployeeName);
            if (IsPsychiatricKind(exam.ExamType) &&
                string.IsNullOrWhiteSpace(MedItemNumber.Normalize(exam.ItemNumber)))
            {
                professionPsych.Add(person);
            }

            if (!IsStrictPeriodicKind(exam.ExamType))
            {
                continue;
            }

            var item = MedItemNumber.Normalize(exam.ItemNumber);
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            periodicItems.Add(PersonItemKey(exam.Organization, exam.EmployeeName, item));
        }

        return exams
            .Where(exam =>
            {
                if (IsPreliminaryKind(exam.ExamType))
                {
                var item = MedItemNumber.Normalize(exam.ItemNumber);
                if (string.IsNullOrWhiteSpace(item))
                {
                    return true;
                }

                return !periodicItems.Contains(PersonItemKey(exam.Organization, exam.EmployeeName, item));
                }

                if (IsPsychiatricKind(exam.ExamType))
                {
                    var item = MedItemNumber.Normalize(exam.ItemNumber);
                    // Факторная психиатрия с пунктом без прогресса — лишняя, если есть профессия-уровневая.
                    if (!string.IsNullOrWhiteSpace(item) &&
                        professionPsych.Contains(PersonKey(exam.Organization, exam.EmployeeName)) &&
                        !HasRecordedProgress(exam))
                    {
                        return false;
                    }

                    // Раньше с тем же пунктом, что периодический, психиатрию выкидывали —
                    // из‑за этого дата «уезжала» / строка становилась пустой. Психиатрию оставляем.
                    return true;
                }

                return true;
            })
            .ToList();
    }

    private static bool SamePerson(string? left, string? right) =>
        EmployeeNameMatcher.Matches(left, right);

    private static string NormalizedPersonName(string? name) =>
        EmployeeNameMatcher.Normalize(name);

    private static Dictionary<string, List<MedStandardRecord>> IndexStandardsByOrganization(
        IEnumerable<string> organizations,
        IReadOnlyList<MedStandardRecord> standards)
    {
        var global = standards
            .Where(s => string.IsNullOrWhiteSpace(s.Organization))
            .ToList();
        var dict = new Dictionary<string, List<MedStandardRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in organizations
                     .Where(o => !string.IsNullOrWhiteSpace(o))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var orgSpecific = standards
                .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
                .Where(s =>
                    OrganizationMatcher.Matches(org, s.Organization) ||
                    OrganizationMatcher.Matches(s.Organization, org) ||
                    string.Equals(s.Organization, org, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (global.Count == 0 && orgSpecific.Count == 0)
            {
                dict[org] = [];
                continue;
            }

            var combined = new List<MedStandardRecord>(global.Count + orgSpecific.Count);
            combined.AddRange(global);
            combined.AddRange(orgSpecific);
            dict[org] = combined;
        }

        return dict;
    }

    private static Dictionary<string, MedExamRecord> BuildPlaceholderIndex(IEnumerable<MedExamRecord> placeholders)
    {
        var index = new Dictionary<string, MedExamRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var exam in placeholders)
        {
            var key = PlaceholderKey(
                exam.Organization,
                exam.EmployeeName,
                NormalizeExamType(exam.ExamType));
            index.TryAdd(key, exam);
        }

        return index;
    }

    private static string PlaceholderKey(string? organization, string? employeeName, string examKind) =>
        string.Join("|",
            organization?.Trim() ?? string.Empty,
            NormalizedPersonName(employeeName),
            examKind);

    private static MedExamRecord? TryTakePlaceholder(
        Dictionary<string, MedExamRecord> index,
        List<MedExamRecord> placeholders,
        string org,
        string name,
        string examKind)
    {
        var key = PlaceholderKey(org, name, examKind);
        if (index.Remove(key, out var found))
        {
            placeholders.Remove(found);
            return found;
        }

        return null;
    }

    private static string PersonKey(string? organization, string? employeeName) =>
        string.Join("|",
            organization?.Trim() ?? string.Empty,
            employeeName?.Trim() ?? string.Empty);

    private static int ExamPreferenceScore(MedExamRecord exam)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(exam.Id) &&
            !exam.Id.StartsWith("std", StringComparison.OrdinalIgnoreCase) &&
            !exam.Id.StartsWith("plist-", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(MedItemNumber.Normalize(exam.ItemNumber)))
        {
            score += 20;
        }

        if (exam.ExamDate.Year >= 1900)
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(exam.Conclusion))
        {
            score += 5;
        }

        if (string.Equals(exam.Status, "Пройден", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        return score;
    }

    private static void EnrichFromRule(
        MedExamRecord exam,
        MedStandardRule rule,
        OrganizationEmployee employee)
    {
        if (string.IsNullOrWhiteSpace(exam.Periodicity) ||
            string.Equals(exam.Periodicity.Trim(), "—", StringComparison.Ordinal))
        {
            exam.Periodicity = MedPeriodicity.ApplyUnder21Override(
                employee.BirthDate ?? exam.BirthDate,
                rule.Periodicity);
        }

        if (string.IsNullOrWhiteSpace(exam.Profession))
        {
            exam.Profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(exam.Department))
        {
            exam.Department = employee.Department?.Trim() ?? string.Empty;
        }

        exam.BirthDate ??= employee.BirthDate;
    }

    /// <summary>Учитывает флаги стандарта: психиатрия только если включена и т.д.</summary>
    public static bool IsExamKindEnabled(MedStandardRecord standard, string? examKind)
    {
        var kind = (examKind ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            return standard.IsPeriodic;
        }

        if (IsPsychiatricKind(kind))
        {
            return standard.HasPsychiatric;
        }

        if (IsPreShiftKind(kind))
        {
            return standard.IsPreShift;
        }

        if (IsExtraordinaryKind(kind))
        {
            return standard.IsExtraordinary;
        }

        // Периодический / предварительный и прочее «основное»
        return standard.IsPeriodic;
    }

    public static bool IsPsychiatricKind(string? examKind) =>
        (examKind ?? string.Empty).Contains("психиатр", StringComparison.OrdinalIgnoreCase);

    public static bool IsPreliminaryKind(string? examKind) =>
        (examKind ?? string.Empty).Contains("предвар", StringComparison.OrdinalIgnoreCase);

    public static bool IsPeriodicKind(string? examKind)
    {
        var kind = (examKind ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            return true;
        }

        return IsStrictPeriodicKind(kind) || IsPreliminaryKind(kind);
    }

    public static bool IsStrictPeriodicKind(string? examKind)
    {
        var kind = (examKind ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            return true;
        }

        if (IsPreliminaryKind(kind) || IsPsychiatricKind(kind) || IsPreShiftKind(kind) || IsExtraordinaryKind(kind))
        {
            return false;
        }

        return kind.Contains("период", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeExamType(string? examType)
    {
        var kind = (examType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Периодический";
        }

        if (IsPsychiatricKind(kind))
        {
            return "Психиатрическое освидетельствование";
        }

        if (IsPreShiftKind(kind))
        {
            return "Предсменный / Предрейсовый";
        }

        if (IsExtraordinaryKind(kind))
        {
            return "Внеочередной";
        }

        if (IsPreliminaryKind(kind))
        {
            return "Предварительный";
        }

        if (kind.Contains("период", StringComparison.OrdinalIgnoreCase))
        {
            return "Периодический";
        }

        return kind;
    }

    private static bool IsPreShiftKind(string? examKind) =>
        (examKind ?? string.Empty).Contains("предсмен", StringComparison.OrdinalIgnoreCase) ||
        (examKind ?? string.Empty).Contains("предрейс", StringComparison.OrdinalIgnoreCase);

    public static bool IsExtraordinaryKind(string? examKind) =>
        (examKind ?? string.Empty).Contains("внеочеред", StringComparison.OrdinalIgnoreCase) ||
        (examKind ?? string.Empty).Contains("внепланов", StringComparison.OrdinalIgnoreCase);

    private static string PersonItemKey(string? organization, string? employeeName, string item) =>
        string.Join("|",
            organization?.Trim() ?? string.Empty,
            employeeName?.Trim() ?? string.Empty,
            item);

    public static string BuildExamKey(MedExamRecord exam) =>
        BuildExamKey(exam.Organization, exam.EmployeeName, exam.ExamType, exam.ItemNumber);

    public static string BuildExamKey(string? organization, string? employeeName, string? examType, string? itemNumber) =>
        string.Join("|",
            organization?.Trim() ?? string.Empty,
            employeeName?.Trim() ?? string.Empty,
            NormalizeExamType(examType),
            MedItemNumber.Normalize(itemNumber));

    public static string StableStandardExamId(string org, string name, string examKind, string item)
    {
        var raw = string.Join("|", org.Trim().ToLowerInvariant(), name.Trim().ToLowerInvariant(),
            examKind.Trim().ToLowerInvariant(), item.Trim().ToLowerInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return "std" + hash[..20].ToLowerInvariant();
    }

    private static bool MatchesToken(string standardValue, string employeeValue)
    {
        if (string.IsNullOrWhiteSpace(standardValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(employeeValue))
        {
            return false;
        }

        return SplitProfessionAliases(standardValue)
            .Any(alias => ProfessionMatcher.Matches(alias, employeeValue));
    }

    /// <summary>Профессии в стандарте: через «;», «,» или перевод строки.</summary>
    public static IReadOnlyList<string> SplitProfessionAliases(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var raw in value.Split(
                     [';', ',', '\n', '\r', '|'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var alias in ExpandProfessionAliasVariants(raw))
            {
                if (!result.Contains(alias, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(alias);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// «Водитель (копия)» / «Водитель — копия» должны совпадать со штатной должностью «Водитель»,
    /// иначе флаг психиатрии на копии стандарта не попадает в отчёт МО.
    /// </summary>
    private static IEnumerable<string> ExpandProfessionAliasVariants(string? value)
    {
        var normalized = ProfessionMatcher.Normalize(value);
        if (normalized.Length == 0)
        {
            yield break;
        }

        yield return normalized;

        var stripped = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"\s*[\(\[\{]?\s*копи[яиея](?:\s*\d+)?\s*[\)\]\}]?\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        stripped = System.Text.RegularExpressions.Regex.Replace(
            stripped,
            @"\s*[-–—]\s*копи[яиея](?:\s*\d+)?\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        stripped = System.Text.RegularExpressions.Regex.Replace(
            stripped,
            @"\s*[\(\[\{]?\s*copy(?:\s*\d+)?\s*[\)\]\}]?\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        stripped = ProfessionMatcher.Normalize(stripped);
        if (stripped.Length > 0 &&
            !string.Equals(stripped, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return stripped;
        }
    }

    /// <summary>
    /// Привязка вручную введённой профессии к формулировке из штата 1С.
    /// Если совпадение однозначное — подставляем каноническое название (разряд, «*»).
    /// </summary>
    public static IReadOnlyList<string> ResolveProfessionsAgainstRoster(
        IEnumerable<string> professions,
        IEnumerable<OrganizationEmployee> roster)
    {
        var rosterProfessions = roster
            .Select(e => ProfessionMatcher.Normalize(e.Profession ?? string.Empty))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = new List<string>();
        foreach (var raw in professions
                     .Select(ProfessionMatcher.Normalize)
                     .Where(p => p.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exact = rosterProfessions.FirstOrDefault(rp =>
                string.Equals(rp, raw, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
            {
                resolved.Add(exact);
                continue;
            }

            var matches = rosterProfessions
                .Where(rp => ProfessionMatcher.Matches(raw, rp))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matches.Count == 1)
            {
                resolved.Add(matches[0]);
                continue;
            }

            resolved.Add(raw);
        }

        return resolved
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int SpecificityScore(MedStandardRecord standard, OrganizationEmployee employee)
    {
        var profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
        var score = 0;
        var aliases = CollectStandardProfessionAliases(standard);

        // Точное совпадение после нормализации — сильнее «разряда» / нечёткого.
        if (aliases.Any(a => string.Equals(
                ProfessionMatcher.Normalize(a),
                profession,
                StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
        }
        else if (aliases.Any(a => string.Equals(
                     ProfessionMatcher.CoreKey(a),
                     ProfessionMatcher.CoreKey(profession),
                     StringComparison.OrdinalIgnoreCase)))
        {
            score += 80;
        }
        else if (aliases.Any(a => ProfessionMatcher.Matches(a, profession)))
        {
            score += 40;
        }

        if (!string.IsNullOrWhiteSpace(standard.Department) &&
            MatchesToken(standard.Department, employee.Department?.Trim() ?? string.Empty))
        {
            score += 15;
        }
        else if (string.IsNullOrWhiteSpace(standard.Department))
        {
            score += 5;
        }

        score += Math.Min(20, standard.Rules?.Count ?? 0);
        return score;
    }

    private static List<MedStandardRecord> ResolveMatchingStandards(
        OrganizationEmployee employee,
        IReadOnlyList<MedStandardRecord> standards)
    {
        var profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
        if (standards.Count == 0 || string.IsNullOrWhiteSpace(profession))
        {
            return [];
        }

        return standards
            .Where(s => StandardMatchesEmployee(s, employee))
            .Select(s => new { Standard = s, Score = SpecificityScore(s, employee) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => (x.Standard.Rules?.Count ?? 0))
            .ThenBy(x => x.Standard.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Standard)
            .ToList();
    }

    private static IReadOnlyList<string> CollectStandardProfessionAliases(MedStandardRecord standard)
    {
        var list = new List<string>();
        list.AddRange(SplitProfessionAliases(standard.Profession));
        // Название — только если профессии не указаны (старые записи).
        // Иначе короткое имя («Машинист») раздувает охват до чужих должностей.
        if (list.Count == 0)
        {
            list.AddRange(SplitProfessionAliases(standard.Name));
        }

        return list
            .Select(ProfessionMatcher.Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Сопоставление стандарта с должностью из штата.
    /// Учитывает список профессий стандарта, название, разряды и падежи.
    /// </summary>
    public static bool StandardMatchesEmployee(MedStandardRecord standard, OrganizationEmployee employee)
    {
        var profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
        if (string.IsNullOrWhiteSpace(profession))
        {
            return false;
        }

        var aliases = CollectStandardProfessionAliases(standard);
        if (aliases.Count == 0)
        {
            return false;
        }

        return aliases.Any(alias => ProfessionMatcher.Matches(alias, profession));
    }

    private static MedStandardRecord MergeMatchingStandards(IReadOnlyList<MedStandardRecord> standards)
    {
        var primary = standards[0];
        var seenRuleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedRules = new List<MedStandardRule>();

        foreach (var standard in standards)
        {
            foreach (var rule in standard.Rules ?? [])
            {
                var examKind = NormalizeExamType(string.IsNullOrWhiteSpace(rule.ExamKind) ? "Периодический" : rule.ExamKind);
                var item = MedItemNumber.Normalize(rule.ItemNumber);
                var factor = (rule.FactorName ?? string.Empty).Trim();
                var key = string.IsNullOrWhiteSpace(item)
                    ? $"{examKind}|{factor}"
                    : $"{examKind}|{item}";
                if (!seenRuleKeys.Add(key))
                {
                    continue;
                }

                mergedRules.Add(new MedStandardRule
                {
                    Id = rule.Id,
                    ItemNumber = item,
                    FactorName = factor,
                    ExamKind = examKind,
                    Periodicity = MedPeriodicity.Normalize(rule.Periodicity),
                    IsPsychiatricActivity = rule.IsPsychiatricActivity,
                    IsActive = rule.IsActive
                });
            }
        }

        return new MedStandardRecord
        {
            Id = primary.Id,
            Organization = primary.Organization,
            Name = primary.Name,
            Department = primary.Department,
            Profession = string.Join("; ",
                standards
                    .SelectMany(s => (s.Profession ?? string.Empty)
                        .Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)),
            IsPeriodic = standards.Any(s => s.IsPeriodic),
            IsPreShift = standards.Any(s => s.IsPreShift),
            IsExtraordinary = standards.Any(s => s.IsExtraordinary),
            HasPsychiatric = standards.Any(s => s.HasPsychiatric),
            IsPreliminaryPrimary = standards.Any(s => s.IsPreliminaryPrimary),
            IsPreliminaryRepeat = standards.Any(s => s.IsPreliminaryRepeat),
            AllowsExtraordinaryManual = standards.Any(s => s.AllowsExtraordinaryManual),
            DefaultMedicalOrganizationId = standards
                .Select(s => s.DefaultMedicalOrganizationId?.Trim() ?? string.Empty)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty,
            Comment = standards
                .Select(s => s.Comment?.Trim() ?? string.Empty)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty,
            Rules = mergedRules
        };
    }
}
