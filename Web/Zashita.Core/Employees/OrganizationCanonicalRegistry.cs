namespace Zashita.Core.Employees;

/// <summary>
/// Канонический список юрлиц (файл «организации.xlsx»).
/// Правило: разные записи списка — разные компании, никогда не склеивать.
/// Один человек может работать в нескольких ЮЛ: в каждом — отдельная карточка штата.
/// Обучение (кроме электробезопасности) по компаниям не смешивается;
/// аттестация ЭБ синхронизируется между ЮЛ отдельно
/// (<c>ElectricalKnowledgeCrossOrgSync</c>).
/// </summary>
public static class OrganizationCanonicalRegistry
{
    /// <summary>Официальные названия из организации.xlsx (не склеивать между собой).</summary>
    public static IReadOnlyList<string> CanonicalNames { get; } =
    [
        "ООО «БЕТОН ЭКСПЕРТ»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ВИКТОРИЯ - А»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ВИКТОРИЯ ИНВЕСТ»",
        "ООО «ВИКТОРИЯ ПРОЕКТ»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ВИКТОРИЯ СИТИ»",
        "ООО «Виктория Строй»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ВИКТОРИЯ ЭСТЕЙТ»",
        "ООО «Е-Строй»",
        "ООО «ИНВИКТУС»",
        "ООО «Компания Строй Каркас»",
        "ООО «КСК»",
        "ООО «Новые метры»",
        "ООО «Отдел Строй»",
        "ООО «Созвездие»",
        "ООО «СтандартСтрой»",
        "ООО «УМТСВ»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ФЕСТ-ГРУПП»",
        "ООО «СПЕЦИАЛИЗИРОВАННЫЙ ЗАСТРОЙЩИК «ВИКТОРИЯ СКК»",
        "ИП Алешкевич А.Д.",
        "ИП Васильев А.В.",
        "ИП Вахитов А.С.",
        "ИП Вахитов С.З.",
        "ИП Демченко Е.Г.",
        "ИП Дубровский М.В.",
        "ИП Евдокимов А.А.",
        "ИП Елеонский Д.Е.",
        "ИП Еремеев М.А.",
        "ИП Кирюшкин В.В.",
        "ИП Лайковская Е.Ю.",
        "ИП Лайковский Т. А.",
        "ИП Макаров Г.А.",
        "ИП Нигамаев Р.Ф.",
        "ИП Овсепян М.П.",
        "ИП Репин В.И.",
        "ИП Русанов К.В.",
        "ИП Сартаков А.А.",
        "ИП Черепонянц О.С.",
        "ИП Черепонянц С.А.",
        "ИП Шварц А.С.",
        "ИП Шварц Б.Э.",
        "ИП Шитова О.Ю.",
        "ИП Шубин Д.В.",
        "ИП Яготин С.С.",
        "ИП Черкасова М.В.",
        "ИП Черкасов С.А."
    ];

    /// <summary>
    /// Стабильный id ЮЛ. Разные id → разные компании (нельзя склеивать).
    /// </summary>
    public static string? TryGetLegalEntityId(string? organization)
    {
        var raw = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var core = OrganizationMatcher.GetCoreName(raw);
        var key = ToKey(core);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (Aliases.TryGetValue(key, out var aliased))
        {
            return aliased;
        }

        if (IdsByKey.TryGetValue(key, out var exact))
        {
            return exact;
        }

        // ИП: «Алешкевич А.Д.» ↔ «Алешкевич Александр Дмитриевич» — один id.
        if (raw.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return TryResolveIpId(core);
        }

        return null;
    }

