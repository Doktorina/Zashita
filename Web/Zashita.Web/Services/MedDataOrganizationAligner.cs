using Zashita.Core.Med;

namespace Zashita.Web.Services;

/// <summary>
/// Приводит поле Organization в медицинских JSON к каноническим именам из справочника.
/// </summary>
public sealed class MedDataOrganizationAligner
{
    private readonly MedExamStore _exams;
    private readonly MedPersonListStore _personLists;
    private readonly MedRestrictionStore _restrictions;
    private readonly MedScheduleStore _schedules;
    private readonly MedDirectionStore _directions;
    private readonly MedRegistryStore _registry;
    private readonly MedStandardStore _standards;
    private readonly MedPhysicalRestrictionNotesStore _physicalRestrictionNotes;

    public MedDataOrganizationAligner(
        MedExamStore exams,
        MedPersonListStore personLists,
        MedRestrictionStore restrictions,
        MedScheduleStore schedules,
        MedDirectionStore directions,
        MedRegistryStore registry,
        MedStandardStore standards,
        MedPhysicalRestrictionNotesStore physicalRestrictionNotes)
    {
        _exams = exams;
        _personLists = personLists;
        _restrictions = restrictions;
        _schedules = schedules;
        _directions = directions;
        _registry = registry;
        _standards = standards;
        _physicalRestrictionNotes = physicalRestrictionNotes;
    }

    public int AlignToCanonicalOrganizations(IReadOnlyList<string> canonicalOrganizations)
    {
        if (canonicalOrganizations.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        changed += _exams.AlignOrganizationNames(canonicalOrganizations);
        changed += _personLists.AlignOrganizationNames(canonicalOrganizations);
        changed += _restrictions.AlignOrganizationNames(canonicalOrganizations);
        changed += _schedules.AlignOrganizationNames(canonicalOrganizations);
        changed += _directions.AlignOrganizationNames(canonicalOrganizations);
        changed += _registry.AlignOrganizationNames(canonicalOrganizations);
        changed += _standards.AlignOrganizationNames(canonicalOrganizations);
        changed += _physicalRestrictionNotes.AlignOrganizationNames(canonicalOrganizations);
        return changed;
    }
}
