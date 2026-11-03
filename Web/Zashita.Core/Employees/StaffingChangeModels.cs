namespace Zashita.Core.Employees;

public enum StaffingChangeKind
{
    Hired = 0,
    Transferred = 1,
    Fired = 2
}

public sealed class StaffingChangeEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public StaffingChangeKind Kind { get; set; }
    public string Organization { get; set; } = string.Empty;
    /// <summary>Откуда перевели (другое юрлицо), если удалось сопоставить по СНИЛС.</summary>
    public string FromOrganization { get; set; } = string.Empty;
    public string Person { get; set; } = string.Empty;
    public string Snils { get; set; } = string.Empty;
    public string OldProfession { get; set; } = string.Empty;
    public string NewProfession { get; set; } = string.Empty;
    public string OldDepartment { get; set; } = string.Empty;
    public string NewDepartment { get; set; } = string.Empty;
    public bool ResetApplied { get; set; }
    public bool Acknowledged { get; set; }
    public string Message { get; set; } = string.Empty;

    public string FormatTransferNotice()
    {
        var person = (Person ?? string.Empty).Trim();
        var toOrg = (Organization ?? string.Empty).Trim();
        var fromOrg = (FromOrganization ?? string.Empty).Trim();
        var oldProf = (OldProfession ?? string.Empty).Trim();
        var newProf = (NewProfession ?? string.Empty).Trim();

        var company = !string.IsNullOrWhiteSpace(fromOrg) && !string.IsNullOrWhiteSpace(toOrg)
            ? $"из «{fromOrg}» → в «{toOrg}»"
            : !string.IsNullOrWhiteSpace(toOrg)
                ? $"в «{toOrg}»"
                : string.Empty;

        var profession = !string.IsNullOrWhiteSpace(oldProf) && !string.IsNullOrWhiteSpace(newProf)
                         && !string.Equals(oldProf, newProf, StringComparison.OrdinalIgnoreCase)
            ? $"«{oldProf}» → «{newProf}»"
            : !string.IsNullOrWhiteSpace(newProf)
                ? newProf
                : string.Empty;

        if (person.Length == 0)
        {
            return string.IsNullOrWhiteSpace(company) ? "Перевод сотрудника" : $"Перевод {company}";
        }

        if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(profession))
        {
            return $"{person} — {company}, {profession}";
        }

        if (!string.IsNullOrWhiteSpace(company))
        {
            return $"{person} — {company}";
        }

        return !string.IsNullOrWhiteSpace(profession)
            ? $"{person} — {profession}"
            : person;
    }

    /// <summary>
    /// Если в одной синхронизации человек уволился из А и принят в Б — помечаем приём как перевод между компаниями.
    /// </summary>
    public static void EnrichCrossOrganizationTransfers(IList<StaffingChangeEvent> events)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        static string SnilsKey(StaffingChangeEvent e) =>
            OneCEmployeeListParser.NormalizeSnils(e.Snils);

        static string PersonKey(StaffingChangeEvent e) =>
            (e.Person ?? string.Empty).Trim().ToLowerInvariant();

        var firedBySnils = events
            .Where(e => e.Kind == StaffingChangeKind.Fired && SnilsKey(e).Length > 0)
            .GroupBy(SnilsKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var firedByName = events
            .Where(e => e.Kind == StaffingChangeKind.Fired && PersonKey(e).Length > 0)
            .GroupBy(PersonKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var hired in events.Where(e => e.Kind == StaffingChangeKind.Hired).ToList())
        {
            StaffingChangeEvent? source = null;
            var snils = SnilsKey(hired);
            if (snils.Length > 0 && firedBySnils.TryGetValue(snils, out var bySnils))
            {
                source = bySnils.FirstOrDefault(f =>
                    !string.Equals(f.Organization, hired.Organization, StringComparison.OrdinalIgnoreCase));
            }

            if (source is null)
            {
                var name = PersonKey(hired);
                if (name.Length > 0 && firedByName.TryGetValue(name, out var byName))
                {
                    source = byName.FirstOrDefault(f =>
                        !string.Equals(f.Organization, hired.Organization, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (source is null)
            {
                continue;
            }

            hired.Kind = StaffingChangeKind.Transferred;
            hired.FromOrganization = source.Organization;
            if (string.IsNullOrWhiteSpace(hired.OldProfession))
            {
                hired.OldProfession = source.OldProfession;
            }

            if (string.IsNullOrWhiteSpace(hired.OldDepartment))
            {
                hired.OldDepartment = source.OldDepartment;
            }

            hired.Message =
                $"Перевод из «{source.Organization}» в «{hired.Organization}».";
            source.Acknowledged = true;
            source.Message = string.IsNullOrWhiteSpace(source.Message)
                ? hired.Message
                : $"{source.Message} {hired.Message}";
        }
    }
}
