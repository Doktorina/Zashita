using System.Net;
using System.Text;

namespace Zashita.Core.Training;

public sealed record TrainingAnnualPlanRow(
    int Number,
    string Direction,
    string Category,
    string Periodicity,
    int HeadCount,
    string Provider,
    IReadOnlyList<int> PlannedMonths);

public sealed record TrainingAnnualPlanDocument(
    string Organization,
    int Year,
    string ApproverPosition,
    string ApproverName,
    IReadOnlyList<TrainingAnnualPlanRow> GeneralRows,
    IReadOnlyList<TrainingAnnualPlanRow> ProfessionRows);

public static class TrainingAnnualPlanBuilder
{
    private static readonly string[] MonthLabels =
        ["Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"];

    public static TrainingAnnualPlanDocument Build(
        IEnumerable<TrainingStandard> standards,
        string organization,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        DateTime reportDate)
    {
        var year = reportDate.Year;
        var org = string.IsNullOrWhiteSpace(organization) ? "Организация" : organization.Trim();
        var standardsList = standards.Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x))).ToList();
        var planned = TrainingReportBuilder.BuildPlannedReport(standardsList, org, employeesByProfession, reportDate);
        var totalWorkers = employeesByProfession.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (totalWorkers == 0)
        {
            totalWorkers = 42;
        }

        var general = BuildGeneralRows(planned, totalWorkers, employeesByProfession);
        var profession = BuildProfessionRows(planned, standardsList, employeesByProfession);

        if (general.Count == 0)
        {
            general = DefaultGeneralRows(totalWorkers);
        }

        if (profession.Count == 0)
        {
            profession = [];
        }

        var (numberedGeneral, numberedProfession) = ApplyNumbering(general, profession);

        return new TrainingAnnualPlanDocument(
            org,
            year,
            "Руководитель организации",
            string.Empty,
            numberedGeneral,
            numberedProfession);
    }

    public static TrainingAnnualPlanDocument ToDocument(TrainingAnnualPlanRecord record)
    {
        var (numberedGeneral, numberedProfession) = ApplyNumbering(
            record.GeneralRows.Select(ToRow).ToList(),
            record.ProfessionRows.Select(ToRow).ToList());

        return new TrainingAnnualPlanDocument(
            record.Organization,
            record.Year,
            record.ApproverPosition,
            record.ApproverName,
            numberedGeneral,
            numberedProfession);
    }

    public static TrainingAnnualPlanRecord FromDocument(TrainingAnnualPlanDocument document) =>
        new()
        {
            Organization = document.Organization,
            Year = document.Year,
            ApproverPosition = document.ApproverPosition,
            ApproverName = document.ApproverName,
            GeneralRows = document.GeneralRows.Select(FromRow).ToList(),
            ProfessionRows = document.ProfessionRows.Select(FromRow).ToList()
        };

    public static TrainingAnnualPlanRecord FromForm(
        string organization,
        int year,
        string approverPosition,
        string approverName,
        IReadOnlyList<string> generalDirection,
        IReadOnlyList<string> generalCategory,
        IReadOnlyList<string> generalPeriodicity,
        IReadOnlyList<string> generalHeadCount,
        IReadOnlyList<string> generalProvider,
        IReadOnlyList<string> generalMonths,
        IReadOnlyList<string> professionDirection,
        IReadOnlyList<string> professionCategory,
        IReadOnlyList<string> professionPeriodicity,
        IReadOnlyList<string> professionHeadCount,
        IReadOnlyList<string> professionProvider,
        IReadOnlyList<string> professionMonths) =>
        new()
        {
            Organization = organization,
            Year = year,
            ApproverPosition = approverPosition,
            ApproverName = approverName,
            GeneralRows = BuildRowData(generalDirection, generalCategory, generalPeriodicity, generalHeadCount, generalProvider, generalMonths),
            ProfessionRows = BuildRowData(professionDirection, professionCategory, professionPeriodicity, professionHeadCount, professionProvider, professionMonths)
        };

    public static string BuildPreviewHtml(TrainingAnnualPlanDocument plan)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <section class="training-annual-plan">
            """);
        sb.Append(BuildHeader(plan));
        sb.Append(BuildTable(plan.GeneralRows, "Общие направления обучения"));
        sb.Append(BuildTable(plan.ProfessionRows, "Обучение работников отдельных профессий (по видам работ)"));
        sb.Append("""
              <div class="training-annual-plan-footnotes">
                <div>* «X» — плановый месяц проведения обучения. Отметка о выполнении проставляется по факту.</div>
                <div>** Инструктажи (вводный, первичный, повторный, внеплановый, целевой) в данный план не включаются.</div>
              </div>
            </section>
            """);
        return sb.ToString();
    }

    public static string BuildHtml(TrainingAnnualPlanDocument plan)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"/>
            <title>Годовой план обучения</title>
            """);
        sb.Append(Zashita.Core.Brand.SiteIconHtml.HeadLinks);
        sb.Append("""
            <link rel="stylesheet" href="/css/training-annual-plan.css" />
            </head><body class="training-annual-plan-body">
            """);
        sb.Append(BuildPreviewHtml(plan));
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildHeader(TrainingAnnualPlanDocument plan) =>
        $"""
              <div class="training-annual-plan-header">
                <div class="training-annual-plan-header-left">
                  <div class="training-annual-plan-title">ГОДОВОЙ ПЛАН ОБУЧЕНИЯ</div>
                  <div class="training-annual-plan-subtitle">по охране труда, пожарной безопасности и промышленной безопасности на {plan.Year} год</div>
                  <div class="training-annual-plan-note">(без учёта инструктажей — ведутся как непрерывный внутренний процесс)</div>
                  <div class="training-annual-plan-org">Организация: <span>{E(plan.Organization)}</span></div>
                </div>
                <div class="training-annual-plan-approve">
                  <div class="training-annual-plan-approve-title">УТВЕРЖДАЮ:</div>
                  <div>{E(plan.ApproverPosition)}</div>
                  <div class="training-annual-plan-approve-line">________________ {E(plan.ApproverName)}</div>
                  <div>«____» ____________ {plan.Year} г.</div>
                </div>
              </div>
            """;

    private static string BuildTable(IReadOnlyList<TrainingAnnualPlanRow> rows, string sectionTitle)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <table class="training-annual-plan-table">
              <thead>
                <tr>
                  <th rowspan="2">№</th>
                  <th rowspan="2">Направление обучения</th>
                  <th rowspan="2">Категория работников / профессия</th>
                  <th rowspan="2">Периодичность</th>
                  <th rowspan="2">Кол-во чел.</th>
                  <th rowspan="2">Учебный центр / ответственный</th>
                  <th colspan="12">Плановые месяцы проведения</th>
                </tr>
                <tr>
            """);
        foreach (var month in MonthLabels)
        {
            sb.Append($"<th>{month}</th>");
        }

        sb.Append("</tr></thead><tbody>");
        sb.Append($"""<tr class="training-annual-plan-section"><td colspan="18">{E(sectionTitle)}</td></tr>""");

        foreach (var row in rows)
        {
            sb.Append("<tr>");
            sb.Append($"<td class=\"training-annual-plan-num\">{row.Number}</td>");
            sb.Append($"<td>{E(row.Direction)}</td>");
            sb.Append($"<td>{E(row.Category)}</td>");
            sb.Append($"<td>{E(row.Periodicity)}</td>");
            sb.Append($"<td class=\"training-annual-plan-count\">{row.HeadCount}</td>");
            sb.Append($"<td>{E(row.Provider)}</td>");
            for (var month = 1; month <= 12; month++)
            {
                var planned = row.PlannedMonths.Contains(month);
                sb.Append(planned
                    ? "<td class=\"training-annual-plan-mark\">X</td>"
                    : "<td></td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static List<TrainingAnnualPlanRow> BuildGeneralRows(
        IReadOnlyList<TrainingReportBuilder.TrainingPlannedRow> planned,
        int totalWorkers,
        IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        var leaderCount = Math.Max(employeesByProfession
            .Where(p => p.Key.Contains("руковод", StringComparison.OrdinalIgnoreCase) ||
                        p.Key.Contains("директор", StringComparison.OrdinalIgnoreCase) ||
                        p.Key.Contains("специалист", StringComparison.OrdinalIgnoreCase) ||
                        p.Key.Contains("инженер", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Value.Count), 3);

        var electricCount = Math.Max(employeesByProfession
            .Where(p => p.Key.Contains("электр", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Value.Count), 1);

        var heightCount = Math.Max(employeesByProfession
            .Where(p => p.Key.Contains("высот", StringComparison.OrdinalIgnoreCase) ||
                        p.Key.Contains("монтаж", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Value.Count), 1);

        var rows = new List<TrainingAnnualPlanRow>
        {
            new(0, "Охрана труда", "Руководители и специалисты", "1 раз в 3 года", leaderCount, "Учебный центр",
                MonthsFromPlanned(planned, "охран", "руковод", 3)),
            new(0, "Пожарно-технический минимум (ПТМ)", "Все работники", "1 раз в год", totalWorkers, "Внутреннее",
                MonthsFromPlanned(planned, "пожар", "птм", 1)),
            new(0, "Электробезопасность", "Электротехнический персонал", "1 раз в год", electricCount, "Учебный центр",
                MonthsFromPlanned(planned, "электр", null, 6)),
            new(0, "Первая помощь", "Все работники", "Ежегодно", totalWorkers, "Внутреннее",
                MonthsFromPlanned(planned, "перв", "помощ", 4, 10)),
            new(0, "Работы на высоте", "Работники, выполняющие работы на высоте", "1 раз в год", heightCount, "Учебный центр",
                MonthsFromPlanned(planned, "высот", null, 5)),
            new(0, "Промышленная безопасность", "Ответственные лица", "1 раз в 5 лет", Math.Max(leaderCount / 2, 1), "Учебный центр",
                MonthsFromPlanned(planned, "промышл", null, 9))
        };

        return rows.Where(r => r.HeadCount > 0).ToList();
    }

    private static List<TrainingAnnualPlanRow> BuildProfessionRows(
        IReadOnlyList<TrainingReportBuilder.TrainingPlannedRow> planned,
        IReadOnlyList<TrainingStandard> standards,
        IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        var rows = new List<TrainingAnnualPlanRow>();
        foreach (var pair in employeesByProfession.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var profession = pair.Key.Trim();
            if (profession.Length == 0)
            {
                continue;
            }

            foreach (var person in pair.Value.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var program = FindProgramForProfession(standards, profession);
                var direction = MapDirection(program);
                var periodicity = TrainingProgramCatalog.DefaultPeriodicity(program);
                var provider = program.Contains("электр", StringComparison.OrdinalIgnoreCase) ? "Учебный центр" : "Внутреннее";
                var category = $"{profession} ({ShortFio(person)})";
                var months = planned
                    .Where(r => string.Equals(r.Profession, profession, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(r.Person, person, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.PlannedDate.Month)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();
                if (months.Count == 0)
                {
                    months = [2 + rows.Count % 10];
                }

                rows.Add(new TrainingAnnualPlanRow(0, direction, category, periodicity, 1, provider, months));
            }
        }

        return rows;
    }

    private static List<TrainingAnnualPlanRow> DefaultGeneralRows(int totalWorkers) =>
    [
        new(2, "Охрана труда", "Руководители и специалисты", "1 раз в 3 года", Math.Max(totalWorkers / 8, 1), "Учебный центр", [3]),
        new(3, "Пожарно-технический минимум (ПТМ)", "Все работники", "1 раз в год", totalWorkers, "Внутреннее", [1]),
        new(4, "Электробезопасность", "Электротехнический персонал", "1 раз в год", Math.Max(totalWorkers / 5, 1), "Учебный центр", [6]),
        new(5, "Первая помощь", "Все работники", "Ежегодно", totalWorkers, "Внутреннее", [4, 10]),
        new(6, "Работы на высоте", "Работники, выполняющие работы на высоте", "1 раз в год", Math.Max(totalWorkers / 7, 1), "Учебный центр", [5]),
        new(7, "Промышленная безопасность", "Ответственные лица", "1 раз в 5 лет", Math.Max(totalWorkers / 14, 1), "Учебный центр", [9])
    ];

    private static List<TrainingAnnualPlanRow> DefaultProfessionRows() => [];

    private static (IReadOnlyList<TrainingAnnualPlanRow> General, IReadOnlyList<TrainingAnnualPlanRow> Profession) ApplyNumbering(
        IReadOnlyList<TrainingAnnualPlanRow> general,
        IReadOnlyList<TrainingAnnualPlanRow> profession)
    {
        var numberedGeneral = Renumber(general, 1);
        var numberedProfession = Renumber(profession, numberedGeneral.Count + 1);
        return (numberedGeneral, numberedProfession);
    }

    private static IReadOnlyList<TrainingAnnualPlanRow> Renumber(IReadOnlyList<TrainingAnnualPlanRow> rows, int startNumber)
    {
        var n = startNumber;
        return rows.Select(r => r with { Number = n++ }).ToList();
    }

    private static List<int> MonthsFromPlanned(
        IReadOnlyList<TrainingReportBuilder.TrainingPlannedRow> planned,
        string directionToken,
        string? categoryToken,
        params int[] fallback)
    {
        var months = planned
            .Where(r =>
                (r.Program.Contains(directionToken, StringComparison.OrdinalIgnoreCase) ||
                 r.TrainingType.Contains(directionToken, StringComparison.OrdinalIgnoreCase)) &&
                (categoryToken is null ||
                 r.Department.Contains(categoryToken, StringComparison.OrdinalIgnoreCase) ||
                 r.Profession.Contains(categoryToken, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.PlannedDate.Month)
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        return months.Count > 0 ? months : fallback.ToList();
    }

    private static string FindProgramForProfession(IReadOnlyList<TrainingStandard> standards, string profession)
    {
        var match = standards.FirstOrDefault(s =>
            s.AllProfessions.Any(p => string.Equals(p, profession, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(s.Profession, profession, StringComparison.OrdinalIgnoreCase));

        var program = match?.Items.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        if (!string.IsNullOrWhiteSpace(program))
        {
            return program;
        }

        if (profession.Contains("электр", StringComparison.OrdinalIgnoreCase))
        {
            return "Обучение по электробезопасности";
        }

        if (profession.Contains("маляр", StringComparison.OrdinalIgnoreCase))
        {
            return "Обучение мерам пожарной безопасности";
        }

        return "Обучение по охране труда";
    }

    private static string MapDirection(string program)
    {
        var p = program.ToLowerInvariant();
        if (p.Contains("пожар") || p.Contains("птм"))
        {
            return "Пожарная безопасность";
        }

        if (p.Contains("электр"))
        {
            return "Электробезопасность";
        }

        if (p.Contains("перв") && p.Contains("помощ"))
        {
            return "Первая помощь";
        }

        if (p.Contains("высот"))
        {
            return "Работы на высоте";
        }

        if (p.Contains("промышл"))
        {
            return "Промышленная безопасность";
        }

        return "Охрана труда";
    }

    private static string ShortFio(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return fullName;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        var initials = string.Concat(parts.Skip(1).Take(2).Select(part => $"{char.ToUpperInvariant(part[0])}."));
        return $"{parts[0]} {initials}".Trim();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static TrainingAnnualPlanRow ToRow(TrainingAnnualPlanRowData row) =>
        new(0, row.Direction, row.Category, row.Periodicity, row.HeadCount, row.Provider, row.PlannedMonths);

    private static TrainingAnnualPlanRowData FromRow(TrainingAnnualPlanRow row) =>
        new()
        {
            Direction = row.Direction,
            Category = row.Category,
            Periodicity = row.Periodicity,
            HeadCount = row.HeadCount,
            Provider = row.Provider,
            PlannedMonths = row.PlannedMonths.ToList()
        };

    private static List<TrainingAnnualPlanRowData> BuildRowData(
        IReadOnlyList<string> directions,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> periodicities,
        IReadOnlyList<string> headCounts,
        IReadOnlyList<string> providers,
        IReadOnlyList<string> monthsCsv)
    {
        var count = new[]
        {
            directions.Count,
            categories.Count,
            periodicities.Count,
            headCounts.Count,
            providers.Count,
            monthsCsv.Count
        }.Max();

        var rows = new List<TrainingAnnualPlanRowData>();
        for (var i = 0; i < count; i++)
        {
            var direction = ValueAt(directions, i);
            var category = ValueAt(categories, i);
            if (direction.Length == 0 && category.Length == 0)
            {
                continue;
            }

            rows.Add(new TrainingAnnualPlanRowData
            {
                Direction = direction,
                Category = category,
                Periodicity = ValueAt(periodicities, i),
                HeadCount = int.TryParse(ValueAt(headCounts, i), out var hc) && hc > 0 ? hc : 1,
                Provider = ValueAt(providers, i),
                PlannedMonths = ParseMonths(ValueAt(monthsCsv, i))
            });
        }

        return rows;
    }

    public static string FormatMonths(IReadOnlyList<int> months) =>
        string.Join(',', months.OrderBy(m => m));

    public static List<int> ParseMonths(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var month) ? month : 0)
            .Where(month => month is >= 1 and <= 12)
            .Distinct()
            .OrderBy(month => month)
            .ToList();
    }

    private static string ValueAt(IReadOnlyList<string> values, int index) =>
        index < values.Count ? (values[index] ?? string.Empty).Trim() : string.Empty;
}
