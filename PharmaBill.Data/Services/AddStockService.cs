using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record AddStockInput(
    Guid? DrugId,
    Guid? CatalogMedicineId,
    string MedicineName,
    string? Composition,
    string? Manufacturer,
    string Schedule,
    string BatchNo,
    DateOnly ExpiryDate,
    decimal Mrp,
    decimal PurchaseRate,
    decimal Quantity,
    decimal FreeQuantity,
    decimal PackSize,
    decimal GstRate,
    Guid SupplierId,
    string SupplierInvoiceNo,
    DateOnly InvoiceDate,
    string? Rack);

public sealed record AddStockResult(Guid DrugId, Guid BatchId, decimal TotalStock, bool MergedIntoExistingBatch);

public sealed class AddStockService(
    IUnitOfWork unitOfWork,
    PurchaseService purchaseService,
    IEntitlementService entitlementService)
{
    public static readonly IReadOnlyList<string> Schedules = ["OTC", "G", "H", "H1", "X", "NDPS"];

    public async Task<Supplier> AddSupplierAsync(
        string name,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        var existing = await unitOfWork.Context.Suppliers.FirstOrDefaultAsync(
            supplier => supplier.Name.ToLower() == trimmed.ToLower(), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var supplier = new Supplier { Name = trimmed };
        unitOfWork.Context.Suppliers.Add(supplier);
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "SupplierCreated",
            EntityName = nameof(Supplier),
            EntityId = supplier.Id,
            Details = trimmed
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return supplier;
    }

    // Suggests a schedule from the owner's override, then the catalogue reference. The user still has to choose.
    public async Task<string?> SuggestScheduleAsync(
        Guid? drugId,
        Guid? catalogMedicineId,
        string medicineName,
        CancellationToken cancellationToken = default)
    {
        var context = unitOfWork.Context;
        var resolvedDrugId = drugId ?? await FindDrugIdAsync(catalogMedicineId, cancellationToken);
        if (resolvedDrugId.HasValue)
        {
            var today = DateTime.UtcNow;
            var overrideSchedule = await context.ScheduleOverrides.AsNoTracking()
                .Where(item => item.DrugId == resolvedDrugId &&
                               (item.EffectiveFromUtc == null || item.EffectiveFromUtc <= today) &&
                               (item.EffectiveToUtc == null || item.EffectiveToUtc >= today))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Select(item => item.Schedule)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(overrideSchedule))
            {
                return overrideSchedule.Trim().ToUpperInvariant();
            }
        }

        var nameKey = medicineName.Trim().ToLowerInvariant();
        var info = await context.CatalogInfos.AsNoTracking()
            .Where(item => (catalogMedicineId != null && item.CatalogMedicineId == catalogMedicineId) ||
                           item.NameKey == nameKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(info?.Schedule))
        {
            return info.Schedule.Trim().ToUpperInvariant();
        }

        return info?.RequiresPrescription == true ? "H" : null;
    }

    public async Task<bool> BatchExistsAsync(
        Guid? drugId,
        Guid? catalogMedicineId,
        Guid supplierId,
        string batchNo,
        DateOnly expiryDate,
        CancellationToken cancellationToken = default)
    {
        var resolvedDrugId = drugId ?? await FindDrugIdAsync(catalogMedicineId, cancellationToken);
        if (!resolvedDrugId.HasValue)
        {
            return false;
        }

        var trimmed = batchNo.Trim();
        return await unitOfWork.Context.Batches.AnyAsync(
            batch => batch.DrugId == resolvedDrugId &&
                     batch.SupplierId == supplierId &&
                     batch.BatchNo == trimmed &&
                     batch.ExpiryDate == expiryDate,
            cancellationToken);
    }

    public async Task<AddStockResult> AddStockAsync(
        AddStockInput input,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(role);
        if (!await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
        }

        var schedule = input.Schedule?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(schedule) || !Schedules.Contains(schedule))
        {
            throw new InvalidOperationException("Choose the schedule (OTC, G, H, H1, X or NDPS).");
        }

        if (string.IsNullOrWhiteSpace(input.MedicineName))
        {
            throw new InvalidOperationException("Medicine name is required.");
        }

        if (input.Quantity <= 0 || input.FreeQuantity < 0 || input.Mrp < 0 || input.PurchaseRate < 0 ||
            input.GstRate < 0 || input.PackSize < 0)
        {
            throw new InvalidOperationException("Enter a positive quantity and non-negative prices, GST and pack size.");
        }

        var context = unitOfWork.Context;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var drugId = input.DrugId ?? await FindDrugIdAsync(input.CatalogMedicineId, cancellationToken);
        Drug drug;
        if (drugId.HasValue)
        {
            drug = await context.Drugs.SingleAsync(item => item.Id == drugId.Value && item.IsActive, cancellationToken);
            if (string.IsNullOrWhiteSpace(drug.Schedule))
            {
                drug.Schedule = schedule;
            }
            else if (!string.Equals(drug.Schedule.Trim(), schedule, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"This medicine is saved with schedule {drug.Schedule}. Use a schedule override to change it.");
            }
        }
        else
        {
            drug = new Drug
            {
                Name = input.MedicineName.Trim(),
                GenericName = string.IsNullOrWhiteSpace(input.Composition) ? null : input.Composition.Trim(),
                Schedule = schedule,
                GstRate = input.GstRate,
                Mrp = input.Mrp,
                SalePrice = input.Mrp,
                CatalogMedicineId = input.CatalogMedicineId
            };
            context.Drugs.Add(drug);
            if (input.PackSize > 0)
            {
                context.DrugPackLevels.Add(new DrugPackLevel
                {
                    DrugId = drug.Id,
                    Level = PackLevel.Strip,
                    Label = "Pack",
                    UnitsPerPack = input.PackSize
                });
            }

            context.AuditLogs.Add(new AuditLog
            {
                UserId = actingUserId,
                Action = "DrugCreated",
                EntityName = nameof(Drug),
                EntityId = drug.Id,
                Details = $"{drug.Name}; schedule {schedule}"
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var batchNo = input.BatchNo.Trim();
        var merged = await context.Batches.AnyAsync(
            batch => batch.DrugId == drug.Id && batch.SupplierId == input.SupplierId &&
                     batch.BatchNo == batchNo && batch.ExpiryDate == input.ExpiryDate,
            cancellationToken);
        var amount = Round(input.Quantity * input.PurchaseRate);
        var tax = Round(amount * input.GstRate / 100m);
        await purchaseService.SavePurchaseAsync(
            new SavePurchaseInput(
                input.SupplierId,
                input.SupplierInvoiceNo,
                input.InvoiceDate,
                amount,
                0m,
                tax,
                amount + tax,
                [
                    new PurchaseLineInput(
                        drug.Id,
                        batchNo,
                        input.ExpiryDate,
                        input.Quantity,
                        input.FreeQuantity,
                        input.Mrp,
                        input.PurchaseRate,
                        input.GstRate,
                        0m,
                        amount,
                        input.Rack)
                ]),
            actingUserId,
            role,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var batch = await context.Batches.AsNoTracking().FirstAsync(
            item => item.DrugId == drug.Id && item.SupplierId == input.SupplierId &&
                    item.BatchNo == batchNo && item.ExpiryDate == input.ExpiryDate,
            cancellationToken);
        var total = await context.StockMovements.AsNoTracking()
            .Where(movement => movement.DrugId == drug.Id)
            .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0m;
        return new AddStockResult(drug.Id, batch.Id, total, merged);
    }

    private async Task<Guid?> FindDrugIdAsync(Guid? catalogMedicineId, CancellationToken cancellationToken)
    {
        if (!catalogMedicineId.HasValue)
        {
            return null;
        }

        return await unitOfWork.Context.Drugs.AsNoTracking()
            .Where(drug => drug.CatalogMedicineId == catalogMedicineId && drug.IsActive)
            .OrderBy(drug => drug.CreatedAtUtc)
            .Select(drug => (Guid?)drug.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void EnsureAllowed(UserRole role)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.ManagePurchases))
        {
            throw new UnauthorizedAccessException("Your role cannot add stock.");
        }
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
