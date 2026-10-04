using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class WholesaleCustomerPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFilePickerService _filePicker;
    private readonly PurchaseSpreadsheetReader _spreadsheetReader;
    private readonly CurrentSession _currentSession;
    private readonly IConfirmationService _confirmation;
    private Guid _editingCustomerId;

    public WholesaleCustomerPageViewModel(
        IServiceScopeFactory scopeFactory,
        IFilePickerService filePicker,
        PurchaseSpreadsheetReader spreadsheetReader,
        CurrentSession currentSession,
        IConfirmationService confirmation)
    {
        _scopeFactory = scopeFactory;
        _filePicker = filePicker;
        _spreadsheetReader = spreadsheetReader;
        _currentSession = currentSession;
        _confirmation = confirmation;
    }

    public ObservableCollection<WholesaleCustomerRecord> Customers { get; } = [];
    public ObservableCollection<string> LicenceTypeOptions { get; } = [];
    public ObservableCollection<string> BuyerTypeOptions { get; } =
        ["Distributor", "Retailer", "Hospital", "Institution", "Other"];
    public ObservableCollection<CustomerLicenceDraft> Licences { get; } = [];

    [ObservableProperty] private WholesaleCustomerRecord? _selectedRecord;
    [ObservableProperty] private CustomerLicenceDraft? _selectedLicence;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _buyerType = "Distributor";
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _gstin = string.Empty;
    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private string _stateDrugControlPortalUrl = string.Empty;
    [ObservableProperty] private string _creditLimit = "0.00";
    [ObservableProperty] private string _creditDays = "0";
    [ObservableProperty] private string _priceCategory = string.Empty;
    [ObservableProperty] private string _route = string.Empty;
    [ObservableProperty] private string _salesman = string.Empty;
    [ObservableProperty] private string _openingBalance = "0.00";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _verifiedBy = string.Empty;
    [ObservableProperty] private DateTime? _verifiedOn;
    [ObservableProperty] private string _verificationMethod = string.Empty;
    [ObservableProperty] private string _buyerLicenceRuleType = "Distributor";
    [ObservableProperty] private string _buyerLicenceRuleTypes = string.Empty;
    [ObservableProperty] private string _newLicenceType = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
        var rules = await service.GetBuyerLicenceRulesAsync(cancellationToken);
        BuyerLicenceRuleTypes = rules.TryGetValue(BuyerLicenceRuleType, out var types)
            ? string.Join(", ", types)
            : string.Empty;
        await ReloadCustomersAsync(service, cancellationToken);
    }

    [RelayCommand]
    private void NewCustomer()
    {
        _editingCustomerId = Guid.NewGuid();
        SelectedRecord = null;
        Name = string.Empty;
        BuyerType = "Distributor";
        Phone = string.Empty;
        Email = string.Empty;
        Address = string.Empty;
        Gstin = string.Empty;
        State = string.Empty;
        StateDrugControlPortalUrl = string.Empty;
        CreditLimit = "0.00";
        CreditDays = "0";
        PriceCategory = string.Empty;
        Route = string.Empty;
        Salesman = string.Empty;
        OpeningBalance = "0.00";
        IsActive = true;
        VerifiedBy = string.Empty;
        VerifiedOn = null;
        VerificationMethod = string.Empty;
        Licences.Clear();
        ErrorMessage = string.Empty;
        StatusMessage = "New customer";
    }

    [RelayCommand]
    private void SelectCustomer(WholesaleCustomerRecord? record)
    {
        SelectedRecord = record;
    }

    partial void OnSelectedRecordChanged(WholesaleCustomerRecord? value)
    {
        if (value is null)
        {
            return;
        }

        _editingCustomerId = value.Customer.Id;
        var customer = value.Customer;
        Name = customer.Name;
        BuyerType = customer.BuyerType ?? string.Empty;
        Phone = customer.Phone ?? string.Empty;
        Email = customer.Email ?? string.Empty;
        Address = customer.Address ?? string.Empty;
        Gstin = customer.Gstin ?? string.Empty;
        State = customer.State ?? string.Empty;
        StateDrugControlPortalUrl = customer.StateDrugControlPortalUrl ?? string.Empty;
        CreditLimit = customer.CreditLimit.ToString("0.00", CultureInfo.CurrentCulture);
        CreditDays = customer.CreditDays.ToString(CultureInfo.CurrentCulture);
        PriceCategory = customer.PriceCategory ?? string.Empty;
        Route = customer.Route ?? string.Empty;
        Salesman = customer.Salesman ?? string.Empty;
        OpeningBalance = customer.OpeningBalance.ToString("0.00", CultureInfo.CurrentCulture);
        IsActive = customer.IsActive;
        VerifiedBy = customer.VerifiedBy ?? string.Empty;
        VerifiedOn = customer.VerifiedOnUtc?.ToLocalTime();
        VerificationMethod = customer.VerificationMethod ?? string.Empty;
        Licences.Clear();
        foreach (var licence in value.Licences)
        {
            Licences.Add(CustomerLicenceDraft.FromEntity(licence));
        }
        ErrorMessage = string.Empty;
        StatusMessage = ExpiryAlertText(value.ExpiryAlert);
    }

    [RelayCommand]
    private void AddLicence()
    {
        if (string.IsNullOrWhiteSpace(NewLicenceType))
        {
            NewLicenceType = LicenceTypeOptions.FirstOrDefault() ?? "20";
        }

        Licences.Add(new CustomerLicenceDraft { LicenceType = NewLicenceType });
    }

    [RelayCommand]
    private void RemoveLicence(CustomerLicenceDraft? licence)
    {
        if (licence is not null)
        {
            Licences.Remove(licence);
        }
    }

    [RelayCommand]
    private void AddLicenceType()
    {
        var licenceType = NewLicenceType.Trim();
        if (licenceType.Length == 0)
        {
            ErrorMessage = "Enter a licence type to add it to the editable list.";
            return;
        }

        if (!LicenceTypeOptions.Contains(licenceType, StringComparer.OrdinalIgnoreCase))
        {
            LicenceTypeOptions.Add(licenceType);
        }

        StatusMessage = $"Licence type {licenceType} will be saved to the editable list.";
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveBuyerLicenceRuleAsync()
    {
        var types = BuyerLicenceRuleTypes.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrWhiteSpace(BuyerLicenceRuleType) || types.Length == 0)
        {
            ErrorMessage = "Configure one or more permitted licence types for this buyer type.";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>()
            .SaveBuyerLicenceRulesAsync(new Dictionary<string, IReadOnlyCollection<string>>
            {
                [BuyerLicenceRuleType.Trim()] = types
            });
        StatusMessage = $"Allowed licence types saved for {BuyerLicenceRuleType}.";
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void SelectLicenceScan(CustomerLicenceDraft? licence)
    {
        if (licence is null)
        {
            return;
        }

        var sourcePath = _filePicker.PickLicenceDocument();
        if (sourcePath is null)
        {
            return;
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".pdf" and not ".png" and not ".jpg" and not ".jpeg" and not ".bmp")
        {
            ErrorMessage = "Licence copy must be a PDF or image file.";
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "licences");
        Directory.CreateDirectory(directory);
        licence.DocumentPath = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, licence.DocumentPath, overwrite: false);
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            var customer = BuildCustomer();
            var licences = Licences.Select(item => item.ToEntity()).ToArray();
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
            await service.SaveAsync(
                customer,
                licences,
                LicenceTypeOptions.ToArray(),
                _currentSession.User?.Id ?? throw new UnauthorizedAccessException("Sign in before editing customers."));
            await ReloadCustomersAsync(service, CancellationToken.None);
            SelectedRecord = Customers.Single(item => item.Customer.Id == customer.Id);
            StatusMessage = "Customer saved.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        var path = _filePicker.PickCustomerExcel();
        if (path is null)
        {
            return;
        }

        ErrorMessage = string.Empty;
        try
        {
            var sheet = await _spreadsheetReader.ReadAsync(path);
            var imports = ParseCustomerRows(sheet, out var rowErrors);
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
            var result = await service.ImportAsync(
                imports,
                LicenceTypeOptions.ToArray(),
                _currentSession.User?.Id ?? throw new UnauthorizedAccessException("Sign in before importing customers."));
            await ReloadCustomersAsync(service, CancellationToken.None);
            var errorSummary = rowErrors.Concat(result.Errors).Take(5).ToArray();
            StatusMessage = $"Imported {result.Imported}; skipped duplicates {result.SkippedDuplicates}; invalid rows {rowErrors.Count + result.Errors.Count}.";
            ErrorMessage = errorSummary.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, errorSummary);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void OpenStatePortal()
    {
        if (!Uri.TryCreate(StateDrugControlPortalUrl, UriKind.Absolute, out var portalUri) ||
            portalUri.Scheme != Uri.UriSchemeHttps)
        {
            ErrorMessage = "Enter the HTTPS URL of the state's drug-control portal first.";
            return;
        }

        var confirmed = _confirmation.Confirm(
            $"PharmaBill cannot verify this portal or the customer's licence online. Open the configured portal?\n\n{portalUri}",
            "External licence portal");
        if (!confirmed)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(portalUri.AbsoluteUri) { UseShellExecute = true });
            ErrorMessage = string.Empty;
            StatusMessage = "Portal opened. Licence verification must be completed and recorded by an authorised user.";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ErrorMessage = $"The configured portal could not be opened: {exception.Message}";
        }
    }

    private async Task ReloadCustomersAsync(
        WholesaleCustomerService service,
        CancellationToken cancellationToken)
    {
        var selectedId = _editingCustomerId;
        var licenceTypes = await service.GetLicenceTypesAsync(cancellationToken);
        LicenceTypeOptions.Clear();
        foreach (var type in licenceTypes)
        {
            LicenceTypeOptions.Add(type);
        }

        var records = await service.GetCustomersAsync(cancellationToken);
        Customers.Clear();
        foreach (var record in records)
        {
            Customers.Add(record);
        }

        if (selectedId != Guid.Empty)
        {
            SelectedRecord = Customers.FirstOrDefault(item => item.Customer.Id == selectedId);
        }
    }

    private Customer BuildCustomer()
    {
        if (!decimal.TryParse(CreditLimit, NumberStyles.Number, CultureInfo.CurrentCulture, out var creditLimit) ||
            !decimal.TryParse(OpeningBalance, NumberStyles.Number, CultureInfo.CurrentCulture, out var openingBalance))
        {
            throw new ArgumentException("Enter valid credit limit and opening balance amounts.");
        }

        if (!int.TryParse(CreditDays, NumberStyles.Integer, CultureInfo.CurrentCulture, out var creditDays))
        {
            throw new ArgumentException("Enter a whole number for credit days.");
        }

        return new Customer
        {
            Id = _editingCustomerId == Guid.Empty ? Guid.NewGuid() : _editingCustomerId,
            Name = Name.Trim(),
            BuyerType = BuyerType.Trim(),
            Phone = NullIfEmpty(Phone),
            Email = NullIfEmpty(Email),
            Address = NullIfEmpty(Address),
            Gstin = NullIfEmpty(Gstin),
            State = NullIfEmpty(State),
            StateDrugControlPortalUrl = NullIfEmpty(StateDrugControlPortalUrl),
            CreditLimit = creditLimit,
            CreditDays = creditDays,
            PriceCategory = NullIfEmpty(PriceCategory),
            Route = NullIfEmpty(Route),
            Salesman = NullIfEmpty(Salesman),
            OpeningBalance = openingBalance,
            IsActive = IsActive,
            VerifiedBy = NullIfEmpty(VerifiedBy),
            VerifiedOnUtc = VerifiedOn.HasValue ? DateTime.SpecifyKind(VerifiedOn.Value.ToUniversalTime(), DateTimeKind.Utc) : null,
            VerificationMethod = NullIfEmpty(VerificationMethod)
        };
    }

    private static IReadOnlyList<WholesaleCustomerImportRecord> ParseCustomerRows(
        PurchaseSpreadsheetData sheet,
        out IReadOnlyList<string> errors)
    {
        var parsedErrors = new List<string>();
        var grouped = new Dictionary<string, WholesaleCustomerImportRecord>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var row = sheet.Rows[index];
            var rowNumber = index + 2;
            var name = Get(row, "Customer", "Customer Name", "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                parsedErrors.Add($"Row {rowNumber}: customer name is required.");
                continue;
            }

            var phone = Get(row, "Phone", "Mobile", "Contact Number");
            var gstin = Get(row, "GSTIN", "GST No", "GST Number");
            var buyerType = Get(row, "Buyer Type", "Type");
            var key = !string.IsNullOrWhiteSpace(gstin)
                ? $"GST:{Normalize(gstin)}"
                : $"NAME:{Normalize(name)}|PHONE:{Normalize(phone)}";
            if (!grouped.TryGetValue(key, out var import))
            {
                var customer = new Customer
                {
                    Name = name.Trim(),
                    BuyerType = string.IsNullOrWhiteSpace(buyerType) ? "Distributor" : buyerType.Trim(),
                    Phone = NullIfEmpty(phone),
                    Email = NullIfEmpty(Get(row, "Email")),
                    Address = NullIfEmpty(Get(row, "Address")),
                    Gstin = NullIfEmpty(gstin),
                    State = NullIfEmpty(Get(row, "State")),
                    StateDrugControlPortalUrl = NullIfEmpty(Get(row, "State Drug Control Portal URL", "Portal URL")),
                    PriceCategory = NullIfEmpty(Get(row, "Price Category")),
                    Route = NullIfEmpty(Get(row, "Route")),
                    Salesman = NullIfEmpty(Get(row, "Salesman")),
                    IsActive = !string.Equals(Get(row, "Status"), "Blocked", StringComparison.OrdinalIgnoreCase)
                };
                if (!TryOptionalDecimal(Get(row, "Credit Limit"), out var creditLimit) ||
                    !TryOptionalDecimal(Get(row, "Opening Balance"), out var openingBalance) ||
                    !TryOptionalInt(Get(row, "Credit Days"), out var creditDays))
                {
                    parsedErrors.Add($"Row {rowNumber}: invalid credit limit, credit days or opening balance.");
                    continue;
                }

                customer.CreditLimit = creditLimit;
                customer.OpeningBalance = openingBalance;
                customer.CreditDays = creditDays;
                import = new WholesaleCustomerImportRecord(customer, []);
                grouped.Add(key, import);
            }

            var licenceNumber = Get(row, "Licence Number", "License Number", "Licence No", "License No");
            var licenceType = Get(row, "Licence Type", "License Type");
            if (!string.IsNullOrWhiteSpace(licenceNumber) || !string.IsNullOrWhiteSpace(licenceType))
            {
                if (string.IsNullOrWhiteSpace(licenceNumber) || string.IsNullOrWhiteSpace(licenceType))
                {
                    parsedErrors.Add($"Row {rowNumber}: licence type and number must both be supplied.");
                    continue;
                }

                if (!TryDate(Get(row, "Issue Date", "Issued On"), out var issueDate) ||
                    !TryDate(Get(row, "Expiry Date", "Expires On"), out var expiryDate))
                {
                    parsedErrors.Add($"Row {rowNumber}: invalid licence issue or expiry date.");
                    continue;
                }

                var licence = new CustomerLicence
                {
                    LicenceType = licenceType.Trim(),
                    LicenceNumber = licenceNumber.Trim(),
                    IssuedOn = issueDate,
                    ExpiresOn = expiryDate,
                    IssuingAuthority = NullIfEmpty(Get(row, "Issuing Authority", "Authority"))
                };
                grouped[key] = import with { Licences = import.Licences.Append(licence).ToArray() };
            }
        }

        errors = parsedErrors;
        return grouped.Values.ToArray();
    }

    private static string Get(IReadOnlyDictionary<string, string> row, params string[] headers)
    {
        foreach (var header in headers)
        {
            if (row.TryGetValue(header, out var value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static bool TryOptionalDecimal(string value, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return true;
        }

        return TryDecimal(value, out result);
    }

    private static bool TryOptionalInt(string value, out int result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0;
            return true;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryDate(string value, out DateOnly? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var localDate) ||
            DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out localDate))
        {
            result = localDate;
            return true;
        }

        return false;
    }

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ExpiryAlertText(CustomerLicenceExpiryAlert alert) => alert switch
    {
        CustomerLicenceExpiryAlert.Expired => "Licence expired",
        CustomerLicenceExpiryAlert.ExpiringWithin30Days => "Licence expiry alert: within 30 days",
        CustomerLicenceExpiryAlert.ExpiringWithin60Days => "Licence expiry alert: within 60 days",
        _ => string.Empty
    };
}

