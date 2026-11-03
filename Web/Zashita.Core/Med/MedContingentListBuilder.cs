using Zashita.Core.Employees;
using Zashita.Core.Ppe;

namespace Zashita.Core.Med;

public sealed class MedContingentListRow
{
    public const string DefaultFactors = "Отсутствуют";
    public const string DefaultOrder = "-";

    public int RowNumber { get; init; }
    public string Department { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string EnvironmentFactors { get; set; } = DefaultFactors;
    public string Order988n { get; set; } = DefaultOrder;
    public string Order29n { get; set; } = DefaultOrder;
    public int WorkersTotal { get; init; }
    public int WorkersWomen { get; init; }
    public int ExamTotal { get; set; }
    public int ExamWomen { get; set; }
    public int LpuTotal { get; set; }
    public int LpuWomen { get; set; }
    public int PathologyTotal { get; set; }
    public int PathologyWomen { get; set; }
    public int AccidentTotal { get; set; }
    public int AccidentWomen { get; set; }
    public int ExceedLimitTotal { get; set; }
    public int ExceedLimitWomen { get; set; }
    public int OccupationalDiseaseTotal { get; set; }
    public int OccupationalDiseaseWomen { get; set; }
    public int SpecialConditionsTotal { get; set; }
    public int SpecialConditionsWomen { get; set; }
    public int Experience5YearsTotal { get; set; }
    public int Experience5YearsWomen { get; set; }
    public int Age40Total { get; set; }
    public int Age40Women { get; set; }
    public int MissingDataTotal { get; set; }
    public int MissingDataWomen { get; set; }

    /// <summary>Оформление ячеек факторов/пунктов: f0, a0, b0 → CSS.</summary>
    public Dictionary<string, string> CellStyles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string RowKey => MedContingentOverridesStore.RowKey(Department, Profession);

    public string StyleFor(string key) =>
        CellStyles.TryGetValue(key, out var css) && !string.IsNullOrWhiteSpace(css) ? css : string.Empty;
}

public sealed class MedContingentListResult
{
    public IReadOnlyList<MedContingentListRow> Rows { get; init; } = [];
    public MedContingentListRow Totals { get; init; } = new();
}

/// <summary>
/// Перечень контингентов: профессии юрлица из 1С; факторы из стандартов медосмотра
/// (кол. 4 — только текст фактора), пункты 988н/29н; ручные правки в overrides.
/// </summary>
public static class MedContingentListBuilder
{
    public static MedContingentListResult Build(
        IEnumerable<OrganizationEmployee> employees,
        IEnumerable<MedExamRecord> exams,
        IEnumerable<MedFactorNode>? factors = null,
        IEnumerable<MedContingentPathologyOverride>? pathologyOverrides = null,
        IEnumerable<MedStandardRecord>? standards = null)
    {
        _ = exams;
        var overridesByKey = MedContingentOverridesStore.IndexByRow(pathologyOverrides ?? []);
        var factorLeaves = (factors ?? [])
            .Where(f => !f.IsGroup && f.IsActive)
            .ToList();
        var factorByItem = factorLeaves
            .Where(f => !string.IsNullOrWhiteSpace(f.ItemNumber))
            .GroupBy(f => MedItemNumber.Normalize(f.ItemNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var factorByName = factorLeaves
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var standardsList = (standards ?? []).ToList();

        var active = employees
            .Where(e => EmployeeHrStatus.IsEmployed(e) && !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new
            {
                Employee = e,
                Department = string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department.Trim(),
                Profession = NormalizeProfession(e.Profession),
                Female = IsFemale(e.Name)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Profession))
            .ToList();

        var groups = active
            .GroupBy(x => (x.Department, x.Profession))
            .OrderBy(g => g.Key.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var rows = new List<MedContingentListRow>();
        var n = 1;
        foreach (var group in groups)
        {
            var people = group.ToList();
            var workersTotal = people.Count;
            var workersWomen = people.Count(p => p.Female);

            var row = new MedContingentListRow
            {
                RowNumber = n++,
                Department = group.Key.Department,
                Profession = group.Key.Profession,
                EnvironmentFactors = MedContingentListRow.DefaultFactors,
                Order988n = MedContingentListRow.DefaultOrder,
                Order29n = MedContingentListRow.DefaultOrder,
                WorkersTotal = workersTotal,
                WorkersWomen = workersWomen,
                ExamTotal = 0,
                ExamWomen = 0,
                LpuTotal = 0,
                LpuWomen = 0
            };

            var sampleEmployee = people[0].Employee;
            var factorsWereDefault = true;
            var ordersWereDefault = true;

            if (overridesByKey.TryGetValue(row.RowKey, out var ov))
            {
                ApplyOverride(row, ov);
                factorsWereDefault = IsDefaultFactors(row.EnvironmentFactors);
                ordersWereDefault = !MedContingentFactorLines.HasOrderCode(row.Order988n)
                                    && !MedContingentFactorLines.HasOrderCode(row.Order29n);
            }

            // Кол. 4: текст фактора из стандарта по профессии (если ещё «Отсутствуют»).
            // Кол. 5–6: пункты из тех же правил, чтобы select и № 29н совпали.
            if (factorsWereDefault || ordersWereDefault)
            {
                ApplyFactorsFromStandards(
                    row,
                    sampleEmployee,
                    standardsList,
                    factorByItem,
                    factorByName,
                    fillFactors: factorsWereDefault,
                    fillOrders: ordersWereDefault);
            }

            if (IsDefaultFactors(row.EnvironmentFactors)
                && MedContingentFactorLines.HasOrderCode(row.Order988n))
            {
                // Есть пункты, нет текста — добираем названия из справочника факторов.
                FillFactorTextsFromOrderCodes(row, factorByItem);
            }
            else if (!IsDefaultFactors(row.EnvironmentFactors)
                     && !MedContingentFactorLines.HasOrderCode(row.Order988n)
                     && !MedContingentFactorLines.HasOrderCode(row.Order29n))
            {
                // Есть текст факторов, пунктов нет — добираем номера из справочника по названию.
                FillOrderCodesFromFactorTexts(row, factorByName);
            }

            if (overridesByKey.TryGetValue(row.RowKey, out var ov2))
            {
                // Ручные 9–12 из сохранения не затираем автоподстановкой.
                if (ov2.ExamTotal is null)
                {
                    SyncExamFromWorkers(row);
                }

                if (ov2.LpuTotal is null)
                {
                    SyncLpuFromExam(row);
                }
            }
            else
            {
                SyncExamFromWorkers(row);
                SyncLpuFromExam(row);
            }

            rows.Add(row);
        }

        return new MedContingentListResult
        {
            Rows = rows,
            Totals = SumRows(rows)
        };
    }

    /// <summary>
    /// Подставляет факторы стандарта медосмотра по профессии.
    /// В кол. 4 — только название фактора (без номера пункта).
    /// </summary>
    private static void ApplyFactorsFromStandards(
        MedContingentListRow row,
        OrganizationEmployee sampleEmployee,
        IReadOnlyList<MedStandardRecord> standards,
        IReadOnlyDictionary<string, MedFactorNode> factorByItem,
        IReadOnlyDictionary<string, MedFactorNode> factorByName,
        bool fillFactors,
        bool fillOrders)
    {
        if (standards.Count == 0 || (!fillFactors && !fillOrders))
        {
            return;
        }

        var stub = new OrganizationEmployee
        {
            Name = sampleEmployee.Name,
            Profession = row.Profession,
            Department = row.Department
        };
        var standard = MedStandardAssignmentBuilder.ResolveForEmployee(stub, standards);
        if (standard is null)
        {
            return;
        }

        var rules = MedStandardAssignmentBuilder.ResolveRulesByExamKind(standard, "Периодический")
            .Where(r => !MedStandardAssignmentBuilder.IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
            .ToList();
        if (rules.Count == 0)
        {
            rules = (standard.Rules ?? [])
                .Where(r => r.IsActive)
                .Where(r => !MedStandardAssignmentBuilder.IsPsychiatricKind(r.ExamKind) && !r.IsPsychiatricActivity)
                .ToList();
        }

        var factorNames = new List<string>();
        var itemNumbers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            var item = MedItemNumber.Normalize(rule.ItemNumber);
            var name = (rule.FactorName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)
                && !string.IsNullOrWhiteSpace(item)
                && factorByItem.TryGetValue(item, out var byItem))
            {
                name = (byItem.Name ?? string.Empty).Trim();
            }

            if (string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(name))
            {
                if (factorByName.TryGetValue(name, out var byName))
                {
                    item = MedItemNumber.Normalize(byName.ItemNumber);
                }
                else
                {
                    var fuzzy = FindFactorByNameFuzzy(name, factorByName);
                    if (fuzzy is not null)
                    {
                        item = MedItemNumber.Normalize(fuzzy.ItemNumber);
                    }
                }
            }

            // Пункт без названия всё равно учитываем (для «только 4.2.5» → Отсутствуют).
            if (string.IsNullOrWhiteSpace(item) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.Equals(name, MedContingentListRow.DefaultFactors, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var dedupeKey = !string.IsNullOrWhiteSpace(item) ? "i:" + item : "n:" + name;
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            factorNames.Add(name);
            itemNumbers.Add(item);
        }

        if (factorNames.Count == 0 && itemNumbers.Count == 0)
        {
            return;
        }

        // Единственный фактор «п.4.2.5» = вредностей нет → «Отсутствуют».
        if (IsOnlyAbsentFactorPoint(itemNumbers))
        {
            if (fillFactors)
            {
                row.EnvironmentFactors = MedContingentListRow.DefaultFactors;
            }

            if (fillOrders)
            {
                row.Order988n = MedContingentListRow.DefaultOrder;
                row.Order29n = MedContingentListRow.DefaultOrder;
            }

            return;
        }

        if (fillFactors)
        {
            var names = factorNames
                .Where(n => !string.IsNullOrWhiteSpace(n)
                            && !string.Equals(n, MedContingentListRow.DefaultFactors, StringComparison.OrdinalIgnoreCase))
                .ToList();
            row.EnvironmentFactors = names.Count > 0
                ? MedContingentFactorLines.Join(names)
                : MedContingentListRow.DefaultFactors;
        }

        if (fillOrders)
        {
            // Кол. 5 / 6 — номер пункта (без «п.» — так select сопоставляет data-item).
            var codes = itemNumbers
                .Select(MedItemNumber.Normalize)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();
            var joined = MedContingentFactorLines.Join(codes);
            row.Order988n = string.IsNullOrWhiteSpace(joined)
                ? MedContingentListRow.DefaultOrder
                : joined;
            // Кол. 6 (29н) — отображение «п.6.1».
            row.Order29n = string.IsNullOrWhiteSpace(joined)
                ? MedContingentListRow.DefaultOrder
                : MedContingentFactorLines.Join(codes.Select(MedItemNumber.Display));
        }
    }

    private static void FillOrderCodesFromFactorTexts(
        MedContingentListRow row,
        IReadOnlyDictionary<string, MedFactorNode> factorByName)
    {
        var names = MedContingentFactorLines.Split(row.EnvironmentFactors);
        var codes988 = new List<string>();
        var codes29 = new List<string>();
        foreach (var name in names)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0
                || string.Equals(key, MedContingentListRow.DefaultFactors, StringComparison.OrdinalIgnoreCase))
            {
                codes988.Add(MedContingentListRow.DefaultOrder);
                codes29.Add(MedContingentListRow.DefaultOrder);
                continue;
            }

            if (factorByName.TryGetValue(key, out var node)
                && !string.IsNullOrWhiteSpace(node.ItemNumber))
            {
                var item = MedItemNumber.Normalize(node.ItemNumber);
                codes988.Add(item);
                codes29.Add(MedItemNumber.Display(item));
            }
            else
            {
                var hit = FindFactorByNameFuzzy(key, factorByName);
                if (hit is not null)
                {
                    var item = MedItemNumber.Normalize(hit.ItemNumber);
                    codes988.Add(item);
                    codes29.Add(MedItemNumber.Display(item));
                }
                else
                {
                    codes988.Add(MedContingentListRow.DefaultOrder);
                    codes29.Add(MedContingentListRow.DefaultOrder);
                }
            }
        }

        if (codes988.Any(c => MedContingentFactorLines.HasOrderCode(c)))
        {
            row.Order988n = MedContingentFactorLines.Join(codes988);
            row.Order29n = MedContingentFactorLines.Join(codes29);
        }
    }

    /// <summary>Длинный текст из стандарта ↔ короткое имя в справочнике: самое длинное вхождение.</summary>
    private static MedFactorNode? FindFactorByNameFuzzy(
        string key,
        IReadOnlyDictionary<string, MedFactorNode> factorByName)
    {
        if (string.IsNullOrWhiteSpace(key) || factorByName.Count == 0)
        {
            return null;
        }

        return factorByName.Values
            .Where(f => !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.ItemNumber))
            .Select(f =>
            {
                var n = f.Name.Trim();
                var score = key.Contains(n, StringComparison.OrdinalIgnoreCase) ? n.Length
                    : n.Contains(key, StringComparison.OrdinalIgnoreCase) ? key.Length / 2
                    : 0;
                return (Node: f, Score: score);
            })
            .Where(x => x.Score >= 6)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Node)
            .FirstOrDefault();
    }

