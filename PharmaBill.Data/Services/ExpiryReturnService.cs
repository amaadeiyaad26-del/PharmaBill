using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Inventory;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>A batch that can be claimed now: its expiry is inside the supplier's window and some of it is saleable.</summary>
public sealed record ClaimableBatchRow(
	Guid BatchId,
	Guid DrugId,
	string ItemName,
	string Pack,
	string HsnCode,
	string BatchNo,
	DateOnly Expiry,
	int DaysToExpiry,
	decimal Available,
	decimal Rate,
	decimal GstRate,
	decimal ValueAtRate,
	Guid? SupplierId,
	string SupplierName,
	bool Traceable,
	string? PurchaseInvoiceNo,
	DateOnly WindowOpens,
	DateOnly WindowCloses);

public sealed record ExpiryClaimLineRequest(Guid BatchId, decimal Quantity);

public sealed record ExpiryCreditLineInput(Guid ClaimLineId, decimal Amount, string? ShortfallReason);

public sealed record ExpiryCreditInput(
	string CreditNoteNo,
	DateOnly CreditNoteDate,
	decimal Amount,
	decimal TaxableValue,
	decimal CgstAmount,
	decimal SgstAmount,
	decimal IgstAmount,
	ExpiryCreditSettlement Settlement,
	IReadOnlyList<ExpiryCreditLineInput> Lines,
	string? Notes = null);

/// <summary>What the pharmacist decides for goods the distributor rejected.</summary>
public enum RejectedGoodsAction
{
	WriteOff,
	ReturnToHeldStock
}

/// <summary>A claim with what is owed on it now.</summary>
public sealed record ExpiryClaimSummary(
	Guid Id,
	string ClaimNo,
	DateOnly ClaimDate,
	ExpiryClaimStatus Status,
	Guid SupplierId,
	string SupplierName,
	int LineCount,
	decimal TotalValue,
	decimal CreditedValue,
	decimal Balance,
	DateOnly? SentDate,
	int? DaysOutstanding);

/// <summary>Held stock (on a draft, or rejected goods back in the shop): not sellable, still the shop's stock.</summary>
public sealed record HeldStockRow(Guid BatchId, Guid DrugId, string ItemName, string BatchNo, decimal Quantity, decimal Value, string ClaimNo);

/// <summary>
/// Expiry returns to distributors. Stock is held, sent and released only inside the transaction that reads it; every
/// value on a claim is recorded when it is saved; a credit note posts to the existing supplier ledger.
/// </summary>
public sealed class ExpiryReturnService(IUnitOfWork unitOfWork, NumberSeriesService numberSeries, IEntitlementService entitlement)
{
	public const string HoldMovement = "ExpiryClaimHold";
	public const string ReleaseMovement = "ExpiryClaimRelease";
	public const string ReferenceType = "ExpiryClaim";
	public const string SeriesSuffix = "-EXP";

	/// <summary>Told to the pharmacist on the screen: the claim records GST but does not decide input tax credit.</summary>
	public const string ItcNote = "Confirm the input tax credit treatment of expiry returns and credit notes with your accountant; Pharma Remit records the GST but files nothing.";

	private PharmaBillDbContext Context => unitOfWork.Context;

	private static decimal Round(decimal amount) => RetailTaxCalculator.RoundMoney(amount);

	// ------------------------------------------------------------------ policy

	/// <summary>The window a supplier's batches are judged by: its own, or the pharmacy's default.</summary>
	public static ExpiryReturnWindow WindowFor(Supplier? supplier, PharmacyProfile profile) =>
		new(supplier?.ExpiryReturnDaysBefore ?? profile.ExpiryReturnDaysBefore, supplier?.ExpiryReturnDaysAfter ?? profile.ExpiryReturnDaysAfter);

