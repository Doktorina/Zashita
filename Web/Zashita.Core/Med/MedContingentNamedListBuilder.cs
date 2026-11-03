using System.Globalization;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public sealed class MedContingentNamedListRow
{
    public string PersonKey { get; set; } = string.Empty;
    public int RowNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string Snils { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string ExperienceTotal { get; set; } = string.Empty;
    public string ExperienceInOrg { get; set; } = string.Empty;
    public string HarmfulFactors { get; set; } = string.Empty;
    public string ActualLevel { get; set; } = string.Empty;
    public string ConditionsClass { get; set; } = string.Empty;
    public string Order988n { get; set; } = string.Empty;
    public string Order29n { get; set; } = string.Empty;
    public string LastExamLpu { get; set; } = string.Empty;
    public string LastExamCpp { get; set; } = string.Empty;
}

/// <summary>
/// Поименный список (ЛПУ): профессии из перечня контингентов, где колонка 11 (LpuTotal) ≠ 0.
/// </summary>
public static class MedContingentNamedListBuilder
{
    public static IReadOnlyList<MedContingentNamedListRow> Build(
        IEnumerable<MedContingentListRow> contingentRows,
        IEnumerable<OrganizationEmployee> employees,
        IEnumerable<MedExamRecord> exams,
        IEnumerable<MedContingentNamedOverride>? namedOverrides = null)
    {
        var selected = contingentRows
            .Where(r => r.LpuTotal > 0)
            .ToList();

        if (selected.Count == 0)
        {
            return [];
        }

        var overrideByKey = MedContingentOverridesStore.IndexNamedByKey(namedOverrides ?? []);

        var examsByPerson = exams
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeName))
            .GroupBy(e => MedContingentListBuilder.NormalizePerson(e.EmployeeName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var active = employees
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .ToList();

        var result = new List<MedContingentNamedListRow>();

        foreach (var contingent in selected
                     .OrderBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase))
        {
            var people = active
                .Where(e =>
                    string.Equals(
                        string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department.Trim(),
                        contingent.Department,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        MedContingentListBuilder.NormalizeProfession(e.Profession),
                        contingent.Profession,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var person in people)
            {
                var birth = person.BirthDate is { } bd ? bd.ToString("dd.MM.yyyy") : string.Empty;
                var snils = OneCEmployeeListParser.FormatSnils(person.Snils);
                var key = MakePersonKey(snils, person.Name, birth, contingent.Department, contingent.Profession);

                var ov = ResolveNamedOverride(overrideByKey, key, snils);
                if (ov is { Removed: true })
                {
                    continue;
                }

                examsByPerson.TryGetValue(MedContingentListBuilder.NormalizePerson(person.Name), out var personExams);
                var lastExam = personExams?
                    .Where(e => e.ExamDate.Year > 1900)
                    .OrderByDescending(e => e.ExamDate)
                    .FirstOrDefault();

                var row = new MedContingentNamedListRow
                {
                    PersonKey = key,
                    FullName = person.Name.Trim(),
                    BirthDate = birth,
                    Snils = snils,
                    Department = contingent.Department,
                    Profession = contingent.Profession,
                    ExperienceTotal = string.Empty,
                    ExperienceInOrg = FormatYears(person.EmploymentDate),
                    HarmfulFactors = contingent.EnvironmentFactors,
                    ActualLevel = string.Empty,
                    ConditionsClass = string.Empty,
                    Order988n = contingent.Order988n,
                    Order29n = contingent.Order29n,
                    LastExamLpu = lastExam is null ? string.Empty : lastExam.ExamDate.ToString("dd.MM.yyyy"),
                    LastExamCpp = string.Empty
                };

                if (ov is not null)
                {
                    ApplyOverride(row, ov);
                }

                result.Add(row);
            }
        }

        for (var i = 0; i < result.Count; i++)
        {
            result[i].RowNumber = i + 1;
        }

        return result;
    }

    public static string MakePersonKey(
        string? snils,
        string? fullName,
        string? birthDate,
        string? department,
        string? profession)
    {
        // Ключ внутри ЮЛ: человек × подразделение × профессия
        // (один СНИЛС может быть в двух компаниях — overrides хранятся по ЮЛ отдельно;
        // внутри ЮЛ совместительство/две должности — разные строки).
        var digits = DigitsOnly(snils);
        var dept = (department ?? string.Empty).Trim();
        var prof = MedContingentListBuilder.NormalizeProfession(profession);
        if (digits.Length >= 11)
        {
            return "s:" + digits + "|" + dept + "|" + prof;
        }

        return "n:" + MedContingentListBuilder.NormalizePerson(fullName)
               + "|" + (birthDate ?? string.Empty).Trim()
               + "|" + dept
               + "|" + prof;
    }

    /// <summary>Новый ключ s:СНИЛС|отдел|профессия; старые overrides могли быть просто s:СНИЛС.</summary>
    private static MedContingentNamedOverride? ResolveNamedOverride(
        IReadOnlyDictionary<string, MedContingentNamedOverride> byKey,
        string key,
        string? snils)
    {
        if (byKey.TryGetValue(key, out var ov))
        {
            return ov;
        }

        var digits = DigitsOnly(snils);
        if (digits.Length >= 11 && byKey.TryGetValue("s:" + digits, out var legacy))
        {
            return legacy;
        }

        return null;
    }

    private static void ApplyOverride(MedContingentNamedListRow row, MedContingentNamedOverride ov)
    {
        if (ov.FullName is not null) row.FullName = ov.FullName.Trim();
        if (ov.BirthDate is not null) row.BirthDate = ov.BirthDate.Trim();
        if (ov.Snils is not null) row.Snils = ov.Snils.Trim();
        if (ov.Department is not null) row.Department = ov.Department.Trim();
        if (ov.Profession is not null) row.Profession = ov.Profession.Trim();
        if (ov.ExperienceTotal is not null) row.ExperienceTotal = ov.ExperienceTotal.Trim();
        // Стаж в организации всегда от даты трудоустройства — override не применяем.
        if (ov.HarmfulFactors is not null) row.HarmfulFactors = ov.HarmfulFactors;
        if (ov.ActualLevel is not null) row.ActualLevel = ov.ActualLevel.Trim();
        if (ov.ConditionsClass is not null) row.ConditionsClass = ov.ConditionsClass.Trim();
        if (ov.Order988n is not null) row.Order988n = ov.Order988n;
        if (ov.Order29n is not null) row.Order29n = ov.Order29n;
        if (ov.LastExamLpu is not null) row.LastExamLpu = ov.LastExamLpu.Trim();
        if (ov.LastExamCpp is not null) row.LastExamCpp = ov.LastExamCpp.Trim();
    }

    private static string DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>Стаж в организации от даты трудоустройства: годы с одной десятичной (6 мес → 0,5).</summary>
    private static string FormatYears(DateTime? employmentDate, DateTime? asOf = null)
    {
        if (employmentDate is null || employmentDate.Value.Year < 1900)
        {
            return string.Empty;
        }

        var start = employmentDate.Value.Date;
        var end = (asOf ?? DateTime.Today).Date;
        if (end < start)
        {
            return "0";
        }

        var months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day)
        {
            months--;
        }

        if (months < 0)
        {
            months = 0;
        }

        var years = months / 12.0;
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        if (Math.Abs(years - Math.Round(years)) < 0.001)
        {
            return ((int)Math.Round(years)).ToString(culture);
        }

        return years.ToString("0.#", culture);
    }
}
