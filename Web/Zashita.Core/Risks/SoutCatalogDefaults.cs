namespace Zashita.Core.Risks;

public static class SoutCatalogDefaults
{
    public static List<SoutCatalogRecord> CreateEquipment(string organization)
    {
        var org = organization.Trim();
        var rows = new List<SoutCatalogRecord>();

        string AddFolder(string name, string? parentId = null)
        {
            var id = Guid.NewGuid().ToString("N");
            rows.Add(new SoutCatalogRecord
            {
                Id = id,
                Organization = org,
                ParentId = parentId,
                IsGroup = true,
                Name = name
            });
            return id;
        }

        void AddItem(string name, string? parentId, string type = "", string inventory = "")
        {
            rows.Add(new SoutCatalogRecord
            {
                Organization = org,
                ParentId = parentId,
                Name = name,
                TypeName = type,
                InventoryNumber = inventory
            });
        }

        var folders = new[]
        {
            "RA.RU.21ЗН54",
            "Архитектура",
            "Компьютеры",
            "Компьютеры Дельта Б",
            "Оборудование ЭТЛ",
            "ОКПБ",
            "Офисная техника",
            "Производственное оборудование",
            "Ручной инструмент",
            "ССЕ",
            "Транспорт",
            "Химическое оборудование"
        };

        foreach (var folder in folders)
        {
            AddFolder(folder);
        }

        var chemId = rows.First(item => item.IsGroup && item.Name == "Химическое оборудование").Id;
        AddItem("01 мм", chemId);
        AddItem("3Ф640", chemId);
        AddItem("GPS Персональный компьютер", chemId);
        AddItem("Автомобиль Renault Logan, 2011 г.в.", chemId, "Легковой автомобиль");
        AddItem("Автотопливозаправщик АТЗ-5,5-5320", chemId);

        return rows;
    }

    public static List<SoutCatalogRecord> CreateMaterials(string organization)
    {
        var org = organization.Trim();
        var rows = new List<SoutCatalogRecord>();

        string AddFolder(string name)
        {
            var id = Guid.NewGuid().ToString("N");
            rows.Add(new SoutCatalogRecord
            {
                Id = id,
                Organization = org,
                IsGroup = true,
                Name = name
            });
            return id;
        }

        void AddItem(string name, string? parentId = null)
        {
            rows.Add(new SoutCatalogRecord
            {
                Organization = org,
                ParentId = parentId,
                Name = name
            });
        }

        var sseId = AddFolder("ССЕ");
        var chemId = AddFolder("Химические вещества");

        AddItem("арматура", sseId);
        AddItem("балка", sseId);
        AddItem("бетон", sseId);
        AddItem("брусья", sseId);
        AddItem("бумага", chemId);
        AddItem("вент-оборудование и комплектующие", chemId);
        AddItem("ветошь", chemId);
        AddItem("воздуховоды из оцинкованной стали", chemId);
        AddItem("выключатели", chemId);
        AddItem("гвозди", chemId);
        AddItem("ГСМ", chemId);
        AddItem("деревянные заготовки", chemId);
        AddItem("дизельное топливо", chemId);

        return rows;
    }
}