public partial class CustomerLicenceDraft : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _licenceType = string.Empty;
    [ObservableProperty] private string _licenceNumber = string.Empty;
    [ObservableProperty] private DateTime? _issuedOn;
    [ObservableProperty] private DateTime? _expiresOn;
    [ObservableProperty] private string _issuingAuthority = string.Empty;
    [ObservableProperty] private string _documentPath = string.Empty;
    [ObservableProperty] private string _authorisation = string.Empty;

    public CustomerLicence ToEntity()
    {
        if (string.IsNullOrWhiteSpace(LicenceType) || string.IsNullOrWhiteSpace(LicenceNumber))
        {
            throw new ArgumentException("Each licence needs a type and licence number.");
        }

        if (IssuedOn.HasValue && ExpiresOn.HasValue && ExpiresOn.Value.Date < IssuedOn.Value.Date)
        {
            throw new ArgumentException($"Licence {LicenceNumber}: expiry cannot be earlier than issue date.");
        }

        return new CustomerLicence
        {
            Id = Id,
            LicenceType = LicenceType.Trim(),
            LicenceNumber = LicenceNumber.Trim(),
            IssuedOn = IssuedOn.HasValue ? DateOnly.FromDateTime(IssuedOn.Value) : null,
            ExpiresOn = ExpiresOn.HasValue ? DateOnly.FromDateTime(ExpiresOn.Value) : null,
            IssuingAuthority = string.IsNullOrWhiteSpace(IssuingAuthority) ? null : IssuingAuthority.Trim(),
            DocumentPath = string.IsNullOrWhiteSpace(DocumentPath) ? null : DocumentPath,
            Authorisation = string.IsNullOrWhiteSpace(Authorisation) ? null : Authorisation.Trim()
        };
    }

    public static CustomerLicenceDraft FromEntity(CustomerLicence licence) => new()
    {
        Id = licence.Id,
        LicenceType = licence.LicenceType,
        LicenceNumber = licence.LicenceNumber,
        IssuedOn = licence.IssuedOn?.ToDateTime(TimeOnly.MinValue),
        ExpiresOn = licence.ExpiresOn?.ToDateTime(TimeOnly.MinValue),
        IssuingAuthority = licence.IssuingAuthority ?? string.Empty,
        DocumentPath = licence.DocumentPath ?? string.Empty,
        Authorisation = licence.Authorisation ?? string.Empty
    };
}
