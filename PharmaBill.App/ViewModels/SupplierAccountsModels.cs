using System;
using PharmaBill.App.Services;
using PharmaBill.Core.Accounting;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class SupplierLedgerDisplayRow
{
	public DateTime EntryAtUtc { get; init; }

	public string EntryType { get; init; } = string.Empty;

	public string EntryTypeLabel { get; init; } = string.Empty;

	public string? ReferenceNo { get; init; }

	public decimal Debit { get; init; }

	public decimal Credit { get; init; }

	public decimal Balance { get; init; }

	public string? Notes { get; init; }

	public string DateText => EntryAtUtc == DateTime.MinValue
		? "Opening"
		: EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");

	public string DebitText => Debit > 0m ? MoneyFormat.Rupees(Debit) : "—";

	public string CreditText => Credit > 0m ? MoneyFormat.Rupees(Credit) : "—";

	public string BalanceText => MoneyFormat.Rupees(Balance);

	public bool IsPayableIncrease => Credit > 0m && Debit == 0m;

	public bool IsPayment => Debit > 0m && Credit == 0m;

	public static SupplierLedgerDisplayRow From(LedgerRow row)
	{
		return new SupplierLedgerDisplayRow
		{
			EntryAtUtc = row.EntryAtUtc,
			EntryType = row.EntryType,
			EntryTypeLabel = FormatEntryType(row.EntryType),
			ReferenceNo = row.ReferenceNo,
			Debit = decimal.Round(row.Debit, 2, MidpointRounding.AwayFromZero),
			Credit = decimal.Round(row.Credit, 2, MidpointRounding.AwayFromZero),
			Balance = decimal.Round(row.Balance, 2, MidpointRounding.AwayFromZero),
			Notes = row.Notes
		};
	}

	private static string FormatEntryType(string entryType)
	{
		if (LedgerEntryTypes.IsPurchaseBill(entryType))
		{
			return "Purchase Bill";
		}

		return entryType switch
		{
			LedgerEntryTypes.SupplierPayment => "Payment Voucher",
			LedgerEntryTypes.PurchaseReturn => "Purchase Return",
			_ => string.IsNullOrWhiteSpace(entryType) ? "Entry" : entryType
		};
	}
}

public sealed class SupplierPayableAgeingRow
{
	public Guid SupplierId { get; init; }

	public string SupplierName { get; init; } = string.Empty;

	public decimal Current0To30 { get; init; }

	public decimal Days31To60 { get; init; }

	public decimal Days61To90 { get; init; }

	public decimal Over90 { get; init; }

	public decimal Total { get; init; }

	public string Current0To30Text => MoneyFormat.Rupees(Current0To30);

	public string Days31To60Text => MoneyFormat.Rupees(Days31To60);

	public string Days61To90Text => MoneyFormat.Rupees(Days61To90);

	public string Over90Text => MoneyFormat.Rupees(Over90);

	public string TotalText => MoneyFormat.Rupees(Total);
}
