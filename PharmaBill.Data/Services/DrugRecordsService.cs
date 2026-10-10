using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class DrugRecordsService(IServiceScopeFactory scopeFactory)
{
	public async Task<IReadOnlyList<DrugRecordsDrugChoice>> SearchDrugsAsync(string query, Guid actingUserId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query, "query");
		if (query.Trim().Length < 2)
		{
			throw new ArgumentException("Enter at least two letters to search drug records.", "query");
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
		PharmaBillDbContext context = unitOfWork.Context;
		string term = query.Trim();
		Guid[] catalogIds = await (from item in context.CatalogMedicines.AsNoTracking()
			where EF.Functions.Like(item.Name, $"%{term}%") || EF.Functions.Like(item.BrandName ?? string.Empty, $"%{term}%") || EF.Functions.Like(item.ShortComposition1 ?? string.Empty, $"%{term}%") || EF.Functions.Like(item.ShortComposition2 ?? string.Empty, $"%{term}%") || EF.Functions.Like(item.GenericName ?? string.Empty, $"%{term}%")
			select item.Id).Take(500).ToArrayAsync(cancellationToken);
		List<Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where EF.Functions.Like(item.Name, $"%{term}%") || EF.Functions.Like(item.BrandName ?? string.Empty, $"%{term}%") || EF.Functions.Like(item.GenericName ?? string.Empty, $"%{term}%") || (item.CatalogMedicineId.HasValue && catalogIds.Contains(item.CatalogMedicineId.Value))
			orderby item.Name
			select item).Take(500).ToListAsync(cancellationToken);
		Guid[] linkedCatalogIds = (from item in drugs
			where item.CatalogMedicineId.HasValue
			select item.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, CatalogMedicine> catalog = await (from item in context.CatalogMedicines.AsNoTracking()
			where linkedCatalogIds.Contains(item.Id)
			select item).ToDictionaryAsync((CatalogMedicine item) => item.Id, cancellationToken);
		DrugRecordsDrugChoice[] results = drugs.Select((Drug item) =>
		{
			Guid? catalogMedicineId = item.CatalogMedicineId;
			object obj;
			if (catalogMedicineId.HasValue)
			{
				Guid valueOrDefault = catalogMedicineId.GetValueOrDefault();
				obj = catalog.GetValueOrDefault(valueOrDefault);
			}
			else
			{
				obj = null;
			}
			CatalogMedicine catalogMedicine = (CatalogMedicine)obj;
			string text = ((catalogMedicine == null) ? item.GenericName : string.Join("; ", new string[2] { catalogMedicine.ShortComposition1, catalogMedicine.ShortComposition2 }.Where((string value) => !string.IsNullOrWhiteSpace(value))));
			return new DrugRecordsDrugChoice(item.Id, item.Name, string.IsNullOrWhiteSpace(text) ? null : text, item.BrandName ?? catalogMedicine?.BrandName);
		}).ToArray();
		await LogAsync(unitOfWork, actingUserId, "DrugRecordsDrugSearch", $"{term}; {results.Length} results.", cancellationToken);
		return results;
	}

	public async Task<DrugRecordsReport> SearchAsync(DrugRecordsQuery query, Guid actingUserId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(query, "query");
		if (query.DrugIds.Count == 0)
		{
			throw new ArgumentException("Select at least one medicine.", "query");
		}
		if (query.EndUtc <= query.StartUtc)
		{
			throw new ArgumentException("The end of the period must be after its start.", "query");
		}
		bool flag;
		switch (query.RecordType)
		{
		case "Sale":
		case "Purchase":
		case "Both":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentException("Record type must be Sale, Purchase, or Both.", "query");
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
		PharmaBillDbContext context = unitOfWork.Context;
		Guid[] selectedIds = query.DrugIds.Distinct().ToArray();
		List<Batch> source = await (from item in context.Batches.AsNoTracking()
			where selectedIds.Contains(item.DrugId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Batch> batchMap = source.ToDictionary((Batch item) => item.Id);
		Guid[] batchIds = source.Select((Batch item) => item.Id).ToArray();
		List<StockMovement> source2 = await (from item in context.StockMovements.AsNoTracking()
			where batchIds.Contains(item.BatchId)
			orderby item.MovementAtUtc
			select item).ToListAsync(cancellationToken);
		StockMovement[] source3 = source2.Where((StockMovement item) => item.MovementAtUtc >= query.StartUtc && item.MovementAtUtc < query.EndUtc).ToArray();
		decimal opening = source2.Where((StockMovement item) => item.MovementAtUtc < query.StartUtc).Sum((StockMovement item) => item.QuantityChange);
		decimal currentStock = source2.Sum((StockMovement item) => item.QuantityChange);
		StockMovement[] saleTypes = source3.Where((StockMovement item) => item.MovementType == "RetailSale").ToArray();
		StockMovement[] purchaseTypes = source3.Where((StockMovement item) =>
		{
			string movementType = item.MovementType;
			return (movementType == "PurchaseReceipt" || movementType == "PurchaseInward" || movementType == "PurchaseReturn") ? true : false;
		}).ToArray();
		List<DrugRecordsMovement> movements = new List<DrugRecordsMovement>();
		foreach (StockMovement item in source3.Where((StockMovement item) =>
		{
			bool flag3;
			switch (item.MovementType)
			{
			case "RetailSale":
			case "RetailSaleReturn":
			case "PurchaseReceipt":
			case "PurchaseInward":
			case "PurchaseReturn":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			return !flag3;
		}))
		{
			movements.Add(new DrugRecordsMovement(item.DrugId, item.BatchId, item.MovementAtUtc, "Adjustment", item.Notes ?? item.MovementType, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, batchMap[item.BatchId].BatchNo, item.QuantityChange));
		}
		Guid[] saleIds = (from item in saleTypes
			where item.ReferenceType == "Sale" && item.ReferenceId.HasValue
			select item.ReferenceId.Value).Distinct().ToArray();
		Dictionary<Guid, Sale> sales = await (from item in context.Sales.AsNoTracking()
			where saleIds.Contains(item.Id)
			select item).ToDictionaryAsync((Sale item) => item.Id, cancellationToken);
		List<ReturnNote> returnNotes = await (from item in context.ReturnNotes.AsNoTracking()
			where item.SourceType == "Sale" && item.ReturnAtUtc >= query.StartUtc && item.ReturnAtUtc < query.EndUtc
			select item).ToListAsync(cancellationToken);
		Guid[] returnNoteIds = returnNotes.Select((ReturnNote item) => item.Id).ToArray();
		List<SaleReturnItem> saleReturnItems = await (from item in context.SaleReturnItems.AsNoTracking()
			where returnNoteIds.Contains(item.ReturnNoteId)
			select item).ToListAsync(cancellationToken);
		Guid[] returnItemSaleIds = saleReturnItems.Select((SaleReturnItem item) => item.SaleItemId).Distinct().ToArray();
		Dictionary<Guid, SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where returnItemSaleIds.Contains(item.Id)
			select item).ToDictionaryAsync((SaleItem item) => item.Id, cancellationToken);
		Guid[] involvedSaleIds = sales.Keys.Concat(saleItems.Values.Select((SaleItem item) => item.SaleId)).Distinct().ToArray();
		List<Sale> salesToRead = await (from item in context.Sales.AsNoTracking()
			where involvedSaleIds.Contains(item.Id)
			select item).ToListAsync(cancellationToken);
		Guid[] patientIds = (from item in salesToRead
			where item.PatientId.HasValue
			select item.PatientId.Value).Distinct().ToArray();
		Dictionary<Guid, Patient> patients = await (from item in context.Patients.AsNoTracking()
			where patientIds.Contains(item.Id)
			select item).ToDictionaryAsync((Patient item) => item.Id, cancellationToken);
		Guid[] prescriptionIds = (from item in salesToRead
			where item.PrescriptionId.HasValue
			select item.PrescriptionId.Value).Distinct().ToArray();
		Dictionary<Guid, Prescription> dictionary = await (from item in context.Prescriptions.AsNoTracking()
			where prescriptionIds.Contains(item.Id)
			select item).ToDictionaryAsync((Prescription item) => item.Id, cancellationToken);
		StockMovement[] array = saleTypes;
		foreach (StockMovement stockMovement in array)
		{
			Sale value = null;
			sales.TryGetValue(stockMovement.ReferenceId ?? Guid.Empty, out value);
			string recordType = "Sale";
			string documentNo = value?.InvoiceNo ?? stockMovement.Notes ?? string.Empty;
			Guid? guid = value?.PatientId;
			object obj;
			if (guid.HasValue)
			{
				Guid valueOrDefault = guid.GetValueOrDefault();
				obj = patients.GetValueOrDefault(valueOrDefault);
			}
			else
			{
				obj = null;
			}
			Patient patient = (Patient)obj;
			guid = value?.PrescriptionId;
			object obj2;
			if (guid.HasValue)
			{
				Guid valueOrDefault2 = guid.GetValueOrDefault();
				obj2 = dictionary.GetValueOrDefault(valueOrDefault2);
			}
			else
			{
				obj2 = null;
			}
			Prescription prescription = (Prescription)obj2;
			Guid drugId = stockMovement.DrugId;
			Guid batchId = stockMovement.BatchId;
			DateTime movementAtUtc = stockMovement.MovementAtUtc;
			string patientName = patient?.Name ?? string.Empty;
			string? address = patient?.Address ?? string.Empty;
			string? phone = patient?.Phone ?? string.Empty;
			string? doctorName = prescription?.PrescriberName ?? string.Empty;
			string? doctorRegistrationNo = prescription?.PrescriberRegistrationNumber ?? string.Empty;
			string batchNo = batchMap[stockMovement.BatchId].BatchNo;
			decimal quantityChange = stockMovement.QuantityChange;
			guid = value?.PatientId;
			movements.Add(new DrugRecordsMovement(drugId, batchId, movementAtUtc, recordType, documentNo, patientName, address, phone, doctorName, doctorRegistrationNo, batchNo, quantityChange, null, null, guid));
		}
		foreach (SaleReturnItem returnItem in saleReturnItems)
		{
			if (selectedIds.Contains(returnItem.DrugId))
			{
				ReturnNote returnNote = returnNotes.FirstOrDefault((ReturnNote item) => item.Id == returnItem.ReturnNoteId);
				SaleItem saleItem = saleItems.GetValueOrDefault(returnItem.SaleItemId);
				Sale? obj3 = ((saleItem == null) ? null : salesToRead.FirstOrDefault((Sale item) => item.Id == saleItem.SaleId));
				Guid? guid = obj3?.PatientId;
				object obj4;
				if (guid.HasValue)
				{
					Guid valueOrDefault3 = guid.GetValueOrDefault();
					obj4 = patients.GetValueOrDefault(valueOrDefault3);
				}
				else
				{
					obj4 = null;
				}
				Patient patient2 = (Patient)obj4;
				guid = obj3?.PrescriptionId;
				object obj5;
				if (guid.HasValue)
				{
					Guid valueOrDefault4 = guid.GetValueOrDefault();
					obj5 = dictionary.GetValueOrDefault(valueOrDefault4);
				}
				else
				{
					obj5 = null;
				}
				Prescription prescription2 = (Prescription)obj5;
				movements.Add(new DrugRecordsMovement(returnItem.DrugId, returnItem.BatchId, returnNote?.ReturnAtUtc ?? query.StartUtc, "Sale return", returnNote?.ReturnNo ?? string.Empty, patient2?.Name ?? string.Empty, patient2?.Address ?? string.Empty, patient2?.Phone ?? string.Empty, prescription2?.PrescriberName ?? string.Empty, prescription2?.PrescriberRegistrationNumber ?? string.Empty, batchMap[returnItem.BatchId].BatchNo, returnItem.Quantity, returnItem.Restocked ? returnItem.Quantity : 0m, returnItem.Quantity, patient2?.Id));
			}
		}
		Guid[] purchaseInvoiceIds = (from item in purchaseTypes
			where (item.MovementType == "PurchaseReceipt" || item.MovementType == "PurchaseInward") && item.ReferenceId.HasValue
			select item.ReferenceId.Value).Distinct().ToArray();
		Dictionary<Guid, PurchaseInvoice> purchaseInvoices = await (from item in context.PurchaseInvoices.AsNoTracking()
			where purchaseInvoiceIds.Contains(item.Id)
			select item).ToDictionaryAsync((PurchaseInvoice item) => item.Id, cancellationToken);
		Guid[] purchaseReturnIds = (from item in purchaseTypes
			where item.MovementType == "PurchaseReturn" && item.ReferenceId.HasValue
			select item.ReferenceId.Value).Distinct().ToArray();
		Dictionary<Guid, PurchaseReturn> purchaseReturns = await (from item in context.PurchaseReturns.AsNoTracking()
			where purchaseReturnIds.Contains(item.Id)
			select item).ToDictionaryAsync((PurchaseReturn item) => item.Id, cancellationToken);
		Guid[] supplierIds = purchaseInvoices.Values.Select((PurchaseInvoice item) => item.SupplierId).Concat(purchaseReturns.Values.Select((PurchaseReturn item) => item.SupplierId)).Distinct()
			.ToArray();
		Dictionary<Guid, Supplier> dictionary2 = await (from item in context.Suppliers.AsNoTracking()
			where supplierIds.Contains(item.Id)
			select item).ToDictionaryAsync((Supplier item) => item.Id, cancellationToken);
		array = purchaseTypes;
		foreach (StockMovement stockMovement2 in array)
		{
			bool flag2 = stockMovement2.MovementType is "PurchaseReceipt" or "PurchaseInward";
			PurchaseInvoice purchaseInvoice = (flag2 ? purchaseInvoices.GetValueOrDefault(stockMovement2.ReferenceId ?? Guid.Empty) : null);
			PurchaseReturn purchaseReturn = (flag2 ? null : purchaseReturns.GetValueOrDefault(stockMovement2.ReferenceId ?? Guid.Empty));
			Guid? guid2 = ((purchaseInvoice != null) ? new Guid?(purchaseInvoice.SupplierId) : purchaseReturn?.SupplierId);
			movements.Add(new DrugRecordsMovement(stockMovement2.DrugId, stockMovement2.BatchId, stockMovement2.MovementAtUtc, flag2 ? "Purchase" : "Purchase return", purchaseInvoice?.InvoiceNo ?? purchaseReturn?.ReturnNo ?? stockMovement2.Notes ?? string.Empty, guid2.HasValue ? (dictionary2.GetValueOrDefault(guid2.Value)?.Name ?? string.Empty) : string.Empty, guid2.HasValue ? (dictionary2.GetValueOrDefault(guid2.Value)?.Address ?? string.Empty) : string.Empty, guid2.HasValue ? (dictionary2.GetValueOrDefault(guid2.Value)?.Phone ?? string.Empty) : string.Empty, string.Empty, string.Empty, batchMap[stockMovement2.BatchId].BatchNo, stockMovement2.QuantityChange));
		}
		IReadOnlyList<DrugRecordsRow> rows;
		DrugRecordsSummary summary;
		(rows, summary) = DrugRecordsCalculator.Calculate(movements, opening, currentStock, query.RecordType);
		await LogAsync(unitOfWork, actingUserId, "DrugRecordsSearched", $"Drugs={string.Join(",", selectedIds)}; type={query.RecordType}; start={query.StartUtc:O}; end={query.EndUtc:O}; rows={rows.Count}.", cancellationToken);
		return new DrugRecordsReport(rows, summary, query.StartUtc, query.EndUtc);
	}

	public async Task LogExportAsync(Guid actingUserId, string format, bool hidePatientPhone, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		await LogAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), actingUserId, "DrugRecordsExported", $"Format={format}; patient phone hidden={hidePatientPhone}.", cancellationToken);
	}

	private static async Task LogAsync(IUnitOfWork unitOfWork, Guid actingUserId, string action, string details, CancellationToken cancellationToken)
	{
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = action,
			EntityName = "DrugRecordsReport",
			Details = details
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}
}
