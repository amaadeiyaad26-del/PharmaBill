using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record DrugRecordsDrugChoice(Guid DrugId, string Name, string? Composition, string? BrandName);

public sealed record DrugRecordsMovement(
    Guid DrugId,
    Guid BatchId,
    DateTime AtUtc,
    string RecordType,
    string DocumentNo,
    string PatientName,
    string Address,
    string Phone,
    string DoctorName,
    string DoctorRegistrationNo,
    string BatchNo,
    decimal QuantityChange,
    decimal? BalanceChange = null,
    decimal? SalesQuantityChange = null,
    Guid? PartyId = null);

public sealed record DrugRecordsRow(
    DateTime AtUtc,
    string RecordType,
    string DocumentNo,
    string PatientName,
    string Address,
    string Phone,
    string DoctorName,
    string DoctorRegistrationNo,
    string BatchNo,
    decimal Quantity,
    decimal RunningBalance);

public sealed record DrugRecordsSummary(
    decimal OpeningBalance,
    decimal Purchased,
    decimal Sold,
    decimal ClosingBalance,
    decimal CurrentStock,
    int UniquePatients,
    bool ClosingMatchesCurrentStock);

public sealed record DrugRecordsReport(
    IReadOnlyList<DrugRecordsRow> Rows,
    DrugRecordsSummary Summary,
    DateTime StartUtc,
    DateTime EndUtc);

public static class DrugRecordsCalculator
{
    public static (IReadOnlyList<DrugRecordsRow> Rows, DrugRecordsSummary Summary) Calculate(
        IEnumerable<DrugRecordsMovement> source,
        decimal openingBalance,
        decimal currentStock,
        string recordType)
    {
        var allMovements = source.OrderBy(item => item.AtUtc)
            .ThenBy(item => item.DocumentNo, StringComparer.Ordinal)
            .ThenBy(item => item.BatchNo, StringComparer.Ordinal)
            .ToArray();
        var selectedRows = new List<DrugRecordsRow>();
        var balance = openingBalance;
        foreach (var movement in allMovements)
        {
            balance += movement.BalanceChange ?? movement.QuantityChange;
            if (recordType != "Both" &&
                !(recordType == "Sale" && movement.RecordType is "Sale" or "Sale return") &&
                !(recordType == "Purchase" && movement.RecordType is "Purchase" or "Purchase return"))
            {
                continue;
            }

            selectedRows.Add(new DrugRecordsRow(
                movement.AtUtc,
                movement.RecordType,
                movement.DocumentNo,
                movement.PatientName,
                movement.Address,
                movement.Phone,
                movement.DoctorName,
                movement.DoctorRegistrationNo,
                movement.BatchNo,
                movement.QuantityChange,
                balance));
        }

        var purchased = allMovements.Where(item => item.RecordType is "Purchase" or "Purchase return")
            .Sum(item => item.QuantityChange);
        var sold = -allMovements.Where(item => item.RecordType is "Sale" or "Sale return")
            .Sum(item => item.SalesQuantityChange ?? item.QuantityChange);
        var closingBalance = openingBalance + allMovements.Sum(item => item.BalanceChange ?? item.QuantityChange);
        var uniquePatients = allMovements.Where(item => item.RecordType is "Sale" or "Sale return")
            .Select(item => item.PartyId?.ToString("D") ?? $"{item.PatientName}\u001f{item.Phone}")
            .Where(item => item != "\u001f")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return (selectedRows, new DrugRecordsSummary(
            openingBalance,
            purchased,
            sold,
            closingBalance,
            currentStock,
            uniquePatients,
            closingBalance == currentStock));
    }
}

public sealed record DrugRecordsQuery(
    IReadOnlyCollection<Guid> DrugIds,
    DateTime StartUtc,
    DateTime EndUtc,
    string RecordType = "Both");

