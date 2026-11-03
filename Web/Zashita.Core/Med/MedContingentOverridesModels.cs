namespace Zashita.Core.Med;

/// <summary>Ручные значения строки перечня контингентов.</summary>
public sealed class MedContingentPathologyOverride
{
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;

    /// <summary>null — брать значение по умолчанию / авто.</summary>
    public string? EnvironmentFactors { get; set; }
    public string? Order988n { get; set; }
    public string? Order29n { get; set; }
    public int? ExamTotal { get; set; }
    public int? ExamWomen { get; set; }
    public int? LpuTotal { get; set; }
    public int? LpuWomen { get; set; }

    public int PathologyTotal { get; set; }
    public int PathologyWomen { get; set; }
    public int AccidentTotal { get; set; }
    public int AccidentWomen { get; set; }
    public int ExceedLimitTotal { get; set; }
    public int ExceedLimitWomen { get; set; }
    public int OccupationalDiseaseTotal { get; set; }
    public int OccupationalDiseaseWomen { get; set; }
    public int SpecialConditionsTotal { get; set; }
    public int SpecialConditionsWomen { get; set; }
    public int Experience5YearsTotal { get; set; }
    public int Experience5YearsWomen { get; set; }
    public int Age40Total { get; set; }
    public int Age40Women { get; set; }
    public int MissingDataTotal { get; set; }
    public int MissingDataWomen { get; set; }

    /// <summary>Оформление ячеек: ключ f0/a0/b0 → CSS (color / font-weight).</summary>
    public Dictionary<string, string>? CellStyles { get; set; }
}

/// <summary>Реквизиты и правки печати списка контингентов (как в Excel-бланке).</summary>
public sealed class MedContingentPrintSettings
{
    public Dictionary<string, string> Overrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Подсказки на странице списков (текст + оформление; в печати/выгрузке панели нет).</summary>
public sealed class MedContingentPageHints
{
    public string ContingentsHintHtml { get; set; } = string.Empty;
    public string ContingentsHintStyle { get; set; } = string.Empty;
    public string NamedHintHtml { get; set; } = string.Empty;
    public string NamedHintStyle { get; set; } = string.Empty;
}

/// <summary>Правка или скрытие строки поименного списка (ЛПУ).</summary>
public sealed class MedContingentNamedOverride
{
    /// <summary>Стабильный ключ: СНИЛС или ФИО+дата+подразделение+профессия.</summary>
    public string PersonKey { get; set; } = string.Empty;

    /// <summary>true — не показывать в списке и печати.</summary>
    public bool Removed { get; set; }

    public string? FullName { get; set; }
    public string? BirthDate { get; set; }
    public string? Snils { get; set; }
    public string? Department { get; set; }
    public string? Profession { get; set; }
    public string? ExperienceTotal { get; set; }
    public string? ExperienceInOrg { get; set; }
    public string? HarmfulFactors { get; set; }
    public string? ActualLevel { get; set; }
    public string? ConditionsClass { get; set; }
    public string? Order988n { get; set; }
    public string? Order29n { get; set; }
    public string? LastExamLpu { get; set; }
    public string? LastExamCpp { get; set; }
}

public sealed class MedContingentOverridesFile
{
    public Dictionary<string, List<MedContingentPathologyOverride>> ByOrganization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, MedContingentPrintSettings> PrintByOrganization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, MedContingentPageHints> HintsByOrganization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<MedContingentNamedOverride>> NamedByOrganization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
