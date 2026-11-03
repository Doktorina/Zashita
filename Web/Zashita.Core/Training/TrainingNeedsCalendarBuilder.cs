using System.Globalization;

namespace Zashita.Core.Training;

/// <summary>
/// Фильтры потребностей и календарь «вид обучения × месяц» для планирования.
/// </summary>
public static class TrainingNeedsCalendarBuilder
{
    public const string FilterOverdue = "overdue";
    public const string FilterNotConducted = "notconducted";
    public const string FilterExpiring = "expiring";
    public const string FilterTrained = "trained";
    public const string FilterPlanned = "planned";
    public const string FilterAll = "all";

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static readonly string[] MonthLabels =
    [
        "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
        "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
    ];

    /// <summary>overdue | notconducted | expiring | trained | planned | пусто (не потребность).</summary>
    public static string ClassifyNeed(TrainingStatusRow row, DateTime asOf)
    {
        if (string.IsNullOrWhiteSpace(row.Program))
        {
            return string.Empty;
        }

        if (string.Equals(row.Traffic, "grey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Traffic, "gray", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, TrainingConductValues.NotTrainingResult, StringComparison.OrdinalIgnoreCase)
            || TrainingConductValues.IsNotTraining(row.Result))
        {
            return string.Empty;
        }

        if (string.Equals(row.Status, "Просрочено", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Traffic, "red", StringComparison.OrdinalIgnoreCase))
        {
            return FilterOverdue;
        }

        if (string.Equals(row.Status, "Не пройдено", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Traffic, "pink", StringComparison.OrdinalIgnoreCase))
        {
            return FilterNotConducted;
        }

        if (string.Equals(row.Status, "Скоро истекает", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Traffic, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            return FilterExpiring;
        }

        if (TryParseRuDate(row.NextTrainingDate, out var next))
        {
            var days = (next.Date - asOf.Date).Days;
            if (days >= 0 && days <= 30)
            {
                return FilterExpiring;
            }
        }

        return string.Empty;
    }

    public static bool IsTrainedRow(TrainingStatusRow row) =>
        string.Equals(row.Traffic, "green", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.Status, "Пройдено", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesNeedFilter(TrainingStatusRow row, string? filter, DateTime asOf)
    {
        var bucket = ClassifyNeed(row, asOf);
        if (string.IsNullOrWhiteSpace(filter) ||
            string.Equals(filter, FilterAll, StringComparison.OrdinalIgnoreCase))
        {
            // По умолчанию — просрочено + не проведено + истекает 30 дн.
            return bucket is FilterOverdue or FilterNotConducted or FilterExpiring;
        }

        var key = filter.Trim();
        if (string.Equals(key, FilterTrained, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(row.Program)
                   && IsTrainedRow(row)
                   && string.IsNullOrEmpty(bucket);
        }

        if (string.Equals(key, FilterPlanned, StringComparison.OrdinalIgnoreCase))
        {
            // Плановые заявки подставляются отдельно в Planning.
            return false;
        }

        return string.Equals(bucket, key, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<TrainingStatusRow> FilterNeeds(
        IEnumerable<TrainingStatusRow> rows,
        string? filter,
        DateTime asOf) =>
        rows.Where(r => MatchesNeedFilter(r, filter, asOf)).ToList();

    public static string PersonKey(TrainingStatusRow row)
    {
        var person = row.Person?.Trim() ?? string.Empty;
        if (person.Length == 0 || person == "—")
        {
            return string.Empty;
        }

        return string.Join('\u001e', person, row.Department?.Trim() ?? string.Empty, row.Profession?.Trim() ?? string.Empty);
    }

    public static int CountPeople(IEnumerable<TrainingStatusRow> rows, DateTime asOf, string filter)
    {
        IEnumerable<TrainingStatusRow> filtered;
        if (string.Equals(filter, FilterTrained, StringComparison.OrdinalIgnoreCase))
        {
            filtered = rows.Where(row =>
                !string.IsNullOrWhiteSpace(row.Program)
                && IsTrainedRow(row)
                && string.IsNullOrEmpty(ClassifyNeed(row, asOf)));
        }
        else
        {
            filtered = FilterNeeds(rows, filter, asOf);
        }

        return filtered
            .Select(PersonKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static TrainingNeedsCalendarMatrix BuildMatrix(
        IReadOnlyList<TrainingStatusRow> statusRows,
        IReadOnlyList<TrainingKindNode> kinds,
        int planYear,
        DateTime asOf,
        IReadOnlyCollection<string>? selectedPrograms)
    {
        var needRows = statusRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Program))
            .Where(r => MatchesNeedFilter(r, FilterAll, asOf) || HasDueInYear(r, planYear, asOf))
            .ToList();

        var groups = BuildKindGroups(kinds, needRows);
        var selected = NormalizeSelected(selectedPrograms, groups);

        var matrixRows = new List<TrainingNeedsCalendarRow>();
        foreach (var group in groups)
        {
            foreach (var program in group.Programs)
            {
                if (selected.Count > 0 &&
                    !selected.Contains(program, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var counts = new int[12];
                var people = new List<TrainingStatusRow>[12];
                for (var i = 0; i < 12; i++)
                {
                    people[i] = [];
                }

                foreach (var row in needRows.Where(r =>
                             Zashita.Core.Electrical.ElectricalKnowledgeCalculator.MatchesTrainingKindFilter(
                                 r.Program, program)))
                {
                    var month = ResolveDueMonth(row, planYear, asOf);
                    if (month is null)
                    {
                        continue;
                    }

                    var idx = month.Value - 1;
                    counts[idx]++;
                    people[idx].Add(row);
                }

                matrixRows.Add(new TrainingNeedsCalendarRow
                {
                    GroupName = group.GroupName,
                    Program = program,
                    MonthCounts = counts,
                    MonthPeople = people
                });
            }
        }

        return new TrainingNeedsCalendarMatrix
        {
            PlanYear = planYear,
            Groups = groups,
            SelectedPrograms = selected.ToList(),
            Rows = matrixRows
        };
    }

    public static IReadOnlyList<TrainingStatusRow> DrillDown(
        TrainingNeedsCalendarMatrix matrix,
        string? program,
        int month)
    {
        if (string.IsNullOrWhiteSpace(program) || month is < 1 or > 12)
        {
            return [];
        }

        var row = matrix.Rows.FirstOrDefault(r =>
            string.Equals(r.Program, program, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return [];
        }

        return row.MonthPeople[month - 1];
    }

    private static bool HasDueInYear(TrainingStatusRow row, int planYear, DateTime asOf)
    {
        return ResolveDueMonth(row, planYear, asOf) is not null;
    }

    /// <summary>Месяц потребности в выбранном году (1–12) или null.</summary>
    public static int? ResolveDueMonth(TrainingStatusRow row, int planYear, DateTime asOf)
    {
        if (TryParseRuDate(row.NextTrainingDate, out var next))
        {
            if (next.Year == planYear)
            {
                return next.Month;
            }

            // Просрочка прошлых лет → в текущем месяце выбранного года (если год = текущий)
            // или в январе плана.
            if (next.Date < asOf.Date && planYear == asOf.Year)
            {
                return asOf.Month;
            }

            if (next.Year < planYear && planYear == asOf.Year)
            {
                return asOf.Month;
            }

            return null;
        }

        var bucket = ClassifyNeed(row, asOf);
        if (bucket is FilterNotConducted or FilterOverdue)
        {
            if (planYear == asOf.Year)
            {
                return asOf.Month;
            }

            // В будущем плане — январь.
            if (planYear > asOf.Year)
            {
                return 1;
            }
        }

        return null;
    }

    public static IReadOnlyList<TrainingNeedsCalendarKindGroup> GroupKindsBySection(
        IEnumerable<TrainingKindNode> kinds,
        IEnumerable<string>? extraNames = null,
        bool includeElectricalSafetyFilter = false)
    {
        var all = kinds.ToList();
        var groupsById = all
            .Where(k => k.IsGroup && !k.MarkedForDeletion)
            .ToDictionary(k => k.Id, StringComparer.OrdinalIgnoreCase);

        var leaves = all
            .Where(k => !k.IsGroup && !k.MarkedForDeletion && !string.IsNullOrWhiteSpace(k.Name))
            .ToList();

        var sectionOrder = TrainingKindCatalogDefaults.SectionOptions
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

        var groups = leaves
            .GroupBy(k => ResolveSection(k, groupsById), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => sectionOrder.TryGetValue(g.Key, out var index) ? index : 1000)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new TrainingNeedsCalendarKindGroup
            {
                GroupName = g.Key,
                Programs = g.Select(k => k.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            })
            .ToList();

        // extraNames — только явные дополнения (например, ЭБ), не «хвосты» из программ/видов работ.
        var known = groups.SelectMany(g => g.Programs).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphan = (extraNames ?? [])
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0 && !known.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (orphan.Count > 0)
        {
            var other = groups.FirstOrDefault(g =>
                string.Equals(g.GroupName, "Прочее", StringComparison.OrdinalIgnoreCase));
            if (other is null)
            {
                groups.Add(new TrainingNeedsCalendarKindGroup
                {
                    GroupName = "Прочее",
                    Programs = orphan
                });
            }
            else
            {
                other.Programs = other.Programs
                    .Concat(orphan)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
        }

        if (includeElectricalSafetyFilter)
        {
            EnsureElectricalSafetyFilterItem(groups);
        }

        return groups;
    }

    /// <summary>Фильтр планирования/календаря: только справочник «Виды обучения» + пункт ЭБ.</summary>
    public static IReadOnlyList<TrainingNeedsCalendarKindGroup> BuildFilterGroupsFromKinds(
        IEnumerable<TrainingKindNode> kinds) =>
        GroupKindsBySection(kinds, extraNames: null, includeElectricalSafetyFilter: true);

    private static void EnsureElectricalSafetyFilterItem(List<TrainingNeedsCalendarKindGroup> groups)
    {
        const string section = "Электробезопасность";
        var name = Zashita.Core.Electrical.ElectricalKnowledgeCalculator.TrainingFilterProgramName;
        var known = groups.SelectMany(g => g.Programs).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (known.Contains(name))
        {
            return;
        }

        var sectionOrder = TrainingKindCatalogDefaults.SectionOptions
            .Select((n, index) => (n, index))
            .ToDictionary(item => item.n, item => item.index, StringComparer.OrdinalIgnoreCase);

        var group = groups.FirstOrDefault(g =>
            string.Equals(g.GroupName, section, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            groups.Add(new TrainingNeedsCalendarKindGroup
            {
                GroupName = section,
                Programs = [name]
            });
            groups.Sort((a, b) =>
            {
                var ai = sectionOrder.TryGetValue(a.GroupName, out var ax) ? ax : 1000;
                var bi = sectionOrder.TryGetValue(b.GroupName, out var bx) ? bx : 1000;
                var bySection = ai.CompareTo(bi);
                return bySection != 0
                    ? bySection
                    : string.Compare(a.GroupName, b.GroupName, StringComparison.CurrentCultureIgnoreCase);
            });
        }
        else
        {
            group.Programs = group.Programs
                .Append(name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public static string ResolveSection(
        TrainingKindNode node,
        IReadOnlyDictionary<string, TrainingKindNode> groupsById)
    {
        // Свой раздел важнее родительского — иначе смена «Раздел» у вида не видна в фильтре.
        var own = node.Section?.Trim();
        if (!string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        if (!string.IsNullOrWhiteSpace(node.ParentId)
            && groupsById.TryGetValue(node.ParentId, out var parent))
        {
            var parentSection = parent.Section?.Trim();
            if (!string.IsNullOrWhiteSpace(parentSection))
            {
                return parentSection;
            }

            var parentName = parent.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                return parentName;
            }
        }

        return "Прочее";
    }

    private static List<TrainingNeedsCalendarKindGroup> BuildKindGroups(
        IReadOnlyList<TrainingKindNode> kinds,
        IReadOnlyList<TrainingStatusRow> needRows)
    {
        // Только справочник «Виды обучения» (+ пункт ЭБ). Не подмешиваем программы/виды работ из строк отчёта.
        _ = needRows;
        return BuildFilterGroupsFromKinds(kinds).ToList();
    }

    private static HashSet<string> NormalizeSelected(
        IReadOnlyCollection<string>? selected,
        IReadOnlyList<TrainingNeedsCalendarKindGroup> groups)
    {
        var all = groups.SelectMany(g => g.Programs).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected is null || selected.Count == 0)
        {
            return all;
        }

        var set = selected
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(all.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Count > 0 ? set : all;
    }

    public static bool TryParseRuDate(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTime.TryParseExact(
                   text.Trim(),
                   "dd.MM.yyyy",
                   Ru,
                   DateTimeStyles.None,
                   out date)
               || DateTime.TryParse(text, Ru, DateTimeStyles.None, out date);
    }
}

public sealed class TrainingNeedsCalendarKindGroup
{
    public string GroupName { get; set; } = string.Empty;
    public IReadOnlyList<string> Programs { get; set; } = [];
}

public sealed class TrainingNeedsCalendarRow
{
    public string GroupName { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    public int[] MonthCounts { get; set; } = new int[12];
    public List<TrainingStatusRow>[] MonthPeople { get; set; } = new List<TrainingStatusRow>[12];
}

public sealed class TrainingNeedsCalendarMatrix
{
    public int PlanYear { get; set; }
    public IReadOnlyList<TrainingNeedsCalendarKindGroup> Groups { get; set; } = [];
    public IReadOnlyList<string> SelectedPrograms { get; set; } = [];
    public IReadOnlyList<TrainingNeedsCalendarRow> Rows { get; set; } = [];
}
