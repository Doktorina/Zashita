using Microsoft.AspNetCore.Mvc;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.ViewComponents;

/// <summary>Таблицы на главной модуля «Обучение»: что сделано (проведение) + срез отчёта.</summary>
public sealed class ModuleTrainingHomeTablesViewComponent : ViewComponent
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingKindStore _kinds;
    private readonly OrganizationEmployeesService _employees;

    public ModuleTrainingHomeTablesViewComponent(
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds,
        OrganizationEmployeesService employees)
    {
        _standards = standards;
        _conduct = conduct;
        _assignments = assignments;
        _kinds = kinds;
        _employees = employees;
    }

    public IViewComponentResult Invoke(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return Content(string.Empty);
        }

        var org = organization.Trim();
        var today = DateTime.Today;
        var employeeList = _employees.Load(org)
            .Select(e => (org, e.Name, e.Profession, e.Department))
            .ToList();
        var assignmentMap = _assignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        var statusRows = TrainingReportBuilder.BuildManualStatusReport(
            _standards.LoadAll(),
            org,
            employeeList,
            today,
            _conduct.LoadAll(),
            assignmentMap,
            _kinds.Load());

        var attention = statusRows
            .Where(r =>
                string.Equals(r.Traffic, "red", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Traffic, "yellow", StringComparison.OrdinalIgnoreCase) ||
                (r.Status?.Contains("не обуч", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Status?.Contains("просроч", StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(r => r.Person, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => TrainingProgramDisplayOrder.GetRank(r.Program))
            .ThenBy(r => r.Program, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

        var conducted = _conduct.LoadAll()
            .Where(r => string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .ThenBy(r => r.Person, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        return View(new ModuleTrainingHomeTablesView(org, attention, conducted, statusRows.Count));
    }
}

public sealed record ModuleTrainingHomeTablesView(
    string Organization,
    IReadOnlyList<TrainingStatusRow> AttentionRows,
    IReadOnlyList<TrainingConductRecord> ConductRows,
    int TotalStatusRows);
