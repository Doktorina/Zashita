using Zashita.Core.Employees;

namespace Zashita.Core.Electrical;

/// <summary>
/// Подтягивает сотрудников из штата 1С в реестр проверки знаний ЭБ.
/// Уже введённые группы, даты и результаты не затираются.
/// Строки уволенных без заполненных данных убираются; с данными остаются в файле,
/// но в отчёты попадают только через фильтр актуального штата.
/// </summary>
public static class ElectricalKnowledgeStaffSync
{
    public static int SyncMissingFromStaff(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        ElectricalKnowledgeCheckStore store)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var existing = store.Load(org);
        var activeStaff = employees
            .Where(e => EmployeeHrStatus.IsEmployed(e) && !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        var activeNames = new HashSet<string>(
            activeStaff.Select(e => e.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var known = new HashSet<string>(
            existing
                .Select(r => r.EmployeeName?.Trim() ?? string.Empty)
                .Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var changed = false;

        // Убрать пустые строки уволенных (без группы/даты/результата).
        var kept = new List<ElectricalKnowledgeCheckRecord>(existing.Count);
        foreach (var row in existing)
        {
            var name = row.EmployeeName?.Trim() ?? string.Empty;
            if (name.Length > 0
                && !activeNames.Contains(name)
                && !HasTrainingData(row))
            {
                changed = true;
                continue;
            }

            kept.Add(row);
        }

        if (changed)
        {
            existing = kept;
            known = new HashSet<string>(
                existing
                    .Select(r => r.EmployeeName?.Trim() ?? string.Empty)
                    .Where(n => n.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (var employee in activeStaff
                     .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = employee.Name.Trim();
            if (!known.Add(name))
            {
                // Дозаполняем должность, если строка уже есть, но должность пустая.
                var row = existing.FirstOrDefault(r =>
                    string.Equals(r.EmployeeName?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (row is not null
                    && string.IsNullOrWhiteSpace(row.Position)
                    && !string.IsNullOrWhiteSpace(employee.Profession))
                {
                    row.Position = employee.Profession.Trim();
                    changed = true;
                }

                continue;
            }

            existing.Add(new ElectricalKnowledgeCheckRecord
            {
                Organization = org,
                EmployeeName = name,
                Position = employee.Profession?.Trim() ?? string.Empty
            });
            added++;
            changed = true;
        }

        if (changed)
        {
            store.Save(org, existing);
        }

        return added;
    }

    private static bool HasTrainingData(ElectricalKnowledgeCheckRecord row) =>
        !string.IsNullOrWhiteSpace(row.CurrentGroup)
        || row.CurrentGroupAssignedDate.HasValue
        || !string.IsNullOrWhiteSpace(row.LastCheckResult)
        || !string.IsNullOrWhiteSpace(row.ResponsibleNote);

    /// <summary>Синхронизирует штат сразу по нескольким организациям (режим «Все»).</summary>
    public static int SyncMissingFromStaffMany(
        IEnumerable<(string Organization, IReadOnlyList<OrganizationEmployee> Employees)> sources,
        ElectricalKnowledgeCheckStore store)
    {
        var total = 0;
        foreach (var (organization, employees) in sources)
        {
            total += SyncMissingFromStaff(organization, employees, store);
        }

        return total;
    }
}
