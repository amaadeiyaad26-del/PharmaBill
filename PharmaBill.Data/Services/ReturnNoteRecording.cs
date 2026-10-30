using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Tax-document rule for credit and debit notes, as for invoices (see <see cref="RecordedGstStates"/>): what the note
/// says (item, HSN, rate, amounts and tax split; party, pharmacy, state codes and supply type; its date) is captured on
/// the note when it is saved, and every report reads it back from there. Nothing re-resolves it from the product, party
/// or tax master afterwards, so editing a product's GST rate or a party's GSTIN never changes a note already issued.
///
/// The same rules fill notes saved before this was recorded (<see cref="ReturnNoteValuesBackfill"/>): values are copied
/// from the original invoice they adjust, which is what was charged; only what that invoice never held comes from
/// current master data, and the row is then flagged <c>ValuesBackfilled</c>.
/// </summary>
internal static class ReturnNoteRecording
{
	/// <summary>Notes whether any value had to come from current master data rather than the original document.</summary>
	internal sealed class Source
	{
		public bool FromMaster { get; private set; }

		/// <summary>
		/// The original document's value. When that document recorded its values (<paramref name="documentRecorded"/>),
		/// its value is what was charged, even when it is empty; only a document saved before recording falls back to
		/// the live value, and that marks the row.
		/// </summary>
		public string From(bool documentRecorded, string? recorded, string? live) =>
			documentRecorded ? recorded?.Trim() ?? string.Empty : Text(recorded, live);

		/// <summary>The recorded value when there is one; otherwise the live one (marking the row), else "".</summary>
		public string Text(string? recorded, string? live)
		{
			if (!string.IsNullOrWhiteSpace(recorded))
			{
				return recorded.Trim();
			}
			FromMaster = true;
			return live?.Trim() ?? string.Empty;
		}

		public T? Value<T>(T? recorded, T? live) where T : struct
		{
			if (recorded.HasValue)
			{
				return recorded;
			}
			FromMaster = true;
			return live;
		}

		public string Live(string? live)
		{
			FromMaster = true;
			return live?.Trim() ?? string.Empty;
		}
	}

	private static decimal Round(decimal amount) => RetailTaxCalculator.RoundMoney(amount);

	private static decimal Share(decimal amount, decimal part, decimal whole) => whole == 0m ? 0m : Round(amount * part / whole);

	// ---------------------------------------------------------------- lines

	/// <summary>
	/// A retail credit-note line from the bill line it returns. Retail is always CGST + SGST, the GST split out of the
	/// GST-inclusive credit exactly as the bill and GSTR-1 split it (RetailTaxCalculator, half to CGST).
	/// </summary>
	public static bool RecordSaleLine(SaleReturnItem row, SaleItem line, Drug? drug, Batch? batch)
	{
		var source = new Source();
		bool recorded = line.ItemName != null; // bill lines record the item name, HSN and batch when saved
		row.ItemName = source.From(recorded, line.ItemName, drug?.Name);
		row.HsnCode = source.From(recorded, line.HsnCode, drug?.HsnCode);
		row.Unit = source.Live(drug?.Unit); // a retail bill line never recorded the pack
		row.BatchNo = source.From(recorded, line.BatchNo, batch?.BatchNo);
		row.BatchExpiry = line.BatchExpiry ?? (line.BatchNo == null ? source.Value<DateOnly>(null, batch?.ExpiryDate) : null);
		row.UnitPrice = line.UnitPrice;
		row.TaxRate = line.TaxRate;
		row.DiscountAmount = Share(line.DiscountAmount, row.Quantity, line.Quantity);
		decimal tax = Round(row.CreditAmount * line.TaxRate / (100m + line.TaxRate));
		decimal cgst = Round(tax / 2m);
		row.CgstAmount = cgst;
		row.SgstAmount = tax - cgst;
		row.IgstAmount = 0m;
		row.TaxableAmount = row.CreditAmount - tax;
		return source.FromMaster;
	}