public sealed class DrugRecordsService(IServiceScopeFactory scopeFactory)
{
    public async Task<IReadOnlyList<DrugRecordsDrugChoice>> SearchDrugsAsync(
        string query,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Trim().Length < 2)
        {
            throw new ArgumentException("Enter at least two letters to search drug records.", nameof(query));
        }

        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = unitOfWork.Context;
        var term = query.Trim();
        var catalogIds = await context.CatalogMedicines.AsNoTracking()
            .Where(item =>
                EF.Functions.Like(item.Name, $"%{term}%") ||
                EF.Functions.Like(item.BrandName ?? string.Empty, $"%{term}%") ||
                EF.Functions.Like(item.ShortComposition1 ?? string.Empty, $"%{term}%") ||
                EF.Functions.Like(item.ShortComposition2 ?? string.Empty, $"%{term}%") ||
                EF.Functions.Like(item.GenericName ?? string.Empty, $"%{term}%"))
            .Select(item => item.Id)
            .Take(500)
            .ToArrayAsync(cancellationToken);
        var drugs = await context.Drugs.AsNoTracking()
            .Where(item =>
                EF.Functions.Like(item.Name, $"%{term}%") ||
                EF.Functions.Like(item.BrandName ?? string.Empty, $"%{term}%") ||
                EF.Functions.Like(item.GenericName ?? string.Empty, $"%{term}%") ||
                (item.CatalogMedicineId.HasValue && catalogIds.Contains(item.CatalogMedicineId.Value)))
            .OrderBy(item => item.Name)
            .Take(500)
            .ToListAsync(cancellationToken);
        var linkedCatalogIds = drugs.Where(item => item.CatalogMedicineId.HasValue)
            .Select(item => item.CatalogMedicineId!.Value).Distinct().ToArray();
        var catalog = await context.CatalogMedicines.AsNoTracking()
            .Where(item => linkedCatalogIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var results = drugs.Select(item =>
        {
            var medicine = item.CatalogMedicineId is { } catalogId
                ? catalog.GetValueOrDefault(catalogId)
                : null;
            var composition = medicine is null
                ? item.GenericName
                : string.Join("; ", new[] { medicine.ShortComposition1, medicine.ShortComposition2 }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return new DrugRecordsDrugChoice(
                item.Id,
                item.Name,
                string.IsNullOrWhiteSpace(composition) ? null : composition,
                item.BrandName ?? medicine?.BrandName);
        }).ToArray();
        await LogAsync(unitOfWork, actingUserId, "DrugRecordsDrugSearch", $"{term}; {results.Length} results.", cancellationToken);
        return results;
    }

    public async Task<DrugRecordsReport> SearchAsync(
        DrugRecordsQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.DrugIds.Count == 0)
        {
            throw new ArgumentException("Select at least one medicine.", nameof(query));
        }

        if (query.EndUtc <= query.StartUtc)
        {
            throw new ArgumentException("The end of the period must be after its start.", nameof(query));
        }

        if (query.RecordType is not ("Sale" or "Purchase" or "Both"))
        {
            throw new ArgumentException("Record type must be Sale, Purchase, or Both.", nameof(query));
        }

        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = unitOfWork.Context;
        var selectedIds = query.DrugIds.Distinct().ToArray();
        var batches = await context.Batches.AsNoTracking()
            .Where(item => selectedIds.Contains(item.DrugId))
            .ToListAsync(cancellationToken);
        var batchMap = batches.ToDictionary(item => item.Id);
        var batchIds = batches.Select(item => item.Id).ToArray();
        var allMovements = await context.StockMovements.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .OrderBy(item => item.MovementAtUtc)
            .ToListAsync(cancellationToken);
        var movementInRange = allMovements.Where(item =>
            item.MovementAtUtc >= query.StartUtc && item.MovementAtUtc < query.EndUtc).ToArray();
        var opening = allMovements.Where(item => item.MovementAtUtc < query.StartUtc)
            .Sum(item => item.QuantityChange);
        var currentStock = allMovements.Sum(item => item.QuantityChange);
        var saleTypes = movementInRange.Where(item => item.MovementType == "RetailSale").ToArray();
        var purchaseTypes = movementInRange.Where(item => item.MovementType is "PurchaseReceipt" or "PurchaseReturn").ToArray();
        var movements = new List<DrugRecordsMovement>();
        foreach (var adjustment in movementInRange.Where(item =>
                     item.MovementType is not ("RetailSale" or "RetailSaleReturn" or "PurchaseReceipt" or "PurchaseReturn")))
        {
            movements.Add(new DrugRecordsMovement(
                adjustment.DrugId,
                adjustment.BatchId,
                adjustment.MovementAtUtc,
                "Adjustment",
                adjustment.Notes ?? adjustment.MovementType,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                batchMap[adjustment.BatchId].BatchNo,
                adjustment.QuantityChange));
        }

        var saleIds = saleTypes.Where(item => item.ReferenceType == nameof(Sale) && item.ReferenceId.HasValue)
            .Select(item => item.ReferenceId!.Value).Distinct().ToArray();
        var sales = await context.Sales.AsNoTracking().Where(item => saleIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var returnNotes = await context.ReturnNotes.AsNoTracking()
            .Where(item => item.SourceType == nameof(Sale) &&
                           item.ReturnAtUtc >= query.StartUtc && item.ReturnAtUtc < query.EndUtc)
            .ToListAsync(cancellationToken);
        var returnNoteIds = returnNotes.Select(item => item.Id).ToArray();
        var saleReturnItems = await context.SaleReturnItems.AsNoTracking()
            .Where(item => returnNoteIds.Contains(item.ReturnNoteId))
            .ToListAsync(cancellationToken);
        var returnItemSaleIds = saleReturnItems.Select(item => item.SaleItemId).Distinct().ToArray();
        var saleItems = await context.SaleItems.AsNoTracking()
            .Where(item => returnItemSaleIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var involvedSaleIds = sales.Keys.Concat(saleItems.Values.Select(item => item.SaleId)).Distinct().ToArray();
        var salesToRead = await context.Sales.AsNoTracking()
            .Where(item => involvedSaleIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var patientIds = salesToRead.Where(item => item.PatientId.HasValue)
            .Select(item => item.PatientId!.Value).Distinct().ToArray();
        var patients = await context.Patients.AsNoTracking().Where(item => patientIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var prescriptionIds = salesToRead.Where(item => item.PrescriptionId.HasValue)
            .Select(item => item.PrescriptionId!.Value).Distinct().ToArray();
        var prescriptions = await context.Prescriptions.AsNoTracking()
            .Where(item => prescriptionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var movement in saleTypes)
        {
            Sale? sale = null;
            string recordType;
            string documentNo;
            sales.TryGetValue(movement.ReferenceId ?? Guid.Empty, out sale);
            recordType = "Sale";
            documentNo = sale?.InvoiceNo ?? movement.Notes ?? string.Empty;

            var patient = sale?.PatientId is { } patientId ? patients.GetValueOrDefault(patientId) : null;
            var prescription = sale?.PrescriptionId is { } prescriptionId
                ? prescriptions.GetValueOrDefault(prescriptionId)
                : null;
            movements.Add(new DrugRecordsMovement(
                movement.DrugId,
                movement.BatchId,
                movement.MovementAtUtc,
                recordType,
                documentNo,
                patient?.Name ?? string.Empty,
                patient?.Address ?? string.Empty,
                patient?.Phone ?? string.Empty,
                prescription?.PrescriberName ?? string.Empty,
                prescription?.PrescriberRegistrationNumber ?? string.Empty,
                batchMap[movement.BatchId].BatchNo,
                movement.QuantityChange,
                PartyId: sale?.PatientId));
        }

        foreach (var returnItem in saleReturnItems)
        {
            if (!selectedIds.Contains(returnItem.DrugId))
            {
                continue;
            }

            var note = returnNotes.FirstOrDefault(item => item.Id == returnItem.ReturnNoteId);
            var saleItem = saleItems.GetValueOrDefault(returnItem.SaleItemId);
            var sale = saleItem is null ? null : salesToRead.FirstOrDefault(item => item.Id == saleItem.SaleId);
            var patient = sale?.PatientId is { } patientId ? patients.GetValueOrDefault(patientId) : null;
            var prescription = sale?.PrescriptionId is { } prescriptionId
                ? prescriptions.GetValueOrDefault(prescriptionId)
                : null;
            movements.Add(new DrugRecordsMovement(
                returnItem.DrugId,
                returnItem.BatchId,
                note?.ReturnAtUtc ?? query.StartUtc,
                "Sale return",
                note?.ReturnNo ?? string.Empty,
                patient?.Name ?? string.Empty,
                patient?.Address ?? string.Empty,
                patient?.Phone ?? string.Empty,
                prescription?.PrescriberName ?? string.Empty,
                prescription?.PrescriberRegistrationNumber ?? string.Empty,
                batchMap[returnItem.BatchId].BatchNo,
                returnItem.Quantity,
                returnItem.Restocked ? returnItem.Quantity : 0m,
                returnItem.Quantity,
                patient?.Id));
        }

        var purchaseInvoiceIds = purchaseTypes.Where(item => item.MovementType == "PurchaseReceipt" && item.ReferenceId.HasValue)
            .Select(item => item.ReferenceId!.Value).Distinct().ToArray();
        var purchaseInvoices = await context.PurchaseInvoices.AsNoTracking()
            .Where(item => purchaseInvoiceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var purchaseReturnIds = purchaseTypes.Where(item => item.MovementType == "PurchaseReturn" && item.ReferenceId.HasValue)
            .Select(item => item.ReferenceId!.Value).Distinct().ToArray();
        var purchaseReturns = await context.PurchaseReturns.AsNoTracking()
            .Where(item => purchaseReturnIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var supplierIds = purchaseInvoices.Values.Select(item => item.SupplierId)
            .Concat(purchaseReturns.Values.Select(item => item.SupplierId)).Distinct().ToArray();
        var suppliers = await context.Suppliers.AsNoTracking().Where(item => supplierIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var movement in purchaseTypes)
        {
            var isReceipt = movement.MovementType == "PurchaseReceipt";
            var invoice = isReceipt ? purchaseInvoices.GetValueOrDefault(movement.ReferenceId ?? Guid.Empty) : null;
            var purchaseReturn = isReceipt ? null : purchaseReturns.GetValueOrDefault(movement.ReferenceId ?? Guid.Empty);
            var supplierId = invoice?.SupplierId ?? purchaseReturn?.SupplierId;
            movements.Add(new DrugRecordsMovement(
                movement.DrugId,
                movement.BatchId,
                movement.MovementAtUtc,
                isReceipt ? "Purchase" : "Purchase return",
                invoice?.InvoiceNo ?? purchaseReturn?.ReturnNo ?? movement.Notes ?? string.Empty,
                supplierId.HasValue ? suppliers.GetValueOrDefault(supplierId.Value)?.Name ?? string.Empty : string.Empty,
                supplierId.HasValue ? suppliers.GetValueOrDefault(supplierId.Value)?.Address ?? string.Empty : string.Empty,
                supplierId.HasValue ? suppliers.GetValueOrDefault(supplierId.Value)?.Phone ?? string.Empty : string.Empty,
                string.Empty,
                string.Empty,
                batchMap[movement.BatchId].BatchNo,
                movement.QuantityChange));
        }

        var (rows, summary) = DrugRecordsCalculator.Calculate(movements, opening, currentStock, query.RecordType);
        await LogAsync(
            unitOfWork,
            actingUserId,
            "DrugRecordsSearched",
            $"Drugs={string.Join(",", selectedIds)}; type={query.RecordType}; start={query.StartUtc:O}; end={query.EndUtc:O}; rows={rows.Count}.",
            cancellationToken);
        return new DrugRecordsReport(rows, summary, query.StartUtc, query.EndUtc);
    }

    public async Task LogExportAsync(
        Guid actingUserId,
        string format,
        bool hidePatientPhone,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await LogAsync(
            unitOfWork,
            actingUserId,
            "DrugRecordsExported",
            $"Format={format}; patient phone hidden={hidePatientPhone}.",
            cancellationToken);
    }

    private static async Task LogAsync(
        IUnitOfWork unitOfWork,
        Guid actingUserId,
        string action,
        string details,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = action,
            EntityName = nameof(DrugRecordsReport),
            Details = details
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
