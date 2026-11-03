using System.Text.RegularExpressions;

namespace Zashita.Core.Incidents;

public static class IncidentNumberHelper
{
    public static string NextNumber(string prefix, IEnumerable<string?> numbers, int pad = 8)
    {
        var max = 0;
        foreach (var number in numbers)
        {
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var match = Regex.Match(number, @"(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value) && value > max)
            {
                max = value;
            }
        }

        return $"{prefix}{(max + 1).ToString($"D{pad}")}";
    }
}
