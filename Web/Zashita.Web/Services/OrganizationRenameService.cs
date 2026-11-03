using Microsoft.Extensions.Logging;
using Zashita.Core.Electrical;
using Zashita.Core.Incidents;
using Zashita.Core.Med;
using Zashita.Core.Ppe;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

public sealed class OrganizationRenameService
{
    private readonly TrainingStandardsStore _trainingStandards;
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPlanningStore _trainingPlanning;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly IssueStandardsStore _issueStandards;
    private readonly SavedSiosCardsStore _savedCards;
    private readonly MedExamStore _medExams;
    private readonly MedPersonListStore _medPersonLists;
    private readonly MedRestrictionStore _medRestrictions;
    private readonly MedPhysicalRestrictionNotesStore _medPhysicalRestrictionNotes;
    private readonly MedRegistryStore _medRegistry;
    private readonly MedDirectionStore _medDirections;
    private readonly MedScheduleStore _medSchedules;
    private readonly WorkplaceAccidentStore _workplaceAccidents;
    private readonly AccidentInvestigationStore _accidentInvestigations;
    private readonly MicrotraumaStore _microtraumas;
    private readonly MicrotraumaInvestigationStore _microInvestigations;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly WorkplaceAssignmentStore _workplaceAssignments;
    private readonly ElectricalKnowledgeCheckStore _electricalChecks;
    private readonly ElectricalCommissionStore _electricalCommissions;
    private readonly OrganizationEmployeesService _orgEmployees;
    private readonly OrganizationRequisitesStore _orgRequisites;
    private readonly ILogger<OrganizationRenameService> _logger;

    public OrganizationRenameService(
        TrainingStandardsStore trainingStandards,
        TrainingConductStore trainingConduct,
        TrainingPlanningStore trainingPlanning,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        IssueStandardsStore issueStandards,
        SavedSiosCardsStore savedCards,
        MedExamStore medExams,
        MedPersonListStore medPersonLists,
        MedRestrictionStore medRestrictions,
        MedPhysicalRestrictionNotesStore medPhysicalRestrictionNotes,
        MedRegistryStore medRegistry,
        MedDirectionStore medDirections,
        MedScheduleStore medSchedules,
        WorkplaceAccidentStore workplaceAccidents,
        AccidentInvestigationStore accidentInvestigations,
        MicrotraumaStore microtraumas,
        MicrotraumaInvestigationStore microInvestigations,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces,
        WorkplaceAssignmentStore workplaceAssignments,
        ElectricalKnowledgeCheckStore electricalChecks,
        ElectricalCommissionStore electricalCommissions,
        OrganizationEmployeesService orgEmployees,
        OrganizationRequisitesStore orgRequisites,
        ILogger<OrganizationRenameService> logger)
    {
        _trainingStandards = trainingStandards;
        _trainingConduct = trainingConduct;
        _trainingPlanning = trainingPlanning;
        _trainingAssignments = trainingAssignments;
        _issueStandards = issueStandards;
        _savedCards = savedCards;
        _medExams = medExams;
        _medPersonLists = medPersonLists;
        _medRestrictions = medRestrictions;
        _medPhysicalRestrictionNotes = medPhysicalRestrictionNotes;
        _medRegistry = medRegistry;
        _medDirections = medDirections;
        _medSchedules = medSchedules;
        _workplaceAccidents = workplaceAccidents;
        _accidentInvestigations = accidentInvestigations;
        _microtraumas = microtraumas;
        _microInvestigations = microInvestigations;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
        _workplaceAssignments = workplaceAssignments;
        _electricalChecks = electricalChecks;
        _electricalCommissions = electricalCommissions;
        _orgEmployees = orgEmployees;
        _orgRequisites = orgRequisites;
        _logger = logger;
    }

