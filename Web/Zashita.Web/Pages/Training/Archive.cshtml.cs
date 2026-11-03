using Zashita.Core.Electrical;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class ArchiveModel : StatusModel
{
    public ArchiveModel(
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds,
        TrainingPlanningStore planning,
        TrainingMappingStore mapping,
        TrainingScheduleImportService scheduleImport,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        ElectricalKnowledgeCheckStore electricalKnowledge)
        : base(standards, conduct, assignments, kinds, planning, mapping, scheduleImport, orgs, employees, electricalKnowledge)
    {
    }

    protected override bool ArchiveOnly => true;
}
