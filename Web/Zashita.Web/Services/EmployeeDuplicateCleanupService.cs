using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>Глобальная зачистка дублей людей во всех справочниках и модулях.</summary>
public sealed class EmployeeDuplicateCleanupService
{
    private readonly OrganizationEmployeesStore _employees;
    private readonly MedExamStore _exams;
    private readonly MedPersonListStore _personLists;
    private readonly MedScheduleStore _schedules;
    private readonly MedRestrictionStore _restrictions;
    private readonly MedRegistryStore _registry;
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly DismissedEmployeePurgeService _dismissedPurge;
    private readonly MedExamRosterAligner _examAligner;
    private readonly OrganizationStore _orgs;

    public EmployeeDuplicateCleanupService(
        OrganizationEmployeesStore employees,
        MedExamStore exams,
        MedPersonListStore personLists,
        MedScheduleStore schedules,
        MedRestrictionStore restrictions,
        MedRegistryStore registry,
        TrainingConductStore trainingConduct,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        DismissedEmployeePurgeService dismissedPurge,
        MedExamRosterAligner examAligner,
        OrganizationStore orgs)
    {
        _employees = employees;
        _exams = exams;
        _personLists = personLists;
        _schedules = schedules;
        _restrictions = restrictions;
        _registry = registry;
        _trainingConduct = trainingConduct;
        _trainingAssignments = trainingAssignments;
        _dismissedPurge = dismissedPurge;
        _examAligner = examAligner;
        _orgs = orgs;
    }

    public EmployeeDuplicateCleanupResult RunFullCleanup(bool purgeDismissed = true)
    {
        var result = new EmployeeDuplicateCleanupResult();

        result.RemovedEmployeeShells = _employees.PurgeDuplicatePersonShells();
        result.CollapsedEmployeeRecords = _employees.CollapseDuplicatePersonRecords();
        result.RemovedDuplicateExams = _exams.PurgeDuplicateExams();
        result.RemovedPersonListLines = PurgeDuplicatePersonListLines();
        result.RemovedScheduleLines = PurgeDuplicateScheduleLines();
        result.RemovedRestrictionDocuments = PurgeDuplicateRestrictionDocuments();
        result.RemovedRegistryRows = PurgeDuplicateRegistryRows();
        result.RemovedTrainingConduct = PurgeDuplicateTrainingConduct();
        result.RemovedTrainingAssignments = PurgeDuplicateTrainingAssignments();

        if (purgeDismissed)
        {
            var dismissed = _dismissedPurge.PurgeFromAllLists();
            result.RemovedDismissedExams = dismissed.RemovedExams;
            result.RemovedDismissedPersonListLines = dismissed.RemovedPersonListLines;
        }

        result.AlignedExams = _examAligner.AlignExamsToRoster(_orgs.AllOrganizations);
        return result;
    }

    private int PurgeDuplicatePersonListLines()
    {
        var records = _personLists.LoadAll();
        var removed = 0;
        var changed = false;
        foreach (var record in records)
        {
            var before = record.Lines.Count;
            record.Lines = EmployeePersonListDeduper.DedupeByPersonName(
                record.Lines,
                line => line.EmployeeName,
                line => EmployeePersonListDeduper.PersonNameScore(
                    line.EmployeeName,
                    line.Profession,
                    line.Workplace));
            removed += before - record.Lines.Count;
            if (before != record.Lines.Count)
            {
                changed = true;
            }
        }

        if (changed)
        {
            _personLists.SaveAll(records.Where(r => r.Lines.Count > 0));
        }

        return removed;
    }

    private int PurgeDuplicateScheduleLines()
    {
        var records = _schedules.LoadAll();
        var removed = 0;
        var changed = false;
        foreach (var record in records)
        {
            var before = record.Lines.Count;
            record.Lines = EmployeePersonListDeduper.DedupeByPersonName(
                record.Lines,
                line => line.EmployeeName,
                line => EmployeePersonListDeduper.PersonNameScore(
                    line.EmployeeName,
                    line.Profession,
                    line.Department)
                    + (line.ExamDate.Year >= 1900 ? 5 : 0));
            removed += before - record.Lines.Count;
            if (before != record.Lines.Count)
            {
                changed = true;
            }
        }

        if (changed)
        {
            _schedules.SaveAll(records.Where(r => r.Lines.Count > 0));
        }

        return removed;
    }

