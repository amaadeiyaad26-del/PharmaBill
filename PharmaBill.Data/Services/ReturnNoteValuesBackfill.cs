using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>What the backfill filled: notes and lines copied from their original invoice, and those that had to use master data.</summary>
public sealed record ReturnNoteBackfillResult(int NotesFromInvoice, int NotesFromMaster, int LinesFromInvoice, int LinesFromMaster)
{
	public int Total => NotesFromInvoice + NotesFromMaster + LinesFromInvoice + LinesFromMaster;
}

/// <summary>
/// Fills the recorded values (see <see cref="ReturnNoteRecording"/>) on credit and debit notes saved before they were
/// recorded, and on any that arrive by sync from an older version. Each note and line is copied from the original
/// invoice it adjusts, which is what was actually charged. Where there is no such invoice, or the invoice never held a
/// value, current master data is used and the row is flagged <c>ValuesBackfilled</c>. Rows already recorded are never
/// touched, so running it again changes nothing.
/// </summary>
public static class ReturnNoteValuesBackfill
{
	public static async Task<ReturnNoteBackfillResult> RunAsync(PharmaBillDbContext context, CancellationToken cancellationToken = default)
	{
		int notesFromInvoice = 0, notesFromMaster = 0, linesFromInvoice = 0, linesFromMaster = 0;

		// Credit and debit notes to customers (retail and wholesale).
		List<ReturnNote> notes = await context.ReturnNotes.IgnoreQueryFilters()
			.Where(note => note.SellerSnapshotJson == null || note.ReturnDate == null || note.IsInterState == null)
			.ToListAsync(cancellationToken);
		foreach (ReturnNote note in notes)
		{
			bool fromMaster;
			switch (note.SourceType)
			{
				case "Sale" when await context.Sales.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == note.SourceId, cancellationToken) is Sale sale:
					fromMaster = await ReturnNoteRecording.RecordSaleNoteAsync(context, note, sale, cancellationToken);
					break;
				case "WholesaleCreditNote" or "WholesaleInvoice" when await context.WholesaleInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == note.SourceId, cancellationToken) is WholesaleInvoice invoice:
					fromMaster = await ReturnNoteRecording.RecordWholesaleNoteAsync(context, note, invoice, cancellationToken);
					break;
				default:
					// A standalone debit note (its source is the customer), or a note whose invoice is gone.
					Customer? customer = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == note.SourceId, cancellationToken);
					await ReturnNoteRecording.RecordDebitNoteAsync(context, note, customer, cancellationToken);
					fromMaster = true;
					break;
			}
			note.ValuesBackfilled = fromMaster;
			if (fromMaster) notesFromMaster++; else notesFromInvoice++;
		}

		List<SaleReturnItem> saleLines = await context.SaleReturnItems.IgnoreQueryFilters().Where(item => item.TaxRate == null).ToListAsync(cancellationToken);
		foreach (SaleReturnItem row in saleLines)
		{
			SaleItem? line = await context.SaleItems.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.SaleItemId, cancellationToken);
			Drug? drug = await context.Drugs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.DrugId, cancellationToken);
			Batch? batch = await context.Batches.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.BatchId, cancellationToken);
			bool fromMaster = line == null
				|| ReturnNoteRecording.RecordSaleLine(row, line, drug, batch);
			if (line == null)
			{
				ReturnNoteRecording.RecordSaleLine(row, StandIn(row.Quantity, row.CreditAmount, drug), drug, batch);
			}
			row.ValuesBackfilled = fromMaster;
			if (fromMaster) linesFromMaster++; else linesFromInvoice++;
		}

		List<WholesaleReturnItem> wholesaleLines = await context.WholesaleReturnItems.IgnoreQueryFilters().Where(item => item.TaxRate == null).ToListAsync(cancellationToken);
		foreach (WholesaleReturnItem row in wholesaleLines)
		{
			WholesaleInvoiceItem? line = await context.WholesaleInvoiceItems.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.WholesaleInvoiceItemId, cancellationToken);
			Drug? drug = await context.Drugs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.DrugId, cancellationToken);
			Batch? batch = await context.Batches.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.BatchId, cancellationToken);
			bool fromMaster;
			if (line != null)
			{
				fromMaster = ReturnNoteRecording.RecordWholesaleLine(row, line, drug, batch);
			}
			else
			{
				SaleItem standIn = StandIn(row.Quantity, row.CreditAmount, drug);
				var invoiceLike = new WholesaleInvoiceItem { Quantity = row.Quantity, UnitPrice = standIn.UnitPrice, TaxRate = standIn.TaxRate };
				ReturnNoteRecording.RecordWholesaleLine(row, invoiceLike, drug, batch);
				fromMaster = true;
			}
			row.ValuesBackfilled = fromMaster;
			if (fromMaster) linesFromMaster++; else linesFromInvoice++;
		}

		// Debit notes to suppliers (purchase returns): no invoice is linked, so the supplier and pharmacy are as they stand.
		List<PurchaseReturn> purchaseReturns = await context.PurchaseReturns.IgnoreQueryFilters()
			.Where(note => note.SellerSnapshotJson == null || note.IsInterState == null)
			.ToListAsync(cancellationToken);
		foreach (PurchaseReturn note in purchaseReturns)
		{
			Supplier? supplier = await context.Suppliers.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == note.SupplierId, cancellationToken);
			await ReturnNoteRecording.RecordPurchaseReturnAsync(context, note, supplier, cancellationToken);
			note.ValuesBackfilled = true;
			notesFromMaster++;
		}

		List<PurchaseReturnItem> purchaseLines = await context.PurchaseReturnItems.IgnoreQueryFilters().Where(item => item.TaxRate == null).ToListAsync(cancellationToken);
		foreach (PurchaseReturnItem row in purchaseLines)
		{
			PurchaseReturn? header = purchaseReturns.FirstOrDefault(item => item.Id == row.PurchaseReturnId)
				?? await context.PurchaseReturns.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == row.PurchaseReturnId, cancellationToken);
			Drug? drug = await context.Drugs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.DrugId, cancellationToken);
			Batch? batch = await context.Batches.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(item => item.Id == row.BatchId, cancellationToken);
			PurchaseItem? purchased = await ReturnNoteRecording.PurchaseOfAsync(context, row.BatchId, cancellationToken);
			bool fromMaster = ReturnNoteRecording.RecordPurchaseLine(row, purchased, drug, batch, header?.IsInterState == true);
			row.ValuesBackfilled = fromMaster;
			if (fromMaster) linesFromMaster++; else linesFromInvoice++;
		}

		var result = new ReturnNoteBackfillResult(notesFromInvoice, notesFromMaster, linesFromInvoice, linesFromMaster);
		if (result.Total > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		return result;
	}

	// For a line whose original invoice line no longer exists: the drug as it stands, priced at what was credited.
	private static SaleItem StandIn(decimal quantity, decimal credit, Drug? drug) => new()
	{
		Quantity = quantity <= 0m ? 1m : quantity,
		UnitPrice = quantity <= 0m ? credit : decimal.Round(credit / quantity, 2, MidpointRounding.AwayFromZero),
		TaxRate = drug?.GstRate ?? 0m
	};
}
