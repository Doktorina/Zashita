using Zashita.Core.Employees;

namespace Zashita.Core.Training;

/// <summary>
/// Для организаций-ИП в отчёте обучения может быть стандарт на должность
/// «Индивидуальный предприниматель» — только для самого предпринимателя по ФИО.
/// </summary>
public static class TrainingIndividualEntrepreneurStandards
{
    public const string ProfessionName = "Индивидуальный предприниматель";
    public const string PersonnelCategory = "Руководитель";

    private static readonly string[] DefaultProgramHints =
    [
        "Обучение по охране труда",
        "Обучение по оказанию первой помощи",
        "Обучение мерам пожарной безопасности",
        "Аттестация по охране труда",
        "Обучение по применению СИЗ"
    ];

    /// <summary>
    /// Создаёт или находит стандарт для ИП и при необходимости назначает его предпринимателю.
    /// Возвращает актуальный список стандартов организации (с новым, если создан).
    /// <paramref name="changed"/> — true, если создали/дополнили стандарт или назначили его.
    /// </summary>
    public static List<TrainingStandard> EnsureForOrganization(
        string organization,
        TrainingStandardsStore standardsStore,
        TrainingPersonStandardAssignmentStore? assignments,
        IReadOnlyList<(string Organization, string Name, string Profession, string Department)> employees,
        IReadOnlyList<TrainingKindNode> kinds,
        out bool changed)
    {
        changed = false;
        var org = (organization ?? string.Empty).Trim();
        var list = standardsStore.LoadAll().ToList();
        // Снять старый запрет автосоздания (мешал ИП получить стандарт после удаления).
        TryClearLegacySuppress(standardsStore, org);
        var isIeOrg = OrganizationMatcher.InferIndividualEntrepreneurEmployee(org) is not null;
        var hasIeEmployee = employees.Any(e =>
            OrganizationMatcher.IsIndividualEntrepreneurPerson(org, e.Name, e.Profession));
        // Организация «ИП …» или в штате есть сам предприниматель.
        if (string.IsNullOrWhiteSpace(org) || (!isIeOrg && !hasIeEmployee))
        {
            return FilterForOrg(list, org);
        }

        // Снимаем ошибочные назначения стандарта ИП с наёмных (баг: раньше помечались все).
        if (assignments is not null
            && ClearIeAssignmentsFromHiredWorkers(org, list, assignments))
        {
            changed = true;
        }

        var existing = FindExisting(list, org);
        TrainingStandard standard;
        if (existing is not null)
        {
            standard = existing;
            if (standard.Items.Count(i => !string.IsNullOrWhiteSpace(i)) == 0)
            {
                var programs = BuildDefaultPrograms(kinds);
                var filled = standard with
                {
                    Items = programs,
                    ProgramPeriodicities = BuildPeriodicities(kinds, programs),
                    PersonnelCategory = string.IsNullOrWhiteSpace(standard.PersonnelCategory)
                        ? PersonnelCategory
                        : standard.PersonnelCategory,
                    Professions = MergeProfession(standard.AllProfessions)
                };
                standardsStore.Upsert(filled);
                standard = filled;
                list = standardsStore.LoadAll().ToList();
                changed = true;
            }
        }
        else
        {
            var programs = BuildDefaultPrograms(kinds);
            standard = new TrainingStandard(
                Id: Guid.NewGuid().ToString("N"),
                Name: ProfessionName,
                Organization: org,
                Profession: ProfessionName,
                Items: programs,
                CreatedAt: DateTime.UtcNow,
                Category: "Обучение",
                Professions: [ProfessionName],
                PersonnelCategory: PersonnelCategory,
                TrainingMethod: "Своими силами",
                ProgramPeriodicities: BuildPeriodicities(kinds, programs),
                Version: 1,
                EffectiveFrom: DateTime.Today);
            standardsStore.Upsert(standard);
            list = standardsStore.LoadAll().ToList();
            changed = true;
        }

        if (assignments is not null
            && AutoAssignIfNeeded(org, standard.Id, employees, assignments))
        {
            changed = true;
        }

        return FilterForOrg(list, org);
    }

    /// <summary>
    /// Пользователь удалил стандарт ИП — снимаем назначения.
    /// Стандарт снова создастся только для самого предпринимателя (не для наёмных).
    /// </summary>
    public static void RememberUserDeleted(
        string organization,
        string standardId,
        TrainingStandardsStore standardsStore,
        TrainingPersonStandardAssignmentStore? assignments)
    {
        _ = standardsStore;
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || assignments is null || string.IsNullOrWhiteSpace(standardId))
        {
            return;
        }

