using System.Net;
using System.Text;

namespace Zashita.Core.Ppe;

public sealed record PpeAcceptanceLine(
    int Number,
    string TabNumber,
    string EmployeeName,
    string Nomenclature,
    decimal Quantity,
    string Unit,
    decimal Price);

public static class PpeAcceptancePrintBuilder
{
    public static string BuildReturnStatement(
        string organization,
        string warehouse,
        string documentNumber,
        DateTime documentDate,
        IReadOnlyList<PpeAcceptanceLine> lines)
    {
        var sb = new StringBuilder();
        AppendDocHead(sb, "Ведомость сдачи работниками специальной одежды");
        sb.Append($"<div class=\"ppe-doc-meta\"><div class=\"ppe-doc-org\">{E(organization)}</div>");
        sb.Append($"<div class=\"ppe-doc-title\">Ведомость сдачи работниками специальной одежды</div>");
        sb.Append($"<div>№ {E(documentNumber)} от {documentDate:dd.MM.yyyy}</div>");
        sb.Append($"<div>Склад-получатель: {E(warehouse)}</div></div>");

        sb.Append("""
            <table class="ppe-doc-table">
              <thead><tr>
                <th>№ п/п</th><th>Табельный номер сотрудника</th><th>Фамилия, имя, отчество</th>
                <th>Номенклатура</th><th>Количество</th><th>Ед. изм.</th>
                <th>Подпись ответственного при приемке</th><th>Подпись сотрудника при сдаче</th>
              </tr></thead><tbody>
            """);

        foreach (var line in lines)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{line.Number}</td>");
            sb.Append($"<td>{E(line.TabNumber)}</td>");
            sb.Append($"<td>{E(line.EmployeeName)}</td>");
            sb.Append($"<td>{E(line.Nomenclature)}</td>");
            sb.Append($"<td>{(line.Quantity > 0 ? line.Quantity.ToString("0.###") : "")}</td>");
            sb.Append($"<td>{E(line.Unit)}</td>");
            sb.Append("<td></td><td></td>");
            sb.Append("</tr>");
        }

        sb.Append("""
            </tbody></table>
            <div class="ppe-doc-signatures">
              <div>Ответственное лицо: ________________________________</div>
              <div class="ppe-doc-sign-hint">(подпись)</div>
            </div>
            """);

        AppendDocFoot(sb);
        return sb.ToString();
    }

    public static string BuildMaterialReturn(
        string organization,
        string department,
        string warehouse,
        string documentNumber,
        DateTime documentDate,
        IReadOnlyList<PpeAcceptanceLine> lines)
    {
        var sb = new StringBuilder();
        AppendDocHead(sb, "Возврат материалов из эксплуатации");
        sb.Append($"<div class=\"ppe-doc-meta\">");
        sb.Append($"<div class=\"ppe-doc-title\">Возврат материалов из эксплуатации № {E(documentNumber)} от {RuDate(documentDate)}</div>");
        sb.Append($"<div>Организация: {E(organization)}</div>");
        sb.Append($"<div>Подразделение: {E(department)}</div>");
        sb.Append($"<div>Склад: {E(warehouse)}</div></div>");

        sb.Append("""
            <table class="ppe-doc-table">
              <thead><tr>
                <th>№</th><th>Артикул</th><th>Материал</th><th>Сотрудник</th>
                <th>Количество</th><th>Цена</th><th>Сумма</th>
              </tr></thead><tbody>
            """);

        foreach (var line in lines)
        {
            var sum = line.Quantity * line.Price;
            sb.Append("<tr>");
            sb.Append($"<td>{line.Number}</td>");
            sb.Append("<td></td>");
            sb.Append($"<td>{E(line.Nomenclature)}</td>");
            sb.Append($"<td>{E($"{line.TabNumber} {line.EmployeeName}")}</td>");
            sb.Append($"<td>{line.Quantity:0.###} {E(line.Unit)}</td>");
            sb.Append($"<td>{line.Price:N2}</td>");
            sb.Append($"<td>{(sum > 0 ? sum.ToString("N2") : "")}</td>");
            sb.Append("</tr>");
        }

        sb.Append($"""
            </tbody></table>
            <div class="ppe-doc-footer">Всего наименований {lines.Count}</div>
            <div class="ppe-doc-signatures ppe-doc-signatures-row">
              <div>Отпустил ________________________</div>
              <div>Получил ________________________</div>
            </div>
            """);

        AppendDocFoot(sb);
        return sb.ToString();
    }

