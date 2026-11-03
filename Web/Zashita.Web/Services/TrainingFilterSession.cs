using System.Text.Json;

namespace Zashita.Web.Services;

public static class TrainingFilterSession
{
    private const string PlanningKindsKey = "Training.Planning.SelectedKinds";
    private const string CalendarKindsKey = "Training.Calendar.SelectedKinds";
    private static readonly JsonSerializerOptions JsonOpts = new();

    public static IReadOnlyList<string> LoadPlanningKinds(HttpContext http)
    {
        var json = http.Session.GetString(PlanningKindsKey);
        return Deserialize(json);
    }

    public static void SavePlanningKinds(HttpContext http, IEnumerable<string>? kinds) =>
        http.Session.SetString(PlanningKindsKey, Serialize(kinds));

    public static IReadOnlyList<string> LoadCalendarKinds(HttpContext http)
    {
        var json = http.Session.GetString(CalendarKindsKey);
        return Deserialize(json);
    }

    public static void SaveCalendarKinds(HttpContext http, IEnumerable<string>? kinds) =>
        http.Session.SetString(CalendarKindsKey, Serialize(kinds));

    private static string Serialize(IEnumerable<string>? kinds) =>
        JsonSerializer.Serialize(
            kinds?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            JsonOpts);

    private static List<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts)?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