	/// <summary>A wholesale credit-note line: the invoice line's recorded values, its tax split in proportion to the quantity.</summary>
	public static bool RecordWholesaleLine(WholesaleReturnItem row, WholesaleInvoiceItem line, Drug? drug, Batch? batch)
	{
		var source = new Source();
		bool recorded = line.ItemName != null; // invoice lines record the item name, HSN, pack and batch when saved
		row.ItemName = source.From(recorded, line.ItemName, drug?.Name);
		row.HsnCode = source.From(recorded, line.HsnCode, drug?.HsnCode);
		row.Unit = source.From(recorded, line.Unit, drug?.Unit);
		row.BatchNo = source.From(recorded, line.BatchNo, batch?.BatchNo);
		row.BatchExpiry = line.BatchExpiry ?? (line.BatchNo == null ? source.Value<DateOnly>(null, batch?.ExpiryDate) : null);
		row.UnitPrice = line.UnitPrice;
		row.TaxRate = line.TaxRate;
		row.DiscountAmount = Share(line.DiscountAmount, row.Quantity, line.Quantity);
		decimal cgst = Share(line.CgstAmount, row.Quantity, line.Quantity);
		decimal sgst = Share(line.SgstAmount, row.Quantity, line.Quantity);
		decimal igst = Share(line.IgstAmount, row.Quantity, line.Quantity);
		row.CgstAmount = cgst;
		row.SgstAmount = sgst;
		row.IgstAmount = igst;
		row.TaxableAmount = row.CreditAmount - cgst - sgst - igst;
		return source.FromMaster;
	}

	/// <summary>
	/// A purchase-return (debit note) line. The rate and batch come from the purchase that brought the batch in; a
	/// purchase line never recorded the item name, HSN or pack, so those are read as they stand when the note is saved.
	/// Its GST is the rate on that purchase applied to the returned value (LineTotal, which is before GST), split as
	/// the supply type requires.
	/// </summary>
	public static bool RecordPurchaseLine(PurchaseReturnItem row, PurchaseItem? purchased, Drug? drug, Batch? batch, bool interState)
	{
		var source = new Source();
		row.ItemName = source.Live(drug?.Name);
		row.HsnCode = source.Live(drug?.HsnCode);
		row.Unit = source.Live(drug?.Unit);
		row.BatchNo = source.Text(purchased?.BatchNo, batch?.BatchNo);
		row.BatchExpiry = purchased != null ? purchased.ExpiryDate : source.Value<DateOnly>(null, batch?.ExpiryDate);
		row.TaxRate = purchased != null ? purchased.TaxRate : source.Value(null, drug?.GstRate) ?? 0m;
		row.DiscountAmount = 0m;
		row.TaxableAmount = row.LineTotal;
		decimal tax = Round(row.LineTotal * row.TaxRate.Value / 100m);
		if (interState)
		{
			row.CgstAmount = 0m;
			row.SgstAmount = 0m;
			row.IgstAmount = tax;
		}
		else
		{
			decimal cgst = Round(tax / 2m);
			row.CgstAmount = cgst;
			row.SgstAmount = tax - cgst;
			row.IgstAmount = 0m;
		}
		return source.FromMaster;
	}

	// ---------------------------------------------------------------- headers

