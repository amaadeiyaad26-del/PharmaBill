using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record WholesaleCustomerRecord(
    Customer Customer,
    IReadOnlyList<CustomerLicence> Licences,
    CustomerLicenceExpiryAlert ExpiryAlert)
{
    public string ExpiryAlertText => ExpiryAlert switch
    {
        CustomerLicenceExpiryAlert.Expired => "Licence expired",
        CustomerLicenceExpiryAlert.ExpiringWithin30Days => "Licence expiry alert: within 30 days",
        CustomerLicenceExpiryAlert.ExpiringWithin60Days => "Licence expiry alert: within 60 days",
        _ => string.Empty
    };

    public string StatusText => Customer.IsActive ? "Active" : "Blocked";
}

public sealed record WholesaleCustomerImportRecord(
    Customer Customer,
    IReadOnlyList<CustomerLicence> Licences);

public sealed record WholesaleCustomerImportResult(
    int Imported,
    int SkippedDuplicates,
    IReadOnlyList<string> Errors);

public sealed class WholesaleCustomerService(IUnitOfWork unitOfWork)
{
    private static readonly string[] FixedLicenceTypes = ["20", "21"];

    public async Task<IReadOnlyList<string>> GetLicenceTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await unitOfWork.Context.PharmacyProfiles
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var configuredTypes = JsonSerializer.Deserialize<string[]>(profile.WholesaleLicenceTypesJson) ?? [];
        return FixedLicenceTypes
            .Concat(configuredTypes)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveBuyerLicenceRulesAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> rules,
        CancellationToken cancellationToken = default)
    {
        if (rules.Count == 0 || rules.Any(rule =>
                string.IsNullOrWhiteSpace(rule.Key) ||
                rule.Value.Count == 0 ||
                rule.Value.Any(string.IsNullOrWhiteSpace)))
        {
            throw new InvalidOperationException("Configure at least one allowed licence type for every buyer type.");
        }

        var profile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
        var current = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            profile.WholesaleBuyerLicenceRulesJson) ?? [];
        foreach (var rule in rules)
        {
            current[rule.Key.Trim()] = current.TryGetValue(rule.Key, out var existing)
                ? existing.Concat(rule.Value).Select(type => type.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : rule.Value.Select(type => type.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        profile.WholesaleBuyerLicenceRulesJson = JsonSerializer.Serialize(current);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetBuyerLicenceRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
        var rules = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            profile.WholesaleBuyerLicenceRulesJson) ?? [];
        return rules.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<WholesaleCustomerRecord>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        var context = unitOfWork.Context;
        var customers = await context.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Name)
            .ToListAsync(cancellationToken);
        var customerIds = customers.Select(customer => customer.Id).ToArray();
        var licences = await context.CustomerLicences
            .AsNoTracking()
            .Where(licence => customerIds.Contains(licence.CustomerId))
            .OrderBy(licence => licence.ExpiresOn)
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return customers.Select(customer =>
        {
            var customerLicences = licences.Where(licence => licence.CustomerId == customer.Id).ToArray();
            var alert = customerLicences
                .Select(licence => CustomerLicenceExpiryAlerts.Evaluate(licence.ExpiresOn, today))
                .OrderByDescending(AlertPriority)
                .FirstOrDefault();
            return new WholesaleCustomerRecord(customer, customerLicences, alert);
        }).ToArray();
    }

    public async Task SaveAsync(
        Customer customer,
        IReadOnlyCollection<CustomerLicence> licences,
        IReadOnlyCollection<string> configuredLicenceTypes,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(licences);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.BuyerType);
        ValidateCustomer(customer);
        ValidateLicences(licences);
        await EnsureNoDuplicatesAsync(customer, licences, cancellationToken);

        var context = unitOfWork.Context;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var isNew = !await context.Customers.AnyAsync(item => item.Id == customer.Id, cancellationToken);
        if (isNew)
        {
            context.Customers.Add(customer);
        }
        else
        {
            var stored = await context.Customers.SingleAsync(item => item.Id == customer.Id, cancellationToken);
            CopyCustomerFields(customer, stored);
            var existingLicences = await context.CustomerLicences
                .Where(item => item.CustomerId == customer.Id)
                .ToListAsync(cancellationToken);
            var submittedIds = licences.Select(item => item.Id).ToHashSet();
            foreach (var licence in existingLicences.Where(item => !submittedIds.Contains(item.Id)))
            {
                context.CustomerLicences.Remove(licence);
            }

            foreach (var licence in licences)
            {
                var existing = existingLicences.SingleOrDefault(item => item.Id == licence.Id);
                if (existing is null)
                {
                    licence.CustomerId = customer.Id;
                    context.CustomerLicences.Add(licence);
                }
                else
                {
                    CopyLicenceFields(licence, existing);
                }
            }
        }

        if (isNew)
        {
            foreach (var licence in licences)
            {
                licence.CustomerId = customer.Id;
                context.CustomerLicences.Add(licence);
            }
        }

        await SaveLicenceTypesAsync(
            configuredLicenceTypes.Concat(licences.Select(item => item.LicenceType)),
            cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = DateTime.UtcNow,
            Action = isNew ? "WholesaleCustomerCreated" : "WholesaleCustomerUpdated",
            EntityName = nameof(Customer),
            EntityId = customer.Id,
            Details = $"Customer {customer.Name}; licences: {licences.Count}; status: {(customer.IsActive ? "Active" : "Blocked")}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WholesaleCustomerImportResult> ImportAsync(
        IReadOnlyCollection<WholesaleCustomerImportRecord> imports,
        IReadOnlyCollection<string> configuredLicenceTypes,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var context = unitOfWork.Context;
        var existingCustomers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var existingLicences = await context.CustomerLicences.AsNoTracking().ToListAsync(cancellationToken);
        var accepted = new List<WholesaleCustomerImportRecord>();
        var errors = new List<string>();
        var skippedDuplicates = 0;

        foreach (var import in imports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(import.Customer.Name);
                ArgumentException.ThrowIfNullOrWhiteSpace(import.Customer.BuyerType);
                ValidateCustomer(import.Customer);
                ValidateLicences(import.Licences);
            }
            catch (ArgumentException exception)
            {
                errors.Add($"{import.Customer.Name}: {exception.Message}");
                continue;
            }

            var duplicate = existingCustomers.Any(existing =>
                IsDuplicateCustomer(existing, import.Customer)) ||
                existingLicences.Any(existing =>
                    import.Licences.Any(candidate =>
                        Normalize(existing.LicenceNumber) == Normalize(candidate.LicenceNumber))) ||
                accepted.Any(existing =>
                    IsDuplicateCustomer(existing.Customer, import.Customer) ||
                    existing.Licences.Any(existingLicence =>
                        import.Licences.Any(candidate =>
                            Normalize(existingLicence.LicenceNumber) == Normalize(candidate.LicenceNumber))));
            if (duplicate)
            {
                skippedDuplicates++;
                continue;
            }

            accepted.Add(import);
            existingCustomers.Add(import.Customer);
            existingLicences.AddRange(import.Licences);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var import in accepted)
        {
            context.Customers.Add(import.Customer);
            foreach (var licence in import.Licences)
            {
                licence.CustomerId = import.Customer.Id;
                context.CustomerLicences.Add(licence);
            }
        }

        await SaveLicenceTypesAsync(
            configuredLicenceTypes.Concat(accepted.SelectMany(item => item.Licences).Select(item => item.LicenceType)),
            cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = DateTime.UtcNow,
            Action = "WholesaleCustomersImported",
            EntityName = nameof(Customer),
            Details = $"Imported {accepted.Count}; skipped duplicates {skippedDuplicates}; invalid rows {errors.Count}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WholesaleCustomerImportResult(accepted.Count, skippedDuplicates, errors);
    }

    private async Task EnsureNoDuplicatesAsync(
        Customer customer,
        IReadOnlyCollection<CustomerLicence> licences,
        CancellationToken cancellationToken)
    {
        var otherCustomers = await unitOfWork.Context.Customers
            .AsNoTracking()
            .Where(item => item.Id != customer.Id)
            .ToListAsync(cancellationToken);
        var customerDuplicate = otherCustomers.FirstOrDefault(item => IsDuplicateCustomer(item, customer));
        if (customerDuplicate is not null)
        {
            throw new InvalidOperationException(
                $"Possible duplicate customer: {customerDuplicate.Name} has the same GSTIN.");
        }

        var licenceNumbers = licences
            .Select(licence => Normalize(licence.LicenceNumber))
            .Where(number => number.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherLicenceNumbers = await unitOfWork.Context.CustomerLicences
            .AsNoTracking()
            .Where(item => item.CustomerId != customer.Id)
            .Select(item => item.LicenceNumber)
            .ToListAsync(cancellationToken);
        var duplicateLicence = otherLicenceNumbers.FirstOrDefault(number =>
            licenceNumbers.Contains(Normalize(number)));
        if (duplicateLicence is not null)
        {
            throw new InvalidOperationException($"Possible duplicate licence number: {duplicateLicence}.");
        }
    }

    private async Task SaveLicenceTypesAsync(
        IEnumerable<string> configuredLicenceTypes,
        CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
        var merged = JsonSerializer.Deserialize<string[]>(profile.WholesaleLicenceTypesJson) ?? [];
        profile.WholesaleLicenceTypesJson = JsonSerializer.Serialize(
            merged.Concat(configuredLicenceTypes)
                .Select(type => type.Trim())
                .Where(type => type.Length > 0 && !FixedLicenceTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void ValidateCustomer(Customer customer)
    {
        if (customer.CreditLimit < 0 || customer.CreditDays < 0)
        {
            throw new ArgumentException("Credit limit and credit days cannot be negative.");
        }
    }

    private static void ValidateLicences(IEnumerable<CustomerLicence> licences)
    {
        var items = licences.ToArray();
        if (items.Any(licence => string.IsNullOrWhiteSpace(licence.LicenceType) ||
                                 string.IsNullOrWhiteSpace(licence.LicenceNumber) ||
                                 !licence.IssuedOn.HasValue ||
                                 !licence.ExpiresOn.HasValue))
        {
            throw new ArgumentException("Each licence needs a type, number, issue date and expiry date.");
        }

        if (items.Any(licence => licence.ExpiresOn < licence.IssuedOn))
        {
            throw new ArgumentException("A licence expiry date cannot be earlier than its issue date.");
        }

        if (items.Select(licence => Normalize(licence.LicenceNumber))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
        {
            throw new ArgumentException("A customer cannot have the same licence number more than once.");
        }
    }

    private static bool IsDuplicateCustomer(Customer left, Customer right) =>
        !string.IsNullOrWhiteSpace(left.Gstin) &&
        Normalize(left.Gstin) == Normalize(right.Gstin);

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static int AlertPriority(CustomerLicenceExpiryAlert alert) => alert switch
    {
        CustomerLicenceExpiryAlert.Expired => 3,
        CustomerLicenceExpiryAlert.ExpiringWithin30Days => 2,
        CustomerLicenceExpiryAlert.ExpiringWithin60Days => 1,
        _ => 0
    };

    private static void CopyCustomerFields(Customer source, Customer target)
    {
        target.Name = source.Name.Trim();
        target.Phone = source.Phone;
        target.Email = source.Email;
        target.Address = source.Address;
        target.Gstin = source.Gstin;
        target.BuyerType = source.BuyerType;
        target.IsActive = source.IsActive;
        target.CreditLimit = source.CreditLimit;
        target.CreditDays = source.CreditDays;
        target.PriceCategory = source.PriceCategory;
        target.Route = source.Route;
        target.Salesman = source.Salesman;
        target.OpeningBalance = source.OpeningBalance;
        target.State = source.State;
        target.StateDrugControlPortalUrl = source.StateDrugControlPortalUrl;
        target.VerifiedBy = source.VerifiedBy;
        target.VerifiedOnUtc = source.VerifiedOnUtc;
        target.VerificationMethod = source.VerificationMethod;
    }

    private static void CopyLicenceFields(CustomerLicence source, CustomerLicence target)
    {
        target.LicenceNumber = source.LicenceNumber.Trim();
        target.LicenceType = source.LicenceType.Trim();
        target.IssuedOn = source.IssuedOn;
        target.ExpiresOn = source.ExpiresOn;
        target.IssuingAuthority = source.IssuingAuthority;
        target.DocumentPath = source.DocumentPath;
        target.Authorisation = source.Authorisation;
        target.Notes = source.Notes;
    }
}
