namespace Zashita.Core.Ppe;

public static class PpeWizardSizes
{
    public static bool IsFemaleSex(string? sex) =>
        (sex ?? string.Empty).Contains("жен", StringComparison.OrdinalIgnoreCase);

    public static string SexLabel(bool female) => female ? "жен." : "муж.";

    public static EmployeePpeSizes Build(
        string? sex,
        string? height,
        string? clothes,
        string? shoe,
        string? headwear,
        string? glove,
        string? gasMask = null)
    {
        var female = IsFemaleSex(sex);
        var raw = new EmployeePpeSizes(
            (height ?? string.Empty).Trim(),
            PpeSizeReference.ShortClothesFromFull(clothes ?? string.Empty),
            (clothes ?? string.Empty).Trim(),
            (shoe ?? string.Empty).Trim(),
            (headwear ?? string.Empty).Trim(),
            (glove ?? string.Empty).Trim(),
            (gasMask ?? string.Empty).Trim(),
            string.Empty);

        return PpeSizeReference.SanitizeForGender(female, raw);
    }

    public static EmployeePpeSizes DefaultsForSex(string? sex) =>
        Build(sex, null, null, null, null, null);

    public static Dictionary<string, EmployeePpeSizes> MapForEmployees(
        IEnumerable<string> employees,
        string? sex,
        string? height,
        string? clothes,
        string? shoe,
        string? headwear,
        string? glove,
        string? gasMask = null)
    {
        var sizes = Build(sex, height, clothes, shoe, headwear, glove, gasMask);
        var map = new Dictionary<string, EmployeePpeSizes>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            map[employee.Trim()] = sizes;
        }

        return map;
    }
}
