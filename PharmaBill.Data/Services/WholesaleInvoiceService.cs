using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Security;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesaleInvoiceService(IUnitOfWork unitOfWork, NumberSeriesService numberSeriesService, IEntitlementService entitlementService, BranchService branchService, ILicenseRuntimeGuard licenseRuntimeGuard)
{
	private sealed record PreparedLine(WholesaleInvoiceLineInput Input, Drug Drug, Batch Batch, string? Schedule, ProductTaxDetails TaxDetails, decimal TaxableAmount, decimal TaxAmount, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount);

	public async Task<WholesaleInvoiceResult> SaveAsync(SaveWholesaleInvoiceInput input, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
		{
			throw new UnauthorizedAccessException("Your role cannot create wholesale invoices.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.CreateBill, cancellationToken)))
		{
			throw new InvalidOperationException("Wholesale billing is unavailable in read-only mode.");
		}
		if (input.Items.Count == 0 || input.Items.Any((WholesaleInvoiceLineInput item) => item.Quantity <= 0m || item.FreeQuantity < 0m || item.UnitPrice < 0m || item.DiscountAmount < 0m))
		{
			throw new InvalidOperationException("Add valid invoice lines with positive quantities and non-negative prices.");
		}
		if (input.PaidAmount < 0m || string.IsNullOrWhiteSpace(input.PaymentMethod) || input.NearExpiryWarningDays < 0)
		{
			throw new InvalidOperationException("Enter a valid payment and near-expiry warning period.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		PharmacyProfile profile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
		if (profile.BusinessMode == BusinessMode.Retail)
		{
			throw new InvalidOperationException("Wholesale invoicing is unavailable in Retail mode.");
		}
		GstStateResolution pharmacyState = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin);
		if (!pharmacyState.IsResolved)
		{
			throw new InvalidOperationException(UnresolvedPharmacyMessage);
		}
		Customer customer = (await context.Customers.SingleOrDefaultAsync((Customer item) => item.Id == input.CustomerId, cancellationToken)) ?? throw new InvalidOperationException("Select a wholesale customer; walk-ins and patients are not allowed.");
		if (!customer.IsActive)
		{
			throw new InvalidOperationException("This customer is Blocked and cannot be invoiced.");
		}
		BuyerPlaceOfSupply buyerPlace = ResolveBuyerPlaceOfSupply(customer, pharmacyState);
		if (buyerPlace.RefusalMessage != null)
		{
			throw new InvalidOperationException(buyerPlace.RefusalMessage);
		}
		GstStateResolution buyerState = buyerPlace.Resolution;
		if (input.MarkedInterState)
		{
			if (buyerPlace.Defaulted)
			{
				throw new InvalidOperationException("This sale is marked inter-state, so the buyer's state is required: enter it (or a valid GSTIN) on the customer record for " + customer.Name + ".");
			}
			if (string.Equals(pharmacyState.Code, buyerState.Code, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"This sale is marked inter-state, but the buyer's state ({buyerState.CanonicalName} {buyerState.Code}) is the pharmacy's own state. Untick inter-state or correct the buyer's state.");
			}
		}
		bool intraState = string.Equals(pharmacyState.Code, buyerState.Code, StringComparison.Ordinal);
		IReadOnlyList<string> stateNotes = new WholesaleGstStateCheck(pharmacyState, buyerState).Notes;
		DateTime invoiceAtUtc = DateTime.SpecifyKind(input.InvoiceAtUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
		DateOnly invoiceDate = DateOnly.FromDateTime(invoiceAtUtc.ToLocalTime());
		IReadOnlyList<CustomerLicence> validLicences = await GetValidLicencesAsync(customer, profile, invoiceDate, cancellationToken);
		Guid[] requestedBatchIds = input.Items.Select((WholesaleInvoiceLineInput line) => line.BatchId).Distinct().ToArray();
		Guid[] requestedDrugIds = input.Items.Select((WholesaleInvoiceLineInput line) => line.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await context.Drugs.Where((Drug item) => item.IsActive && requestedDrugIds.Contains(item.Id)).ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await context.Batches.Where((Batch item) => requestedBatchIds.Contains(item.Id)).ToDictionaryAsync((Batch item) => item.Id, cancellationToken);
		if (drugs.Count != requestedDrugIds.Length || batches.Count != requestedBatchIds.Length || input.Items.Any((WholesaleInvoiceLineInput line) => !batches.TryGetValue(line.BatchId, out var value) || value.DrugId != line.DrugId))
		{
			throw new InvalidOperationException("An invoice line refers to an unavailable medicine or batch.");
		}
		Dictionary<Guid, decimal> quantities = await (from movement in context.StockMovements
			where requestedBatchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		List<ScheduleOverride> overrides = await context.ScheduleOverrides.Where((ScheduleOverride item) => requestedDrugIds.Contains(item.DrugId)).ToListAsync(cancellationToken);
		List<CatalogInfo> catalogInfos = await context.CatalogInfos.AsNoTracking().ToListAsync(cancellationToken);
		List<PreparedLine> prepared = new List<PreparedLine>();
		foreach (WholesaleInvoiceLineInput item in input.Items)
		{
			Drug drug = drugs[item.DrugId];
			Batch batch = batches[item.BatchId];
			decimal valueOrDefault = quantities.GetValueOrDefault(batch.Id);
			if (item.Quantity + item.FreeQuantity > valueOrDefault)
			{
				throw new InvalidOperationException($"Quantity exceeds stock for {drug.Name}, batch {batch.BatchNo} ({valueOrDefault} available).");
			}
			if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < invoiceDate)
			{
				throw new InvalidOperationException("Expired batch " + batch.BatchNo + " cannot be sold.");
			}
			if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value.DayNumber - invoiceDate.DayNumber <= input.NearExpiryWarningDays && !input.ConfirmNearExpiry)
			{
				throw new InvalidOperationException($"Confirm sale of near-expiry batch {batch.BatchNo} expiring {batch.ExpiryDate:yyyy-MM-dd}.");
			}
			decimal num = batch.Mrp ?? drug.Mrp ?? throw new InvalidOperationException("Set MRP for " + drug.Name + " before selling.");
			if (item.UnitPrice > num)
			{
				throw new InvalidOperationException("Selling price for " + drug.Name + " cannot exceed batch MRP.");
			}
			string schedule = ResolveSchedule(drug, catalogInfos, overrides, invoiceAtUtc);
			string text = schedule;
			bool flag = ((text == "X" || text == "NDPS") ? true : false);
			if (flag && !validLicences.Any((CustomerLicence licence) => HasScheduleAuthorisation(licence.Authorisation, schedule)))
			{
				throw new InvalidOperationException($"Customer {customer.Name} has no valid licence authorisation matching Schedule {schedule}.");
			}
			decimal num2 = RoundMoney(item.Quantity * item.UnitPrice - item.DiscountAmount);
			if (num2 < 0m)
			{
				throw new InvalidOperationException("Discount exceeds line amount for " + drug.Name + ".");
			}
			ProductTaxDetails taxDetails = ProductTax.ForBilling(drug, item.Tax);
			decimal num3 = RoundMoney(num2 * taxDetails.GstRate / 100m);
			decimal num4 = (intraState ? RoundMoney(num3 / 2m) : 0m);
			decimal sgstAmount = (intraState ? (num3 - num4) : 0m);
			prepared.Add(new PreparedLine(item, drug, batch, schedule, taxDetails, num2, num3, num4, sgstAmount, intraState ? 0m : num3));
			quantities[batch.Id] = valueOrDefault - item.Quantity - item.FreeQuantity;
		}
		decimal subtotal = prepared.Sum((PreparedLine line) => line.TaxableAmount);
		decimal taxTotal = prepared.Sum((PreparedLine line) => line.TaxAmount);
		decimal cgstTotal = prepared.Sum((PreparedLine line) => line.CgstAmount);
		decimal sgstTotal = prepared.Sum((PreparedLine line) => line.SgstAmount);
		decimal igstTotal = prepared.Sum((PreparedLine line) => line.IgstAmount);
		decimal num5 = subtotal + taxTotal;
		decimal roundedTotal = decimal.Round(num5, 0, MidpointRounding.AwayFromZero);
		decimal roundOff = RoundMoney(roundedTotal - num5);
		if (buyerPlace.Defaulted && roundedTotal > profile.B2cLargeInvoiceThreshold)
		{
			throw new InvalidOperationException($"Invoice value ₹{roundedTotal:N2} is above the B2C-large threshold (₹{profile.B2cLargeInvoiceThreshold:N2}), where place of supply must be reported. Enter the buyer's state (or a valid GSTIN) on the customer record for {customer.Name}.");
		}
		if (input.PaidAmount > roundedTotal)
		{
			throw new InvalidOperationException("Payment cannot exceed the invoice total.");
		}
		decimal openingBalance = customer.OpeningBalance;
		decimal? num6 = await context.CustomerLedgerEntries.Where((CustomerLedgerEntry entry) => entry.CustomerId == customer.Id).SumAsync((Expression<Func<CustomerLedgerEntry, decimal?>>)((CustomerLedgerEntry entry) => entry.Debit - entry.Credit), cancellationToken);
		decimal num7 = ((decimal?)openingBalance + num6) ?? customer.OpeningBalance;
		decimal outstandingAfter = RoundMoney(num7 + roundedTotal - input.PaidAmount);
		bool hasOverdue = await HasOverdueInvoicesAsync(customer, invoiceDate, cancellationToken);
		bool flag3 = customer.CreditLimit > 0m && outstandingAfter > customer.CreditLimit;
		if ((hasOverdue | flag3) && !input.OverrideCreditOrOverdue)
		{
			throw new InvalidOperationException((hasOverdue ? "Customer has overdue invoices" : "Customer credit limit would be exceeded") + "; owner override with a reason is required.");
		}
		if ((hasOverdue | flag3) && (role != UserRole.Owner || string.IsNullOrWhiteSpace(input.OverrideReason)))
		{
			throw new UnauthorizedAccessException("Only the owner can override credit or overdue warnings, with a reason.");
		}
		string paymentStatus;
		if (input.PaidAmount == roundedTotal)
		{
			paymentStatus = "Paid";
		}
		else
		{
			paymentStatus = ((input.PaidAmount > 0m) ? "PartiallyPaid" : "Credit");
		}
		WholesaleInvoiceResult result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			Branch branch = await branchService.EnsureCurrentBranchAsync(cancellationToken);
			string seriesPrefix = await branchService.ResolveWholesaleInvoiceSeriesPrefixAsync(cancellationToken);
			string text2 = await numberSeriesService.AllocateAsync(seriesPrefix, invoiceDate, cancellationToken);
			SellerSnapshot seller = await TaxDocumentSnapshotBuilder.BuildSellerAsync(context, profile, branch, pharmacyState.Code, branding: null, cancellationToken);
			List<CustomerLicence> buyerLicences = await context.CustomerLicences.AsNoTracking()
				.Where(item => item.CustomerId == customer.Id)
				.OrderBy(item => item.LicenceType)
				.ThenBy(item => item.LicenceNumber)
				.ToListAsync(cancellationToken);
			BuyerSnapshot buyer = new BuyerSnapshot
			{
				Name = customer.Name,
				Address = customer.Address,
				Phone = customer.Phone,
				Gstin = customer.Gstin,
				StateCode = buyerState.Code,
				StateName = buyerPlace.Defaulted ? buyerState.CanonicalName + " (buyer's state not on record; supplier's state used)" : buyerState.CanonicalName,
				Licences = buyerLicences.Select(item => new LicenceSnapshot(item.LicenceType, item.LicenceNumber, item.ExpiresOn)).ToArray()
			};
			WholesaleInvoice invoice = new WholesaleInvoice
			{
				CustomerId = customer.Id,
				InvoiceNo = text2,
				InvoiceAtUtc = invoiceAtUtc,
				Subtotal = subtotal,
				TaxAmount = taxTotal,
				CgstAmount = cgstTotal,
				SgstAmount = sgstTotal,
				IgstAmount = igstTotal,
				RoundOff = roundOff,
				DiscountAmount = prepared.Sum((PreparedLine line) => line.Input.DiscountAmount),
				TotalAmount = roundedTotal,
				PaidAmount = input.PaidAmount,
				PaymentStatus = paymentStatus,
				TransportDetails = NullIfWhiteSpace(input.TransportDetails),
				VehicleNumber = NullIfWhiteSpace(input.VehicleNumber),
				EWayBillNumber = NullIfWhiteSpace(input.EWayBillNumber),
				Irn = NullIfWhiteSpace(input.Irn),
				Notes = NullIfWhiteSpace(input.Notes),
				BranchId = branch.Id,
				PharmacyStateCode = pharmacyState.Code,
				BuyerStateCode = buyerState.Code,
				BuyerStateDefaulted = buyerPlace.Defaulted,
				BuyerGstin = customer.Gstin,
				BuyerLegalName = customer.Name,
				BuyerAddress = customer.Address,
				SellerSnapshotJson = TaxDocumentSnapshot.Serialize(seller),
				BuyerSnapshotJson = TaxDocumentSnapshot.Serialize(buyer)
			};
			context.WholesaleInvoices.Add(invoice);
			foreach (PreparedLine item2 in prepared)
			{
				item2.Batch.Quantity = quantities[item2.Batch.Id];
				context.WholesaleInvoiceItems.Add(new WholesaleInvoiceItem
				{
					WholesaleInvoiceId = invoice.Id,
					DrugId = item2.Drug.Id,
					BatchId = item2.Batch.Id,
					Quantity = item2.Input.Quantity,
					FreeQuantity = item2.Input.FreeQuantity,
					UnitPrice = item2.Input.UnitPrice,
					DiscountAmount = item2.Input.DiscountAmount,
					TaxRate = item2.TaxDetails.GstRate,
					TaxableAmount = item2.TaxableAmount,
					CgstAmount = item2.CgstAmount,
					SgstAmount = item2.SgstAmount,
					IgstAmount = item2.IgstAmount,
					LineTotal = item2.TaxableAmount + item2.TaxAmount,
					ItemName = item2.Drug.Name,
					HsnCode = item2.TaxDetails.HsnCode,
					Unit = item2.Drug.Unit,
					BatchNo = item2.Batch.BatchNo,
					BatchExpiry = item2.Batch.ExpiryDate,
					Mrp = item2.Batch.Mrp ?? item2.Drug.Mrp
				});
				context.StockMovements.Add(new StockMovement
				{
					BatchId = item2.Batch.Id,
					DrugId = item2.Drug.Id,
					QuantityChange = -(item2.Input.Quantity + item2.Input.FreeQuantity),
					MovementType = "WholesaleSale",
					ReferenceType = "WholesaleInvoice",
					ReferenceId = invoice.Id,
					MovementAtUtc = invoiceAtUtc,
					Notes = text2
				});
				context.WholesaleRateHistory.Add(new WholesaleRateHistory
				{
					CustomerId = customer.Id,
					DrugId = item2.Drug.Id,
					BatchId = item2.Batch.Id,
					InvoiceId = invoice.Id,
					SoldAtUtc = invoiceAtUtc,
					UnitRate = item2.Input.UnitPrice,
					Quantity = item2.Input.Quantity
				});
				bool flag;
				switch (item2.Schedule)
				{
				case "H1":
				case "X":
				case "NDPS":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag || IsHabitForming(item2.Drug, catalogInfos))
				{
					context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
					{
						RegisterType = (item2.Schedule ?? "HabitForming"),
						CustomerId = customer.Id,
						DrugId = item2.Drug.Id,
						SupplierId = item2.Batch.SupplierId,
						BatchId = item2.Batch.Id,
						BatchNo = item2.Batch.BatchNo,
						Quantity = item2.Input.Quantity + item2.Input.FreeQuantity,
						EntryAtUtc = invoiceAtUtc,
						BuyerName = customer.Name,
						BuyerAddress = customer.Address,
						BuyerPhone = customer.Phone,
						BuyerLicenceNumber = validLicences.First().LicenceNumber,
						Notes = "Wholesale invoice " + text2
					});
				}
			}
			context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
			{
				CustomerId = customer.Id,
				EntryAtUtc = invoiceAtUtc,
				EntryType = LedgerEntryTypes.WholesaleInvoice,
				ReferenceId = invoice.Id,
				ReferenceNo = text2,
				Debit = roundedTotal,
				Credit = 0m,
				Notes = ((input.OverrideReason == null) ? null : ("Owner override: " + input.OverrideReason.Trim()))
			});
			if (input.PaidAmount > 0m)
			{
				Receipt receipt = new Receipt
				{
					CustomerId = customer.Id,
					WholesaleInvoiceId = invoice.Id,
					ReceiptNo = text2 + "-RCPT",
					ReceiptAtUtc = invoiceAtUtc,
					Amount = input.PaidAmount,
					PaymentMethod = input.PaymentMethod.Trim(),
					ReferenceNumber = NullIfWhiteSpace(input.PaymentReference),
					Notes = "Against invoice " + text2
				};
				context.Receipts.Add(receipt);
				context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
				{
					CustomerId = customer.Id,
					EntryAtUtc = invoiceAtUtc,
					EntryType = LedgerEntryTypes.Receipt,
					ReferenceId = receipt.Id,
					ReferenceNo = receipt.ReceiptNo,
					Debit = 0m,
					Credit = input.PaidAmount,
					Notes = receipt.PaymentMethod
				});
			}
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = invoiceAtUtc,
				Action = "WholesaleInvoicePosted",
				EntityName = "WholesaleInvoice",
				EntityId = invoice.Id,
				BranchId = branch.Id,
				Details = $"Invoice {text2}; buyer {customer.Name}; total {roundedTotal}; branch {branch.Code}; GST states {pharmacyState.Code}->{buyerState.Code}"
					+ (buyerPlace.Defaulted ? " (buyer state defaulted to supplier's: no GSTIN or state on record)." : ".")
					+ (stateNotes.Count == 0 ? string.Empty : " " + string.Join(" ", stateNotes))
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			if (!licenseRuntimeGuard.OnTransactionCommitted())
			{
				throw new InvalidOperationException("System clock manipulation detected. Please set your system time correctly to resume.");
			}
			result = new WholesaleInvoiceResult(invoice, cgstTotal, sgstTotal, igstTotal, roundOff, outstandingAfter, hasOverdue, stateNotes);
		}
		return result;
	}

	private const string UnresolvedPharmacyMessage = "The pharmacy's GST state could not be determined: no valid GSTIN and no recognised state. Set it in Settings → Firm Profile before calculating CGST/SGST or IGST.";

	/// <summary>How the buyer's place of supply was decided for an invoice.</summary>
	private sealed record BuyerPlaceOfSupply(GstStateResolution Resolution, bool Defaulted, string? LocalSaleNote, string? RefusalMessage);

	/// <summary>
	/// Valid GSTIN → its state; else a recorded state; else (unregistered, nothing on record) the supplier's own
	/// state, i.e. an intra-state sale. Only a recorded state that is not a real state is refused.
	/// </summary>
	private static BuyerPlaceOfSupply ResolveBuyerPlaceOfSupply(Customer customer, GstStateResolution pharmacyState)
	{
		GstStateResolution resolution = IndianGstStateCodes.ResolveForTax(customer.State, customer.Gstin);
		if (resolution.IsResolved)
		{
			return new BuyerPlaceOfSupply(resolution, Defaulted: false, LocalSaleNote: null, RefusalMessage: null);
		}
		if (!string.IsNullOrWhiteSpace(customer.State))
		{
			return new BuyerPlaceOfSupply(resolution, false, null,
				$"The buyer's GST state could not be determined for {customer.Name}: \"{customer.State.Trim()}\" is not a recognised Indian GST state. Pick the state from the list on the customer record.");
		}
		string note = string.IsNullOrWhiteSpace(customer.Gstin)
			? "No GSTIN on record — treated as a local sale (CGST/SGST)."
			: "GSTIN on record is not valid — treated as an unregistered local sale (CGST/SGST).";
		return new BuyerPlaceOfSupply(
			new GstStateResolution(pharmacyState.Code, GstStateSource.SupplierDefault, null, null, null),
			Defaulted: true,
			LocalSaleNote: note,
			RefusalMessage: null);
	}

	/// <summary>Resolves both sides for the invoice screen so mismatches and refusals are shown before saving.</summary>
	public async Task<WholesaleGstStateCheck> CheckGstStatesAsync(Guid customerId, CancellationToken cancellationToken = default)
	{
		PharmacyProfile profile = await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		Customer customer = await unitOfWork.Context.Customers.AsNoTracking().SingleAsync(item => item.Id == customerId, cancellationToken);
		GstStateResolution pharmacy = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin);
		if (!pharmacy.IsResolved)
		{
			return new WholesaleGstStateCheck(pharmacy, IndianGstStateCodes.ResolveForTax(customer.State, customer.Gstin)) { RefusalMessage = UnresolvedPharmacyMessage };
		}
		BuyerPlaceOfSupply buyer = ResolveBuyerPlaceOfSupply(customer, pharmacy);
		return new WholesaleGstStateCheck(pharmacy, buyer.Resolution)
		{
			RefusalMessage = buyer.RefusalMessage,
			BuyerStateDefaulted = buyer.Defaulted,
			LocalSaleNote = buyer.LocalSaleNote,
			B2cLargeInvoiceThreshold = profile.B2cLargeInvoiceThreshold
		};
	}

	/// <summary>Replaces a stored state name that disagrees with the GSTIN by the GSTIN's canonical state name.</summary>
	public async Task<IReadOnlyList<string>> CorrectStoredStatesFromGstinAsync(Guid customerId, Guid userId, CancellationToken cancellationToken = default)
	{
		PharmaBillDbContext context = unitOfWork.Context;
		PharmacyProfile profile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
		Customer customer = await context.Customers.SingleAsync(item => item.Id == customerId, cancellationToken);
		List<string> changes = new List<string>();
		GstStateResolution pharmacy = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin);
		if (pharmacy.HasMismatch && pharmacy.CanonicalName != null)
		{
			changes.Add($"Pharmacy state '{profile.State}' -> '{pharmacy.CanonicalName}'");
			profile.State = pharmacy.CanonicalName;
		}
		GstStateResolution buyer = IndianGstStateCodes.ResolveForTax(customer.State, customer.Gstin);
		if (buyer.HasMismatch && buyer.CanonicalName != null)
		{
			changes.Add($"Buyer {customer.Name} state '{customer.State}' -> '{buyer.CanonicalName}'");
			customer.State = buyer.CanonicalName;
		}
		if (changes.Count > 0)
		{
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = DateTime.UtcNow,
				Action = "GstStateCorrectedFromGstin",
				EntityName = "Customer",
				EntityId = customer.Id,
				Details = string.Join("; ", changes) + "."
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
		}
		return changes;
	}

	public async Task CancelAsync(Guid invoiceId, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (role != UserRole.Owner || string.IsNullOrWhiteSpace(reason))
		{
			throw new UnauthorizedAccessException("Invoice cancellation requires the owner role and a reason.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		WholesaleInvoice invoice = (await context.WholesaleInvoices.SingleOrDefaultAsync((WholesaleInvoice item) => item.Id == invoiceId, cancellationToken)) ?? throw new InvalidOperationException("Invoice not found.");
		if (invoice.Status != "Posted")
		{
			throw new InvalidOperationException("Only a posted wholesale invoice can be cancelled.");
		}
		List<WholesaleInvoiceItem> lines = await context.WholesaleInvoiceItems.Where((WholesaleInvoiceItem item) => item.WholesaleInvoiceId == invoice.Id).ToListAsync(cancellationToken);
		List<Guid> lineIds = lines.Select(line => line.Id).ToList();
		if (await context.WholesaleReturnItems.AnyAsync(item => lineIds.Contains(item.WholesaleInvoiceItemId), cancellationToken))
		{
			// Cancelling restores every line in full and credits the full total, which would double-count
			// anything already returned or credited. Those invoices must be corrected with a credit note instead.
			throw new InvalidOperationException("This invoice already has credit notes, so it cannot be cancelled. Issue a credit note for the remaining quantity instead.");
		}
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		invoice.Status = "Cancelled";
		invoice.CancelledAtUtc = DateTime.UtcNow;
		invoice.CancellationReason = reason.Trim();
		ReturnNote correction = new ReturnNote
		{
			ReturnNo = invoice.InvoiceNo + "-CANCEL",
			SourceType = "WholesaleInvoice",
			SourceId = invoice.Id,
			ReturnAtUtc = DateTime.UtcNow,
			TotalAmount = invoice.TotalAmount,
			Reason = reason.Trim(),
			Notes = "Cancellation correction; original invoice number retained: " + invoice.InvoiceNo
		};
		context.ReturnNotes.Add(correction);
		foreach (WholesaleInvoiceItem line in lines)
		{
			Batch batch = await context.Batches.SingleAsync((Batch item) => item.Id == line.BatchId, cancellationToken);
			decimal num = line.Quantity + line.FreeQuantity;
			batch.Quantity += num;
			context.StockMovements.Add(new StockMovement
			{
				BatchId = batch.Id,
				DrugId = line.DrugId,
				QuantityChange = num,
				MovementType = "WholesaleSaleCancellation",
				ReferenceType = "ReturnNote",
				ReferenceId = correction.Id,
				Notes = reason.Trim()
			});
		}
		context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
		{
			CustomerId = invoice.CustomerId,
			EntryAtUtc = correction.ReturnAtUtc,
			EntryType = LedgerEntryTypes.WholesaleInvoiceCancellation,
			ReferenceId = correction.Id,
			ReferenceNo = correction.ReturnNo,
			Debit = 0m,
			Credit = invoice.TotalAmount,
			Notes = reason.Trim()
		});
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			ActionAtUtc = correction.ReturnAtUtc,
			Action = "WholesaleInvoiceCancelled",
			EntityName = "WholesaleInvoice",
			EntityId = invoice.Id,
			Details = "Invoice number retained: " + invoice.InvoiceNo + "; reason: " + reason.Trim()
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private async Task<IReadOnlyList<CustomerLicence>> GetValidLicencesAsync(Customer customer, PharmacyProfile profile, DateOnly invoiceDate, CancellationToken cancellationToken)
	{
		Dictionary<string, string[]> source = BuyerLicenceRules.Parse(profile.WholesaleBuyerLicenceRulesJson);
		string[] allowedTypes = BuyerLicenceRules.AllowedFor(source, customer.BuyerType);
		if (allowedTypes.Length == 0)
		{
			throw new InvalidOperationException(BuyerLicenceRules.MissingRuleMessage(source, customer.BuyerType ?? string.Empty)
				?? "No permitted drug licence types are configured for buyer type '" + customer.BuyerType + "'.");
		}
		CustomerLicence[] array = (await unitOfWork.Context.CustomerLicences.Where((CustomerLicence licence) => licence.CustomerId == customer.Id && licence.IssuedOn.HasValue && licence.IssuedOn.Value <= invoiceDate && licence.ExpiresOn.HasValue && licence.ExpiresOn.Value >= invoiceDate).ToListAsync(cancellationToken)).Where((CustomerLicence licence) => allowedTypes.Contains(licence.LicenceType, StringComparer.OrdinalIgnoreCase)).ToArray();
		if (array.Length == 0)
		{
			throw new InvalidOperationException($"Customer {customer.Name} needs an issued, unexpired licence allowed for buyer type '{customer.BuyerType}'.");
		}
		return array;
	}

	private async Task<bool> HasOverdueInvoicesAsync(Customer customer, DateOnly invoiceDate, CancellationToken cancellationToken)
	{
		if (customer.CreditDays <= 0)
		{
			return false;
		}
		List<WholesaleInvoice> invoices = await unitOfWork.Context.WholesaleInvoices.Where((WholesaleInvoice invoice) => invoice.CustomerId == customer.Id && invoice.Status == "Posted").ToListAsync(cancellationToken);
		Guid[] invoiceIds = invoices.Select((WholesaleInvoice invoice) => invoice.Id).ToArray();
		Dictionary<Guid, decimal> receipts = await (from receipt in unitOfWork.Context.Receipts
			where receipt.WholesaleInvoiceId.HasValue && invoiceIds.Contains(receipt.WholesaleInvoiceId.Value)
			group receipt by receipt.WholesaleInvoiceId.Value into @group
			select new
			{
				InvoiceId = @group.Key,
				Amount = @group.Sum((Receipt receipt) => receipt.Amount)
			}).ToDictionaryAsync(item => item.InvoiceId, item => item.Amount, cancellationToken);
		return invoices.Any((WholesaleInvoice invoice) => invoice.TotalAmount - Math.Max(invoice.PaidAmount, receipts.GetValueOrDefault(invoice.Id)) > 0m && DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).AddDays(customer.CreditDays) < invoiceDate);
	}

	private static string? ResolveSchedule(Drug drug, IEnumerable<CatalogInfo> infos, IEnumerable<ScheduleOverride> overrides, DateTime atUtc)
	{
		ScheduleOverride scheduleOverride = (from item in overrides
			where item.DrugId == drug.Id && (!item.EffectiveFromUtc.HasValue || item.EffectiveFromUtc <= atUtc) && (!item.EffectiveToUtc.HasValue || item.EffectiveToUtc >= atUtc)
			orderby item.EffectiveFromUtc descending
			select item).FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(scheduleOverride?.Schedule))
		{
			return NormalizeSchedule(scheduleOverride.Schedule);
		}
		if (!string.IsNullOrWhiteSpace(drug.Schedule))
		{
			return NormalizeSchedule(drug.Schedule);
		}
		string text = infos.FirstOrDefault((CatalogInfo info) => (drug.CatalogMedicineId.HasValue && info.CatalogMedicineId == drug.CatalogMedicineId) || string.Equals(info.NameKey, drug.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))?.Schedule;
		if (text == null)
		{
			return null;
		}
		return NormalizeSchedule(text);
	}

	private static bool HasScheduleAuthorisation(string? authorisation, string schedule)
	{
		if (!string.IsNullOrWhiteSpace(authorisation))
		{
			if (!authorisation.Contains(schedule, StringComparison.OrdinalIgnoreCase))
			{
				if (schedule == "NDPS")
				{
					return authorisation.Contains("Narcotic", StringComparison.OrdinalIgnoreCase);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool IsHabitForming(Drug drug, IReadOnlyCollection<CatalogInfo> catalogInfos)
	{
		return catalogInfos.Any((CatalogInfo info) => info.IsHabitForming && ((drug.CatalogMedicineId.HasValue && info.CatalogMedicineId == drug.CatalogMedicineId) || string.Equals(info.NameKey, drug.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)));
	}

	private static string? NormalizeSchedule(string value)
	{
		string text = value.Trim().ToUpperInvariant().Replace("SCHEDULE ", string.Empty, StringComparison.Ordinal);
		bool flag;
		switch (text)
		{
		case "H1":
		case "X":
		case "NDPS":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return null;
		}
		return text;
	}

	private static decimal RoundMoney(decimal amount)
	{
		return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
	}

	private static string? NullIfWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