	public async Task<PharmacyProfile> ProfileAsync(CancellationToken cancellationToken = default) =>
		await Context.PharmacyProfiles.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken) ?? new PharmacyProfile();

	/// <summary>Sets the default window every supplier without its own follows.</summary>
	public async Task SaveDefaultWindowAsync(int daysBefore, int daysAfter, Guid userId, CancellationToken cancellationToken = default)
	{
		ExpiryReturnWindow window = ExpiryReturnWindow.Validated(daysBefore, daysAfter);
		PharmacyProfile profile = await Context.PharmacyProfiles.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken)
			?? throw new InvalidOperationException("Set up the pharmacy profile first.");
		Context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "ExpiryReturnDefaultWindowChanged",
			EntityName = "PharmacyProfile",
			EntityId = profile.Id,
			Details = $"{profile.ExpiryReturnDaysBefore}/{profile.ExpiryReturnDaysAfter} -> {window.DaysBefore}/{window.DaysAfter} days before/after expiry"
		});
		profile.ExpiryReturnDaysBefore = window.DaysBefore;
		profile.ExpiryReturnDaysAfter = window.DaysAfter;
		await Context.SaveChangesAsync(cancellationToken);
	}

	/// <summary>Sets one supplier's window and stated policy. Null days make it follow the default again.</summary>
	public async Task SaveSupplierPolicyAsync(Guid supplierId, int? daysBefore, int? daysAfter, string? note, Guid userId, CancellationToken cancellationToken = default)
	{
		if (daysBefore.HasValue != daysAfter.HasValue)
		{
			throw new InvalidOperationException("Enter both the days before and the days after expiry, or neither to follow the default.");
		}
		if (daysBefore.HasValue)
		{
			ExpiryReturnWindow.Validated(daysBefore.Value, daysAfter!.Value);
		}
		Supplier supplier = await Context.Suppliers.SingleOrDefaultAsync(item => item.Id == supplierId, cancellationToken)
			?? throw new InvalidOperationException("Supplier not found.");
		Context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "SupplierExpiryPolicyChanged",
			EntityName = "Supplier",
			EntityId = supplier.Id,
			Details = $"{supplier.Name}: {Describe(supplier.ExpiryReturnDaysBefore, supplier.ExpiryReturnDaysAfter)} -> {Describe(daysBefore, daysAfter)}"
		});
		supplier.ExpiryReturnDaysBefore = daysBefore;
		supplier.ExpiryReturnDaysAfter = daysAfter;
		supplier.ExpiryReturnPolicyNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
		await Context.SaveChangesAsync(cancellationToken);

		static string Describe(int? before, int? after) => before.HasValue ? $"{before}/{after} days" : "default";
	}

	// ------------------------------------------------------------------ claimable now

	/// <summary>
	/// Batches that can be claimed today (Asia/Kolkata): saleable quantity left (held quantities are already out of it),
	/// not banned, expiry inside the window of the supplier the purchase came from. A batch with no traceable purchase is
	/// listed under no supplier, judged by the default window, for the pharmacist to assign.
	/// </summary>
	public async Task<IReadOnlyList<ClaimableBatchRow>> GetClaimableAsync(CancellationToken cancellationToken = default)
	{
		DateOnly today = BusinessClock.Today;
		PharmacyProfile profile = await ProfileAsync(cancellationToken);
		List<Supplier> suppliers = await Context.Suppliers.AsNoTracking().ToListAsync(cancellationToken);
		int widest = suppliers.Select(item => item.ExpiryReturnDaysBefore ?? profile.ExpiryReturnDaysBefore).Append(profile.ExpiryReturnDaysBefore).Max();
		int widestAfter = suppliers.Select(item => item.ExpiryReturnDaysAfter ?? profile.ExpiryReturnDaysAfter).Append(profile.ExpiryReturnDaysAfter).Max();
		DateOnly latest = today.AddDays(widest);
		DateOnly earliest = today.AddDays(-widestAfter);
		List<Batch> batches = await Context.Batches.AsNoTracking()
			.Where(batch => batch.ExpiryDate != null && batch.ExpiryDate <= latest && batch.ExpiryDate >= earliest && !batch.IsBanned)
			.ToListAsync(cancellationToken);
		if (batches.Count == 0)
		{
			return [];
		}
		Guid[] batchIds = batches.Select(batch => batch.Id).ToArray();
		Dictionary<Guid, decimal> onHand = await Context.StockMovements.AsNoTracking()
			.Where(movement => batchIds.Contains(movement.BatchId))
			.GroupBy(movement => movement.BatchId)
			.Select(group => new { group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
			.ToDictionaryAsync(row => row.Key, row => row.Quantity, cancellationToken);
		Dictionary<Guid, Origin> origins = await OriginsAsync(batchIds, cancellationToken);
		Guid[] drugIds = batches.Select(batch => batch.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await Context.Drugs.AsNoTracking().Where(drug => drugIds.Contains(drug.Id)).ToDictionaryAsync(drug => drug.Id, cancellationToken);
		Dictionary<Guid, Supplier> supplierById = suppliers.ToDictionary(item => item.Id);
		var rows = new List<ClaimableBatchRow>();
		foreach (Batch batch in batches)
		{
			decimal available = onHand.GetValueOrDefault(batch.Id);
			if (available <= 0m || !drugs.TryGetValue(batch.DrugId, out Drug? drug))
			{
				continue;
			}
			origins.TryGetValue(batch.Id, out Origin? origin);
			Supplier? supplier = origin?.SupplierId is Guid supplierId ? supplierById.GetValueOrDefault(supplierId) : null;
			ExpiryReturnWindow window = WindowFor(supplier, profile);
			DateOnly expiry = batch.ExpiryDate!.Value;
			if (!window.Accepts(expiry, today))
			{
				continue;
			}
			(decimal rate, decimal gstRate) = RateOf(origin?.Item, batch, drug);
			rows.Add(new ClaimableBatchRow(batch.Id, drug.Id, drug.Name, drug.Unit ?? string.Empty, drug.HsnCode ?? string.Empty, batch.BatchNo, expiry,
				expiry.DayNumber - today.DayNumber, available, rate, gstRate, Round(rate * available), supplier?.Id, supplier?.Name ?? string.Empty,
				supplier != null, origin?.InvoiceNo, window.OpensOn(expiry), window.ClosesOn(expiry)));
		}
		return rows.OrderBy(row => row.SupplierName.Length == 0).ThenBy(row => row.SupplierName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(row => row.Expiry).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private sealed record Origin(PurchaseItem Item, Guid SupplierId, string InvoiceNo);

	// The purchase line that brought each batch in, and its supplier.
	private async Task<Dictionary<Guid, Origin>> OriginsAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken)
	{
		var items = await (from item in Context.PurchaseItems.AsNoTracking()
			join invoice in Context.PurchaseInvoices.AsNoTracking() on item.PurchaseInvoiceId equals invoice.Id
			where item.BatchId != null && batchIds.Contains(item.BatchId.Value)
			select new { Item = item, invoice.SupplierId, invoice.InvoiceNo }).ToListAsync(cancellationToken);
		return items.GroupBy(row => row.Item.BatchId!.Value)
			.ToDictionary(group => group.Key, group =>
			{
				var first = group.OrderBy(row => row.Item.CreatedAtUtc).First();
				return new Origin(first.Item, first.SupplierId, first.InvoiceNo);
			});
	}

	/// <summary>
	/// What the shop paid per unit: the purchase line's rate times quantity less its discount (before GST), over its billed quantity.
	/// Without a purchase line, the batch's own purchase price. GST at the purchase line's rate, else the product's.
	/// </summary>
	private static (decimal Rate, decimal GstRate) RateOf(PurchaseItem? item, Batch batch, Drug drug)
	{
		if (item != null && item.Quantity > 0m)
		{
			// The purchase line's total includes its GST; the claim works from the value before it.
			decimal value = item.UnitPrice * item.Quantity - item.DiscountAmount;
			return (Round(value / item.Quantity), item.TaxRate);
		}
		return (Round(batch.PurchasePrice), drug.IsNilRated ? 0m : drug.GstRate ?? 0m);
	}

	// ------------------------------------------------------------------ draft

	/// <summary>
	/// Builds a draft claim for one supplier and holds its quantities: from now they are not sellable. The stock is read
	/// inside this transaction, so two counters cannot claim, or claim and sell, the same strip.
	/// </summary>
	public async Task<ExpiryClaim> CreateDraftAsync(Guid supplierId, IReadOnlyList<ExpiryClaimLineRequest> requested, string? notes, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ExpiryClaimLineRequest[] lines = requested.Where(line => line.Quantity != 0m)
			.GroupBy(line => line.BatchId).Select(group => new ExpiryClaimLineRequest(group.Key, group.Sum(line => line.Quantity))).ToArray();
		if (lines.Length == 0 || lines.Any(line => line.Quantity <= 0m))
		{
			throw new InvalidOperationException("Tick at least one batch and enter a quantity above zero.");
		}
		DateOnly today = BusinessClock.Today;
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		Supplier supplier = await Context.Suppliers.SingleOrDefaultAsync(item => item.Id == supplierId, cancellationToken)
			?? throw new InvalidOperationException("Choose the supplier the goods go back to.");
		PharmacyProfile profile = await ProfileAsync(cancellationToken);
		ExpiryReturnWindow window = WindowFor(supplier, profile);
		Guid[] batchIds = lines.Select(line => line.BatchId).ToArray();
		Dictionary<Guid, Batch> batches = await Context.Batches.Where(batch => batchIds.Contains(batch.Id)).ToDictionaryAsync(batch => batch.Id, cancellationToken);
		Dictionary<Guid, Origin> origins = await OriginsAsync(batchIds, cancellationToken);
		Guid[] drugIds = batches.Values.Select(batch => batch.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await Context.Drugs.AsNoTracking().Where(drug => drugIds.Contains(drug.Id)).ToDictionaryAsync(drug => drug.Id, cancellationToken);

		// This read decides: whatever another counter sold or claimed before it is already out of these figures.
		var byLocation = await Context.StockMovements
			.Where(movement => batchIds.Contains(movement.BatchId))
			.GroupBy(movement => new { movement.BatchId, movement.LocationId })
			.Select(group => new { group.Key.BatchId, group.Key.LocationId, Quantity = group.Sum(movement => movement.QuantityChange) })
			.ToListAsync(cancellationToken);
		Guid? retailLocation = await Context.StorageLocations.Where(item => item.IsDefaultRetailLocation && item.IsActive).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);

		var claim = new ExpiryClaim
		{
			ClaimDate = today,
			Status = ExpiryClaimStatus.Draft,
			SupplierId = supplier.Id,
			Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
			CreatedByUserId = userId,
			CreatedByName = await UserNameAsync(userId, cancellationToken),
			PolicyDaysBeforeExpiry = window.DaysBefore,
			PolicyDaysAfterExpiry = window.DaysAfter,
			PolicyNote = supplier.ExpiryReturnPolicyNote ?? string.Empty
		};
		List<LicenceRecord> licences = await Context.LicenceRecords.AsNoTracking().OrderBy(item => item.LicenceType).ToListAsync(cancellationToken);
		RecordParties(claim, supplier, profile, string.Join(", ", licences
			.Where(item => !string.IsNullOrWhiteSpace(item.LicenceNumber) && (item.ExpiresOn == null || item.ExpiresOn >= today))
			.Select(item => (string.IsNullOrWhiteSpace(item.LicenceType) ? "" : item.LicenceType.Trim() + ": ") + item.LicenceNumber.Trim())));
		var claimLines = new List<ExpiryClaimLine>();
		foreach (ExpiryClaimLineRequest line in lines)
		{
			Batch batch = batches.GetValueOrDefault(line.BatchId) ?? throw new InvalidOperationException("A ticked batch no longer exists.");
			Drug drug = drugs[batch.DrugId];
			if (batch.IsBanned)
			{
				throw new InvalidOperationException($"{drug.Name} batch {batch.BatchNo} is on hold or banned; it is not in saleable condition to claim.");
			}
			if (batch.ExpiryDate is not DateOnly expiry || !window.Accepts(expiry, today))
			{
				throw new InvalidOperationException($"{drug.Name} batch {batch.BatchNo} is outside {supplier.Name}'s return window ({window.Describe().ToLowerInvariant()}).");
			}
			origins.TryGetValue(batch.Id, out Origin? origin);
			if (origin != null && origin.SupplierId != supplier.Id)
			{
				throw new InvalidOperationException($"{drug.Name} batch {batch.BatchNo} was bought from another supplier; claim it from that supplier.");
			}
			var places = byLocation.Where(row => row.BatchId == batch.Id && row.Quantity > 0m)
				.OrderByDescending(row => row.LocationId == retailLocation).ThenBy(row => row.LocationId == null).ThenByDescending(row => row.Quantity).ToList();
			decimal available = byLocation.Where(row => row.BatchId == batch.Id).Sum(row => row.Quantity);
			if (line.Quantity > available)
			{
				throw new InvalidOperationException($"Only {available:0.##} of {drug.Name} batch {batch.BatchNo} is in saleable stock; it may have been sold or claimed just now.");
			}
			(decimal rate, decimal gstRate) = RateOf(origin?.Item, batch, drug);
			decimal left = line.Quantity;
			foreach (var place in places)
			{
				if (left <= 0m)
				{
					break;
				}
				decimal take = Math.Min(left, place.Quantity);
				left -= take;
				ExpiryClaimLine claimLine = NewLine(claim, batch, drug, origin?.Item, take, rate, gstRate, place.LocationId);
				claimLines.Add(claimLine);
				Context.StockMovements.Add(new StockMovement
				{
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					LocationId = place.LocationId,
					QuantityChange = -take,
					MovementType = HoldMovement,
					ReferenceType = ReferenceType,
					ReferenceId = claim.Id,
					Notes = "Held for expiry return"
				});
				if (place.LocationId is Guid locationId)
				{
					await StorageLocationService.ApplyBalanceDeltaAsync(Context, locationId, batch.Id, batch.DrugId, -take, cancellationToken);
				}
			}
			if (left > 0m)
			{
				throw new InvalidOperationException($"{drug.Name} batch {batch.BatchNo}: the stock could not be placed in a storage location.");
			}
			batch.Quantity = available - line.Quantity;
		}
		Context.ExpiryClaimLines.AddRange(claimLines);
		claim.TaxableValue = claimLines.Sum(line => line.TaxableValue);
		claim.CgstAmount = claimLines.Sum(line => line.CgstAmount);
		claim.SgstAmount = claimLines.Sum(line => line.SgstAmount);
		claim.IgstAmount = claimLines.Sum(line => line.IgstAmount);
		claim.TotalValue = claimLines.Sum(line => line.LineValue);
		claim.ClaimNo = await numberSeries.AllocateAsync((string.IsNullOrWhiteSpace(profile.InvoicePrefix) ? "WIN1" : profile.InvoicePrefix.Trim()) + SeriesSuffix, today, cancellationToken);
		Context.ExpiryClaims.Add(claim);
		Audit(userId, "ExpiryClaimDrafted", claim, $"{claim.ClaimNo} to {claim.SupplierName}: {claimLines.Count} line(s), {claim.TotalValue:0.00} held for return");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return claim;
	}

	private static ExpiryClaimLine NewLine(ExpiryClaim claim, Batch batch, Drug drug, PurchaseItem? purchased, decimal quantity, decimal rate, decimal gstRate, Guid? locationId)
	{
		decimal taxable = Round(rate * quantity);
		decimal tax = Round(taxable * gstRate / 100m);
		var line = new ExpiryClaimLine
		{
			ExpiryClaimId = claim.Id,
			BatchId = batch.Id,
			DrugId = drug.Id,
			LocationId = locationId,
			PurchaseItemId = purchased?.Id,
			ItemName = drug.Name,
			Pack = drug.Unit ?? string.Empty,
			HsnCode = drug.HsnCode ?? string.Empty,
			BatchNo = batch.BatchNo,
			BatchExpiry = batch.ExpiryDate,
			Quantity = quantity,
			PurchaseRate = rate,
			GstRate = gstRate,
			TaxableValue = taxable,
			State = ExpiryClaimLineState.Held
		};
		if (claim.IsInterState)
		{
			line.IgstAmount = tax;
		}
		else
		{
			line.CgstAmount = Round(tax / 2m);
			line.SgstAmount = tax - line.CgstAmount;
		}
		line.LineValue = taxable + tax;
		return line;
	}

	// The supplier and the pharmacy as they stand now, recorded on the claim and never read again.
	private static void RecordParties(ExpiryClaim claim, Supplier supplier, PharmacyProfile profile, string pharmacyLicences)
	{
		string pharmacyState = IndianGstStateCodes.ResolveForTax(profile.State, profile.Gstin).Code ?? string.Empty;
		// A supplier is placed by its GSTIN; without one it is treated as local, as on a purchase return.
		string supplierState = IndianGstStateCodes.ResolveForTax(null, supplier.Gstin).Code ?? pharmacyState;
		claim.SupplierName = supplier.Name.Trim();
		claim.SupplierAddress = supplier.Address?.Trim() ?? string.Empty;
		claim.SupplierGstin = supplier.Gstin?.Trim() ?? string.Empty;
		claim.SupplierPhone = supplier.Phone?.Trim() ?? string.Empty;
		claim.SupplierDrugLicence = supplier.DrugLicenceNumber?.Trim() ?? string.Empty;
		claim.SupplierStateCode = supplierState;
		claim.PharmacyName = (string.IsNullOrWhiteSpace(profile.LegalName) ? profile.Name : profile.LegalName).Trim();
		claim.PharmacyAddress = profile.Address?.Trim() ?? string.Empty;
		claim.PharmacyGstin = profile.Gstin?.Trim() ?? string.Empty;
		claim.PharmacyPhone = profile.Phone?.Trim() ?? string.Empty;
		claim.PharmacyDrugLicence = pharmacyLicences;
		claim.PharmacyStateCode = pharmacyState;
		claim.IsInterState = pharmacyState.Length > 0 && supplierState.Length > 0 && !string.Equals(pharmacyState, supplierState, StringComparison.Ordinal);
	}

	// ------------------------------------------------------------------ void

	/// <summary>Cancels a draft: every held quantity goes back to saleable stock, where it came from. Kept, with an audit entry.</summary>
	public async Task VoidDraftAsync(Guid claimId, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaim claim = await ClaimAsync(claimId, cancellationToken);
		if (claim.Status != ExpiryClaimStatus.Draft)
		{
			throw new InvalidOperationException($"{claim.ClaimNo} has been sent; only a draft can be voided. Record the distributor's credit or rejection instead.");
		}
		List<ExpiryClaimLine> lines = await Context.ExpiryClaimLines.Where(line => line.ExpiryClaimId == claim.Id).ToListAsync(cancellationToken);
		foreach (ExpiryClaimLine line in lines.Where(line => line.State == ExpiryClaimLineState.Held))
		{
			await ReleaseAsync(claim, line, line.Quantity, "Expiry claim voided", cancellationToken);
			line.ReleasedQuantity = line.Quantity;
			line.State = ExpiryClaimLineState.Released;
		}
		claim.Status = ExpiryClaimStatus.Voided;
		claim.VoidedAtUtc = BusinessClock.UtcNow;
		claim.VoidedByUserId = userId;
		claim.VoidedByName = await UserNameAsync(userId, cancellationToken);
		claim.VoidReason = reason.Trim();
		claim.ClosedAtUtc = claim.VoidedAtUtc;
		Audit(userId, "ExpiryClaimVoided", claim, $"{claim.ClaimNo} voided ({reason.Trim()}); {lines.Sum(line => line.Quantity):0.##} unit(s) back to saleable stock");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	// Puts a held quantity back into saleable stock at the location it was taken from.
	private async Task ReleaseAsync(ExpiryClaim claim, ExpiryClaimLine line, decimal quantity, string note, CancellationToken cancellationToken)
	{
		Batch batch = await Context.Batches.SingleAsync(item => item.Id == line.BatchId, cancellationToken);
		decimal onHand = (await Context.StockMovements.Where(movement => movement.BatchId == batch.Id).SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken)) ?? 0m;
		Context.StockMovements.Add(new StockMovement
		{
			BatchId = line.BatchId,
			DrugId = line.DrugId,
			LocationId = line.LocationId,
			QuantityChange = quantity,
			MovementType = ReleaseMovement,
			ReferenceType = ReferenceType,
			ReferenceId = claim.Id,
			Notes = note
		});
		if (line.LocationId is Guid locationId)
		{
			await StorageLocationService.ApplyBalanceDeltaAsync(Context, locationId, line.BatchId, line.DrugId, quantity, cancellationToken);
		}
		batch.Quantity = onHand + quantity;
	}

	// ------------------------------------------------------------------ send

	/// <summary>The goods go to the distributor: they leave stock-in-hand and the claim's value is owed to the shop.</summary>
	public async Task MarkSentAsync(Guid claimId, DateOnly sentDate, string via, string? sentTo, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(via, nameof(via));
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaim claim = await ClaimAsync(claimId, cancellationToken);
		if (claim.Status != ExpiryClaimStatus.Draft)
		{
			throw new InvalidOperationException($"{claim.ClaimNo} has already been sent.");
		}
		if (sentDate < claim.ClaimDate || sentDate > BusinessClock.Today)
		{
			throw new InvalidOperationException("The date sent is between the claim date and today.");
		}
		List<ExpiryClaimLine> lines = await Context.ExpiryClaimLines.Where(line => line.ExpiryClaimId == claim.Id).ToListAsync(cancellationToken);
		Guid[] drugIds = lines.Select(line => line.DrugId).Distinct().ToArray();
		Dictionary<Guid, string?> schedules = await Context.Drugs.AsNoTracking().Where(drug => drugIds.Contains(drug.Id)).ToDictionaryAsync(drug => drug.Id, drug => drug.Schedule, cancellationToken);
		foreach (ExpiryClaimLine line in lines)
		{
			line.State = ExpiryClaimLineState.Sent;
			// Controlled medicines leaving the shop are entered in their register, as an outgoing purchase return is.
			string? schedule = schedules.GetValueOrDefault(line.DrugId)?.Trim().ToUpperInvariant();
			if (schedule is "H1" or "X" or "NDPS")
			{
				Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
				{
					RegisterType = schedule,
					DrugId = line.DrugId,
					SupplierId = claim.SupplierId,
					BatchId = line.BatchId,
					BatchNo = line.BatchNo,
					Quantity = line.Quantity,
					EntryAtUtc = BusinessClock.UtcNow,
					Notes = $"Outgoing expiry return {claim.ClaimNo} to {claim.SupplierName}"
				});
			}
		}
		claim.Status = ExpiryClaimStatus.Sent;
		claim.SentDate = sentDate;
		claim.SentAtUtc = BusinessClock.UtcNow;
		claim.SentVia = via.Trim();
		claim.SentTo = string.IsNullOrWhiteSpace(sentTo) ? null : sentTo.Trim();
		claim.SentByUserId = userId;
		claim.SentByName = await UserNameAsync(userId, cancellationToken);
		Audit(userId, "ExpiryClaimSent", claim, $"{claim.ClaimNo} sent {sentDate:dd-MMM-yyyy} via {claim.SentVia}{(claim.SentTo == null ? "" : " to " + claim.SentTo)}; {claim.TotalValue:0.00} owed");
		await RegisterRecordedBy.StampAsync(Context, userId, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	// ------------------------------------------------------------------ credit notes

	/// <summary>
	/// Records a distributor's credit note against a sent claim: which lines it covers, and any shortfall the pharmacist
	/// accepts with a reason. Adjusted against purchases, it reduces what the shop owes the distributor in the supplier
	/// ledger; paid in cash, it is recorded on the claim and the ledger is left as it is.
	/// </summary>
	public async Task<ExpiryClaimCredit> RecordCreditNoteAsync(Guid claimId, ExpiryCreditInput input, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(input.CreditNoteNo, nameof(input.CreditNoteNo));
		if (input.Amount < 0m || input.TaxableValue < 0m || input.CgstAmount < 0m || input.SgstAmount < 0m || input.IgstAmount < 0m)
		{
			throw new InvalidOperationException("Credit note amounts cannot be negative.");
		}
		if (input.CreditNoteDate > BusinessClock.Today)
		{
			throw new InvalidOperationException("The credit note date cannot be in the future.");
		}
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaim claim = await ClaimAsync(claimId, cancellationToken);
		if (claim.Status is not (ExpiryClaimStatus.Sent or ExpiryClaimStatus.PartCredited))
		{
			throw new InvalidOperationException($"{claim.ClaimNo} is {StatusText(claim.Status).ToLowerInvariant()}; a credit note is recorded against a sent claim.");
		}
		if (input.CreditNoteDate < claim.SentDate)
		{
			throw new InvalidOperationException("The credit note is dated before the goods were sent.");
		}
		if (await Context.ExpiryClaimCredits.AnyAsync(item => item.SupplierId == claim.SupplierId && item.CreditNoteNo == input.CreditNoteNo.Trim(), cancellationToken))
		{
			throw new InvalidOperationException($"Credit note {input.CreditNoteNo.Trim()} from {claim.SupplierName} is already recorded.");
		}
		Dictionary<Guid, ExpiryClaimLine> lines = await Context.ExpiryClaimLines.Where(line => line.ExpiryClaimId == claim.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
		ExpiryCreditLineInput[] covered = input.Lines.Where(line => line.Amount != 0m || !string.IsNullOrWhiteSpace(line.ShortfallReason)).ToArray();
		if (covered.Length == 0)
		{
			throw new InvalidOperationException("Say which claim lines this credit note covers.");
		}
		if (Round(covered.Sum(line => line.Amount)) != Round(input.Amount))
		{
			throw new InvalidOperationException($"The lines add up to {covered.Sum(line => line.Amount):0.00}, but the credit note is for {input.Amount:0.00}.");
		}
		var credit = new ExpiryClaimCredit
		{
			ExpiryClaimId = claim.Id,
			SupplierId = claim.SupplierId,
			CreditNoteNo = input.CreditNoteNo.Trim(),
			CreditNoteDate = input.CreditNoteDate,
			Amount = Round(input.Amount),
			TaxableValue = Round(input.TaxableValue),
			CgstAmount = Round(input.CgstAmount),
			SgstAmount = Round(input.SgstAmount),
			IgstAmount = Round(input.IgstAmount),
			Settlement = input.Settlement,
			Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
			RecordedByUserId = userId,
			RecordedByName = await UserNameAsync(userId, cancellationToken)
		};
		foreach (ExpiryCreditLineInput part in covered)
		{
			ExpiryClaimLine line = lines.GetValueOrDefault(part.ClaimLineId) ?? throw new InvalidOperationException("A credit line does not belong to this claim.");
			decimal open = ExpiryClaimMath.Open(line);
			if (part.Amount < 0m || Round(part.Amount) > open)
			{
				throw new InvalidOperationException($"{line.ItemName} batch {line.BatchNo}: {open:0.00} is still owed, so {part.Amount:0.00} cannot be credited against it.");
			}
			decimal shortfall = 0m;
			if (!string.IsNullOrWhiteSpace(part.ShortfallReason))
			{
				// The pharmacist accepts that the rest of this line will not be paid.
				shortfall = open - Round(part.Amount);
				line.ShortfallValue += shortfall;
				line.ShortfallReason = string.IsNullOrWhiteSpace(line.ShortfallReason) ? part.ShortfallReason.Trim() : line.ShortfallReason + "; " + part.ShortfallReason.Trim();
			}
			line.CreditedValue += Round(part.Amount);
			Context.ExpiryClaimCreditLines.Add(new ExpiryClaimCreditLine
			{
				ExpiryClaimCreditId = credit.Id,
				ExpiryClaimLineId = line.Id,
				Amount = Round(part.Amount),
				ShortfallValue = shortfall,
				ShortfallReason = string.IsNullOrWhiteSpace(part.ShortfallReason) ? null : part.ShortfallReason.Trim()
			});
		}
		if (credit.Settlement == ExpiryCreditSettlement.AdjustedAgainstPurchases && credit.Amount > 0m)
		{
			var entry = new SupplierLedgerEntry
			{
				SupplierId = claim.SupplierId,
				EntryAtUtc = BusinessClock.UtcNow,
				EntryType = LedgerEntryTypes.ExpiryClaimCredit,
				ReferenceId = credit.Id,
				ReferenceNo = credit.CreditNoteNo,
				Debit = credit.Amount,
				Credit = 0m,
				Notes = $"Expiry claim {claim.ClaimNo}: credit note {credit.CreditNoteNo}, adjusted against purchases"
			};
			Context.SupplierLedgerEntries.Add(entry);
			credit.SupplierLedgerEntryId = entry.Id;
		}
		Context.ExpiryClaimCredits.Add(credit);
		ExpiryClaimMath.Settle(claim, lines.Values);
		Audit(userId, "ExpiryClaimCreditRecorded", claim, $"{claim.ClaimNo}: credit note {credit.CreditNoteNo} for {credit.Amount:0.00} ({SettlementText(credit.Settlement)}); balance {ExpiryClaimMath.Balance(lines.Values):0.00}");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return credit;
	}

	// ------------------------------------------------------------------ rejection

	/// <summary>
	/// The distributor rejected some or all of a line. The pharmacist decides: write the goods off, or take them back as
	/// held stock (in the shop, not sellable). Nothing moves stock here: the goods left saleable stock when they were
	/// held, so writing them off does not take them out a second time.
	/// </summary>
	public async Task RejectLineAsync(Guid claimLineId, decimal quantity, string reason, RejectedGoodsAction action, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaimLine line = await Context.ExpiryClaimLines.SingleOrDefaultAsync(item => item.Id == claimLineId, cancellationToken)
			?? throw new InvalidOperationException("Claim line not found.");
		ExpiryClaim claim = await ClaimAsync(line.ExpiryClaimId, cancellationToken);
		if (claim.Status is not (ExpiryClaimStatus.Sent or ExpiryClaimStatus.PartCredited))
		{
			throw new InvalidOperationException($"{claim.ClaimNo} is {StatusText(claim.Status).ToLowerInvariant()}; rejections are recorded on a sent claim.");
		}
		decimal open = ExpiryClaimMath.Open(line);
		decimal openQuantity = line.LineValue == 0m ? line.Quantity - line.RejectedQuantity : Math.Round(line.Quantity * open / line.LineValue, 2);
		if (quantity <= 0m || quantity > openQuantity)
		{
			throw new InvalidOperationException($"{line.ItemName} batch {line.BatchNo}: up to {openQuantity:0.##} can still be rejected.");
		}
		decimal value = Math.Min(open, Round(line.LineValue * quantity / line.Quantity));
		string name = await UserNameAsync(userId, cancellationToken);
		line.RejectedQuantity += quantity;
		line.RejectedValue += value;
		line.RejectionReason = reason.Trim();
		line.RejectedAtUtc = BusinessClock.UtcNow;
		line.RejectedByUserId = userId;
		line.RejectedByName = name;
		if (action == RejectedGoodsAction.WriteOff)
		{
			line.WrittenOffQuantity += quantity;
			line.WrittenOffAtUtc = BusinessClock.UtcNow;
			line.WrittenOffByUserId = userId;
			line.WrittenOffByName = name;
		}
		UpdateLineState(line);
		List<ExpiryClaimLine> lines = await Context.ExpiryClaimLines.Where(item => item.ExpiryClaimId == claim.Id).ToListAsync(cancellationToken);
		ExpiryClaimMath.Settle(claim, lines);
		Audit(userId, action == RejectedGoodsAction.WriteOff ? "ExpiryClaimRejectedWrittenOff" : "ExpiryClaimRejectedHeld", claim,
			$"{claim.ClaimNo}: {line.ItemName} batch {line.BatchNo}, {quantity:0.##} rejected ({reason.Trim()}); " + (action == RejectedGoodsAction.WriteOff ? "written off" : "back in the shop as held stock"));
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	/// <summary>Rejected goods held in the shop: write them off now.</summary>
	public async Task WriteOffHeldRejectedAsync(Guid claimLineId, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaimLine line = await HeldRejectedLineAsync(claimLineId, cancellationToken);
		decimal held = ExpiryClaimMath.HeldRejected(line);
		line.WrittenOffQuantity += held;
		line.WrittenOffAtUtc = BusinessClock.UtcNow;
		line.WrittenOffByUserId = userId;
		line.WrittenOffByName = await UserNameAsync(userId, cancellationToken);
		UpdateLineState(line);
		ExpiryClaim claim = await ClaimAsync(line.ExpiryClaimId, cancellationToken);
		Audit(userId, "ExpiryClaimHeldWrittenOff", claim, $"{claim.ClaimNo}: {line.ItemName} batch {line.BatchNo}, {held:0.##} held after rejection, written off");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	/// <summary>Rejected goods held in the shop and not yet expired: put them back on sale.</summary>
	public async Task ReleaseHeldRejectedAsync(Guid claimLineId, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaimLine line = await HeldRejectedLineAsync(claimLineId, cancellationToken);
		if (line.BatchExpiry is DateOnly expiry && expiry < BusinessClock.Today)
		{
			throw new InvalidOperationException($"{line.ItemName} batch {line.BatchNo} expired on {expiry:dd-MMM-yyyy}; it cannot go back on sale. Write it off instead.");
		}
		decimal held = ExpiryClaimMath.HeldRejected(line);
		ExpiryClaim claim = await ClaimAsync(line.ExpiryClaimId, cancellationToken);
		await ReleaseAsync(claim, line, held, "Rejected expiry return put back on sale", cancellationToken);
		line.ReleasedQuantity += held;
		UpdateLineState(line);
		Audit(userId, "ExpiryClaimHeldReleased", claim, $"{claim.ClaimNo}: {line.ItemName} batch {line.BatchNo}, {held:0.##} back to saleable stock");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private async Task<ExpiryClaimLine> HeldRejectedLineAsync(Guid claimLineId, CancellationToken cancellationToken)
	{
		ExpiryClaimLine line = await Context.ExpiryClaimLines.SingleOrDefaultAsync(item => item.Id == claimLineId, cancellationToken)
			?? throw new InvalidOperationException("Claim line not found.");
		if (ExpiryClaimMath.HeldRejected(line) <= 0m)
		{
			throw new InvalidOperationException($"{line.ItemName} batch {line.BatchNo} has no rejected goods held in the shop.");
		}
		return line;
	}

	private static void UpdateLineState(ExpiryClaimLine line)
	{
		if (line.RejectedQuantity < line.Quantity)
		{
			line.State = ExpiryClaimLineState.Sent;
		}
		else if (ExpiryClaimMath.HeldRejected(line) > 0m)
		{
			line.State = ExpiryClaimLineState.RejectedHeld;
		}
		else
		{
			line.State = line.WrittenOffQuantity > 0m ? ExpiryClaimLineState.RejectedWrittenOff : ExpiryClaimLineState.Released;
		}
	}

	/// <summary>Gives up on what is still owed: the rest of every line becomes a shortfall with this reason, and the claim closes.</summary>
	public async Task WriteOffBalanceAsync(Guid claimId, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default)
	{
		await DemandAsync(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		ExpiryClaim claim = await ClaimAsync(claimId, cancellationToken);
		if (claim.Status is not (ExpiryClaimStatus.Sent or ExpiryClaimStatus.PartCredited))
		{
			throw new InvalidOperationException($"{claim.ClaimNo} has no balance to write off.");
		}
		List<ExpiryClaimLine> lines = await Context.ExpiryClaimLines.Where(item => item.ExpiryClaimId == claim.Id).ToListAsync(cancellationToken);
		decimal written = 0m;
		foreach (ExpiryClaimLine line in lines)
		{
			decimal open = ExpiryClaimMath.Open(line);
			if (open > 0m)
			{
				written += open;
				line.ShortfallValue += open;
				line.ShortfallReason = string.IsNullOrWhiteSpace(line.ShortfallReason) ? reason.Trim() : line.ShortfallReason + "; " + reason.Trim();
			}
		}
		ExpiryClaimMath.Settle(claim, lines);
		Audit(userId, "ExpiryClaimBalanceWrittenOff", claim, $"{claim.ClaimNo}: {written:0.00} not credited, written off ({reason.Trim()})");
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	// ------------------------------------------------------------------ reading

	public Task<IReadOnlyList<ExpiryClaimSummary>> GetClaimsAsync(bool open, CancellationToken cancellationToken = default) =>
		ExpiryClaimQueries.ClaimsAsync(Context, open, cancellationToken);

	public async Task<(ExpiryClaim Claim, IReadOnlyList<ExpiryClaimLine> Lines, IReadOnlyList<ExpiryClaimCredit> Credits)> GetClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
	{
		ExpiryClaim claim = await Context.ExpiryClaims.AsNoTracking().SingleOrDefaultAsync(item => item.Id == claimId, cancellationToken)
			?? throw new InvalidOperationException("Claim not found.");
		List<ExpiryClaimLine> lines = await Context.ExpiryClaimLines.AsNoTracking().Where(line => line.ExpiryClaimId == claimId)
			.OrderBy(line => line.ItemName).ThenBy(line => line.BatchNo).ToListAsync(cancellationToken);
		List<ExpiryClaimCredit> credits = await Context.ExpiryClaimCredits.AsNoTracking().Where(item => item.ExpiryClaimId == claimId)
			.OrderBy(item => item.CreditNoteDate).ThenBy(item => item.CreatedAtUtc).ToListAsync(cancellationToken);
		return (claim, lines, credits);
	}

	/// <summary>Held stock for the stock valuation: on draft claims, and rejected goods back in the shop.</summary>
	public Task<IReadOnlyList<HeldStockRow>> GetHeldStockAsync(CancellationToken cancellationToken = default) => ExpiryClaimQueries.HeldStockAsync(Context, cancellationToken);

	public Task<ReportTable> PendingAgeingAsync(CancellationToken cancellationToken = default) => ExpiryClaimQueries.PendingAgeingAsync(Context, cancellationToken);

	public Task<ReportTable> RecoveryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => ExpiryClaimQueries.RecoveryAsync(Context, from, to, cancellationToken);

	// ------------------------------------------------------------------ helpers

	public static string StatusText(ExpiryClaimStatus status) => status switch
	{
		ExpiryClaimStatus.Draft => "Draft",
		ExpiryClaimStatus.Sent => "Sent",
		ExpiryClaimStatus.PartCredited => "Part credited",
		ExpiryClaimStatus.Credited => "Credited",
		ExpiryClaimStatus.Rejected => "Rejected",
		ExpiryClaimStatus.WrittenOff => "Written off",
		ExpiryClaimStatus.Voided => "Voided",
		_ => status.ToString()
	};

	public static string SettlementText(ExpiryCreditSettlement settlement) =>
		settlement == ExpiryCreditSettlement.Cash ? "paid in cash" : "adjusted against purchases";

	private async Task<ExpiryClaim> ClaimAsync(Guid claimId, CancellationToken cancellationToken) =>
		await Context.ExpiryClaims.SingleOrDefaultAsync(item => item.Id == claimId, cancellationToken)
		?? throw new InvalidOperationException("Claim not found.");

	private async Task DemandAsync(UserRole role, CancellationToken cancellationToken)
	{
		if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
		{
			throw new UnauthorizedAccessException("Your role cannot make expiry returns.");
		}
		if (!await entitlement.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken))
		{
			throw new InvalidOperationException("Expiry returns are unavailable in read-only mode.");
		}
	}

	private async Task<string> UserNameAsync(Guid userId, CancellationToken cancellationToken) =>
		await Context.AppUsers.IgnoreQueryFilters().Where(user => user.Id == userId)
			.Select(user => user.DisplayName != "" ? user.DisplayName : user.UserName).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

	private void Audit(Guid userId, string action, ExpiryClaim claim, string details) =>
		Context.AuditLogs.Add(new AuditLog { UserId = userId, Action = action, EntityName = "ExpiryClaim", EntityId = claim.Id, Details = details });
}

/// <summary>What is owed on a claim and where it stands, from its recorded lines.</summary>
public static class ExpiryClaimMath
{
	/// <summary>What is still owed on a sent line: its value less credit, rejection and accepted shortfall.</summary>
	public static decimal Open(ExpiryClaimLine line) =>
		Math.Max(0m, line.LineValue - line.CreditedValue - line.RejectedValue - line.ShortfallValue);

	public static decimal Balance(IEnumerable<ExpiryClaimLine> lines) => lines.Sum(Open);

	/// <summary>Rejected goods back in the shop and not yet written off or put back on sale.</summary>
	public static decimal HeldRejected(ExpiryClaimLine line) => Math.Max(0m, line.RejectedQuantity - line.WrittenOffQuantity - line.ReleasedQuantity);

	/// <summary>The line's quantity held in the shop now: all of a draft line, or rejected goods taken back.</summary>
	public static decimal HeldQuantity(ExpiryClaimLine line) => line.State == ExpiryClaimLineState.Held ? line.Quantity : HeldRejected(line);

	/// <summary>Sets a sent claim's status from its lines: part credited while something is owed, closed when nothing is.</summary>
	public static void Settle(ExpiryClaim claim, IEnumerable<ExpiryClaimLine> claimLines)
	{
		ExpiryClaimLine[] lines = claimLines.ToArray();
		decimal balance = Balance(lines);
		decimal credited = lines.Sum(line => line.CreditedValue);
		if (balance > 0m)
		{
			claim.Status = credited > 0m ? ExpiryClaimStatus.PartCredited : ExpiryClaimStatus.Sent;
			claim.ClosedAtUtc = null;
			return;
		}
		if (credited > 0m)
		{
			claim.Status = ExpiryClaimStatus.Credited;
		}
		else
		{
			claim.Status = lines.All(line => line.RejectedQuantity >= line.Quantity) ? ExpiryClaimStatus.Rejected : ExpiryClaimStatus.WrittenOff;
		}
		claim.ClosedAtUtc ??= BusinessClock.UtcNow;
	}
}

/// <summary>Read-only views of the claims: the open and closed lists and the two reports. Used by the screen and by Reports.</summary>
public static class ExpiryClaimQueries
{
	public static async Task<IReadOnlyList<ExpiryClaimSummary>> ClaimsAsync(PharmaBillDbContext Context, bool open, CancellationToken cancellationToken = default)
	{
		ExpiryClaimStatus[] openStatuses = [ExpiryClaimStatus.Draft, ExpiryClaimStatus.Sent, ExpiryClaimStatus.PartCredited];
		List<ExpiryClaim> claims = await Context.ExpiryClaims.AsNoTracking()
			.Where(claim => openStatuses.Contains(claim.Status) == open)
			.ToListAsync(cancellationToken);
		Guid[] ids = claims.Select(claim => claim.Id).ToArray();
		ILookup<Guid, ExpiryClaimLine> lines = (await Context.ExpiryClaimLines.AsNoTracking().Where(line => ids.Contains(line.ExpiryClaimId)).ToListAsync(cancellationToken))
			.ToLookup(line => line.ExpiryClaimId);
		DateOnly today = BusinessClock.Today;
		IEnumerable<ExpiryClaimSummary> rows = claims.Select(claim =>
		{
			ExpiryClaimLine[] own = lines[claim.Id].ToArray();
			decimal balance = claim.Status is ExpiryClaimStatus.Draft or ExpiryClaimStatus.Voided ? 0m : ExpiryClaimMath.Balance(own);
			int? days = claim.SentDate is DateOnly sent && claim.Status is ExpiryClaimStatus.Sent or ExpiryClaimStatus.PartCredited ? today.DayNumber - sent.DayNumber : null;
			return new ExpiryClaimSummary(claim.Id, claim.ClaimNo, claim.ClaimDate, claim.Status, claim.SupplierId, claim.SupplierName, own.Length,
				claim.TotalValue, own.Sum(line => line.CreditedValue), balance, claim.SentDate, days);
		});
		// Open claims oldest first: the ones that get forgotten. Closed: most recent first.
		return open
			? rows.OrderByDescending(row => row.Status != ExpiryClaimStatus.Draft).ThenByDescending(row => row.DaysOutstanding ?? -1).ThenBy(row => row.ClaimDate).ThenBy(row => row.ClaimNo).ToList()
			: rows.OrderByDescending(row => row.ClaimDate).ThenByDescending(row => row.ClaimNo).ToList();
	}


	/// <summary>Pending claims by supplier: claimed, credited, balance and how long the oldest has been waiting.</summary>
	public static async Task<ReportTable> PendingAgeingAsync(PharmaBillDbContext Context, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<ExpiryClaimSummary> open = (await ClaimsAsync(Context, open: true, cancellationToken)).Where(row => row.Status != ExpiryClaimStatus.Draft).ToList();
		var rows = open.GroupBy(row => (row.SupplierId, row.SupplierName))
			.Select(group => new
			{
				Supplier = group.Key.SupplierName,
				Claims = group.Count(),
				Claimed = group.Sum(row => row.TotalValue),
				Credited = group.Sum(row => row.CreditedValue),
				Balance = group.Sum(row => row.Balance),
				Oldest = group.Max(row => row.DaysOutstanding ?? 0),
				Over60 = group.Where(row => row.DaysOutstanding > 60).Sum(row => row.Balance)
			})
			.OrderByDescending(row => row.Oldest).ThenByDescending(row => row.Balance)
			.Select(row => (IReadOnlyList<string>)[row.Supplier, row.Claims.ToString(CultureInfo.InvariantCulture), Money(row.Claimed), Money(row.Credited), Money(row.Balance), row.Oldest.ToString(CultureInfo.InvariantCulture), Money(row.Over60)])
			.ToList();
		return new ReportTable("Pending expiry claims ageing", ["Supplier", "Open claims", "Claimed", "Credited", "Balance", "Days outstanding (oldest)", "Balance over 60 days"], rows);
	}

	/// <summary>
	/// Recovery by distributor over a period (claims dated in it, sent or later): claimed against credited, as a value and a
	/// percentage, with what was rejected and what was written off.
	/// </summary>
	public static async Task<ReportTable> RecoveryAsync(PharmaBillDbContext Context, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		List<ExpiryClaim> claims = await Context.ExpiryClaims.AsNoTracking()
			.Where(claim => claim.ClaimDate >= from && claim.ClaimDate <= to && claim.Status != ExpiryClaimStatus.Draft && claim.Status != ExpiryClaimStatus.Voided)
			.ToListAsync(cancellationToken);
		Guid[] ids = claims.Select(claim => claim.Id).ToArray();
		ILookup<Guid, ExpiryClaimLine> lines = (await Context.ExpiryClaimLines.AsNoTracking().Where(line => ids.Contains(line.ExpiryClaimId)).ToListAsync(cancellationToken)).ToLookup(line => line.ExpiryClaimId);
		var rows = claims.GroupBy(claim => (claim.SupplierId, claim.SupplierName))
			.Select(group =>
			{
				ExpiryClaimLine[] own = group.SelectMany(claim => lines[claim.Id]).ToArray();
				decimal claimed = group.Sum(claim => claim.TotalValue);
				decimal credited = own.Sum(line => line.CreditedValue);
				return new
				{
					Supplier = group.Key.SupplierName,
					Claims = group.Count(),
					Claimed = claimed,
					Credited = credited,
					Rejected = own.Sum(line => line.RejectedValue),
					Shortfall = own.Sum(line => line.ShortfallValue),
					Pending = own.Sum(ExpiryClaimMath.Open),
					Recovery = claimed == 0m ? 0m : Math.Round(credited * 100m / claimed, 1)
				};
			})
			.OrderBy(row => row.Recovery).ThenByDescending(row => row.Claimed)
			.Select(row => (IReadOnlyList<string>)[row.Supplier, row.Claims.ToString(CultureInfo.InvariantCulture), Money(row.Claimed), Money(row.Credited), row.Recovery.ToString("0.0", CultureInfo.InvariantCulture) + "%", Money(row.Rejected), Money(row.Shortfall), Money(row.Pending)])
			.ToList();
		return new ReportTable("Expiry claim recovery by distributor", ["Supplier", "Claims", "Claimed", "Credited", "Recovery %", "Rejected", "Shortfall accepted", "Still owed"], rows);
	}

	private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

	/// <summary>Held stock for the stock valuation: on draft claims, and rejected goods back in the shop.</summary>
	public static async Task<IReadOnlyList<HeldStockRow>> HeldStockAsync(PharmaBillDbContext Context, CancellationToken cancellationToken = default)
	{
		var rows = await (from line in Context.ExpiryClaimLines.AsNoTracking()
			join claim in Context.ExpiryClaims.AsNoTracking() on line.ExpiryClaimId equals claim.Id
			where line.State == ExpiryClaimLineState.Held || line.State == ExpiryClaimLineState.RejectedHeld || line.State == ExpiryClaimLineState.Sent
			select new { Line = line, claim.ClaimNo }).ToListAsync(cancellationToken);
		return rows.Select(row => new { row.Line, row.ClaimNo, Quantity = ExpiryClaimMath.HeldQuantity(row.Line) })
			.Where(row => row.Quantity > 0m)
			.Select(row => new HeldStockRow(row.Line.BatchId, row.Line.DrugId, row.Line.ItemName, row.Line.BatchNo, row.Quantity, RetailTaxCalculator.RoundMoney(row.Line.PurchaseRate * row.Quantity), row.ClaimNo))
			.ToList();
	}

}