    public static IReadOnlyList<PpeAcceptanceLine> DemoLines() =>
    [
        new(1, "ОТ-00001322", "Иванова Инна Валерьевна",
            "Брюки [Мужские брюки с пуговицами]", 0, "шт", 2000m)
    ];

    public static string BuildWriteoffMb8(PpeActRecord record, bool autoPrint = true)
    {
        var sb = new StringBuilder();
        AppendDocHead(sb, "МБ-8", autoPrint);
        sb.Append(BuildWriteoffMb8Body(record));
        AppendDocFoot(sb);
        return sb.ToString();
    }

    public static string BuildWriteoffMb8Body(PpeActRecord record)
    {
        var sb = new StringBuilder();
        AppendWriteoffMb8Page(sb, record);
        return sb.ToString();
    }

    public static string BuildInspectionAct(
        PpeActRecord record,
        string title,
        bool singleLine = false,
        bool autoPrint = true)
    {
        var sb = new StringBuilder();
        AppendDocHead(sb, title, autoPrint);
        sb.Append(BuildInspectionActBody(record, title, singleLine));
        AppendDocFoot(sb);
        return sb.ToString();
    }

    public static string BuildInspectionActBody(
        PpeActRecord record,
        string title,
        bool singleLine = false)
    {
        var sb = new StringBuilder();
        if (singleLine)
        {
            var lines = GetActLines(record);
            AppendSingleNomenclatureActPage(sb, record, lines[0]);
        }
        else
        {
            AppendAllNomenclaturesActPage(sb, record);
        }

        return sb.ToString();
    }

    private static List<PpeActLine> GetActLines(PpeActRecord record)
    {
        var lines = record.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToList();
        if (lines.Count == 0)
        {
            lines.Add(new PpeActLine
            {
                Name = "СИЗ не указано",
                Quantity = 1,
                IssueDate = record.Date,
                WearPercent = 0,
                Conclusion = record.SuitabilityDecision
            });
        }

        return lines;
    }

    private static void AppendAllNomenclaturesActPage(StringBuilder sb, PpeActRecord record)
    {
        var lines = GetActLines(record);
        var orderDate = RuDate(record.Date);

        sb.Append("""
            <section class="ppe-act-page">
              <div class="ppe-act-caption">АКТ от
            """);
        sb.Append($" {record.Date:dd.MM.yyyy} ");
        sb.Append("""
              </div>
              <div class="ppe-act-title">осмотра средств индивидуальной защиты</div>
            """);
        sb.Append($"""
            <div class="ppe-act-text">
              Комиссия по определению пригодности средств индивидуальной защиты, бывших в употреблении,
              к дальнейшей эксплуатации, действующая на основании приказа № {E(record.OrderNumber)} от {orderDate},
              в составе:
            </div>
            """);
        AppendCommissionCompositionGrid(sb, record);
        sb.Append("""
            <div class="ppe-act-text">произвела осмотр средств индивидуальной защиты, выданных работнику</div>
            <div class="ppe-act-sign-grid">
              <div class="ppe-act-sign-line">{E(record.Position)}</div>
              <div class="ppe-act-sign-line">{E(record.Employee)}</div>
              <div class="ppe-act-sign-hint">(должность)</div>
              <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
            </div>
            <div class="ppe-act-text">По результатам осмотра выявлено:</div>
            <table class="ppe-doc-table ppe-act-table">
              <thead><tr>
                <th>Наименование СИЗ</th>
                <th>Количество</th>
                <th>Дата выдачи</th>
                <th>Процент износа, %</th>
                <th>Заключение</th>
              </tr></thead><tbody>
            """);
        foreach (var line in lines)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{E(line.Name)}</td>");
            sb.Append($"<td>{line.Quantity:0.###}</td>");
            sb.Append($"<td>{line.IssueDate:dd.MM.yyyy}</td>");
            sb.Append($"<td>{line.WearPercent}</td>");
            sb.Append($"<td>{E(ResolveConclusion(record, line))}</td>");
            sb.Append("</tr>");
        }

