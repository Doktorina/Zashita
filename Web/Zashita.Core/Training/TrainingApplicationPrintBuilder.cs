using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Training;

public static class TrainingApplicationPrintBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Build(TrainingPlanningRecord record) =>
        Build(record, TrainingApplicationPrintModes.ApplicationV2);

    public static string Build(TrainingPlanningRecord record, string? mode)
    {
        var normalized = TrainingApplicationPrintModes.Normalize(mode);
        var participants = record.Participants.Count > 0
            ? record.Participants
            : [new TrainingPlanningParticipant
            {
                ProgramName = record.Content,
                TrainingDirection = FirstNonEmpty(record.TrainingKind, record.Section)
            }];

        return normalized switch
        {
            TrainingApplicationPrintModes.ApplicationV3 => BuildVariant3(record, participants),
            TrainingApplicationPrintModes.ApplicationV4 => BuildVariant4(record, participants),
            TrainingApplicationPrintModes.IndustrialSafety => BuildIndustrialSafety(record, participants),
            _ => BuildVariant2(record, participants)
        };
    }

    public static string BuildDocument(TrainingPlanningRecord record) =>
        Build(record, TrainingApplicationPrintModes.ApplicationV2);

    private static string BuildVariant2(
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants)
    {
        var program = FirstNonEmpty(
            FirstProgramName(participants, string.Empty),
            record.TrainingKind,
            record.Content);
        var direction = FirstNonEmpty(
            FirstDirection(participants, string.Empty),
            program,
            record.TrainingKind);

        var recipientOrg = FirstNonEmpty(
            record.RecipientOrganization,
            "Исполнительному директору НЧОУ УЭЦ «Строитель»");
        var recipientName = FirstNonEmpty(record.RecipientName, "А.А. Улановскому");
        var orgName = FirstNonEmpty(record.Organization, "________________");
        var orgTitle = Employees.OrganizationRequisites.FormatApplicationOrgTitle(orgName);
        var detailsBody = ResolveLetterheadBody(record);
        var appNumber = FormatApplicationNumber(O(record, "number", record.Number));
        var agreedLine = O(record, "signAgreed", FormatAgreementLine(record));
        var objectNote = O(record, "objectNote", (record.ObjectNote ?? string.Empty).Trim());
        var hasObject = !string.IsNullOrWhiteSpace(objectNote);
        // С объектом — как в образце: «С уважением: ООО «…»»; иначе строка директора.
        var directorLine = O(
            record,
            "signDirector",
            hasObject
                ? orgName
                : $"Директор {orgName} __________ ________________");

        var sb = new StringBuilder();
        sb.Append(hasObject
            ? """<div class="training-app-doc training-app-v2 training-app-has-object">"""
            : """<div class="training-app-doc training-app-v2">""");

        // Объект — только если заполнен (как на образце с «Объект: …» справа сверху).
        if (hasObject)
        {
            sb.Append("""<div class="training-app-object" contenteditable="true" data-override-key="objectNote">Объект: """)
                .Append(E(objectNote))
                .Append("</div>");
        }

        // Шапка организации (как в образце)
        sb.Append("""<div class="training-app-letterhead">""");
        sb.Append("""<div class="training-app-letterhead-rule"></div>""");
        sb.Append("""<div class="training-app-letterhead-org" contenteditable="true" data-override-key="orgName">""")
            .Append(E(O(record, "orgName", orgTitle)))
            .Append("</div>");
        sb.Append("""<div class="training-app-letterhead-rule"></div>""");
        sb.Append("""<div class="training-app-letterhead-details" contenteditable="true" data-override-key="orgDetails">""")
            .Append(Nl(O(record, "orgDetails", detailsBody)))
            .Append("</div>");
        sb.Append("""<div class="training-app-letterhead-rule training-app-letterhead-rule-double"></div>""");
        sb.Append("</div>");

        // Таблица вместо CSS grid — Word сохраняет раскладку дата | адресат.
        sb.Append("""<table class="training-app-top" border="0" cellspacing="0" cellpadding="0" width="100%"><tr>""");
        sb.Append("""<td class="training-app-date" valign="top" width="50%" contenteditable="true" data-override-key="docDate">от """)
            .Append(E(O(record, "docDate", FormatLongDate(record.DocumentDate))))
            .Append("</td>");
        sb.Append("""<td class="training-app-to" valign="top" width="50%" align="right">""");
        sb.Append("""<div contenteditable="true" data-override-key="recipientOrg">""")
            .Append(E(O(record, "recipientOrg", recipientOrg)))
            .Append("</div>");
        sb.Append("""<div contenteditable="true" data-override-key="recipientName">""")
            .Append(E(O(record, "recipientName", recipientName)))
            .Append("</div></td></tr></table>");

        sb.Append("""<div class="training-app-title">ЗАЯВКА <span contenteditable="true" data-override-key="number">""")
            .Append(E(appNumber))
            .Append("</span></div>");
        sb.Append("""<div class="training-app-subtitle">на обучение сотрудников организации</div>""");

        sb.Append("""<table class="training-app-section-program" border="0" cellspacing="0" cellpadding="0" width="100%"><tr>""");
        sb.Append("""<td class="training-app-label-inline" valign="bottom" style="width:1%;white-space:nowrap;padding-right:10px">1. По программе</td>""");
        sb.Append("""<td class="training-app-program" valign="bottom" contenteditable="true" data-override-key="program">""")
            .Append(E(O(record, "program", program)))
            .Append("</td></tr></table>");
        if (hasObject)
        {
            sb.Append("""<div class="training-app-hint">(вид обучения)</div>""");
        }

        sb.Append("""<div class="training-app-section"><div class="training-app-label">2.""");
        if (hasObject)
        {
            sb.Append(" Контактное лицо:");
        }

        sb.Append("</div>");
        sb.Append("""<table class="training-app-contact"><tbody>""");
        AppendContactRow(sb, record, "contactName", "ФИО", record.ContactPersonName);
        AppendContactRow(sb, record, "contactPhone", "Телефон", record.ContactPersonPhone);
        AppendContactRow(sb, record, "contactEmail", "E-mail", record.ContactPersonEmail);
        AppendContactRow(sb, record, "contactPosition", "Должность", record.ContactPersonPosition);
        sb.Append("</tbody></table></div>");

        sb.Append("""<div class="training-app-section"><div class="training-app-label">3. Участники обучения:</div>""");
        AppendApplicationParticipantsTable(sb, record, participants, direction);
        sb.Append("</div>");

        if (hasObject)
        {
            // С объектом: «С уважением» слева, «СОГЛАСОВАНО» справа — как на образце.
            sb.Append("""
                <table class="training-app-foot training-app-foot-split" border="0" cellspacing="0" cellpadding="0" width="100%"><tr>
                  <td class="training-app-foot-left" valign="top" width="58%">
                    <div>С уважением:</div>
                    <div class="training-app-sign-row" contenteditable="true" data-override-key="signDirector">
                """);
            sb.Append(E(directorLine));
            sb.Append("""
                    </div>
                  </td>
                  <td class="training-app-foot-right" valign="bottom" width="42%" align="right">
                    <div class="training-app-sign-row" contenteditable="true" data-override-key="signAgreed">
                """);
            sb.Append(Nl(agreedLine));
            sb.Append("""
                    </div>
                  </td>
                </tr></table>
                """);
        }
        else
        {
            // Без объекта: подписи столбиком — как на первом образце.
            sb.Append("""
                <div class="training-app-foot">
                  <div>С уважением:</div>
                  <div class="training-app-sign-row" contenteditable="true" data-override-key="signDirector">
                """);
            sb.Append(E(directorLine));
            sb.Append("""
                  </div>
                  <div class="training-app-sign-row" contenteditable="true" data-override-key="signAgreed">
                """);
            sb.Append(Nl(agreedLine));
            sb.Append("""
                  </div>
                </div>
                """);
        }

        sb.Append("""
            <p class="training-app-edit-hint ppe-no-print">Правка: клик по тексту или ячейке. Затем «Записать».</p>
            </div>
            """);
        return sb.ToString();
    }

    /// <summary>Текст блока «СОГЛАСОВАНО» из списка согласования в форме заявки.</summary>
    public static string FormatAgreementLine(TrainingPlanningRecord record)
    {
        var people = (record.AgreementPersons ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.FullName) || !string.IsNullOrWhiteSpace(p.Position))
            .ToList();
        if (people.Count == 0)
        {
            return "СОГЛАСОВАНО __________ ________________";
        }

        // Как в образцах: «СОГЛАСОВАНО __________ А.Н. Юзеев»
        var lines = new List<string>();
        foreach (var person in people)
        {
            var shortName = FormatPersonInitials(person.FullName);
            if (string.IsNullOrWhiteSpace(shortName))
            {
                shortName = "________________";
            }

            lines.Add($"СОГЛАСОВАНО __________ {shortName}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Как в образцах печати: «А.Н. Юзеев» (инициалы + фамилия).</summary>
    private static string FormatPersonInitials(string fullName)
    {
        var parts = (fullName ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        var surname = parts[0];
        var initials = new StringBuilder();
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            initials.Append(char.ToUpperInvariant(part[0])).Append('.');
        }

        return initials.Length == 0 ? surname : $"{initials} {surname}";
    }

    private static void AppendApplicationParticipantsTable(
        StringBuilder sb,
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants,
        string fallbackDirection)
    {
        sb.Append("""
            <table class="training-app-table training-app-table-v2" border="1" cellspacing="0" cellpadding="3" width="100%">
              <thead>
                <tr>
                  <th style="width:5%">№ п/п</th>
                  <th style="width:18%">ФИО</th>
                  <th style="width:20%">Должность</th>
                  <th style="width:11%">Дата рождения</th>
                  <th style="width:12%">Дата трудоустройства</th>
                  <th style="width:12%">СНИЛС</th>
                  <th style="width:22%">Направление обучения</th>
                </tr>
              </thead>
              <tbody>
            """);

        var rows = participants
            .Where(p =>
                !string.IsNullOrWhiteSpace(p.FullName) ||
                !string.IsNullOrWhiteSpace(p.Position) ||
                !string.IsNullOrWhiteSpace(p.Snils) ||
                p.BirthDate.HasValue ||
                p.EmploymentDate.HasValue ||
                !string.IsNullOrWhiteSpace(p.ProgramName) ||
                !string.IsNullOrWhiteSpace(p.TrainingDirection))
            .ToList();
        var count = Math.Max(rows.Count, 1);
        for (var i = 0; i < count; i++)
        {
            var p = i < rows.Count ? rows[i] : new TrainingPlanningParticipant();
            var n = i + 1;
            var direction = FirstNonEmpty(p.TrainingDirection, p.ProgramName, fallbackDirection);
            sb.Append("<tr>");
            sb.Append("""<td class="c">""").Append(n).Append("</td>");
            sb.Append(Cell(record, $"r{n}-fio", p.FullName));
            sb.Append(Cell(record, $"r{n}-pos", p.Position));
            sb.Append(Cell(record, $"r{n}-birth", FormatDate(p.BirthDate), "c"));
            sb.Append(Cell(record, $"r{n}-hire", FormatDate(p.EmploymentDate), "c"));
            sb.Append(Cell(record, $"r{n}-snils", p.Snils, "c"));
            sb.Append(Cell(record, $"r{n}-dir", direction, "c"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static string ResolveLetterheadBody(TrainingPlanningRecord record)
    {
        var details = (record.OrganizationDetails ?? string.Empty).Trim();
        if (details.Length == 0
            || details.Contains("реквизиты организации", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Если в реквизитах целиком продублировано название организации — убираем первую строку.
        var org = (record.Organization ?? string.Empty).Trim();
        if (org.Length > 0
            && details.StartsWith(org, StringComparison.OrdinalIgnoreCase))
        {
            var rest = details[org.Length..].TrimStart(',', ' ', '\n', '\r');
            return rest;
        }

        return details;
    }

    private static string FormatApplicationNumber(string? number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return "____";
        }

        if (raw.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..].Trim();
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < 100)
        {
            return n.ToString("00", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static string Nl(string? value) =>
        E(value).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br/>", StringComparison.Ordinal);

    private static string BuildVariant3(
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants)
    {
        var program = FirstProgramName(participants, record.Content);
        var sb = new StringBuilder();
        sb.Append("""<div class="training-app-doc training-app-v3">""");
        AppendOrgHeader(sb, record, "Заявка на обучение");
        sb.Append("""<p class="training-app-variant-note">Вариант 3</p>""");
        sb.Append("""<p>Направляем работников для прохождения обучения по программе <span contenteditable="true" data-override-key="program">""")
            .Append(E(O(record, "program", program)))
            .Append("</span>.</p>");
        AppendDocumentMeta(sb, record);
        AppendParticipantsGrid(sb, record, participants, FirstDirection(participants, record.Section), compact: true);
        AppendContactSignature(sb, record);
        sb.Append("""<p class="training-app-edit-hint ppe-no-print">Правка: клик по тексту или ячейке. Затем «Записать».</p></div>""");
        return sb.ToString();
    }

    private static string BuildVariant4(
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants)
    {
        var sb = new StringBuilder();
        sb.Append("""<div class="training-app-doc training-app-v4">""");
        AppendOrgHeader(sb, record, "Заявка на обучение");
        sb.Append("""<p class="training-app-variant-note">Вариант 4</p>""");

        var list = participants.Count > 0 ? participants : [new TrainingPlanningParticipant()];
        for (var i = 0; i < list.Count; i++)
        {
            var p = list[i];
            var n = i + 1;
            sb.Append("""<h2 class="training-app-worker-title">Работник</h2><table class="training-app-meta"><tbody>""");
            AppendMetaCell(sb, record, $"p{n}-fio", "Ф.И.О.", p.FullName);
            AppendMetaCell(sb, record, $"p{n}-pos", "Должность", p.Position);
            AppendMetaCell(sb, record, $"p{n}-birth", "Дата рождения", FormatDate(p.BirthDate));
            AppendMetaCell(sb, record, $"p{n}-hire", "Дата трудоустройства", FormatDate(p.EmploymentDate));
            AppendMetaCell(sb, record, $"p{n}-snils", "СНИЛС", p.Snils);
            AppendMetaCell(sb, record, $"p{n}-program", "Наименование программы", p.ProgramName);
            AppendMetaCell(sb, record, $"p{n}-dir", "Направление обучения", p.TrainingDirection);
            sb.Append("</tbody></table>");
        }

        AppendDocumentMeta(sb, record, includeProgram: false);
        AppendContactSignature(sb, record);
        sb.Append("""<p class="training-app-edit-hint ppe-no-print">Правка: клик по тексту или ячейке. Затем «Записать».</p></div>""");
        return sb.ToString();
    }

    private static string BuildIndustrialSafety(
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants)
    {
        var sb = new StringBuilder();
        sb.Append("""<div class="training-app-doc training-app-industrial">""");
        AppendOrgHeader(sb, record, "Заявление об аттестации по вопросам промышленной безопасности");
        sb.Append("""<p>Прошу допустить к аттестации по вопросам промышленной безопасности следующих работников организации <span contenteditable="true" data-override-key="orgName">""")
            .Append(E(O(record, "orgName", record.Organization)))
            .Append("</span>:</p>");
        AppendDocumentMeta(sb, record);
        AppendParticipantsGrid(sb, record, participants, FirstDirection(participants, record.Section), compact: false);
        sb.Append("""
            <p>Основание: требования Федерального закона от 21.07.1997 № 116-ФЗ
            «О промышленной безопасности опасных производственных объектов».</p>
            """);
        AppendContactSignature(sb, record);
        sb.Append("""<p class="training-app-edit-hint ppe-no-print">Правка: клик по тексту или ячейке. Затем «Записать».</p></div>""");
        return sb.ToString();
    }

    private static void AppendOrgHeader(StringBuilder sb, TrainingPlanningRecord record, string title)
    {
        var details = string.IsNullOrWhiteSpace(record.OrganizationDetails)
            ? record.Organization
            : record.OrganizationDetails;
        sb.Append("""<div class="training-app-org-block" contenteditable="true" data-override-key="orgDetails">""")
            .Append(E(O(record, "orgDetails", details)))
            .Append("</div>");
        sb.Append("""<h1 class="training-app-h1">""").Append(E(title)).Append("</h1>");
        sb.Append("""<p class="training-app-numline">№ <span contenteditable="true" data-override-key="number">""")
            .Append(E(O(record, "number", record.Number)))
            .Append("""</span> от <span contenteditable="true" data-override-key="docDateShort">""")
            .Append(E(O(record, "docDateShort", FormatDate(record.DocumentDate))))
            .Append("</span></p>");
    }

    private static void AppendDocumentMeta(
        StringBuilder sb,
        TrainingPlanningRecord record,
        bool includeProgram = true)
    {
        sb.Append("""<table class="training-app-meta"><tbody>""");
        AppendMetaCell(sb, record, "metaOrg", "Организация", record.Organization);
        AppendMetaCell(sb, record, "metaDept", "Подразделение", record.Department);
        AppendMetaCell(sb, record, "metaSection", "Раздел", record.Section);
        AppendMetaCell(sb, record, "metaKind", "Вид обучения, инструктажа, проверки знаний", record.TrainingKind);
        AppendMetaCell(sb, record, "metaContent", "Содержание", record.Content);
        AppendMetaCell(sb, record, "metaExec", "Дата проведения", FormatDate(record.ExecutionDate));
        if (includeProgram)
        {
            AppendMetaCell(sb, record, "metaProgram", "Наименование программы",
                FirstProgramName(record.Participants, record.Content));
            AppendMetaCell(sb, record, "metaDirection", "Направление обучения",
                FirstDirection(record.Participants, record.Section));
        }

        sb.Append("</tbody></table>");
    }

    private static void AppendParticipantsGrid(
        StringBuilder sb,
        TrainingPlanningRecord record,
        IReadOnlyList<TrainingPlanningParticipant> participants,
        string fallbackDirection,
        bool compact)
    {
        sb.Append("""
            <table class="training-app-table">
              <thead>
                <tr>
                  <th style="width:5%">№</th>
                  <th style="width:18%">Ф.И.О.</th>
                  <th style="width:14%">Должность</th>
            """);
        if (!compact)
        {
            sb.Append("""
                  <th style="width:11%">Дата рождения</th>
                  <th style="width:12%">Дата трудоустройства</th>
                  <th style="width:11%">СНИЛС</th>
                """);
        }

        sb.Append("""<th>Наименование программы</th><th>Направление обучения</th></tr></thead><tbody>""");

        var rows = participants
            .Where(p =>
                !string.IsNullOrWhiteSpace(p.FullName) ||
                !string.IsNullOrWhiteSpace(p.Position) ||
                !string.IsNullOrWhiteSpace(p.Snils) ||
                p.BirthDate.HasValue ||
                p.EmploymentDate.HasValue ||
                !string.IsNullOrWhiteSpace(p.ProgramName))
            .ToList();
        var count = Math.Max(rows.Count, 1);
        for (var i = 0; i < count; i++)
        {
            var p = i < rows.Count ? rows[i] : new TrainingPlanningParticipant();
            var n = i + 1;
            var direction = FirstNonEmpty(p.TrainingDirection, p.ProgramName, fallbackDirection);
            sb.Append("<tr>");
            sb.Append("""<td class="c">""").Append(n).Append("</td>");
            sb.Append(Cell(record, $"r{n}-fio", p.FullName));
            sb.Append(Cell(record, $"r{n}-pos", p.Position));
            if (!compact)
            {
                sb.Append(Cell(record, $"r{n}-birth", FormatDate(p.BirthDate), "c"));
                sb.Append(Cell(record, $"r{n}-hire", FormatDate(p.EmploymentDate), "c"));
                sb.Append(Cell(record, $"r{n}-snils", p.Snils, "c"));
            }

            sb.Append(Cell(record, $"r{n}-program", p.ProgramName));
            sb.Append(Cell(record, $"r{n}-dir", direction));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static void AppendContactSignature(StringBuilder sb, TrainingPlanningRecord record)
    {
        sb.Append("""
            <table class="training-app-signatures">
              <tr>
                <td>Контактное лицо:</td>
                <td contenteditable="true" data-override-key="contactName">
            """);
        sb.Append(E(O(record, "contactName", record.ContactPersonName)));
        sb.Append("""
                </td>
                <td><span class="training-app-sign-line" contenteditable="true" data-override-key="contactSign">&nbsp;</span></td>
              </tr>
              <tr>
                <td></td>
                <td class="training-app-sign-caption">(Ф.И.О.)</td>
                <td class="training-app-sign-caption" style="text-align:center">(подпись)</td>
              </tr>
            </table>
            """);
    }

    private static void AppendContactRow(
        StringBuilder sb,
        TrainingPlanningRecord record,
        string key,
        string label,
        string? value)
    {
        sb.Append("<tr><td class=\"k\">")
            .Append(E(label))
            .Append("</td><td class=\"v\" contenteditable=\"true\" data-override-key=\"")
            .Append(E(key))
            .Append("\">");
        var text = O(record, key, value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            sb.Append("""<span class="fill">&nbsp;</span>""");
        }
        else
        {
            sb.Append(E(text));
        }

        sb.Append("</td></tr>");
    }

    private static void AppendMetaCell(
        StringBuilder sb,
        TrainingPlanningRecord record,
        string key,
        string label,
        string? value)
    {
        sb.Append("""<tr><td class="label">""").Append(E(label)).Append("</td>");
        sb.Append(Cell(record, key, string.IsNullOrWhiteSpace(value) ? "—" : value!));
        sb.Append("</tr>");
    }

    private static string Cell(TrainingPlanningRecord record, string key, string value, string? extraClass = null)
    {
        var cls = string.IsNullOrWhiteSpace(extraClass) ? string.Empty : $" class=\"{extraClass}\"";
        return $"""<td{cls} contenteditable="true" data-override-key="{E(key)}">{E(O(record, key, value))}</td>""";
    }

    private static string O(TrainingPlanningRecord record, string key, string value)
    {
        record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return record.PrintOverrides.TryGetValue(key, out var overridden) ? overridden : value;
    }

    private static string FirstProgramName(IReadOnlyList<TrainingPlanningParticipant> participants, string fallback) =>
        participants.FirstOrDefault(participant => !string.IsNullOrWhiteSpace(participant.ProgramName))?.ProgramName
        ?? fallback;

    private static string FirstDirection(IReadOnlyList<TrainingPlanningParticipant> participants, string fallback) =>
        participants.FirstOrDefault(participant => !string.IsNullOrWhiteSpace(participant.TrainingDirection))?.TrainingDirection
        ?? fallback;

    private static string FormatDate(DateTime? value) =>
        value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatLongDate(DateTime value) =>
        $"« {value:dd} » {value.ToString("MMMM", Ru).ToLowerInvariant()} {value:yyyy} г.";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
