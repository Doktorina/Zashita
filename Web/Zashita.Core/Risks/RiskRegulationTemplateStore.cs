using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Шаблон «Положения» (методики ОПР) по организации — HTML с плейсхолдерами.</summary>
public sealed class RiskRegulationTemplateStore
{
    private readonly RiskDataPaths _paths;
    private readonly object _gate = new();

    public RiskRegulationTemplateStore(RiskDataPaths paths) => _paths = paths;

    public bool HasCustomTemplate(string organization) =>
        File.Exists(FilePath(organization));

    public string? GetCustomHtml(string organization)
    {
        var path = FilePath(organization);
        lock (_gate)
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
    }

    public string GetHtmlOrDefault(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        var html = GetCustomHtml(organization) ?? RiskOrgDocumentsPrintBuilder.DefaultRegulationBodyHtml();
        html = NormalizeRegulationFormatting(html);
        if (org.Length > 0)
        {
            html = InjectOrganizationPlaceholder(html, org);
        }

        return ReplaceStandaloneOrganizationParagraphs(html);
    }

    public void SaveHtml(string organization, string? html)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        var path = FilePath(org);
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.RegulationTemplatesFolder);
            var text = NormalizeRegulationFormatting((html ?? string.Empty).Trim());
            text = InjectOrganizationPlaceholder(text, org);
            text = ReplaceStandaloneOrganizationParagraphs(text);
            if (text.Length == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            File.WriteAllText(path, text, Encoding.UTF8);
            var meta = path + ".meta.json";
            File.WriteAllText(meta,
                $"{{\"organization\":{JsonEsc(org)},\"savedUtc\":\"{DateTime.UtcNow:O}\"}}",
                Encoding.UTF8);
        }
    }

    public string ImportFile(string organization, Stream stream, string fileName)
    {
        var org = (organization ?? string.Empty).Trim();
        var name = (fileName ?? string.Empty).Trim();
        var ext = Path.GetExtension(name).ToLowerInvariant();
        string html;
        if (ext is ".docx")
        {
            html = DocxToHtml(stream, org);
        }
        else if (ext is ".html" or ".htm")
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            html = reader.ReadToEnd();
        }
        else if (ext is ".txt" or ".md")
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            html = PlainTextToHtml(reader.ReadToEnd());
        }
        else
        {
            throw new InvalidOperationException("Поддерживаются файлы .docx, .html, .htm, .txt.");
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("Файл пуст или не удалось извлечь текст.");
        }

        SaveHtml(org, html);
        return html;
    }

    public void ResetToDefault(string organization) => SaveHtml(organization, null);

    /// <summary>
    /// Текст методики из встроенного образца .docx (ИП Нигамаев и аналоги).
    /// Организация — плейсхолдер {{Организация}} в колонтитуле и в заголовке 1-й стр.
    /// </summary>
    public static string LoadDefaultMethodologyHtml()
    {
        lock (DefaultMethodologyGate)
        {
            if (!string.IsNullOrEmpty(CachedDefaultMethodologyHtml))
            {
                return CachedDefaultMethodologyHtml;
            }

            var asm = typeof(RiskRegulationTemplateStore).Assembly;
            using var stream = asm.GetManifestResourceStream("Zashita.Core.Risks.Assets.metodika-default.docx");
            if (stream is null)
            {
                return string.Empty;
            }

            // Имя из образца — чтобы DocxToHtml / Inject заменили его на {{Организация}}.
            const string sampleOrg = "ИП Нигамаев Ринат Фаизович";
            var html = DocxToHtml(stream, sampleOrg);
            html = NormalizeRegulationFormatting(html);
            html = InjectOrganizationPlaceholder(html, sampleOrg);
            html = ReplaceStandaloneOrganizationParagraphs(html);
            if (!html.Contains("{{Организация}}", StringComparison.Ordinal))
            {
                html = """<p class="risks-regulation-running-header">{{Организация}}</p>""" + html;
            }

            CachedDefaultMethodologyHtml = html;
            return html;
        }
    }

    private static readonly object DefaultMethodologyGate = new();
    private static string? CachedDefaultMethodologyHtml;

    public string? TemplateFileName(string organization)
    {
        var path = FilePath(organization);
        return File.Exists(path) ? Path.GetFileName(path) : null;
    }

    public DateTime? LastWriteUtc(string organization)
    {
        var path = FilePath(organization);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    private string FilePath(string organization)
    {
        var safe = SafeOrgKey(organization);
        return Path.Combine(_paths.RegulationTemplatesFolder, safe + ".html");
    }

    private static string SafeOrgKey(string organization)
    {
        var org = (organization ?? string.Empty).Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(org)))[..16].ToLowerInvariant();
        var slug = Regex.Replace(org, @"[^a-zа-я0-9]+", "-", RegexOptions.IgnoreCase);
        if (slug.Length > 40)
        {
            slug = slug[..40];
        }

        return string.IsNullOrWhiteSpace(slug) ? hash : $"{slug}-{hash}";
    }

    private static string JsonEsc(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string PlainTextToHtml(string text)
    {
        var parts = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var sb = new StringBuilder();
        foreach (var line in parts)
        {
            var t = line.Trim();
            if (t.Length == 0)
            {
                sb.Append("<br/>");
                continue;
            }

            if (IsTopLevelNumberedHeading(t))
            {
                sb.Append($"<h2 class=\"risks-method-h\">{WebUtility.HtmlEncode(t)}</h2>");
            }
            else
            {
                sb.Append($"<p class=\"risks-order-preamble\">{WebUtility.HtmlEncode(t)}</p>");
            }
        }

        return sb.ToString();
    }

    /// <summary>Пункты «5.1.» не должны быть h2; целиком жирный пункт — обычный текст.</summary>
    public static string NormalizeRegulationFormatting(string? html)
    {
        var text = html ?? string.Empty;
        if (text.Length == 0)
        {
            return text;
        }

        // <h2>5.1. …</h2> → обычный абзац
        text = Regex.Replace(
            text,
            @"<h2(\s[^>]*)?>\s*(?<body>\d+\.\d+[\s\S]*?)</h2>",
            "<p class=\"risks-order-preamble\">${body}</p>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // <p/li…><strong>5.1. весь абзац</strong></p> → без strong
        text = Regex.Replace(
            text,
            @"<(?<tag>p|li)(?<attrs>[^>]*)>\s*<strong>(?<body>\d+\.\d+[\s\S]*?)</strong>\s*</\k<tag>>",
            "<${tag}${attrs}>${body}</${tag}>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Фрагментированные <strong> внутри заголовков разделов → один strong
        text = Regex.Replace(
            text,
            @"<h2(?<attrs>\s[^>]*risks-method-h[^>]*)>(?<body>[\s\S]*?)</h2>",
            m =>
            {
                var body = Regex.Replace(m.Groups["body"].Value, @"</?strong>", string.Empty,
                    RegexOptions.IgnoreCase);
                body = Regex.Replace(body, @"\s+", " ").Trim();
                return $"<h2{m.Groups["attrs"].Value}>{body}</h2>";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Абзацы «-Приказ…» / «– ГОСТ…» → пункты списка
        text = FoldDashParagraphsIntoLists(text);

        // Кривая матрица из Word → каноническая цветная 5×5
        // (Build / BuildClassification сами добавляют подпись «Таблица N»)
        text = Regex.Replace(
            text,
            @"<table\b[^>]*>[\s\S]*?</table>",
            m =>
            {
                var plain = StripTags(m.Value);
                if (RiskAssessmentMatrixHtml.LooksLikeMatrixTable(plain))
                {
                    return RiskAssessmentMatrixHtml.Build();
                }

                if (LooksLikeClassificationTable(plain))
                {
                    return RiskAssessmentMatrixHtml.BuildClassification();
                }

                return m.Value;
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Подписи из образца Word остаются перед канонической — схлопываем после замены таблиц
        text = CollapseDuplicateTableCaptions(text);

        // Хвостовая точка-мусор из образца
        text = Regex.Replace(
            text,
            @"<p class=""risks-order-preamble"">\s*\.\s*</p>\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return text;
    }

    /// <summary>
    /// После подмены таблиц каноническим HTML остаётся 2–3 подписи из образца Word.
    /// Оставляем одну каноническую «Таблица N» перед каждой таблицей.
    /// Важно: не использовать [\s\S]*? до &lt;/p&gt; с lookahead — regex «перепрыгивает»
    /// через &lt;table&gt; и вырезает матрицу целиком.
    /// </summary>
    private static string CollapseDuplicateTableCaptions(string text)
    {
        // Только короткие подряд идущие подписи (без вложенных тегов кроме b/strong)
        const string caption1 =
            """(?:<p\b[^>]*>\s*(?:<(?:strong|b)>)?\s*Таблица\s*1\.\s*[^<>]{0,100}(?:</(?:strong|b)>)?\s*</p>\s*)""";
        const string caption2 =
            """(?:<p\b[^>]*>\s*(?:<(?:strong|b)>)?\s*Таблица\s*2\.\s*[^<>]{0,120}(?:</(?:strong|b)>)?\s*</p>\s*)""";

        text = Regex.Replace(
            text,
            caption1 + "{2,}",
            """<p class="risks-method-table-caption"><b>Таблица 1. Матрица оценки рисков</b></p>""" + "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        text = Regex.Replace(
            text,
            caption2 + "{2,}",
            """<p class="risks-method-table-caption"><b>Таблица 2. Классификация рисков по значимости</b></p>""" + "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Обломки из Word: <p class="risks-order-preamble"><strong>Таблица N…</strong>…</p>
        // сразу перед канонической подписью (без захвата таблиц).
        text = Regex.Replace(
            text,
            """<p class="risks-order-preamble">\s*(?:<(?:strong|b)>[^<]{0,80}</(?:strong|b)>\s*){1,4}</p>\s*(?=<p class="risks-method-table-caption">)""",
            m =>
            {
                var plain = Regex.Replace(m.Value, "<[^>]+>", " ");
                return Regex.IsMatch(plain, @"Таблица\s*[12]\.", RegexOptions.IgnoreCase)
                    ? string.Empty
                    : m.Value;
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return text;
    }

    private static bool LooksLikeClassificationTable(string plain)
    {
        var t = (plain ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        var hasLevel = t.Contains("уровень риска", StringComparison.Ordinal);
        var hasLow = t.Contains("1-4", StringComparison.Ordinal) || t.Contains("1–4", StringComparison.Ordinal);
        var hasHigh = t.Contains("11-25", StringComparison.Ordinal) || t.Contains("11–25", StringComparison.Ordinal);
        return hasLevel && hasLow && hasHigh;
    }

    /// <summary>Склеивает подряд идущие абзацы с маркером «-» в &lt;ul class="risks-method-list"&gt;.</summary>
    private static string FoldDashParagraphsIntoLists(string html)
    {
        return Regex.Replace(
            html,
            @"(?:<p class=""risks-order-preamble"">\s*[-–—•*]\s*(?<item>[\s\S]*?)</p>\s*){2,}",
            m =>
            {
                var items = Regex.Matches(
                    m.Value,
                    @"<p class=""risks-order-preamble"">\s*[-–—•*]\s*(?<item>[\s\S]*?)</p>",
                    RegexOptions.IgnoreCase);
                var sb = new StringBuilder();
                sb.Append("""<ul class="risks-method-list">""");
                foreach (Match item in items)
                {
                    var body = item.Groups["item"].Value.Trim();
                    body = Regex.Replace(body, @"^[-–—•*]\s*", string.Empty);
                    sb.Append("<li>").Append(body).Append("</li>");
                }

                sb.Append("</ul>");
                return sb.ToString();
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string StripTags(string html) =>
        Regex.Replace(html ?? string.Empty, @"<[^>]+>", " ");

    /// <summary>Жёстко прописанное имя юрлица из Word → плейсхолдер для печати.</summary>
    public static string InjectOrganizationPlaceholder(string? html, string organization)
    {
        var text = html ?? string.Empty;
        var org = (organization ?? string.Empty).Trim();
        if (text.Length == 0 || org.Length < 2)
        {
            return text;
        }

        foreach (var candidate in BuildOrganizationLiteralVariants(org))
        {
            text = ReplaceLiteralIgnoreCase(text, candidate, "{{Организация}}");
            var encoded = WebUtility.HtmlEncode(candidate);
            if (!string.Equals(encoded, candidate, StringComparison.Ordinal))
            {
                text = ReplaceLiteralIgnoreCase(text, encoded, "{{Организация}}");
            }
        }

        return text;
    }

    /// <summary>
    /// Подменяет известные названия юрлиц и отдельные абзацы-названия на {{Организация}}.
    /// Нужно, когда в Word «вшито» другое ООО/ИП, а «Подставить» задаёт печатную организацию.
    /// </summary>
    public static string ReplaceHardcodedOrganizations(
        string? html,
        IEnumerable<string?> organizations)
    {
        var text = html ?? string.Empty;
        if (text.Length == 0)
        {
            return text;
        }

        var candidates = (organizations ?? Array.Empty<string?>())
            .Select(o => (o ?? string.Empty).Trim())
            .Where(o => o.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(o => o.Length)
            .ToList();

        foreach (var org in candidates)
        {
            text = InjectOrganizationPlaceholder(text, org);
        }

        return ReplaceStandaloneOrganizationParagraphs(text);
    }

    /// <summary>
    /// Абзац целиком — название ООО/ИП (типичная шапка положения) → {{Организация}}.
    /// </summary>
    public static string ReplaceStandaloneOrganizationParagraphs(string? html)
    {
        var text = html ?? string.Empty;
        if (text.Length == 0)
        {
            return text;
        }

        return Regex.Replace(
            text,
            @"<(?<tag>p|h[1-6]|div)(?<attrs>[^>]*)>(?<inner>.*?)</\k<tag>>",
            match =>
            {
                var inner = match.Groups["inner"].Value;
                if (inner.Contains("{{Организация}}", StringComparison.OrdinalIgnoreCase)
                    || inner.Contains("{{ Организация }}", StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                var plain = Collapse(StripTags(inner)).Trim().Trim('«', '»', '"', '\'', '“', '”');
                if (!LooksLikeStandaloneOrganizationLine(plain))
                {
                    return match.Value;
                }

                return $"<{match.Groups["tag"].Value}{match.Groups["attrs"].Value}>{{{{Организация}}}}</{match.Groups["tag"].Value}>";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    private static IEnumerable<string> BuildOrganizationLiteralVariants(string organization)
    {
        var org = organization.Trim();
        if (org.Length < 2)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length >= 2)
            {
                seen.Add(v);
            }
        }

        Add(org);
        Add(org.Trim('«', '»', '"', '\'', '“', '”'));

        var core = OrganizationMatcher.GetCoreName(org);
        if (core.Length >= 2 && !string.Equals(core, OrganizationMatcher.Normalize(org), StringComparison.OrdinalIgnoreCase))
        {
            // «ВИКТОРИЯ СТРОЙ» / ООО «ВИКТОРИЯ СТРОЙ» — как в Word
            Add(core);
            Add($"«{core}»");
            Add($"\"{core}\"");
            Add($"ООО «{core}»");
            Add($"ООО \"{core}\"");
            Add($"ООО {core}");
            Add($"ИП «{core}»");
            Add($"ИП \"{core}\"");
            Add($"ИП {core}");
        }

        foreach (var baseName in seen.ToList())
        {
            Add($"«{baseName}»");
            Add($"\"{baseName}\"");
            Add($"'{baseName}'");
            Add(baseName.Replace('«', '"').Replace('»', '"'));
            Add(baseName.Replace('«', '\"').Replace('»', '\"'));
            Add(baseName.Replace("«", "&laquo;", StringComparison.Ordinal)
                .Replace("»", "&raquo;", StringComparison.Ordinal));
            Add(baseName.Replace("«", "&#171;", StringComparison.Ordinal)
                .Replace("»", "&#187;", StringComparison.Ordinal));
        }

        foreach (var item in seen.OrderByDescending(x => x.Length))
        {
            yield return item;
        }
    }

    private static bool LooksLikeStandaloneOrganizationLine(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain) || plain.Length > 160)
        {
            return false;
        }

        if (OrganizationMatcher.LooksLikeOrganization(plain))
        {
            return true;
        }

        // «ВИКТОРИЯ СТРОЙ» без ООО — только если вся строка в кавычках/ёлочках.
        var t = plain.Trim();
        if ((t.StartsWith('«') && t.EndsWith('»'))
            || (t.StartsWith('"') && t.EndsWith('"'))
            || (t.StartsWith('\'') && t.EndsWith('\'')))
        {
            var inner = t.Trim('«', '»', '"', '\'').Trim();
            var words = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return inner.Length >= 3 && words.Length is >= 1 and <= 6;
        }

        return false;
    }

    private static string ReplaceLiteralIgnoreCase(string input, string find, string replace)
    {
        if (string.IsNullOrEmpty(find) || input.Length == 0)
        {
            return input;
        }

        return Regex.Replace(input, Regex.Escape(find), replace, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string DocxToHtml(Stream stream, string? organization = null)
    {
        using var ms = DocxPackageRepair.ToRepairedMemoryStream(stream);
        using var doc = WordprocessingDocument.Open(ms, false);
        var part = doc.MainDocumentPart;
        var body = part?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var org = (organization ?? string.Empty).Trim();
        var sb = new StringBuilder();
        var header = ExtractPrimaryHeader(part);
        if (header.Length > 0)
        {
            var headerHtml = !string.IsNullOrWhiteSpace(org)
                             && (string.Equals(header, org, StringComparison.OrdinalIgnoreCase)
                                 || header.Contains(org, StringComparison.OrdinalIgnoreCase)
                                 || org.Contains(header, StringComparison.OrdinalIgnoreCase))
                ? "{{Организация}}"
                : WebUtility.HtmlEncode(header);
            sb.Append($"<p class=\"risks-regulation-running-header\">{headerHtml}</p>");
        }

        var inList = false;
        foreach (var el in FlattenBody(body))
        {
            if (el is Paragraph para)
            {
                var plain = Collapse(para.InnerText);
                if (plain.Length == 0)
                {
                    if (inList)
                    {
                        sb.Append("</ul>");
                        inList = false;
                    }

                    continue;
                }

                if (IsListParagraph(para, plain))
                {
                    if (!inList)
                    {
                        sb.Append("""<ul class="risks-method-list">""");
                        inList = true;
                    }

                    sb.Append("<li>");
                    AppendInline(sb, para, stripListMarker: true);
                    sb.Append("</li>");
                    continue;
                }

                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                AppendParagraph(sb, para, plain, org);
            }
            else if (el is Table table)
            {
                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                AppendTable(sb, table);
            }
        }

        if (inList)
        {
            sb.Append("</ul>");
        }

        return sb.ToString();
    }

    private static IEnumerable<OpenXmlElement> FlattenBody(Body body)
    {
        foreach (var child in body.ChildElements)
        {
            foreach (var el in FlattenElement(child))
            {
                yield return el;
            }
        }
    }

    private static IEnumerable<OpenXmlElement> FlattenElement(OpenXmlElement el)
    {
        if (el is SdtBlock sdt)
        {
            var content = sdt.SdtContentBlock;
            if (content is null)
            {
                yield break;
            }

            foreach (var nested in content.ChildElements)
            {
                foreach (var x in FlattenElement(nested))
                {
                    yield return x;
                }
            }

            yield break;
        }

        yield return el;
    }

    private static string ExtractPrimaryHeader(MainDocumentPart? part)
    {
        if (part is null)
        {
            return string.Empty;
        }

        foreach (var headerPart in part.HeaderParts)
        {
            var header = headerPart.Header;
            if (header is null)
            {
                continue;
            }

            foreach (var para in header.Descendants<Paragraph>())
            {
                var text = Collapse(para.InnerText);
                if (text.Length > 0)
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static void AppendParagraph(StringBuilder sb, Paragraph para, string plain, string? organization = null)
    {
        var style = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        var align = para.ParagraphProperties?.Justification?.Val?.Value;
        var centered = align == JustificationValues.Center;
        var isHeadingStyle = style.Contains("Heading", StringComparison.OrdinalIgnoreCase)
                             || style.Contains("Заголовок", StringComparison.OrdinalIgnoreCase);
        // Только разделы вида «5. Название», не пункты «5.1. …» / «5.2.1. …»
        // (иначе длинные абзацы методики становятся жирными h2).
        var isNumberedHeading = IsTopLevelNumberedHeading(plain);
        var isTitle = LooksLikeTitle(plain);
        var org = (organization ?? string.Empty).Trim();
        var plainTrim = plain.Trim();
        var isOrgLine = (org.Length > 0
                         && (string.Equals(plainTrim, org, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(plainTrim.Trim('«', '»', '"', '\''), org, StringComparison.OrdinalIgnoreCase)))
                        || LooksLikeStandaloneOrganizationLine(plainTrim);

        if (isOrgLine)
        {
            var cls = centered ? "risks-wp-org-center" : "risks-order-preamble";
            sb.Append($"<p class=\"{cls}\">{{{{Организация}}}}</p>");
            return;
        }

        if (isTitle && !isNumberedHeading)
        {
            sb.Append("""<p class="risks-method-title-import">""");
            AppendInline(sb, para, stripListMarker: false);
            sb.Append("</p>");
            return;
        }

        if (centered && !isNumberedHeading && !IsClauseParagraph(plain) && plain.Length < 180)
        {
            var cls = IsBoldParagraph(para) ? "risks-method-subtitle-import" : "risks-wp-org-center";
            sb.Append($"<p class=\"{cls}\">");
            AppendInline(sb, para, stripListMarker: false);
            sb.Append("</p>");
            return;
        }

        if ((isHeadingStyle && !IsClauseParagraph(plain) && plain.Length <= 160) || isNumberedHeading)
        {
            sb.Append("""<h2 class="risks-method-h">""");
            AppendInline(sb, para, stripListMarker: false);
            sb.Append("</h2>");
            return;
        }

        sb.Append("""<p class="risks-order-preamble">""");
        // В Word пункты «5.1.» часто целиком помечены Bold — для положения это обычный текст.
        var suppressWholeBold = IsClauseParagraph(plain) && IsBoldParagraph(para);
        AppendInline(sb, para, stripListMarker: false, suppressBold: suppressWholeBold);
        sb.Append("</p>");
    }

    /// <summary>Раздел «5. …», но не пункт «5.1. …».</summary>
    private static bool IsTopLevelNumberedHeading(string plain)
    {
        var t = (plain ?? string.Empty).TrimStart();
        if (!Regex.IsMatch(t, @"^\d+\.\s+\S"))
        {
            return false;
        }

        // «5.1.» / «5.2.3.» — обычные пункты положения, не заголовки.
        if (Regex.IsMatch(t, @"^\d+\.\d+"))
        {
            return false;
        }

        // Длинные абзацы с номером раздела без подпункта всё же оставляем текстом.
        return t.Length <= 160;
    }

    private static bool IsClauseParagraph(string plain) =>
        Regex.IsMatch((plain ?? string.Empty).TrimStart(), @"^\d+(\.\d+)+\.?\s+\S");

    private static bool LooksLikeTitle(string plain)
    {
        var t = plain.Trim();
        if (t.Length == 0 || t.Length > 120)
        {
            return false;
        }

        if (t.Contains("ПОЛОЖЕНИЕ", StringComparison.OrdinalIgnoreCase)
            || t.Contains("МЕТОДИКА", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var letters = t.Where(char.IsLetter).ToArray();
        if (letters.Length < 4)
        {
            return false;
        }

        var upper = letters.Count(char.IsUpper);
        return upper >= letters.Length * 0.85;
    }

    private static bool IsListParagraph(Paragraph para, string plain)
    {
        // Нумерованные разделы/пункты («5. …», «5.1. …») — не маркеры списка.
        if (LooksLikeTitle(plain)
            || IsTopLevelNumberedHeading(plain)
            || IsClauseParagraph(plain)
            || Regex.IsMatch(plain.TrimStart(), @"^\d+\.\s+\S"))
        {
            return false;
        }

        var style = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        if (style.Contains("Heading", StringComparison.OrdinalIgnoreCase)
            || style.Contains("Заголовок", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var num = para.ParagraphProperties?.NumberingProperties;
        if (num?.NumberingId?.Val is not null)
        {
            // Нумерованные заголовки вида «1.» часто тоже с numbering — отсекаем выше.
            return true;
        }

        var t = plain.TrimStart();
        return Regex.IsMatch(t, @"^[-–—•*]\s*\S");
    }

    private static bool IsBoldParagraph(Paragraph para)
    {
        var runs = para.Elements<Run>().Where(r => !string.IsNullOrWhiteSpace(r.InnerText)).ToList();
        if (runs.Count == 0)
        {
            return false;
        }

        return runs.All(IsBoldRun);
    }

    private static bool IsBoldRun(Run run)
    {
        var b = run.RunProperties?.Bold;
        if (b is null)
        {
            return false;
        }

        return b.Val is null || b.Val.Value;
    }

    private static void AppendInline(StringBuilder sb, Paragraph para, bool stripListMarker, bool suppressBold = false)
    {
        var firstText = true;
        foreach (var child in para.ChildElements)
        {
            if (child is Run run)
            {
                AppendRun(sb, run, ref firstText, stripListMarker, suppressBold);
            }
            else if (child is Hyperlink link)
            {
                foreach (var linkRun in link.Elements<Run>())
                {
                    AppendRun(sb, linkRun, ref firstText, stripListMarker, suppressBold);
                }
            }
        }
    }

    private static void AppendRun(StringBuilder sb, Run run, ref bool firstText, bool stripListMarker, bool suppressBold = false)
    {
        var bold = !suppressBold && IsBoldRun(run);
        var italic = run.RunProperties?.Italic is { } it && (it.Val is null || it.Val.Value);

        foreach (var el in run.Elements())
        {
            if (el is Text text)
            {
                var value = text.Text ?? string.Empty;
                if (stripListMarker && firstText)
                {
                    value = Regex.Replace(value, @"^\s*[-–—•*]\s*", string.Empty);
                    firstText = false;
                }
                else if (value.Length > 0)
                {
                    firstText = false;
                }

                if (value.Length == 0)
                {
                    continue;
                }

                var enc = WebUtility.HtmlEncode(value);
                if (bold && italic)
                {
                    sb.Append($"<strong><em>{enc}</em></strong>");
                }
                else if (bold)
                {
                    sb.Append($"<strong>{enc}</strong>");
                }
                else if (italic)
                {
                    sb.Append($"<em>{enc}</em>");
                }
                else
                {
                    sb.Append(enc);
                }
            }
            else if (el is Break)
            {
                sb.Append("<br/>");
            }
            else if (el is TabChar)
            {
                sb.Append("&emsp;");
            }
        }
    }

    private static void AppendTable(StringBuilder sb, Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var cells = rows.SelectMany(r => r.Elements<TableCell>()).ToList();
        if (cells.Count == 0 || cells.All(c => string.IsNullOrWhiteSpace(c.InnerText)))
        {
            // Пустая «рамка» из Word (часто под заголовком) — не выводим.
            return;
        }

        // Однострочная декоративная линия без смысла — пропускаем.
        if (rows.Count == 1 && Collapse(rows[0].InnerText).Length == 0)
        {
            return;
        }

        var plain = Collapse(string.Join(' ', rows.Select(r => r.InnerText ?? string.Empty)));
        if (RiskAssessmentMatrixHtml.LooksLikeMatrixTable(plain))
        {
            // Вместо кривой выгрузки из Word — каноническая цветная матрица 5×5.
            sb.Append(RiskAssessmentMatrixHtml.Build());
            return;
        }

        sb.Append("""<table class="risks-wp-table risks-method-doc-table"><tbody>""");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row.Elements<TableCell>())
            {
                var fill = cell.TableCellProperties?.Shading?.Fill?.Value;
                var style = string.Empty;
                if (!string.IsNullOrWhiteSpace(fill)
                    && !string.Equals(fill, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    style = $" style=\"background-color:#{fill.Trim().TrimStart('#')}\"";
                }

                var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                var colspan = gridSpan > 1 ? $" colspan=\"{gridSpan}\"" : string.Empty;
                sb.Append($"<td{colspan}{style}>");
                var paras = cell.Elements<Paragraph>().ToList();
                if (paras.Count == 0)
                {
                    sb.Append(WebUtility.HtmlEncode(Collapse(cell.InnerText)));
                }
                else
                {
                    for (var i = 0; i < paras.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append("<br/>");
                        }

                        AppendInline(sb, paras[i], stripListMarker: false);
                    }
                }

                sb.Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Replace('\u00a0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
