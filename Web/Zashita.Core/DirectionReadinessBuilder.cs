using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Ppe;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Core;

/// <summary>
/// Готовность по направлениям на главной: числитель и знаменатель на уровне РМ / сотрудников.
/// </summary>
public static class DirectionReadinessBuilder
{
    public sealed record DirectionReadiness(
        int SoutPercent,
        int RiskPercent,
        int TrainingPercent,
        int PpePercent,
        int MedPercent,
        string SoutDetail,
        string RiskDetail,
        string TrainingDetail,
        string PpeDetail,
        string MedDetail);

    public static DirectionReadiness Build(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<TrainingStandard> trainingStandards,
        IReadOnlyList<TrainingConductRecord> trainingConduct,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        IReadOnlyList<IssueStandard> issueStandards,
        IReadOnlyList<SavedSiosCardRecord> savedCards,
        IReadOnlyList<MedExamRecord> medExams,
        DateTime? asOf = null,
        IReadOnlyDictionary<string, string>? trainingAssignments = null,
        IReadOnlyList<WorkplaceRecord>? riskWorkplaces = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var (soutOk, soutTotal) = CountSout(workplaces, organization, today);
        var (riskOk, riskTotal) = CountRisks(riskWorkplaces ?? workplaces, organization, today);
        var (trainOk, trainTotal) = CountTraining(
            organization, trainingStandards, trainingConduct, employeesByProfession, today, trainingAssignments);
        var (ppeOk, ppeTotal) = CountPpe(organization, issueStandards, savedCards, employeesByProfession);
        var (medOk, medTotal) = CountMed(organization, medExams, today);

        return new DirectionReadiness(
            Percent(soutOk, soutTotal),
            Percent(riskOk, riskTotal),
            Percent(trainOk, trainTotal),
            Percent(ppeOk, ppeTotal),
            Percent(medOk, medTotal),
            FormatDetail(soutOk, soutTotal, "РМ с действующей СОУТ", "используемых РМ"),
            FormatDetail(riskOk, riskTotal, "РМ с оценкой рисков", "подлежащих оценке"),
            FormatDetail(trainOk, trainTotal, "действующих удостоверений", "обязательных программ"),
            FormatDetail(ppeOk, ppeTotal, "выданных позиций", "по нормативу"),
            FormatDetail(medOk, medTotal, "в сроке", "подлежащих медосмотру"));
    }

    private static (int Ok, int Total) CountSout(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        DateTime today)
    {
        var used = workplaces
            .Where(w => MatchesOrg(w.Organization, organization))
            .Where(w => !w.IsGroup && w.IsUsed)
            .ToList();
        var ok = used.Count(w => RiskAssessmentStatusBuilder.HasValidSout(w, today));
        return (ok, used.Count);
    }

    private static (int Ok, int Total) CountRisks(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        DateTime today)
    {
        var subject = workplaces
            .Where(w => MatchesOrg(w.Organization, organization))
            .Where(w => !w.IsGroup && w.IsUsed)
            .ToList();
        var ok = subject.Count(w => RiskAssessmentStatusBuilder.HasValidRiskAssessment(w, today));
        return (ok, subject.Count);
    }

    private static (int Ok, int Total) CountTraining(
        string organization,
        IReadOnlyList<TrainingStandard> standards,
        IReadOnlyList<TrainingConductRecord> conduct,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        DateTime today,
        IReadOnlyDictionary<string, string>? assignments)
    {
        var employeeRows = employeesByProfession
            .SelectMany(pair => pair.Value.Select(name => (
                Organization: organization ?? "",
                Name: name,
                Profession: pair.Key,
                Department: "—")))
            .ToList();

        var rows = TrainingReportBuilder.BuildManualStatusReport(
            standards,
            organization,
            employeeRows,
            today,
            conduct,
            assignments);

        var summary = TrainingReportBuilder.SummarizeStatusRows(rows);
        return (summary.Ok, summary.Total);
    }