    /// <summary>Два названия — разные юрлица из реестра (или явные пары КСК/Каркас и т.п.).</summary>
    public static bool AreDistinctLegalEntities(string? left, string? right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        if (OrganizationLegalEntitySplit.AreKnownDistinctLegacy(a, b))
        {
            return true;
        }

        var idLeft = TryGetLegalEntityId(a);
        var idRight = TryGetLegalEntityId(b);
        if (idLeft is null || idRight is null)
        {
            return false;
        }

        return !string.Equals(idLeft, idRight, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveCanonicalName(string? organization)
    {
        var id = TryGetLegalEntityId(organization);
        if (id is null)
        {
            return null;
        }

        return CanonicalById.TryGetValue(id, out var name) ? name : null;
    }

    private static readonly Dictionary<string, string> CanonicalById;
    private static readonly Dictionary<string, string> IdsByKey;

    /// <summary>Варианты написания из 1С / Excel → id канона.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["сстрой"] = "стандартстрой",
        ["cстрой"] = "стандартстрой",
        ["стандартстрой"] = "стандартстрой",
        ["естрой"] = "естрой",
        ["отделстрой"] = "отделстрой",
        ["новыеметры"] = "новыеметры",
        ["бетонэксперт"] = "бетонэксперт",
        ["инвиктус"] = "инвиктус",
        ["созвездие"] = "созвездие",
        ["умтсв"] = "умтсв",
        ["кск"] = "кск",
        ["компаниастройкаркас"] = "компаниастройкаркас",
        ["стройкаркас"] = "компаниастройкаркас",
        ["викториястрой"] = "викториястрой",
        ["викторияпроект"] = "викторияпроект",
        ["викторияинвест"] = "викторияинвест",
        ["викториясити"] = "викториясити",
        ["викторияэстейт"] = "викторияэстейт",
        ["викторияа"] = "викторияа",
        ["викторияскк"] = "викторияскк",
        ["фестгрупп"] = "фестгрупп"
    };

    static OrganizationCanonicalRegistry()
    {
        CanonicalById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IdsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CanonicalNames)
        {
            var id = ToKey(OrganizationMatcher.GetCoreName(name));
            if (id.Length == 0)
            {
                continue;
            }

            // Алиасы перекрывают id для ССТРОЙ и т.п.
            if (Aliases.TryGetValue(id, out var aliased))
            {
                id = aliased;
            }

            CanonicalById[id] = name;
            IdsByKey[id] = id;
            IdsByKey[ToKey(OrganizationMatcher.GetCoreName(name))] = id;
        }
    }

    private static string? TryResolveIpId(string core)
    {
        var parts = core.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var surname = parts[0];
        var hits = new List<string>();
        foreach (var name in CanonicalNames)
        {
            if (!name.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var canonicalCore = OrganizationMatcher.GetCoreName(name);
            var cParts = canonicalCore.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cParts.Length == 0
                || !cParts[0].Equals(surname, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Только фамилия без инициалов — неоднозначно при однофамильцах.
            if (parts.Length < 2)
            {
                hits.Add(canonicalCore);
                continue;
            }

            if (cParts.Length >= 2 && !InitialsCompatible(parts, cParts))
            {
                continue;
            }

            var id = ToKey(canonicalCore);
            return Aliases.TryGetValue(id, out var aliased) ? aliased : id;
        }

        if (parts.Length < 2 && hits.Count == 1)
        {
            var id = ToKey(hits[0]);
            return Aliases.TryGetValue(id, out var aliased) ? aliased : id;
        }

        return null;
    }

    private static bool InitialsCompatible(string[] left, string[] right)
    {
        // Normalize убирает точки: «А.Д.» → «ад» одним токеном.
        var aRest = ExpandInitialTokens(left.Skip(1));
        var bRest = ExpandInitialTokens(right.Skip(1));
        if (aRest.Count == 0 || bRest.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < Math.Min(aRest.Count, bRest.Count); i++)
        {
            var a = aRest[i];
            var b = bRest[i];
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (a.Length == 1 || b.Length == 1)
            {
                if (char.ToUpperInvariant(a[0]) == char.ToUpperInvariant(b[0]))
                {
                    continue;
                }
            }

            return false;
        }

        return true;
    }

    private static List<string> ExpandInitialTokens(IEnumerable<string> tokens)
    {
        var result = new List<string>();
        foreach (var token in tokens)
        {
            var t = (token ?? string.Empty).Trim('.').ToLowerInvariant().Replace('ё', 'е');
            if (t.Length == 0)
            {
                continue;
            }

            // «ад» после удаления точек из «А.Д.» → А + Д
            if (t.Length == 2 && t.All(char.IsLetter))
            {
                result.Add(t[0].ToString());
                result.Add(t[1].ToString());
                continue;
            }

            result.Add(t);
        }

        return result;
    }

    private static string ToKey(string core)
    {
        var s = (core ?? string.Empty)
            .ToLowerInvariant()
            .Replace('ё', 'е');
        var chars = s.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }
}
