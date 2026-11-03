using Zashita.Core.Employees;
using Zashita.Core.Med;

namespace Zashita.Web.Services;

/// <summary>Подтягивает ФИО/подразделение/должность в медосмотрах из штата 1С.</summary>
public sealed class MedExamRosterAligner
{
    private readonly OrganizationEmployeesService _employees;
    private readonly MedExamStore _exams;

    public MedExamRosterAligner(OrganizationEmployeesService employees, MedExamStore exams)
    {
        _employees = employees;
        _exams = exams;
    }

    public int AlignExamsToRoster(IEnumerable<string> organizations)
    {
        var orgList = organizations.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        if (orgList.Count == 0)
        {
            return 0;
        }

        var roster = EmployeeRosterIndex.Build(_employees.LoadPairsForReport(orgList));
        var list = _exams.LoadAll();
        var changed = 0;
        var removed = 0;
        var kept = new List<MedExamRecord>(list.Count);
        foreach (var exam in list)
        {
            if (!roster.TryResolve(exam.Organization, exam.EmployeeName, out var employee))
            {
                if (!EmployeeNameMatcher.IsPlausiblePersonName(exam.EmployeeName))
                {
                    removed++;
                    continue;
                }

                exam.Department = EmployeeContextValidator.NormalizeDepartment(exam.Department);
                exam.Profession = EmployeeContextValidator.NormalizeProfession(exam.Profession);
                kept.Add(exam);
                continue;
            }

            var touched = false;
            if (!string.Equals(exam.EmployeeName, employee.Name, StringComparison.OrdinalIgnoreCase))
            {
                exam.EmployeeName = employee.Name;
                touched = true;
            }

            var department = EmployeeContextValidator.NormalizeDepartment(
                string.IsNullOrWhiteSpace(employee.Department) ? exam.Department : employee.Department);
            if (!string.Equals(exam.Department, department, StringComparison.OrdinalIgnoreCase))
            {
                exam.Department = department;
                touched = true;
            }

            var profession = EmployeeContextValidator.NormalizeProfession(
                string.IsNullOrWhiteSpace(employee.Profession) ? exam.Profession : employee.Profession);
            if (!string.Equals(exam.Profession, profession, StringComparison.OrdinalIgnoreCase))
            {
                exam.Profession = profession;
                touched = true;
            }

            if (touched)
            {
                changed++;
            }

            kept.Add(exam);
        }

        if (changed > 0 || removed > 0)
        {
            _exams.SaveAll(kept);
        }

        return changed + removed;
    }
}
