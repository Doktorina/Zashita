namespace Zashita.Core.Risks;

public static class SoutDemoDocuments
{
    public static List<WorkplaceAssignmentDocument> CreateAssignments(string organization, IReadOnlyList<WorkplaceRecord> workplaces)
    {
        // Демо-сотрудники больше не создаются — состав только из штата 1С.
        _ = organization;
        _ = workplaces;
        return [];
    }

    public static List<SoutPreparationDocument> CreatePreparation(string organization) =>
    [
        new()
        {
            Organization = organization,
            Number = "ОТ-00000012",
            Date = new DateTime(2025, 5, 7, 14, 24, 0),
            StartDate = new DateTime(2025, 5, 7),
            EndDate = new DateTime(2025, 6, 30),
            Department = "Бригада ремонта Ижевск",
            ContractorOrganization = "ООО \"ЭКСПЕРТЭГИДА\"",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000016",
            Date = new DateTime(2024, 6, 26, 10, 0, 0),
            StartDate = new DateTime(2024, 6, 26),
            EndDate = new DateTime(2024, 7, 5),
            Department = "Производство",
            ContractorOrganization = "ООО \"Проф-Эксперт\"",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000001",
            Date = new DateTime(2024, 1, 15, 9, 30, 0),
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 2, 28),
            Department = "Бригада ремонта Ижевск",
            ContractorOrganization = "КЦОТ",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000003",
            Date = new DateTime(2025, 2, 3, 11, 15, 0),
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 2, 28),
            Department = "Технический отдел",
            ContractorOrganization = "ООО \"ЭКОСТАНДАРТ\"",
            IsPosted = false
        }
    ];

    public static List<SoutAnnualPlanDocument> CreateAnnualPlans(string organization) =>
    [
        new()
        {
            Organization = organization,
            Number = "ОТ-00000001",
            Date = new DateTime(2022, 2, 16),
            PeriodStart = new DateTime(2022, 1, 1),
            PeriodEnd = new DateTime(2022, 12, 31),
            PlanYear = 2022,
            PlanType = "Офис",
            Scenario = string.Empty,
            Department = string.Empty,
            Status = "Утверждён",
            IsPosted = true,
            GeneralRows = [new SoutAnnualPlanGeneralRow { Contractor = "ООО \"ЭКСПЕРТЭГИДА\"" }]
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000003",
            Date = new DateTime(2022, 9, 26),
            PeriodStart = new DateTime(2022, 1, 1),
            PeriodEnd = new DateTime(2022, 12, 31),
            PlanYear = 2022,
            PlanType = "Офис",
            Scenario = string.Empty,
            Department = string.Empty,
            Status = "Утверждён",
            IsPosted = true,
            GeneralRows = [new SoutAnnualPlanGeneralRow { Contractor = "КЦОТ" }]
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000006",
            Date = new DateTime(2023, 7, 26),
            PeriodStart = new DateTime(2023, 1, 1),
            PeriodEnd = new DateTime(2023, 12, 31),
            PlanYear = 2023,
            PlanType = "Офис",
            Scenario = "план на 2022",
            Department = "Дирекция информационных технологий",
            Status = "На утверждении",
            IsPosted = false,
            GeneralRows = [new SoutAnnualPlanGeneralRow { Contractor = "ООО \"ЭКОСТАНДАРТ\"" }]
        }
    ];
}
