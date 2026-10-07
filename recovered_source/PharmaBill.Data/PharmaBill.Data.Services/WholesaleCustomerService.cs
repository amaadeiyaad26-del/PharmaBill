using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesaleCustomerService(IUnitOfWork unitOfWork)
{
	private static readonly string[] FixedLicenceTypes = new string[2] { "20", "21" };

	public async Task<IReadOnlyList<string>> GetLicenceTypesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string[] second = JsonSerializer.Deserialize<string[]>((await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken)).WholesaleLicenceTypesJson) ?? Array.Empty<string>();
		return (from type in FixedLicenceTypes.Concat(second)
			where !string.IsNullOrWhiteSpace(type)
			select type).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string type) => type, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public async Task SaveBuyerLicenceRulesAsync(IReadOnlyDictionary<string, IReadOnlyCollection<string>> rules, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (rules.Count == 0 || rules.Any((KeyValuePair<string, IReadOnlyCollection<string>> rule) => string.IsNullOrWhiteSpace(rule.Key) || rule.Value.Count == 0 || rule.Value.Any(string.IsNullOrWhiteSpace)))
		{
			throw new InvalidOperationException("Configure at least one allowed licence type for every buyer type.");
		}
		PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
		Dictionary<string, string[]> dictionary = JsonSerializer.Deserialize<Dictionary<string, string[]>>(pharmacyProfile.WholesaleBuyerLicenceRulesJson) ?? new Dictionary<string, string[]>();
		foreach (KeyValuePair<string, IReadOnlyCollection<string>> rule in rules)
		{
			dictionary[rule.Key.Trim()] = (dictionary.TryGetValue(rule.Key, out var value) ? (from type in value.Concat(rule.Value)
				select type.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : rule.Value.Select((string type) => type.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
		}
		pharmacyProfile.WholesaleBuyerLicenceRulesJson = JsonSerializer.Serialize(dictionary);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetBuyerLicenceRulesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return ((IEnumerable<KeyValuePair<string, string[]>>)(JsonSerializer.Deserialize<Dictionary<string, string[]>>((await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken)).WholesaleBuyerLicenceRulesJson) ?? new Dictionary<string, string[]>())).ToDictionary((Func<KeyValuePair<string, string[]>, string>)((KeyValuePair<string, string[]> pair) => pair.Key), (Func<KeyValuePair<string, string[]>, IReadOnlyCollection<string>>)((KeyValuePair<string, string[]> pair) => pair.Value), (IEqualityComparer<string>?)StringComparer.OrdinalIgnoreCase);
	}

	public async Task<IReadOnlyList<WholesaleCustomerRecord>> GetCustomersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		List<Customer> customers = await (from customer in context.Customers.AsNoTracking()
			orderby customer.Name
			select customer).ToListAsync(cancellationToken);
		Guid[] customerIds = customers.Select((Customer customer) => customer.Id).ToArray();
		List<CustomerLicence> licences = await (from licence in context.CustomerLicences.AsNoTracking()
			where customerIds.Contains(licence.CustomerId)
			orderby licence.ExpiresOn
			select licence).ToListAsync(cancellationToken);
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		return customers.Select((Customer customer) =>
		{
			CustomerLicence[] array = licences.Where((CustomerLicence licence) => licence.CustomerId == customer.Id).ToArray();
			CustomerLicenceExpiryAlert expiryAlert = array.Select((CustomerLicence licence) => CustomerLicenceExpiryAlerts.Evaluate(licence.ExpiresOn, today)).OrderByDescending(AlertPriority).FirstOrDefault();
			return new WholesaleCustomerRecord(customer, array, expiryAlert);
		}).ToArray();
	}

	public async Task SaveAsync(Customer customer, IReadOnlyCollection<CustomerLicence> licences, IReadOnlyCollection<string> configuredLicenceTypes, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(customer, "customer");
		ArgumentNullException.ThrowIfNull(licences, "licences");
		ArgumentException.ThrowIfNullOrWhiteSpace(customer.Name, "customer.Name");
		ArgumentException.ThrowIfNullOrWhiteSpace(customer.BuyerType, "customer.BuyerType");
		ValidateCustomer(customer);
		ValidateLicences(licences);
		await EnsureNoDuplicatesAsync(customer, licences, cancellationToken);
		PharmaBillDbContext context = unitOfWork.Context;
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		bool isNew = !(await context.Customers.AnyAsync((Customer item) => item.Id == customer.Id, cancellationToken));
		if (isNew)
		{
			context.Customers.Add(customer);
		}
		else
		{
			Customer target = await context.Customers.SingleAsync((Customer item) => item.Id == customer.Id, cancellationToken);
			CopyCustomerFields(customer, target);
			List<CustomerLicence> source = await context.CustomerLicences.Where((CustomerLicence item) => item.CustomerId == customer.Id).ToListAsync(cancellationToken);
			HashSet<Guid> submittedIds = licences.Select((CustomerLicence item) => item.Id).ToHashSet();
			foreach (CustomerLicence item in source.Where((CustomerLicence item) => !submittedIds.Contains(item.Id)))
			{
				context.CustomerLicences.Remove(item);
			}
			foreach (CustomerLicence licence in licences)
			{
				CustomerLicence customerLicence = source.SingleOrDefault((CustomerLicence item) => item.Id == licence.Id);
				if (customerLicence == null)
				{
					licence.CustomerId = customer.Id;
					context.CustomerLicences.Add(licence);
				}
				else
				{
					CopyLicenceFields(licence, customerLicence);
				}
			}
		}
		if (isNew)
		{
			foreach (CustomerLicence licence2 in licences)
			{
				licence2.CustomerId = customer.Id;
				context.CustomerLicences.Add(licence2);
			}
		}
		await SaveLicenceTypesAsync(configuredLicenceTypes.Concat(licences.Select((CustomerLicence item) => item.LicenceType)), cancellationToken);
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			ActionAtUtc = DateTime.UtcNow,
			Action = (isNew ? "WholesaleCustomerCreated" : "WholesaleCustomerUpdated"),
			EntityName = "Customer",
			EntityId = customer.Id,
			Details = $"Customer {customer.Name}; licences: {licences.Count}; status: {(customer.IsActive ? "Active" : "Blocked")}."
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<WholesaleCustomerImportResult> ImportAsync(IReadOnlyCollection<WholesaleCustomerImportRecord> imports, IReadOnlyCollection<string> configuredLicenceTypes, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		List<Customer> existingCustomers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
		List<CustomerLicence> list = await context.CustomerLicences.AsNoTracking().ToListAsync(cancellationToken);
		List<WholesaleCustomerImportRecord> accepted = new List<WholesaleCustomerImportRecord>();
		List<string> errors = new List<string>();
		int skippedDuplicates = 0;
		foreach (WholesaleCustomerImportRecord import in imports)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(import.Customer.Name, "import.Customer.Name");
				ArgumentException.ThrowIfNullOrWhiteSpace(import.Customer.BuyerType, "import.Customer.BuyerType");
				ValidateCustomer(import.Customer);
				ValidateLicences(import.Licences);
			}
			catch (ArgumentException ex)
			{
				errors.Add(import.Customer.Name + ": " + ex.Message);
				continue;
			}
			if (existingCustomers.Any((Customer existing) => IsDuplicateCustomer(existing, import.Customer)) || list.Any((CustomerLicence existing) => import.Licences.Any((CustomerLicence candidate) => Normalize(existing.LicenceNumber) == Normalize(candidate.LicenceNumber))) || accepted.Any((WholesaleCustomerImportRecord existing) => IsDuplicateCustomer(existing.Customer, import.Customer) || existing.Licences.Any((CustomerLicence existingLicence) => import.Licences.Any((CustomerLicence candidate) => Normalize(existingLicence.LicenceNumber) == Normalize(candidate.LicenceNumber)))))
			{
				skippedDuplicates++;
				continue;
			}
			accepted.Add(import);
			existingCustomers.Add(import.Customer);
			list.AddRange(import.Licences);
		}
		WholesaleCustomerImportResult result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			foreach (WholesaleCustomerImportRecord item in accepted)
			{
				context.Customers.Add(item.Customer);
				foreach (CustomerLicence licence in item.Licences)
				{
					licence.CustomerId = item.Customer.Id;
					context.CustomerLicences.Add(licence);
				}
			}
			await SaveLicenceTypesAsync(configuredLicenceTypes.Concat(from item in accepted.SelectMany((WholesaleCustomerImportRecord item) => item.Licences)
				select item.LicenceType), cancellationToken);
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = DateTime.UtcNow,
				Action = "WholesaleCustomersImported",
				EntityName = "Customer",
				Details = $"Imported {accepted.Count}; skipped duplicates {skippedDuplicates}; invalid rows {errors.Count}."
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = new WholesaleCustomerImportResult(accepted.Count, skippedDuplicates, errors);
		}
		return result;
	}

	private async Task EnsureNoDuplicatesAsync(Customer customer, IReadOnlyCollection<CustomerLicence> licences, CancellationToken cancellationToken)
	{
		Customer customer2 = (await (from item in unitOfWork.Context.Customers.AsNoTracking()
			where item.Id != customer.Id
			select item).ToListAsync(cancellationToken)).FirstOrDefault((Customer item) => IsDuplicateCustomer(item, customer));
		if (customer2 != null)
		{
			throw new InvalidOperationException("Possible duplicate customer: " + customer2.Name + " has the same GSTIN.");
		}
		HashSet<string> licenceNumbers = (from licence in licences
			select Normalize(licence.LicenceNumber) into number
			where number.Length > 0
			select number).ToHashSet(StringComparer.OrdinalIgnoreCase);
		string text = (await (from item in unitOfWork.Context.CustomerLicences.AsNoTracking()
			where item.CustomerId != customer.Id
			select item.LicenceNumber).ToListAsync(cancellationToken)).FirstOrDefault((string number) => licenceNumbers.Contains(Normalize(number)));
		if (text != null)
		{
			throw new InvalidOperationException("Possible duplicate licence number: " + text + ".");
		}
	}

	private async Task SaveLicenceTypesAsync(IEnumerable<string> configuredLicenceTypes, CancellationToken cancellationToken)
	{
		PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
		string[] first = JsonSerializer.Deserialize<string[]>(pharmacyProfile.WholesaleLicenceTypesJson) ?? Array.Empty<string>();
		pharmacyProfile.WholesaleLicenceTypesJson = JsonSerializer.Serialize((from type in first.Concat(configuredLicenceTypes)
			select type.Trim() into type
			where type.Length > 0 && !FixedLicenceTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
			select type).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string type) => type, StringComparer.OrdinalIgnoreCase).ToArray());
	}

	private static void ValidateCustomer(Customer customer)
	{
		if (customer.CreditLimit < 0m || customer.CreditDays < 0)
		{
			throw new ArgumentException("Credit limit and credit days cannot be negative.");
		}
	}

	private static void ValidateLicences(IEnumerable<CustomerLicence> licences)
	{
		CustomerLicence[] array = licences.ToArray();
		if (array.Any((CustomerLicence licence) => string.IsNullOrWhiteSpace(licence.LicenceType) || string.IsNullOrWhiteSpace(licence.LicenceNumber) || !licence.IssuedOn.HasValue || !licence.ExpiresOn.HasValue))
		{
			throw new ArgumentException("Each licence needs a type, number, issue date and expiry date.");
		}
		if (array.Any((CustomerLicence licence) => licence.ExpiresOn < licence.IssuedOn))
		{
			throw new ArgumentException("A licence expiry date cannot be earlier than its issue date.");
		}
		if (array.Select((CustomerLicence licence) => Normalize(licence.LicenceNumber)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != array.Length)
		{
			throw new ArgumentException("A customer cannot have the same licence number more than once.");
		}
	}

	private static bool IsDuplicateCustomer(Customer left, Customer right)
	{
		if (!string.IsNullOrWhiteSpace(left.Gstin))
		{
			return Normalize(left.Gstin) == Normalize(right.Gstin);
		}
		return false;
	}

	private static string Normalize(string? value)
	{
		return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
	}

	private static int AlertPriority(CustomerLicenceExpiryAlert alert)
	{
		return alert switch
		{
			CustomerLicenceExpiryAlert.Expired => 3, 
			CustomerLicenceExpiryAlert.ExpiringWithin30Days => 2, 
			CustomerLicenceExpiryAlert.ExpiringWithin60Days => 1, 
			_ => 0, 
		};
	}

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