    private static (int Ok, int Total) CountPpe(
        string organization,
        IReadOnlyList<IssueStandard> standards,
        IReadOnlyList<SavedSiosCardRecord> cards,
        IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        var orgStandards = standards
            .Where(s => string.IsNullOrWhiteSpace(s.Organization) || MatchesOrg(s.Organization, organization))
            .Where(s => s.Items.Count > 0)
            .ToList();

        if (orgStandards.Count == 0)
        {
            return (0, 0);
        }

        var requiredPairs = new List<(string Employee, string Item)>();
        foreach (var standard in orgStandards)
        {
            var employees = ResolveStandardEmployees(standard, employeesByProfession);
            foreach (var employee in employees)
            {
                foreach (var item in standard.Items.Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    requiredPairs.Add((employee, item.Trim()));
                }
            }
        }

        if (requiredPairs.Count == 0)
        {
            return (0, 0);
        }

        var issued = new HashSet<(string Employee, string Item)>(PersonProgramComparer.Instance);
        foreach (var card in cards.Where(c => MatchesOrg(c.Organization, organization)))
        {
            var items = card.Rows.Count > 0
                ? card.Rows.Select(r => (r.Name, Qty: FirstNonEmpty(r.BackIssueQty, r.Qty)))
                : card.PrintRows.Select(r => (r.Name, Qty: "1"));

            foreach (var employee in card.Employees.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                foreach (var (name, qty) in items)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (IsPositiveQty(qty) || string.IsNullOrWhiteSpace(qty))
                    {
                        // Пустое кол-во на карточке считаем выданным (карточка заведена).
                        issued.Add((employee.Trim(), name.Trim()));
                    }
                }
            }
        }

        var ok = requiredPairs.Count(pair =>
            issued.Contains(pair) ||
            issued.Any(i =>
                string.Equals(i.Item1, pair.Employee, StringComparison.OrdinalIgnoreCase) &&
                (i.Item2.Contains(pair.Item, StringComparison.OrdinalIgnoreCase) ||
                 pair.Item.Contains(i.Item2, StringComparison.OrdinalIgnoreCase))));
        return (ok, requiredPairs.Count);
    }

    private static IReadOnlyList<string> ResolveStandardEmployees(
        IssueStandard standard,
        IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        if (standard.Workers is { Count: > 0 })
        {
            return standard.Workers.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (!string.IsNullOrWhiteSpace(standard.Profession) &&
            employeesByProfession.TryGetValue(standard.Profession, out var list))
        {
            return list;
        }

        return [];
    }

    private static (int Ok, int Total) CountMed(
        string organization,
        IReadOnlyList<MedExamRecord> exams,
        DateTime today)
    {
        var orgExams = exams
            .Where(e => MatchesOrg(e.Organization, organization))
            .Where(e => !e.ExamType.Contains("смен", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (orgExams.Count == 0)
        {
            return (0, 0);
        }

        var byPerson = orgExams
            .GroupBy(e => e.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ok = 0;
        foreach (var group in byPerson)
        {
            var lastPassed = group
                .Where(e => string.Equals(e.Status, "Пройден", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.ExamDate)
                .FirstOrDefault();
            if (lastPassed is null)
            {
                continue;
            }

            var due = MedDashboardBuilder.ResolveDueDate(lastPassed);
            if (due >= today)
            {
                ok++;
            }
        }

        return (ok, byPerson.Count);
    }

    private static int Percent(int ok, int total) =>
        total <= 0 ? 0 : (int)Math.Round(ok * 100.0 / total);

    private static string FormatDetail(int ok, int total, string okLabel, string totalLabel) =>
        total <= 0 ? "нет данных" : $"{ok} {okLabel} / {total} {totalLabel}";

    private static bool MatchesOrg(string left, string right) =>
        string.IsNullOrWhiteSpace(right) ||
        string.Equals(left?.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase) ||
        OrganizationMatcher.Matches(right, left ?? string.Empty);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static bool IsPositiveQty(string? qty)
    {
        if (string.IsNullOrWhiteSpace(qty))
        {
            return false;
        }

        var digits = new string(qty.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        if (double.TryParse(digits.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n > 0;
        }

        return true;
    }

    private sealed class PersonProgramComparer : IEqualityComparer<(string, string)>
    {
        public static readonly PersonProgramComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1 ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2 ?? string.Empty));
    }
}
