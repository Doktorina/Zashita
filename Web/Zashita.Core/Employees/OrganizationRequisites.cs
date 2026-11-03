namespace Zashita.Core.Employees;

/// <summary>Один банковский счёт организации (у юрлица может быть несколько).</summary>
public sealed class OrganizationBankAccount
{
    /// <summary>Подпись: «основной», «Альфа», «зарплатный» и т.п.</summary>
    public string Label { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
    public string Bik { get; set; } = string.Empty;
    public string CheckingAccount { get; set; } = string.Empty;
    public string CorrespondentAccount { get; set; } = string.Empty;
    public string BankCity { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Label)
        && string.IsNullOrWhiteSpace(BankName)
        && string.IsNullOrWhiteSpace(Bik)
        && string.IsNullOrWhiteSpace(CheckingAccount)
        && string.IsNullOrWhiteSpace(CorrespondentAccount)
        && string.IsNullOrWhiteSpace(BankCity);

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(Bik)
        || !string.IsNullOrWhiteSpace(BankName)
        || !string.IsNullOrWhiteSpace(CheckingAccount)
        || !string.IsNullOrWhiteSpace(CorrespondentAccount);

    public void Normalize()
    {
        Label = (Label ?? string.Empty).Trim();
        BankName = (BankName ?? string.Empty).Trim();
        Bik = (Bik ?? string.Empty).Trim();
        CheckingAccount = (CheckingAccount ?? string.Empty).Trim();
        CorrespondentAccount = (CorrespondentAccount ?? string.Empty).Trim();
        BankCity = (BankCity ?? string.Empty).Trim();
    }

    public OrganizationBankAccount Clone() => new()
    {
        Label = Label,
        BankName = BankName,
        Bik = Bik,
        CheckingAccount = CheckingAccount,
        CorrespondentAccount = CorrespondentAccount,
        BankCity = BankCity,
        IsPrimary = IsPrimary
    };

    public string FormatLetterhead()
    {
        var bik = string.IsNullOrWhiteSpace(Bik) ? "_______________" : Bik.Trim();
        var bank = string.IsNullOrWhiteSpace(BankName) ? "________________________________" : BankName.Trim();
        var city = string.IsNullOrWhiteSpace(BankCity) ? "_______________" : BankCity.Trim();
        var ks = string.IsNullOrWhiteSpace(CorrespondentAccount) ? "_________________________" : CorrespondentAccount.Trim();
        var rs = string.IsNullOrWhiteSpace(CheckingAccount) ? "_________________________" : CheckingAccount.Trim();
        var head = $"БИК {bik}  Банк {bank}  г. {city}\nк/с {ks}  р/с {rs}";
        return string.IsNullOrWhiteSpace(Label) ? head : $"{Label.Trim()}: {head}";
    }
}

/// <summary>Юридические и банковские реквизиты организации-работодателя.</summary>
public sealed class OrganizationRequisites
{
    public string Inn { get; set; } = string.Empty;
    public string Kpp { get; set; } = string.Empty;
    public string Ogrn { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    /// <summary>Несколько банковских счетов. Legacy-поля ниже синхронизируются с основным.</summary>
    public List<OrganizationBankAccount> Banks { get; set; } = [];

    // Legacy single-bank fields (для старых JSON и печати).
    public string BankName { get; set; } = string.Empty;
    public string Bik { get; set; } = string.Empty;
    public string CheckingAccount { get; set; } = string.Empty;
    public string CorrespondentAccount { get; set; } = string.Empty;
    public string BankCity { get; set; } = string.Empty;

    /// <summary>Электронная почта и телефон работодателя (для направлений МО).</summary>
    public string EmployerContacts { get; set; } = string.Empty;
    /// <summary>Форма собственности и ОКВЭД (для направлений МО).</summary>
    public string OwnershipOkved { get; set; } = string.Empty;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Inn)
        && string.IsNullOrWhiteSpace(Kpp)
        && string.IsNullOrWhiteSpace(Ogrn)
        && string.IsNullOrWhiteSpace(Address)
        && !Banks.Any(b => !b.IsEmpty)
        && string.IsNullOrWhiteSpace(BankName)
        && string.IsNullOrWhiteSpace(Bik)
        && string.IsNullOrWhiteSpace(CheckingAccount)
        && string.IsNullOrWhiteSpace(CorrespondentAccount)
        && string.IsNullOrWhiteSpace(BankCity)
        && string.IsNullOrWhiteSpace(EmployerContacts)
        && string.IsNullOrWhiteSpace(OwnershipOkved);

    public bool HasBankDetails =>
        Banks.Any(b => b.HasDetails)
        || !string.IsNullOrWhiteSpace(Bik)
        || !string.IsNullOrWhiteSpace(BankName)
        || !string.IsNullOrWhiteSpace(CheckingAccount)
        || !string.IsNullOrWhiteSpace(CorrespondentAccount);

    public OrganizationBankAccount? PrimaryBank =>
        Banks.FirstOrDefault(b => b.IsPrimary && b.HasDetails)
        ?? Banks.FirstOrDefault(b => b.HasDetails);

