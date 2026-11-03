namespace Zashita.Core.Risks;

public static class SoutWorkplaceDefaults
{
    public static List<WorkplaceRecord> Create(string organization)
    {
        var org = organization.Trim();
        var groups = new[]
        {
            "Администрация",
            "Бригада по обработке поверхностей и художественной росписи",
            "Бухгалтерия",
            "Диспетчерская",
            "Инженеры",
            "Лифтёры",
            "МТО",
            "Производство",
            "Разнорабочие",
            "Участки",
            "Участок №1"
        };

        var rows = new List<WorkplaceRecord>();
        foreach (var groupName in groups)
        {
            var groupId = Guid.NewGuid().ToString("N");
            rows.Add(new WorkplaceRecord
            {
                Id = groupId,
                Organization = org,
                IsGroup = true,
                Name = groupName,
                Department = groupName,
                IsUsed = true
            });
        }

        var adminId = rows[0].Id;
        rows.AddRange(
        [
            new()
            {
                Organization = org,
                ParentId = adminId,
                Name = "Директор",
                WorkplaceNumber = "000000001",
                MainWorkplaceNumber = "000000001",
                ConditionsClass = "2",
                Profession = "Директор",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Администрация",
                IsUsed = true
            },
            new()
            {
                Organization = org,
                ParentId = adminId,
                Name = "Секретарь",
                WorkplaceNumber = "000000002",
                MainWorkplaceNumber = "000000002",
                ConditionsClass = "2",
                Profession = "Секретарь",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Администрация",
                IsUsed = true
            }
        ]);

        var productionId = rows.First(r => r.Name == "Производство").Id;
        rows.Add(new WorkplaceRecord
        {
            Organization = org,
            ParentId = productionId,
            Name = "Маляр",
            WorkplaceNumber = "000000010",
            MainWorkplaceNumber = "000000010",
            ConditionsClass = "3.1",
            Profession = "Маляр",
            WorkerCount = 2,
            PlanYear = "2025",
            Department = "Производство",
            IsUsed = true
        });

        var repairId = Guid.NewGuid().ToString("N");
        rows.Add(new WorkplaceRecord
        {
            Id = repairId,
            Organization = org,
            IsGroup = true,
            Name = "Бригада ремонта Ижевск",
            Department = "Бригада ремонта Ижевск",
            IsUsed = true
        });

        rows.AddRange(
        [
            new()
            {
                Organization = org,
                ParentId = repairId,
                Name = "каменщик",
                WorkplaceNumber = "2",
                MainWorkplaceNumber = "2",
                ConditionsClass = "3.1",
                Profession = "каменщик",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Бригада ремонта Ижевск",
                IsUsed = true
            },
            new()
            {
                Organization = org,
                ParentId = repairId,
                Name = "маляр",
                WorkplaceNumber = "5",
                MainWorkplaceNumber = "5",
                ConditionsClass = "3.1",
                Profession = "маляр",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Бригада ремонта Ижевск",
                IsUsed = true
            },
            new()
            {
                Organization = org,
                ParentId = repairId,
                Name = "маляр 6А",
                WorkplaceNumber = "6А",
                MainWorkplaceNumber = "6А",
                ConditionsClass = "3.1",
                Profession = "маляр",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Бригада ремонта Ижевск",
                IsUsed = true
            },
            new()
            {
                Organization = org,
                ParentId = repairId,
                Name = "плотник",
                WorkplaceNumber = "1",
                MainWorkplaceNumber = "1",
                ConditionsClass = "3.1",
                Profession = "плотник",
                WorkerCount = 1,
                PlanYear = "2025",
                Department = "Бригада ремонта Ижевск",
                IsUsed = true
            }
        ]);

        return rows;
    }
}