    private int PurgeDuplicateRestrictionDocuments()
    {
        var records = _restrictions.LoadAll()
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var kept = new List<MedRestrictionDocument>();
        var removed = 0;
        foreach (var record in records)
        {
            if (kept.Any(existing =>
                    OrganizationMatcher.Matches(existing.Organization, record.Organization)
                    && EmployeeNameMatcher.Matches(existing.EmployeeName, record.EmployeeName)))
            {
                removed++;
                continue;
            }

            kept.Add(record);
        }

        if (removed > 0)
        {
            _restrictions.SaveAll(kept);
        }

        return removed;
    }

    private int PurgeDuplicateRegistryRows()
    {
        var rows = _registry.LoadAll()
            .OrderByDescending(r => r.Date)
            .ToList();
        var kept = new List<MedRegistryRow>();
        var removed = 0;
        foreach (var row in rows)
        {
            if (kept.Any(existing =>
                    string.Equals(existing.Kind, row.Kind, StringComparison.OrdinalIgnoreCase)
                    && OrganizationMatcher.Matches(existing.Organization, row.Organization)
                    && EmployeeNameMatcher.Matches(existing.EmployeeName, row.EmployeeName)
                    && existing.Date.Date == row.Date.Date))
            {
                removed++;
                continue;
            }

            kept.Add(row);
        }

        if (removed > 0)
        {
            _registry.SaveAll(kept);
        }

        return removed;
    }

    private int PurgeDuplicateTrainingConduct()
    {
        var records = _trainingConduct.LoadAll()
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .ToList();
        var kept = new List<TrainingConductRecord>();
        var removed = 0;
        foreach (var record in records)
        {
            if (kept.Any(existing =>
                    OrganizationMatcher.Matches(existing.Organization, record.Organization)
                    && EmployeeNameMatcher.Matches(existing.Person, record.Person)
                    && string.Equals(existing.Program, record.Program, StringComparison.OrdinalIgnoreCase)
                    && existing.TrainingDate == record.TrainingDate))
            {
                removed++;
                continue;
            }

            kept.Add(record);
        }

        if (removed > 0)
        {
            _trainingConduct.SaveAll(kept);
        }

        return removed;
    }

    private int PurgeDuplicateTrainingAssignments()
    {
        var records = _trainingAssignments.LoadAll()
            .OrderByDescending(a => !string.IsNullOrWhiteSpace(a.StandardId) ? 1 : 0)
            .ToList();
        var kept = new List<TrainingPersonStandardAssignment>();
        var removed = 0;
        foreach (var record in records)
        {
            if (kept.Any(existing =>
                    OrganizationMatcher.Matches(existing.Organization, record.Organization)
                    && EmployeeNameMatcher.Matches(existing.Person, record.Person)
                    && string.Equals(existing.Profession, record.Profession, StringComparison.OrdinalIgnoreCase)))
            {
                removed++;
                continue;
            }

            kept.Add(record);
        }

        if (removed > 0)
        {
            _trainingAssignments.SaveAll(kept);
        }

        return removed;
    }
}

public sealed class EmployeeDuplicateCleanupResult
{
    public int RemovedEmployeeShells { get; set; }
    public int CollapsedEmployeeRecords { get; set; }
    public int RemovedDuplicateExams { get; set; }
    public int RemovedPersonListLines { get; set; }
    public int RemovedScheduleLines { get; set; }
    public int RemovedRestrictionDocuments { get; set; }
    public int RemovedRegistryRows { get; set; }
    public int RemovedTrainingConduct { get; set; }
    public int RemovedTrainingAssignments { get; set; }
    public int RemovedDismissedExams { get; set; }
    public int RemovedDismissedPersonListLines { get; set; }
    public int AlignedExams { get; set; }

    public int TotalRemoved =>
        RemovedEmployeeShells
        + CollapsedEmployeeRecords
        + RemovedDuplicateExams
        + RemovedPersonListLines
        + RemovedScheduleLines
        + RemovedRestrictionDocuments
        + RemovedRegistryRows
        + RemovedTrainingConduct
        + RemovedTrainingAssignments
        + RemovedDismissedExams
        + RemovedDismissedPersonListLines;

    public bool AnyChanges => TotalRemoved > 0 || AlignedExams > 0;
}
