namespace Zashita.Core.Training;

public sealed class TrainingPlanningParticipant
{
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public DateTime? EmploymentDate { get; set; }
    public string Snils { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string TrainingDirection { get; set; } = string.Empty;
}

/// <summary>Строка блока «Согласование» → в печати под «СОГЛАСОВАНО».</summary>
public sealed class TrainingPlanningAgreementPerson
{
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}

public sealed class TrainingPlanningRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    /// <summary>Объект / площадка (поле формы заявки).</summary>
    public string ObjectNote { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Section { get; set; } = "Охрана труда";
    public DateTime? ExecutionDate { get; set; }
    public string TrainingKind { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OrganizationDetails { get; set; } = string.Empty;
    public string ContactPersonName { get; set; } = string.Empty;
    public string ContactPersonPhone { get; set; } = string.Empty;
    public string ContactPersonEmail { get; set; } = string.Empty;
    public string ContactPersonPosition { get; set; } = string.Empty;
    public string RecipientOrganization { get; set; } = "Исполнительному директору НЧОУ УЭЦ «Строитель»";
    public string RecipientName { get; set; } = "А.А. Улановскому";
    public List<TrainingPlanningParticipant> Participants { get; set; } = [];
    public List<TrainingPlanningAgreementPerson> AgreementPersons { get; set; } = [];
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record TrainingApplicationPrintContext(
    string Organization,
    string OrganizationDetails,
    string ProgramName,
    string FullName,
    string Position,
    DateTime? BirthDate,
    DateTime? EmploymentDate,
    string Snils,
    string TrainingDirection,
    string ContactPersonName,
    string TrainingKind,
    string Content,
    string DocumentNumber,
    DateTime DocumentDate,
    DateTime? ExecutionDate);
