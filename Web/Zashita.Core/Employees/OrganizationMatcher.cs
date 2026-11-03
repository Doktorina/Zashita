namespace Zashita.Core.Employees;

public static class OrganizationMatcher
{
  /// <summary>Строгое совпадение для справочника организаций (без объединения похожих юрлиц).</summary>
  public static bool MatchesForCatalog(string leftOrganization, string rightOrganization)
  {
    var left = (leftOrganization ?? string.Empty).Trim();
    var right = (rightOrganization ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
    {
      return false;
    }

    if (OrganizationLegalEntitySplit.AreKnownDistinct(left, right))
    {
      return false;
    }

    // Обе стороны из реестра организаций.xlsx — только одинаковый id (ССТРОЙ↔СтандартСтрой да, Виктория≠Виктория Сити).
    var idLeft = OrganizationCanonicalRegistry.TryGetLegalEntityId(left);
    var idRight = OrganizationCanonicalRegistry.TryGetLegalEntityId(right);
    if (idLeft is not null && idRight is not null)
    {
      return string.Equals(idLeft, idRight, StringComparison.OrdinalIgnoreCase);
    }

    if (string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    // ИП: только полное совпадение нормализованной строки в каталоге —
    // иначе «Вахитов А.» может схлопнуть разные однофамильцев при AlignEmployeeKeys.
    if (left.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
        && right.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var leftCore = GetCoreName(left);
    var rightCore = GetCoreName(right);
    if (string.IsNullOrWhiteSpace(leftCore)
        || !string.Equals(leftCore, rightCore, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    // Короткие ядра («кск») не склеиваем с длинными названиями.
    if (leftCore.Length <= 4
        && !string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase))
    {
      var leftWords = leftCore.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var rightWords = GetCoreName(right).Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (leftWords.Length != rightWords.Length)
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Найти каноническое имя организации из списка (ИП «Нигамаев Р.Ф.» → «Нигамаев Ринат Фаизович»).
  /// </summary>
  public static string? ResolveOrganizationName(string? query, IEnumerable<string> candidates)
  {
    var q = (query ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(q))
    {
      return null;
    }

    string? exact = null;
    string? matched = null;
    foreach (var candidate in candidates)
    {
      var c = (candidate ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(c))
      {
        continue;
      }

      if (string.Equals(q, c, StringComparison.OrdinalIgnoreCase))
      {
        exact = c;
        break;
      }

      if (matched is null && Matches(q, c))
      {
        matched = c;
      }
    }

    return exact ?? matched;
  }

  /// <summary>
  /// Схлопывает дубли организаций: «ИП Алешкевич А.Д.» и «ИП Алешкевич Александр Дмитриевич».
  /// </summary>
  public static IReadOnlyList<string> DeduplicateOrganizations(
      IEnumerable<string> organizations,
      IEnumerable<string>? preferredNames = null)
  {
    var preferred = new HashSet<string>(
        (preferredNames ?? [])
            .Concat(OrganizationCanonicalRegistry.CanonicalNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()),
        StringComparer.OrdinalIgnoreCase);

    var result = new List<string>();
    foreach (var org in organizations
                 .Where(name => !string.IsNullOrWhiteSpace(name))
                 .Select(name => name.Trim()))
    {
      var existingIndex = -1;
      for (var i = 0; i < result.Count; i++)
      {
        if (Matches(result[i], org))
        {
          existingIndex = i;
          break;
        }
      }

      if (existingIndex < 0)
      {
        result.Add(org);
        continue;
      }

      result[existingIndex] = PickPreferredOrganizationName(result[existingIndex], org, preferred);
    }

    return result
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
  }

  /// <summary>
  /// Карта «любое написание → каноническое имя» для схлопывания дублей в отчётах.
  /// </summary>
  public static IReadOnlyDictionary<string, string> BuildCanonicalNameMap(
      IEnumerable<string?> organizations,
      IEnumerable<string>? preferredNames = null)
  {
    var names = organizations
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var canonicalList = DeduplicateOrganizations(names, preferredNames);
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in names)
    {
      var canonical = canonicalList.FirstOrDefault(c => Matches(c, name)) ?? name;
      map[name] = canonical;
    }

    return map;
  }

  public static string PickPreferredOrganizationName(
      string left,
      string right,
      IReadOnlySet<string>? preferredNames = null)
  {
    if (!Matches(left, right))
    {
      return left;
    }

    // ИП: полное ФИО из 1С важнее инициалов из xlsx («С.А.»), иначе карточка
    // «ИП … Сергей Андреевич» пустая, а люди лежат под «ИП … С.А.».
    if (left.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
        && right.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      var leftInitials = HasIpInitialsDisplayForm(left);
      var rightInitials = HasIpInitialsDisplayForm(right);
      if (leftInitials != rightInitials)
      {
        return leftInitials ? right : left;
      }
    }

    preferredNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (preferredNames.Contains(left))
    {
      return left;
    }

    if (preferredNames.Contains(right))
    {
      return right;
    }

    return left.Length >= right.Length ? left : right;
  }

  /// <summary>ИП с инициалами («ИП Черепонянц С.А.»), не полное ФИО.</summary>
  public static bool IsIpInitialsForm(string? organization) =>
      HasIpInitialsDisplayForm(organization ?? string.Empty);

  private static bool HasIpInitialsDisplayForm(string organization)
  {
    var trimmed = organization.Trim();
    if (!trimmed.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var tokens = trimmed[2..].Trim(' ', '.', '«', '»', '"')
      .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return tokens.Length == 2 && LooksLikeInitials(tokens[1]);
  }

  public static bool Matches(string selectedOrganization, string rowOrganization)
  {
    if (string.IsNullOrWhiteSpace(selectedOrganization))
    {
      return true;
    }

    var left = selectedOrganization.Trim();
    var right = (rowOrganization ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(right))
    {
      return false;
    }

    if (OrganizationLegalEntitySplit.AreKnownDistinct(left, right))
    {
      return false;
    }

    if (MatchesForCatalog(left, right))
    {
      return true;
    }

    if (left.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
        && right.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return MatchesIndividualEntrepreneur(left, right);
    }

    return false;
  }

  public static bool IsVictoriaContourOrganization(string organization)
  {
    var normalized = Normalize(organization);
    return normalized.Contains("виктория", StringComparison.OrdinalIgnoreCase);
  }

  public static string GetSurname(string value)
  {
    var core = GetCoreName(value);
    return core.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
  }

  public static string Normalize(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    var chars = value
      .ToLowerInvariant()
      .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
      .ToArray();
    return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
  }

  public static string GetCoreName(string value)
  {
    var norm = Normalize(value);
    if (string.IsNullOrWhiteSpace(norm))
    {
      return string.Empty;
    }

    // «СЗ» / «специализированный застройщик» — хвост/префикс юрлица, не часть бренда
    // («Виктория Инвест» ↔ «Виктория инвест СЗ»).
    // У ИП «С.З.» после Normalize → «сз» — это инициалы (Сергей Захарович), не юрформа!
    var isIp = norm.StartsWith("ип ", StringComparison.Ordinal)
               || string.Equals(norm, "ип", StringComparison.Ordinal);
    string[] legalForms = isIp
      ? ["ооо", "ип", "ао", "пао", "зао", "оао"]
      : ["ооо", "ип", "ао", "пао", "зао", "оао", "сз"];
    var words = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
      .Where(w => !legalForms.Contains(w, StringComparer.OrdinalIgnoreCase))
      .ToList();

    if (words.Count >= 2
        && words[0].Equals("специализированный", StringComparison.OrdinalIgnoreCase)
        && words[1].Equals("застройщик", StringComparison.OrdinalIgnoreCase))
    {
      words = words.Skip(2).ToList();
    }

    for (var i = 0; i < words.Count; i++)
    {
      var w = words[i];
      if (w.Length > 2 && w.EndsWith("сз", StringComparison.OrdinalIgnoreCase))
      {
        words[i] = w[..^2];
      }
    }

    var core = string.Join(' ', words.Where(w => w.Length > 0));
    return CanonicalizeKnownOrgCoreAlias(core);
  }

  /// <summary>
  /// «ССтрой» / «С Строй» / «Стандарт Строй» в Excel ↔ «СтандартСтрой» в справочнике.
  /// </summary>
  public static string CanonicalizeKnownOrgCoreAlias(string core)
  {
    var compact = (core ?? string.Empty)
      .ToLowerInvariant()
      .Replace('ё', 'е')
      .Replace(" ", "", StringComparison.Ordinal)
      .Replace("-", "", StringComparison.Ordinal)
      .Replace(".", "", StringComparison.Ordinal);
    if (compact is "сстрой" or "cстрой" or "ccтрой" or "стандартстрой")
    {
      return "стандартстрой";
    }

    return core ?? string.Empty;
  }

  public static bool LooksLikeOrganization(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    var trimmed = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
    if (!trimmed.StartsWith("ООО", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var core = GetCoreName(trimmed);
    return !string.IsNullOrWhiteSpace(core) && core.Length >= 3;
  }

  /// <summary>ИП «Фамилия И.О.» → «Фамилия И.О.» для единственного сотрудника ИП.</summary>
  public static string? InferIndividualEntrepreneurEmployee(string organization)
  {
    var trimmed = (organization ?? string.Empty).Trim();
    if (!trimmed.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var name = trimmed[2..].Trim(' ', '.', '«', '»', '"');
    return string.IsNullOrWhiteSpace(name) ? null : name;
  }

  /// <summary>
  /// Сам индивидуальный предприниматель организации (не наёмный работник).
  /// Такого человека не показываем в архиве обучений как уволенного.
  /// Идентификация только по ФИО из названия «ИП …» — профессия сама по себе
  /// (и вызов с подставленным «Индивидуальный предприниматель») никого не помечает ИП.
  /// </summary>
  public static bool IsIndividualEntrepreneurPerson(
      string? organization,
      string? personName,
      string? profession = null)
  {
    _ = profession;
    var org = (organization ?? string.Empty).Trim();
    if (!org.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var ipName = InferIndividualEntrepreneurEmployee(org);
    if (string.IsNullOrWhiteSpace(ipName))
    {
      return false;
    }

    if (string.IsNullOrWhiteSpace(personName))
    {
      return false;
    }

    if (EmployeeNameMatcher.Matches(personName, ipName))
    {
      return true;
    }

    // «Яготин С.С» ↔ «Яготин С.С.» / полное ФИО из названия ИП
    return EmployeeNameMatcher.Matches(
        NormalizeIndividualEntrepreneurPersonName(personName),
        NormalizeIndividualEntrepreneurPersonName(ipName));
  }

  private static string NormalizeIndividualEntrepreneurPersonName(string? name)
  {
    var text = (name ?? string.Empty).Trim();
    if (text.Length == 0)
    {
      return string.Empty;
    }

    // Убираем лишние точки в инициалах: «С.С.» → «С.С», двойные пробелы.
    text = text.Replace("..", ".", StringComparison.Ordinal);
    while (text.Contains(". ", StringComparison.Ordinal))
    {
      text = text.Replace(". ", ".", StringComparison.Ordinal);
    }

    return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
  }

  private static bool MatchesIndividualEntrepreneur(string left, string right)
  {
    var leftParts = ParseIndividualEntrepreneurParts(left);
    var rightParts = ParseIndividualEntrepreneurParts(right);
    if (leftParts is null || rightParts is null)
    {
      return false;
    }

    if (!leftParts.Surname.Equals(rightParts.Surname, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    // Без имени нельзя объединять однофамильцев (Вахитов Александр ≠ Вахитов Сергей).
    // «ИП Шубин» ↔ «ИП Шубин Д.В.» — короткая форма без инициалов совпадает с полной.
    if (string.IsNullOrWhiteSpace(leftParts.FirstName) || string.IsNullOrWhiteSpace(rightParts.FirstName))
    {
      var leftBare = string.IsNullOrWhiteSpace(leftParts.FirstName)
                     && string.IsNullOrWhiteSpace(leftParts.Patronymic);
      var rightBare = string.IsNullOrWhiteSpace(rightParts.FirstName)
                      && string.IsNullOrWhiteSpace(rightParts.Patronymic);
      if (leftBare || rightBare)
      {
        return true;
      }

      return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    if (IsFullNameToken(leftParts.FirstName) && IsFullNameToken(rightParts.FirstName))
    {
      if (!leftParts.FirstName.Equals(rightParts.FirstName, StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      return PatronymicCompatible(leftParts.Patronymic, rightParts.Patronymic);
    }

    return InitialCompatible(leftParts.FirstName, rightParts.FirstName)
           && PatronymicCompatible(leftParts.Patronymic, rightParts.Patronymic);
  }

  private static IndividualEntrepreneurParts? ParseIndividualEntrepreneurParts(string organization)
  {
    var trimmed = organization.Trim();
    if (!trimmed.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var person = trimmed[2..].Trim(' ', '.', '«', '»', '"');
    if (string.IsNullOrWhiteSpace(person))
    {
      return null;
    }

    var tokens = person.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 0)
    {
      return null;
    }

    var surname = tokens[0];
    string firstName = string.Empty;
    string patronymic = string.Empty;

    if (tokens.Length == 1)
    {
      return new IndividualEntrepreneurParts(surname, firstName, patronymic);
    }

    if (tokens.Length == 2 && LooksLikeInitials(tokens[1]))
    {
      var initials = ExpandInitials(tokens[1]);
      firstName = initials.FirstName;
      patronymic = initials.Patronymic;
      return new IndividualEntrepreneurParts(surname, firstName, patronymic);
    }

    firstName = tokens[1];
    if (tokens.Length >= 3)
    {
      patronymic = tokens[2];
    }

    return new IndividualEntrepreneurParts(surname, firstName, patronymic);
  }

  private static (string FirstName, string Patronymic) ExpandInitials(string value)
  {
    var letters = value
      .Where(char.IsLetter)
      .Select(char.ToUpperInvariant)
      .ToArray();

    return letters.Length switch
    {
      0 => (string.Empty, string.Empty),
      1 => (letters[0].ToString(), string.Empty),
      _ => (letters[0].ToString(), letters[1].ToString())
    };
  }

  private static bool LooksLikeInitials(string value)
  {
    var letters = value.Where(char.IsLetter).ToArray();
    return letters.Length is > 0 and <= 2
           && value.Replace(".", string.Empty).Trim().Length <= 4;
  }

  private static bool IsFullNameToken(string value) =>
    !string.IsNullOrWhiteSpace(value) && value.Length > 2;

  private static bool InitialCompatible(string left, string right)
  {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
    {
      return true;
    }

    var leftLetter = char.ToUpperInvariant(left.Trim().TrimEnd('.')[0]);
    var rightLetter = char.ToUpperInvariant(right.Trim().TrimEnd('.')[0]);
    return leftLetter == rightLetter;
  }

  private static bool PatronymicCompatible(string left, string right)
  {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
    {
      return true;
    }

    if (IsFullNameToken(left) && IsFullNameToken(right))
    {
      return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    return InitialCompatible(left, right);
  }

  private sealed record IndividualEntrepreneurParts(string Surname, string FirstName, string Patronymic);
}
