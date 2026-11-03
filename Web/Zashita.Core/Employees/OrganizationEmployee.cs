namespace Zashita.Core.Employees;

public sealed class OrganizationEmployee
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Стабильный идентификатор из 1С (GUID/код), если есть в выгрузке.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string Patronymic { get; set; } = string.Empty;

    public string Profession { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Snils { get; set; } = string.Empty;

    /// <summary>Адрес регистрации / проживания (из 1С).</summary>
    public string HomeAddress { get; set; } = string.Empty;

    public DateTime? BirthDate { get; set; }

    public DateTime? EmploymentDate { get; set; }

    /// <summary>Дата перевода (из 1С), если была.</summary>
    public DateTime? TransferDate { get; set; }

    /// <summary>Дата увольнения (из 1С).</summary>
    public DateTime? DismissalDate { get; set; }

    /// <summary>Вид занятости: основное место, внутреннее/внешнее совмещение.</summary>
    public string EmploymentType { get; set; } = string.Empty;

    /// <summary>false — отсутствует в последней выгрузке 1С (уволен/неактуален).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Статус из 1С: отпуск, больничный, уход за ребёнком и т.п.</summary>
    public string HrStatus { get; set; } = string.Empty;

    public DateTime? SyncedAt { get; set; }
}
