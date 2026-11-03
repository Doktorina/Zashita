using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Med;

/// <summary>Печатная форма «Направление на медицинский осмотр работника» (бланк МО, предпросмотр/печать).</summary>
public static class MedDirectionPrintBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Build(MedDirectionRecord document, string? signatureImageSrc = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append("""<div class="med-dir-sheet">""");

        AppendHeader(sb, document);

        sb.Append("""<h1 class="med-dir-title">Направление на медицинский осмотр работника</h1>""");
        sb.Append("""<div class="med-dir-title-sub">""");
        sb.Append(BuildExamTypeLegend(document));
        sb.Append(""" <span class="med-dir-hint">(нужное подчеркнуть)</span></div>""");

        // Таблица вместо <ol>: Word не раздувает левый отступ списка.
        sb.Append("""<div class="med-dir-fields">""");

        OpenField(sb, 1);
        sb.Append("""<span class="med-dir-label">Дата направления</span> """);
        sb.Append(Underline(document, "referral-date", FormatLongDate(document.Date), valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 2);
        sb.Append("""<span class="med-dir-label">Дата осмотра</span> """);
        sb.Append(Underline(document, "exam-date",
            document.ExamDate is null ? string.Empty : FormatLongDate(document.ExamDate.Value)));
        CloseField(sb);

        OpenField(sb, 3);
        sb.Append("""<span class="med-dir-label">Вид медицинского осмотра</span> """);
        sb.Append(Underline(document, "exam-type", ResolveExamTypeFull(document), valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 4);
        sb.Append("""<span class="med-dir-label">Фамилия</span> """);
        sb.Append(Underline(document, "last-name", document.LastName, valueHighlight: true));
        sb.Append("""<br /><span class="med-dir-label">Имя</span> """);
        sb.Append(Underline(document, "first-name", document.FirstName, valueHighlight: true));
        sb.Append("""<br /><span class="med-dir-label">Отчество</span> """);
        sb.Append(Underline(document, "middle-name", document.MiddleName, valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 5);
        sb.Append("""<span class="med-dir-label">Пол работника</span> """);
        sb.Append(BuildGender(document));
        sb.Append(""" <span class="med-dir-hint">(нужное подчеркнуть)</span>""");
        CloseField(sb);

        var ageText = FormatAgeDisplay(document);
        OpenField(sb, 6);
        sb.Append("""<span class="med-dir-label">Возраст</span> """);
        sb.Append(Underline(document, "age", ageText, "med-dir-u-short", valueHighlight: true));
        sb.Append("""; <span class="med-dir-label">дата рождения</span> """);
        sb.Append(Underline(document, "birth-date",
            document.BirthDate?.ToString("dd.MM.yyyy") ?? "", "med-dir-u-date", valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 7);
        sb.Append("""<span class="med-dir-label">Домашний адрес работника</span> """);
        sb.Append(Underline(document, "address", document.HomeAddress, valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 8);
        sb.Append("""<span class="med-dir-label">Место работы:</span>""");
        sb.Append("""<div class="med-dir-sub">8.1. <span class="med-dir-label">Полное наименование работодателя</span> """);
        sb.Append(Underline(document, "employer", document.Organization, valueHighlight: true));
        sb.Append("</div>");
        sb.Append("""<div class="med-dir-sub">8.2. <span class="med-dir-label">Электронная почта, контактный телефон работодателя</span> """);
        sb.Append(Underline(document, "employer-contacts", document.EmployerContacts));
        sb.Append("</div>");
        sb.Append("""<div class="med-dir-sub">8.3. <span class="med-dir-label">Форма собственности и вид экономической деятельности работодателя по ОКВЭД</span> """);
        sb.Append(Underline(document, "okved", document.OwnershipOkved));
        sb.Append("</div>");
        sb.Append("""<div class="med-dir-sub">8.4. <span class="med-dir-label">Наименование структурного подразделения (при наличии)</span> """);
        sb.Append(Underline(document, "department", document.Department));
        sb.Append("</div>");
        CloseField(sb);

        OpenField(sb, 9);
        sb.Append("""<span class="med-dir-label">Наименование должности (профессии) или вида работ</span> """);
        sb.Append(Underline(document, "profession", document.ProfessionOrWorkType, valueHighlight: true));
        CloseField(sb);

        OpenField(sb, 10);
        sb.Append("""<span class="med-dir-label">Профессия (должность) (на которую принимается на работу)*</span> """);
        var hireProfession = IsPreliminaryExam(document.ExamType) ? document.HireProfession : string.Empty;
        sb.Append(Underline(document, "hire-profession", hireProfession, valueHighlight: IsPreliminaryExam(document.ExamType)));
        CloseField(sb);

        OpenField(sb, 11);
        sb.Append("""<span class="med-dir-label">Стаж работы</span>""");
        sb.Append("""<div class="med-dir-sub">11.1. общий """);
        sb.Append(Underline(document, "exp-total", document.ExperienceTotal, "med-dir-u-short"));
        sb.Append(" (года)</div>");
        sb.Append("""<div class="med-dir-sub">11.2. в профессии """);
        sb.Append(Underline(document, "exp-prof", document.ExperienceProfession, "med-dir-u-short"));
        sb.Append(" (года)</div>");
        sb.Append("""<div class="med-dir-sub">11.3. во вредных условиях труда (конкретизировать вид фактора) (обязательно) """);
        sb.Append(Underline(document, "exp-hazard", document.ExperienceHazardous, "med-dir-u-short"));
        sb.Append(" (года)</div>");
        CloseField(sb);

        OpenField(sb, 12);
        sb.Append("""<span class="med-dir-label">Характеристика условий труда на рабочем месте</span>""");
        sb.Append("""
            <table class="med-dir-factors">
              <colgroup>
                <col style="width:42%" />
                <col style="width:14%" />
                <col style="width:26%" />
                <col style="width:18%" />
              </colgroup>
              <thead>
                <tr>
                  <th>Наименование вредных и (или) опасных производственных факторов, видов работ</th>
                  <th>Код фактора по приказу Минздрава РФ № 29н</th>
                  <th>Уровень фактора по результатам лабораторного контроля</th>
                  <th>Класс условий труда</th>
                </tr>
              </thead>
              <tbody>
            """);

        var factors = document.Factors.Count > 0
            ? document.Factors
            : [new MedDirectionFactorLine()];
        for (var i = 0; i < factors.Count; i++)
        {
            var f = factors[i];
            var code29 = string.IsNullOrWhiteSpace(f.Code29n)
                ? string.Empty
                : MedItemNumber.Normalize(f.Code29n);
            sb.Append("<tr>");
            sb.Append(Td(document, $"f{i}-name", f.FactorName));
            sb.Append(Td(document, $"f{i}-c29", code29, "med-dir-num"));
            sb.Append(Td(document, $"f{i}-level", f.Level));
            sb.Append(Td(document, $"f{i}-class", f.ConditionsClass));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        CloseField(sb);
        sb.Append("</div>");

        AppendSignature(sb, document, signatureImageSrc);

        sb.Append("""
            <div class="med-dir-footnote">
              * - заполняется только при оформлении направления на предварительный медицинский осмотр
            </div>
            """);

        sb.Append("</div>");
        return sb.ToString();
    }

    public static string BuildDownloadDocument(
        MedDirectionRecord document,
        string css,
        string? logoDataUrl = null,
        string? signatureImageSrc = null)
    {
        var body = Build(document, signatureImageSrc)
            .Replace(" contenteditable=\"true\"", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" contenteditable='true'", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(logoDataUrl))
        {
            body = body.Replace(
                "src=\"/img/med-org-logo.png\"",
                $"src=\"{logoDataUrl}\"",
                StringComparison.OrdinalIgnoreCase);
        }

        // Свой CSS для Word: без экранных height/overflow и без отступов <ol>.
        return $$"""
            <!DOCTYPE html>
            <html xmlns:o="urn:schemas-microsoft-com:office:office"
                  xmlns:w="urn:schemas-microsoft-com:office:word"
                  xmlns="http://www.w3.org/TR/REC-html40">
            <head>
              <meta http-equiv="Content-Type" content="text/html; charset=utf-8" />
              <!--[if gte mso 9]><xml>
                <w:WordDocument>
                  <w:View>Print</w:View>
                  <w:Zoom>100</w:Zoom>
                  <w:DoNotOptimizeForBrowser/>
                </w:WordDocument>
              </xml><![endif]-->
              <style>
            {{css}}
            @page { size: 210mm 297mm; margin: 12mm; }
            html, body {
              margin: 0 !important;
              padding: 0 !important;
              background: #fff !important;
              color: #000 !important;
              font-family: "Times New Roman", Times, serif;
              font-size: 10pt;
            }
            .med-dir-preview-doc,
            .sout-eq-doc,
            .sout-eq-doc.med-print-sheet,
            .sout-eq-doc.med-dir-preview-doc {
              width: 100% !important;
              max-width: 100% !important;
              height: auto !important;
              min-height: 0 !important;
              max-height: none !important;
              padding: 0 !important;
              margin: 0 !important;
              box-shadow: none !important;
              overflow: visible !important;
              background: #fff !important;
            }
            .med-dir-sheet { width: 100% !important; overflow: visible !important; color: #000 !important; }
            .med-dir-header { width: 100%; border-collapse: collapse; margin-bottom: 8pt; }
            .med-dir-header td { border: none; vertical-align: top; padding: 0; }
            .med-dir-logo { width: 90pt; text-align: center; }
            .med-dir-logo-mark { width: 48pt; height: 48pt; }
            .med-dir-logo-text { font-size: 8pt; font-weight: bold; color: #1a6b4a; }
            .med-dir-mo-card { text-align: right; font-size: 8.5pt; }
            .med-dir-title { text-align: center; font-size: 13pt; font-weight: bold; margin: 6pt 0 4pt; }
            .med-dir-title-sub { text-align: center; font-size: 8.5pt; margin-bottom: 8pt; }
            .med-dir-fields { width: 100%; margin: 0; padding: 0; }
            .med-dir-item { width: 100%; border-collapse: collapse; margin: 0 0 4pt 0; }
            .med-dir-item td { border: none; padding: 0; vertical-align: top; }
            .med-dir-n { width: 22pt; font-size: 10pt; }
            .med-dir-b { font-size: 10pt; }
            .med-dir-sub { margin: 2pt 0 0 6pt; }
            .med-dir-factors { width: 100%; border-collapse: collapse; margin-top: 4pt; font-size: 8pt; }
            .med-dir-factors th, .med-dir-factors td { border: 1px solid #000; padding: 2pt; vertical-align: top; }
            .med-dir-mo-name {
              font-size: 10pt !important;
              font-weight: bold !important;
              text-decoration: underline !important;
            }
            .med-dir-value,
            .med-dir-value b,
            .med-dir-u.med-dir-value,
            .med-dir-u.med-dir-value b {
              font-weight: bold !important;
              font-size: 10.5pt !important;
            }
            .med-dir-u, .med-dir-u-mark {
              border-bottom: 1px solid #000 !important;
              text-decoration: none !important;
              padding: 0 3pt 0;
              box-shadow: none !important;
            }
            .med-dir-u u, .med-dir-u-mark u {
              text-decoration: none !important;
              border-bottom: none !important;
            }
            .med-dir-u-blank { display: inline-block; min-width: 120pt; }
            .med-dir-u-short { display: inline-block; min-width: 40pt; text-align: center; }
            .med-dir-u-date { display: inline-block; min-width: 70pt; text-align: center; }
            .med-dir-sign { margin-top: 12pt; }
            .med-dir-sign-row { width: 100%; border-collapse: collapse; }
            .med-dir-sign-row td { border: none; vertical-align: bottom; padding: 0; }
            .med-dir-sign-position { font-weight: bold; width: 34%; }
            .med-dir-sign-row td.med-dir-sign-gap { border-bottom: 1px solid #000 !important; height: 28pt; text-align: center; width: 32%; }
            .med-dir-sign-name { width: 34%; text-align: right; }
            .med-dir-sign-name-shelf {
              display: inline-block;
              min-width: 120pt;
              border-bottom: 1px solid #000;
              padding: 0 4pt 2pt;
              text-align: center;
              font-weight: bold;
            }
            .med-dir-sign-image { max-height: 26pt; }
            .med-dir-footnote { margin-top: 8pt; font-size: 8.5pt; }
              </style>
            </head>
            <body>
            <div class="med-dir-preview-doc">
            {{body}}
            </div>
            </body>
            </html>
            """;
    }

    private static void OpenField(StringBuilder sb, int number)
    {
        sb.Append("""<table class="med-dir-item" cellspacing="0" cellpadding="0"><tr>""");
        sb.Append($"""<td class="med-dir-n">{number}.</td>""");
        sb.Append("""<td class="med-dir-b">""");
    }

    private static void CloseField(StringBuilder sb)
    {
        sb.Append("</td></tr></table>");
    }

    private static void AppendHeader(StringBuilder sb, MedDirectionRecord document)
    {
        var name = Ov(document, "mo-name", document.MedicalOrgName);
        var address = Ov(document, "mo-address",
            string.IsNullOrWhiteSpace(document.MedicalOrgAddress)
                ? ExtractAddress(document.MedicalOrgDetails)
                : document.MedicalOrgAddress);
        var ogrn = Ov(document, "ogrn", document.Ogrn);
        var inn = Ov(document, "inn", document.Inn);
        var kpp = Ov(document, "kpp", document.Kpp);
        var license = Ov(document, "license", document.LicenseInfo);
        var idsLine = Ov(document, "mo-ids", BuildIdsLine(ogrn, inn, kpp));
        var brand = ShortBrand(name);

        sb.Append("""<table class="med-dir-header" cellspacing="0" cellpadding="0"><tr>""");
        sb.Append("""<td class="med-dir-logo">""");
        // PNG, не SVG — Word и html2canvas стабильно сохраняют картинку.
        sb.Append("""<img class="med-dir-logo-mark" src="/img/med-org-logo.png" width="68" height="68" alt="" />""");
        sb.Append($"<div class=\"med-dir-logo-text\" contenteditable=\"true\" data-override-key=\"mo-brand\">{Web(Ov(document, "mo-brand", brand))}</div>");
        sb.Append("</td>");

        sb.Append("""<td class="med-dir-mo-card">""");
        sb.Append($"<div class=\"med-dir-mo-name med-dir-value\" contenteditable=\"true\" data-override-key=\"mo-name\">{Web(name)}</div>");
        sb.Append($"<div contenteditable=\"true\" data-override-key=\"mo-address\">{Web(address)}</div>");
        sb.Append($"<div contenteditable=\"true\" data-override-key=\"mo-ids\">{Web(idsLine)}</div>");
        sb.Append($"<div class=\"med-dir-mo-license\" contenteditable=\"true\" data-override-key=\"license\">{Web(license)}</div>");
        sb.Append("</td></tr></table>");
    }

    private static void AppendSignature(StringBuilder sb, MedDirectionRecord document, string? signatureImageSrc = null)
    {
        var signPosition = ResolveSignPosition(document);
        var signName = ResolveSignName(document);
        var src = string.IsNullOrWhiteSpace(signatureImageSrc)
            ? document.SignatureImageUrl
            : signatureImageSrc;

        sb.Append("""<div class="med-dir-sign">""");
        sb.Append("""<div>Уполномоченный представитель работодателя, составивший направление</div>""");
        sb.Append("""<table class="med-dir-sign-row" cellspacing="0" cellpadding="0"><tr>""");
        sb.Append($"<td class=\"med-dir-sign-position\" contenteditable=\"true\" data-override-key=\"sign-position\">{Web(Ov(document, "sign-position", signPosition))}</td>");
        sb.Append("""<td class="med-dir-sign-gap">""");
        if (!string.IsNullOrWhiteSpace(src) && IsSafeImageSrc(src))
        {
            // Не HtmlEncode весь src: data:image/...;base64 ломается (&, и т.п. в редких случаях),
            // а для путей достаточно экранирования кавычек в атрибуте.
            sb.Append($"<img class=\"med-dir-sign-image\" src=\"{Attr(src)}\" alt=\"Подпись\" />");
        }

        sb.Append("</td>");
        sb.Append("""<td class="med-dir-sign-name">""");
        sb.Append($"<span class=\"med-dir-sign-name-shelf\" contenteditable=\"true\" data-override-key=\"sign-name\">{Web(Ov(document, "sign-name", signName))}</span>");
        sb.Append("</td>");
        sb.Append("</tr></table>");
        sb.Append("""<div class="med-dir-sign-hint">подпись (должность, фамилия, инициалы (при наличии))</div>""");
        sb.Append("</div>");
    }

    private static bool IsSafeImageSrc(string src)
    {
        // Только встроенные data: URL — путь /uploads/... со временем пропадает
        // и даёт «битую» иконку в предпросмотре и PDF.
        return src.Trim().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Экранирование значения HTML-атрибута (не полное HtmlEncode текста).</summary>
    private static string Attr(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);
    }

    private static bool IsPreliminaryExam(string? examType) =>
        MedStandardAssignmentBuilder.IsPreliminaryKind(examType);

    private static string BuildIdsLine(string ogrn, string inn, string kpp)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ogrn))
        {
            parts.Add($"ОГРН {ogrn.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(inn))
        {
            parts.Add($"ИНН {inn.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(kpp))
        {
            parts.Add($"КПП {kpp.Trim()}");
        }

        return string.Join(", ", parts);
    }

    private static string ShortBrand(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Медцентр";
        }

        value = value.Replace("ООО", "", StringComparison.OrdinalIgnoreCase)
            .Replace("МЦ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("«", "").Replace("»", "").Replace("\"", "")
            .Trim(' ', ',', '-', '–');
        return string.IsNullOrWhiteSpace(value) ? "Медцентр" : value;
    }

    private static string ExtractAddress(string? details)
    {
        var text = (details ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var first = text.Split(',', 2)[0].Trim();
        return first;
    }

    private static string FormatLongDate(DateTime date) =>
        date.ToString("dd MMMM yyyy", Ru);

    private static string ResolveExamTypeFull(MedDirectionRecord document)
    {
        var examType = (document.ExamType ?? string.Empty).Trim();
        var lower = examType.ToLowerInvariant().Replace('ё', 'е');
        if (lower.Contains("предвар", StringComparison.Ordinal))
        {
            return "предварительный (при поступлении на работу)";
        }

        if (lower.Contains("период", StringComparison.Ordinal))
        {
            return "периодический";
        }

        if (lower.Contains("внеочеред", StringComparison.Ordinal))
        {
            return "внеочередной";
        }

        if (lower.Contains("профпригод", StringComparison.Ordinal) ||
            lower.Contains("экспертиз", StringComparison.Ordinal))
        {
            return "экспертизу профпригодности";
        }

        return examType;
    }

    private static string BuildExamTypeLegend(MedDirectionRecord document)
    {
        var examType = ResolveExamTypeFull(document).ToLowerInvariant().Replace('ё', 'е');
        var prelim = examType.Contains("предвар", StringComparison.Ordinal);
        var periodic = examType.Contains("период", StringComparison.Ordinal);
        var extraordinary = examType.Contains("внеочеред", StringComparison.Ordinal);
        var prof = examType.Contains("профпригод", StringComparison.Ordinal) ||
                   examType.Contains("экспертиз", StringComparison.Ordinal);

        static string Mark(string text, bool selected)
        {
            var encoded = WebUtility.HtmlEncode(text);
            return selected
                ? $"<span class=\"med-dir-u-mark med-dir-value\"><b>{encoded}</b></span>"
                : encoded;
        }

        return "(" +
               Mark("предварительный (при поступлении на работу)", prelim) + ", " +
               Mark("периодический", periodic) + ", " +
               Mark("внеочередной", extraordinary) + ", " +
               Mark("экспертизу профпригодности", prof) +
               ")";
    }

    private static string ResolveSignPosition(MedDirectionRecord document)
    {
        if (!string.IsNullOrWhiteSpace(document.AuthorizedRepresentativePosition))
        {
            return document.AuthorizedRepresentativePosition.Trim();
        }

        var raw = document.AuthorizedRepresentative?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return LooksLikePersonName(raw) ? string.Empty : raw;
    }

    private static string ResolveSignName(MedDirectionRecord document)
    {
        var raw = document.AuthorizedRepresentative?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(document.AuthorizedRepresentativePosition))
        {
            return LooksLikePersonName(raw) ? FormatInitialsName(raw) : string.Empty;
        }

        return FormatInitialsName(raw);
    }

    private static string FormatInitialsName(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            parts[1].Length > 1 &&
            parts[2].Length > 1 &&
            !parts[1].Contains('.', StringComparison.Ordinal))
        {
            // Фамилия Имя Отчество → И.О. Фамилия
            return $"{char.ToUpperInvariant(parts[1][0])}.{char.ToUpperInvariant(parts[2][0])}. {parts[0]}";
        }

        return value;
    }

    private static bool LooksLikePersonName(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal) && value.Any(char.IsLetter))
        {
            return true;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts.All(p => p.Length > 0 && char.IsUpper(p[0]));
    }

    private static string BuildGender(MedDirectionRecord document)
    {
        var g = Ov(document, "gender", document.Gender).Trim().ToLowerInvariant();
        var male = g.Contains("муж");
        var female = g.Contains("жен");

        var maleHtml = male
            ? """<span class="med-dir-u med-dir-value" contenteditable="true" data-override-key="gender-male"><b>мужской</b></span>"""
            : """<span contenteditable="true" data-override-key="gender-male">мужской</span>""";
        var femaleHtml = female
            ? """<span class="med-dir-u med-dir-value" contenteditable="true" data-override-key="gender-female"><b>женский</b></span>"""
            : """<span contenteditable="true" data-override-key="gender-female">женский</span>""";
        return $"{maleHtml}, {femaleHtml}";
    }

    private static int? ResolveAge(MedDirectionRecord document)
    {
        if (document.Age is > 0)
        {
            return document.Age;
        }

        return MedPeriodicity.CalculateAge(document.BirthDate, document.Date);
    }

    private static string FormatAgeDisplay(MedDirectionRecord document)
    {
        if (document.PrintOverrides.TryGetValue("age", out var overrideValue)
            && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return FormatAgeText(overrideValue.Trim());
        }

        var age = ResolveAge(document);
        return age is null or < 0 ? string.Empty : MedPeriodicity.FormatAgeYears(age.Value);
    }

    private static string FormatAgeText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 0 && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var age))
        {
            return MedPeriodicity.FormatAgeYears(age);
        }

        return raw;
    }

    private static string Underline(
        MedDirectionRecord document,
        string key,
        string value,
        string? extraClass = null,
        bool valueHighlight = false)
    {
        var classes = new List<string> { "med-dir-u" };
        if (!string.IsNullOrWhiteSpace(extraClass))
        {
            classes.Add(extraClass);
        }

        if (valueHighlight)
        {
            classes.Add("med-dir-value");
        }

        var cls = string.Join(' ', classes);
        var text = Ov(document, key, value)?.Trim() ?? string.Empty;
        if (IsUnderlinePlaceholder(text))
        {
            text = string.Empty;
        }

        // Пустые поля: только border-bottom + min-width (прочерки «___» + border дают двойную линию).
        string inner;
        if (string.IsNullOrWhiteSpace(text))
        {
            var isCompact = extraClass is not null &&
                            (extraClass.Contains("short", StringComparison.OrdinalIgnoreCase)
                             || extraClass.Contains("date", StringComparison.OrdinalIgnoreCase));
            if (!isCompact)
            {
                classes.Add("med-dir-u-blank");
            }

            cls = string.Join(' ', classes);
            inner = "&nbsp;";
        }
        else if (valueHighlight)
        {
            inner = $"<b>{Web(text)}</b>";
        }
        else
        {
            inner = Web(text);
        }

        return $"<span class=\"{cls}\" contenteditable=\"true\" data-override-key=\"{Web(key)}\">{inner}</span>";
    }

    /// <summary>Старые пустые поля хранили «___» — без очистки снова двоится с border-bottom.</summary>
    private static bool IsUnderlinePlaceholder(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch is '_' or '—' or '–' or '-' or '\u00A0')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string Td(
        MedDirectionRecord document,
        string key,
        string value,
        string? cssClass = null,
        bool valueHighlight = false)
    {
        var classes = new List<string>();
        if (!string.IsNullOrWhiteSpace(cssClass))
        {
            classes.Add(cssClass);
        }

        if (valueHighlight)
        {
            classes.Add("med-dir-value");
        }

        var cls = classes.Count == 0 ? string.Empty : $" class=\"{string.Join(' ', classes)}\"";
        var text = Ov(document, key, value)?.Trim() ?? string.Empty;
        var inner = string.IsNullOrWhiteSpace(text)
            ? Web(text)
            : valueHighlight
                ? $"<b>{Web(text)}</b>"
                : Web(text);
        return $"<td{cls} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{inner}</td>";
    }

    private static string Ov(MedDirectionRecord document, string key, string value)
    {
        if (document.PrintOverrides.TryGetValue(key, out var overrideValue) &&
            !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        return value ?? string.Empty;
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