    /// <summary>Единственный пункт стандарта — 4.2.5 (без вредностей).</summary>
    private static bool IsOnlyAbsentFactorPoint(IReadOnlyList<string> itemNumbers)
    {
        var real = itemNumbers
            .Select(MedItemNumber.Normalize)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return real.Count == 1
               && string.Equals(real[0], "4.2.5", StringComparison.OrdinalIgnoreCase);
    }

    private static void FillFactorTextsFromOrderCodes(
        MedContingentListRow row,
        IReadOnlyDictionary<string, MedFactorNode> factorByItem)
    {
        var codes = MedContingentFactorLines.Split(row.Order988n);
        if (codes.Count == 1 && string.IsNullOrWhiteSpace(codes[0]))
        {
            codes = MedContingentFactorLines.Split(row.Order29n);
        }

        var names = new List<string>();
        foreach (var code in codes)
        {
            var item = MedItemNumber.Normalize(code);
            if (string.IsNullOrWhiteSpace(item) || item is "-" or "—" or "–")
            {
                continue;
            }

            if (factorByItem.TryGetValue(item, out var node)
                && !string.IsNullOrWhiteSpace(node.Name))
            {
                names.Add(node.Name.Trim());
            }
        }

        if (names.Count > 0)
        {
            row.EnvironmentFactors = MedContingentFactorLines.Join(names);
        }
    }

