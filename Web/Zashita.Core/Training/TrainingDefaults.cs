namespace Zashita.Core.Training;

using System.Text.RegularExpressions;

public static class TrainingKindCatalogDefaults
{
    public static List<TrainingKindNode> CreateSeed()
    {
        var gAtt = Group("att", "Аттестации", "Проверка знаний", "Охрана труда");
        var gInstr = Group("instr", "Инструктажи", "Инструктаж", "Охрана труда");
        var gOt = Group("ot", "Обучение по охране труда", "Обучение", "Охрана труда");
        var gIntern = Group("intern", "Стажировки", "Стажировка", "Охрана труда");
        var gFirstAid = Group("fa", "Обучение по оказанию первой помощи", "Обучение", "Первая помощь");
        var gSiz = Group("siz", "Обучение по применению СИЗ", "Обучение", "Охрана труда");
        var gHeight = Group("height", "Обучение по безопасным методам работ на высоте", "Обучение", "Охрана труда");
        var gFire = Group("fire", "Обучение мерам пожарной безопасности", "Обучение", "Пожарная безопасность");
        var gElec = Group("elec", "Обучение по электробезопасности", "Обучение", "Электробезопасность");

        return
        [
            gAtt,
            Item("att-1", gAtt.Id, "Аттестация по охране труда", "Проверка знаний", "1 раз в 3 года", "Охрана труда"),
            Item("att-2", gAtt.Id, "Аттестация по пожарной безопасности", "Проверка знаний", "1 раз в 3 года", "Пожарная безопасность"),
            gInstr,
            Item("instr-1", gInstr.Id, "Вводный инструктаж по охране труда", "Инструктаж", "1 раз", "Охрана труда"),
            Item("instr-2", gInstr.Id, "Инструктаж на рабочем месте", "Инструктаж", "1 раз в год", "Охрана труда"),
            Item("instr-3", gInstr.Id, "Вводный инструктаж ССЕ", "Инструктаж", "1 раз", "Охрана труда"),
            Item("instr-4", gInstr.Id, "Вводный противопожарный инструктаж", "Инструктаж", "1 раз", "Пожарная безопасность"),
            Item("instr-5", gInstr.Id, "Первичный противопожарный инструктаж на рабочем месте", "Инструктаж", "1 раз", "Пожарная безопасность"),
            Item("instr-6", gInstr.Id, "Повторный противопожарный инструктаж на рабочем месте", "Инструктаж", "1 раз в год", "Пожарная безопасность"),
            Item("instr-7", gInstr.Id, "Внеплановый противопожарный инструктаж", "Инструктаж", "по необходимости", "Пожарная безопасность"),
            Item("instr-8", gInstr.Id, "Целевой противопожарный инструктаж", "Инструктаж", "по необходимости", "Пожарная безопасность"),
            gOt,
            Item("ot-1", gOt.Id, "Обучение по охране труда", "Обучение", "1 раз в год", "Охрана труда"),
            Item("ot-2", gOt.Id, "Обучение безопасным методам и приёмам выполнения работ", "Обучение", "1 раз в год", "Охрана труда"),
            gIntern,
            Item("intern-1", gIntern.Id, "Стажировка на рабочем месте", "Стажировка", "при приёме", "Охрана труда"),
            gFirstAid,
            Item("fa-1", gFirstAid.Id, "Обучение по оказанию первой помощи", "Обучение", "1 раз в год", "Первая помощь"),
            gSiz,
            Item("siz-1", gSiz.Id, "Обучение по применению СИЗ", "Обучение", "1 раз в 6 месяцев", "Охрана труда"),
            gHeight,
            Item("h-1", gHeight.Id, "Обучение по безопасным методам работ на высоте", "Обучение", "1 раз в год", "Охрана труда"),
            gFire,
            Item("fire-1", gFire.Id, "Обучение мерам пожарной безопасности", "Обучение", "1 раз в год", "Пожарная безопасность"),
            Item("fire-2", gFire.Id, "Противопожарная безопасность", "Обучение", "1 раз в год", "Пожарная безопасность"),
            Item("fire-3", gFire.Id, "Специалист по пожарной профилактике", "Обучение", "1 раз", "Пожарная безопасность"),
            gElec,
            Item("elec-1", gElec.Id, "Обучение по электробезопасности", "Обучение", "1 раз в год", "Электробезопасность")
        ];
    }

