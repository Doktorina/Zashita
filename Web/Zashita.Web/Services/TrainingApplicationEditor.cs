using Zashita.Core.Employees;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

public sealed class TrainingApplicationEditor
{
    private readonly TrainingPlanningStore _store;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingKindStore _kinds;
    private readonly TrainingProgramStore _programs;
    private readonly OrganizationEmployeesService _employees;
    private readonly TrainingMappingStore _mapping;

    public TrainingApplicationEditor(
        TrainingPlanningStore store,
        TrainingConductStore conduct,
        TrainingKindStore kinds,
        TrainingProgramStore programs,
        OrganizationEmployeesService employees,
        TrainingMappingStore mapping)
    {
        _store = store;
        _conduct = conduct;
        _kinds = kinds;
        _programs = programs;
        _employees = employees;
        _mapping = mapping;
    }

    public TrainingApplicationEditorContext LoadContext(
        string organization,
        string? editId,
        bool create,
        string? q)
    {
        var all = _store.LoadAll()
            .Where(record => string.Equals(record.Organization, organization, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(record.Organization))
            .ToList();

        var employees = _employees.LoadForTrainingReport(organization);
        var kindNodes = _kinds.Load().Where(k => !k.IsGroup).ToList();

        var departments = employees.Select(e => e.Department)
            .Concat(all.Select(r => r.Department))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sections = TrainingKindCatalogDefaults.SectionOptions
            .Concat(kindNodes.Select(k => k.Section))
            .Concat(all.Select(r => r.Section))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var programOptions = _programs.Load()
            .Where(p => !p.IsGroup)
            .Select(p => p.Name.Trim())
            .Concat(all.Select(r => r.TrainingKind))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var kindOptions = kindNodes
            .Select(kind => kind.Name.Trim())
            .Concat(all.Select(record => record.TrainingKind))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var kindProgramLookup = TrainingKindProgramMapping.BuildLookup(
            _mapping.Load(TrainingMappingKind.WorkTypes));

        var rows = all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Trim();
            rows = rows.Where(record =>
                record.Number.Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.TrainingKind.Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.Department.Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.Participants.Any(participant =>
                    participant.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var editRecord = ResolveEditRecord(organization, editId, create);
        var resolvedEditId = editId;
        if (create || string.Equals(editId, "new", StringComparison.OrdinalIgnoreCase))
        {
            resolvedEditId = null;
        }

        return new TrainingApplicationEditorContext(
            rows
                .OrderByDescending(record => record.DocumentDate)
                .ThenByDescending(record => record.Number, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            editRecord,
            resolvedEditId,
            departments,
            sections,
            programOptions,
            kindOptions,
            kindProgramLookup,
            employees);
    }

    public string ResolveProgramForKind(string? kindName) =>
        TrainingKindProgramMapping.ResolveProgram(_mapping.Load(TrainingMappingKind.WorkTypes), kindName) ?? string.Empty;

    public TrainingPlanningRecord ResolveEditRecord(string organization, string? editId, bool create)
    {
        if (!string.IsNullOrWhiteSpace(editId) && !string.Equals(editId, "new", StringComparison.OrdinalIgnoreCase))
        {
            var existing = _store.GetById(editId) ?? new TrainingPlanningRecord
            {
                Id = editId,
                Organization = organization,
                Section = "Охрана труда",
                DocumentDate = DateTime.Today,
                Participants = [new TrainingPlanningParticipant()]
            };
            if (existing.Participants.Count == 0)
            {
                existing.Participants = [new TrainingPlanningParticipant()];
            }

            existing.AgreementPersons ??= [];
            return existing;
        }

        return CreateBlankRecord(organization);
    }

    public static TrainingPlanningRecord CreateBlankRecord(string organization) =>
        new()
        {
            Organization = organization,
            Section = "Охрана труда",
            DocumentDate = DateTime.Today,
            OrganizationDetails = $"{organization}, реквизиты организации",
            RecipientOrganization = "Исполнительному директору НЧОУ УЭЦ «Строитель»",
            RecipientName = "А.А. Улановскому",
            ContactPersonName = "Савельева Анна Сергеевна",
            ContactPersonPhone = "+7 900-213-31-14",
            ContactPersonEmail = "savelieva-as@visural.ru",
            ContactPersonPosition = "Ведущий специалист по охране труда",
            Participants = [new TrainingPlanningParticipant()],
            AgreementPersons = [new TrainingPlanningAgreementPerson()]
        };

    public TrainingPlanningRecord BuildRecord(
        string organization,
        string? editId,
        DateTime? documentDate,
        string? number,
        string? orgName,
        string? department,
        string? section,
        DateTime? executionDate,
        string? trainingKind,
        string? content,
        string? organizationDetails,
        string? objectNote,
        string? contactPersonName,
        string? contactPersonPhone,
        string? contactPersonEmail,
        string? contactPersonPosition,
        string? recipientOrganization,
        string? recipientName,
        string[]? participantFullName,
        string[]? participantPosition,
        string[]? participantBirthDate,
        string[]? participantEmploymentDate,
        string[]? participantSnils,
        string[]? participantTrainingDirection,
        string[]? agreementFullName = null,
        string[]? agreementPosition = null)
    {
        var existing = !string.IsNullOrWhiteSpace(editId) ? _store.GetById(editId) : null;
        var participants = BuildParticipants(
            participantFullName,
            participantPosition,
            participantBirthDate,
            participantEmploymentDate,
            participantSnils,
            participantTrainingDirection,
            trainingKind,
            section);
        if (participants.Count == 0)
        {
            participants = existing?.Participants.ToList() ?? [];
        }
        if (participants.Count == 0)
        {
            participants = [new TrainingPlanningParticipant()];
        }

        var agreement = BuildAgreementPersons(agreementFullName, agreementPosition);
        if (agreement.Count == 0 && existing?.AgreementPersons is { Count: > 0 }
            && (agreementFullName is null || agreementFullName.Length == 0))
        {
            agreement = existing.AgreementPersons.ToList();
        }

        // Раздел и дата проведения убраны из формы заявки — сохраняем прежние значения при правке.
        var resolvedSection = !string.IsNullOrWhiteSpace(section)
            ? section.Trim()
            : (!string.IsNullOrWhiteSpace(existing?.Section) ? existing!.Section.Trim() : "Охрана труда");
        var resolvedExecution = executionDate ?? existing?.ExecutionDate;
        var overrides = existing?.PrintOverrides is { Count: > 0 }
            ? new Dictionary<string, string>(existing.PrintOverrides, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Список согласования из формы перезаписывает ручную правку «СОГЛАСОВАНО» в предпросмотре.
        if (agreement.Any(p => !string.IsNullOrWhiteSpace(p.FullName) || !string.IsNullOrWhiteSpace(p.Position)))
        {
            overrides.Remove("signAgreed");
        }

        var resolvedObject = objectNote?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedObject))
        {
            overrides.Remove("objectNote");
        }
        else
        {
            overrides.Remove("objectNote"); // всегда берём актуальное значение из формы
        }

        return new TrainingPlanningRecord
        {
            Id = string.IsNullOrWhiteSpace(editId) ? Guid.NewGuid().ToString("N") : editId,
            DocumentDate = documentDate ?? DateTime.Today,
            Number = number?.Trim() ?? string.Empty,
            Organization = string.IsNullOrWhiteSpace(orgName) ? organization : orgName.Trim(),
            ObjectNote = resolvedObject,
            Department = department?.Trim() ?? string.Empty,
            Section = resolvedSection,
            ExecutionDate = resolvedExecution,
            TrainingKind = trainingKind?.Trim() ?? string.Empty,
            Content = string.IsNullOrWhiteSpace(content)
                ? $"Планирование: {trainingKind?.Trim() ?? string.Empty}".Trim().TrimEnd(':')
                : content.Trim(),
            OrganizationDetails = organizationDetails?.Trim()
                                  ?? existing?.OrganizationDetails?.Trim()
                                  ?? string.Empty,
            ContactPersonName = contactPersonName?.Trim() ?? string.Empty,
            ContactPersonPhone = contactPersonPhone?.Trim() ?? string.Empty,
            ContactPersonEmail = contactPersonEmail?.Trim() ?? string.Empty,
            ContactPersonPosition = contactPersonPosition?.Trim() ?? string.Empty,
            RecipientOrganization = string.IsNullOrWhiteSpace(recipientOrganization)
                ? "Исполнительному директору НЧОУ УЭЦ «Строитель»"
                : recipientOrganization.Trim(),
            RecipientName = string.IsNullOrWhiteSpace(recipientName)
                ? "А.А. Улановскому"
                : recipientName.Trim(),
            Participants = participants,
            AgreementPersons = agreement,
            PrintOverrides = overrides
        };
    }

    private static List<TrainingPlanningAgreementPerson> BuildAgreementPersons(
        string[]? fullNames,
        string[]? positions)
    {
        var count = Math.Max(fullNames?.Length ?? 0, positions?.Length ?? 0);
        var list = new List<TrainingPlanningAgreementPerson>();
        for (var i = 0; i < count; i++)
        {
            var name = i < (fullNames?.Length ?? 0) ? (fullNames![i] ?? string.Empty).Trim() : string.Empty;
            var position = i < (positions?.Length ?? 0) ? (positions![i] ?? string.Empty).Trim() : string.Empty;
            if (name.Length == 0 && position.Length == 0)
            {
                continue;
            }

            list.Add(new TrainingPlanningAgreementPerson
            {
                FullName = name,
                Position = position
            });
        }

        return list;
    }

    public string AllocateNextNumber()
    {
        var max = _store.LoadAll()
            .Select(record => long.TryParse(record.Number, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString("000000000", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Save(TrainingPlanningRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Number))
        {
            record.Number = AllocateNextNumber();
        }

        var previous = !string.IsNullOrWhiteSpace(record.Id) ? _store.GetById(record.Id) : null;
        _store.Upsert(record);

        var list = _conduct.LoadAll();
        if (previous is not null
            && (!string.Equals(previous.Number, record.Number, StringComparison.OrdinalIgnoreCase)
                || previous.DocumentDate.Date != record.DocumentDate.Date
                || !SameParticipants(previous, record)))
        {
            TrainingApplicationCommentSync.ApplyOnDelete(previous, list, ResolveProgramForKind);
        }

        TrainingApplicationCommentSync.ApplyOnSave(record, list, ResolveProgramForKind);
        _conduct.SaveAll(list);
        Pages.Training.StatusModel.InvalidateReportCache();
    }

    public void Delete(string id)
    {
        var existing = _store.GetById(id);
        if (existing is not null)
        {
            var list = _conduct.LoadAll();
            TrainingApplicationCommentSync.ApplyOnDelete(existing, list, ResolveProgramForKind);
            _conduct.SaveAll(list);
        }

        _store.Delete(id);
        Pages.Training.StatusModel.InvalidateReportCache();
    }

    private static bool SameParticipants(TrainingPlanningRecord left, TrainingPlanningRecord right)
    {
        var a = (left.Participants ?? [])
            .Select(p => $"{p.FullName?.Trim()}|{p.ProgramName?.Trim()}|{p.Position?.Trim()}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var b = (right.Participants ?? [])
            .Select(p => $"{p.FullName?.Trim()}|{p.ProgramName?.Trim()}|{p.Position?.Trim()}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return string.Equals(
            left.TrainingKind?.Trim(),
            right.TrainingKind?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    public static OrganizationEmployee? FindEmployee(
        IReadOnlyList<OrganizationEmployee> employees,
        string? person,
        string? department,
        string? profession)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            return null;
        }

        var name = person.Trim();
        var dept = department?.Trim() ?? string.Empty;
        var prof = profession?.Trim() ?? string.Empty;

        return employees.FirstOrDefault(employee =>
                   string.Equals(employee.Name, name, StringComparison.OrdinalIgnoreCase)
                   && (string.IsNullOrWhiteSpace(dept)
                       || string.Equals(employee.Department?.Trim(), dept, StringComparison.OrdinalIgnoreCase))
                   && (string.IsNullOrWhiteSpace(prof)
                       || string.Equals(employee.Profession?.Trim(), prof, StringComparison.OrdinalIgnoreCase)))
               ?? employees.FirstOrDefault(employee =>
                   string.Equals(employee.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static TrainingPlanningParticipant BuildParticipantFromEmployee(
        OrganizationEmployee? employee,
        string? person,
        string? profession,
        string? program) =>
        new()
        {
            FullName = person?.Trim() ?? string.Empty,
            Position = employee?.Profession?.Trim() ?? profession?.Trim() ?? string.Empty,
            BirthDate = employee?.BirthDate,
            EmploymentDate = employee?.EmploymentDate,
            Snils = OneCEmployeeListParser.FormatSnils(employee?.Snils),
            ProgramName = program?.Trim() ?? string.Empty,
            TrainingDirection = "Охрана труда"
        };

    private static List<TrainingPlanningParticipant> BuildParticipants(
        IReadOnlyList<string>? fullNames,
        IReadOnlyList<string>? positions,
        IReadOnlyList<string>? birthDates,
        IReadOnlyList<string>? employmentDates,
        IReadOnlyList<string>? snilsValues,
        IReadOnlyList<string>? directions,
        string? trainingKind,
        string? section)
    {
        var count = new[]
        {
            fullNames?.Count ?? 0,
            positions?.Count ?? 0,
            birthDates?.Count ?? 0,
            employmentDates?.Count ?? 0,
            snilsValues?.Count ?? 0,
            directions?.Count ?? 0
        }.Max();

        var participants = new List<TrainingPlanningParticipant>();
        for (var i = 0; i < count; i++)
        {
            var fullName = At(fullNames, i);
            var position = At(positions, i);
            var birthDate = ParseDate(At(birthDates, i));
            var employmentDate = ParseDate(At(employmentDates, i));
            var snils = At(snilsValues, i);
            var direction = At(directions, i);

            if (string.IsNullOrWhiteSpace(fullName)
                && string.IsNullOrWhiteSpace(position)
                && birthDate is null
                && employmentDate is null
                && string.IsNullOrWhiteSpace(snils)
                && string.IsNullOrWhiteSpace(direction))
            {
                continue;
            }

            participants.Add(new TrainingPlanningParticipant
            {
                FullName = fullName,
                Position = position,
                BirthDate = birthDate,
                EmploymentDate = employmentDate,
                Snils = OneCEmployeeListParser.FormatSnils(snils),
                ProgramName = trainingKind?.Trim() ?? string.Empty,
                TrainingDirection = string.IsNullOrWhiteSpace(direction)
                    ? section?.Trim() ?? "Охрана труда"
                    : direction
            });
        }

        return participants;
    }

    private static string At(IReadOnlyList<string>? values, int index) =>
        values is not null && index < values.Count ? values[index]?.Trim() ?? string.Empty : string.Empty;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
}

public sealed record TrainingApplicationEditorContext(
    IReadOnlyList<TrainingPlanningRecord> Rows,
    TrainingPlanningRecord EditRecord,
    string? EditId,
    IReadOnlyList<string> Departments,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> ProgramOptions,
    IReadOnlyList<string> KindOptions,
    IReadOnlyDictionary<string, string> KindProgramLookup,
    IReadOnlyList<OrganizationEmployee> OrganizationEmployees);
