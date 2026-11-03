namespace Zashita.Core.Risks;

public static class SoutCommissionDefaults
{
    private const string StandingFolderName = "Постоянно действующие комиссии";

    public static List<SoutCommissionRecord> Create(string organization)
    {
        var org = organization.Trim();
        var rows = new List<SoutCommissionRecord>();

        string AddFolder(string name)
        {
            var id = Guid.NewGuid().ToString("N");
            rows.Add(new SoutCommissionRecord
            {
                Id = id,
                Organization = org,
                IsGroup = true,
                Name = name
            });
            return id;
        }

        void AddItem(string name, string parentId, string composition, DateTime orderDate, string orderNumber, bool inactive = false)
        {
            rows.Add(new SoutCommissionRecord
            {
                Organization = org,
                ParentId = parentId,
                Name = name,
                Composition = composition,
                OrderDate = orderDate,
                OrderNumber = orderNumber,
                IsInactive = inactive
            });
        }

        AddFolder("Комиссии по расследованию профзаболеваний");
        var standingId = AddFolder(StandingFolderName);

        AddItem(
            "Комиссия по проведению специальной оценки условий труда",
            standingId,
            "Калюбаева П.Н., Гаврюхин В.И., Кириченко А.В., Смирнова Е.П.",
            new DateTime(2021, 12, 31),
            "182");
        AddItem(
            "Комиссия по локализации и ликвидации последствий аварий",
            standingId,
            "Иванов И.И., Петров П.П., Сидоров С.С.",
            new DateTime(2022, 6, 15),
            "97");
        AddItem(
            "Комиссия по охране труда",
            standingId,
            "Козлов К.К., Новикова Н.Н.",
            new DateTime(2023, 1, 10),
            "12");

        return rows;
    }
}