    private static bool IsDefaultFactors(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return t.Length == 0
               || string.Equals(t, MedContingentListRow.DefaultFactors, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyOverride(MedContingentListRow row, MedContingentPathologyOverride ov)
    {
        if (ov.EnvironmentFactors is not null)
        {
            row.EnvironmentFactors = string.IsNullOrWhiteSpace(ov.EnvironmentFactors)
                ? MedContingentListRow.DefaultFactors
                : ov.EnvironmentFactors.Trim();
        }

        if (ov.Order988n is not null)
        {
            row.Order988n = string.IsNullOrWhiteSpace(ov.Order988n)
                ? MedContingentListRow.DefaultOrder
                : ov.Order988n.Trim();
        }

        if (ov.Order29n is not null)
        {
            row.Order29n = string.IsNullOrWhiteSpace(ov.Order29n)
                ? MedContingentListRow.DefaultOrder
                : ov.Order29n.Trim();
        }

        if (ov.ExamTotal is int examTotal)
        {
            row.ExamTotal = examTotal;
        }

        if (ov.ExamWomen is int examWomen)
        {
            row.ExamWomen = examWomen;
        }

        if (ov.LpuTotal is int lpuTotal)
        {
            row.LpuTotal = lpuTotal;
        }

        if (ov.LpuWomen is int lpuWomen)
        {
            row.LpuWomen = lpuWomen;
        }

        row.PathologyTotal = ov.PathologyTotal;
        row.PathologyWomen = ov.PathologyWomen;
        row.AccidentTotal = ov.AccidentTotal;
        row.AccidentWomen = ov.AccidentWomen;
        row.ExceedLimitTotal = ov.ExceedLimitTotal;
        row.ExceedLimitWomen = ov.ExceedLimitWomen;
        row.OccupationalDiseaseTotal = ov.OccupationalDiseaseTotal;
        row.OccupationalDiseaseWomen = ov.OccupationalDiseaseWomen;
        row.SpecialConditionsTotal = ov.SpecialConditionsTotal;
        row.SpecialConditionsWomen = ov.SpecialConditionsWomen;
        row.Experience5YearsTotal = ov.Experience5YearsTotal;
        row.Experience5YearsWomen = ov.Experience5YearsWomen;
        row.Age40Total = ov.Age40Total;
        row.Age40Women = ov.Age40Women;
        row.MissingDataTotal = ov.MissingDataTotal;
        row.MissingDataWomen = ov.MissingDataWomen;
        if (ov.CellStyles is { Count: > 0 })
        {
            row.CellStyles = new Dictionary<string, string>(ov.CellStyles, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Кол. 9–10 (всего подлежащих): 0, если пункты 988н/29н пустые или «-»;
    /// иначе копируются из кол. 7–8 (численность работающих).
    /// </summary>
    public static void SyncExamFromWorkers(MedContingentListRow row)
    {
        if (MedContingentFactorLines.HasOrderCode(row.Order988n) ||
            MedContingentFactorLines.HasOrderCode(row.Order29n))
        {
            row.ExamTotal = row.WorkersTotal;
            row.ExamWomen = row.WorkersWomen;
        }
        else
        {
            row.ExamTotal = 0;
            row.ExamWomen = 0;
        }
    }

    /// <summary>
    /// Кол. 11–12 (в ЛПУ): 0, если пункты 988н/29н пустые или «-»;
    /// иначе копируются из кол. 9–10.
    /// </summary>
    public static void SyncLpuFromExam(MedContingentListRow row)
    {
        if (MedContingentFactorLines.HasOrderCode(row.Order988n) ||
            MedContingentFactorLines.HasOrderCode(row.Order29n))
        {
            row.LpuTotal = row.ExamTotal;
            row.LpuWomen = row.ExamWomen;
        }
        else
        {
            row.LpuTotal = 0;
            row.LpuWomen = 0;
        }
    }

    private static MedContingentListRow SumRows(IReadOnlyList<MedContingentListRow> rows) => new()
    {
        RowNumber = 0,
        Department = "ИТОГО",
        Profession = string.Empty,
        EnvironmentFactors = string.Empty,
        WorkersTotal = rows.Sum(r => r.WorkersTotal),
        WorkersWomen = rows.Sum(r => r.WorkersWomen),
        ExamTotal = rows.Sum(r => r.ExamTotal),
        ExamWomen = rows.Sum(r => r.ExamWomen),
        LpuTotal = rows.Sum(r => r.LpuTotal),
        LpuWomen = rows.Sum(r => r.LpuWomen),
        PathologyTotal = rows.Sum(r => r.PathologyTotal),
        PathologyWomen = rows.Sum(r => r.PathologyWomen),
        AccidentTotal = rows.Sum(r => r.AccidentTotal),
        AccidentWomen = rows.Sum(r => r.AccidentWomen),
        ExceedLimitTotal = rows.Sum(r => r.ExceedLimitTotal),
        ExceedLimitWomen = rows.Sum(r => r.ExceedLimitWomen),
        OccupationalDiseaseTotal = rows.Sum(r => r.OccupationalDiseaseTotal),
        OccupationalDiseaseWomen = rows.Sum(r => r.OccupationalDiseaseWomen),
        SpecialConditionsTotal = rows.Sum(r => r.SpecialConditionsTotal),
        SpecialConditionsWomen = rows.Sum(r => r.SpecialConditionsWomen),
        Experience5YearsTotal = rows.Sum(r => r.Experience5YearsTotal),
        Experience5YearsWomen = rows.Sum(r => r.Experience5YearsWomen),
        Age40Total = rows.Sum(r => r.Age40Total),
        Age40Women = rows.Sum(r => r.Age40Women),
        MissingDataTotal = rows.Sum(r => r.MissingDataTotal),
        MissingDataWomen = rows.Sum(r => r.MissingDataWomen)
    };

    public static string NormalizeProfession(string? profession)
    {
        var value = (profession ?? string.Empty).Trim().TrimEnd('*').Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    public static string NormalizePerson(string name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool IsFemale(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            var patronymic = parts[2];
            if (patronymic.EndsWith("вна", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("чна", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("шна", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("кизы", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("кызы", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (patronymic.EndsWith("вич", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("ьич", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("оглы", StringComparison.OrdinalIgnoreCase) ||
                patronymic.EndsWith("угли", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return PpeSizeReference.InferredFemale(fullName ?? string.Empty);
    }
}
