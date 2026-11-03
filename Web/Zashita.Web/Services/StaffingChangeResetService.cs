using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>
/// При переводе должности сбрасывает обучение/медосмотры и помечает РМ на внеплановый СОУТ/ОПР.
/// </summary>
public sealed class StaffingChangeResetService
{
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly MedExamStore _medExams;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly RiskHazardAssessmentStore _hazards;

    public StaffingChangeResetService(
        TrainingConductStore trainingConduct,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        MedExamStore medExams,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces,
        RiskHazardAssessmentStore hazards)
    {
        _trainingConduct = trainingConduct;
        _trainingAssignments = trainingAssignments;
        _medExams = medExams;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
        _hazards = hazards;
    }

    public void ApplyTransferReset(StaffingChangeEvent change)
    {
        if (change.Kind != StaffingChangeKind.Transferred)
        {
            return;
        }

        var org = change.Organization?.Trim() ?? string.Empty;
        var person = change.Person?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(person))
        {
            return;
        }

        ResetTraining(org, person, change.OldProfession, change.OldDepartment);
        ResetMed(org, person);
        FlagSoutAndRisks(org, person, change.OldProfession, change.NewProfession, change.OldDepartment, change.NewDepartment);
        change.ResetApplied = true;
        if (string.IsNullOrWhiteSpace(change.Message))
        {
            change.Message = "Сброшены обучение и медосмотры; отмечены РМ для пересмотра СОУТ/ОПР.";
        }
        else if (!change.Message.Contains("Сброшены", StringComparison.OrdinalIgnoreCase))
        {
            change.Message += " Сброшены обучение/МО; отмечены РМ.";
        }
    }

    private void ResetTraining(
        string organization,
        string person,
        string oldProfession,
        string oldDepartment)
    {
        var conduct = _trainingConduct.LoadAll();
        var changed = false;
        var activeRows = conduct
            .Where(r => !r.IsArchivedSnapshot)
            .Where(r =>
                string.Equals(r.Person, person, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(r.Organization)
                    || OrganizationMatcher.Matches(organization, r.Organization)
                    || string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var row in activeRows)
        {
            var hasData = row.TrainingDate.HasValue
                          || !string.IsNullOrWhiteSpace(row.CertificateNumber)
                          || !string.IsNullOrWhiteSpace(row.ProtocolNumber)
                          || !string.IsNullOrWhiteSpace(row.Comment)
                          || !string.IsNullOrWhiteSpace(row.Result)
                          || !string.IsNullOrWhiteSpace(row.ProtocolMovement)
                          || !string.IsNullOrWhiteSpace(row.UpdStatus);
            if (!hasData)
            {
                conduct.Remove(row);
                changed = true;
                continue;
            }

            conduct.Add(CloneArchivedSnapshot(row, oldProfession, oldDepartment));
            conduct.Remove(row);
            changed = true;
        }

        if (changed)
        {
            _trainingConduct.SaveAll(conduct);
        }

        _trainingAssignments.ClearPerson(organization, person);
    }

    private static TrainingConductRecord CloneArchivedSnapshot(
        TrainingConductRecord source,
        string oldProfession,
        string oldDepartment) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = source.Organization,
            Department = string.IsNullOrWhiteSpace(oldDepartment) ? source.Department : oldDepartment.Trim(),
            Profession = string.IsNullOrWhiteSpace(oldProfession) ? source.Profession : oldProfession.Trim(),
            Person = source.Person,
            Program = source.Program,
            TrainingType = source.TrainingType,
            Result = source.Result,
            TrainingDate = source.TrainingDate,
            NextTrainingDate = source.NextTrainingDate,
            ProtocolNumber = source.ProtocolNumber,
            CertificateNumber = source.CertificateNumber,
            ProtocolMovement = source.ProtocolMovement,
            UpdStatus = source.UpdStatus,
            Comment = source.Comment,
            ObjectNote = source.ObjectNote,
            IsArchivedSnapshot = true
        };

    private void ResetMed(string organization, string person)
    {
        var all = _medExams.LoadAll();
        var changed = false;
        foreach (var exam in all.Where(e =>
                     string.Equals(e.EmployeeName, person, StringComparison.OrdinalIgnoreCase)
                     && (string.IsNullOrWhiteSpace(e.Organization)
                         || OrganizationMatcher.Matches(organization, e.Organization)
                         || string.Equals(e.Organization, organization, StringComparison.OrdinalIgnoreCase))))
        {
            exam.ExamDate = DateTime.MinValue;
            exam.NextExamDate = null;
            exam.Status = "Требуется";
            exam.Conclusion = string.Empty;
            changed = true;
        }

        if (changed)
        {
            _medExams.SaveAll(all);
        }
    }

    private void FlagSoutAndRisks(
        string organization,
        string person,
        string oldProfession,
        string newProfession,
        string oldDepartment,
        string newDepartment)
    {
        FlagMatchingWorkplaces(_workplaces, organization, oldProfession, newProfession, oldDepartment, newDepartment);
        FlagMatchingWorkplaces(_riskWorkplaces, organization, oldProfession, newProfession, oldDepartment, newDepartment);

        var hazards = _hazards.LoadAll();
        var nextHazards = hazards
            .Where(h => !(
                (string.IsNullOrWhiteSpace(h.Organization)
                 || OrganizationMatcher.Matches(organization, h.Organization)
                 || string.Equals(h.Organization, organization, StringComparison.OrdinalIgnoreCase))
                && (string.Equals(h.EmployeeName, person, StringComparison.OrdinalIgnoreCase)
                    || ProfessionMatches(h.Profession, oldProfession))))
            .ToList();
        if (nextHazards.Count != hazards.Count)
        {
            _hazards.SaveAll(nextHazards);
        }
    }

    private static void FlagMatchingWorkplaces(
        WorkplaceStore store,
        string organization,
        string oldProfession,
        string newProfession,
        string oldDepartment,
        string newDepartment)
    {
        var workplaces = store.LoadByOrganization(organization);
        var changed = false;
        foreach (var wp in workplaces.Where(w => !w.IsGroup))
        {
            var prof = string.IsNullOrWhiteSpace(wp.Profession) ? wp.Name : wp.Profession;
            var matchesProf = ProfessionMatches(prof, oldProfession) || ProfessionMatches(prof, newProfession);
            var dept = wp.Department ?? string.Empty;
            var matchesDept =
                (!string.IsNullOrWhiteSpace(oldDepartment)
                 && dept.Contains(oldDepartment, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(newDepartment)
                    && dept.Contains(newDepartment, StringComparison.OrdinalIgnoreCase));
            if (!matchesProf && !matchesDept)
            {
                continue;
            }

            wp.RequiresUnscheduledSout = true;
            changed = true;
        }

        if (changed)
        {
            store.SaveForOrganization(organization, workplaces);
        }
    }

    private static bool ProfessionMatches(string? left, string? right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || a.Contains(b, StringComparison.OrdinalIgnoreCase)
               || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }
}