    public static IReadOnlyList<string> TypeOptions { get; } =
    [
        "Обучение",
        "Инструктаж",
        "Проверка знаний",
        "Стажировка",
        "Тренировка",
        "Инструктаж вводный",
        "Инструктаж первичный",
        "Инструктаж повторный"
    ];

    public static IReadOnlyList<string> SectionOptions { get; } =
    [
        "Охрана труда",
        "Пожарная безопасность",
        "Электробезопасность",
        "Первая помощь",
        "Промышленная безопасность"
    ];

    /// <summary>Базовые разделы плюс те, что уже есть в справочнике (для ввода в карточке / datalist).</summary>
    public static IReadOnlyList<string> MergeSectionOptions(IEnumerable<string?> extra)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in SectionOptions)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        foreach (var item in extra
                     .Select(v => (v ?? string.Empty).Trim())
                     .Where(v => v.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase))
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// Разделы, которые реально встречаются в справочнике.
    /// Для фильтра выпадающего списка — без неиспользуемых дефолтов.
    /// </summary>
    public static IReadOnlyList<string> CollectUsedSections(IEnumerable<TrainingKindNode> nodes)
    {
        var all = nodes
            .Where(n => !n.MarkedForDeletion)
            .ToList();
        var groupsById = all
            .Where(k => k.IsGroup && !string.IsNullOrWhiteSpace(k.Id))
            .GroupBy(k => k.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in all)
        {
            var own = (node.Section ?? string.Empty).Trim();
            if (own.Length > 0)
            {
                used.Add(own);
            }

            // Раздел группы, если у элемента пусто.
            if (own.Length == 0
                && !string.IsNullOrWhiteSpace(node.ParentId)
                && groupsById.TryGetValue(node.ParentId, out var parent))
            {
                var parentSection = (parent.Section ?? string.Empty).Trim();
                if (parentSection.Length > 0)
                {
                    used.Add(parentSection);
                }
                else
                {
                    var parentName = (parent.Name ?? string.Empty).Trim();
                    if (parentName.Length > 0)
                    {
                        used.Add(parentName);
                    }
                }
            }
        }

        used.Remove("Прочее");

        var sectionOrder = SectionOptions
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

        return used
            .OrderBy(name => sectionOrder.TryGetValue(name, out var index) ? index : 1000)
            .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Смена раздела у группы — на потомков с пустым разделом или с прежним разделом группы.
    /// </summary>
    public static bool PropagateSectionToDescendants(
        IList<TrainingKindNode> nodes,
        string groupId,
        string newSection,
        string? previousSection)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(newSection))
        {
            return false;
        }

        var changed = false;
        void Walk(string parentId)
        {
            foreach (var child in nodes.Where(n =>
                         string.Equals(n.ParentId, parentId, StringComparison.OrdinalIgnoreCase)))
            {
                var own = (child.Section ?? string.Empty).Trim();
                var shouldUpdate = string.IsNullOrWhiteSpace(own)
                    || (!string.IsNullOrWhiteSpace(previousSection)
                        && own.Equals(previousSection, StringComparison.OrdinalIgnoreCase));

                if (shouldUpdate && !own.Equals(newSection, StringComparison.OrdinalIgnoreCase))
                {
                    child.Section = newSection;
                    changed = true;
                }

                if (child.IsGroup)
                {
                    Walk(child.Id);
                }
            }
        }

        Walk(groupId);
        return changed;
    }

    /// <summary>
    /// Подтягивает раздел от родительской группы, если у вида пусто или стоит устаревшая
    /// «Охрана труда», а группа уже в другом разделе (и имя вида не указывает на свой раздел).
    /// </summary>
    public static bool SyncSectionsFromParentGroups(IList<TrainingKindNode> nodes)
    {
        var byId = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var node in nodes)
        {
            if (node.IsGroup || string.IsNullOrWhiteSpace(node.ParentId))
            {
                continue;
            }

            if (!byId.TryGetValue(node.ParentId, out var parent) || !parent.IsGroup)
            {
                continue;
            }

            var parentSection = (parent.Section ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(parentSection))
            {
                continue;
            }

            var own = (node.Section ?? string.Empty).Trim();
            var inferred = InferSection(node.Name);
            if (inferred is not null
                && !inferred.Equals(parentSection, StringComparison.OrdinalIgnoreCase))
            {
                // У вида свой явный раздел по названию — не перетираем группой.
                continue;
            }

            var shouldSync = string.IsNullOrWhiteSpace(own)
                || (own.Equals("Охрана труда", StringComparison.OrdinalIgnoreCase)
                    && !parentSection.Equals("Охрана труда", StringComparison.OrdinalIgnoreCase));

            if (shouldSync && !own.Equals(parentSection, StringComparison.OrdinalIgnoreCase))
            {
                node.Section = parentSection;
                changed = true;
            }
        }

        return changed;
    }

    public static IReadOnlyList<string> PersonnelCategoryOptions { get; } =
    [
        "Рабочий",
        "Специалист",
        "Руководитель",
        "Член комиссии"
    ];

    /// <summary>Пустая категория в справочнике («Все») подходит для любой категории стандарта.</summary>
    public static bool MatchesPersonnelCategory(string? kindCategory, string? selectedCategory)
    {
        if (string.IsNullOrWhiteSpace(selectedCategory))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(kindCategory))
        {
            return true;
        }

        return kindCategory.Trim().Equals(selectedCategory.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static TrainingKindNode Group(string code, string name, string type, string section) =>
        new()
        {
            Id = $"grp-{code}",
            IsGroup = true,
            Name = name,
            Code = code,
            Type = type,
            Section = section
        };

    private static TrainingKindNode Item(
        string code,
        string parentId,
        string name,
        string type,
        string periodicity,
        string section) =>
        new()
        {
            Id = $"item-{code}",
            ParentId = parentId,
            IsGroup = false,
            Name = name,
            Code = code,
            Type = type,
            Periodicity = periodicity,
            Section = section
        };

    /// <summary>
    /// Проставляет разделы по названию вида/группы, если раздел пустой или устаревший «Охрана труда» у пожарных/ЭБ/первой помощи.
    /// </summary>
    public static bool TryNormalizeSections(IList<TrainingKindNode> nodes)
    {
        var changed = false;
        foreach (var node in nodes)
        {
            var inferred = InferSection(node.Name);
            if (inferred is null)
            {
                continue;
            }

            var current = (node.Section ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(current)
                || (current.Equals("Охрана труда", StringComparison.OrdinalIgnoreCase)
                    && !inferred.Equals("Охрана труда", StringComparison.OrdinalIgnoreCase)))
            {
                if (!current.Equals(inferred, StringComparison.OrdinalIgnoreCase))
                {
                    node.Section = inferred;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static string? InferSection(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        if (n.Contains("пожар") || n.Contains("противопожар"))
        {
            return "Пожарная безопасность";
        }

        if (n.Contains("электробезоп") || n.Contains("электробезопас"))
        {
            return "Электробезопасность";
        }

        if (n.Contains("первой помощи") || n.Contains("первая помощь"))
        {
            return "Первая помощь";
        }

        if (n.Contains("промышленн") && n.Contains("безопас"))
        {
            return "Промышленная безопасность";
        }

        return null;
    }
}

public static class TrainingProgramCoursesDefaults
{
    private static readonly HashSet<string> DemoSeedIds = CreateSeed()
        .Select(node => node.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsDemoSeedId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && DemoSeedIds.Contains(id.Trim());

    public static List<TrainingProgramCourseNode> CreateSeed()
    {
        var gOt = Group("grp-ot", "Охрана труда");
        var gFire = Group("grp-fire", "Пожарная безопасность");
        var gIntern = Group("grp-intern", "Стажировки");

        return
        [
            gOt,
            Item("p1", gOt.Id, "Охрана труда", "Водитель автомобиля", new DateTime(2024, 1, 1), 15m),
            Item("p2", gOt.Id, "Охрана труда", "Обучение по охране труда", new DateTime(2024, 1, 1), 42m),
            Item("p3", gOt.Id, "Охрана труда", "Инструктаж на рабочем месте", new DateTime(2024, 6, 1), 2.5m),
            gFire,
            Item("p4", gFire.Id, "Пожарная безопасность", "Противопожарный инструктаж", new DateTime(2024, 1, 1), 4m),
            Item("p5", gFire.Id, "Пожарная безопасность", "Обучение мерам пожарной безопасности", new DateTime(2023, 12, 15), 8m),
            Item("p7", gFire.Id, "Пожарная безопасность", "Программа вводного противопожарного инструктажа", new DateTime(2024, 1, 1), 2m),
            Item("p8", gFire.Id, "Пожарная безопасность", "Программа первичного противопожарного инструктажа на рабочем месте", new DateTime(2024, 1, 1), 2m),
            Item("p9", gFire.Id, "Пожарная безопасность", "Программа повторного противопожарного инструктажа", new DateTime(2024, 1, 1), 1.5m),
            Item("p11", gFire.Id, "Пожарная безопасность", "Программа внепланового противопожарного инструктажа", new DateTime(2024, 1, 1), 1m),
            Item("p12", gFire.Id, "Пожарная безопасность", "Программа целевого противопожарного инструктажа", new DateTime(2024, 1, 1), 1m),
            Item("p13", gFire.Id, "Пожарная безопасность", "Пожарная безопасность", new DateTime(2024, 1, 1), 8m),
            Item("p14", gFire.Id, "Пожарная безопасность", "Противопожарная безопасность", new DateTime(2024, 1, 1), 8m),
            Item("p10", gFire.Id, "Пожарная безопасность", "Специалист по пожарной профилактике", new DateTime(2023, 6, 1), 72m),
            gIntern,
            Item("p6", gIntern.Id, "Стажировки", "Стажировка на рабочем месте", null, 0.25m)
        ];
    }

    private static TrainingProgramCourseNode Group(string code, string name) =>
        new()
        {
            Id = code,
            IsGroup = true,
            Name = name,
            Code = code,
            Section = name.Contains("Стажир", StringComparison.Ordinal) ? "Стажировки" :
                name.Contains("Пожар", StringComparison.Ordinal) ? "Пожарная безопасность" : "Охрана труда"
        };

    private static TrainingProgramCourseNode Item(string code, string parentId, string section, string name, DateTime? approval, decimal hours) =>
        new()
        {
            Id = $"item-{code}",
            ParentId = parentId,
            IsGroup = false,
            Name = name,
            Code = code,
            ApprovalDate = approval,
            TotalHours = hours,
            Section = section
        };
}

public static class TrainingPlanTypeDefaults
{
    public static List<TrainingPlanTypeNode> CreateSeed()
    {
        var training = Group("obuchenie", "Обучение");
        return
        [
            training,
            Item("annual-2026-d", training.Id, "Годовой план обучения сотрудников на 2026 год(Д)", "/Training/Planned?PlanYear=2026"),
            Item("annual-2026-aln", training.Id, "Годовой план обучения сотрудников на 2026 год_АЛН", "/Training/Planned?PlanYear=2026"),
            Item("instr", training.Id, "Инструктаж", null),
            Item("ot-40", training.Id, "Охрана труда 40 ч", null)
        ];
    }

    private static TrainingPlanTypeNode Group(string code, string name) =>
        new()
        {
            Id = $"grp-{code}",
            IsGroup = true,
            Name = name
        };

    private static TrainingPlanTypeNode Item(string code, string parentId, string name, string? routeUrl) =>
        new()
        {
            Id = $"plan-{code}",
            ParentId = parentId,
            Name = name,
            RouteUrl = routeUrl
        };
}

public static class TrainingSimpleTreeDefaults
{
    public static List<TrainingSimpleTreeNode> CreateSeed(TrainingSimpleTreeKind kind) =>
        kind switch
        {
            TrainingSimpleTreeKind.Topics => TopicsSeed(),
            TrainingSimpleTreeKind.Tests => TestsSeed(),
            TrainingSimpleTreeKind.Questions => QuestionsSeed(),
            TrainingSimpleTreeKind.WorkTypes => WorkTypesSeed(),
            _ => []
        };

    private static TrainingSimpleTreeNode G(string id, string code, string name, bool isGroup, string? parentId) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            IsGroup = isGroup,
            ParentId = parentId
        };

    private static TrainingSimpleTreeNode T(string id, string code, string parentId, string name, decimal? hours) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            IsGroup = false,
            ParentId = parentId,
            Hours = hours
        };

    private static List<TrainingSimpleTreeNode> TopicsSeed()
    {
        var gInstr = G("grp-instr", "t2", "Инструктаж", true, null);
        var gOt = G("grp-ot", "t3", "Охрана труда", true, null);
        var gFire = G("grp-fire", "t4", "Пожарная безопасность", true, null);
        var gEl = G("grp-el", "t7", "Электробезопасность", true, null);
        return
        [
            gInstr,
            T("it1", "01", gInstr.Id, "6.9.5 Аттестация ИТР по лифтам с ссылками на новые НТД", 8m),
            gOt,
            T("it2", "02", gOt.Id, "35 302 2 (пример темы)", 4m),
            gFire,
            T("it4", "04", gFire.Id, "Программа вводного противопожарного инструктажа", 2m),
            T("it5", "05", gFire.Id, "Программа первичного противопожарного инструктажа на рабочем месте", 2m),
            T("it6", "06", gFire.Id, "Программа повторного противопожарного инструктажа", 1.5m),
            T("it7", "07", gFire.Id, "Программа внепланового противопожарного инструктажа", 1m),
            T("it8", "08", gFire.Id, "Программа целевого противопожарного инструктажа", 1m),
            T("it9", "09", gFire.Id, "Обучение мерам пожарной безопасности", 8m),
            T("it10", "10", gFire.Id, "Пожарная безопасность", 8m),
            T("it11", "11", gFire.Id, "Противопожарная безопасность", 8m),
            T("it12", "12", gFire.Id, "Специалист по пожарной профилактике", 72m),
            gEl,
            T("it3", "03", gEl.Id, "ЭБ 302.2 (июнь 2014г) III группа допуска до 1000 В", 16m)
        ];
    }

    private static List<TrainingSimpleTreeNode> TestsSeed()
    {
        var gOt = G("tg3", "e3", "Охрана труда", true, null);
        var gEl = G("tg8", "e8", "Электробезопасность III группа допуска до 1000 В", true, null);
        return
        [
            gOt,
            gEl,
            new TrainingSimpleTreeNode
            {
                Id = "te2",
                Code = "e02",
                Name = "Тест для проверки знаний(Д)",
                IsGroup = false,
                ParentId = gEl.Id,
                ApprovedDate = new DateTime(2025, 5, 13)
            }
        ];
    }

    private static List<TrainingSimpleTreeNode> QuestionsSeed()
    {
        var g = G("qg3", "q3", "Проверка знаний руководителей и специалистов", true, null);
        return
        [
            g,
            new TrainingSimpleTreeNode
            {
                Id = "qi4",
                Code = "04",
                Name = "Что такое охрана труда?",
                Description = "Дайте определение понятия «охрана труда» в соответствии с ТК РФ.",
                IsGroup = false,
                ParentId = g.Id
            }
        ];
    }

    private static List<TrainingSimpleTreeNode> WorkTypesSeed()
    {
        var g1 = G("wg1", "w1", "Акты-допуски", true, null);
        var g2 = G("wg2", "w2", "Виды работ (Приказ Минтруда № 657н)", true, null);
        var g3 = G("wg3", "w3", "Виды работ для хронометража", true, null);
        var g4 = G("wg4", "w4", "Наряды-допуски", true, null);
        var g5 = G("wg5", "w5", "ОКПД", true, null);
        var g6 = G("wg6", "w6", "Работы с повышенной опасностью", true, null);
        var g7 = G("wg7", "w7", "Экология", true, null);
        return
        [
            g1,
            g2,
            W("wi1", "w01", g2.Id, "Архивирование документов"),
            W("wi2", "w02", g2.Id, "Входной контроль"),
            W("wi3", "w03", g2.Id, "Выполнение работ на высоте"),
            W("wi4", "w04", g2.Id, "Малярные работы"),
            W("wi5", "w05", g2.Id, "Работа за компьютером"),
            g3,
            g4,
            g5,
            g6,
            g7
        ];
    }

    private static TrainingSimpleTreeNode W(string id, string code, string parentId, string name) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            IsGroup = false,
            ParentId = parentId
        };
}

public static class TrainingProgramCatalog
{
    public static readonly string[] DefaultPrograms =
    [
        "Вводный по охране труда",
        "Инструктаж на рабочем месте",
        "Инструктаж по охране труда",
        "Целевой инструктаж по охране труда",
        "Вводный противопожарный инструктаж",
        "Первичный противопожарный инструктаж на рабочем месте",
        "Повторный противопожарный инструктаж на рабочем месте",
        "Внеплановый противопожарный инструктаж",
        "Целевой противопожарный инструктаж",
        "Противопожарный инструктаж на рабочем месте",
        "Обучение мерам пожарной безопасности",
        "Аттестация по пожарной безопасности",
        "Противопожарная безопасность",
        "Пожарная безопасность",
        "Специалист по пожарной профилактике",
        "Проверка знаний",
        "Правильное применение СИЗ",
        "Программа обучения по оказанию первой помощи",
        "Обучение по охране труда",
        "Обучение безопасным методам и приёмам выполнения работ",
        "Стажировка на рабочем месте"
    ];

    /// <summary>Вводные / первичные / повторные и др. инструктажи — в архиве обучения не показываем.</summary>
    public static bool IsBriefingProgram(string? programName)
    {
        var n = (programName ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (n.Length == 0)
        {
            return false;
        }

        return n.Contains("инструктаж", StringComparison.Ordinal);
    }

    public static int PeriodMonths(string programName)
    {
        var n = (programName ?? string.Empty).Trim();
        if (n.Contains("6 месяц", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (n.Contains("5 лет", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("5 год", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("промышленн", StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (n.Contains("3 год", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("проверк", StringComparison.OrdinalIgnoreCase))
        {
            return 36;
        }

        if (n.Contains("вводн", StringComparison.OrdinalIgnoreCase))
        {
            return 120;
        }

        return 12;
    }

    public static int PeriodMonthsFromPeriodicity(string? periodicity)
    {
        var n = (periodicity ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (n.Length == 0)
        {
            return 12;
        }

        if (n is "однократно" or "1 раз" or "при приёме" or "при приеме" or "по необходимости"
            || n.Contains("единораз", StringComparison.Ordinal))
        {
            return 120;
        }

        var monthMatch = Regex.Match(n,
            @"(?:раз\s+в\s+)?(\d+|один|два|три|четыре|пять|шесть|семь|восемь|девять|десять|одной|двух|трех|трёх)\s*меся");
        if (monthMatch.Success)
        {
            return Math.Max(1, ParseCountToken(monthMatch.Groups[1].Value) ?? 12);
        }

        if (n.Contains("6 месяц"))
        {
            return 6;
        }

        var yearMatch = Regex.Match(n,
            @"(?:раз\s+в\s+)?(\d+|один|два|три|четыре|пять|шесть|семь|восемь|девять|десять)\s*(?:лет|год)");
        if (yearMatch.Success)
        {
            return Math.Max(1, ParseCountToken(yearMatch.Groups[1].Value) ?? 1) * 12;
        }

        var bareYearMatch = Regex.Match(n, @"раз\s+в\s+(\d+)\b");
        if (bareYearMatch.Success)
        {
            return Math.Max(1, int.Parse(bareYearMatch.Groups[1].Value)) * 12;
        }

        if (n.Contains("ежегодн") || n.Contains("1 раз в год") || n.Contains("раз в год") ||
            (n.Contains("год") && !Regex.IsMatch(n, @"\d\s*год")))
        {
            return 12;
        }

        return 12;
    }

    private static int? ParseCountToken(string token)
    {
        if (int.TryParse(token, out var number))
        {
            return number;
        }

        return WordNumbers.TryGetValue(token.Trim(), out var word) ? word : null;
    }

    private static readonly Dictionary<string, int> WordNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["один"] = 1,
        ["одной"] = 1,
        ["два"] = 2,
        ["двух"] = 2,
        ["три"] = 3,
        ["трех"] = 3,
        ["трёх"] = 3,
        ["четыре"] = 4,
        ["четырёх"] = 4,
        ["пять"] = 5,
        ["пяти"] = 5,
        ["шесть"] = 6,
        ["семь"] = 7,
        ["восемь"] = 8,
        ["девять"] = 9,
        ["десять"] = 10
    };

    public static string FromKindPeriodicity(string? value) => (value ?? string.Empty).Trim();

    /// <summary>
    /// Копия словаря периодичностей без падения на ключах, отличающихся только регистром
    /// (System.Text.Json десериализует без OrdinalIgnoreCase — при сохранении истории это ломало словарь).
    /// </summary>
    public static Dictionary<string, string> CopyPeriodicitiesIgnoreCase(
        IEnumerable<KeyValuePair<string, string>>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var kv in source)
        {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = (kv.Value ?? string.Empty).Trim();
        }

        return result;
    }

    public static string ResolvePeriodicity(string programName, string? kindPeriodicity, string? storedPeriodicity)
    {
        var kind = FromKindPeriodicity(kindPeriodicity);
        if (kind.Length > 0)
        {
            return kind;
        }

        var stored = (storedPeriodicity ?? string.Empty).Trim();
        if (stored.Length > 0)
        {
            return stored;
        }

        return DefaultPeriodicity(programName);
    }

    public static Dictionary<string, string> BuildKindPeriodicityLookup(
        IEnumerable<TrainingKindNode> kinds,
        string? personnelCategory = null) =>
        kinds
            .Where(node => !node.IsGroup && !node.MarkedForDeletion && !string.IsNullOrWhiteSpace(node.Name))
            .Where(node => TrainingKindCatalogDefaults.MatchesPersonnelCategory(node.PersonnelCategory, personnelCategory))
            .GroupBy(node => node.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => FromKindPeriodicity(SelectKindForCategory(group, personnelCategory).Periodicity),
                StringComparer.OrdinalIgnoreCase);

    public static TrainingKindNode SelectKindForCategory(
        IEnumerable<TrainingKindNode> candidates,
        string? personnelCategory)
    {
        return candidates
            .OrderByDescending(node => !string.IsNullOrWhiteSpace(FromKindPeriodicity(node.Periodicity)))
            .ThenByDescending(node => !string.IsNullOrWhiteSpace(node.PersonnelCategory) &&
                                      !string.IsNullOrWhiteSpace(personnelCategory) &&
                                      node.PersonnelCategory.Trim().Equals(personnelCategory.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(node => !string.IsNullOrWhiteSpace(node.PersonnelCategory))
            .First();
    }

    public static string ResolvePeriodicityForKind(
        string programName,
        IEnumerable<TrainingKindNode> kinds,
        string? personnelCategory,
        IReadOnlyDictionary<string, string>? storedPeriodicities = null)
    {
        var key = programName.Trim();
        var matches = kinds
            .Where(node => !node.IsGroup && !node.MarkedForDeletion)
            .Where(node => node.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Where(node => TrainingKindCatalogDefaults.MatchesPersonnelCategory(node.PersonnelCategory, personnelCategory))
            .ToList();

        string? storedPeriod = null;
        storedPeriodicities?.TryGetValue(key, out storedPeriod);

        if (matches.Count > 0)
        {
            // Приоритет: периодичность из справочника (для выбранной категории) -> сохраненная в стандарте -> дефолт по программе.
            return ResolvePeriodicity(programName, SelectKindForCategory(matches, personnelCategory).Periodicity, storedPeriod);
        }

        // Если по категории совпадений нет, берём из справочника по названию программы (любая категория),
        // чтобы не откатываться к устаревшей периодичности в стандарте.
        var anyCategoryMatches = kinds
            .Where(node => !node.IsGroup && !node.MarkedForDeletion)
            .Where(node => node.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (anyCategoryMatches.Count > 0)
        {
            return ResolvePeriodicity(programName, SelectKindForCategory(anyCategoryMatches, personnelCategory).Periodicity, storedPeriod);
        }

        return ResolvePeriodicity(programName, null, storedPeriod);
    }

    public static string ResolvePeriodicity(
        string programName,
        IReadOnlyDictionary<string, string>? kindLookup,
        IReadOnlyDictionary<string, string>? storedPeriodicities)
    {
        string? kindPeriod = null;
        string? storedPeriod = null;
        kindLookup?.TryGetValue(programName.Trim(), out kindPeriod);
        storedPeriodicities?.TryGetValue(programName.Trim(), out storedPeriod);
        return ResolvePeriodicity(programName, kindPeriod, storedPeriod);
    }

    [Obsolete("Use ResolvePeriodicity to keep catalog wording.")]
    public static string ToFormPeriodicity(string? value) => FromKindPeriodicity(value).Length > 0
        ? FromKindPeriodicity(value)
        : DefaultPeriodicity(string.Empty);

    public static string DefaultPeriodicity(string programName)
    {
        var n = (programName ?? string.Empty).Trim();
        if (n.Contains("вводн", StringComparison.OrdinalIgnoreCase))
        {
            return "1 раз";
        }

        if (n.Contains("проверк", StringComparison.OrdinalIgnoreCase))
        {
            return "1 раз в 3 года";
        }

        if (n.Contains("СИЗ", StringComparison.OrdinalIgnoreCase))
        {
            return "1 раз в 6 месяцев";
        }

        if (n.Contains("стажиров", StringComparison.OrdinalIgnoreCase))
        {
            return "при приёме";
        }

        return "1 раз в год";
    }

    public static readonly string[] PeriodicityOptions =
    [
        "однократно",
        "6 месяцев",
        "1 год",
        "3 года",
        "5 лет"
    ];

    /// <summary>
    /// В отчёте периодичность правится вручную у «Проф обучение»
    /// (срок зависит от профессии / удостоверения).
    /// </summary>
    public static bool AllowsManualPeriodicityInReport(string? programName)
    {
        var n = (programName ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (n.Length == 0)
        {
            return false;
        }

        return n.Contains("профобуч", StringComparison.Ordinal)
               || n.Contains("проф. обуч", StringComparison.Ordinal)
               || n.Contains("проф обуч", StringComparison.Ordinal)
               || (n.Contains("профессиональн", StringComparison.Ordinal)
                   && n.Contains("обучен", StringComparison.Ordinal));
    }

    public static bool IsBlankPeriodicityMarker(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length == 0
               || v == "-"
               || v == "—"
               || v == "–";
    }

    public static IReadOnlyList<string> PeriodicityOptionsFor(string? currentValue)
    {
        var normalized = FromKindPeriodicity(currentValue);
        if (normalized.Length > 0 &&
            !IsBlankPeriodicityMarker(normalized) &&
            !PeriodicityOptions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return [..PeriodicityOptions, normalized];
        }

        return PeriodicityOptions;
    }
}