        var list = assignments.LoadAll();
        var next = list
            .Where(a => !string.Equals(a.StandardId, standardId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (next.Count != list.Count)
        {
            assignments.SaveAll(next);
        }
    }

    /// <summary>Снять назначения стандарта ИП с наёмных работников (оставить только у самого ИП).</summary>
    public static bool ClearIeAssignmentsFromHiredWorkers(
        string organization,
        IReadOnlyList<TrainingStandard> standards,
        TrainingPersonStandardAssignmentStore assignments)
    {
        var org = (organization ?? string.Empty).Trim();
        var ieStandardIds = TrainingReportBuilder.StandardsForOrganization(standards, org)
            .Where(s => IsIeProfession(s.Name) || IsIeProfession(s.Profession)
                        || s.AllProfessions.Any(IsIeProfession))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ieStandardIds.Count == 0)
        {
            return false;
        }

        var list = assignments.LoadAll();
        var changed = false;
        var next = new List<TrainingPersonStandardAssignment>(list.Count);
        foreach (var a in list)
        {
            if (string.IsNullOrWhiteSpace(a.StandardId)
                || !ieStandardIds.Contains(a.StandardId)
                || !(string.Equals(a.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                     || OrganizationMatcher.Matches(org, a.Organization)))
            {
                next.Add(a);
                continue;
            }

            if (OrganizationMatcher.IsIndividualEntrepreneurPerson(org, a.Person, a.Profession))
            {
                next.Add(a);
                continue;
            }

            // Наёмный с назначением стандарта ИП — снимаем.
            changed = true;
        }

        if (changed)
        {
            assignments.SaveAll(next);
        }

        return changed;
    }

    private static bool AutoAssignIfNeeded(
        string organization,
        string standardId,
        IReadOnlyList<(string Organization, string Name, string Profession, string Department)> employees,
        TrainingPersonStandardAssignmentStore assignments)
    {
        var assigned = false;
        foreach (var employee in employees)
        {
            var person = (employee.Name ?? string.Empty).Trim();
            if (person.Length == 0)
            {
                continue;
            }

            // Только сам предприниматель по ФИО из названия организации.
            if (!OrganizationMatcher.IsIndividualEntrepreneurPerson(organization, person, employee.Profession))
            {
                continue;
            }

            var rawProfession = (employee.Profession ?? string.Empty).Trim();
            var professionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ProfessionName };
            if (string.IsNullOrWhiteSpace(rawProfession) || rawProfession == "—")
            {
                professionKeys.Add("—");
            }
            else
            {
                professionKeys.Add(rawProfession);
            }

            foreach (var profession in professionKeys)
            {
                // Есть запись назначения (в т.ч. пустой id = «без стандарта») — не трогаем.
                if (assignments.Find(organization, person, profession) is not null)
                {
                    continue;
                }

                assignments.SetAssignment(organization, person, profession, standardId);
                assigned = true;
            }
        }

        return assigned;
    }

    private static TrainingStandard? FindExisting(IReadOnlyList<TrainingStandard> standards, string organization)
    {
        return TrainingReportBuilder.StandardsForOrganization(standards, organization)
            .Where(s => s.AllProfessions.Any(IsIeProfession) || IsIeProfession(s.Name) || IsIeProfession(s.Profession))
            .OrderByDescending(s => s.Items.Count(i => !string.IsNullOrWhiteSpace(i)))
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private static bool IsIeProfession(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase)
               && text.Contains("предпринимател", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> MergeProfession(IReadOnlyList<string> existing)
    {
        var result = existing
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!result.Any(IsIeProfession))
        {
            result.Add(ProfessionName);
        }

        return result;
    }

    private static List<string> BuildDefaultPrograms(IReadOnlyList<TrainingKindNode> kinds)
    {
        static IEnumerable<TrainingKindNode> LeafKinds(IReadOnlyList<TrainingKindNode> source) =>
            source.Where(k => !k.IsGroup && !k.MarkedForDeletion && !string.IsNullOrWhiteSpace(k.Name));

        var catalog = LeafKinds(kinds)
            .Where(k => TrainingKindCatalogDefaults.MatchesPersonnelCategory(k.PersonnelCategory, PersonnelCategory))
            .Select(k => k.Name.Trim())
            .Where(n => !TrainingProgramCatalog.IsBriefingProgram(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Нет видов с категорией «Руководитель» — берём любые программы ОТ из каталога.
        if (catalog.Count == 0)
        {
            catalog = LeafKinds(kinds)
                .Select(k => k.Name.Trim())
                .Where(n => !TrainingProgramCatalog.IsBriefingProgram(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var picked = new List<string>();
        foreach (var hint in DefaultProgramHints)
        {
            var match = catalog.FirstOrDefault(n =>
                string.Equals(n, hint, StringComparison.OrdinalIgnoreCase)
                || n.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || hint.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !picked.Contains(match, StringComparer.OrdinalIgnoreCase))
            {
                picked.Add(match);
            }
        }

        if (picked.Count > 0)
        {
            return picked;
        }

        // Каталог есть, но по подсказкам не совпало — первые программы из каталога.
        if (catalog.Count > 0)
        {
            return catalog.Take(5).ToList();
        }

        return DefaultProgramHints.ToList();
    }

    private static Dictionary<string, string> BuildPeriodicities(
        IReadOnlyList<TrainingKindNode> kinds,
        IReadOnlyList<string> programs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in programs)
        {
            result[program] = TrainingProgramCatalog.ResolvePeriodicityForKind(
                program,
                kinds,
                PersonnelCategory,
                null);
        }

        return result;
    }

    private static List<TrainingStandard> FilterForOrg(IEnumerable<TrainingStandard> standards, string org) =>
        standards
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s => string.IsNullOrWhiteSpace(org)
                        || string.Equals(s.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(org, s.Organization))
            .ToList();

    private static void TryClearLegacySuppress(TrainingStandardsStore standardsStore, string organization)
    {
        try
        {
            var path = standardsStore.IeStandardSuppressFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var orgs = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
            var next = orgs
                .Where(o => !(string.Equals(o.Trim(), organization, StringComparison.OrdinalIgnoreCase)
                              || OrganizationMatcher.Matches(organization, o)))
                .ToList();
            if (next.Count == 0)
            {
                File.Delete(path);
            }
            else if (next.Count != orgs.Count)
            {
                File.WriteAllText(
                    path,
                    System.Text.Json.JsonSerializer.Serialize(next, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // ignore
        }
    }
}
