using System.Net;
using System.Text;
using Zashita.Core.Brand;

namespace Zashita.Core.Training;

public static class TrainingProgramPrintBuilder
{
    public static string Build(TrainingProgramCourseNode program, string mode, string organization)
    {
        var title = ResolveTitle(mode);
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8" />
              <title>
            """);
        sb.Append(WebUtility.HtmlEncode(title));
        sb.Append("</title>");
        sb.Append(SiteIconHtml.HeadLinks);
        sb.Append("""
              <style>
                body { font-family: "Times New Roman", serif; font-size: 12pt; margin: 24px; color: #111; }
                h1 { font-size: 16pt; text-align: center; margin: 0 0 8px; }
                h2 { font-size: 13pt; margin: 18px 0 8px; }
                .meta { width: 100%; border-collapse: collapse; margin: 12px 0 18px; }
                .meta td { border: 1px solid #333; padding: 6px 8px; vertical-align: top; }
                .meta .label { width: 28%; background: #f5f5f5; font-weight: 700; }
                .content p { margin: 0 0 10px; text-align: justify; line-height: 1.35; }
                .topics { margin: 0; padding-left: 22px; }
                .topics li { margin-bottom: 6px; }
                .signatures { margin-top: 28px; width: 100%; border-collapse: collapse; }
                .signatures td { padding: 10px 0; vertical-align: bottom; }
                .line { border-bottom: 1px solid #111; min-width: 180px; display: inline-block; }
              </style>
            </head>
            <body>
            """);

        sb.Append("<h1>").Append(WebUtility.HtmlEncode(title)).Append("</h1>");
        sb.Append("<p style=\"text-align:center;margin:0 0 18px;\">")
            .Append(WebUtility.HtmlEncode(organization))
            .Append("</p>");

        sb.Append("""
            <table class="meta">
              <tr><td class="label">Программа</td><td>
            """);
        sb.Append(WebUtility.HtmlEncode(program.Name));
        sb.Append("</td></tr><tr><td class=\"label\">Раздел</td><td>");
        sb.Append(WebUtility.HtmlEncode(program.Section));
        sb.Append("</td></tr><tr><td class=\"label\">Трудоёмкость</td><td>");
        sb.Append(program.TotalHours > 0 ? $"{program.TotalHours:0.##} ч." : "—");
        sb.Append("</td></tr><tr><td class=\"label\">Дата утверждения</td><td>");
        sb.Append(program.ApprovalDate?.ToString("dd.MM.yyyy") ?? "—");
        sb.Append("</td></tr></table>");

        AppendBody(sb, program, mode);

        sb.Append("""
            <table class="signatures">
              <tr>
                <td>Утверждаю:</td>
                <td><span class="line">&nbsp;</span></td>
                <td><span class="line">&nbsp;</span></td>
              </tr>
              <tr>
                <td></td>
                <td style="font-size:10pt;text-align:center;">(должность)</td>
                <td style="font-size:10pt;text-align:center;">(подпись, Ф.И.О.)</td>
              </tr>
            </table>
            </body></html>
            """);

        return sb.ToString();
    }

    private static string ResolveTitle(string mode) => mode switch
    {
        "order" => "Приказ об утверждении программы инструктажа",
        "briefing" => "Программа инструктажа",
        "detailed" => "Программа подробная",
        "detailed-groups" => "Программа подробная (с группами)",
        "custom" => "Настраиваемый комплект документов",
        _ => "Программа"
    };

    private static void AppendBody(StringBuilder sb, TrainingProgramCourseNode program, string mode)
    {
        sb.Append("<div class=\"content\">");

        if (mode == "order")
        {
            sb.Append("<p>Утвердить программу «")
                .Append(WebUtility.HtmlEncode(program.Name))
                .Append("» и ввести её в действие с даты подписания настоящего приказа.</p>");
            sb.Append("<p>Ответственным за организацию обучения по данной программе назначить специалиста по охране труда.</p>");
            sb.Append("</div>");
            return;
        }

        sb.Append("<h2>1. Цель программы</h2><p>Обеспечить подготовку работников по теме «")
            .Append(WebUtility.HtmlEncode(program.Name))
            .Append("» в соответствии с требованиями законодательства и локальных актов организации.</p>");

        sb.Append("<h2>2. Категории обучающихся</h2><p>Работники, выполняющие работы в разделе «")
            .Append(WebUtility.HtmlEncode(program.Section))
            .Append("».</p>");

        var topics = BuildTopics(program, mode);
        sb.Append("<h2>3. Содержание программы</h2><ul class=\"topics\">");
        foreach (var topic in topics)
        {
            sb.Append("<li>").Append(WebUtility.HtmlEncode(topic)).Append("</li>");
        }
        sb.Append("</ul>");

        sb.Append("<h2>4. Форма проведения</h2><p>Лекция, инструктаж, практическое занятие, проверка знаний.</p>");
        sb.Append("<h2>5. Контроль знаний</h2><p>Устный опрос, тестирование или практическая демонстрация навыков.</p>");
        sb.Append("</div>");
    }

    private static IReadOnlyList<string> BuildTopics(TrainingProgramCourseNode program, string mode)
    {
        var baseTopics = new List<string>
        {
            "Нормативные правовые акты и локальные документы по охране труда",
            "Опасные и вредные производственные факторы на рабочем месте",
            "Требования безопасности при выполнении работ",
            "Средства индивидуальной защиты и порядок их применения",
            "Действия работника при несчастном случае и при пожаре"
        };

        if (program.Section.Contains("Пожар", StringComparison.OrdinalIgnoreCase))
        {
            baseTopics.Insert(1, "Пожарная опасность объекта и меры пожарной безопасности");
        }

        if (mode == "briefing")
        {
            return baseTopics.Take(4).ToList();
        }

        if (mode == "detailed-groups")
        {
            return
            [
                "Раздел I. Общие положения",
                ..baseTopics.Take(2).Select(t => "Раздел II. " + t),
                ..baseTopics.Skip(2).Select(t => "Раздел III. " + t)
            ];
        }

        if (mode == "custom")
        {
            return
            [
                "Комплект включает программу, приказ об утверждении и лист ознакомления",
                ..baseTopics
            ];
        }

        return mode == "detailed" ? baseTopics : baseTopics.Take(5).ToList();
    }
}
