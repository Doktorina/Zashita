using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Печать реестра опасностей (с выбором позиций).</summary>
public static class RiskHazardCatalogPrintBuilder
{
    public static string Build(
        string organization,
        IReadOnlyList<RiskHazardCatalogItem> items,
        IReadOnlySet<string>? selectedIds = null,
        string? chairmanName = null,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var rows = RiskHazardNumberComparer.OrderByNumber(
            items
                .Where(i => i.IsActive)
                .Where(i => selectedIds is null || selectedIds.Count == 0 || selectedIds.Contains(i.Id)));

        var sb = new StringBuilder();
        sb.Append("""<div class="risks-workplaces-sheet">""");
        sb.Append("""<h1 class="risks-wp-title">Перечень (реестр) опасностей</h1>""");
        if (!string.IsNullOrWhiteSpace(organization))
        {
            sb.Append($"<p class=\"risks-wp-org-center\">{Web(organization.Trim())}</p>");
        }

        sb.Append("""
            <table class="risks-wp-table risks-wp-table-2col">
              <thead>
                <tr>
                  <th>Номер</th>
                  <th>Опасность</th>
                </tr>
              </thead>
              <tbody>
            """);

        if (rows.Count == 0)
        {
            sb.Append("""<tr><td colspan="2" class="risks-wp-empty">Нет выбранных опасностей.</td></tr>""");
        }
        else
        {
            foreach (var item in rows)
            {
                sb.Append("<tr>");
                sb.Append($"<td class=\"risks-wp-col-n\">{Web(item.Number)}</td>");
                sb.Append($"<td>{Web(item.Title)}</td>");
                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table>");
        var chair = chairmanName;
        if (!RiskPrintChrome.IsUsablePersonChairman(chair, organization))
        {
            chair = RiskPrintChrome.ResolveChairmanInitials(organization, employees);
        }

        if (string.IsNullOrWhiteSpace(chair) || RiskPrintChrome.LooksLikeOrganizationName(chair))
        {
            chair = "\u00a0";
        }

        RiskPrintChrome.AppendChairmanFooter(sb, chair);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
