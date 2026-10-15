using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>Stores buyer-type synonyms (Wholesaler, Stockist…) as their canonical type and audits every remap.</summary>
public static class BuyerTypeRemapping
{
	public const string CustomerRemapAction = "CustomerBuyerTypeRemapped";

	public const string RuleRemapAction = "BuyerLicenceRuleRemapped";

	/// <summary>Canonicalises <paramref name="customer"/>.BuyerType; adds an audit entry when the stored value changes.</summary>
	public static bool Apply(PharmaBillDbContext context, Customer customer, Guid? userId, string reason)
	{
		string? original = customer.BuyerType;
		string? canonical = BuyerTypes.Canonical(original);
		if (string.Equals(original, canonical, StringComparison.Ordinal))
		{
			return false;
		}
		customer.BuyerType = canonical;
		// Whitespace tidy-ups and the picker's own "Distributor / Wholesaler" label are not synonym remaps.
		if (string.Equals(original?.Trim(), canonical, StringComparison.Ordinal)
			|| string.Equals(original?.Trim(), BuyerTypes.DistributorLabel, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			ActionAtUtc = DateTime.UtcNow,
			Action = CustomerRemapAction,
			EntityName = "Customer",
			EntityId = customer.Id,
			AuthorizedBy = userId.HasValue ? null : "System",
			Reason = reason,
			Details = $"Customer {customer.Name}: buyer type '{original}' stored as '{canonical}'."
		});
		return true;
	}

	/// <summary>One-time, idempotent startup clean-up of existing customers and licence rules.</summary>
	public static async Task<int> NormalizeExistingAsync(PharmaBillDbContext context, CancellationToken cancellationToken = default)
	{
		int changes = 0;
		List<Customer> customers = (await context.Customers.Where(customer => customer.BuyerType != null).ToListAsync(cancellationToken))
			.Where(customer => BuyerTypes.IsAlias(customer.BuyerType))
			.ToList();
		foreach (Customer customer in customers)
		{
			Apply(context, customer, userId: null, reason: "Buyer-type synonyms merged into one type");
		}
		changes += customers.Count;
		foreach (PharmacyProfile profile in await context.PharmacyProfiles.ToListAsync(cancellationToken))
		{
			(string json, IReadOnlyList<string> ruleChanges) = BuyerLicenceRules.MergeAliasRules(profile.WholesaleBuyerLicenceRulesJson);
			if (ruleChanges.Count == 0)
			{
				continue;
			}
			profile.WholesaleBuyerLicenceRulesJson = json;
			foreach (string change in ruleChanges)
			{
				context.AuditLogs.Add(new AuditLog
				{
					ActionAtUtc = DateTime.UtcNow,
					Action = RuleRemapAction,
					EntityName = "PharmacyProfile",
					EntityId = profile.Id,
					AuthorizedBy = "System",
					Reason = "Buyer-type synonyms merged into one type",
					Details = change
				});
				changes++;
			}
		}
		if (changes > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		return changes;
	}
}
