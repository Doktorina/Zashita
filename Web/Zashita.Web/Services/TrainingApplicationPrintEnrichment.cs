using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>Автоподстановка реквизитов и подписей перед печатью/предпросмотром заявки.</summary>
public static class TrainingApplicationPrintEnrichment
{
    public static void Apply(
        TrainingPlanningRecord record,
        OrganizationRequisitesStore requisites,
        OrganizationEmployeesService employees)
    {
        var org = (record.Organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var body = requisites.Get(org).FormatApplicationLetterheadBody();
        if (!string.IsNullOrWhiteSpace(body))
        {
            // Всегда берём актуальные реквизиты из справочника организации.
            record.OrganizationDetails = body;

            // Пустой/плейсхолдерный override не должен перекрывать автоподстановку.
            if (record.PrintOverrides.TryGetValue("orgDetails", out var overridden)
                && (string.IsNullOrWhiteSpace(overridden)
                    || overridden.Contains("реквизиты организации", StringComparison.OrdinalIgnoreCase)))
            {
                record.PrintOverrides.Remove("orgDetails");
            }
        }

        var hasObject = !string.IsNullOrWhiteSpace(record.ObjectNote)
                        || (record.PrintOverrides.TryGetValue("objectNote", out var objectOverride)
                            && !string.IsNullOrWhiteSpace(objectOverride));

        if (!record.PrintOverrides.ContainsKey("signDirector"))
        {
            if (hasObject)
            {
                // Образец с объектом: «С уважением: ООО «…»»
                record.PrintOverrides["signDirector"] = org;
            }
            else
            {
                var director = RiskPrintChrome.ResolveDirectorSignInitials(org, employees.Load(org));
                var namePart = string.IsNullOrWhiteSpace(director) ? "________________" : director;
                record.PrintOverrides["signDirector"] = $"Директор {org} __________ {namePart}";
            }
        }

        if (record.AgreementPersons is { Count: > 0 }
            && record.AgreementPersons.Any(p =>
                !string.IsNullOrWhiteSpace(p.FullName) || !string.IsNullOrWhiteSpace(p.Position)))
        {
            record.PrintOverrides["signAgreed"] =
                TrainingApplicationPrintBuilder.FormatAgreementLine(record);
        }
        else if (!record.PrintOverrides.ContainsKey("signAgreed"))
        {
            record.PrintOverrides["signAgreed"] =
                TrainingApplicationPrintBuilder.FormatAgreementLine(record);
        }
    }

    /// <summary>Подтянуть реквизиты в форму заявки (скрытое поле / сохранение).</summary>
    public static void ApplyToForm(TrainingPlanningRecord record, OrganizationRequisitesStore requisites)
    {
        var org = (record.Organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        var body = requisites.Get(org).FormatApplicationLetterheadBody();
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var details = (record.OrganizationDetails ?? string.Empty).Trim();
        if (details.Length == 0
            || details.Contains("реквизиты организации", StringComparison.OrdinalIgnoreCase))
        {
            record.OrganizationDetails = body;
        }
    }
}
