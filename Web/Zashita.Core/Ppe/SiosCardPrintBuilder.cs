using System.Net;
using System.Text;
using Zashita.Core.Brand;

namespace Zashita.Core.Ppe;

/// <summary>HTML-печать личной карточки СИЗ (лицевая + оборотная), по образцу десктопной SiosCardPreviewForm.</summary>
public static class SiosCardPrintBuilder
{
    private const int MinTableRows = 1;

    public static string BuildHtmlDocument(SiosCardPrintContext context, bool autoPrint = true)
    {
        var employees = context.Employees.Count > 0 ? context.Employees : [""];
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8" />
              <title>
            """);
        sb.Append(E(context.DocumentName));
        sb.Append("</title>");
        sb.Append(SiteIconHtml.HeadLinks);
        sb.Append("""
              <link rel="stylesheet" href="/css/sios-card-print.css?v=datefit4" />
              <style>
                @page { size: A4 landscape; margin: 8mm; }
                .sios-page { width: 297mm; max-width: 297mm; min-height: 210mm; box-sizing: border-box; }
                .sios-back-table { table-layout: fixed; width: 100%; }
                .sios-back-table col.sios-col-name { width: 16%; }
                .sios-back-table col.sios-col-model { width: 12%; }
                .sios-back-table col.sios-col-date { width: 26mm; }
                .sios-back-table col.sios-col-qty { width: 14mm; }
                .sios-back-table col.sios-col-method { width: 16mm; }
                .sios-back-table col.sios-col-sign { width: 18mm; }
                .sios-back-table col.sios-col-act { width: 20mm; }
                .sios-back-table td.sios-date-cell,
                .sios-back-table th.sios-date-cell {
                  white-space: nowrap !important;
                  word-break: keep-all !important;
                  overflow-wrap: normal !important;
                  font-size: 9pt !important;
                  padding: 2px 3px !important;
                  letter-spacing: 0 !important;
                  text-align: center;
                }
                @media print {
                  @page { size: A4 landscape; margin: 8mm; }
                  .sios-page { width: auto; max-width: none; min-height: auto; margin: 0; padding: 0; }
                  .sios-back-table col.sios-col-date { width: 26mm !important; }
                  [contenteditable="true"] { outline: none !important; background: transparent !important; }
                }
                body.sios-print-preview [contenteditable="true"] {
                  outline: 1px dashed #93c5fd;
                  background: #f8fbff;
                  cursor: text;
                  min-height: 1.1em;
                }
                body.sios-print-preview [contenteditable="true"]:focus {
                  outline: 2px solid #2563eb;
                  background: #eff6ff;
                }
              </style>
            </head>
            """);
        sb.Append(autoPrint
            ? "<body class=\"sios-print-body\" onload=\"window.print()\">"
            : "<body class=\"sios-print-body sios-print-preview\">");

        for (var i = 0; i < employees.Count; i++)
        {
            var employee = employees[i];
            if (i > 0)
            {
                sb.Append("<div class=\"sios-page-break\"></div>");
            }

            AppendEmployeePages(sb, context, employee);
        }

        if (context.EditablePreview)
        {
            sb.Append("""
                <script>
                (function () {
                  function notify(el) {
                    if (!el || !window.parent || window.parent === window) return;
                    var field = el.getAttribute('data-sios-field');
                    var index = el.getAttribute('data-sios-index');
                    if (!field || index === null) return;
                    window.parent.postMessage({
                      type: 'sios-preview-edit',
                      field: field,
                      index: Number(index),
                      value: (el.innerText || '').replace(/\u00a0/g, ' ').trim()
                    }, '*');
                  }
                  document.addEventListener('focusout', function (e) {
                    var el = e.target && e.target.closest ? e.target.closest('[data-sios-field]') : null;
                    notify(el);
                  });
                  document.addEventListener('keydown', function (e) {
                    if (e.key !== 'Enter' || !e.target || !e.target.closest) return;
                    var el = e.target.closest('[data-sios-field]');
                    if (!el) return;
                    e.preventDefault();
                    el.blur();
                  });
                })();
                </script>
                """);
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendEmployeePages(StringBuilder sb, SiosCardPrintContext ctx, string employee)
    {
        var isDsiz = ctx.UseDsizSinglePageLayout ||
                     (ctx.Rows.Count > 0 && ctx.Rows.All(r =>
                         string.Equals(r.ShelfLife, "1 год", StringComparison.OrdinalIgnoreCase)));
        var isDuty = !isDsiz && PpeCardTypes.IsDutyCard(ctx.DocumentName);

        if (isDsiz)
        {
            AppendDsizFrontPage(sb, ctx, employee);
            AppendBackPage(sb, ctx, employee, pageBreak: true);
            return;
        }

        if (isDuty)
        {
            AppendDutyFrontPage(sb, ctx, employee);
            AppendDutyBackPage(sb, ctx, pageBreak: true);
            return;
        }

        AppendFrontPage(sb, ctx, employee);
        AppendBackPage(sb, ctx, employee, pageBreak: true);
    }

    /// <summary>Лицевая сторона по бланку «Карточка учета выдачи дежурных СИЗ.docx».</summary>
    private static void AppendDutyFrontPage(StringBuilder sb, SiosCardPrintContext ctx, string employee)
    {
        var title = string.IsNullOrWhiteSpace(ctx.DocumentName) ? PpeCardTypes.DutySiz : ctx.DocumentName.Trim();
        var workplace = string.IsNullOrWhiteSpace(ctx.WorkplaceId) ? ctx.TabNumber : ctx.WorkplaceId;
        var dept = string.IsNullOrWhiteSpace(ctx.Department) ? "" : ctx.Department.Trim();
        var order = string.IsNullOrWhiteSpace(ctx.NormsOrderText) ? "" : ctx.NormsOrderText.Trim();
        var itemRows = ctx.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var tableRows = Math.Max(MinTableRows, itemRows.Count);

        sb.Append("<section class=\"sios-page sios-page-portrait sios-duty\">");
        sb.Append($"<div class=\"sios-doc-title\">{E(title)}</div>");
        sb.Append("<div class=\"sios-face-caption\">Лицевая сторона личной карточки</div>");
        sb.Append($"<div class=\"sios-card-no\">КАРТОЧКА № {E(ctx.CardNumber)}</div>");
        sb.Append("<div class=\"sios-subtitle\">учета выдачи дежурных СИЗ</div>");

        sb.Append("<div class=\"sios-duty-profile\">");
        sb.Append("<div class=\"sios-duty-label\">Идентификатор рабочего места, за которым закреплены дежурные СИЗ:</div>");
        sb.Append($"<div class=\"sios-duty-value\">{E(workplace)}</div>");
        sb.Append("<div class=\"sios-duty-line\">Структурное подразделение <span class=\"sios-duty-fill\">");
        sb.Append(E(dept));
        sb.Append("</span></div>");
        sb.Append("<div class=\"sios-duty-line\">Фамилия, имя, отчество (при наличии) ответственного <span class=\"sios-duty-fill\">");
        sb.Append(E(employee));
        sb.Append("</span></div>");
        sb.Append("<div class=\"sios-duty-line\">Профессия (должность) ответственного <span class=\"sios-duty-fill\">");
        sb.Append(E(ctx.Profession));
        sb.Append("</span></div>");
        sb.Append("<div class=\"sios-duty-label\">Предусмотрена приказом (номер и дата приказа об утверждении Норм) выдача:</div>");
        if (!string.IsNullOrWhiteSpace(order))
        {
            sb.Append($"<div class=\"sios-duty-value\">{E(order)}</div>");
        }

        sb.Append("</div>");

        sb.Append("<table class=\"sios-table sios-front-table sios-duty-front-table\"><thead><tr>");
        sb.Append("<th>Наименование СИЗ</th>");
        sb.Append("<th>Пункт<br/>Норм</th>");
        sb.Append("<th>Единица измерения,<br/>периодичность выдачи</th>");
        sb.Append("<th>Количество<br/>на период</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < tableRows; i++)
        {
            sb.Append("<tr>");
            if (i < itemRows.Count)
            {
                var row = itemRows[i];
                sb.Append($"<td>{E(SimplifyPpeName(row.Name))}</td>");
                AppendCell(sb, DutyNormPoint(row), editable: ctx.EditablePreview, field: "NormPoint", index: i, cssClass: "sios-center");
                sb.Append($"<td>{E(DutyUnitAndPeriod(row))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(DutyQuantity(row))}</td>");
            }
            else
            {
                for (var c = 0; c < 4; c++) sb.Append("<td>&nbsp;</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("<div class=\"sios-footer sios-duty-footer\">");
        sb.Append("<div class=\"sios-footer-text\">Ответственное лицо</div>");
        AppendResponsibleSignBlock(sb, ctx.ResponsiblePerson);
        sb.Append("</div></section>");
    }

    /// <summary>Оборотная сторона по бланку дежурных СИЗ (9 колонок, без «лично/дозатор»).</summary>
    private static void AppendDutyBackPage(StringBuilder sb, SiosCardPrintContext ctx, bool pageBreak = false)
    {
        var itemRows = ctx.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var tableRows = Math.Max(MinTableRows, itemRows.Count);

        sb.Append($"<section class=\"sios-page sios-page-portrait sios-duty{(pageBreak ? " sios-page-break" : "")}\">");
        sb.Append("<div class=\"sios-back-title\">Оборотная сторона личной карточки</div>");

        sb.Append("<table class=\"sios-table sios-back-table sios-duty-back-table\"><colgroup>");
        sb.Append("<col class=\"sios-col-name\" /><col class=\"sios-col-model\" />");
        sb.Append("<col class=\"sios-col-date\" style=\"width:26mm\" /><col class=\"sios-col-qty\" /><col class=\"sios-col-sign\" />");
        sb.Append("<col class=\"sios-col-date\" style=\"width:26mm\" /><col class=\"sios-col-qty\" /><col class=\"sios-col-sign\" /><col class=\"sios-col-act\" />");
        sb.Append("</colgroup><thead>");
        sb.Append("<tr class=\"sios-back-group\">");
        sb.Append("<th rowspan=\"2\">Наименование СИЗ</th>");
        sb.Append("<th rowspan=\"2\">Модель, марка, артикул,<br/>класс защиты СИЗ</th>");
        sb.Append("<th colspan=\"3\">Выдано</th>");
        sb.Append("<th colspan=\"4\">Возвращено</th>");
        sb.Append("</tr><tr class=\"sios-back-sub\">");
        sb.Append("<th class=\"sios-date-cell\">дата</th><th>количество</th><th>подпись получившего СИЗ</th>");
        sb.Append("<th class=\"sios-date-cell\">дата</th><th>количество</th><th>подпись сдавшего СИЗ</th>");
        sb.Append("<th>Акт списания<br/>(дата, номер)</th>");
        sb.Append("</tr><tr class=\"sios-back-nums\">");
        for (var n = 1; n <= 9; n++) sb.Append($"<th>{n}</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < tableRows; i++)
        {
            sb.Append("<tr>");
            if (i < itemRows.Count)
            {
                var row = itemRows[i];
                sb.Append($"<td>{E(SimplifyPpeName(row.Name))}</td>");
                AppendCell(sb, GetModelDescription(row), editable: ctx.EditablePreview, field: "ModelDescription", index: i);
                sb.Append($"<td class=\"sios-center sios-date-cell\">{E(ResolveBackIssueDate(row, ctx))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(row.BackIssueQty)}</td>");
                sb.Append($"<td>{E(row.BackIssueSign)}</td>");
                sb.Append($"<td class=\"sios-center sios-date-cell\">{E(row.BackReturnDate)}</td>");
                sb.Append($"<td class=\"sios-center\">{E(row.BackReturnQty)}</td>");
                sb.Append($"<td>{E(row.BackReturnSign)}</td>");
                sb.Append($"<td>{E(row.BackAct)}</td>");
            }
            else
            {
                for (var c = 0; c < 9; c++) sb.Append("<td>&nbsp;</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static void AppendFrontPage(StringBuilder sb, SiosCardPrintContext ctx, string employee)
    {
        var sizes = ResolveSizes(ctx, employee);
        var sex = ResolveSex(ctx, employee);
        var hire = FormatDate(ctx.HireDate);
        var profChange = FormatDate(ctx.ProfessionChangeDate);
        var tab = string.IsNullOrWhiteSpace(ctx.TabNumber) ? "" : ctx.TabNumber.Trim();
        var dept = string.IsNullOrWhiteSpace(ctx.Department) ? "" : ctx.Department.Trim();
        var title = string.IsNullOrWhiteSpace(ctx.DocumentName) ? PpeCardTypes.PersonalSiz : ctx.DocumentName.Trim();
        var itemRows = ctx.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var tableRows = Math.Max(MinTableRows, itemRows.Count);

        sb.Append("<section class=\"sios-page sios-page-portrait\">");
        sb.Append($"<div class=\"sios-doc-title\">{E(title)}</div>");
        sb.Append("<div class=\"sios-face-caption\">Лицевая сторона личной карточки</div>");
        sb.Append($"<div class=\"sios-card-no\">ЛИЧНАЯ КАРТОЧКА № {E(ctx.CardNumber)}</div>");
        sb.Append("<div class=\"sios-subtitle\">учета выдачи СИЗ</div>");

        sb.Append("<div class=\"sios-profile\">");
        sb.Append("<div class=\"sios-profile-left\">");
        AppendField(sb, "ФИО", employee);
        AppendField(sb, "Табельный номер", tab);
        AppendField(sb, "Структурное подразделение", dept);
        AppendField(sb, "Профессия (должность)", ctx.Profession);
        AppendField(sb, "Дата поступления на работу", hire);
        AppendField(sb, "Дата изменения профессии (должности) или", profChange);
        sb.Append("<div class=\"sios-field sios-field-multiline\">перевода в другое структурное<br/>подразделение</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"sios-profile-right\">");
        AppendPersonalProfileRight(sb, sex, sizes);
        sb.Append("</div></div>");

        sb.Append("<table class=\"sios-table sios-front-table\"><thead><tr>");
        sb.Append("<th>Наименование СИЗ</th>");
        sb.Append("<th>Пункт<br/>Норм</th>");
        sb.Append("<th>Единица измерения,<br/>периодичность выдачи</th>");
        sb.Append("<th>Количество<br/>на период</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < tableRows; i++)
        {
            sb.Append("<tr>");
            if (i < itemRows.Count)
            {
                var row = itemRows[i];
                sb.Append($"<td>{E(SimplifyPpeName(row.Name))}</td>");
                AppendCell(sb, row.NormPoint, editable: ctx.EditablePreview, field: "NormPoint", index: i, cssClass: "sios-center");
                sb.Append($"<td>{E(PersonalUnitAndPeriod(row))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(FrontQuantityFromStandard(row))}</td>");
            }
            else
            {
                for (var c = 0; c < 4; c++) sb.Append("<td>&nbsp;</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("<div class=\"sios-footer\">");
        sb.Append("<div class=\"sios-footer-text\">Ответственное лицо за ведение карточек<br/>учета выдачи СИЗ</div>");
        AppendResponsibleSignBlock(sb, ctx.ResponsiblePerson);
        sb.Append("</div></section>");
    }

    private static void AppendBackPage(StringBuilder sb, SiosCardPrintContext ctx, string employee, bool pageBreak = false)
    {
        var itemRows = ctx.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var tableRows = Math.Max(MinTableRows, itemRows.Count);

        sb.Append($"<section class=\"sios-page sios-page-portrait{(pageBreak ? " sios-page-break" : "")}\">");
        sb.Append($"<div class=\"sios-back-title\">Оборотная сторона личной карточки (№ {E(ctx.CardNumber)})</div>");

        sb.Append("<table class=\"sios-table sios-back-table\"><colgroup>");
        sb.Append("<col class=\"sios-col-name\" /><col class=\"sios-col-model\" />");
        sb.Append("<col class=\"sios-col-date\" style=\"width:26mm\" /><col class=\"sios-col-qty\" /><col class=\"sios-col-method\" /><col class=\"sios-col-sign\" />");
        sb.Append("<col class=\"sios-col-date\" style=\"width:26mm\" /><col class=\"sios-col-qty\" /><col class=\"sios-col-sign\" /><col class=\"sios-col-act\" />");
        sb.Append("</colgroup><thead>");
        sb.Append("<tr class=\"sios-back-group\">");
        sb.Append("<th rowspan=\"2\">Наименование<br/>СИЗ</th>");
        sb.Append("<th rowspan=\"2\">Модель, марка,<br/>артикул, класс<br/>защиты СИЗ,<br/>дерматологических СИЗ</th>");
        sb.Append("<th colspan=\"4\">Выдано</th>");
        sb.Append("<th colspan=\"4\">Возвращено <sup>**</sup></th>");
        sb.Append("</tr><tr class=\"sios-back-sub\">");
        sb.Append("<th class=\"sios-date-cell\">дата</th><th>коли-<br/>чество</th><th>Лично/дозатор <sup>*</sup></th><th>подпись<br/>получившего СИЗ</th>");
        sb.Append("<th class=\"sios-date-cell\">дата</th><th>коли-<br/>чество</th><th>Подпись<br/>сдавшего СИЗ</th>");
        sb.Append("<th>Акт списания<br/>(дата, номер)</th>");
        sb.Append("</tr><tr class=\"sios-back-nums\">");
        for (var n = 1; n <= 10; n++) sb.Append($"<th>{n}</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < tableRows; i++)
        {
            sb.Append("<tr>");
            if (i < itemRows.Count)
            {
                var row = itemRows[i];
                sb.Append($"<td>{E(SimplifyPpeName(row.Name))}</td>");
                AppendCell(sb, GetModelDescription(row), editable: ctx.EditablePreview, field: "ModelDescription", index: i);
                sb.Append($"<td class=\"sios-center sios-date-cell\">{E(ResolveBackIssueDate(row, ctx))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(IssuedQuantity(row, isDuty: false))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(ResolveBackIssueMethod(row))}</td>");
                sb.Append($"<td class=\"sios-sign-cell\">{E(ResolveBackIssueSign(row))}</td>");
                sb.Append($"<td class=\"sios-center sios-date-cell\">{E(row.BackReturnDate)}</td>");
                sb.Append($"<td class=\"sios-center\">{E(row.BackReturnQty)}</td>");
                sb.Append($"<td>{E(row.BackReturnSign)}</td>");
                sb.Append($"<td>{E(row.BackAct)}</td>");
            }
            else
            {
                for (var c = 0; c < 10; c++) sb.Append("<td>&nbsp;</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<div class=\"sios-back-notes\">");
        sb.Append("<div><sup>*</sup> — сведения указываются только для дерматологических СИЗ.</div>");
        sb.Append("<div><sup>**</sup> — сведения указываются для всех СИЗ, кроме дерматологических СИЗ и СИЗ однократного применения.</div>");
        sb.Append("</div></section>");
    }

    private static void AppendCell(
        StringBuilder sb,
        string? value,
        bool editable,
        string field,
        int index,
        string? cssClass = null)
    {
        var cls = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        if (!editable)
        {
            sb.Append($"<td{cls}>{E(value)}</td>");
            return;
        }

        var extraClass = string.IsNullOrWhiteSpace(cssClass) ? "sios-edit-cell" : $"{cssClass} sios-edit-cell";
        sb.Append(
            $"<td class=\"{extraClass}\" contenteditable=\"true\" data-sios-field=\"{E(field)}\" data-sios-index=\"{index}\">{E(value)}</td>");
    }

    /// <summary>Лицевая сторона ДСИЗ — по образцу Excel (как обычная карточка СИЗ, с полями ФИО и таблицей норм).</summary>
    private static void AppendDsizFrontPage(StringBuilder sb, SiosCardPrintContext ctx, string employee)
    {
        var sizes = ResolveSizes(ctx, employee);
        var sex = ResolveSex(ctx, employee);
        var hire = FormatDate(ctx.HireDate);
        var profChange = FormatDate(ctx.ProfessionChangeDate);
        var (last, first, middle) = SplitName(employee);
        var tab = string.IsNullOrWhiteSpace(ctx.TabNumber) ? "" : ctx.TabNumber.Trim();
        var dept = string.IsNullOrWhiteSpace(ctx.Department) ? "" : ctx.Department.Trim();
        var itemRows = ctx.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        var tableRows = Math.Max(MinTableRows, itemRows.Count);

        sb.Append("<section class=\"sios-page sios-page-portrait sios-dsiz\">");
        sb.Append($"<div class=\"sios-card-no\">ЛИЧНАЯ КАРТОЧКА № {E(ctx.CardNumber)}</div>");
        sb.Append("<div class=\"sios-subtitle\">учета выдачи дерматологических СИЗ и смывающих средств</div>");

        sb.Append("<div class=\"sios-profile\">");
        sb.Append("<div class=\"sios-profile-left\">");
        AppendField(sb, "Фамилия", last);
        AppendField(sb, "Имя", first);
        AppendField(sb, "Отчество (при наличии)", middle);
        AppendField(sb, "Табельный номер", tab);
        AppendField(sb, "Структурное подразделение", dept);
        AppendField(sb, "Профессия (должность)", ctx.Profession);
        AppendField(sb, "Дата поступления на работу", hire);
        AppendField(sb, "Дата изменения профессии (должности) или", profChange);
        sb.Append("<div class=\"sios-field sios-field-multiline\">перевода в другое структурное<br/>подразделение</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"sios-profile-right\">");
        AppendPersonalProfileRight(sb, sex, sizes);
        sb.Append("</div></div>");

        sb.Append("<table class=\"sios-table sios-front-table\"><thead><tr>");
        sb.Append("<th>Наименование СИЗ</th>");
        sb.Append("<th>Пункт<br/>Норм</th>");
        sb.Append("<th>Единица измерения,<br/>периодичность выдачи</th>");
        sb.Append("<th>Количество<br/>на период</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < tableRows; i++)
        {
            sb.Append("<tr>");
            if (i < itemRows.Count)
            {
                var row = itemRows[i];
                sb.Append($"<td>{E(row.Name)}</td>");
                sb.Append($"<td class=\"sios-center\">{E(DsizNormPoint(row, i))}</td>");
                sb.Append($"<td>{E(DsizUnitAndPeriod(row))}</td>");
                sb.Append($"<td class=\"sios-center\">{E(DsizPeriodQty(row))}</td>");
            }
            else
            {
                for (var c = 0; c < 4; c++) sb.Append("<td>&nbsp;</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p class=\"sios-dsiz-foot\">Примечание: срок использования каждого дозируемого/смывающего средства — 1 год.</p>");

        sb.Append("<div class=\"sios-footer\">");
        sb.Append("<div class=\"sios-footer-text\">Ответственное лицо за ведение карточек<br/>учета выдачи СИЗ</div>");
        AppendResponsibleSignBlock(sb, ctx.ResponsiblePerson);
        sb.Append("</div></section>");
    }

    private static void AppendField(StringBuilder sb, string label, string value) =>
        sb.Append($"<div class=\"sios-field\"><span class=\"sios-label\">{E(label)}:</span>&nbsp;&nbsp;{E(value)}</div>");

    private static void AppendResponsibleSignBlock(StringBuilder sb, string? responsiblePerson)
    {
        sb.Append("<div class=\"sios-sign-block\">");
        sb.Append("<div class=\"sios-sign-col\">");
        sb.Append("<div class=\"sios-sign-line\"></div>");
        sb.Append("<div class=\"sios-sign-caption\">(подпись)</div>");
        sb.Append("</div>");
        sb.Append("<div class=\"sios-sign-col\">");
        sb.Append($"<div class=\"sios-name-line\">{E(FormatPersonShortName(responsiblePerson))}</div>");
        sb.Append("<div class=\"sios-sign-caption\">(фамилия, инициалы)</div>");
        sb.Append("</div></div>");
    }

    private static void AppendPersonalProfileRight(StringBuilder sb, string sex, EmployeePpeSizes sizes)
    {
        var clothes = !string.IsNullOrWhiteSpace(sizes.ClothesSize) ? sizes.ClothesSize : sizes.UniformSize;
        var sizod = !string.IsNullOrWhiteSpace(sizes.RespiratorSize) ? sizes.RespiratorSize : sizes.GasMaskSize;

        AppendRightSizeLine(sb, "Пол", sex);
        AppendRightSizeLine(sb, "Рост", sizes.Height);
        AppendRightSizeLine(sb, "Размер одежды", clothes);
        AppendRightSizeLine(sb, "Размер обуви", sizes.ShoeSize);
        AppendRightSizeLine(sb, "Размер головного убора", sizes.HeadwearSize);
        AppendRightSizeLine(sb, "Размер СИЗОД", sizod);
        AppendRightSizeLine(sb, "Размер СИЗ рук", sizes.GloveSize);
    }

    private static void AppendRightSizeLine(StringBuilder sb, string label, string? value)
    {
        var display = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        sb.Append("<div class=\"sios-right-line\">");
        sb.Append($"<span class=\"sios-right-label\">{E(label)}</span>");
        sb.Append("&nbsp;&nbsp;");
        sb.Append($"<span class=\"sios-right-value\">{E(display)}</span>");
        sb.Append("</div>");
    }

    private static EmployeePpeSizes ResolveSizes(SiosCardPrintContext ctx, string employee)
    {
        if (ctx.SizesByEmployee is not null)
        {
            foreach (var kv in ctx.SizesByEmployee)
            {
                if (string.Equals(kv.Key, employee, StringComparison.OrdinalIgnoreCase))
                {
                    return PpeSizeReference.SanitizeForGender(employee, kv.Value);
                }
            }
        }

        return PpeSizeReference.SanitizeForGender(employee, PpeSizeReference.DefaultsForEmployee(employee));
    }

    private static string ResolveSex(SiosCardPrintContext ctx, string employee)
    {
        if (!string.IsNullOrWhiteSpace(ctx.Sex))
        {
            return ctx.Sex.Trim();
        }

        return InferSex(employee);
    }

    private static string InferSex(string employee)
    {
        return PpeSizeReference.InferredFemale(employee) ? "жен." : "муж.";
    }

    private static string FormatPersonShortName(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        if (parts[0].Contains('.', StringComparison.Ordinal) && parts[0].Length <= 8)
        {
            return $"{parts[^1]} {parts[0]}";
        }

        var surname = parts[0];
        var initials = $"{parts[1][0]}.";
        if (parts.Length > 2)
        {
            initials += $"{parts[2][0]}.";
        }

        return $"{surname} {initials}";
    }

    private static (string Last, string First, string Middle) SplitName(string fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (
            parts.Length > 0 ? parts[0] : "",
            parts.Length > 1 ? parts[1] : "",
            parts.Length > 2 ? parts[2] : "");
    }

    private static string FormatDate(DateTime date) =>
        date == default ? "" : date.ToString("dd.MM.yyyy");

    private static string SimplifyPpeName(string name)
    {
        var source = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source)) return "";
        if (ContainsAny(source, "костюм")) return "Костюм рабочий";
        if (ContainsAny(source, "очки") && ContainsAny(source, "защит", "специаль")) return "Очки от ударов";
        if (ContainsAny(source, "очки")) return "Очки защитные";
        if (ContainsAny(source, "каска")) return "Каска защитная";
        if (ContainsAny(source, "ботин", "сапог")) return "Обувь специальная";
        if (ContainsAny(source, "перчат", "рукавиц")) return "Перчатки защитные";
        if (ContainsAny(source, "жилет")) return "Жилет сигнальный";
        if (ContainsAny(source, "респир")) return "Респиратор фильтрующий";
        if (ContainsAny(source, "фартук", "халат")) return "Фартук защитный";
        if (ContainsAny(source, "каск", "шлем", "шапк", "берет")) return "Головной убор";
        return source;
    }

    private static bool ContainsAny(string text, params string[] parts) =>
        parts.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string GetPpeUnit(SiosEditableRow row)
    {
        var text = $"{row.Name} {row.Qty}";
        if (ContainsAny(text, "пара", "бот", "сапог", "бахил", "перчат", "рукавиц")) return "пара";
        if (ContainsAny(text, "крем", "мазь")) return "туба";
        return "штука";
    }

    private static string PersonalUnitAndPeriod(SiosEditableRow row)
    {
        var unit = GetPpeUnit(row);
        var period = ResolveIssuePeriodLabel(row);
        if (string.IsNullOrWhiteSpace(period))
        {
            return unit;
        }

        return $"{unit}, {period}";
    }

    private static string FrontQuantityFromStandard(SiosEditableRow row)
    {
        var norm = (row.Norm ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(norm) &&
            !norm.Equals("По норме", StringComparison.OrdinalIgnoreCase) &&
            !norm.Equals("Определяется документами", StringComparison.OrdinalIgnoreCase))
        {
            if (norm.Contains("износ", StringComparison.OrdinalIgnoreCase))
            {
                return "до износа";
            }

            // «1 раз на 2 года» — на лицевой оставляем формулировку нормы.
            if (LooksLikePeriodNorm(norm))
            {
                return norm.ToLowerInvariant();
            }

            return NormalizeCount(norm);
        }

        var period = ResolveIssuePeriodLabel(row);
        if (string.Equals(period, "до износа", StringComparison.OrdinalIgnoreCase))
        {
            return "до износа";
        }

        return NormalizeCount(row.Qty);
    }

    private static string DutyNormPoint(SiosEditableRow row)
    {
        // Колонка «Пункт Норм» заполняется только вручную на предпросмотре.
        // Не подставляем Qty/Norm — там количество, не пункт типовых норм.
        return (row.NormPoint ?? string.Empty).Trim();
    }

    private static string DutyUnitAndPeriod(SiosEditableRow row)
    {
        var unit = !string.IsNullOrWhiteSpace(row.Unit) ? row.Unit.Trim() : GetPpeUnit(row);
        var life = (row.ShelfLife ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(life))
        {
            return unit;
        }

        if (string.Equals(life, "До износа", StringComparison.OrdinalIgnoreCase))
        {
            return $"{unit}, до износа";
        }

        return $"{unit}, {life.ToLowerInvariant()}";
    }

    private static string DutyQuantity(SiosEditableRow row)
    {
        var t = (row.Qty ?? string.Empty).Trim();
        if (t.Length == 0) t = (row.Norm ?? string.Empty).Trim();
        return NormalizeCount(t);
    }

    private static string ResolveBackIssueMethod(SiosEditableRow row)
    {
        // Сноска *: «Лично/дозатор» заполняется только для дерматологических СИЗ.
        if (!string.IsNullOrWhiteSpace(row.IssueMethod) &&
            row.IssueMethod.Contains("дозир", StringComparison.OrdinalIgnoreCase))
        {
            return "дозатор";
        }

        // Обычные СИЗ — ячейка пустая (заполняют от руки при выдаче).
        return string.Empty;
    }

    private static string DsizNormPoint(SiosEditableRow row, int index)
    {
        if (!string.IsNullOrWhiteSpace(row.NormPoint))
        {
            return row.NormPoint.Trim();
        }

        return string.Empty;
    }

    private static string DsizUnitAndPeriod(SiosEditableRow row)
    {
        var unit = !string.IsNullOrWhiteSpace(row.Unit) ? row.Unit.Trim() : "л/кг";
        return $"{unit}, 1 год";
    }

    private static string DsizPeriodQty(SiosEditableRow row) => NormalizeCount(row.Qty);

    private static string GetModelDescription(SiosEditableRow row)
    {
        // Только то, что задано явно (из номенклатуры / правки на предпросмотре).
        // Никаких вымышленных «Hammer / TREK / Модель по норме …».
        var custom = (row.ModelDescription ?? string.Empty).Trim();
        if (custom.Length > 0)
        {
            return custom;
        }

        // Если модель не заполнена — полное наименование позиции (как в стандарте/номенклатуре).
        return (row.Name ?? string.Empty).Trim();
    }

    private static string ResolveBackIssueDate(SiosEditableRow row, SiosCardPrintContext ctx)
    {
        var issueDate = ctx.PpeIssueDate != default ? ctx.PpeIssueDate : ctx.HireDate;
        if (issueDate != default)
        {
            return FormatDate(issueDate);
        }

        return string.IsNullOrWhiteSpace(row.BackIssueDate) ? string.Empty : row.BackIssueDate.Trim();
    }

    private static string ResolveBackIssueSign(SiosEditableRow row) =>
        string.IsNullOrWhiteSpace(row.BackIssueSign) ? string.Empty : row.BackIssueSign.Trim();

    private static string IssuedQuantity(SiosEditableRow row, bool isDuty)
    {
        if (!string.IsNullOrWhiteSpace(row.BackIssueQty)) return row.BackIssueQty.Trim();

        if (isDuty)
        {
            var t = (row.Qty ?? string.Empty).Trim();
            if (t.Length == 0) t = (row.Norm ?? string.Empty).Trim();
            return NormalizeCount(t);
        }

        var norm = (row.Norm ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(norm) &&
            !norm.Equals("По норме", StringComparison.OrdinalIgnoreCase) &&
            !norm.Equals("Определяется документами", StringComparison.OrdinalIgnoreCase))
        {
            return FormatIssuedQuantityFromNorm(norm);
        }

        var qty = NormalizeCount(row.Qty);
        var period = ResolveIssuePeriodLabel(row);
        if (string.IsNullOrWhiteSpace(period) ||
            string.Equals(period, "до износа", StringComparison.OrdinalIgnoreCase))
        {
            // «До износа» — только количество, без ложного «(год)».
            return qty;
        }

        return $"{qty} ({period})";
    }

    /// <summary>
    /// Норма вида «1 раз на 2 года» → на обороте «1 (2 года)»; «до износа» → «до износа».
    /// </summary>
    private static string FormatIssuedQuantityFromNorm(string norm)
    {
        if (norm.Contains("износ", StringComparison.OrdinalIgnoreCase))
        {
            return "до износа";
        }

        var oncePer = System.Text.RegularExpressions.Regex.Match(
            norm,
            @"(\d+)\s*раз(?:а|ов)?\s*(?:на|в)\s*(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (oncePer.Success)
        {
            var qty = oncePer.Groups[1].Value;
            var n = int.Parse(oncePer.Groups[2].Value);
            return $"{qty} ({FormatPeriodCount(n, oncePer.Groups[3].Value)})";
        }

        var yearsOnly = System.Text.RegularExpressions.Regex.Match(
            norm,
            @"(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (yearsOnly.Success && !System.Text.RegularExpressions.Regex.IsMatch(norm, @"^\d+$"))
        {
            var qty = NormalizeCount(norm);
            var n = int.Parse(yearsOnly.Groups[1].Value);
            // «2 года» без количества выдачи → qty из начала строки или 1
            return $"{qty} ({FormatPeriodCount(n, yearsOnly.Groups[2].Value)})";
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"^\d+$"))
        {
            return norm;
        }

        return NormalizeCount(norm);
    }

    private static bool LooksLikePeriodNorm(string norm) =>
        norm.Contains("раз", StringComparison.OrdinalIgnoreCase)
        || norm.Contains("год", StringComparison.OrdinalIgnoreCase)
        || norm.Contains("лет", StringComparison.OrdinalIgnoreCase)
        || norm.Contains("месяц", StringComparison.OrdinalIgnoreCase)
        || norm.Contains("износ", StringComparison.OrdinalIgnoreCase);

    private static string ResolveIssuePeriodLabel(SiosEditableRow row)
    {
        var norm = (row.Norm ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(norm) &&
            !norm.Equals("По норме", StringComparison.OrdinalIgnoreCase) &&
            !norm.Equals("Определяется документами", StringComparison.OrdinalIgnoreCase))
        {
            if (norm.Contains("износ", StringComparison.OrdinalIgnoreCase))
            {
                return "до износа";
            }

            var oncePer = System.Text.RegularExpressions.Regex.Match(
                norm,
                @"раз(?:а|ов)?\s*(?:на|в)\s*(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (oncePer.Success)
            {
                return FormatPeriodCount(int.Parse(oncePer.Groups[1].Value), oncePer.Groups[2].Value);
            }

            var yearsOnly = System.Text.RegularExpressions.Regex.Match(
                norm,
                @"(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (yearsOnly.Success)
            {
                return FormatPeriodCount(int.Parse(yearsOnly.Groups[1].Value), yearsOnly.Groups[2].Value);
            }
        }

        var life = (row.ShelfLife ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(life))
        {
            return string.Empty;
        }

        if (life.Contains("износ", StringComparison.OrdinalIgnoreCase))
        {
            return "до износа";
        }

        var fromLife = System.Text.RegularExpressions.Regex.Match(
            life,
            @"(\d+)\s*(год(?:а|ов)?|лет|месяц(?:а|ев)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (fromLife.Success)
        {
            return FormatPeriodCount(int.Parse(fromLife.Groups[1].Value), fromLife.Groups[2].Value);
        }

        return life.ToLowerInvariant();
    }

    private static string FormatPeriodCount(int n, string unitRaw)
    {
        var unit = (unitRaw ?? string.Empty).Trim().ToLowerInvariant();
        if (unit.StartsWith("месяц", StringComparison.Ordinal))
        {
            return n switch
            {
                1 => "месяц",
                >= 2 and <= 4 => $"{n} месяца",
                _ => $"{n} месяцев"
            };
        }

        // год / года / лет
        if (n == 1)
        {
            return "год";
        }

        return $"{n} {RussianYearWord(n)}";
    }

    private static string RussianYearWord(int n)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        if (mod100 is >= 11 and <= 14)
        {
            return "лет";
        }

        return mod10 switch
        {
            1 => "год",
            2 or 3 or 4 => "года",
            _ => "лет"
        };
    }

    private static string NormalizeCount(string? qty)
    {
        if (string.IsNullOrWhiteSpace(qty)) return "1";
        var s = qty!.Replace('х', '-').Replace('×', '-');
        var current = "";
        foreach (var ch in s)
        {
            if (char.IsDigit(ch)) current += ch;
            else if (current.Length > 0) break;
        }

        return current.Length > 0 ? current : "1";
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
