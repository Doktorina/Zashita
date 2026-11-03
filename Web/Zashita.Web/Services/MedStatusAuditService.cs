using Zashita.Core.Employees;
using Zashita.Core.Med;

namespace Zashita.Web.Services;

public sealed class MedStatusAuditService
{
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly MedExamStore _exams;
    private readonly MedStandardStore _standards;
    private readonly MedDirectionStore _directions;
    private readonly MedPersonListStore _personLists;

    public MedStatusAuditService(
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        MedExamStore exams,
        MedStandardStore standards,
        MedDirectionStore directions,
        MedPersonListStore personLists)
    {
        _orgs = orgs;
        _employees = employees;
        _exams = exams;
        _standards = standards;
        _directions = directions;
        _personLists = personLists;
    }

    public MedStatusAuditReport RunFullAudit(DateTime reportDate)
    {
        var organizations = _orgs.AllOrganizations.ToList();
        var employeePairs = _employees.LoadPairsForReport(organizations).ToList();
        var roster = EmployeeRosterIndex.Build(employeePairs);
        var exams = MergePersonListFallbackExams(_exams.LoadAll(), organizations);
        var standards = _standards.LoadAll();
        var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employeePairs, exams, standards));
        var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
            _directions.LoadAll(),
            organizations);

        var rawRows = MedStatusBuilder.Build(employeePairs, synced, reportDate.Date, directionKeys);
        var cleanedRows = MedStatusReportCleanup.Clean(rawRows, roster);
        var duplicates = MedStatusDuplicateAuditor.FindTreeDuplicates(cleanedRows, roster);
        var garbageExams = _exams.LoadAll()
            .Where(e => !EmployeeNameMatcher.IsPlausiblePersonName(e.EmployeeName))
            .Select(e => $"{e.Organization}: {e.EmployeeName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new MedStatusAuditReport(
            organizations.Count,
            employeePairs.Count,
            duplicates,
            roster.FindAmbiguousMatches(),
            garbageExams);
    }

    private List<MedExamRecord> MergePersonListFallbackExams(
        IReadOnlyList<MedExamRecord> exams,
        IReadOnlyList<string> organizations)
    {
        var merged = MedStandardAssignmentBuilder.CollapseDuplicateExams(exams).ToList();
        var keys = new HashSet<string>(
            merged.Select(MedStandardAssignmentBuilder.BuildExamKey),
            StringComparer.OrdinalIgnoreCase);

        foreach (var doc in _personLists.LoadAll().Where(d =>
                     organizations.Any(o => MedOrganizationFilter.Matches(o, d.Organization))))
        {
            foreach (var line in doc.Lines)
            {
                var name = line.EmployeeName?.Trim() ?? string.Empty;
                var item = MedItemNumber.Normalize(line.ItemNumber);
                if (!EmployeeNameMatcher.IsPlausiblePersonName(name) || string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var examType = MedStandardAssignmentBuilder.NormalizeExamType(doc.ExamType);
                var key = MedStandardAssignmentBuilder.BuildExamKey(doc.Organization, name, examType, item);
                if (!keys.Add(key))
                {
                    continue;
                }

                merged.Add(new MedExamRecord
                {
                    Id = $"plist-{doc.Id}-{line.Id}",
                    Organization = doc.Organization,
                    EmployeeName = name,
                    Profession = ProfessionMatcher.Normalize(line.Profession ?? string.Empty),
                    Department = line.Workplace,
                    ExamType = examType,
                    ItemNumber = item,
                    Periodicity = MedPeriodicity.Normalize(line.Periodicity),
                    BirthDate = line.BirthDate
                });
            }
        }

        return MedStandardAssignmentBuilder.CollapseDuplicateExams(merged);
    }
}

public sealed record MedStatusAuditReport(
    int OrganizationCount,
    int EmployeeCount,
    IReadOnlyList<MedStatusDuplicateIssue> TreeDuplicates,
    IReadOnlyList<EmployeeRosterDuplicate> AmbiguousRosterMatches,
    IReadOnlyList<string> GarbageExamNames);