    public string FormatSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Inn))
        {
            parts.Add($"ИНН {Inn.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(Ogrn))
        {
            parts.Add($"ОГРН {Ogrn.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(Kpp))
        {
            parts.Add($"КПП {Kpp.Trim()}");
        }

        var bankCount = Banks.Count(b => b.HasDetails);
        if (bankCount > 1)
        {
            parts.Add($"банков: {bankCount}");
        }
        else if (bankCount == 1 || HasBankDetails)
        {
            var bank = PrimaryBank?.BankName;
            if (string.IsNullOrWhiteSpace(bank))
            {
                bank = BankName;
            }

            if (!string.IsNullOrWhiteSpace(bank))
            {
                var shortBank = bank.Trim();
                parts.Add(shortBank.Length > 28 ? shortBank[..28] + "…" : shortBank);
            }
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(Address))
        {
            var address = Address.Trim();
            parts.Add(address.Length > 40 ? address[..40] + "…" : address);
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    public string FormatBankLetterhead()
    {
        var primary = PrimaryBank;
        if (primary is not null)
        {
            return primary.FormatLetterhead();
        }

        var bik = string.IsNullOrWhiteSpace(Bik) ? "_______________" : Bik.Trim();
        var bank = string.IsNullOrWhiteSpace(BankName) ? "________________________________" : BankName.Trim();
        var city = string.IsNullOrWhiteSpace(BankCity) ? "_______________" : BankCity.Trim();
        var ks = string.IsNullOrWhiteSpace(CorrespondentAccount) ? "_________________________" : CorrespondentAccount.Trim();
        var rs = string.IsNullOrWhiteSpace(CheckingAccount) ? "_________________________" : CheckingAccount.Trim();
        return $"БИК {bik}  Банк {bank}  г. {city}\nк/с {ks}  р/с {rs}";
    }

    /// <summary>
    /// Полное наименование для шапки заявки:
    /// «ООО «…»» → «Общество с ограниченной ответственностью «…»».
    /// </summary>
    public static string FormatApplicationOrgTitle(string? organization)
    {
        var name = (organization ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return string.Empty;
        }

        if (name.StartsWith("Общество с ограниченной ответственностью", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Индивидуальный предприниматель", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Акционерное общество", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Публичное акционерное общество", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (name.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            return "Общество с ограниченной ответственностью " + name[3..].Trim();
        }

        if (name.StartsWith("ПАО", StringComparison.OrdinalIgnoreCase))
        {
            return "Публичное акционерное общество " + name[3..].Trim();
        }

        if (name.StartsWith("АО", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("АОБ", StringComparison.OrdinalIgnoreCase))
        {
            return "Акционерное общество " + name[2..].Trim();
        }

        if (name.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return "Индивидуальный предприниматель " + name[2..].Trim();
        }

        return name;
    }

    /// <summary>
    /// Блок реквизитов под названием организации в заявке на обучение
    /// (адрес, ИНН/КПП/ОГРН, строка банка в формате образца).
    /// </summary>
    public string FormatApplicationLetterheadBody()
    {
        Normalize();
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(Address))
        {
            lines.Add(Address);
        }

        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(Inn))
        {
            ids.Add($"ИНН {Inn}");
        }

        if (!string.IsNullOrWhiteSpace(Kpp))
        {
            ids.Add($"КПП {Kpp}");
        }

        if (!string.IsNullOrWhiteSpace(Ogrn))
        {
            ids.Add($"ОГРН {Ogrn}");
        }

        if (ids.Count > 0)
        {
            lines.Add(string.Join(" ", ids));
        }

        var bank = PrimaryBank;
        var rs = (bank?.CheckingAccount ?? CheckingAccount ?? string.Empty).Trim();
        var ks = (bank?.CorrespondentAccount ?? CorrespondentAccount ?? string.Empty).Trim();
        var bik = (bank?.Bik ?? Bik ?? string.Empty).Trim();
        var bankName = (bank?.BankName ?? BankName ?? string.Empty).Trim();
        var bankCity = (bank?.BankCity ?? BankCity ?? string.Empty).Trim();
        if (rs.Length > 0 || ks.Length > 0 || bik.Length > 0 || bankName.Length > 0)
        {
            var parts = new List<string>();
            if (rs.Length > 0)
            {
                parts.Add($"р/с {rs}");
            }

            // Как в образце: «в Уральском Банке ПАО СБЕРБАНК г. Екатеринбург»
            var bankPart = bankName;
            if (bankCity.Length > 0
                && bankPart.IndexOf("г.", StringComparison.OrdinalIgnoreCase) < 0
                && bankPart.IndexOf(bankCity, StringComparison.OrdinalIgnoreCase) < 0)
            {
                bankPart = string.IsNullOrWhiteSpace(bankPart)
                    ? $"г. {bankCity}"
                    : $"{bankPart} г. {bankCity}";
            }

            if (bankPart.Length > 0)
            {
                if (!bankPart.StartsWith("в ", StringComparison.OrdinalIgnoreCase))
                {
                    bankPart = "в " + bankPart;
                }

                parts.Add(bankPart);
            }

            if (ks.Length > 0)
            {
                parts.Add($"к/с {ks}");
            }

            if (bik.Length > 0)
            {
                parts.Add($"БИК {bik}");
            }

            lines.Add(string.Join(" ", parts));
        }

        return string.Join("\n", lines);
    }

    /// <summary>Все заполненные банки — для предпросмотра в карточке организации.</summary>
    public string FormatAllBanksPreview()
    {
        var filled = Banks.Where(b => b.HasDetails).ToList();
        if (filled.Count == 0)
        {
            return HasBankDetails ? FormatBankLetterhead() : string.Empty;
        }

        return string.Join("\n\n", filled.Select(b => b.FormatLetterhead()));
    }

    public void Normalize()
    {
        Inn = (Inn ?? string.Empty).Trim();
        Kpp = (Kpp ?? string.Empty).Trim();
        Ogrn = (Ogrn ?? string.Empty).Trim();
        Address = (Address ?? string.Empty).Trim();
        EmployerContacts = (EmployerContacts ?? string.Empty).Trim();
        OwnershipOkved = (OwnershipOkved ?? string.Empty).Trim();

        Banks ??= [];
        foreach (var bank in Banks)
        {
            bank.Normalize();
        }

        Banks.RemoveAll(b => b.IsEmpty);

        // Старые JSON без Banks → один банк из legacy-полей.
        if (Banks.Count == 0 &&
            (!string.IsNullOrWhiteSpace(BankName)
             || !string.IsNullOrWhiteSpace(Bik)
             || !string.IsNullOrWhiteSpace(CheckingAccount)
             || !string.IsNullOrWhiteSpace(CorrespondentAccount)
             || !string.IsNullOrWhiteSpace(BankCity)))
        {
            Banks.Add(new OrganizationBankAccount
            {
                Label = "Основной",
                BankName = BankName,
                Bik = Bik,
                CheckingAccount = CheckingAccount,
                CorrespondentAccount = CorrespondentAccount,
                BankCity = BankCity,
                IsPrimary = true
            });
        }

        if (Banks.Count > 0 && !Banks.Any(b => b.IsPrimary))
        {
            Banks[0].IsPrimary = true;
        }

        // Legacy-поля = основной банк (совместимость печати/старого кода).
        var primary = PrimaryBank ?? Banks.FirstOrDefault();
        if (primary is not null)
        {
            BankName = primary.BankName;
            Bik = primary.Bik;
            CheckingAccount = primary.CheckingAccount;
            CorrespondentAccount = primary.CorrespondentAccount;
            BankCity = primary.BankCity;
        }
        else
        {
            BankName = string.Empty;
            Bik = string.Empty;
            CheckingAccount = string.Empty;
            CorrespondentAccount = string.Empty;
            BankCity = string.Empty;
        }
    }

    public OrganizationRequisites Clone() => new()
    {
        Inn = Inn,
        Kpp = Kpp,
        Ogrn = Ogrn,
        Address = Address,
        Banks = Banks.Select(b => b.Clone()).ToList(),
        BankName = BankName,
        Bik = Bik,
        CheckingAccount = CheckingAccount,
        CorrespondentAccount = CorrespondentAccount,
        BankCity = BankCity,
        EmployerContacts = EmployerContacts,
        OwnershipOkved = OwnershipOkved
    };

    /// <summary>Заполнить пустые банковские поля из текста letterhead печати.</summary>
    public void EnrichBankFromLetterhead(string? letterhead)
    {
        if (string.IsNullOrWhiteSpace(letterhead) || HasBankDetails)
        {
            return;
        }

        var text = letterhead.Trim();
        TryCapture(text, @"БИК\s+([^\s_]+)", v => { if (string.IsNullOrWhiteSpace(Bik) && !IsPlaceholder(v)) Bik = v; });
        TryCapture(text, @"Банк\s+(.+?)\s+г\.", v => { if (string.IsNullOrWhiteSpace(BankName) && !IsPlaceholder(v)) BankName = v.Trim(); });
        TryCapture(text, @"г\.\s+([^\r\n]+)", v =>
        {
            if (string.IsNullOrWhiteSpace(BankCity) && !IsPlaceholder(v))
            {
                BankCity = v.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0];
            }
        });
        TryCapture(text, @"к/с\s+([^\s_]+)", v => { if (string.IsNullOrWhiteSpace(CorrespondentAccount) && !IsPlaceholder(v)) CorrespondentAccount = v; });
        TryCapture(text, @"р/с\s+([^\s_]+)", v => { if (string.IsNullOrWhiteSpace(CheckingAccount) && !IsPlaceholder(v)) CheckingAccount = v; });

        if (!HasBankDetails && text.Length > 0 && !text.Contains('_', StringComparison.Ordinal))
        {
            BankName = text.Length > 120 ? text[..120] + "…" : text;
        }

        Normalize();
    }

    private static bool IsPlaceholder(string value) =>
        string.IsNullOrWhiteSpace(value) || value.All(c => c == '_');

    private static void TryCapture(string text, string pattern, Action<string> assign)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (match.Success && match.Groups.Count > 1)
        {
            assign(match.Groups[1].Value.Trim());
        }
    }
}