    public void Propagate(string oldName, string newName)
    {
        var from = oldName.Trim();
        var to = newName.Trim();
        if (from.Length == 0 || to.Length == 0
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Ошибки отдельных хранилищ не должны ронять страницу переименования.
        Run("training-conduct", () => _trainingConduct.RenameOrganization(from, to));
        Run("training-planning", () => _trainingPlanning.RenameOrganization(from, to));
        Run("training-assignments", () => _trainingAssignments.RemapOrganization(from, to));
        Run("org-employees", () => _orgEmployees.RenameOrganization(from, to));
        Run("org-requisites", () => _orgRequisites.Rename(from, to));
        Run("training-standards", () => RenameTrainingStandards(from, to));
        Run("issue-standards", () => RenameIssueStandards(from, to));
        Run("saved-cards", () => RenameSavedCards(from, to));
        Run("med-data", () => RenameMedData(from, to));
        Run("incidents", () => RenameIncidents(from, to));
        Run("workplaces", () => _workplaces.RenameOrganization(from, to));
        Run("risk-workplaces", () => _riskWorkplaces.RenameOrganization(from, to));
        Run("workplace-assignments", () => RenameWorkplaceAssignments(from, to));
        Run("electrical-checks", () => _electricalChecks.RenameOrganization(from, to));
        Run("electrical-commissions", () => _electricalCommissions.RenameOrganization(from, to));
    }

    private void Run(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Organization rename step failed: {Step}", step);
        }
    }

    private void RenameTrainingStandards(string oldName, string newName)
    {
        var standards = _trainingStandards.LoadAll();
        var changed = false;
        // Только for по индексу: foreach + list[i]=… даёт InvalidOperationException.
        for (var i = 0; i < standards.Count; i++)
        {
            var item = standards[i];
            if (!OrgEquals(item.Organization, oldName))
            {
                continue;
            }

            standards[i] = item with { Organization = newName };
            changed = true;
        }

        if (changed)
        {
            _trainingStandards.SaveAll(standards);
        }
    }

    private void RenameIssueStandards(string oldName, string newName)
    {
        var items = _issueStandards.LoadAll();
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!OrgEquals(item.Organization, oldName))
            {
                continue;
            }

            items[i] = item with { Organization = newName };
            changed = true;
        }

        if (changed)
        {
            _issueStandards.SaveAll(items);
        }
    }

    private void RenameSavedCards(string oldName, string newName)
    {
        var items = _savedCards.LoadAll();
        var changed = false;
        foreach (var item in items)
        {
            if (!OrgEquals(item.Organization, oldName))
            {
                continue;
            }

            item.Organization = newName;
            changed = true;
        }

        if (changed)
        {
            _savedCards.SaveAll(items);
        }
    }

    private void RenameMedData(string oldName, string newName)
    {
        RenameMutable(_medExams.LoadAll(), _medExams.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        RenameMutable(_medPersonLists.LoadAll(), _medPersonLists.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        RenameMutable(_medRestrictions.LoadAll(), _medRestrictions.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        _medPhysicalRestrictionNotes.RenameOrganization(oldName, newName);

        var registry = _medRegistry.LoadAll();
        var registryChanged = false;
        for (var i = 0; i < registry.Count; i++)
        {
            var item = registry[i];
            if (!OrgEquals(item.Organization, oldName))
            {
                continue;
            }

            registry[i] = item with { Organization = newName };
            registryChanged = true;
        }

        if (registryChanged)
        {
            _medRegistry.SaveAll(registry);
        }

        _medDirections.RenameOrganization(oldName, newName);
        RenameMutable(_medSchedules.LoadAll(), _medSchedules.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
    }

    private void RenameIncidents(string oldName, string newName)
    {
        RenameMutable(_workplaceAccidents.LoadAll(), _workplaceAccidents.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        RenameMutable(_accidentInvestigations.LoadAll(), _accidentInvestigations.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        RenameMutable(_microtraumas.LoadAll(), _microtraumas.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
        RenameMutable(_microInvestigations.LoadAll(), _microInvestigations.SaveAll, oldName, newName, x => x.Organization, (x, v) => x.Organization = v);
    }

    private void RenameWorkplaceAssignments(string oldName, string newName)
    {
        RenameMutable(
            _workplaceAssignments.LoadAll(),
            _workplaceAssignments.SaveAll,
            oldName,
            newName,
            x => x.Organization,
            (x, v) => x.Organization = v);
    }

    private static void RenameMutable<T>(
        List<T> items,
        Action<List<T>> save,
        string oldName,
        string newName,
        Func<T, string> getOrg,
        Action<T, string> setOrg)
    {
        var changed = false;
        foreach (var item in items)
        {
            if (!OrgEquals(getOrg(item), oldName))
            {
                continue;
            }

            setOrg(item, newName);
            changed = true;
        }

        if (changed)
        {
            save(items);
        }
    }

    private static bool OrgEquals(string? left, string right) =>
        string.Equals((left ?? string.Empty).Trim(), right, StringComparison.OrdinalIgnoreCase);
}
