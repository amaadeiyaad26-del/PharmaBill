using System.Globalization;
using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

/// <summary>A bill was read, but what was read does not agree with the totals printed on it. Nothing is shown.</summary>
public sealed class InvoiceNotReconciledException(string message) : InvalidOperationException(message);

/// <summary>
/// Checks a read bill against the totals printed on it: the lines must add up to the printed taxable value, their GST
/// to the printed tax, and both to the grand total. A reading that fails is refused, never shown as a draft the user
/// might accept: a half-read bill is worse than none.
/// </summary>
public static class InvoiceReconciliation
{
	/// <summary>The largest difference accepted between a sum and its printed figure (paise rounding on each line).</summary>
	public static decimal Tolerance(int lines) => 1m + 0.02m * lines;

	private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

	private static string Rs(decimal value) => "₹" + value.ToString("N2", India);

	/// <summary>Why this reading cannot be trusted; empty when it agrees with every total printed on the bill.</summary>
	public static IReadOnlyList<string> Problems(ExtractedPurchaseInvoice invoice)
	{
		List<string> problems = new List<string>();
		IReadOnlyList<ExtractedPurchaseLine> items = invoice.Items ?? [];
		if (items.Count == 0)
		{
			problems.Add("no item lines were read");
			return problems;
		}

		InvoicePrintedTotals? printed = invoice.Printed;
		if (printed == null || !printed.HasAny)
		{
			problems.Add("the bill's printed taxable value and grand total could not be found, so the lines cannot be checked");
			return problems;
		}

		for (int index = 0; index < items.Count; index++)
		{
			ExtractedPurchaseLine line = items[index];
			string which = "line " + (index + 1) + " (" + Short(line.ItemName) + ")";
			if (line.Amount <= 0m || line.Quantity <= 0m || line.Rate <= 0m)
			{
				problems.Add(which + " has no quantity, rate or amount");
				continue;
			}
			decimal gross = decimal.Round(line.Quantity * line.Rate, 2, MidpointRounding.AwayFromZero);
			// A discount can make the amount less than quantity × rate; nothing makes it more.
			if (line.Amount > gross + Math.Max(0.05m, gross * 0.005m))
			{
				problems.Add($"{which}: {line.Quantity:0.##} × {Rs(line.Rate)} is {Rs(gross)}, but its amount reads {Rs(line.Amount)}");
			}
			if (GstRates.Problem(line.Gst) != null)
			{
				problems.Add($"{which} has GST {line.Gst:0.##}%, which is not a GST rate");
			}
		}

		decimal lineTotal = items.Sum(line => line.Amount);
		decimal tolerance = Tolerance(items.Count);
		if (printed.Taxable is decimal taxable && Math.Abs(lineTotal - taxable) > tolerance)
		{
			problems.Add($"the {items.Count} lines read add up to {Rs(lineTotal)}, but the bill's printed taxable value is {Rs(taxable)}");
		}

		decimal lineTax = items.Sum(line => decimal.Round(line.Amount * line.Gst / 100m, 2, MidpointRounding.AwayFromZero));
		if (printed.Tax is decimal tax && Math.Abs(lineTax - tax) > tolerance)
		{
			problems.Add($"GST on the lines read is {Rs(lineTax)}, but the bill's printed GST is {Rs(tax)}");
		}

		if (printed.GrandTotal is decimal grand)
		{
			decimal expected = (printed.Taxable ?? lineTotal) + (printed.Tax ?? lineTax) + (printed.RoundOff ?? 0m);
			// Without a printed round-off line the bill may still have rounded to the rupee.
			decimal allowed = printed.RoundOff.HasValue ? tolerance : tolerance + 0.5m;
			if (Math.Abs(expected - grand) > allowed)
			{
				problems.Add($"taxable value plus GST is {Rs(expected)}, but the bill's printed grand total is {Rs(grand)}");
			}
		}

		return problems;
	}

	/// <summary>Throws <see cref="InvoiceNotReconciledException"/> naming what does not agree.</summary>
	public static void Ensure(ExtractedPurchaseInvoice invoice, string? readBy = null)
	{
		IReadOnlyList<string> problems = Problems(invoice);
		if (problems.Count > 0)
		{
			throw new InvoiceNotReconciledException(Refusal(problems, readBy ?? invoice.ReadBy));
		}
	}

	public static string Refusal(IReadOnlyList<string> problems, string? readBy) =>
		"This bill was not loaded: what was read does not match the totals printed on it, so it cannot be trusted. "
		+ Capitalise(string.Join("; ", problems.Take(3))) + (problems.Count > 3 ? $"; and {problems.Count - 3} more." : ".")
		+ (string.IsNullOrWhiteSpace(readBy) ? string.Empty : " " + readBy.Trim().TrimEnd('.') + ".")
		+ " Nothing was put on the purchase grid. Try a clearer scan or the supplier's PDF, or enter the bill by hand.";

	private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

	private static string Short(string name)
	{
		string trimmed = (name ?? string.Empty).Trim();
		return trimmed.Length <= 40 ? trimmed : trimmed[..40] + "…";
	}
}
