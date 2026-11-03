namespace Zashita.Core.Employees;

/// <summary>
/// Seed штата для демо. В портфолио пусто — люди только из App_Data (вымышленные).
/// </summary>
public static class OrganizationEmployeesCatalog
{
    public static IReadOnlyDictionary<string, IReadOnlyList<OrganizationEmployee>> SeedData { get; } =
        new Dictionary<string, IReadOnlyList<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
}
