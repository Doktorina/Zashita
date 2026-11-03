using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Risks;

/// <summary>
/// Печатные формы раздела «Подготовка проведения СОУТ».
/// </summary>
public static class SoutPreparationPrintBuilder
{
    public static string Build(
        SoutPreparationDocument document,
        IReadOnlyList<SoutPreparationPrintRow> rows,
        string? mode = null,
        SoutPreparationCommissionInfo? commission = null,
        SoutPreparationOrgHeader? orgHeader = null)
    {
        return SoutPreparationPrintModes.Normalize(mode) switch
        {
            SoutPreparationPrintModes.WorkplacesList => BuildWorkplacesList(document, rows, commission),
            SoutPreparationPrintModes.InfoNeeded => BuildInfoNeeded(document, rows, commission, orgHeader),
            _ => BuildEquipment(document, rows, commission)
        };
    }

    private static string BuildInfoNeeded(
        SoutPreparationDocument document,
        IReadOnlyList<SoutPreparationPrintRow> rows,
        SoutPreparationCommissionInfo? commission,
        SoutPreparationOrgHeader? orgHeader)
    {
        var sb = new StringBuilder();
        var contractDefault = string.IsNullOrWhiteSpace(document.Number)
            ? string.Empty
            : $"№ {document.Number.Trim()} от {document.Date:dd.MM.yyyy}";
        var orgDefault = string.IsNullOrWhiteSpace(document.Organization)
            ? string.Empty
            : document.Organization.Trim();
        var contractorDefault = string.IsNullOrWhiteSpace(document.ContractorOrganization)
            ? string.Empty
            : document.ContractorOrganization.Trim();
        var addressDefault = (orgHeader?.Address ?? string.Empty).Trim();
        var contactDefault = (orgHeader?.Contact ?? string.Empty).Trim();
        var emailDefault = (orgHeader?.Email ?? string.Empty).Trim();
        var phoneDefault = (orgHeader?.Phone ?? string.Empty).Trim();

        sb.Append("""
            <div class="sout-info-doc">
              <div class="sout-info-header">
            """);
        // Компактная шапка как в образце: договор|адрес; организация; контакт|email|телефон.
        sb.Append($"""
                <div class="sout-info-meta-row sout-info-meta-row--split">
                  <span class="sout-info-meta-label">Номер и дата договора:</span>
                  <span class="sout-info-meta-value" contenteditable="true" data-override-key="infoContract">{E(BlankField(MetaValue(document, "infoContract", contractDefault)))}</span>
                  <span class="sout-info-meta-label">Адрес проведения замеров:</span>
                  <span class="sout-info-meta-value sout-info-meta-value--wide" contenteditable="true" data-override-key="infoAddress">{E(BlankField(MetaValue(document, "infoAddress", addressDefault)))}</span>
                </div>
                <div class="sout-info-meta-row">
                  <span class="sout-info-meta-label">Наименование организации*:</span>
                  <span class="sout-info-meta-value" contenteditable="true" data-override-key="infoOrg">{E(BlankField(MetaValue(document, "infoOrg", orgDefault)))}</span>
                </div>
                <div class="sout-info-meta-row sout-info-meta-row--split">
                  <span class="sout-info-meta-label">Контактное (ответственное) лицо</span>
                  <span class="sout-info-meta-value" contenteditable="true" data-override-key="infoContact">{E(BlankField(MetaValue(document, "infoContact", contactDefault)))}</span>
                  <span class="sout-info-meta-label">E-mail</span>
                  <span class="sout-info-meta-value" contenteditable="true" data-override-key="infoEmail">{E(BlankField(MetaValue(document, "infoEmail", emailDefault)))}</span>
                  <span class="sout-info-meta-label">телефон</span>
                  <span class="sout-info-meta-value" contenteditable="true" data-override-key="infoPhone">{E(BlankField(MetaValue(document, "infoPhone", phoneDefault)))}</span>
                </div>
            """);
        sb.Append("""
              </div>
              <div class="sout-info-title">Информация, необходимая для проведения СОУТ</div>
              <table class="sout-info-table sout-info-table--form sout-info-table--21">
                <colgroup>
                  <!-- На всю ширину листа; кол.12 (оборудование) шире — горизонтальный заголовок -->
                  <col style="width:3.2%" /><col style="width:5.5%" /><col style="width:5.2%" /><col style="width:4.0%" />
                  <col style="width:8.5%" /><col style="width:6.5%" /><col style="width:2.8%" />
                  <col style="width:2.4%" /><col style="width:2.4%" /><col style="width:2.4%" /><col style="width:2.4%" />
                  <col style="width:16.5%" /><col style="width:2.8%" />
                  <col style="width:2.4%" /><col style="width:2.4%" /><col style="width:2.4%" /><col style="width:2.4%" /><col style="width:2.4%" />
                  <col style="width:3.0%" /><col style="width:4.0%" /><col style="width:4.4%" />
                </colgroup>
                <thead>
                  <tr>
            """);
        // Заголовки 1в1 с шаблоном Дубровского (IP_Dubrovskiy … SOUT_2025_171).
        sb.Append(VerticalTh("Индивидуальный №"));
        sb.Append(VerticalTh("Структурное подразделение"));
        sb.Append(VerticalTh("Должность (по штатному расписанию)"));
        sb.Append(VerticalTh("код профессии ОК 016-94"));
        sb.Append("""
                    <th rowspan="2" class="sout-info-h">ФИО<br/>(полностью)</th>
                    <th rowspan="2" class="sout-info-h">СНИЛС</th>
            """);
        sb.Append(VerticalTh("Продолжительность рабочего дня/недели (в часах)"));
        sb.Append("""
                    <th colspan="4">Количество работников на данном<br/>рабочем месте</th>
                    <th rowspan="2" class="sout-info-equip">Используемое оборудование, материалы, инструмент, включая ПЭВМ, МФУ, принтер.<br/>(трансп. средство: марка, модель, гос. номер, г/вып, тип топлива)<br/>Краткое описание выполняемых работ (для всех кроме офисных работников)</th>
            """);
        sb.Append(VerticalTh("Время работы на данном оборудовании (часов в смену)"));
        sb.Append("""
                    <th colspan="5">Гарантии и компенсации<br/>(за вредные условия труда)<br/>(что выдается фактически) (да/нет)</th>
            """);
        sb.Append(VerticalTh("Проф. мед. Осмотры (да/нет)"));
        sb.Append(VerticalTh("Класс условий труда по результатам предыдущей аттестации рабочих мест"));
        sb.Append(VerticalTh("Наличие несчастного случая или профзаболевания"));
        sb.Append("""
                  </tr>
                  <tr>
            """);
        sb.Append(VerticalTh("Всего", rowspan: false));
        sb.Append(VerticalTh("из них женщин", rowspan: false));
        sb.Append(VerticalTh("из них лиц до 18 лет", rowspan: false));
        sb.Append(VerticalTh("из них инвалидов", rowspan: false));
        sb.Append(VerticalTh("повышенная оплата труда", rowspan: false));
        sb.Append(VerticalTh("ежегодн доп. отпуск", rowspan: false));
        sb.Append(VerticalTh("сокращенная продолжительность рабочего времени", rowspan: false));
        sb.Append(VerticalTh("молоко/ЛПУ", rowspan: false));
        sb.Append(VerticalTh("досрочное назначение трудовой пенсии", rowspan: false));
        sb.Append("""
                  </tr>
                  <tr class="sout-info-col-numbers">
                    <th>1</th><th>2</th><th>3</th><th>4</th><th>5</th><th>6</th><th>7</th>
                    <th>8</th><th>9</th><th>10</th><th>11</th><th>12</th><th>13</th>
                    <th>14</th><th>15</th><th>16</th><th>17</th><th>18</th>
                    <th>19</th><th>20</th><th>21</th>
                  </tr>
                </thead>
                <tbody>
            """);

        var list = rows.Count > 0
            ? rows
            : Enumerable.Range(1, 1).Select(i => new SoutPreparationPrintRow { Index = i }).ToList();

        foreach (var row in list)
        {
            sb.Append("<tr>");
            sb.Append(Td(document, row.Index, "workplaceNumber", Dash(row.WorkplaceNumber), "sout-info-c"));
            sb.Append(Td(document, row.Index, "department", row.Department, null, $"data-row=\"{row.Index}\" data-autofill-target=\"department\""));
            sb.Append(Td(document, row.Index, "profession", row.Profession, null, $"data-row=\"{row.Index}\" data-autofill-target=\"profession\""));
            sb.Append(Td(document, row.Index, "okCode", Dash(row.OkCode), "sout-info-c", $"data-row=\"{row.Index}\" data-autofill-target=\"okCode\""));
            sb.Append(Td(document, row.Index, "employeeName", row.EmployeeName, null,
                $"""data-row="{row.Index}" data-autofill="fio" """));
            sb.Append(Td(document, row.Index, "snils", Dash(row.Snils), "sout-info-c", $"data-row=\"{row.Index}\" data-autofill-target=\"snils\""));
            sb.Append(Td(document, row.Index, "workWeekHours", FirstOr(row.WorkWeekHours, "40"), "sout-info-c"));
            sb.Append(Td(document, row.Index, "totalWorkers", FirstOr(row.TotalWorkers, "1"), "sout-info-c"));
            sb.Append(Td(document, row.Index, "womenCount", ZeroAsDash(row.WomenCount), "sout-info-c"));
            sb.Append(Td(document, row.Index, "minorsCount", ZeroAsDash(row.MinorsCount), "sout-info-c"));
            sb.Append(Td(document, row.Index, "disabledCount", ZeroAsDash(row.DisabledCount), "sout-info-c"));
            sb.Append(Td(document, row.Index, "equipment", FirstOr(row.WorkDescription, row.Equipment)));
            sb.Append(Td(document, row.Index, "equipmentHours", FirstOr(row.EquipmentHours, "6"), "sout-info-c"));
            sb.Append(Td(document, row.Index, "extraPay", Dash(row.ExtraPayPercent), "sout-info-c"));
            sb.Append(Td(document, row.Index, "extraLeave", Dash(row.ExtraLeave), "sout-info-c"));
            sb.Append(Td(document, row.Index, "shortDay", Dash(row.ShortDay), "sout-info-c"));
            sb.Append(Td(document, row.Index, "milk", Dash(row.Milk), "sout-info-c"));
            sb.Append(Td(document, row.Index, "pension", Dash(row.PensionBenefits), "sout-info-c"));
            sb.Append(Td(document, row.Index, "medicalExam", FirstOr(row.MedicalExam, "да"), "sout-info-c"));
            sb.Append(Td(document, row.Index, "prevClass", Dash(row.PreviousConditionsClass), "sout-info-c"));
            sb.Append(Td(document, row.Index, "accident", FirstOr(row.AccidentOrDisease, "нет"), "sout-info-c"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");

        // Сноски под таблицей — на альбомном листе (как в образце Дубровского).
        sb.Append("""
              <div class="sout-info-footnotes">
                <p class="sout-info-footnote-req">*Все столбцы обязательные для заполнения.</p>
                <p class="sout-info-footnote-law">*Согласно п/п 4 п.2 ст.6 ФЗ-426 «О проведении специальной оценки условий труда»:</p>
              </div>
            """);

        // Закрывающий блок — строго одна книжная страница (правила + сводная + комиссия + подписи).
        sb.Append("""
              <div class="sout-info-tail">
            """);

        sb.Append($"""
              <div class="sout-info-rules" contenteditable="true" data-override-key="infoRules">
                {InfoRulesHtml(document)}
              </div>
            """);

        var workplacesTotal = document.SelectedWorkplaceIds.Count > 0
            ? document.SelectedWorkplaceIds.Count
            : list
                .Select(r => $"{r.Department}|{r.Profession}|{r.WorkplaceNumber}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        var employeesTotal = list.Count(r => !string.IsNullOrWhiteSpace(r.EmployeeName));
        if (employeesTotal <= 0)
        {
            employeesTotal = list.Sum(r =>
            {
                if (int.TryParse(r.TotalWorkers, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                {
                    return n;
                }

                return 1;
            });
        }

        sb.Append($"""
              <div class="sout-info-summary-lead">Для заполнения Сводной ведомости необходимо заполнить таблицу:</div>
              <table class="sout-info-summary">
                <thead>
                  <tr>
                    <th>Наименование</th>
                    <th>Количество рабочих мест и численность работников, занятых на этих рабочих местах, ВСЕГО по предприятию</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>Рабочие места (ед.)</td>
                    <td contenteditable="true" data-override-key="summaryWorkplaces" class="sout-info-c">{E(Override(document, "summaryWorkplaces", workplacesTotal.ToString(CultureInfo.InvariantCulture)))}</td>
                  </tr>
                  <tr>
                    <td>Работники, занятые на рабочих местах (чел.)</td>
                    <td contenteditable="true" data-override-key="summaryEmployees" class="sout-info-c">{E(Override(document, "summaryEmployees", employeesTotal.ToString(CultureInfo.InvariantCulture)))}</td>
                  </tr>
                  <tr>
                    <td>из них женщин</td>
                    <td contenteditable="true" data-override-key="summaryWomen" class="sout-info-c">{E(Override(document, "summaryWomen", ""))}</td>
                  </tr>
                  <tr>
                    <td>из них лиц в возрасте до 18 лет</td>
                    <td contenteditable="true" data-override-key="summaryMinors" class="sout-info-c">{E(Override(document, "summaryMinors", ""))}</td>
                  </tr>
                  <tr>
                    <td>из них инвалидов</td>
                    <td contenteditable="true" data-override-key="summaryDisabled" class="sout-info-c">{E(Override(document, "summaryDisabled", ""))}</td>
                  </tr>
                </tbody>
              </table>
            """);

        AppendCommissionTable(sb, document, commission);

        sb.Append($"""
              <div class="sout-info-signs">
                <div class="sout-info-sign-block">
                  <div class="sout-info-sign-role">Исполнитель</div>
                  <div class="sout-info-sign-org" contenteditable="true" data-override-key="infoContractor">{E(BlankField(Override(document, "infoContractor", contractorDefault)))}</div>
                  <div class="sout-info-sign-shelf" aria-hidden="true"></div>
                  <div class="sout-info-sign-mp">М.П.</div>
                </div>
                <div class="sout-info-sign-block">
                  <div class="sout-info-sign-role">Заказчик</div>
                  <div class="sout-info-sign-org" contenteditable="true" data-override-key="infoClient">{E(BlankField(Override(document, "infoClient", orgDefault)))}</div>
                  <div class="sout-info-sign-shelf" aria-hidden="true"></div>
                </div>
              </div>
              </div>
            </div>
            """);

        return sb.ToString();
    }

    private static string InfoRulesHtml(SoutPreparationDocument document)
    {
        // Текст 1в1 с образцом (Дубровский): курсив+подчёркивание у вводной в «ёлочках», дальше обычные строки.
        const string defaultRules =
            "«Организация проводящая специальную оценку условий труда, обязана не приступать к проведению специальной оценке условий труда либо приостанавливать ее проведение в случае непредставления работодателем необходимых сведений, документов и информации, которые предусмотрены гражданско-правовым договором, и которые характеризуют условия труда на рабочих местах, а так же разъяснений по вопросам проведения специальной оценки условий труда»\n" +
            "Столбец № 1 – индивидуальный номер рабочего места (должен повторяться как был в предыдущей СОУТ или следующий после последнего номера действующей СОУТ)\n" +
            "Столбец № 4 – код профессии из Общероссийского классификатора профессий рабочих, должностей служащих и тарифных разрядов ОК 016-94\n" +
            "Столбец № 7 – если работники работают по сменам, обязательно указать продолжительность смены и кол-во человек в смену\n" +
            "Столбец № 12 – для всех кроме офисных работников обязательно нужно прописывать краткое описание выполняемых работ, место их выполнения (например: цех, гараж, строительная площадка и т.д., % рабочего времени который работник проводит на данном рабочем месте)\n" +
            "Столбец № 14-18 – проставляется фактическое наличие да/нет (ОБЯЗАТЕЛЬНО! Пропускать или игнорировать нельзя)\n" +
            "Столбец № 20 – заполняется если ранее была проведена специальная оценка условий труда\n" +
            "Столбец № 21 – указать наличие несчастного случая на производстве или профзаболевания (да/нет)";

        if (!document.PrintOverrides.TryGetValue("infoRules", out var custom) || string.IsNullOrWhiteSpace(custom))
        {
            var html = E(defaultRules).Replace("\n", "<br/>");
            var br = html.IndexOf("<br/>", StringComparison.Ordinal);
            if (br > 0)
            {
                html =
                    "<span class=\"sout-info-rules-lead\">" + html[..br] + "</span>" +
                    html[br..];
            }

            return html;
        }

        // Старые короткие/с жёлтым выделением overrides — подменяем на актуальный образец.
        var customNorm = custom.Replace("\r\n", "\n").Trim();
        if (customNorm.Contains("УНИКАЛЬНЫЙ", StringComparison.OrdinalIgnoreCase)
            || customNorm.Contains("016-2025", StringComparison.OrdinalIgnoreCase)
            || customNorm.Contains("• Столбец", StringComparison.Ordinal)
            || customNorm.Contains("sout-info-hl", StringComparison.OrdinalIgnoreCase)
            || customNorm.Length < 400)
        {
            var html = E(defaultRules).Replace("\n", "<br/>");
            var br = html.IndexOf("<br/>", StringComparison.Ordinal);
            if (br > 0)
            {
                html =
                    "<span class=\"sout-info-rules-lead\">" + html[..br] + "</span>" +
                    html[br..];
            }

            return html;
        }

        return E(custom).Replace("\n", "<br/>");
    }

    private static void AppendCommissionTable(
        StringBuilder sb,
        SoutPreparationDocument document,
        SoutPreparationCommissionInfo? commission)
    {
        var rows = new List<(string Status, string Position, string Name)>();
        var chairmanProfession = CommOverride(
            document,
            "infoComm1Position",
            string.IsNullOrWhiteSpace(commission?.ChairmanProfession) ? string.Empty : commission!.ChairmanProfession);
        var chairmanName = CommOverride(
            document,
            "infoComm1Name",
            string.IsNullOrWhiteSpace(commission?.ChairmanName) ? string.Empty : commission!.ChairmanName);
        rows.Add((
            Override(document, "infoComm1Status", "Председатель комиссии по СОУТ"),
            chairmanProfession,
            chairmanName));

        var members = (commission?.Members ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Name) || !string.IsNullOrWhiteSpace(m.Profession))
            .Take(2)
            .ToList();
        for (var i = 0; i < members.Count; i++)
        {
            var posKey = $"infoComm{i + 2}Position";
            var nameKey = $"infoComm{i + 2}Name";
            rows.Add((
                Override(document, $"infoComm{i + 2}Status", $"Член комиссии {i + 1}"),
                CommOverride(document, posKey, members[i].Profession),
                CommOverride(document, nameKey, members[i].Name)));
        }

        while (rows.Count < 5)
        {
            var n = rows.Count;
            var statusDefault = n == 0
                ? "Председатель комиссии по СОУТ"
                : (n <= 2 ? $"Член комиссии {n}" : "…");
            rows.Add((
                Override(document, $"infoComm{n + 1}Status", statusDefault),
                CommOverride(document, $"infoComm{n + 1}Position", string.Empty),
                CommOverride(document, $"infoComm{n + 1}Name", string.Empty)));
        }

        // Без отдельного заголовка — как в образце: сразу таблица после сводной.
        sb.Append("""
              <table class="sout-info-commission">
                <thead>
                  <tr>
                    <th>Статус (1 — председатель, 2 и более — члены комиссии)</th>
                    <th>Должность (полностью)</th>
                    <th>ФИО (расшифровать)</th>
                  </tr>
                </thead>
                <tbody>
            """);
        for (var i = 0; i < rows.Count; i++)
        {
            var n = i + 1;
            var row = rows[i];
            var status = NormalizeCommStatus(row.Status, n);
            sb.Append("<tr>");
            sb.Append($"""<td contenteditable="true" data-override-key="infoComm{n}Status">{E(status)}</td>""");
            sb.Append($"""<td contenteditable="true" data-override-key="infoComm{n}Position" data-autofill-target="commPosition" data-comm-row="{n}">{E(row.Position)}</td>""");
            sb.Append($"""<td contenteditable="true" data-override-key="infoComm{n}Name" data-autofill="fio-comm" data-comm-row="{n}">{E(row.Name)}</td>""");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("""
              <p class="sout-wp-edit-hint ppe-no-print">В комиссии: выбор ФИО из списка подставляет должность. Затем «Записать».</p>
            """);
    }

    private static string NormalizeCommStatus(string? status, int rowNumber)
    {
        var s = (status ?? string.Empty).Trim();
        if (s.Length == 0 || s == "1")
        {
            return rowNumber == 1 ? "Председатель комиссии по СОУТ" : $"Член комиссии {Math.Max(1, rowNumber - 1)}";
        }

        if (s == "2")
        {
            return $"Член комиссии {Math.Max(1, rowNumber - 1)}";
        }

        return s;
    }

    /// <summary>Пустой сохранённый override не затирает должность/ФИО, найденные по справочнику.</summary>
    private static string CommOverride(SoutPreparationDocument document, string key, string value)
    {
        if (!document.PrintOverrides.TryGetValue(key, out var overridden))
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(overridden) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return overridden;
    }

    private static string MetaLine(SoutPreparationDocument document, string key, string label, string value) =>
        $"""
        <div class="sout-info-meta-line">
          <span class="sout-info-meta-label">{E(label)}:</span>
          <span class="sout-info-meta-value" contenteditable="true" data-override-key="{E(key)}">{E(BlankField(Override(document, key, value)))}</span>
        </div>
        """;

    /// <summary>Пустое поле — неразрывный пробел, чтобы полка (border-bottom) не схлопывалась.</summary>
    private static string BlankField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\u00A0";
        }

        var trimmed = value.Trim();
        var stripped = trimmed
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("—", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);
        return stripped.Length == 0 ? "\u00A0" : trimmed;
    }

    private static string BuildEquipment(
        SoutPreparationDocument document,
        IReadOnlyList<SoutPreparationPrintRow> rows,
        SoutPreparationCommissionInfo? commission)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <div class="sout-eq-doc">
              <div class="sout-eq-org-line" contenteditable="true" data-override-key="orgName">{E(Override(document, "orgName", document.Organization))}</div>
              <div class="sout-eq-org-hint">(полное наименование работодателя)</div>
              <div class="sout-eq-title">
                ПЕРЕЧЕНЬ ОБОРУДОВАНИЯ, ИНСТРУМЕНТОВ И ПРИСПОСОБЛЕНИЙ,<br/>
                применяемых на рабочих местах, а также используемых материалов и сырья
              </div>
              <table class="sout-eq-table">
                <thead>
                  <tr>
                    <th class="sout-eq-col-num">Индивидуальный номер рабочего места</th>
                    <th class="sout-eq-col-name">Наименование рабочего места (профессии, должности)</th>
                    <th class="sout-eq-col-equip">Перечень используемого на рабочем месте оборудования, инструментов и приспособлений, данные об их наличии и состоянии (по каждому рабочему месту)</th>
                    <th class="sout-eq-col-mat">Используемые материалы и сырье</th>
                  </tr>
                  <tr class="sout-eq-col-numbers">
                    <th>1</th><th>2</th><th>3</th><th>4</th>
                  </tr>
                </thead>
                <tbody>
            """);

        var list = rows.Count > 0
            ? rows
            : Enumerable.Range(1, 1).Select(i => new SoutPreparationPrintRow { Index = i }).ToList();
        foreach (var row in list)
        {
            sb.Append("<tr>");
            sb.Append(Td(document, row.Index, "workplaceNumber", row.WorkplaceNumber));
            sb.Append(Td(document, row.Index, "profession", row.Profession));
            sb.Append(Td(document, row.Index, "equipment", row.Equipment));
            sb.Append(Td(document, row.Index, "materials", row.Materials));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendCommission(sb, document, commission);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildWorkplacesList(
        SoutPreparationDocument document,
        IReadOnlyList<SoutPreparationPrintRow> rows,
        SoutPreparationCommissionInfo? commission)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <div class="sout-wp-doc">
              <div class="sout-wp-title">
                ПЕРЕЧЕНЬ<br/>
                <span class="sout-wp-title-sub">рабочих мест, подлежащих специальной оценке условий труда</span>
              </div>
            """);
        sb.Append($"""
              <div class="sout-wp-meta" contenteditable="true" data-override-key="wpOrg">
                {E(Override(document, "wpOrg", string.IsNullOrWhiteSpace(document.Organization)
                    ? "Организация: ______________________________"
                    : $"Организация: {document.Organization}"))}
              </div>
              <div class="sout-wp-meta" contenteditable="true" data-override-key="wpContractor">
                {E(Override(document, "wpContractor", string.IsNullOrWhiteSpace(document.ContractorOrganization)
                    ? "Организация, проводящая специальную оценку: ______________________________"
                    : $"Организация, проводящая специальную оценку: {document.ContractorOrganization}"))}
              </div>
              <table class="sout-wp-table">
                <thead>
                  <tr>
                    <th>№ п/п</th>
                    <th>Номер рабочего места</th>
                    <th>Наименование должности, профессии или специальности работника</th>
                    <th>Ф.И.О. работника</th>
                    <th>СНИЛС</th>
                    <th>Время работы (часов в смену)</th>
                    <th>Всего работающих / женщин / лиц до 18 лет / инвалидов (из них)</th>
                    <th>Перечень оборудования (тип, модель, год выпуска) и инструментов</th>
                    <th>Наименование, марка используемого сырья и материалов</th>
                    <th>Вредные и (или) опасные производственные факторы</th>
                    <th>Размер доплат за вредные условия труда в % к тарифной ставке, окладу</th>
                    <th>Молоко (М) или лечебно-профилактическое питание (Л) (М/Л/Нет)</th>
                    <th>Продолжительность дополнительного отпуска по списку (№ стр, раздела, пункта, к-во дней)</th>
                    <th>Сокращенный рабочий день (да/нет)</th>
                    <th>Льготное пенсионное обеспечение (№ списка, код профессии)</th>
                    <th>Необходимость мед. осмотров, № приказа, пункт</th>
                  </tr>
                  <tr class="sout-wp-col-numbers">
                    <th>1</th><th>2</th><th>3</th><th>4</th><th>5</th><th>6</th><th>7</th><th>8</th>
                    <th>9</th><th>10</th><th>11</th><th>12</th><th>13</th><th>14</th><th>15</th><th>16</th>
                  </tr>
                </thead>
                <tbody>
            """);

        var list = rows.Count > 0
            ? rows
            : Enumerable.Range(1, 1).Select(i => new SoutPreparationPrintRow { Index = i }).ToList();

        foreach (var row in list)
        {
            sb.Append("<tr>");
            sb.Append(Td(document, row.Index, "n", row.Index.ToString(CultureInfo.InvariantCulture), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "workplaceNumber", row.WorkplaceNumber, "sout-wp-c"));
            sb.Append(Td(document, row.Index, "profession", row.Profession));
            sb.Append(Td(document, row.Index, "employeeName", row.EmployeeName));
            sb.Append(Td(document, row.Index, "snils", row.Snils, "sout-wp-c"));
            sb.Append(Td(document, row.Index, "shiftHours", row.ShiftHours, "sout-wp-c"));
            sb.Append(Td(document, row.Index, "headcount", row.HeadcountCell, "sout-wp-c"));
            sb.Append(Td(document, row.Index, "equipment", row.Equipment));
            sb.Append(Td(document, row.Index, "materials", row.Materials));
            sb.Append(Td(document, row.Index, "harmfulFactors", row.HarmfulFactors));
            sb.Append(Td(document, row.Index, "extraPay", Dash(row.ExtraPayPercent), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "milk", FirstOr(row.Milk, "Нет"), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "extraLeave", Dash(row.ExtraLeave), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "shortDay", FirstOr(row.ShortDay, "Нет"), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "pension", Dash(row.PensionBenefits), "sout-wp-c"));
            sb.Append(Td(document, row.Index, "medicalExam", Dash(row.MedicalExam)));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendCommission(sb, document, commission);
        sb.Append("""
              <p class="sout-wp-edit-hint ppe-no-print">Правка: клик по ячейке. Затем «Записать».</p>
            </div>
            """);
        return sb.ToString();
    }

    private static void AppendCommission(
        StringBuilder sb,
        SoutPreparationDocument document,
        SoutPreparationCommissionInfo? commission)
    {
        var signDate = Override(document, "signDate", $"«{document.Date:dd}» {RuMonth(document.Date)} {document.Date:yyyy} г.");
        var chairmanProfession = Override(
            document,
            "chairmanProfession",
            string.IsNullOrWhiteSpace(commission?.ChairmanProfession) ? string.Empty : commission!.ChairmanProfession);
        var chairmanName = Override(
            document,
            "chairmanName",
            string.IsNullOrWhiteSpace(commission?.ChairmanName) ? string.Empty : commission!.ChairmanName);

        var members = (commission?.Members ?? [])
            .Where(member => !string.IsNullOrWhiteSpace(member.Name) || !string.IsNullOrWhiteSpace(member.Profession))
            .Take(4)
            .ToList();

        while (members.Count < 2)
        {
            members.Add(new SoutPreparationCommissionMember());
        }

        sb.Append("""
            <div class="sout-eq-sign-section">
              <div class="sout-eq-sign-caption">Председатель комиссии по проведению специальной оценки рабочих мест:</div>
              <div class="sout-eq-sign-row">
            """);
        sb.Append(SignField(document, "chairmanProfession", chairmanProfession, "должность"));
        sb.Append(SignField(document, "chairmanSign", "", "подпись"));
        sb.Append(SignField(document, "chairmanName", chairmanName, "ФИО"));
        sb.Append(SignField(document, "signDate", signDate, "дата"));
        sb.Append("</div>");

        sb.Append("""
              <div class="sout-eq-sign-caption">Члены комиссии по проведению специальной оценки условий труда:</div>
            """);

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var profKey = $"member{i + 1}Profession";
            var nameKey = $"member{i + 1}Name";
            var dateKey = $"member{i + 1}Date";
            var signKey = $"member{i + 1}Sign";
            sb.Append("""<div class="sout-eq-sign-row">""");
            sb.Append(SignField(document, profKey, Override(document, profKey, member.Profession), "должность"));
            sb.Append(SignField(document, signKey, "", "подпись"));
            sb.Append(SignField(document, nameKey, Override(document, nameKey, member.Name), "ФИО"));
            sb.Append(SignField(document, dateKey, Override(document, dateKey, signDate), "дата"));
            sb.Append("</div>");
        }

        sb.Append("</div>");
    }

    private static string SignField(SoutPreparationDocument document, string key, string value, string label) =>
        $"""
        <div class="sout-eq-sign-field">
          <div class="sout-eq-sign-value" contenteditable="true" data-override-key="{E(key)}">{E(Override(document, key, value))}</div>
          <div class="sout-eq-sign-label">{E(label)}</div>
        </div>
        """;

    /// <summary>
    /// Вертикальный заголовок — writing-mode снизу вверх (1в1 с образцом Word).
    /// </summary>
    private static string VerticalTh(string text, bool rowspan = true)
    {
        var rs = rowspan ? " rowspan=\"2\"" : string.Empty;
        var raw = (text ?? string.Empty).Trim();
        var n = raw.Length;
        var fontSize = n > 50
            ? (rowspan ? "4.5pt" : "4pt")
            : n > 32
                ? (rowspan ? "5pt" : "4.5pt")
                : n > 18
                    ? (rowspan ? "5.5pt" : "5pt")
                    : (rowspan ? "6pt" : "5.5pt");
        var vertStyle =
            "display:inline-block;writing-mode:vertical-rl;transform:rotate(180deg);" +
            "white-space:nowrap;line-height:1;margin:0 auto;padding:0;" +
            $"font-size:{fontSize};font-family:Arial,Tahoma,sans-serif;font-weight:400;" +
            "color:#000;background:transparent;";
        return $"""<th{rs} class="sout-info-v"><span class="sout-info-vert" style="{vertStyle}">{E(raw)}</span></th>""";
    }

    private static string Td(
        SoutPreparationDocument document,
        int rowIndex,
        string column,
        string value,
        string? extraClass = null,
        string? extraAttrs = null)
    {
        var key = $"r{rowIndex}:{column}";
        var cls = string.IsNullOrWhiteSpace(extraClass) ? string.Empty : $" class=\"{extraClass}\"";
        var attrs = string.IsNullOrWhiteSpace(extraAttrs) ? string.Empty : " " + extraAttrs.Trim();
        return $"""<td{cls} contenteditable="true" data-override-key="{E(key)}"{attrs}>{E(Override(document, key, value))}</td>""";
    }

    /// <summary>
    /// Пустой / «—» / «___» в PrintOverrides не перекрывает вычисленное значение
    /// (иначе после «Записать» с пустыми ячейками ФИО/СНИЛС навсегда остаются пустыми).
    /// </summary>
    private static string Override(SoutPreparationDocument document, string key, string value)
    {
        if (document.PrintOverrides.TryGetValue(key, out var overridden)
            && !IsBlankPlaceholder(overridden))
        {
            return overridden;
        }

        return value;
    }

    /// <summary>Для шапки: пустой сохранённый override не перекрывает данные организации.</summary>
    private static string MetaValue(SoutPreparationDocument document, string key, string value)
    {
        if (document.PrintOverrides.TryGetValue(key, out var overridden)
            && !IsBlankPlaceholder(overridden))
        {
            return overridden;
        }

        return value;
    }

    private static bool IsBlankPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var stripped = text.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("—", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);
        return stripped.Length == 0;
    }

    private static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string ZeroAsDash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        var trimmed = value.Trim();
        return trimmed is "0" or "-" or "—" ? "—" : trimmed;
    }

    private static string FirstOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string RuMonth(DateTime date) =>
        date.ToString("MMMM", new CultureInfo("ru-RU")).ToLowerInvariant();
}
