using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record WholesaleInvoiceLineInput(
    Guid DrugId,
    Guid BatchId,
    decimal Quantity,
    decimal FreeQuantity,
    decimal UnitPrice,
    decimal DiscountAmount = 0m);

public sealed record SaveWholesaleInvoiceInput(
    Guid CustomerId,
    IReadOnlyList<WholesaleInvoiceLineInput> Items,
    decimal PaidAmount,
    string PaymentMethod,
    string? PaymentReference,
    bool ConfirmNearExpiry,
    int NearExpiryWarningDays,
    bool OverrideCreditOrOverdue,
    string? OverrideReason,
    string? TransportDetails,
    string? VehicleNumber,
    string? EWayBillNumber,
    string? Irn,
    string? Notes,
    DateTime? InvoiceAtUtc = null);

public sealed record WholesaleInvoiceResult(
    WholesaleInvoice Invoice,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal OutstandingAfterPosting,
    bool HasOverdueBalance);

public sealed class WholesaleInvoiceService(
    IUnitOfWork unitOfWork,
    NumberSeriesService numberSeriesService,
    IEntitlementService entitlementService)
{
    public async Task<WholesaleInvoiceResult> SaveAsync(
        SaveWholesaleInvoiceInput input,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
        {
            throw new UnauthorizedAccessException("Your role cannot create wholesale invoices.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.CreateBill, cancellationToken))
        {
            throw new InvalidOperationException("Wholesale billing is unavailable in read-only mode.");
        }

        if (input.Items.Count == 0 ||
            input.Items.Any(item => item.Quantity <= 0 || item.FreeQuantity < 0 ||
                                    item.UnitPrice < 0 || item.DiscountAmount < 0))
        {
            throw new InvalidOperationException("Add valid invoice lines with positive quantities and non-negative prices.");
        }

        if (input.PaidAmount < 0 || string.IsNullOrWhiteSpace(input.PaymentMethod) ||
            input.NearExpiryWarningDays < 0)
        {
            throw new InvalidOperationException("Enter a valid payment and near-expiry warning period.");
        }

        var context = unitOfWork.Context;
        var profile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
        if (profile.BusinessMode == BusinessMode.Retail)
        {
            throw new InvalidOperationException("Wholesale invoicing is unavailable in Retail mode.");
        }

        if (string.IsNullOrWhiteSpace(profile.State))
        {
            throw new InvalidOperationException("Set the pharmacy state before calculating CGST/SGST or IGST.");
        }

        var customer = await context.Customers.SingleOrDefaultAsync(
            item => item.Id == input.CustomerId,
            cancellationToken) ?? throw new InvalidOperationException("Select a wholesale customer; walk-ins and patients are not allowed.");
        if (!customer.IsActive)
        {
            throw new InvalidOperationException("This customer is Blocked and cannot be invoiced.");
        }

        var invoiceAtUtc = DateTime.SpecifyKind(input.InvoiceAtUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        var invoiceDate = DateOnly.FromDateTime(invoiceAtUtc.ToLocalTime());
        var validLicences = await GetValidLicencesAsync(customer, profile, invoiceDate, cancellationToken);

        var requestedBatchIds = input.Items.Select(line => line.BatchId).Distinct().ToArray();
        var requestedDrugIds = input.Items.Select(line => line.DrugId).Distinct().ToArray();
        var drugs = await context.Drugs.Where(item => item.IsActive && requestedDrugIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var batches = await context.Batches.Where(item => requestedBatchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (drugs.Count != requestedDrugIds.Length ||
            batches.Count != requestedBatchIds.Length ||
            input.Items.Any(line => !batches.TryGetValue(line.BatchId, out var batch) || batch.DrugId != line.DrugId))
        {
            throw new InvalidOperationException("An invoice line refers to an unavailable medicine or batch.");
        }

        var quantities = await context.StockMovements
            .Where(movement => requestedBatchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(item => item.QuantityChange) })
            .ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
        var overrides = await context.ScheduleOverrides
            .Where(item => requestedDrugIds.Contains(item.DrugId))
            .ToListAsync(cancellationToken);
        var catalogInfos = await context.CatalogInfos.AsNoTracking().ToListAsync(cancellationToken);
        var prepared = new List<PreparedLine>();
        foreach (var line in input.Items)
        {
            var drug = drugs[line.DrugId];
            var batch = batches[line.BatchId];
            var available = quantities.GetValueOrDefault(batch.Id);
            if (line.Quantity + line.FreeQuantity > available)
            {
                throw new InvalidOperationException(
                    $"Quantity exceeds stock for {drug.Name}, batch {batch.BatchNo} ({available} available).");
            }

            if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < invoiceDate)
            {
                throw new InvalidOperationException($"Expired batch {batch.BatchNo} cannot be sold.");
            }

            if (batch.ExpiryDate.HasValue &&
                batch.ExpiryDate.Value.DayNumber - invoiceDate.DayNumber <= input.NearExpiryWarningDays &&
                !input.ConfirmNearExpiry)
            {
                throw new InvalidOperationException(
                    $"Confirm sale of near-expiry batch {batch.BatchNo} expiring {batch.ExpiryDate:yyyy-MM-dd}.");
            }

            var mrp = batch.Mrp ?? drug.Mrp
                ?? throw new InvalidOperationException($"Set MRP for {drug.Name} before selling.");
            if (line.UnitPrice > mrp)
            {
                throw new InvalidOperationException($"Selling price for {drug.Name} cannot exceed batch MRP.");
            }

            var schedule = ResolveSchedule(drug, catalogInfos, overrides, invoiceAtUtc);
            if (schedule is "X" or "NDPS" &&
                !validLicences.Any(licence => HasScheduleAuthorisation(licence.Authorisation, schedule)))
            {
                throw new InvalidOperationException(
                    $"Customer {customer.Name} has no valid licence authorisation matching Schedule {schedule}.");
            }

            var taxable = RoundMoney(line.Quantity * line.UnitPrice - line.DiscountAmount);
            if (taxable < 0)
            {
                throw new InvalidOperationException($"Discount exceeds line amount for {drug.Name}.");
            }

            var tax = RoundMoney(taxable * (drug.GstRate ?? 0m) / 100m);
            var sameState = string.Equals(profile.State.Trim(), customer.State?.Trim(), StringComparison.OrdinalIgnoreCase);
            var cgst = sameState ? RoundMoney(tax / 2m) : 0m;
            var sgst = sameState ? tax - cgst : 0m;
            prepared.Add(new PreparedLine(line, drug, batch, schedule, taxable, tax, cgst, sgst, sameState ? 0m : tax));
            quantities[batch.Id] = available - line.Quantity - line.FreeQuantity;
        }

        var subtotal = prepared.Sum(line => line.TaxableAmount);
        var taxTotal = prepared.Sum(line => line.TaxAmount);
        var cgstTotal = prepared.Sum(line => line.CgstAmount);
        var sgstTotal = prepared.Sum(line => line.SgstAmount);
        var igstTotal = prepared.Sum(line => line.IgstAmount);
        var exactTotal = subtotal + taxTotal;
        var roundedTotal = decimal.Round(exactTotal, 0, MidpointRounding.AwayFromZero);
        var roundOff = RoundMoney(roundedTotal - exactTotal);
        if (input.PaidAmount > roundedTotal)
        {
            throw new InvalidOperationException("Payment cannot exceed the invoice total.");
        }

        var currentBalance = customer.OpeningBalance + await context.CustomerLedgerEntries
            .Where(entry => entry.CustomerId == customer.Id)
            .SumAsync(entry => (decimal?)(entry.Debit - entry.Credit), cancellationToken) ?? customer.OpeningBalance;
        var outstandingAfter = RoundMoney(currentBalance + roundedTotal - input.PaidAmount);
        var hasOverdue = await HasOverdueInvoicesAsync(customer, invoiceDate, cancellationToken);
        var exceedsLimit = customer.CreditLimit > 0 && outstandingAfter > customer.CreditLimit;
        if ((hasOverdue || exceedsLimit) && !input.OverrideCreditOrOverdue)
        {
            var warning = hasOverdue ? "Customer has overdue invoices" : "Customer credit limit would be exceeded";
            throw new InvalidOperationException($"{warning}; owner override with a reason is required.");
        }

        if ((hasOverdue || exceedsLimit) &&
            (role != UserRole.Owner || string.IsNullOrWhiteSpace(input.OverrideReason)))
        {
            throw new UnauthorizedAccessException("Only the owner can override credit or overdue warnings, with a reason.");
        }

        var paymentStatus = input.PaidAmount == roundedTotal
            ? "Paid"
            : input.PaidAmount > 0 ? "PartiallyPaid" : "Credit";
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var invoiceNo = await numberSeriesService.AllocateAsync(
            profile.InvoicePrefix,
            invoiceDate,
            cancellationToken);
        var invoice = new WholesaleInvoice
        {
            CustomerId = customer.Id,
            InvoiceNo = invoiceNo,
            InvoiceAtUtc = invoiceAtUtc,
            Subtotal = subtotal,
            TaxAmount = taxTotal,
            CgstAmount = cgstTotal,
            SgstAmount = sgstTotal,
            IgstAmount = igstTotal,
            RoundOff = roundOff,
            DiscountAmount = prepared.Sum(line => line.Input.DiscountAmount),
            TotalAmount = roundedTotal,
            PaidAmount = input.PaidAmount,
            PaymentStatus = paymentStatus,
            TransportDetails = NullIfWhiteSpace(input.TransportDetails),
            VehicleNumber = NullIfWhiteSpace(input.VehicleNumber),
            EWayBillNumber = NullIfWhiteSpace(input.EWayBillNumber),
            Irn = NullIfWhiteSpace(input.Irn),
            Notes = NullIfWhiteSpace(input.Notes)
        };
        context.WholesaleInvoices.Add(invoice);
        foreach (var line in prepared)
        {
            line.Batch.Quantity = quantities[line.Batch.Id];
            context.WholesaleInvoiceItems.Add(new WholesaleInvoiceItem
            {
                WholesaleInvoiceId = invoice.Id,
                DrugId = line.Drug.Id,
                BatchId = line.Batch.Id,
                Quantity = line.Input.Quantity,
                FreeQuantity = line.Input.FreeQuantity,
                UnitPrice = line.Input.UnitPrice,
                DiscountAmount = line.Input.DiscountAmount,
                TaxRate = line.Drug.GstRate ?? 0m,
                TaxableAmount = line.TaxableAmount,
                CgstAmount = line.CgstAmount,
                SgstAmount = line.SgstAmount,
                IgstAmount = line.IgstAmount,
                LineTotal = line.TaxableAmount + line.TaxAmount
            });
            context.StockMovements.Add(new StockMovement
            {
                BatchId = line.Batch.Id,
                DrugId = line.Drug.Id,
                QuantityChange = -(line.Input.Quantity + line.Input.FreeQuantity),
                MovementType = "WholesaleSale",
                ReferenceType = nameof(WholesaleInvoice),
                ReferenceId = invoice.Id,
                MovementAtUtc = invoiceAtUtc,
                Notes = invoiceNo
            });
            context.WholesaleRateHistory.Add(new WholesaleRateHistory
            {
                CustomerId = customer.Id,
                DrugId = line.Drug.Id,
                BatchId = line.Batch.Id,
                InvoiceId = invoice.Id,
                SoldAtUtc = invoiceAtUtc,
                UnitRate = line.Input.UnitPrice,
                Quantity = line.Input.Quantity
            });
            if (line.Schedule is "H1" or "X" or "NDPS" ||
                IsHabitForming(line.Drug, catalogInfos))
            {
                context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
                {
                    RegisterType = line.Schedule ?? "HabitForming",
                    CustomerId = customer.Id,
                    DrugId = line.Drug.Id,
                    SupplierId = line.Batch.SupplierId,
                    BatchId = line.Batch.Id,
                    BatchNo = line.Batch.BatchNo,
                    Quantity = line.Input.Quantity + line.Input.FreeQuantity,
                    EntryAtUtc = invoiceAtUtc,
                    BuyerName = customer.Name,
                    BuyerAddress = customer.Address,
                    BuyerPhone = customer.Phone,
                    BuyerLicenceNumber = validLicences.First().LicenceNumber,
                    Notes = $"Wholesale invoice {invoiceNo}"
                });
            }
        }

        context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            CustomerId = customer.Id,
            EntryAtUtc = invoiceAtUtc,
            EntryType = "WholesaleInvoice",
            ReferenceId = invoice.Id,
            ReferenceNo = invoiceNo,
            Debit = roundedTotal,
            Notes = input.OverrideReason is null ? null : $"Owner override: {input.OverrideReason.Trim()}"
        });
        if (input.PaidAmount > 0)
        {
            var receipt = new Receipt
            {
                CustomerId = customer.Id,
                WholesaleInvoiceId = invoice.Id,
                ReceiptNo = $"{invoiceNo}-RCPT",
                ReceiptAtUtc = invoiceAtUtc,
                Amount = input.PaidAmount,
                PaymentMethod = input.PaymentMethod.Trim(),
                ReferenceNumber = NullIfWhiteSpace(input.PaymentReference),
                Notes = $"Against invoice {invoiceNo}"
            };
            context.Receipts.Add(receipt);
            context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                CustomerId = customer.Id,
                EntryAtUtc = invoiceAtUtc,
                EntryType = "Receipt",
                ReferenceId = receipt.Id,
                ReferenceNo = receipt.ReceiptNo,
                Credit = input.PaidAmount,
                Notes = receipt.PaymentMethod
            });
        }

        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = invoiceAtUtc,
            Action = "WholesaleInvoicePosted",
            EntityName = nameof(WholesaleInvoice),
            EntityId = invoice.Id,
            Details = $"Invoice {invoiceNo}; buyer {customer.Name}; total {roundedTotal}; taxes CGST {cgstTotal}, SGST {sgstTotal}, IGST {igstTotal}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WholesaleInvoiceResult(invoice, cgstTotal, sgstTotal, igstTotal, roundOff, outstandingAfter, hasOverdue);
    }

    public async Task CancelAsync(
        Guid invoiceId,
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role != UserRole.Owner || string.IsNullOrWhiteSpace(reason))
        {
            throw new UnauthorizedAccessException("Invoice cancellation requires the owner role and a reason.");
        }

        var context = unitOfWork.Context;
        var invoice = await context.WholesaleInvoices.SingleOrDefaultAsync(
            item => item.Id == invoiceId,
            cancellationToken) ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status != "Posted")
        {
            throw new InvalidOperationException("Only a posted wholesale invoice can be cancelled.");
        }

        var lines = await context.WholesaleInvoiceItems
            .Where(item => item.WholesaleInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        invoice.Status = "Cancelled";
        invoice.CancelledAtUtc = DateTime.UtcNow;
        invoice.CancellationReason = reason.Trim();
        var correction = new ReturnNote
        {
            ReturnNo = $"{invoice.InvoiceNo}-CANCEL",
            SourceType = nameof(WholesaleInvoice),
            SourceId = invoice.Id,
            ReturnAtUtc = DateTime.UtcNow,
            TotalAmount = invoice.TotalAmount,
            Reason = reason.Trim(),
            Notes = $"Cancellation correction; original invoice number retained: {invoice.InvoiceNo}"
        };
        context.ReturnNotes.Add(correction);
        foreach (var line in lines)
        {
            var batch = await context.Batches.SingleAsync(item => item.Id == line.BatchId, cancellationToken);
            var quantity = line.Quantity + line.FreeQuantity;
            batch.Quantity += quantity;
            context.StockMovements.Add(new StockMovement
            {
                BatchId = batch.Id,
                DrugId = line.DrugId,
                QuantityChange = quantity,
                MovementType = "WholesaleSaleCancellation",
                ReferenceType = nameof(ReturnNote),
                ReferenceId = correction.Id,
                Notes = reason.Trim()
            });
        }

        context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            CustomerId = invoice.CustomerId,
            EntryAtUtc = correction.ReturnAtUtc,
            EntryType = "WholesaleInvoiceCancellation",
            ReferenceId = correction.Id,
            ReferenceNo = correction.ReturnNo,
            Credit = invoice.TotalAmount,
            Notes = reason.Trim()
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = correction.ReturnAtUtc,
            Action = "WholesaleInvoiceCancelled",
            EntityName = nameof(WholesaleInvoice),
            EntityId = invoice.Id,
            Details = $"Invoice number retained: {invoice.InvoiceNo}; reason: {reason.Trim()}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerLicence>> GetValidLicencesAsync(
        Customer customer,
        PharmacyProfile profile,
        DateOnly invoiceDate,
        CancellationToken cancellationToken)
    {
        var rules = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            profile.WholesaleBuyerLicenceRulesJson) ?? [];
        var allowedTypes = rules.FirstOrDefault(rule =>
                string.Equals(rule.Key, customer.BuyerType, StringComparison.OrdinalIgnoreCase))
            .Value ?? [];
        if (allowedTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"No permitted drug licence types are configured for buyer type '{customer.BuyerType}'.");
        }

        var licences = await unitOfWork.Context.CustomerLicences
            .Where(licence => licence.CustomerId == customer.Id &&
                              licence.IssuedOn.HasValue && licence.IssuedOn.Value <= invoiceDate &&
                              licence.ExpiresOn.HasValue && licence.ExpiresOn.Value >= invoiceDate)
            .ToListAsync(cancellationToken);
        var valid = licences.Where(licence => allowedTypes.Contains(
            licence.LicenceType,
            StringComparer.OrdinalIgnoreCase)).ToArray();
        if (valid.Length == 0)
        {
            throw new InvalidOperationException(
                $"Customer {customer.Name} needs an issued, unexpired licence allowed for buyer type '{customer.BuyerType}'.");
        }

        return valid;
    }

    private async Task<bool> HasOverdueInvoicesAsync(
        Customer customer,
        DateOnly invoiceDate,
        CancellationToken cancellationToken)
    {
        if (customer.CreditDays <= 0)
        {
            return false;
        }

        var invoices = await unitOfWork.Context.WholesaleInvoices
            .Where(invoice => invoice.CustomerId == customer.Id && invoice.Status == "Posted")
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var receipts = await unitOfWork.Context.Receipts
            .Where(receipt => receipt.WholesaleInvoiceId.HasValue &&
                              invoiceIds.Contains(receipt.WholesaleInvoiceId.Value))
            .GroupBy(receipt => receipt.WholesaleInvoiceId!.Value)
            .Select(group => new { InvoiceId = group.Key, Amount = group.Sum(receipt => receipt.Amount) })
            .ToDictionaryAsync(item => item.InvoiceId, item => item.Amount, cancellationToken);
        return invoices.Any(invoice =>
            invoice.TotalAmount - Math.Max(invoice.PaidAmount, receipts.GetValueOrDefault(invoice.Id)) > 0 &&
            DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).AddDays(customer.CreditDays) < invoiceDate);
    }

    private static string? ResolveSchedule(
        Drug drug,
        IEnumerable<CatalogInfo> infos,
        IEnumerable<ScheduleOverride> overrides,
        DateTime atUtc)
    {
        var currentOverride = overrides
            .Where(item => item.DrugId == drug.Id &&
                           (!item.EffectiveFromUtc.HasValue || item.EffectiveFromUtc <= atUtc) &&
                           (!item.EffectiveToUtc.HasValue || item.EffectiveToUtc >= atUtc))
            .OrderByDescending(item => item.EffectiveFromUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(currentOverride?.Schedule))
        {
            return NormalizeSchedule(currentOverride.Schedule);
        }

        if (!string.IsNullOrWhiteSpace(drug.Schedule))
        {
            return NormalizeSchedule(drug.Schedule);
        }

        return infos.FirstOrDefault(info =>
            (drug.CatalogMedicineId.HasValue && info.CatalogMedicineId == drug.CatalogMedicineId) ||
            string.Equals(info.NameKey, drug.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))?.Schedule
            is { } schedule ? NormalizeSchedule(schedule) : null;
    }

    private static bool HasScheduleAuthorisation(string? authorisation, string schedule) =>
        !string.IsNullOrWhiteSpace(authorisation) &&
        (authorisation.Contains(schedule, StringComparison.OrdinalIgnoreCase) ||
         (schedule == "NDPS" && authorisation.Contains("Narcotic", StringComparison.OrdinalIgnoreCase)));

    private static bool IsHabitForming(
        Drug drug,
        IReadOnlyCollection<CatalogInfo> catalogInfos) =>
        catalogInfos.Any(info => info.IsHabitForming &&
            ((drug.CatalogMedicineId.HasValue && info.CatalogMedicineId == drug.CatalogMedicineId) ||
             string.Equals(info.NameKey, drug.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)));

    private static string? NormalizeSchedule(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Replace("SCHEDULE ", string.Empty, StringComparison.Ordinal);
        return normalized is "H1" or "X" or "NDPS" ? normalized : null;
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record PreparedLine(
        WholesaleInvoiceLineInput Input,
        Drug Drug,
        Batch Batch,
        string? Schedule,
        decimal TaxableAmount,
        decimal TaxAmount,
        decimal CgstAmount,
        decimal SgstAmount,
        decimal IgstAmount);
}