	/// <summary>A retail credit note takes the parties, state and supply type its bill recorded (always intra-state).</summary>
	public static async Task<bool> RecordSaleNoteAsync(PharmaBillDbContext context, ReturnNote note, Sale sale, CancellationToken cancellationToken)
	{
		var source = new Source();
		note.ReturnDate ??= note.ReturnAtUtc.ToBusinessDate();
		if (RecordedGstStates.IsRecorded(sale))
		{
			BuyerSnapshot buyer = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(sale.BuyerSnapshotJson) ?? new BuyerSnapshot();
			note.SellerSnapshotJson = sale.SellerSnapshotJson;
			note.BuyerSnapshotJson = sale.BuyerSnapshotJson;
			note.PharmacyStateCode = sale.PharmacyStateCode;
			// A retail buyer is placed in the shop's own state (retail is always CGST + SGST); the bill's own state code.
			note.BuyerStateCode = string.IsNullOrWhiteSpace(buyer.StateCode) ? sale.PharmacyStateCode : buyer.StateCode.Trim();
			note.BuyerLegalName = buyer.Name.Trim();
			note.BuyerAddress = buyer.Address?.Trim() ?? string.Empty;
			note.BuyerGstin = buyer.Gstin?.Trim() ?? string.Empty;
		}
		else
		{
			PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PharmacyProfile();
			Patient? patient = sale.PatientId is Guid patientId ? await context.Patients.AsNoTracking().FirstOrDefaultAsync(item => item.Id == patientId, cancellationToken) : null;
			string state = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin).Code ?? string.Empty;
			SellerSnapshot seller = await TaxDocumentSnapshotBuilder.BuildSellerAsync(context, profile, null, state, null, cancellationToken);
			var buyer = new BuyerSnapshot { Name = patient?.Name ?? "Walk-in customer", Address = patient?.Address, Phone = patient?.Phone, StateCode = state, StateName = IndianGstStateCodes.NameForCode(state) };
			note.SellerSnapshotJson = TaxDocumentSnapshot.Serialize(seller);
			note.BuyerSnapshotJson = TaxDocumentSnapshot.Serialize(buyer);
			note.PharmacyStateCode = source.Live(state);
			note.BuyerStateCode = state;
			note.BuyerLegalName = buyer.Name;
			note.BuyerAddress = buyer.Address ?? string.Empty;
			note.BuyerGstin = string.Empty;
		}
		note.IsInterState = false;
		return source.FromMaster;
	}

	/// <summary>A wholesale credit note or cancellation takes the parties, states and supply type its invoice recorded.</summary>
	public static async Task<bool> RecordWholesaleNoteAsync(PharmaBillDbContext context, ReturnNote note, WholesaleInvoice invoice, CancellationToken cancellationToken)
	{
		note.ReturnDate ??= note.ReturnAtUtc.ToBusinessDate();
		if (RecordedGstStates.IsRecorded(invoice))
		{
			note.SellerSnapshotJson = invoice.SellerSnapshotJson;
			note.BuyerSnapshotJson = invoice.BuyerSnapshotJson;
			note.PharmacyStateCode = invoice.PharmacyStateCode;
			note.BuyerStateCode = invoice.BuyerStateCode;
			note.BuyerGstin = invoice.BuyerGstin?.Trim() ?? string.Empty;
			note.BuyerLegalName = invoice.BuyerLegalName?.Trim() ?? (TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(invoice.BuyerSnapshotJson)?.Name ?? string.Empty);
			note.BuyerAddress = invoice.BuyerAddress?.Trim() ?? string.Empty;
			note.IsInterState = !string.Equals(invoice.PharmacyStateCode, invoice.BuyerStateCode, StringComparison.Ordinal);
			return false;
		}
		Customer? customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == invoice.CustomerId, cancellationToken);
		await RecordCustomerPartiesAsync(context, note, customer, cancellationToken);
		// The invoice charged IGST or CGST + SGST: that, not today's states, is the supply type it applied.
		note.IsInterState = invoice.IgstAmount > 0m;
		return true;
	}

	/// <summary>A wholesale debit note stands alone (no invoice): the customer and pharmacy as they are when it is saved.</summary>
	public static async Task RecordDebitNoteAsync(PharmaBillDbContext context, ReturnNote note, Customer? customer, CancellationToken cancellationToken)
	{
		note.ReturnDate ??= note.ReturnAtUtc.ToBusinessDate();
		await RecordCustomerPartiesAsync(context, note, customer, cancellationToken);
	}

	private static async Task RecordCustomerPartiesAsync(PharmaBillDbContext context, ReturnNote note, Customer? customer, CancellationToken cancellationToken)
	{
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PharmacyProfile();
		string pharmacyState = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin).Code ?? string.Empty;
		// The same rule as the invoice: a buyer with no usable state is treated as local.
		string buyerState = IndianGstStateCodes.ResolveForTax(customer?.State, customer?.Gstin).Code ?? pharmacyState;
		SellerSnapshot seller = await TaxDocumentSnapshotBuilder.BuildSellerAsync(context, profile, null, pharmacyState, null, cancellationToken);
		var buyer = new BuyerSnapshot
		{
			Name = customer?.Name ?? string.Empty,
			Address = customer?.Address,
			Phone = customer?.Phone,
			Gstin = customer?.Gstin,
			StateCode = buyerState,
			StateName = IndianGstStateCodes.NameForCode(buyerState)
		};
		note.SellerSnapshotJson = TaxDocumentSnapshot.Serialize(seller);
		note.BuyerSnapshotJson = TaxDocumentSnapshot.Serialize(buyer);
		note.PharmacyStateCode = pharmacyState;
		note.BuyerStateCode = buyerState;
		note.BuyerGstin = customer?.Gstin?.Trim() ?? string.Empty;
		note.BuyerLegalName = customer?.Name.Trim() ?? string.Empty;
		note.BuyerAddress = customer?.Address?.Trim() ?? string.Empty;
		note.IsInterState = pharmacyState.Length > 0 && !string.Equals(pharmacyState, buyerState, StringComparison.Ordinal);
	}

	/// <summary>A debit note to a supplier: the supplier (seller of the goods) and pharmacy as they are when it is saved.</summary>
	public static async Task RecordPurchaseReturnAsync(PharmaBillDbContext context, PurchaseReturn note, Supplier? supplier, CancellationToken cancellationToken)
	{
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PharmacyProfile();
		string pharmacyState = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin).Code ?? string.Empty;
		// A supplier is placed by its GSTIN; without one it is treated as local, as an unregistered buyer is.
		string supplierState = IndianGstStateCodes.ResolveForTax(null, supplier?.Gstin).Code ?? pharmacyState;
		var seller = new SellerSnapshot
		{
			Name = supplier?.Name ?? string.Empty,
			Address = supplier?.Address,
			Phone = supplier?.Phone,
			Gstin = supplier?.Gstin,
			StateCode = supplierState,
			StateName = IndianGstStateCodes.NameForCode(supplierState)
		};
		var buyer = new BuyerSnapshot
		{
			Name = profile.LegalName ?? profile.Name,
			Address = profile.Address,
			Phone = profile.Phone,
			Gstin = profile.Gstin,
			StateCode = pharmacyState,
			StateName = IndianGstStateCodes.NameForCode(pharmacyState)
		};
		note.SellerSnapshotJson = TaxDocumentSnapshot.Serialize(seller);
		note.BuyerSnapshotJson = TaxDocumentSnapshot.Serialize(buyer);
		note.PharmacyStateCode = pharmacyState;
		note.SellerStateCode = supplierState;
		note.SellerGstin = supplier?.Gstin?.Trim() ?? string.Empty;
		note.SellerLegalName = supplier?.Name.Trim() ?? string.Empty;
		note.SellerAddress = supplier?.Address?.Trim() ?? string.Empty;
		note.IsInterState = pharmacyState.Length > 0 && !string.Equals(pharmacyState, supplierState, StringComparison.Ordinal);
	}

	/// <summary>The purchase line that brought a batch in: where a returned batch's rate and expiry were recorded.</summary>
	public static Task<PurchaseItem?> PurchaseOfAsync(PharmaBillDbContext context, Guid batchId, CancellationToken cancellationToken) =>
		context.PurchaseItems.AsNoTracking()
			.Where(item => item.BatchId == batchId)
			.OrderBy(item => item.CreatedAtUtc)
			.FirstOrDefaultAsync(cancellationToken);
}
