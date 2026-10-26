using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Customer State holds only a canonical GST state name; anything else typed there (a city such as
/// "SRINAGAR") belongs in Area. Older free-text "State / Area" values are split with these rules.
/// </summary>
public static class CustomerStateArea
{
	private static readonly HashSet<string> CanonicalNames = IndianGstStateCodes.States.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

	/// <summary>A recognised state maps to its canonical name; unrecognised text moves to Area and State is left blank.</summary>
	public static (string? State, string? Area) Split(string? stateText, string? area)
	{
		string? text = string.IsNullOrWhiteSpace(stateText) ? null : stateText.Trim();
		string? currentArea = string.IsNullOrWhiteSpace(area) ? null : area.Trim();
		if (text == null)
		{
			return (null, currentArea);
		}
		GstStateResolution resolution = IndianGstStateCodes.ResolveForTax(text, null);
		if (resolution.IsResolved)
		{
			return (resolution.CanonicalName, currentArea);
		}
		if (currentArea == null)
		{
			return (null, text);
		}
		if (string.Equals(currentArea, text, StringComparison.OrdinalIgnoreCase))
		{
			return (null, currentArea);
		}
		return (null, currentArea + ", " + text);
	}

	/// <summary>One-time, idempotent split of legacy State values. Rows already holding a canonical name are untouched.</summary>
	public static async Task<int> NormalizeExistingAsync(PharmaBillDbContext context, CancellationToken cancellationToken = default)
	{
		List<Customer> candidates = (await context.Customers.Where(customer => customer.State != null).ToListAsync(cancellationToken))
			.Where(customer => !CanonicalNames.Contains(customer.State!))
			.ToList();
		foreach (Customer customer in candidates)
		{
			(customer.State, customer.Area) = Split(customer.State, customer.Area);
		}
		if (candidates.Count > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		return candidates.Count;
	}
}
