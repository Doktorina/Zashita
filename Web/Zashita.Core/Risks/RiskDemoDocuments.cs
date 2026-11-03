namespace Zashita.Core.Risks;

public static class RiskDemoDocuments
{
    public static List<RiskUnscheduledOrderDocument> CreateUnscheduledOrders(string organization) =>
    [
        new()
        {
            Organization = organization,
            Number = "0000-000001",
            Date = new DateTime(2017, 11, 9, 10, 0, 0),
            Department = "Цех сборки-сварки",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "0000-000002",
            Date = new DateTime(2017, 12, 15, 10, 0, 0),
            Department = "Цех малярных работ",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "0000-000003",
            Date = new DateTime(2018, 2, 20, 11, 0, 0),
            Department = "Цех сборки-сварки",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "0000-000004",
            Date = new DateTime(2019, 5, 14, 9, 30, 0),
            Department = "Аппарат управления",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000001",
            Date = new DateTime(2026, 5, 19, 14, 0, 0),
            Department = "Аппарат управления",
            IsPosted = false
        }
    ];

    public static List<RiskPreparationDocument> CreateRiskPreparation(string organization) =>
    [
        new()
        {
            Organization = organization,
            Number = "0000-000001",
            Date = new DateTime(2017, 11, 1, 12, 0, 0),
            StartDate = new DateTime(2017, 11, 1),
            EndDate = new DateTime(2017, 12, 31),
            Department = "Бухгалтерия",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "0000-000002",
            Date = new DateTime(2018, 3, 15, 10, 30, 0),
            StartDate = new DateTime(2018, 3, 1),
            EndDate = new DateTime(2018, 4, 30),
            Department = "Цех Рога",
            IsPosted = true
        },
        new()
        {
            Organization = organization,
            Number = "ОТ-00000001",
            Date = new DateTime(2019, 1, 10, 9, 0, 0),
            StartDate = new DateTime(2019, 1, 1),
            EndDate = new DateTime(2019, 2, 28),
            Department = "Администрация",
            IsPosted = false
        }
    ];
}
