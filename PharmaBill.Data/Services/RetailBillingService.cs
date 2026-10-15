using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Loyalty;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class RetailBillingService(IUnitOfWork unitOfWork, NumberSeriesService numberSeriesService, IEntitlementService entitlementService, CatalogSearchService catalogSearchService, StorageLocationService storageLocations, BranchService branchService, ILicenseRuntimeGuard licenseRuntimeGuard, string? prescriptionStorageDirectory = null)
{
	private sealed record PreparedSaleLine(RetailSaleLineInput Input, Drug Drug, Batch Batch, string? Schedule, bool RequiresPrescription, bool IsHabitForming, string? RegisterType, InclusiveTaxLine Tax);

	/// <summary>Loyalty points for the saved patient with this phone number, or null when there is none.</summary>
	public async Task<int?> GetLoyaltyPointsAsync(string phone, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(phone))
		{
			return null;
		}
		string normalized = NormalizePhone(phone);
		return await unitOfWork.Context.Patients.AsNoTracking()
			.Where((Patient patient) => patient.Phone == normalized)
			.Select((Patient patient) => (int?)patient.LoyaltyPoints)
			.SingleOrDefaultAsync(cancellationToken);
	}

	// Saved patients whose phone starts with the digits typed so far (at least 4 digits).
	public async Task<IReadOnlyList<RetailPatientMatch>> SearchPatientsByPhoneAsync(string phoneText, CancellationToken cancellationToken = default(CancellationToken))
	{
		string digits = new string((phoneText ?? string.Empty).Where(char.IsDigit).ToArray());
		if (digits.Length < 4)
		{
			return Array.Empty<RetailPatientMatch>();
		}
		return await unitOfWork.Context.Patients.AsNoTracking()
			.Where((Patient patient) => patient.Phone != null && patient.Phone.StartsWith(digits))
			.OrderBy((Patient patient) => patient.Name)
			.Take(8)
			.Select((Patient patient) => new RetailPatientMatch(patient.Id, patient.Name, patient.Phone!, patient.Address, patient.LoyaltyPoints))
			.ToListAsync(cancellationToken);
	}

	public async Task<Guid?> FindPatientIdByPhoneAsync(string phone, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(phone))
		{
			return null;
		}
		string normalized = NormalizePhone(phone);
		return await unitOfWork.Context.Patients.AsNoTracking()
			.Where((Patient patient) => patient.Phone == normalized)
			.Select((Patient patient) => (Guid?)patient.Id)
			.SingleOrDefaultAsync(cancellationToken);
	}

	// Every bill this patient has, newest first, with the medicines on each bill.
	public async Task<IReadOnlyList<RetailPatientBill>> GetPatientHistoryAsync(Guid patientId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		List<Sale> sales = await context.Sales.AsNoTracking()
			.Where((Sale sale) => sale.PatientId == patientId)
			.OrderByDescending((Sale sale) => sale.SaleAtUtc)
			.Take(200)
			.ToListAsync(cancellationToken);
		Guid[] saleIds = sales.Select((Sale sale) => sale.Id).ToArray();
		var items = await (from item in context.SaleItems.AsNoTracking()
			join drug in context.Drugs.AsNoTracking() on item.DrugId equals drug.Id
			where saleIds.Contains(item.SaleId)
			select new { item.SaleId, item.DrugId, drug.Name, item.Quantity }).ToListAsync(cancellationToken);
		return sales.Select((Sale sale) => new RetailPatientBill(
			sale.Id,
			sale.InvoiceNo,
			sale.SaleAtUtc,
			sale.TotalAmount,
			sale.PaidAmount,
			sale.PaymentStatus,
			items.Where(item => item.SaleId == sale.Id)
				.Select(item => new RetailPatientBillLine(item.DrugId, item.Name, item.Quantity))
				.ToList())).ToList();
	}

	public async Task<IReadOnlyList<RetailStockChoice>> SearchStockAsync(string query, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query, "query");
		StorageLocation retailLocation = await storageLocations.GetDefaultRetailLocationAsync(cancellationToken);
		PharmaBillDbContext context = unitOfWork.Context;
		string searchTerm = query.Trim();
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive && (drug.Barcode == searchTerm || EF.Functions.Like(drug.Name, $"%{searchTerm}%") || EF.Functions.Like(drug.BrandName ?? string.Empty, $"%{searchTerm}%") || EF.Functions.Like(drug.GenericName ?? string.Empty, $"%{searchTerm}%"))
			select drug).Take(50).ToListAsync(cancellationToken);
		if (searchTerm.Length >= 2 && drugs.Count == 0)
		{
			MedicineSearchResults medicineSearchResults = await catalogSearchService.SearchAsync(searchTerm, cancellationToken);
			Guid[] catalogIds = (from result in medicineSearchResults.InStock.Concat(medicineSearchResults.FromCatalog)
				where result.IsInStock
				select result.CatalogMedicineId).Distinct().ToArray();
			if (catalogIds.Length != 0)
			{
				List<Drug> source = await (from drug in context.Drugs.AsNoTracking()
					where drug.IsActive && drug.CatalogMedicineId.HasValue && catalogIds.Contains(drug.CatalogMedicineId.Value)
					select drug).ToListAsync(cancellationToken);
				drugs.AddRange(source.Where((Drug linked) => drugs.All((Drug existing) => existing.Id != linked.Id)));
			}
		}
		if (drugs.Count == 0)
		{
			return Array.Empty<RetailStockChoice>();
		}
		Guid[] drugIds = drugs.Select((Drug drug) => drug.Id).Distinct().ToArray();
		List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
			where drugIds.Contains(batch.DrugId)
			select batch).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch) => batch.Id).ToArray();
		Dictionary<Guid, decimal> stock = await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId) && movement.LocationId == retailLocation.Id
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		Guid[] catalogMedicineIds = (from drug in drugs
			where drug.CatalogMedicineId.HasValue
			select drug.CatalogMedicineId.Value).Distinct().ToArray();
		string[] nameKeys = (from drug in drugs
			select drug.Name.Trim().ToLowerInvariant() into key
			where key.Length > 0
			select key).Distinct().ToArray();
		List<CatalogInfo> catalogInfo = await (from info in context.CatalogInfos.AsNoTracking()
			where (info.CatalogMedicineId.HasValue && catalogMedicineIds.Contains(info.CatalogMedicineId.Value)) || nameKeys.Contains(info.NameKey)
			select info).ToListAsync(cancellationToken);
		List<ScheduleOverride> overrides = await (from item in context.ScheduleOverrides.AsNoTracking()
			where drugIds.Contains(item.DrugId)
			select item).ToListAsync(cancellationToken);
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		return (from choice in drugs.SelectMany((Drug drug) => (from batch in batches
				where batch.DrugId == drug.Id
				where !batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= today
				where stock.GetValueOrDefault(batch.Id) > 0m
				orderby batch.ExpiryDate ?? DateOnly.MaxValue
				select batch).ThenBy((Batch batch) => batch.BatchNo, StringComparer.OrdinalIgnoreCase).Select((Batch batch) =>
			{
				CatalogInfo catalogInfo2 = FindCatalogInfo(drug, catalogInfo);
				return new RetailStockChoice(drug.Id, drug.Name, drug.Barcode, batch.Id, batch.BatchNo, batch.ExpiryDate, batch.Mrp ?? drug.Mrp.GetValueOrDefault(), batch.SalePrice ?? drug.SalePrice, stock.GetValueOrDefault(batch.Id), drug.GstRate.GetValueOrDefault(), ResolveSchedule(drug, catalogInfo2, overrides.Where((ScheduleOverride item) => item.DrugId == drug.Id).ToList()), catalogInfo2?.IsHabitForming ?? false, catalogInfo2?.RegisterType, drug.IsBanned || batch.IsBanned);
			}))
			orderby choice.Barcode == searchTerm descending
			select choice).ThenBy((RetailStockChoice choice) => choice.DrugName, StringComparer.OrdinalIgnoreCase).ThenBy((RetailStockChoice choice) => choice.ExpiryDate ?? DateOnly.MaxValue).ToArray();
	}

	public async Task<RetailStockChoice?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(barcode, "barcode");
		string term = barcode.Trim();
		PharmaBillDbContext context = unitOfWork.Context;
		var drugMatch = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive && drug.Barcode == term
			select new { drug.Id }).FirstOrDefaultAsync(cancellationToken);
		if (drugMatch != null)
		{
			IReadOnlyList<RetailStockChoice> source = await SearchStockAsync(term, cancellationToken);
			return source.FirstOrDefault((RetailStockChoice choice) => choice.DrugId == drugMatch.Id) ?? source.FirstOrDefault();
		}
		var batchMatch = await (from batch in context.Batches.AsNoTracking()
			where batch.BatchNo == term
			select new { batch.Id, batch.DrugId }).FirstOrDefaultAsync(cancellationToken);
		if (batchMatch == null)
		{
			return null;
		}
		string text = await (from drug in context.Drugs.AsNoTracking()
			where drug.Id == batchMatch.DrugId && drug.IsActive
			select drug.Name).FirstOrDefaultAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		IReadOnlyList<RetailStockChoice> source2 = await SearchStockAsync(text, cancellationToken);
		return source2.FirstOrDefault((RetailStockChoice choice) => choice.BatchId == batchMatch.Id) ?? source2.FirstOrDefault((RetailStockChoice choice) => choice.DrugId == batchMatch.DrugId);
	}

	public async Task<IReadOnlyDictionary<Guid, RetailStockChoice>> GetFefoChoicesAsync(IReadOnlyCollection<Guid> drugIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		Dictionary<Guid, RetailStockChoice> result = new Dictionary<Guid, RetailStockChoice>();
		foreach (var drug in await (from drug2 in unitOfWork.Context.Drugs.AsNoTracking()
			where drugIds.Contains(drug2.Id)
			select new { drug2.Id, drug2.Name }).ToListAsync(cancellationToken))
		{
			RetailStockChoice retailStockChoice = (from choice in await SearchStockAsync(drug.Name, cancellationToken)
				where choice.DrugId == drug.Id
				orderby choice.ExpiryDate ?? DateOnly.MaxValue
				select choice).FirstOrDefault();
			if ((object)retailStockChoice != null)
			{
				result[drug.Id] = retailStockChoice;
			}
		}
		return result;
	}

	public async Task<string> PreviewNextInvoiceNoAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string text = await (from profile in unitOfWork.Context.PharmacyProfiles.AsNoTracking()
			select profile.InvoicePrefix).FirstOrDefaultAsync(cancellationToken);
		return await numberSeriesService.PreviewNextAsync(string.IsNullOrWhiteSpace(text) ? "WIN1" : text, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
	}

	public async Task<RetailSaleResult> SaveSaleAsync(SaveRetailSaleInput input, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(input, "input");
		if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
		{
			throw new UnauthorizedAccessException("Your role cannot create retail bills.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.CreateBill, cancellationToken)))
		{
			throw new InvalidOperationException("Billing is unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(input.PatientName, "input.PatientName");
		string phone = NormalizePhone(input.PatientPhone);
		if (phone.Count(char.IsDigit) < 7)
		{
			throw new InvalidOperationException("Enter a valid patient phone number.");
		}
		if (input.Items.Count == 0)
		{
			throw new InvalidOperationException("Add at least one medicine to the bill.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		StorageLocation retailLocation = await storageLocations.GetDefaultRetailLocationAsync(cancellationToken);
		PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
		if (pharmacyProfile.BusinessMode == BusinessMode.Wholesaler)
		{
			throw new InvalidOperationException("Retail billing is unavailable in Wholesaler mode.");
		}
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		Guid[] drugIds = input.Items.Select((RetailSaleLineInput item) => item.DrugId).Distinct().ToArray();
		Guid[] batchIds = input.Items.Select((RetailSaleLineInput item) => item.BatchId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await context.Drugs.Where((Drug drug2) => drugIds.Contains(drug2.Id) && drug2.IsActive).ToDictionaryAsync((Drug drug2) => drug2.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await context.Batches.Where((Batch batch3) => batchIds.Contains(batch3.Id)).ToDictionaryAsync((Batch batch3) => batch3.Id, cancellationToken);
		if (drugs.Count != drugIds.Length || batches.Count != batchIds.Length || input.Items.Any((RetailSaleLineInput item) => !batches.TryGetValue(item.BatchId, out var value) || value.DrugId != item.DrugId))
		{
			throw new InvalidOperationException("One or more bill items refer to an unavailable medicine or batch.");
		}
		Dictionary<Guid, decimal> currentQuantities = await (from movement in context.StockMovements
			where batchIds.Contains(movement.BatchId) && movement.LocationId == retailLocation.Id
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		List<CatalogInfo> infos = await context.CatalogInfos.AsNoTracking().ToListAsync(cancellationToken);
		List<ScheduleOverride> source = await (from item in context.ScheduleOverrides.AsNoTracking()
			where drugIds.Contains(item.DrugId)
			select item).ToListAsync(cancellationToken);
		List<PreparedSaleLine> preparedLines = new List<PreparedSaleLine>(input.Items.Count);
		foreach (RetailSaleLineInput item in input.Items)
		{
			if (item.Quantity <= 0m || item.DiscountAmount < 0m || item.UnitPrice < 0m)
			{
				throw new InvalidOperationException("Quantity and price must be positive; discount cannot be negative.");
			}
			RetailSaleLineInput retailSaleLineInput = item with
			{
				DiscountAmount = RetailTaxCalculator.RoundMoney(item.DiscountAmount)
			};
			Drug drug = drugs[item.DrugId];
			Batch batch = batches[item.BatchId];
			if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < today)
			{
				throw new InvalidOperationException("Expired batch " + batch.BatchNo + " cannot be sold.");
			}
			decimal valueOrDefault = currentQuantities.GetValueOrDefault(batch.Id);
			if (item.Quantity > valueOrDefault)
			{
				throw new InvalidOperationException($"Quantity exceeds available stock for {drug.Name}, batch {batch.BatchNo} ({valueOrDefault} available).");
			}
			decimal? num = batch.Mrp ?? drug.Mrp;
			if (!num.HasValue || num.Value <= 0m)
			{
				throw new InvalidOperationException("A valid MRP is required for " + drug.Name + " before it can be billed.");
			}
			if (item.UnitPrice > num.Value)
			{
				throw new InvalidOperationException("Selling price for " + drug.Name + " cannot exceed its batch MRP.");
			}
			CatalogInfo catalogInfo = FindCatalogInfo(drug, infos);
			string text = ResolveSchedule(drug, catalogInfo, source.Where((ScheduleOverride item) => item.DrugId == drug.Id).ToList());
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
			bool requiresPrescription = flag;
			preparedLines.Add(new PreparedSaleLine(retailSaleLineInput, drug, batch, text, requiresPrescription, catalogInfo?.IsHabitForming ?? false, catalogInfo?.RegisterType, RetailTaxCalculator.SplitInclusive(retailSaleLineInput.Quantity, retailSaleLineInput.UnitPrice, drug.GstRate.GetValueOrDefault(), retailSaleLineInput.DiscountAmount)));
			currentQuantities[batch.Id] = valueOrDefault - retailSaleLineInput.Quantity;
		}
		PreparedSaleLine[] controlledItems = preparedLines.Where((PreparedSaleLine preparedSaleLine) => preparedSaleLine.RequiresPrescription).ToArray();
		if (controlledItems.Length != 0)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(input.PatientAddress, "input.PatientAddress");
			ArgumentException.ThrowIfNullOrWhiteSpace(input.PrescriberName, "input.PrescriberName");
			ArgumentException.ThrowIfNullOrWhiteSpace(input.PrescriberRegistrationNumber, "input.PrescriberRegistrationNumber");
			if (string.IsNullOrWhiteSpace(input.PrescriptionDocumentPath) && !input.PrescriptionAdminOverride)
			{
				throw new InvalidOperationException("Attach a prescription image or PDF for H1, X, and NDPS items.");
			}
		}
		decimal subtotal = preparedLines.Sum((PreparedSaleLine preparedSaleLine) => preparedSaleLine.Tax.NetAmount);
		decimal taxAmount = preparedLines.Sum((PreparedSaleLine preparedSaleLine) => preparedSaleLine.Tax.TaxAmount);
		decimal discountAmount = preparedLines.Sum((PreparedSaleLine preparedSaleLine) => RetailTaxCalculator.RoundMoney(preparedSaleLine.Input.DiscountAmount));
		decimal totalAmount = preparedLines.Sum((PreparedSaleLine preparedSaleLine) => preparedSaleLine.Tax.GrossAmount);
		decimal appliedPayments = ValidatePayments(input, totalAmount);
		decimal num2 = input.Payments.Where((RetailPaymentInput retailPaymentInput) => retailPaymentInput.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase)).Sum((RetailPaymentInput retailPaymentInput) => (!(retailPaymentInput.TenderedAmount > 0m)) ? retailPaymentInput.AppliedAmount : retailPaymentInput.TenderedAmount);
		decimal num3 = input.Payments.Where((RetailPaymentInput retailPaymentInput) => retailPaymentInput.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase)).Sum((RetailPaymentInput retailPaymentInput) => retailPaymentInput.AppliedAmount);
		decimal changeDue = RetailTaxCalculator.RoundMoney(Math.Max(0m, num2 - num3));
		string? attachmentPath = null;
		string? stagedPendingPath = IsPendingArchive(input.PrescriptionDocumentPath) ? Path.GetFullPath(input.PrescriptionDocumentPath) : null;
		bool shouldKeepAttachment = false;
		try
		{
			RetailSaleResult result;
			await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
			{
				Patient patient = await context.Patients.SingleOrDefaultAsync((Patient item) => item.Phone == phone, cancellationToken);
				if (patient == null)
				{
					patient = new Patient
					{
						Name = input.PatientName.Trim(),
						Phone = phone,
						Address = NullIfWhiteSpace(input.PatientAddress)
					};
					context.Patients.Add(patient);
				}
				else
				{
					patient.Name = input.PatientName.Trim();
					patient.Address = NullIfWhiteSpace(input.PatientAddress);
				}
				if (input.AddToCustomers)
					{
						await EnsureConsumerCustomerAsync(context, input.PatientName.Trim(), phone, NullIfWhiteSpace(input.PatientAddress), cancellationToken);
					}
					string invoicePrefix = await branchService.ResolveInvoiceSeriesPrefixAsync(cancellationToken);
				Branch branch = await branchService.EnsureCurrentBranchAsync(cancellationToken);
				string invoiceNo = await numberSeriesService.AllocateAsync(invoicePrefix, today, cancellationToken);
				attachmentPath = await PrescriptionArchive.StoreForBillAsync(input.PrescriptionDocumentPath, controlledItems.Length != 0 && !input.PrescriptionAdminOverride, string.IsNullOrWhiteSpace(prescriptionStorageDirectory) ? null : prescriptionStorageDirectory, invoiceNo, cancellationToken);
				Prescription prescription = null;
				if (controlledItems.Length != 0 || attachmentPath != null || !string.IsNullOrWhiteSpace(input.PrescriberName) || !string.IsNullOrWhiteSpace(input.PrescriberRegistrationNumber))
				{
					prescription = new Prescription
					{
						PatientId = patient.Id,
						PrescriberName = NullIfWhiteSpace(input.PrescriberName),
						PrescriberRegistrationNumber = NullIfWhiteSpace(input.PrescriberRegistrationNumber),
						PrescriptionDate = today,
						Notes = ((attachmentPath == null) ? null : ("DocumentPath=" + attachmentPath))
					};
					context.Prescriptions.Add(prescription);
				}
				decimal paidAmount = appliedPayments;
				decimal loyaltyRedeemed = RetailTaxCalculator.RoundMoney(input.Payments.Where((RetailPaymentInput payment) => IsLoyalty(payment.Method)).Sum((RetailPaymentInput payment) => payment.AppliedAmount));
				if (loyaltyRedeemed > 0m)
				{
					if (loyaltyRedeemed > patient.LoyaltyPoints)
					{
						throw new InvalidOperationException($"Only {patient.LoyaltyPoints} loyalty points are available for this patient.");
					}
					if (RetailTaxCalculator.RoundMoney(totalAmount - paidAmount) > 0m)
					{
						throw new InvalidOperationException("Loyalty points cover only part of the bill. Add the rest as cash, UPI or card.");
					}
					patient.LoyaltyPoints -= (int)loyaltyRedeemed;
				}
				string? pharmacyStateCode = IndianGstStateCodes.ResolveForTax(pharmacyProfile.State, pharmacyProfile.Gstin).Code;
				SellerSnapshot sellerSnapshot = await TaxDocumentSnapshotBuilder.BuildSellerAsync(context, pharmacyProfile, branch, pharmacyStateCode, input.PrintBranding, cancellationToken);
				BuyerSnapshot buyerSnapshot = new BuyerSnapshot
				{
					Name = input.PatientName.Trim(),
					Phone = phone,
					Address = NullIfWhiteSpace(input.PatientAddress),
					PrescriberName = prescription?.PrescriberName,
					PrescriberRegistrationNumber = prescription?.PrescriberRegistrationNumber,
					PrescriptionReference = prescription?.ReferenceNumber
				};
				Sale sale = new Sale
				{
					PharmacyStateCode = pharmacyStateCode,
					SellerSnapshotJson = TaxDocumentSnapshot.Serialize(sellerSnapshot),
					BuyerSnapshotJson = TaxDocumentSnapshot.Serialize(buyerSnapshot),
					PatientId = patient.Id,
					PrescriptionId = prescription?.Id,
					PrescriptionFilePath = attachmentPath,
					InvoiceNo = invoiceNo,
					SaleAtUtc = DateTime.UtcNow,
					Subtotal = subtotal,
					TaxAmount = taxAmount,
					DiscountAmount = discountAmount,
					TotalAmount = totalAmount,
					PaidAmount = paidAmount,
					PaymentStatus = ((paidAmount >= totalAmount) ? "Paid" : ((paidAmount > 0m) ? "Partial" : "Credit")),
					Notes = NullIfWhiteSpace(input.Notes),
					MrdNumber = NullIfWhiteSpace(input.MrdNumber),
					IsLocked = true,
					BilledByUserId = actingUserId,
					BranchId = branch.Id
				};
				context.Sales.Add(sale);
				foreach (PreparedSaleLine prepared in preparedLines)
				{
					RetailSaleLineInput line = prepared.Input;
					Batch batch2 = prepared.Batch;
					batch2.Quantity = currentQuantities[batch2.Id];
					context.SaleItems.Add(new SaleItem
					{
						SaleId = sale.Id,
						DrugId = line.DrugId,
						BatchId = line.BatchId,
						Quantity = line.Quantity,
						UnitPrice = line.UnitPrice,
						DiscountAmount = line.DiscountAmount,
						TaxRate = prepared.Drug.GstRate.GetValueOrDefault(),
						LineTotal = prepared.Tax.GrossAmount,
						ItemName = prepared.Drug.Name,
						HsnCode = prepared.Drug.HsnCode,
						BatchNo = batch2.BatchNo,
						BatchExpiry = batch2.ExpiryDate
					});
					context.StockMovements.Add(new StockMovement
					{
						BatchId = batch2.Id,
						DrugId = line.DrugId,
						LocationId = retailLocation.Id,
						QuantityChange = -line.Quantity,
						MovementType = "RetailSale",
						ReferenceType = "Sale",
						ReferenceId = sale.Id,
						MovementAtUtc = sale.SaleAtUtc,
						Notes = sale.InvoiceNo
					});
					await StorageLocationService.ApplyBalanceDeltaAsync(context, retailLocation.Id, batch2.Id, line.DrugId, -line.Quantity, cancellationToken);
					if (prepared.RequiresPrescription || prepared.IsHabitForming)
					{
						context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
						{
							RegisterType = (prepared.Schedule ?? prepared.RegisterType ?? "HabitForming"),
							SaleId = sale.Id,
							PatientId = patient.Id,
							DrugId = prepared.Drug.Id,
							BatchId = batch2.Id,
							BatchNo = batch2.BatchNo,
							Quantity = line.Quantity,
							EntryAtUtc = sale.SaleAtUtc,
							PatientName = patient.Name,
							PrescriberName = prescription?.PrescriberName,
							PrescriberRegistrationNumber = prescription?.PrescriberRegistrationNumber,
							Notes = BuildRegisterNotes(prepared.IsHabitForming, attachmentPath, input.PrescriptionAdminOverride)
						});
					}
				}
				foreach (RetailPaymentInput payment in input.Payments.Where((RetailPaymentInput retailPaymentInput) => retailPaymentInput.AppliedAmount > 0m && !IsLoyalty(retailPaymentInput.Method)))
				{
					string receiptNo = await numberSeriesService.AllocateAsync(invoicePrefix + "-R", today, cancellationToken);
					// Cash/UPI/Card: Receipt is the cash/bank debit side; Sale row is the sales credit side.
					context.Receipts.Add(new Receipt
					{
						ReceiptNo = receiptNo,
						ReceiptAtUtc = sale.SaleAtUtc,
						Amount = RetailTaxCalculator.RoundMoney(payment.AppliedAmount),
						PaymentMethod = payment.Method.Trim(),
						Notes = $"SaleId={sale.Id:D}; InvoiceNo={invoiceNo}"
					});
				}
				patient.LoyaltyPoints += LoyaltyPolicy.PointsForAmount(totalAmount - loyaltyRedeemed);
				decimal unpaid = RetailTaxCalculator.RoundMoney(totalAmount - paidAmount);
				if (unpaid > 0m)
				{
					// Credit sale: Debit CustomerLedger (receivable ↑); sales credit remains on the Sale document.
					Customer creditCustomer = await EnsureRetailCreditCustomerAsync(context, patient, cancellationToken);
					context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
					{
						CustomerId = creditCustomer.Id,
						EntryAtUtc = sale.SaleAtUtc,
						EntryType = LedgerEntryTypes.RetailSale,
						ReferenceId = sale.Id,
						ReferenceNo = invoiceNo,
						Debit = unpaid,
						Credit = 0m,
						Notes = "Retail credit sale " + invoiceNo
					});
				}
				context.AuditLogs.Add(new AuditLog
				{
					UserId = actingUserId,
					Action = "RetailSalePosted",
					EntityName = "Sale",
					EntityId = sale.Id,
					BranchId = branch.Id,
					Details = $"Invoice {invoiceNo}; total {totalAmount:F2}; paid {paidAmount:F2}; branch {branch.Code}"
				});
				await unitOfWork.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				if (!licenseRuntimeGuard.OnTransactionCommitted())
				{
					throw new InvalidOperationException("System clock manipulation detected. Please set your system time correctly to resume.");
				}
				shouldKeepAttachment = attachmentPath != null;
				result = new RetailSaleResult(sale, subtotal, taxAmount, discountAmount, totalAmount, paidAmount, changeDue, invoiceNo);
			}
			return result;
		}
		finally
		{
			if (!shouldKeepAttachment && attachmentPath != null && File.Exists(attachmentPath))
			{
				File.Delete(attachmentPath);
			}
			else if (shouldKeepAttachment && stagedPendingPath != null && !string.Equals(stagedPendingPath, attachmentPath, StringComparison.OrdinalIgnoreCase) && File.Exists(stagedPendingPath))
			{
				File.Delete(stagedPendingPath);
			}
		}
	}

	public async Task<IReadOnlyList<RecentRetailBill>> SearchRecentBillsAsync(string? query, CancellationToken cancellationToken = default(CancellationToken))
	{
		IQueryable<Sale> terms = from sale in unitOfWork.Context.Sales.AsNoTracking()
			where !sale.IsDeleted
			select sale;
		Guid[] matchingPatientIds = Array.Empty<Guid>();
		if (!string.IsNullOrWhiteSpace(query))
		{
			string term = query.Trim();
			Guid[] array = await (from patient in unitOfWork.Context.Patients.AsNoTracking()
				where EF.Functions.Like(patient.Name, $"%{term}%") || EF.Functions.Like(patient.Phone ?? string.Empty, $"%{term}%")
				select patient.Id).ToArrayAsync(cancellationToken);
			matchingPatientIds = array;
			terms = terms.Where((Sale sale) => EF.Functions.Like(sale.InvoiceNo, $"%{term}%") || (sale.PatientId.HasValue && matchingPatientIds.Contains(sale.PatientId.Value)));
		}
		var sales = await (from sale in terms.OrderByDescending((Sale sale) => sale.SaleAtUtc).Take(100)
			select new { sale.Id, sale.InvoiceNo, sale.SaleAtUtc, sale.PatientId, sale.TotalAmount, sale.PaidAmount, sale.IsLocked, sale.BilledByUserId }).ToListAsync(cancellationToken);
		Guid[] patientIds = (from sale in sales
			where sale.PatientId.HasValue
			select sale.PatientId.Value).Distinct().ToArray();
		Dictionary<Guid, Patient> patients = await (from patient in unitOfWork.Context.Patients.AsNoTracking()
			where patientIds.Contains(patient.Id)
			select patient).ToDictionaryAsync((Patient patient) => patient.Id, cancellationToken);
		return sales.Select(sale =>
		{
			patients.TryGetValue(sale.PatientId ?? Guid.Empty, out var value);
			return new RecentRetailBill(sale.Id, sale.InvoiceNo, sale.SaleAtUtc, value?.Name ?? string.Empty, value?.Phone, sale.TotalAmount, sale.PaidAmount, sale.IsLocked, sale.BilledByUserId);
		}).ToArray();
	}

	public async Task UnlockSaleAsync(Guid saleId, Guid adminUserId, UserRole adminRole, string reason, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(adminRole, AppPermission.EditPrintedBills))
		{
			throw new UnauthorizedAccessException("Only an Admin can unlock a finalized bill.");
		}
		Sale sale = (await unitOfWork.Context.Sales.SingleOrDefaultAsync((Sale item) => item.Id == saleId, cancellationToken)) ?? throw new InvalidOperationException("The selected bill could not be found.");
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, sale.TotalAmount, sale.PaidAmount, sale.IsLocked, sale.IsDeleted, sale.Notes });
		RecordLockGuard.Demand(sale.SaleAtUtc, "RetailBill", sale.Id, "MODIFIED_AFTER_LOCK");
		if (sale.IsLocked)
		{
			sale.IsLocked = false;
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = adminUserId,
				Action = "RetailSaleUnlocked",
				EntityName = "Sale",
				EntityId = sale.Id,
				Details = "Admin override unlock for " + sale.InvoiceNo + ". Reason: " + reason.Trim()
			});
		}

		await ComplianceAuditWriter.AppendIfUnlockedAsync(unitOfWork.Context, sale.SaleAtUtc, new ComplianceAuditEntry
		{
			EntityType = "RetailBill",
			EntityId = sale.Id,
			Action = "MODIFIED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = reason.Trim(),
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, sale.TotalAmount, sale.PaidAmount, sale.IsLocked, sale.IsDeleted, sale.Notes }),
			UserId = adminUserId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	/// <summary>
	/// Admin void of a retail bill. It is reversed the same way a full credit note reverses it: every item still on the
	/// bill goes back to stock, the unpaid part comes off the customer ledger, and the cash received for it is refunded
	/// with a "Refund" receipt so the day book reconciles. The bill is kept as an audited, deleted record.
	/// </summary>
	public async Task VoidSaleAsync(Guid saleId, Guid adminUserId, UserRole adminRole, string reason, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(adminRole, AppPermission.EditPrintedBills))
		{
			throw new UnauthorizedAccessException("Only an Admin can void a finalized bill.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");

		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		Sale sale = (await unitOfWork.Context.Sales.SingleOrDefaultAsync((Sale item) => item.Id == saleId, cancellationToken))
			?? throw new InvalidOperationException("The selected bill could not be found, or it has already been voided.");
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, sale.TotalAmount, sale.PaidAmount, sale.IsLocked, sale.IsDeleted, sale.Notes });

		Dictionary<Guid, decimal> returnedBySaleItem = (await GetPriorReturnItemsAsync(saleId, cancellationToken))
			.GroupBy(item => item.SaleItemId)
			.ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
		RetailSaleReturnLineInput[] openLines = (await unitOfWork.Context.SaleItems.AsNoTracking()
			.Where(item => item.SaleId == saleId)
			.ToListAsync(cancellationToken))
			.Select(item => new RetailSaleReturnLineInput(item.Id, item.Quantity - returnedBySaleItem.GetValueOrDefault(item.Id)))
			.Where(line => line.Quantity > 0m)
			.ToArray();
		if (openLines.Length == 0)
		{
			throw new InvalidOperationException("Every item on this bill has already been returned, so there is nothing left to void.");
		}

		RetailSaleReturnResult creditNote = await IssueSalesReturnCoreAsync(saleId, openLines, "Void of " + sale.InvoiceNo + ": " + reason.Trim(), true, adminUserId, cancellationToken);

		string saleIdText = saleId.ToString("D");
		decimal cashReceived = await unitOfWork.Context.Receipts.AsNoTracking()
			.Where(receipt => receipt.PaymentMethod != "Refund" && receipt.Notes != null && receipt.Notes.Contains(saleIdText))
			.SumAsync(receipt => (decimal?)receipt.Amount, cancellationToken) ?? 0m;
		decimal refund = RetailTaxCalculator.RoundMoney(Math.Min(cashReceived, creditNote.CreditAmount));
		if (refund > 0m)
		{
			PharmacyProfile profile = await unitOfWork.Context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
			string refundNo = await numberSeriesService.AllocateAsync(profile.InvoicePrefix + "-RF", DateOnly.FromDateTime(DateTime.Today), cancellationToken);
			unitOfWork.Context.Receipts.Add(new Receipt
			{
				ReceiptNo = refundNo,
				ReceiptAtUtc = DateTime.UtcNow,
				Amount = refund,
				PaymentMethod = "Refund",
				Notes = $"Refund for voided {sale.InvoiceNo}; credit note {creditNote.ReturnNo}; SaleId={saleIdText}"
			});
		}

		sale.IsDeleted = true;
		sale.IsLocked = true;
		sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? ("VOIDED: " + reason.Trim()) : (sale.Notes + " | VOIDED: " + reason.Trim());
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = adminUserId,
			Action = "RetailSaleVoided",
			EntityName = "Sale",
			EntityId = sale.Id,
			Details = $"Admin void of {sale.InvoiceNo}. Credit note {creditNote.ReturnNo} for {creditNote.CreditAmount:F2}; restocked {creditNote.RestockedQuantity:N2}; refunded {refund:F2}. Reason: {reason.Trim()}"
		});
		await ComplianceAuditWriter.AppendIfUnlockedAsync(unitOfWork.Context, sale.SaleAtUtc, new ComplianceAuditEntry
		{
			EntityType = "RetailBill",
			EntityId = sale.Id,
			Action = "VOIDED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = reason.Trim(),
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, sale.IsDeleted, sale.Notes, creditNote.ReturnNo, refund }),
			UserId = adminUserId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task DemandUnlockedForMutationAsync(Guid saleId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Sale sale = (await unitOfWork.Context.Sales.AsNoTracking().SingleOrDefaultAsync((Sale item) => item.Id == saleId, cancellationToken)) ?? throw new InvalidOperationException("The selected bill could not be found.");
		RecordLockGuard.Demand(sale.SaleAtUtc, "RetailBill", sale.Id, "MODIFIED_AFTER_LOCK");
		if (sale.IsLocked)
		{
			throw new InvalidOperationException("This bill is locked after print/save. An Admin must authorize an unlock override before changes.");
		}
		if (sale.IsDeleted)
		{
			throw new InvalidOperationException("This bill has been voided.");
		}
	}

	private static string[] RecordedLicenceLines(SellerSnapshot seller)
	{
		List<string> lines = seller.Licences.Where(item => item.Number != string.Empty).Select(item => item.Number).ToList();
		foreach (string extra in seller.ExtraLicenceLines)
		{
			int space = extra.IndexOf(' ');
			string number = space < 0 ? extra : extra[(space + 1)..];
			if (!lines.Any(item => item.Contains(number, StringComparison.OrdinalIgnoreCase)))
			{
				lines.Add(extra);
			}
		}
		return lines.ToArray();
	}

	public async Task<RetailBillPrintData> GetPrintableBillAsync(Guid saleId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		Sale sale = (await context.Sales.AsNoTracking().SingleOrDefaultAsync((Sale item) => item.Id == saleId, cancellationToken)) ?? throw new InvalidOperationException("The selected bill could not be found.");
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		Patient patient = ((!sale.PatientId.HasValue) ? null : (await context.Patients.AsNoTracking().SingleOrDefaultAsync((Patient item) => item.Id == sale.PatientId, cancellationToken)));
		Patient patient2 = patient;
		List<SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where item.SaleId == saleId
			select item).ToListAsync(cancellationToken);
		Guid[] drugIds = saleItems.Select((SaleItem item) => item.DrugId).Distinct().ToArray();
		Guid[] batchIds = saleItems.Select((SaleItem item) => item.BatchId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where drugIds.Contains(item.Id)
			select item).ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await (from item in context.Batches.AsNoTracking()
			where batchIds.Contains(item.Id)
			select item).ToDictionaryAsync((Batch item) => item.Id, cancellationToken);
		string[] licences = await (from item in context.LicenceRecords.AsNoTracking()
			where item.LicenceNumber != string.Empty
			select item.LicenceNumber).ToArrayAsync(cancellationToken);
		Prescription prescription = null;
		if (sale.PrescriptionId.HasValue)
		{
			prescription = await context.Prescriptions.AsNoTracking().SingleOrDefaultAsync((Prescription item) => item.Id == sale.PrescriptionId, cancellationToken);
		}
		decimal num = decimal.Round(sale.TaxAmount / 2m, 2, MidpointRounding.AwayFromZero);
		decimal cgstAmount = num;
		decimal sgstAmount = sale.TaxAmount - num;
		SellerSnapshot? seller = TaxDocumentSnapshot.Deserialize<SellerSnapshot>(sale.SellerSnapshotJson);
		BuyerSnapshot? buyer = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(sale.BuyerSnapshotJson);
		if (seller != null && buyer != null)
		{
			return new RetailBillPrintData(
				seller.Name, seller.Address, seller.Phone, seller.Gstin,
				RecordedLicenceLines(seller),
				seller.CompetentPersonName, seller.CompetentPersonQualification, seller.CompetentPersonRegistrationNumber,
				sale.InvoiceNo, sale.SaleAtUtc, buyer.Name, buyer.Phone, buyer.Address,
				sale.Subtotal, sale.TaxAmount, sale.DiscountAmount, sale.TotalAmount, sale.PaidAmount,
				saleItems.Select(item =>
				{
					decimal lineTax = RetailTaxCalculator.SplitInclusive(item.Quantity, item.UnitPrice, item.TaxRate, item.DiscountAmount).TaxAmount;
					Drug? drug = drugs.GetValueOrDefault(item.DrugId);
					Batch? batch = batches.GetValueOrDefault(item.BatchId);
					return new RetailBillPrintLine(item.ItemName ?? drug?.Name ?? "Unknown medicine", item.BatchNo ?? batch?.BatchNo ?? "Unknown batch", item.ItemName != null ? item.BatchExpiry : batch?.ExpiryDate, item.Quantity, item.UnitPrice, item.TaxRate, lineTax, item.DiscountAmount, item.LineTotal, item.ItemName != null ? item.HsnCode : drug?.HsnCode);
				}).ToArray(),
				buyer.PrescriberName, buyer.PrescriberRegistrationNumber, seller.FssaiNumber, seller.AddressLine2, seller.TermsText, cgstAmount, sgstAmount, sale.MrdNumber,
				DetailsRecorded: true);
		}
		return new RetailBillPrintData(profile.Name, profile.Address, profile.Phone, profile.Gstin, licences, profile.CompetentPersonName, profile.CompetentPersonQualification, profile.CompetentPersonRegistrationNumber, sale.InvoiceNo, sale.SaleAtUtc, patient2?.Name ?? string.Empty, patient2?.Phone, patient2?.Address, sale.Subtotal, sale.TaxAmount, sale.DiscountAmount, sale.TotalAmount, sale.PaidAmount, saleItems.Select((SaleItem item) =>
		{
			Drug valueOrDefault = drugs.GetValueOrDefault(item.DrugId);
			decimal taxAmount = RetailTaxCalculator.SplitInclusive(item.Quantity, item.UnitPrice, item.TaxRate, item.DiscountAmount).TaxAmount;
			return new RetailBillPrintLine(valueOrDefault?.Name ?? "Unknown medicine", batches.GetValueOrDefault(item.BatchId)?.BatchNo ?? "Unknown batch", batches.GetValueOrDefault(item.BatchId)?.ExpiryDate, item.Quantity, item.UnitPrice, item.TaxRate, taxAmount, item.DiscountAmount, item.LineTotal, valueOrDefault?.HsnCode);
		}).ToArray(), prescription?.PrescriberName, prescription?.PrescriberRegistrationNumber, null, null, null, cgstAmount, sgstAmount, sale.MrdNumber);
	}

	public async Task<IReadOnlyList<RetailSaleReturnableLine>> GetSaleReturnLinesAsync(Guid saleId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		if (!(await context.Sales.AnyAsync((Sale item) => item.Id == saleId, cancellationToken)))
		{
			throw new InvalidOperationException("The original bill could not be found.");
		}
		List<SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where item.SaleId == saleId
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> quantities = (from item in await GetPriorReturnItemsAsync(saleId, cancellationToken)
			group item by item.SaleItemId).ToDictionary((IGrouping<Guid, SaleReturnItem> group) => group.Key, (IGrouping<Guid, SaleReturnItem> group) => group.Sum((SaleReturnItem item) => item.Quantity));
		Guid[] drugIds = saleItems.Select((SaleItem item) => item.DrugId).Distinct().ToArray();
		Guid[] batchIds = saleItems.Select((SaleItem item) => item.BatchId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where drugIds.Contains(item.Id)
			select item).ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await (from item in context.Batches.AsNoTracking()
			where batchIds.Contains(item.Id)
			select item).ToDictionaryAsync((Batch item) => item.Id, cancellationToken);
		return saleItems.Select((SaleItem item) =>
		{
			decimal valueOrDefault = quantities.GetValueOrDefault(item.Id);
			return new RetailSaleReturnableLine
			{
				SaleItemId = item.Id,
				DrugName = (drugs.GetValueOrDefault(item.DrugId)?.Name ?? "Unknown medicine"),
				BatchNo = (batches.GetValueOrDefault(item.BatchId)?.BatchNo ?? "Unknown batch"),
				ExpiryDate = batches.GetValueOrDefault(item.BatchId)?.ExpiryDate,
				SoldQuantity = item.Quantity,
				ReturnedQuantity = valueOrDefault,
				RemainingQuantity = Math.Max(0m, item.Quantity - valueOrDefault),
				UnitPrice = ((item.Quantity > 0m) ? (item.LineTotal / item.Quantity) : 0m)
			};
		}).ToArray();
	}

	public async Task<RetailSaleReturnResult> IssueSalesReturnAsync(Guid saleId, IReadOnlyCollection<RetailSaleReturnLineInput> requestedLines, string reason, bool restock, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
		{
			throw new UnauthorizedAccessException("Your role cannot process retail sales returns.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken)))
		{
			throw new InvalidOperationException("Sales returns are unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		ArgumentNullException.ThrowIfNull(requestedLines, "requestedLines");
		if (requestedLines.Count == 0 || requestedLines.Any((RetailSaleReturnLineInput line) => line.Quantity <= 0m) || requestedLines.Select((RetailSaleReturnLineInput line) => line.SaleItemId).Distinct().Count() != requestedLines.Count)
		{
			throw new ArgumentException("Select one or more return quantities greater than zero.", "requestedLines");
		}
		RetailSaleReturnResult result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			result = await IssueSalesReturnCoreAsync(saleId, requestedLines, reason, restock, actingUserId, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}
		return result;
	}

	/// <summary>Credit note work. Runs inside a transaction opened by the caller, so a void can share it.</summary>
	private async Task<RetailSaleReturnResult> IssueSalesReturnCoreAsync(Guid saleId, IReadOnlyCollection<RetailSaleReturnLineInput> requestedLines, string reason, bool restock, Guid actingUserId, CancellationToken cancellationToken)
	{
		Sale sale = (await unitOfWork.Context.Sales.SingleOrDefaultAsync((Sale sale2) => sale2.Id == saleId, cancellationToken)) ?? throw new InvalidOperationException("The original bill could not be found.");
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, sale.TotalAmount, sale.PaidAmount, sale.IsDeleted });
		RecordLockGuard.Demand(sale.SaleAtUtc, "RetailBill", sale.Id, "MODIFIED_AFTER_LOCK");
		List<ReturnNote> priorNotes = await unitOfWork.Context.ReturnNotes.Where((ReturnNote returnNote) => returnNote.SourceType == "Sale" && returnNote.SourceId == saleId).ToListAsync(cancellationToken);
		IReadOnlyList<SaleReturnItem> priorReturns = await GetPriorReturnItemsAsync(saleId, cancellationToken);
		HashSet<Guid> priorNoteIdsWithItems = priorReturns.Select((SaleReturnItem saleReturnItem) => saleReturnItem.ReturnNoteId).ToHashSet();
		if (priorNotes.Any((ReturnNote returnNote) => !priorNoteIdsWithItems.Contains(returnNote.Id)))
		{
			throw new InvalidOperationException("An older credit note has no item-level return details. Review it before processing another return.");
		}
		Dictionary<Guid, SaleItem> saleItems = await unitOfWork.Context.SaleItems.Where((SaleItem saleItem) => saleItem.SaleId == saleId).ToDictionaryAsync((SaleItem saleItem) => saleItem.Id, cancellationToken);
		Guid[] batchIds = saleItems.Values.Select((SaleItem line) => line.BatchId).Distinct().ToArray();
		Dictionary<Guid, Batch> dictionary = await unitOfWork.Context.Batches.Where((Batch batch) => batchIds.Contains(batch.Id)).ToDictionaryAsync((Batch batch) => batch.Id, cancellationToken);
		Dictionary<Guid, decimal> dictionary2 = (from saleReturnItem in priorReturns
			group saleReturnItem by saleReturnItem.SaleItemId).ToDictionary((IGrouping<Guid, SaleReturnItem> group) => group.Key, (IGrouping<Guid, SaleReturnItem> group) => group.Sum((SaleReturnItem saleReturnItem) => saleReturnItem.Quantity));
		Dictionary<Guid, decimal> dictionary3 = (from saleReturnItem in priorReturns
			group saleReturnItem by saleReturnItem.SaleItemId).ToDictionary((IGrouping<Guid, SaleReturnItem> group) => group.Key, (IGrouping<Guid, SaleReturnItem> group) => group.Sum((SaleReturnItem saleReturnItem) => saleReturnItem.CreditAmount));
		List<(SaleItem SaleItem, Batch Batch, decimal Quantity, decimal CreditAmount)> processed = new List<(SaleItem, Batch, decimal, decimal)>();
		foreach (RetailSaleReturnLineInput requestedLine in requestedLines)
		{
			if (!saleItems.TryGetValue(requestedLine.SaleItemId, out var value) || !dictionary.TryGetValue(value.BatchId, out var value2))
			{
				throw new InvalidOperationException("A selected return line does not belong to the original bill.");
			}
			decimal valueOrDefault = dictionary2.GetValueOrDefault(value.Id);
			decimal num = value.Quantity - valueOrDefault;
			if (requestedLine.Quantity > num)
			{
				throw new InvalidOperationException($"Return quantity for {value.Id} exceeds the remaining bill quantity ({num}).");
			}
			if (restock)
			{
				DateOnly? expiryDate = value2.ExpiryDate;
				if (expiryDate.HasValue)
				{
					DateOnly valueOrDefault2 = expiryDate.GetValueOrDefault();
					if (valueOrDefault2 <= DateOnly.FromDateTime(DateTime.Today))
					{
						throw new InvalidOperationException("Expired batch " + value2.BatchNo + " cannot be returned to saleable stock. Clear restock confirmation to issue a credit-only return.");
					}
				}
			}
			decimal num2 = RetailTaxCalculator.RoundMoney(value.LineTotal - dictionary3.GetValueOrDefault(value.Id));
			decimal val = ((requestedLine.Quantity == num) ? num2 : RetailTaxCalculator.RoundMoney(value.LineTotal * requestedLine.Quantity / value.Quantity));
			val = Math.Min(val, num2);
			processed.Add((value, value2, requestedLine.Quantity, val));
		}
		decimal creditTotal = RetailTaxCalculator.RoundMoney(processed.Sum(((SaleItem SaleItem, Batch Batch, decimal Quantity, decimal CreditAmount) tuple) => tuple.CreditAmount));
		if (creditTotal <= 0m)
		{
			throw new InvalidOperationException("The selected bill quantities have no remaining amount to credit.");
		}
		PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
		string returnNo = await numberSeriesService.AllocateAsync(pharmacyProfile.InvoicePrefix + "-CN", DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		ReturnNote note = new ReturnNote
		{
			ReturnNo = returnNo,
			SourceType = "Sale",
			SourceId = sale.Id,
			ReturnAtUtc = DateTime.UtcNow,
			TotalAmount = creditTotal,
			Reason = reason.Trim(),
			Notes = $"Credit note against invoice {sale.InvoiceNo}; restocked={restock}."
		};
		unitOfWork.Context.ReturnNotes.Add(note);
		decimal restockedQuantity = 0m;
		foreach (var item in processed)
		{
			SaleReturnItem entity = new SaleReturnItem
			{
				ReturnNoteId = note.Id,
				SaleItemId = item.SaleItem.Id,
				BatchId = item.Batch.Id,
				DrugId = item.SaleItem.DrugId,
				Quantity = item.Quantity,
				CreditAmount = item.CreditAmount,
				Restocked = restock
			};
			unitOfWork.Context.SaleReturnItems.Add(entity);
			if (restock)
			{
				decimal valueOrDefault3 = (await unitOfWork.Context.StockMovements.Where((StockMovement movement) => movement.BatchId == item.Batch.Id).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
				item.Batch.Quantity = valueOrDefault3 + item.Quantity;
				restockedQuantity += item.Quantity;
				unitOfWork.Context.StockMovements.Add(new StockMovement
				{
					BatchId = item.Batch.Id,
					DrugId = item.SaleItem.DrugId,
					QuantityChange = item.Quantity,
					MovementType = "RetailSaleReturn",
					ReferenceType = "ReturnNote",
					ReferenceId = note.Id,
					MovementAtUtc = note.ReturnAtUtc,
					Notes = returnNo
				});
			}
		}
		await PostRetailSaleReturnLedgerAsync(unitOfWork.Context, sale, note, creditTotal, cancellationToken);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "RetailSalesReturnProcessed",
			EntityName = "ReturnNote",
			EntityId = note.Id,
			Details = $"Credit note {returnNo} against {sale.InvoiceNo}; amount {creditTotal:F2}; restocked: {restockedQuantity}; reason: {reason.Trim()}"
		});
		await ComplianceAuditWriter.AppendIfUnlockedAsync(unitOfWork.Context, sale.SaleAtUtc, new ComplianceAuditEntry
		{
			EntityType = "RetailBill",
			EntityId = sale.Id,
			Action = "MODIFIED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = reason.Trim(),
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { sale.InvoiceNo, returnNo, creditTotal, restockedQuantity, reason = reason.Trim() }),
			UserId = actingUserId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return new RetailSaleReturnResult(returnNo, creditTotal, restockedQuantity);
	}

	private static async Task PostRetailSaleReturnLedgerAsync(
		PharmaBillDbContext context,
		Sale sale,
		ReturnNote note,
		decimal creditTotal,
		CancellationToken cancellationToken)
	{
		// Reverse receivable for the unpaid portion of the original bill (Credit CustomerLedger).
		CustomerLedgerEntry? saleLedger = await context.CustomerLedgerEntries
			.AsNoTracking()
			.FirstOrDefaultAsync(
				entry => entry.ReferenceId == sale.Id && entry.EntryType == LedgerEntryTypes.RetailSale,
				cancellationToken);
		if (saleLedger == null)
		{
			return;
		}

		decimal priorReturnCredits = await context.CustomerLedgerEntries.AsNoTracking()
			.Where(entry => entry.CustomerId == saleLedger.CustomerId
				&& entry.EntryType == LedgerEntryTypes.RetailSaleReturn
				&& entry.Notes != null
				&& entry.Notes.Contains(sale.InvoiceNo))
			.SumAsync(entry => (decimal?)entry.Credit, cancellationToken) ?? 0m;
		decimal remainingReceivable = RetailTaxCalculator.RoundMoney(Math.Max(0m, saleLedger.Debit - priorReturnCredits));
		decimal receivableCredit = RetailTaxCalculator.RoundMoney(Math.Min(creditTotal, remainingReceivable));
		if (receivableCredit <= 0m)
		{
			return;
		}

		context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
		{
			CustomerId = saleLedger.CustomerId,
			EntryAtUtc = note.ReturnAtUtc,
			EntryType = LedgerEntryTypes.RetailSaleReturn,
			ReferenceId = note.Id,
			ReferenceNo = note.ReturnNo,
			Debit = 0m,
			Credit = receivableCredit,
			Notes = "Retail return against " + sale.InvoiceNo
		});
	}

	// "Add to customers" on a retail bill: the patient appears in the Patients & Consumers list.
	// Matched by phone, so a patient who already has a credit account is not duplicated.
	private static async Task EnsureConsumerCustomerAsync(PharmaBillDbContext context, string name, string phone, string? address, CancellationToken cancellationToken)
	{
		bool exists = await context.Customers.AnyAsync((Customer item) => item.IsActive && item.Phone == phone, cancellationToken);
		if (exists)
		{
			return;
		}

		context.Customers.Add(new Customer
		{
			Name = name,
			Phone = phone,
			Address = address,
			BuyerType = "Walk-in",
			IsActive = true
		});
	}

	private static async Task<Customer> EnsureRetailCreditCustomerAsync(
		PharmaBillDbContext context,
		Patient patient,
		CancellationToken cancellationToken)
	{
		string phone = patient.Phone.Trim();
		Customer? existing = await context.Customers
			.FirstOrDefaultAsync(
				customer => customer.IsActive && customer.Phone == phone,
				cancellationToken);
		if (existing != null)
		{
			return existing;
		}

		Customer created = new Customer
		{
			Name = patient.Name.Trim(),
			Phone = phone,
			Address = patient.Address,
			BuyerType = "Retail",
			IsActive = true,
			CreditDays = 30
		};
		context.Customers.Add(created);
		return created;
	}

	private async Task<IReadOnlyList<SaleReturnItem>> GetPriorReturnItemsAsync(Guid saleId, CancellationToken cancellationToken)
	{
		Guid[] noteIds = await (from note in unitOfWork.Context.ReturnNotes
			where note.SourceType == "Sale" && note.SourceId == saleId
			select note.Id).ToArrayAsync(cancellationToken);
		return await unitOfWork.Context.SaleReturnItems.Where((SaleReturnItem item) => noteIds.Contains(item.ReturnNoteId)).ToListAsync(cancellationToken);
	}

	private static decimal ValidatePayments(SaveRetailSaleInput input, decimal total)
	{
		decimal num = 0m;
		foreach (RetailPaymentInput payment in input.Payments)
		{
			bool flag;
			switch (payment.Method)
			{
			case "Cash":
			case "UPI":
			case "Card":
			case "Credit":
			case "Loyalty":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				throw new InvalidOperationException("Unsupported payment method '" + payment.Method + "'.");
			}
			if (payment.AppliedAmount < 0m || payment.TenderedAmount < 0m)
			{
				throw new InvalidOperationException("Payment amounts cannot be negative.");
			}
			if (payment.Method == "Cash" && payment.TenderedAmount > 0m && payment.TenderedAmount < payment.AppliedAmount)
			{
				throw new InvalidOperationException("Cash tendered cannot be less than the cash amount applied.");
			}
			if (payment.Method == "UPI" && payment.AppliedAmount > 0m && !input.UpiPaymentReceived)
			{
				throw new InvalidOperationException("Confirm that the UPI payment was received before saving.");
			}
			if (IsLoyalty(payment.Method) && payment.AppliedAmount != decimal.Truncate(payment.AppliedAmount))
			{
				throw new InvalidOperationException("Loyalty points are redeemed in whole points.");
			}
			if (payment.Method == "Credit" && payment.AppliedAmount != 0m)
			{
				throw new InvalidOperationException("Credit is the unpaid balance, not a received payment.");
			}
			num += RetailTaxCalculator.RoundMoney(payment.AppliedAmount);
		}
		if (num > total)
		{
			throw new InvalidOperationException("Applied payments cannot exceed the bill total.");
		}
		return RetailTaxCalculator.RoundMoney(num);
	}

	private static bool IsLoyalty(string? method) => string.Equals(method, "Loyalty", StringComparison.Ordinal);

	private static bool IsPendingArchive(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return Path.GetFullPath(path).Contains($"{Path.DirectorySeparatorChar}_pending{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
	}

	private static string? BuildRegisterNotes(bool habitForming, string? attachmentPath, bool adminOverride)
	{
		string habit = habitForming ? "Habit-forming classification is reference data; verify against the prescription and applicable rules." : string.Empty;
		string file = string.IsNullOrWhiteSpace(attachmentPath) ? string.Empty : "PrescriptionFile=" + attachmentPath;
		string waived = adminOverride && string.IsNullOrWhiteSpace(attachmentPath) ? "Prescription attachment waived by admin override." : string.Empty;
		string combined = string.Join(" ", new[] { habit, file, waived }.Where(part => !string.IsNullOrWhiteSpace(part)));
		return string.IsNullOrWhiteSpace(combined) ? null : combined;
	}

	private static CatalogInfo? FindCatalogInfo(Drug drug, IReadOnlyCollection<CatalogInfo> infos)
	{
		return infos.FirstOrDefault((CatalogInfo info) =>
		{
			if (drug.CatalogMedicineId.HasValue)
			{
				Guid? catalogMedicineId = info.CatalogMedicineId;
				Guid? catalogMedicineId2 = drug.CatalogMedicineId;
				if (catalogMedicineId.HasValue != catalogMedicineId2.HasValue)
				{
					return false;
				}
				if (!catalogMedicineId.HasValue)
				{
					return true;
				}
				return catalogMedicineId.GetValueOrDefault() == catalogMedicineId2.GetValueOrDefault();
			}
			return false;
		}) ?? infos.FirstOrDefault((CatalogInfo info) => string.Equals(info.NameKey, drug.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
	}

	private static string? ResolveSchedule(Drug drug, CatalogInfo? info, IReadOnlyCollection<ScheduleOverride> overrides)
	{
		DateTime today = DateTime.UtcNow;
		return NormalizeSchedule((from item in overrides
			where (!item.EffectiveFromUtc.HasValue || item.EffectiveFromUtc <= today) && (!item.EffectiveToUtc.HasValue || item.EffectiveToUtc >= today)
			orderby item.EffectiveFromUtc descending
			select item).FirstOrDefault()?.Schedule ?? drug.Schedule ?? info?.Schedule);
	}

	private static string? NormalizeSchedule(string? schedule)
	{
		if (!string.IsNullOrWhiteSpace(schedule))
		{
			return schedule.Trim().ToUpperInvariant();
		}
		return null;
	}

	private static string NormalizePhone(string value)
	{
		return string.Concat(value.Where((char character) => char.IsDigit(character) || character == '+')).Trim();
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
