using System;
using System.Globalization;
using System.Linq;

namespace PharmaBill.Core.Ai;

public static class AssistantQueryInterpreter
{
	public const string UnrecognisedMessage = "Try: sales today, sales this month, expiring batches, low stock, top selling, gst, profit, h1 sales, backup.";

	public static AssistantAction Interpret(string? query, DateOnly today)
	{
		string text = (query ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
		if (text.Length == 0)
		{
			return new AssistantAction(Recognised: false, "Try: sales today, sales this month, expiring batches, low stock, top selling, gst, profit, h1 sales, backup.");
		}
		if (HasAny(text, "h1", "schedule h", "schedule x", "ndps", "narcotic", "register"))
		{
			return new AssistantAction(Recognised: true, "Opening statutory registers (Schedule H1 / X / NDPS).", "Registers");
		}
		if (HasAny(text, "expir", "near expiry", "short expiry"))
		{
			return Report("Expiry", "batches expiring in the next 90 days", today, today.AddDays(90));
		}
		if (HasAny(text, "low stock", "reorder", "out of stock", "running out", "shortage"))
		{
			return Report("LowStock", "low-stock medicines", today, today);
		}
		var (dateOnly, dateOnly2, text2) = ResolvePeriod(text, today);
		if (HasAny(text, "top sell", "best sell", "fast moving", "fastest", "most sold", "top medicine"))
		{
			DateOnly dateOnly3;
			DateOnly to;
			string text3;
			if (!HasPeriodWord(text))
			{
				(dateOnly3, to, text3) = MonthToDate(today);
			}
			else
			{
				DateOnly dateOnly4 = dateOnly2;
				string text4 = text2;
				text3 = text4;
				to = dateOnly4;
				dateOnly3 = dateOnly;
			}
			return Report("TopSellingDrugs", "top-selling medicines, " + text3, dateOnly3, to);
		}
		if (HasAny(text, "gst", "tax"))
		{
			return Report("GstSummary", "GST summary, " + text2, dateOnly, dateOnly2);
		}
		if (HasAny(text, "profit", "margin"))
		{
			return Report("Profit", "profit, " + text2, dateOnly, dateOnly2);
		}
		if (HasAny(text, "supplier"))
		{
			return Report("SupplierWise", "supplier-wise purchases, " + text2, dateOnly, dateOnly2);
		}
		if (HasAny(text, "purchase", "bought"))
		{
			return Report("Purchases", "purchases, " + text2, dateOnly, dateOnly2);
		}
		if (HasAny(text, "sale", "sales", "sold", "revenue", "turnover"))
		{
			return Report("Sales", "sales, " + text2, dateOnly, dateOnly2);
		}
		if (HasAny(text, "dashboard", "home", "overview"))
		{
			return Section("Dashboard", "Opening the dashboard.");
		}
		if (HasAny(text, "new bill", "billing", "retail bill"))
		{
			return Section("Billing", "Opening retail billing.");
		}
		if (HasAny(text, "wholesale"))
		{
			return Section("WholesaleBilling", "Opening wholesale billing.");
		}
		if (HasAny(text, "stock", "inventory"))
		{
			return Section("Stock", "Opening stock.");
		}
		if (HasAny(text, "customer", "buyer"))
		{
			return Section("Customers", "Opening customers.");
		}
		if (HasAny(text, "backup", "restore"))
		{
			return Section("Backup", "Opening backup.");
		}
		if (HasAny(text, "sync"))
		{
			return Section("Sync", "Opening sync.");
		}
		if (HasAny(text, "setting"))
		{
			return Section("Settings", "Opening settings.");
		}
		return new AssistantAction(Recognised: false, "Try: sales today, sales this month, expiring batches, low stock, top selling, gst, profit, h1 sales, backup.");
	}

	private static AssistantAction Report(string key, string what, DateOnly from, DateOnly to)
	{
		return new AssistantAction(Recognised: true, "Showing " + what + ".", "Reports", key, from, to);
	}

	private static AssistantAction Section(string key, string message)
	{
		return new AssistantAction(Recognised: true, message, key);
	}

	private static bool HasAny(string text, params string[] needles)
	{
		return needles.Any((string needle) => text.Contains(needle, StringComparison.Ordinal));
	}

	private static bool HasPeriodWord(string text)
	{
		return HasAny(text, "today", "yesterday", "week", "month", "year");
	}

	private static (DateOnly From, DateOnly To, string Label) MonthToDate(DateOnly today)
	{
		return (From: new DateOnly(today.Year, today.Month, 1), To: today, Label: "this month");
	}

	private static (DateOnly From, DateOnly To, string Label) ResolvePeriod(string text, DateOnly today)
	{
		if (text.Contains("yesterday", StringComparison.Ordinal))
		{
			return (From: today.AddDays(-1), To: today.AddDays(-1), Label: "yesterday");
		}
		if (text.Contains("today", StringComparison.Ordinal))
		{
			return (From: today, To: today, Label: "today");
		}
		if (text.Contains("last month", StringComparison.Ordinal))
		{
			DateOnly item = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
			return (From: item, To: item.AddMonths(1).AddDays(-1), Label: "last month");
		}
		if (text.Contains("week", StringComparison.Ordinal))
		{
			return (From: today.AddDays(-6), To: today, Label: "last 7 days");
		}
		if (text.Contains("year", StringComparison.Ordinal))
		{
			return (From: new DateOnly(today.Year, 1, 1), To: today, Label: "this year");
		}
		return MonthToDate(today);
	}
}