        sb.Append($"""
            </tbody></table>
            <div class="ppe-act-signatures-stack">
              <div class="ppe-act-sign-grid ppe-act-sign-grid-bottom">
                <div class="ppe-act-sign-line">{E(record.Position)}</div>
                <div class="ppe-act-sign-line"></div>
                <div class="ppe-act-sign-line">{E(record.Employee)}</div>
                <div class="ppe-act-sign-hint">(должность)</div>
                <div class="ppe-act-sign-hint">(подпись)</div>
                <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
              </div>
            """);
        AppendCommissionSignatureRows(sb, record);
        sb.Append("""
            </div>
            </section>
            """);
    }

    private static void AppendWriteoffMb8Page(StringBuilder sb, PpeActRecord record)
    {
        var lines = GetActLines(record);
        var totalQty = lines.Sum(x => x.Quantity);
        var totalCount = lines.Count;
        var blankRows = Math.Max(3 - lines.Count, 0);

        sb.Append("""
            <section class="ppe-mb8-page">
              <table class="ppe-mb8-header-grid">
                <tr>
                  <td class="ppe-mb8-header-left">
            """);
        sb.Append($"""
                    <div class="ppe-mb8-field"><span>Организация</span><div class="ppe-mb8-underline">{E(record.Organization)}</div></div>
                    <div class="ppe-mb8-field"><span>ИНН</span><div class="ppe-mb8-underline"></div></div>
                    <div class="ppe-mb8-field"><span>структурное подразделение</span><div class="ppe-mb8-underline">{E(record.Department)}</div></div>
                  </td>
                  <td class="ppe-mb8-header-right">
                    <div class="ppe-mb8-approve">
                      <div class="ppe-mb8-approve-title">УТВЕРЖДАЮ</div>
                      <div class="ppe-mb8-approve-line">{E(record.ApproverPosition)}</div>
                      <div class="ppe-mb8-hint">(должность)</div>
                      <div class="ppe-mb8-approve-line ppe-mb8-approve-line-sm">{E(record.ApproverName)}</div>
                      <div class="ppe-mb8-hint">(личная подпись) (расшифровка подписи)</div>
                      <div class="ppe-mb8-approve-date">«&nbsp;&nbsp;&nbsp;&nbsp;» _____________ 20&nbsp;&nbsp;&nbsp;&nbsp; г.</div>
                    </div>
                    <div class="ppe-mb8-form-code">Типовая межотраслевая форма № МБ-8</div>
                    <div class="ppe-mb8-hint">Утверждена постановлением Госкомстата России от 30.10.97 № 71а</div>
                    <table class="ppe-mb8-code-table">
                      <tr>
                        <td class="ppe-mb8-code-label">Форма по ОКУД</td>
                        <td class="ppe-mb8-code-box">0</td><td class="ppe-mb8-code-box">3</td><td class="ppe-mb8-code-box">2</td>
                        <td class="ppe-mb8-code-box">0</td><td class="ppe-mb8-code-box">0</td><td class="ppe-mb8-code-box">0</td><td class="ppe-mb8-code-box">4</td>
                      </tr>
                      <tr>
                        <td class="ppe-mb8-code-label">по ОКПО</td>
                        <td class="ppe-mb8-code-box"></td><td class="ppe-mb8-code-box"></td><td class="ppe-mb8-code-box"></td>
                        <td class="ppe-mb8-code-box"></td><td class="ppe-mb8-code-box"></td><td class="ppe-mb8-code-box"></td><td class="ppe-mb8-code-box"></td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            """);
        sb.Append($"""
              <table class="ppe-mb8-meta">
                <tr>
                  <td class="ppe-mb8-meta-label">Дата составления</td>
                  <td class="ppe-mb8-meta-value">{record.Date:dd.MM.yyyy}</td>
                  <td class="ppe-mb8-meta-label">Вид операции</td>
                  <td class="ppe-mb8-meta-value ppe-mb8-meta-code">списание</td>
                  <td class="ppe-mb8-meta-label">код</td>
                  <td class="ppe-mb8-meta-value ppe-mb8-meta-code">09</td>
                </tr>
                <tr>
                  <td class="ppe-mb8-meta-label">Структурное подразделение</td>
                  <td class="ppe-mb8-meta-value" colspan="5">{E(record.Department)}</td>
                </tr>
                <tr>
                  <td class="ppe-mb8-meta-label">Материально ответственное лицо</td>
                  <td class="ppe-mb8-meta-value" colspan="5">{E($"{record.Employee}, {record.Position}")}</td>
                </tr>
                <tr>
                  <td class="ppe-mb8-meta-label">Основание для составления акта</td>
                  <td class="ppe-mb8-meta-value" colspan="5">{E($"Приказ № {record.OrderNumber}")}</td>
                </tr>
              </table>
              <div class="ppe-mb8-act-title">АКТ № {E(record.Number)}</div>
              <div class="ppe-mb8-act-subtitle">на списание малоценных и быстроизнашивающихся предметов</div>
              <div class="ppe-mb8-act-date">от «{record.Date:dd}» {MonthName(record.Date.Month)} {record.Date:yyyy} г.</div>
              <table class="ppe-mb8-items">
                <thead>
                  <tr class="ppe-mb8-colnums">
                    <th>1</th><th>2</th><th>3</th><th>4</th><th>5</th><th>6</th><th>7</th><th>8</th><th>9</th><th>10</th><th>11</th><th>12</th><th>13</th><th>14</th>
                  </tr>
                  <tr>
                    <th rowspan="2">Наименование предмета</th>
                    <th rowspan="2">Номенклатурный номер</th>
                    <th rowspan="2">Инвентарный номер</th>
                    <th colspan="2">Единица измерения</th>
                    <th rowspan="2">Количество</th>
                    <th rowspan="2">Дата поступления</th>
                    <th rowspan="2">Цена, руб. коп.</th>
                    <th rowspan="2">Сумма, руб. коп.</th>
                    <th rowspan="2">Сумма начисленной амортизации, руб. коп.</th>
                    <th rowspan="2">Срок службы, мес.</th>
                    <th colspan="2">Причина списания</th>
                    <th rowspan="2">Паспорт (сертификат) №</th>
                  </tr>
                  <tr>
                    <th>код</th>
                    <th>наименование</th>
                    <th>наименование</th>
                    <th>код</th>
                  </tr>
                </thead>
                <tbody>
            """);
        foreach (var line in lines)
        {
            AppendMb8ItemRow(sb, record, line);
        }

        for (var i = 0; i < blankRows; i++)
        {
            sb.Append("<tr><td>&nbsp;</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>");
        }

        sb.Append($"""
                </tbody>
                <tfoot>
                  <tr>
                    <td colspan="5" class="ppe-mb8-total-label">Итого</td>
                    <td>{totalQty:0.###}</td>
                    <td></td><td></td><td></td><td></td><td></td><td colspan="2"></td><td></td>
                  </tr>
                </tfoot>
              </table>
              <div class="ppe-mb8-summary">Общее количество предметов: <strong>{totalCount}</strong> ({QuantityInWords(totalCount)})</div>
              <div class="ppe-mb8-summary">Номера и даты актов на выбытие (утилизации, ликвидации) предметов ________________________________________________</div>
              <table class="ppe-mb8-scrap-table">
                <thead>
                  <tr>
                    <th colspan="8">Утиль (лом)</th>
                  </tr>
                  <tr>
                    <th rowspan="2">наименование</th>
                    <th rowspan="2">номенклатурный номер</th>
                    <th rowspan="2">инвентарный номер</th>
                    <th colspan="2">единица измерения</th>
                    <th rowspan="2">количество</th>
                    <th rowspan="2">сумма, руб. коп.</th>
                    <th rowspan="2">примечание</th>
                  </tr>
                  <tr>
                    <th>код</th>
                    <th>наименование</th>
                  </tr>
                </thead>
                <tbody>
                  <tr><td>&nbsp;</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>
                  <tr><td>&nbsp;</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>
                </tbody>
              </table>
              <table class="ppe-mb8-sign-table">
                <thead>
                  <tr>
                    <th></th>
                    <th>должность</th>
                    <th>подпись</th>
                    <th>расшифровка подписи</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td class="ppe-mb8-sign-role">Материально ответственное лицо</td>
                    <td>{E(record.Position)}</td>
                    <td></td>
                    <td>{E(record.Employee)}</td>
                  </tr>
            """);
        AppendMb8CommissionSignRows(sb, record);
        sb.Append("""
                </tbody>
              </table>
            </section>
            """);
    }

    private static void AppendMb8CommissionSignRows(StringBuilder sb, PpeActRecord record)
    {
        sb.Append($"""
                  <tr>
                    <td class="ppe-mb8-sign-role">Председатель комиссии</td>
                    <td>{E(record.CommissionHeadPosition)}</td>
                    <td></td>
                    <td>{E(record.CommissionHead)}</td>
                  </tr>
            """);

        var members = GetCommissionMembers(record);
        const int memberRows = 4;
        for (var i = 0; i < memberRows; i++)
        {
            var member = i < members.Count ? members[i] : null;
            var role = i == 0 ? "Члены комиссии" : string.Empty;
            sb.Append("<tr>");
            sb.Append($"<td class=\"ppe-mb8-sign-role\">{E(role)}</td>");
            sb.Append($"<td>{E(member?.Position)}</td>");
            sb.Append("<td></td>");
            sb.Append($"<td>{E(member?.Name)}</td>");
            sb.Append("</tr>");
        }
    }

    private static void AppendCommissionCompositionGrid(StringBuilder sb, PpeActRecord record)
    {
        var members = GetCommissionMembers(record);
        var member1 = members.Count > 0 ? members[0] : null;
        var member2 = members.Count > 1 ? members[1] : null;

        sb.Append($"""
            <div class="ppe-act-sign-grid">
              <div class="ppe-act-sign-line">{E(record.CommissionHeadPosition)}</div>
              <div class="ppe-act-sign-line">{E(record.CommissionHead)}</div>
              <div class="ppe-act-sign-line">{E(member1?.Position)}</div>
              <div class="ppe-act-sign-line">{E(member1?.Name)}</div>
              <div class="ppe-act-sign-hint">(должность)</div>
              <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
              <div class="ppe-act-sign-hint">(должность)</div>
              <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
            </div>
            """);

        if (member2 is not null || members.Count > 2)
        {
            var member3 = members.Count > 2 ? members[2] : null;
            sb.Append($"""
                <div class="ppe-act-sign-grid">
                  <div class="ppe-act-sign-line">{E(member2?.Position)}</div>
                  <div class="ppe-act-sign-line">{E(member2?.Name)}</div>
                  <div class="ppe-act-sign-line">{E(member3?.Position)}</div>
                  <div class="ppe-act-sign-line">{E(member3?.Name)}</div>
                  <div class="ppe-act-sign-hint">(должность)</div>
                  <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
                  <div class="ppe-act-sign-hint">(должность)</div>
                  <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
                </div>
                """);
        }
    }

    private static void AppendCommissionSignatureRows(StringBuilder sb, PpeActRecord record)
    {
        sb.Append($"""
              <div class="ppe-act-sign-grid ppe-act-sign-grid-bottom">
                <div class="ppe-act-sign-line">{E(record.CommissionHeadPosition)}</div>
                <div class="ppe-act-sign-line"></div>
                <div class="ppe-act-sign-line">{E(record.CommissionHead)}</div>
                <div class="ppe-act-sign-hint">(должность)</div>
                <div class="ppe-act-sign-hint">(подпись)</div>
                <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
              </div>
            """);

        foreach (var member in GetCommissionMembers(record))
        {
            sb.Append($"""
              <div class="ppe-act-sign-grid ppe-act-sign-grid-bottom">
                <div class="ppe-act-sign-line">{E(member.Position)}</div>
                <div class="ppe-act-sign-line"></div>
                <div class="ppe-act-sign-line">{E(member.Name)}</div>
                <div class="ppe-act-sign-hint">(должность)</div>
                <div class="ppe-act-sign-hint">(подпись)</div>
                <div class="ppe-act-sign-hint">(Ф.И.О.)</div>
              </div>
            """);
        }
    }

    private static IReadOnlyList<PpeActCommissionMember> GetCommissionMembers(PpeActRecord record) =>
        record.CommissionMembers
            .Where(x => !string.IsNullOrWhiteSpace(x.Position) || !string.IsNullOrWhiteSpace(x.Name))
            .ToList();

    private static void AppendMb8ItemRow(StringBuilder sb, PpeActRecord record, PpeActLine line)
    {
        var unit = ResolveMeasureUnit(line);
        var reason = ResolveConclusion(record, line);
        var reasonCode = ResolveReasonCode(reason);
        var serviceLife = ResolveServiceLifeMonths(line);

        sb.Append("<tr>");
        sb.Append($"<td>{E(line.Name)}</td>");
        sb.Append("<td></td><td></td>");
        sb.Append($"<td>{unit.Code}</td>");
        sb.Append($"<td>{unit.Name}</td>");
        sb.Append($"<td>{line.Quantity:0.###}</td>");
        sb.Append($"<td>{line.IssueDate:dd.MM.yyyy}</td>");
        sb.Append("<td></td><td></td><td></td>");
        sb.Append($"<td>{serviceLife}</td>");
        sb.Append($"<td>{E(reason)}</td>");
        sb.Append($"<td>{reasonCode}</td>");
        sb.Append("<td></td>");
        sb.Append("</tr>");
    }

    private static (string Code, string Name) ResolveMeasureUnit(PpeActLine line)
    {
        var name = line.Name.ToLowerInvariant();
        if (name.Contains("обув", StringComparison.Ordinal) ||
            name.Contains("сапог", StringComparison.Ordinal) ||
            name.Contains("ботин", StringComparison.Ordinal) ||
            name.Contains("валенк", StringComparison.Ordinal))
        {
            return ("715", "пар");
        }

        if (name.Contains("перчат", StringComparison.Ordinal))
        {
            return ("715", "пар");
        }

        return ("796", "шт");
    }

    private static string ResolveReasonCode(string conclusion)
    {
        if (conclusion.Contains("утил", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Contains("не пригод", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Contains("ликвид", StringComparison.OrdinalIgnoreCase))
        {
            return "05";
        }

        if (conclusion.Contains("износ", StringComparison.OrdinalIgnoreCase))
        {
            return "04";
        }

        return "09";
    }

    private static int ResolveServiceLifeMonths(PpeActLine line) =>
        line.WearPercent >= 100 ? 0 : Math.Max(12 - line.WearPercent / 10, 0);

    private static string QuantityInWords(int count) => count switch
    {
        1 => "одно наименование",
        2 => "два наименования",
        3 => "три наименования",
        4 => "четыре наименования",
        5 => "пять наименований",
        _ => $"{count} наименований"
    };

    private static void AppendSingleNomenclatureActPage(StringBuilder sb, PpeActRecord record, PpeActLine line)
    {
        var conclusion = ResolveConclusion(record, line);
        var qtyLabel = line.Quantity == 1
            ? "1 шт"
            : $"{line.Quantity:0.###} шт";
        var nameWithQty = $"{line.Name} ({qtyLabel})";

        sb.Append("""
            <section class="ppe-act-page ppe-act-single">
              <div class="ppe-act-single-title">
                АКТ осмотра специальной одежды, специальной обуви и других средств индивидуальной защиты
              </div>
            """);
        sb.Append($"<div class=\"ppe-act-single-date\">«{record.Date:dd}» {MonthName(record.Date.Month)} {record.Date:yyyy} г.</div>");
        sb.Append("""
              <div class="ppe-act-text">Составлен комиссией в составе:</div>
              <div class="ppe-act-single-row">
                <span>Председатель комиссии</span>
                <span class="ppe-act-single-line">{head}</span>
              </div>
            """.Replace("{head}", E($"{record.CommissionHeadPosition} {record.CommissionHead}".Trim())));
        var membersText = string.Join("; ", GetCommissionMembers(record)
            .Select(x => $"{x.Position} {x.Name}".Trim())
            .Where(x => x.Length > 0));
        sb.Append("""
              <div class="ppe-act-single-row">
                <span>Члены комиссии</span>
                <span class="ppe-act-single-line">{commission}</span>
              </div>
            """.Replace("{commission}", E(membersText)));
        sb.Append($"""
              <ol class="ppe-act-single-list">
                <li><span class="ppe-act-single-label">Ф.И.О., профессия работника, сдавшего СИЗ</span><span class="ppe-act-single-value">{E($"{record.Employee}, {record.Position}")}</span></li>
                <li><span class="ppe-act-single-label">Наименование СИЗ</span><span class="ppe-act-single-value">{E(nameWithQty)}</span></li>
                <li><span class="ppe-act-single-label">Дата выдачи СИЗ</span><span class="ppe-act-single-value">{line.IssueDate:dd.MM.yyyy}</span></li>
                <li><span class="ppe-act-single-label">Срок эксплуатации</span><span class="ppe-act-single-value">{line.IssueDate:dd.MM.yyyy}</span></li>
                <li><span class="ppe-act-single-label">Визуальным осмотром установлено</span><span class="ppe-act-single-value ppe-act-single-blank"></span></li>
                <li><span class="ppe-act-single-label">Износ (%)</span><span class="ppe-act-single-value">{line.WearPercent}</span></li>
                <li><span class="ppe-act-single-label">Заключение комиссии</span><span class="ppe-act-single-value">{E(conclusion)}</span></li>
                <li><span class="ppe-act-single-label">Новый срок эксплуатации СИЗ</span><span class="ppe-act-single-value">{record.Date:dd.MM.yyyy}</span></li>
              </ol>
              <div class="ppe-act-single-signatures">
                <div class="ppe-act-single-sign-row">
                  <span>Сотрудник</span>
                  <span class="ppe-act-single-line"></span>
                  <span>{E(record.Employee)}</span>
                </div>
                <div class="ppe-act-single-sign-row">
                  <span>Председатель комиссии</span>
                  <span class="ppe-act-single-line"></span>
                  <span>{E($"{record.CommissionHeadPosition} {record.CommissionHead}".Trim())}</span>
                </div>
              </div>
            </section>
            """);
    }

    private static string ResolveConclusion(PpeActRecord record, PpeActLine line) =>
        string.IsNullOrWhiteSpace(line.Conclusion) ? record.SuitabilityDecision : line.Conclusion;

    private static void AppendDocHead(StringBuilder sb, string _, bool autoPrint = true)
    {
        sb.Append("""
            <!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"/>
            <title> </title>
            """);
        sb.Append(Zashita.Core.Brand.SiteIconHtml.HeadLinks);
        sb.Append("""
            <link rel="stylesheet" href="/css/ppe-doc-print.css" />
            """);

        if (!autoPrint)
        {
            sb.Append("""
              <style>
                .ppe-preview-print-btn {
                  position: fixed;
                  top: 14px;
                  right: 14px;
                  z-index: 9999;
                  background: #0d6efd;
                  color: #fff;
                  border: none;
                  border-radius: 8px;
                  padding: 10px 14px;
                  font-weight: 700;
                  cursor: pointer;
                }
                @media print {
                  .ppe-preview-print-btn { display: none !important; }
                }
              </style>
              """);
        }

        if (autoPrint)
        {
            sb.Append("""
              <script>
              window.addEventListener('load', function () {
                var prev = document.title;
                document.title = '\u00a0';
                window.print();
                window.addEventListener('afterprint', function () { document.title = prev; }, { once: true });
              });
              </script>
              """);
        }

        sb.Append("""
            </head><body class="ppe-doc-body">
            """);

        if (!autoPrint)
        {
            sb.Append("""
              <button class="ppe-preview-print-btn" type="button" onclick="window.print()">Печать</button>
              """);
        }
    }

    private static void AppendDocFoot(StringBuilder sb) => sb.Append("</body></html>");

    private static string RuDate(DateTime date) =>
        $"{date:dd} {MonthName(date.Month)} {date:yyyy} г.";

    private static string MonthName(int month) => month switch
    {
        1 => "января", 2 => "февраля", 3 => "марта", 4 => "апреля",
        5 => "мая", 6 => "июня", 7 => "июля", 8 => "августа",
        9 => "сентября", 10 => "октября", 11 => "ноября", 12 => "декабря",
        _ => ""
    };

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
