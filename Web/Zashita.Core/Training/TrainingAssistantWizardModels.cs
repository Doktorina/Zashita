namespace Zashita.Core.Training;

public enum TrainingAssistantWizardKind
{
    /// <summary>Планирование инструктажа.</summary>
    Plan,
    /// <summary>Проведение инструктажа.</summary>
    Conduct,
    /// <summary>Проведение инструктажа без планирования.</summary>
    Direct,
    /// <summary>Планирование обучения (не инструктаж).</summary>
    TrainPlan
}

public sealed class TrainingAssistantSummaryRow
{
    public string Organization { get; set; } = string.Empty;
    public string TrainingKind { get; set; } = string.Empty;
    public int HeadCount { get; set; }
}

public sealed class TrainingAssistantEmployeeRow
{
    public string Department { get; set; } = string.Empty;
    public string Person { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public DateTime? PlanningDate { get; set; }
    public DateTime? NextTrainingDate { get; set; }
    public DateTime? ConductDate { get; set; }
    public string Attendance { get; set; } = "Явка";
    public string Note { get; set; } = "Рабочий день";
}

public sealed class TrainingAssistantResultDocument
{
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "Обучение";
    public string TrainingKind { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    public string Responsible { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public static class TrainingAssistantWizardLabels
{
    public static bool IsBriefingWizard(TrainingAssistantWizardKind kind) =>
        kind is TrainingAssistantWizardKind.Plan
            or TrainingAssistantWizardKind.Conduct
            or TrainingAssistantWizardKind.Direct;

    public static string Title(TrainingAssistantWizardKind kind) => kind switch
    {
        TrainingAssistantWizardKind.Conduct => "Помощник проведения инструктажа",
        TrainingAssistantWizardKind.Direct => "Проведение инструктажа без планирования",
        TrainingAssistantWizardKind.TrainPlan => "Помощник планирования обучения",
        _ => "Помощник планирования инструктажа"
    };

    public static string Step1Hint(TrainingAssistantWizardKind kind) => kind switch
    {
        TrainingAssistantWizardKind.Conduct => "Выберите организацию и вид инструктажа для создания документа обучения",
        TrainingAssistantWizardKind.Direct => "Выберите организацию и вид инструктажа для создания документа обучения",
        TrainingAssistantWizardKind.TrainPlan => "Выберите организацию и вид обучения для создания документа планирования",
        _ => "Выберите организацию и вид инструктажа для создания документа планирования"
    };

    public static string Step2Hint(TrainingAssistantWizardKind kind, string organization) =>
        kind switch
        {
            TrainingAssistantWizardKind.Conduct =>
                $"По организации «{organization}» требуется провести инструктажи",
            TrainingAssistantWizardKind.Direct =>
                $"По организации «{organization}» требуется провести инструктажи",
            TrainingAssistantWizardKind.TrainPlan =>
                $"По организации «{organization}» требуется запланировать обучение",
            _ => $"По организации «{organization}» требуется запланировать инструктажи"
        };

    public static string Step3Hint(TrainingAssistantWizardKind kind) => kind switch
    {
        TrainingAssistantWizardKind.Conduct => "Сформированы документы проведения инструктажей",
        TrainingAssistantWizardKind.Direct => "Сформированы документы проведения инструктажей",
        TrainingAssistantWizardKind.TrainPlan => "Сформированы документы планирования обучения",
        _ => "Сформированы документы планирования инструктажей"
    };

    public static TrainingAssistantWizardKind ParseKind(string? value)
    {
        var key = value?.Trim().ToLowerInvariant();
        return key switch
        {
            "conduct" => TrainingAssistantWizardKind.Conduct,
            "direct" => TrainingAssistantWizardKind.Direct,
            "train-plan" or "trainplan" or "обучение" or "training" => TrainingAssistantWizardKind.TrainPlan,
            _ => TrainingAssistantWizardKind.Plan
        };
    }

    public static string KindToRoute(TrainingAssistantWizardKind kind) => kind switch
    {
        TrainingAssistantWizardKind.Conduct => "conduct",
        TrainingAssistantWizardKind.Direct => "direct",
        TrainingAssistantWizardKind.TrainPlan => "train-plan",
        _ => "plan"
    };

    /// <summary>Определяет, относится ли вид/программа к инструктажам.</summary>
    public static bool IsBriefingTrainingKind(string? trainingKind, IReadOnlyList<TrainingKindNode>? kinds = null)
    {
        var name = trainingKind?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (kinds is { Count: > 0 })
        {
            var match = kinds.FirstOrDefault(k =>
                !k.IsGroup && string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !string.IsNullOrWhiteSpace(match.Type))
            {
                return match.Type.Contains("инструктаж", StringComparison.OrdinalIgnoreCase);
            }
        }

        return name.Contains("инструктаж", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesWizardFamily(string? trainingKind, TrainingAssistantWizardKind wizardKind, IReadOnlyList<TrainingKindNode>? kinds = null)
    {
        var briefing = IsBriefingTrainingKind(trainingKind, kinds);
        return IsBriefingWizard(wizardKind) ? briefing : !briefing;
    }
}
