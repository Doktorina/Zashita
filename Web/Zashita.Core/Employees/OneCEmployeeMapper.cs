namespace Zashita.Core.Employees;

/// <summary>Применение полей выгрузки 1С к карточке сотрудника.</summary>
public static class OneCEmployeeMapper
{
    public static void ApplyRow(OrganizationEmployee employee, OneCEmployeeRow row, DateTime syncedAt, bool? isActive = null)
    {
        employee.Name = row.Name.Trim();
        EmployeeFio.ApplyNameParts(employee);
        employee.Profession = row.Profession?.Trim() ?? string.Empty;
        employee.Department = row.Department?.Trim() ?? string.Empty;
        employee.Snils = OneCEmployeeListParser.NormalizeSnils(row.Snils);
        employee.ExternalId = row.ExternalId?.Trim() ?? string.Empty;
        employee.BirthDate = row.BirthDate;
        employee.EmploymentDate = row.EmploymentDate;
        employee.EmploymentType = row.EmploymentType?.Trim() ?? string.Empty;
        employee.HomeAddress = row.HomeAddress?.Trim() ?? string.Empty;
        employee.HrStatus = row.HrStatus?.Trim() ?? string.Empty;

        // Явно «работающий» после повторного приёма / перевода в другую компанию:
        // чистая карточка — только дата рождения и приёма, без увольнения и перевода.
        if (isActive == true)
        {
            employee.DismissalDate = row.DismissalDate is { } future && future.Date > DateTime.Today
                ? future
                : null;
            employee.TransferDate = null;
            if (EmployeeHrStatus.IsDismissed(employee.HrStatus) || string.IsNullOrWhiteSpace(employee.HrStatus))
            {
                employee.HrStatus = "Работа";
            }
        }
        else
        {
            DateTime? dismissal = row.DismissalDate;
            if (dismissal is null
                && row.TransferDate is { } transfer
                && transfer.Date <= DateTime.Today)
            {
                dismissal = transfer;
            }

            if (dismissal is null && EmployeeHrStatus.IsDismissed(row.HrStatus))
            {
                dismissal = DateTime.Today;
            }

            employee.DismissalDate = dismissal;
            employee.TransferDate = null;
            if (dismissal is not null
                || EmployeeHrStatus.IsDismissed(employee.HrStatus)
                || EmployeeHrStatus.IsDismissed(row.HrStatus))
            {
                employee.HrStatus = "Увольнение";
            }
        }

        employee.IsActive = isActive ?? (EmployeeHrStatus.IsOnStaffStatus(employee.HrStatus)
                                         && employee.DismissalDate is null);
        if (string.IsNullOrWhiteSpace(employee.HrStatus) && employee.IsActive)
        {
            employee.HrStatus = "Работа";
        }

        employee.SyncedAt = syncedAt;
    }

    public static void MergeRow(OrganizationEmployee employee, OneCEmployeeRow row, DateTime syncedAt)
    {
        employee.Name = row.Name.Trim();
        EmployeeFio.ApplyNameParts(employee);

        if (!string.IsNullOrWhiteSpace(row.Profession))
        {
            employee.Profession = row.Profession.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.Department))
        {
            employee.Department = row.Department.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.Snils))
        {
            employee.Snils = OneCEmployeeListParser.NormalizeSnils(row.Snils);
        }

        if (!string.IsNullOrWhiteSpace(row.ExternalId))
        {
            employee.ExternalId = row.ExternalId.Trim();
        }

        employee.BirthDate = row.BirthDate ?? employee.BirthDate;
        employee.EmploymentDate = row.EmploymentDate ?? employee.EmploymentDate;
        // Дата перевода в карточке штата не храним — это уход с юрлица, не поле UI.
        employee.TransferDate = null;

        if (row.DismissalDate is not null)
        {
            employee.DismissalDate = row.DismissalDate;
        }
        else if (row.TransferDate is { } transfer && transfer.Date <= DateTime.Today
                 && EmployeeHrStatus.IsDismissed(row.HrStatus))
        {
            employee.DismissalDate = transfer;
        }

        if (!string.IsNullOrWhiteSpace(row.EmploymentType))
        {
            employee.EmploymentType = row.EmploymentType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.HomeAddress))
        {
            employee.HomeAddress = row.HomeAddress.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.HrStatus))
        {
            employee.HrStatus = row.HrStatus.Trim();
        }

        if (row.DismissalDate is not null)
        {
            employee.HrStatus = "Увольнение";
        }

        employee.IsActive = EmployeeHrStatus.IsOnStaffStatus(employee.HrStatus)
                            && (employee.DismissalDate is null || employee.DismissalDate.Value.Date > DateTime.Today);

        if (string.IsNullOrWhiteSpace(employee.HrStatus) && employee.IsActive)
        {
            employee.HrStatus = "Работа";
        }

        employee.SyncedAt = syncedAt;
    }
}
