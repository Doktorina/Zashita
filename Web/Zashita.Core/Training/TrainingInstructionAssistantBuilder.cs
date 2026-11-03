using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public static class TrainingInstructionAssistantBuilder
{
    public static List<TrainingAssistantSummaryRow> BuildSummaryRows(
        IEnumerable<TrainingStandard> standards,
        string organization,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        TrainingAssistantWizardKind wizardKind = TrainingAssistantWizardKind.Plan,
        IReadOnlyList<TrainingKindNode>? kinds = null,
        string? trainingKindFilter = null)
    {
        var selectedOrg = organization?.Trim() ?? string.Empty;
        var rows = new Dictionary<string, TrainingAssistantSummaryRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var standard in standards.Where(s => s.Items.Any(item => !string.IsNullOrWhiteSpace(item))))
        {
            var standardOrg = string.IsNullOrWhiteSpace(standard.Organization)
                ? selectedOrg
                : standard.Organization.Trim();

            // Только выбранная организация из шапки (не «чужие» ООО/ИП).
            if (!string.IsNullOrWhiteSpace(selectedOrg) &&
                !string.Equals(standardOrg, selectedOrg, StringComparison.OrdinalIgnoreCase) &&
                !OrganizationMatcher.Matches(selectedOrg, standardOrg))
            {
                continue;
            }

            var workers = ExpandWorkers(standard, employeesByProfession);
            foreach (var program in standard.Items.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var kind = MapTrainingKind(program, standard.TrainingCategory);
                if (!TrainingAssistantWizardLabels.MatchesWizardFamily(kind, wizardKind, kinds))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(trainingKindFilter) &&
                    !string.Equals(kind, trainingKindFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var displayOrg = string.IsNullOrWhiteSpace(selectedOrg) ? standardOrg : selectedOrg;
                var key = $"{displayOrg}|{kind}";
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new TrainingAssistantSummaryRow
                    {
                        Organization = displayOrg,
                        TrainingKind = kind,
                        HeadCount = 0
                    };
                    rows[key] = row;
                }

                row.HeadCount += workers.Count;
            }
        }

        var result = rows.Values
            .Where(row => row.HeadCount > 0)
            .OrderBy(row => row.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.TrainingKind, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (result.Count > 0)
        {
            return result;
        }

        var demo = DemoSummary(selectedOrg, wizardKind);
        if (!string.IsNullOrWhiteSpace(trainingKindFilter))
        {
            demo = demo
                .Where(r => string.Equals(r.TrainingKind, trainingKindFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return demo;
    }

    /// <summary>Список видов для выбора в помощнике (по типу: инструктаж / обучение).</summary>
    public static IReadOnlyList<string> ResolveKindOptions(
        TrainingAssistantWizardKind wizardKind,
        IReadOnlyList<TrainingKindNode>? kinds,
        IReadOnlyList<TrainingAssistantSummaryRow>? summaryRows = null)
    {
        var fromCatalog = (kinds ?? [])
            .Where(k => !k.IsGroup && !string.IsNullOrWhiteSpace(k.Name))
            .Where(k => TrainingAssistantWizardLabels.MatchesWizardFamily(k.Name, wizardKind, kinds) ||
                        TrainingAssistantWizardLabels.MatchesWizardFamily(
                            string.IsNullOrWhiteSpace(k.Type) ? k.Name : k.Type, wizardKind, kinds) ||
                        (TrainingAssistantWizardLabels.IsBriefingWizard(wizardKind)
                            ? k.Type.Contains("инструктаж", StringComparison.OrdinalIgnoreCase)
                            : !k.Type.Contains("инструктаж", StringComparison.OrdinalIgnoreCase)))
            .Select(k => k.Name.Trim())
            .ToList();

        var fromSummary = (summaryRows ?? [])
            .Select(r => r.TrainingKind)
            .Where(n => !string.IsNullOrWhiteSpace(n));

        return fromCatalog
            .Concat(fromSummary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static List<TrainingAssistantEmployeeRow> BuildEmployeeRows(
        IEnumerable<TrainingStandard> standards,
        string organization,
        string trainingKind,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        DateTime analyticsDate,
        TrainingAssistantWizardKind kind)
    {
        var rows = new List<TrainingAssistantEmployeeRow>();
        var deptNames = new[] { "Бригада ремонта", "Производство", "Администрация", "Участок №1" };
        var deptIndex = 0;

        foreach (var standard in standards.Where(s => s.Items.Any(item => !string.IsNullOrWhiteSpace(item))))
        {
            var org = string.IsNullOrWhiteSpace(standard.Organization) ? organization.Trim() : standard.Organization.Trim();
            if (!org.Equals(organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var programs = standard.Items
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => MapTrainingKind(item, standard.TrainingCategory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!programs.Any(program => program.Equals(trainingKind, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var workers = ExpandWorkers(standard, employeesByProfession);
            var department = deptNames[deptIndex % deptNames.Length];
            deptIndex++;
            var profession = string.IsNullOrWhiteSpace(standard.Profession) ? "Рабочий" : standard.Profession.Trim();

            foreach (var worker in workers)
            {
                rows.Add(new TrainingAssistantEmployeeRow
                {
                    Department = department,
                    Person = worker,
                    Profession = profession,
                    PlanningDate = analyticsDate.Date.AddDays(1),
                    NextTrainingDate = analyticsDate.Date.AddMonths(12),
                    ConductDate = analyticsDate.Date,
                    Attendance = "Явка",
                    Note = "Рабочий день"
                });
            }
        }

        if (rows.Count > 0)
        {
            return rows;
        }

        // Без сотрудников из 1С / стандартов — пустой список (демо-ФИО не подставляем).
        return [];
    }

    public static List<TrainingAssistantResultDocument> BuildResultDocuments(
        TrainingAssistantWizardKind kind,
        string organization,
        string trainingKind,
        IReadOnlyList<TrainingAssistantEmployeeRow> employees,
        string responsible,
        DateTime documentDate)
    {
        var assistantName = TrainingAssistantWizardLabels.Title(kind);
        var commentPrefix = kind switch
        {
            TrainingAssistantWizardKind.TrainPlan => $"#Создан помощником \"{assistantName}\"",
            TrainingAssistantWizardKind.Direct => "#Создан помощником \"Проведение инструктажа без планирования\"",
            TrainingAssistantWizardKind.Conduct => "#Создан помощником \"Помощник проведения инструктажа\"",
            _ => "#Создан помощником \"Помощник планирования инструктажа\""
        };

        var documentType = TrainingAssistantWizardLabels.IsBriefingWizard(kind) ? "Инструктаж" : "Обучение";

        return
        [
            new TrainingAssistantResultDocument
            {
                DocumentDate = documentDate,
                Number = kind is TrainingAssistantWizardKind.Plan or TrainingAssistantWizardKind.TrainPlan
                    ? "ПЛ-00000001"
                    : "ОТ-00000040",
                Organization = organization,
                DocumentType = documentType,
                TrainingKind = trainingKind,
                Program = employees.FirstOrDefault()?.Profession ?? string.Empty,
                Responsible = responsible,
                Comment = commentPrefix
            }
        ];
    }

    private static List<string> ExpandWorkers(
        TrainingStandard standard,
        IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        var workers = new List<string>();
        if (standard.AllProfessions is { Count: > 0 })
        {
            foreach (var profession in standard.AllProfessions)
            {
                if (employeesByProfession.TryGetValue(profession, out var people))
                {
                    workers.AddRange(people);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(standard.Profession) &&
                 employeesByProfession.TryGetValue(standard.Profession.Trim(), out var byProfession))
        {
            workers.AddRange(byProfession);
        }

        return workers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MapTrainingKind(string program, string category)
    {
        var text = $"{program} {category}".ToLowerInvariant();
        if (text.Contains("перв") && text.Contains("помощ"))
        {
            return "Обучение по оказанию первой помощи пострадавшим";
        }

        if (text.Contains("вводн"))
        {
            return "Вводный инструктаж по охране труда";
        }

        if (text.Contains("повтор"))
        {
            return "Повторный инструктаж по охране труда";
        }

        if (text.Contains("электр"))
        {
            return "Электробезопасность 2 группа";
        }

        if (text.Contains("стаж"))
        {
            return "Стажировка";
        }

        if (!string.IsNullOrWhiteSpace(program))
        {
            return program.Trim();
        }

        return string.IsNullOrWhiteSpace(category) ? "Обучение по охране труда" : category.Trim();
    }

    private static List<TrainingAssistantSummaryRow> DemoSummary(string organization, TrainingAssistantWizardKind wizardKind)
    {
        if (TrainingAssistantWizardLabels.IsBriefingWizard(wizardKind))
        {
            return
            [
                new() { Organization = organization, TrainingKind = "Повторный инструктаж по охране труда", HeadCount = 2 },
                new() { Organization = organization, TrainingKind = "Вводный инструктаж по охране труда", HeadCount = 1 },
                new() { Organization = organization, TrainingKind = "Первичный инструктаж по охране труда", HeadCount = 3 }
            ];
        }

        return
        [
            new() { Organization = organization, TrainingKind = "Обучение по оказанию первой помощи пострадавшим", HeadCount = 3 },
            new() { Organization = organization, TrainingKind = "Обучение по ОТ рабочих по 12 часовой программе", HeadCount = 4 },
            new() { Organization = organization, TrainingKind = "Электробезопасность 2 группа", HeadCount = 1 }
        ];
    }

    private static List<TrainingAssistantEmployeeRow> DemoEmployees(
        string organization,
        string trainingKind,
        DateTime analyticsDate,
        TrainingAssistantWizardKind kind)
    {
        _ = organization;
        _ = trainingKind;
        _ = analyticsDate;
        _ = kind;
        return [];
    }
}
