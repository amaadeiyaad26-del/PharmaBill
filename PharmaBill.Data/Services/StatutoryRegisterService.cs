using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record StatutoryRegisterRow(
    Guid EntryId,
    DateTime EntryAtUtc,
    string RegisterType,
    string DocumentNo,
    string PartyName,
    string Address,
    string Phone,
    string BuyerLicenceNo,
    string DoctorName,
    string DoctorRegistrationNo,
    string DrugName,
    string BatchNo,
    decimal Quantity,
    decimal Balance,
    string Reason);

public sealed class StatutoryRegisterService(IServiceScopeFactory scopeFactory)
{
    public async Task<IReadOnlyList<StatutoryRegisterRow>> SearchAsync(
        string registerType,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerType);
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = unitOfWork.Context;
        var entries = await context.ScheduleRegisterEntries.AsNoTracking()
            .OrderBy(item => item.EntryAtUtc)
            .ToListAsync(cancellationToken);
        if (!registerType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(item =>
                    item.RegisterType.Contains(registerType, StringComparison.OrdinalIgnoreCase) ||
                    (registerType == "Habit-forming" &&
                     (item.RegisterType.Contains("Habit", StringComparison.OrdinalIgnoreCase) ||
                      item.RegisterType.Equals("HABIT", StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        var saleIds = entries.Where(item => item.SaleId.HasValue).Select(item => item.SaleId!.Value).Distinct().ToArray();
        var saleIdMap = await context.Sales.AsNoTracking().Where(item => saleIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var prescriptionIds = saleIdMap.Values.Where(item => item.PrescriptionId.HasValue)
            .Select(item => item.PrescriptionId!.Value).Distinct().ToArray();
        var prescriptions = await context.Prescriptions.AsNoTracking()
            .Where(item => prescriptionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var patientIds = entries.Where(item => item.PatientId.HasValue)
            .Select(item => item.PatientId!.Value).Distinct().ToArray();
        var patients = await context.Patients.AsNoTracking()
            .Where(item => patientIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var drugIds = entries.Where(item => item.DrugId.HasValue)
            .Select(item => item.DrugId!.Value).Distinct().ToArray();
        var drugs = await context.Drugs.AsNoTracking().Where(item => drugIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var batchIds = entries.Where(item => item.BatchId.HasValue)
            .Select(item => item.BatchId!.Value).Distinct().ToArray();
        var batches = await context.Batches.AsNoTracking().Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var invoiceIds = entries.Where(item => item.SupplierId.HasValue)
            .Select(item => item.SupplierId!.Value).Distinct().ToArray();
        var suppliers = await context.Suppliers.AsNoTracking().Where(item => invoiceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var buyers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var buyersById = buyers.ToDictionary(item => item.Id);
        var buyerIds = buyersById.Keys.ToArray();
        var customerLicences = await context.CustomerLicences.AsNoTracking()
            .Where(item => buyerIds.Contains(item.CustomerId))
            .ToListAsync(cancellationToken);
        var buyerLicences = customerLicences.GroupBy(item => item.CustomerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var movements = await context.StockMovements.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .ToListAsync(cancellationToken);
        var balancesByBatch = movements.GroupBy(item => item.BatchId)
            .ToDictionary(group => group.Key, group =>
            {
                var running = 0m;
                return group.OrderBy(item => item.MovementAtUtc)
                    .Select(item =>
                    {
                        running += item.QuantityChange;
                        return (AtUtc: item.MovementAtUtc, Balance: running);
                    })
                    .ToArray();
            });

        var results = new List<StatutoryRegisterRow>(entries.Count);
        foreach (var entry in entries)
        {
            var sale = entry.SaleId.HasValue ? saleIdMap.GetValueOrDefault(entry.SaleId.Value) : null;
            var patient = entry.PatientId.HasValue ? patients.GetValueOrDefault(entry.PatientId.Value) : null;
            var supplier = entry.SupplierId.HasValue ? suppliers.GetValueOrDefault(entry.SupplierId.Value) : null;
            var buyer = sale?.PatientId is { } buyerId ? buyersById.GetValueOrDefault(buyerId) : null;
            var batch = entry.BatchId.HasValue ? batches.GetValueOrDefault(entry.BatchId.Value) : null;
            var drug = entry.DrugId.HasValue ? drugs.GetValueOrDefault(entry.DrugId.Value) : null;
            var prescription = sale?.PrescriptionId is { } prescriptionId
                ? prescriptions.GetValueOrDefault(prescriptionId)
                : null;
            var buyerLicence = buyer is null
                ? supplier?.DrugLicenceNumber ?? string.Empty
                : buyerLicences.GetValueOrDefault(buyer.Id, [])
                    .FirstOrDefault(item => item.ExpiresOn >= DateOnly.FromDateTime(entry.EntryAtUtc))?.LicenceNumber
                  ?? string.Empty;
            var balance = entry.BatchId is { } id && balancesByBatch.TryGetValue(id, out var balancePoints)
                ? GetBalanceAt(balancePoints, entry.EntryAtUtc)
                : 0m;
            var documentNo = sale?.InvoiceNo ?? ExtractDocumentNumber(entry.Notes);
            results.Add(new StatutoryRegisterRow(
                entry.Id,
                entry.EntryAtUtc,
                entry.RegisterType,
                documentNo,
                patient?.Name ?? buyer?.Name ?? supplier?.Name ?? entry.PatientName ?? string.Empty,
                patient?.Address ?? buyer?.Address ?? supplier?.Address ?? string.Empty,
                patient?.Phone ?? buyer?.Phone ?? supplier?.Phone ?? string.Empty,
                buyerLicence,
                prescription?.PrescriberName ?? entry.PrescriberName ?? string.Empty,
                prescription?.PrescriberRegistrationNumber ?? entry.PrescriberRegistrationNumber ?? string.Empty,
                drug?.Name ?? string.Empty,
                batch?.BatchNo ?? entry.BatchNo ?? string.Empty,
                entry.Quantity ?? 0m,
                balance,
                entry.Notes ?? string.Empty));
        }

        context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "StatutoryRegisterSearched",
            EntityName = nameof(ScheduleRegisterEntry),
            Details = $"Register filter: {registerType}; {results.Count} entries."
        });
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    public async Task AppendCorrectionAsync(
        Guid entryId,
        string reason,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = unitOfWork.Context;
        var source = await context.ScheduleRegisterEntries.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == entryId, cancellationToken)
            ?? throw new InvalidOperationException("The statutory register entry was not found.");
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var correction = new ScheduleRegisterEntry
        {
            RegisterType = $"{source.RegisterType}-Correction",
            SaleId = source.SaleId,
            PatientId = source.PatientId,
            DrugId = source.DrugId,
            SupplierId = source.SupplierId,
            BatchId = source.BatchId,
            Quantity = source.Quantity,
            BatchNo = source.BatchNo,
            PatientName = source.PatientName,
            PrescriberName = source.PrescriberName,
            PrescriberRegistrationNumber = source.PrescriberRegistrationNumber,
            Notes = $"Correction to entry {source.Id:D}. Reason: {reason.Trim()}"
        };
        context.ScheduleRegisterEntries.Add(correction);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "StatutoryRegisterCorrectionAppended",
            EntityName = nameof(ScheduleRegisterEntry),
            EntityId = correction.Id,
            Details = correction.Notes
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string ExtractDocumentNumber(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        if (notes.StartsWith("Purchase invoice ", StringComparison.OrdinalIgnoreCase))
        {
            return notes["Purchase invoice ".Length..].Trim();
        }

        if (notes.StartsWith("Outgoing purchase return ", StringComparison.OrdinalIgnoreCase))
        {
            return notes["Outgoing purchase return ".Length..].Split(';', 2)[0].Trim();
        }

        return notes;
    }

    private static decimal GetBalanceAt((DateTime AtUtc, decimal Balance)[] points, DateTime atUtc)
    {
        var low = 0;
        var high = points.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (points[middle].AtUtc <= atUtc)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low == 0 ? 0m : points[low - 1].Balance;
    }
}
