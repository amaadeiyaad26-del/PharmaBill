using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record RetailStockChoice(
    Guid DrugId,
    string DrugName,
    string? Barcode,
    Guid BatchId,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Mrp,
    decimal? SalePrice,
    decimal AvailableQuantity,
    decimal GstRate,
    string? Schedule,
    bool IsHabitForming,
    string? RegisterType);

public sealed record RetailSaleLineInput(Guid DrugId, Guid BatchId, decimal Quantity, decimal DiscountAmount, decimal UnitPrice);

public sealed record RetailPaymentInput(string Method, decimal AppliedAmount, decimal TenderedAmount = 0m);

public sealed record SaveRetailSaleInput(
    string PatientName,
    string PatientPhone,
    string? PatientAddress,
    string? PrescriberName,
    string? PrescriberRegistrationNumber,
    string? PrescriptionDocumentPath,
    IReadOnlyList<RetailSaleLineInput> Items,
    IReadOnlyList<RetailPaymentInput> Payments,
    bool UpiPaymentReceived,
    string? Notes = null);

public sealed record RetailSaleResult(
    Sale Sale,
    decimal Subtotal,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal ChangeDue,
    string InvoiceNo);

public sealed record RecentRetailBill(
    Guid SaleId,
    string InvoiceNo,
    DateTime SaleAtUtc,
    string PatientName,
    string? PatientPhone,
    decimal TotalAmount,
    decimal PaidAmount);

public sealed class RetailSaleReturnableLine
{
    public Guid SaleItemId { get; init; }
    public string DrugName { get; init; } = string.Empty;
    public string BatchNo { get; init; } = string.Empty;
    public DateOnly? ExpiryDate { get; init; }
    public decimal SoldQuantity { get; init; }
    public decimal ReturnedQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal ReturnQuantity { get; set; }
}

public sealed record RetailSaleReturnLineInput(Guid SaleItemId, decimal Quantity);

public sealed record RetailSaleReturnResult(string ReturnNo, decimal CreditAmount, decimal RestockedQuantity);

public sealed record RetailBillPrintLine(
    string DrugName,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal LineTotal);

public sealed record RetailBillPrintData(
    string PharmacyName,
    string? PharmacyAddress,
    string? PharmacyPhone,
    string? Gstin,
    IReadOnlyList<string> LicenceNumbers,
    string? PharmacistName,
    string? PharmacistQualification,
    string? PharmacistRegistrationNumber,
    string InvoiceNo,
    DateTime SaleAtUtc,
    string PatientName,
    string? PatientPhone,
    string? PatientAddress,
    decimal Subtotal,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    IReadOnlyList<RetailBillPrintLine> Items);

public sealed class RetailBillingService(
    IUnitOfWork unitOfWork,
    NumberSeriesService numberSeriesService,
    IEntitlementService entitlementService,
    CatalogSearchService catalogSearchService,
    string? prescriptionStorageDirectory = null)
{
    public async Task<IReadOnlyList<RetailStockChoice>> SearchStockAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var context = unitOfWork.Context;
        var searchTerm = query.Trim();
        var drugs = await context.Drugs.AsNoTracking()
            .Where(drug => drug.IsActive &&
                (drug.Barcode == searchTerm ||
                 EF.Functions.Like(drug.Name, $"%{searchTerm}%") ||
                 EF.Functions.Like(drug.BrandName ?? string.Empty, $"%{searchTerm}%") ||
                 EF.Functions.Like(drug.GenericName ?? string.Empty, $"%{searchTerm}%")))
            .Take(50)
            .ToListAsync(cancellationToken);

        if (searchTerm.Length >= 2 && drugs.Count == 0)
        {
            var catalogResults = await catalogSearchService.SearchAsync(searchTerm, cancellationToken);
            var catalogIds = catalogResults.InStock
                .Concat(catalogResults.FromCatalog)
                .Where(result => result.IsInStock)
                .Select(result => result.CatalogMedicineId)
                .Distinct()
                .ToArray();
            if (catalogIds.Length > 0)
            {
                var linkedDrugs = await context.Drugs.AsNoTracking()
                    .Where(drug => drug.IsActive &&
                        drug.CatalogMedicineId.HasValue &&
                        catalogIds.Contains(drug.CatalogMedicineId.Value))
                    .ToListAsync(cancellationToken);
                drugs.AddRange(linkedDrugs.Where(linked => drugs.All(existing => existing.Id != linked.Id)));
            }
        }

        if (drugs.Count == 0)
        {
            return [];
        }

        var drugIds = drugs.Select(drug => drug.Id).Distinct().ToArray();
        var batches = await context.Batches.AsNoTracking()
            .Where(batch => drugIds.Contains(batch.DrugId))
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var stock = await context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
        var catalogInfo = await context.CatalogInfos.AsNoTracking().ToListAsync(cancellationToken);
        var overrides = await context.ScheduleOverrides.AsNoTracking()
            .Where(item => drugIds.Contains(item.DrugId))
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return drugs
            .SelectMany(drug => batches
                .Where(batch => batch.DrugId == drug.Id)
                .Where(batch => !batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= today)
                .Where(batch => stock.GetValueOrDefault(batch.Id) > 0)
                .OrderBy(batch => batch.ExpiryDate ?? DateOnly.MaxValue)
                .ThenBy(batch => batch.BatchNo, StringComparer.OrdinalIgnoreCase)
                .Select(batch =>
                {
                    var medicineInfo = FindCatalogInfo(drug, catalogInfo);
                    return new RetailStockChoice(
                        drug.Id,
                        drug.Name,
                        drug.Barcode,
                        batch.Id,
                        batch.BatchNo,
                        batch.ExpiryDate,
                        batch.Mrp ?? drug.Mrp ?? 0m,
                        batch.SalePrice ?? drug.SalePrice,
                        stock.GetValueOrDefault(batch.Id),
                        drug.GstRate ?? 0m,
                        ResolveSchedule(drug, medicineInfo, overrides.Where(item => item.DrugId == drug.Id).ToList()),
                        medicineInfo?.IsHabitForming == true,
                        medicineInfo?.RegisterType);
                }))
            .OrderByDescending(choice => choice.Barcode == searchTerm)
            .ThenBy(choice => choice.DrugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.ExpiryDate ?? DateOnly.MaxValue)
            .ToArray();
    }

    public async Task<string> PreviewNextInvoiceNoAsync(CancellationToken cancellationToken = default)
    {
        var prefix = await unitOfWork.Context.PharmacyProfiles.AsNoTracking()
            .Select(profile => profile.InvoicePrefix)
            .FirstOrDefaultAsync(cancellationToken);
        return await numberSeriesService.PreviewNextAsync(
            string.IsNullOrWhiteSpace(prefix) ? NumberSeriesService.DefaultPrefix : prefix,
            DateOnly.FromDateTime(DateTime.Today),
            cancellationToken);
    }

    public async Task<RetailSaleResult> SaveSaleAsync(
        SaveRetailSaleInput input,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!PermissionMatrix.Allows(role, AppPermission.CreateBill))
        {
            throw new UnauthorizedAccessException("Your role cannot create retail bills.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.CreateBill, cancellationToken))
        {
            throw new InvalidOperationException("Billing is unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(input.PatientName);
        var phone = NormalizePhone(input.PatientPhone);
        if (phone.Count(char.IsDigit) < 7)
        {
            throw new InvalidOperationException("Enter a valid patient phone number.");
        }

        if (input.Items.Count == 0)
        {
            throw new InvalidOperationException("Add at least one medicine to the bill.");
        }

        var context = unitOfWork.Context;
        var profile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
        if (profile.BusinessMode == BusinessMode.Wholesaler)
        {
            throw new InvalidOperationException("Retail billing is unavailable in Wholesaler mode.");
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var drugIds = input.Items.Select(item => item.DrugId).Distinct().ToArray();
        var batchIds = input.Items.Select(item => item.BatchId).Distinct().ToArray();
        var drugs = await context.Drugs.Where(drug => drugIds.Contains(drug.Id) && drug.IsActive)
            .ToDictionaryAsync(drug => drug.Id, cancellationToken);
        var batches = await context.Batches.Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        if (drugs.Count != drugIds.Length || batches.Count != batchIds.Length ||
            input.Items.Any(item => !batches.TryGetValue(item.BatchId, out var batch) || batch.DrugId != item.DrugId))
        {
            throw new InvalidOperationException("One or more bill items refer to an unavailable medicine or batch.");
        }

        var currentQuantities = await context.StockMovements
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
        var infos = await context.CatalogInfos.AsNoTracking().ToListAsync(cancellationToken);
        var overrides = await context.ScheduleOverrides.AsNoTracking()
            .Where(item => drugIds.Contains(item.DrugId))
            .ToListAsync(cancellationToken);

        var preparedLines = new List<PreparedSaleLine>(input.Items.Count);
        foreach (var line in input.Items)
        {
            if (line.Quantity <= 0 || line.DiscountAmount < 0 || line.UnitPrice < 0)
            {
                throw new InvalidOperationException("Quantity and price must be positive; discount cannot be negative.");
            }

            var roundedLine = line with
            {
                DiscountAmount = RetailTaxCalculator.RoundMoney(line.DiscountAmount)
            };
            var drug = drugs[line.DrugId];
            var batch = batches[line.BatchId];
            if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < today)
            {
                throw new InvalidOperationException($"Expired batch {batch.BatchNo} cannot be sold.");
            }

            var available = currentQuantities.GetValueOrDefault(batch.Id);
            if (line.Quantity > available)
            {
                throw new InvalidOperationException(
                    $"Quantity exceeds available stock for {drug.Name}, batch {batch.BatchNo} ({available} available).");
            }

            var mrp = batch.Mrp ?? drug.Mrp;
            if (!mrp.HasValue || mrp.Value <= 0)
            {
                throw new InvalidOperationException($"A valid MRP is required for {drug.Name} before it can be billed.");
            }

            if (line.UnitPrice > mrp.Value)
            {
                throw new InvalidOperationException($"Selling price for {drug.Name} cannot exceed its batch MRP.");
            }

            var medicineInfo = FindCatalogInfo(drug, infos);
            var schedule = ResolveSchedule(drug, medicineInfo, overrides.Where(item => item.DrugId == drug.Id).ToList());
            var requiresPrescription = schedule is "H1" or "X" or "NDPS";
            preparedLines.Add(new PreparedSaleLine(
                roundedLine,
                drug,
                batch,
                schedule,
                requiresPrescription,
                medicineInfo?.IsHabitForming == true,
                medicineInfo?.RegisterType,
                RetailTaxCalculator.SplitInclusive(
                    roundedLine.Quantity,
                    roundedLine.UnitPrice,
                    drug.GstRate ?? 0m,
                    roundedLine.DiscountAmount)));
            currentQuantities[batch.Id] = available - roundedLine.Quantity;
        }

        var controlledItems = preparedLines.Where(line => line.RequiresPrescription).ToArray();
        if (controlledItems.Length > 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(input.PatientAddress);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.PrescriberName);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.PrescriberRegistrationNumber);
            if (string.IsNullOrWhiteSpace(input.PrescriptionDocumentPath))
            {
                throw new InvalidOperationException("Attach a prescription image or PDF for H1, X, and NDPS items.");
            }
        }

        var subtotal = preparedLines.Sum(line => line.Tax.NetAmount);
        var taxAmount = preparedLines.Sum(line => line.Tax.TaxAmount);
        var discountAmount = preparedLines.Sum(line => RetailTaxCalculator.RoundMoney(line.Input.DiscountAmount));
        var totalAmount = preparedLines.Sum(line => line.Tax.GrossAmount);
        var appliedPayments = ValidatePayments(input, totalAmount);
        var cashTendered = input.Payments
            .Where(payment => payment.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => payment.TenderedAmount > 0 ? payment.TenderedAmount : payment.AppliedAmount);
        var cashApplied = input.Payments
            .Where(payment => payment.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => payment.AppliedAmount);
        var changeDue = RetailTaxCalculator.RoundMoney(Math.Max(0m, cashTendered - cashApplied));
        var attachmentPath = await CopyPrescriptionAsync(
            input.PrescriptionDocumentPath,
            controlledItems.Length > 0,
            prescriptionStorageDirectory,
            cancellationToken);
        var shouldKeepAttachment = false;

        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            var patient = await context.Patients.SingleOrDefaultAsync(
                item => item.Phone == phone,
                cancellationToken);
            if (patient is null)
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

            Prescription? prescription = null;
            if (controlledItems.Length > 0 || attachmentPath is not null ||
                !string.IsNullOrWhiteSpace(input.PrescriberName) ||
                !string.IsNullOrWhiteSpace(input.PrescriberRegistrationNumber))
            {
                prescription = new Prescription
                {
                    PatientId = patient.Id,
                    PrescriberName = NullIfWhiteSpace(input.PrescriberName),
                    PrescriberRegistrationNumber = NullIfWhiteSpace(input.PrescriberRegistrationNumber),
                    PrescriptionDate = today,
                    Notes = attachmentPath is null ? null : $"DocumentPath={attachmentPath}"
                };
                context.Prescriptions.Add(prescription);
            }

            var invoiceNo = await numberSeriesService.AllocateAsync(
                string.IsNullOrWhiteSpace(profile.InvoicePrefix) ? NumberSeriesService.DefaultPrefix : profile.InvoicePrefix,
                today,
                cancellationToken);
            var paidAmount = appliedPayments;
            var sale = new Sale
            {
                PatientId = patient.Id,
                PrescriptionId = prescription?.Id,
                InvoiceNo = invoiceNo,
                SaleAtUtc = DateTime.UtcNow,
                Subtotal = subtotal,
                TaxAmount = taxAmount,
                DiscountAmount = discountAmount,
                TotalAmount = totalAmount,
                PaidAmount = paidAmount,
                PaymentStatus = paidAmount >= totalAmount ? "Paid" : paidAmount > 0 ? "Partial" : "Credit",
                Notes = NullIfWhiteSpace(input.Notes)
            };
            context.Sales.Add(sale);

            foreach (var prepared in preparedLines)
            {
                var line = prepared.Input;
                var batch = prepared.Batch;
                batch.Quantity = currentQuantities[batch.Id];
                context.SaleItems.Add(new SaleItem
                {
                    SaleId = sale.Id,
                    DrugId = line.DrugId,
                    BatchId = line.BatchId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountAmount = line.DiscountAmount,
                    TaxRate = prepared.Drug.GstRate ?? 0m,
                    LineTotal = prepared.Tax.GrossAmount
                });
                context.StockMovements.Add(new StockMovement
                {
                    BatchId = batch.Id,
                    DrugId = line.DrugId,
                    QuantityChange = -line.Quantity,
                    MovementType = "RetailSale",
                    ReferenceType = nameof(Sale),
                    ReferenceId = sale.Id,
                    MovementAtUtc = sale.SaleAtUtc,
                    Notes = sale.InvoiceNo
                });

                if (prepared.RequiresPrescription || prepared.IsHabitForming)
                {
                    context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
                    {
                        RegisterType = prepared.Schedule ?? prepared.RegisterType ?? "HabitForming",
                        SaleId = sale.Id,
                        PatientId = patient.Id,
                        DrugId = prepared.Drug.Id,
                        BatchId = batch.Id,
                        BatchNo = batch.BatchNo,
                        Quantity = line.Quantity,
                        EntryAtUtc = sale.SaleAtUtc,
                        PatientName = patient.Name,
                        PrescriberName = prescription?.PrescriberName,
                        PrescriberRegistrationNumber = prescription?.PrescriberRegistrationNumber,
                        Notes = prepared.IsHabitForming
                            ? "Habit-forming classification is reference data; verify against the prescription and applicable rules."
                            : null
                    });
                }
            }

            foreach (var payment in input.Payments.Where(payment => payment.AppliedAmount > 0))
            {
                var receiptNo = await numberSeriesService.AllocateAsync(
                    $"{profile.InvoicePrefix}-R",
                    today,
                    cancellationToken);
                context.Receipts.Add(new Receipt
                {
                    ReceiptNo = receiptNo,
                    ReceiptAtUtc = sale.SaleAtUtc,
                    Amount = RetailTaxCalculator.RoundMoney(payment.AppliedAmount),
                    PaymentMethod = payment.Method.Trim(),
                    Notes = $"SaleId={sale.Id:D}; InvoiceNo={invoiceNo}"
                });
            }

            context.AuditLogs.Add(new AuditLog
            {
                UserId = actingUserId,
                Action = "RetailSalePosted",
                EntityName = nameof(Sale),
                EntityId = sale.Id,
                Details = $"Invoice {invoiceNo}; total {totalAmount:F2}; paid {paidAmount:F2}"
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            shouldKeepAttachment = attachmentPath is not null;
            return new RetailSaleResult(
                sale,
                subtotal,
                taxAmount,
                discountAmount,
                totalAmount,
                paidAmount,
                changeDue,
                invoiceNo);
        }
        finally
        {
            if (!shouldKeepAttachment && attachmentPath is not null && File.Exists(attachmentPath))
            {
                File.Delete(attachmentPath);
            }
        }
    }

    public async Task<IReadOnlyList<RecentRetailBill>> SearchRecentBillsAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var terms = unitOfWork.Context.Sales.AsNoTracking();
        Guid[] matchingPatientIds = [];
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            matchingPatientIds = await unitOfWork.Context.Patients.AsNoTracking()
                .Where(patient => EF.Functions.Like(patient.Name, $"%{term}%") ||
                                  EF.Functions.Like(patient.Phone ?? string.Empty, $"%{term}%"))
                .Select(patient => patient.Id)
                .ToArrayAsync(cancellationToken);
            terms = terms.Where(sale =>
                EF.Functions.Like(sale.InvoiceNo, $"%{term}%") ||
                (sale.PatientId.HasValue && matchingPatientIds.Contains(sale.PatientId.Value)));
        }

        var sales = await terms.OrderByDescending(sale => sale.SaleAtUtc)
            .Take(100)
            .Select(sale => new
            {
                sale.Id,
                sale.InvoiceNo,
                sale.SaleAtUtc,
                sale.PatientId,
                sale.TotalAmount,
                sale.PaidAmount
            })
            .ToListAsync(cancellationToken);
        var patientIds = sales.Where(sale => sale.PatientId.HasValue)
            .Select(sale => sale.PatientId!.Value)
            .Distinct()
            .ToArray();
        var patients = await unitOfWork.Context.Patients.AsNoTracking()
            .Where(patient => patientIds.Contains(patient.Id))
            .ToDictionaryAsync(patient => patient.Id, cancellationToken);
        return sales.Select(sale =>
        {
            patients.TryGetValue(sale.PatientId ?? Guid.Empty, out var patient);
            return new RecentRetailBill(
                sale.Id,
                sale.InvoiceNo,
                sale.SaleAtUtc,
                patient?.Name ?? string.Empty,
                patient?.Phone,
                sale.TotalAmount,
                sale.PaidAmount);
        }).ToArray();
    }

    public async Task<Sale> GetSaleAsync(Guid saleId, CancellationToken cancellationToken = default) =>
        await unitOfWork.Context.Sales.SingleOrDefaultAsync(sale => sale.Id == saleId, cancellationToken)
        ?? throw new InvalidOperationException("The selected bill could not be found.");

    public async Task<RetailBillPrintData> GetPrintableBillAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var context = unitOfWork.Context;
        var sale = await context.Sales.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == saleId, cancellationToken)
            ?? throw new InvalidOperationException("The selected bill could not be found.");
        var profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
        var patient = sale.PatientId.HasValue
            ? await context.Patients.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == sale.PatientId, cancellationToken)
            : null;
        var saleItems = await context.SaleItems.AsNoTracking()
            .Where(item => item.SaleId == saleId)
            .ToListAsync(cancellationToken);
        var drugIds = saleItems.Select(item => item.DrugId).Distinct().ToArray();
        var batchIds = saleItems.Select(item => item.BatchId).Distinct().ToArray();
        var drugs = await context.Drugs.AsNoTracking().Where(item => drugIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var batches = await context.Batches.AsNoTracking().Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var licences = await context.LicenceRecords.AsNoTracking()
            .Where(item => item.LicenceNumber != string.Empty)
            .Select(item => item.LicenceNumber)
            .ToArrayAsync(cancellationToken);
        return new RetailBillPrintData(
            profile.Name,
            profile.Address,
            profile.Phone,
            profile.Gstin,
            licences,
            profile.CompetentPersonName,
            profile.CompetentPersonQualification,
            profile.CompetentPersonRegistrationNumber,
            sale.InvoiceNo,
            sale.SaleAtUtc,
            patient?.Name ?? string.Empty,
            patient?.Phone,
            patient?.Address,
            sale.Subtotal,
            sale.TaxAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.PaidAmount,
            saleItems.Select(item => new RetailBillPrintLine(
                drugs.GetValueOrDefault(item.DrugId, "Unknown medicine"),
                batches.GetValueOrDefault(item.BatchId)?.BatchNo ?? "Unknown batch",
                batches.GetValueOrDefault(item.BatchId)?.ExpiryDate,
                item.Quantity,
                item.UnitPrice,
                item.TaxRate,
                RetailTaxCalculator.SplitInclusive(
                    item.Quantity,
                    item.UnitPrice,
                    item.TaxRate,
                    item.DiscountAmount).TaxAmount,
                item.DiscountAmount,
                item.LineTotal)).ToArray());
    }

    public async Task<IReadOnlyList<RetailSaleReturnableLine>> GetSaleReturnLinesAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var context = unitOfWork.Context;
        if (!await context.Sales.AnyAsync(item => item.Id == saleId, cancellationToken))
        {
            throw new InvalidOperationException("The original bill could not be found.");
        }

        var saleItems = await context.SaleItems.AsNoTracking()
            .Where(item => item.SaleId == saleId)
            .ToListAsync(cancellationToken);
        var priorReturns = await GetPriorReturnItemsAsync(saleId, cancellationToken);
        var quantities = priorReturns.GroupBy(item => item.SaleItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var drugIds = saleItems.Select(item => item.DrugId).Distinct().ToArray();
        var batchIds = saleItems.Select(item => item.BatchId).Distinct().ToArray();
        var drugs = await context.Drugs.AsNoTracking()
            .Where(item => drugIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var batches = await context.Batches.AsNoTracking()
            .Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        return saleItems.Select(item =>
        {
            var returned = quantities.GetValueOrDefault(item.Id);
            return new RetailSaleReturnableLine
            {
                SaleItemId = item.Id,
                DrugName = drugs.GetValueOrDefault(item.DrugId)?.Name ?? "Unknown medicine",
                BatchNo = batches.GetValueOrDefault(item.BatchId)?.BatchNo ?? "Unknown batch",
                ExpiryDate = batches.GetValueOrDefault(item.BatchId)?.ExpiryDate,
                SoldQuantity = item.Quantity,
                ReturnedQuantity = returned,
                RemainingQuantity = Math.Max(0m, item.Quantity - returned),
                UnitPrice = item.Quantity > 0 ? item.LineTotal / item.Quantity : 0m
            };
        }).ToArray();
    }

    public async Task<RetailSaleReturnResult> IssueSalesReturnAsync(
        Guid saleId,
        IReadOnlyCollection<RetailSaleReturnLineInput> requestedLines,
        string reason,
        bool restock,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
        {
            throw new UnauthorizedAccessException("Your role cannot process retail sales returns.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken))
        {
            throw new InvalidOperationException("Sales returns are unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(requestedLines);
        if (requestedLines.Count == 0 ||
            requestedLines.Any(line => line.Quantity <= 0) ||
            requestedLines.Select(line => line.SaleItemId).Distinct().Count() != requestedLines.Count)
        {
            throw new ArgumentException("Select one or more return quantities greater than zero.", nameof(requestedLines));
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var sale = await unitOfWork.Context.Sales.SingleOrDefaultAsync(item => item.Id == saleId, cancellationToken)
            ?? throw new InvalidOperationException("The original bill could not be found.");

        var priorNotes = await unitOfWork.Context.ReturnNotes
            .Where(note => note.SourceType == nameof(Sale) && note.SourceId == saleId)
            .ToListAsync(cancellationToken);
        var priorReturns = await GetPriorReturnItemsAsync(saleId, cancellationToken);
        var priorNoteIdsWithItems = priorReturns.Select(item => item.ReturnNoteId).ToHashSet();
        if (priorNotes.Any(note => !priorNoteIdsWithItems.Contains(note.Id)))
        {
            throw new InvalidOperationException(
                "An older credit note has no item-level return details. Review it before processing another return.");
        }

        var saleItems = await unitOfWork.Context.SaleItems
            .Where(item => item.SaleId == saleId)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var batchIds = saleItems.Values.Select(line => line.BatchId).Distinct().ToArray();
        var batches = await unitOfWork.Context.Batches
            .Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var returnedQuantities = priorReturns.GroupBy(item => item.SaleItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var returnedCredits = priorReturns.GroupBy(item => item.SaleItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.CreditAmount));
        var processed = new List<(SaleItem SaleItem, Batch Batch, decimal Quantity, decimal CreditAmount)>();
        foreach (var requested in requestedLines)
        {
            if (!saleItems.TryGetValue(requested.SaleItemId, out var saleItem) ||
                !batches.TryGetValue(saleItem.BatchId, out var batch))
            {
                throw new InvalidOperationException("A selected return line does not belong to the original bill.");
            }

            var alreadyReturned = returnedQuantities.GetValueOrDefault(saleItem.Id);
            var remainingQuantity = saleItem.Quantity - alreadyReturned;
            if (requested.Quantity > remainingQuantity)
            {
                throw new InvalidOperationException(
                    $"Return quantity for {saleItem.Id} exceeds the remaining bill quantity ({remainingQuantity}).");
            }

            if (restock && batch.ExpiryDate is { } expiryDate &&
                expiryDate <= DateOnly.FromDateTime(DateTime.Today))
            {
                throw new InvalidOperationException(
                    $"Expired batch {batch.BatchNo} cannot be returned to saleable stock. Clear restock confirmation to issue a credit-only return.");
            }

            var remainingCredit = RetailTaxCalculator.RoundMoney(
                saleItem.LineTotal - returnedCredits.GetValueOrDefault(saleItem.Id));
            var creditAmount = requested.Quantity == remainingQuantity
                ? remainingCredit
                : RetailTaxCalculator.RoundMoney(saleItem.LineTotal * requested.Quantity / saleItem.Quantity);
            creditAmount = Math.Min(creditAmount, remainingCredit);
            processed.Add((saleItem, batch, requested.Quantity, creditAmount));
        }

        var creditTotal = RetailTaxCalculator.RoundMoney(processed.Sum(item => item.CreditAmount));
        if (creditTotal <= 0m)
        {
            throw new InvalidOperationException("The selected bill quantities have no remaining amount to credit.");
        }

        var profile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
        var returnNo = await numberSeriesService.AllocateAsync(
            $"{profile.InvoicePrefix}-CN",
            DateOnly.FromDateTime(DateTime.Today),
            cancellationToken);
        var note = new ReturnNote
        {
            ReturnNo = returnNo,
            SourceType = nameof(Sale),
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
            var returnItem = new SaleReturnItem
            {
                ReturnNoteId = note.Id,
                SaleItemId = item.SaleItem.Id,
                BatchId = item.Batch.Id,
                DrugId = item.SaleItem.DrugId,
                Quantity = item.Quantity,
                CreditAmount = item.CreditAmount,
                Restocked = restock
            };
            unitOfWork.Context.SaleReturnItems.Add(returnItem);
            if (!restock)
            {
                continue;
            }

            var movementQuantity = await unitOfWork.Context.StockMovements
                .Where(movement => movement.BatchId == item.Batch.Id)
                .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0m;
            item.Batch.Quantity = movementQuantity + item.Quantity;
            restockedQuantity += item.Quantity;
            unitOfWork.Context.StockMovements.Add(new StockMovement
            {
                BatchId = item.Batch.Id,
                DrugId = item.SaleItem.DrugId,
                QuantityChange = item.Quantity,
                MovementType = "RetailSaleReturn",
                ReferenceType = nameof(ReturnNote),
                ReferenceId = note.Id,
                MovementAtUtc = note.ReturnAtUtc,
                Notes = returnNo
            });
        }

        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "RetailSalesReturnProcessed",
            EntityName = nameof(ReturnNote),
            EntityId = note.Id,
            Details = $"Credit note {returnNo} against {sale.InvoiceNo}; amount {creditTotal:F2}; restocked: {restockedQuantity}; reason: {reason.Trim()}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RetailSaleReturnResult(returnNo, creditTotal, restockedQuantity);
    }

    private async Task<IReadOnlyList<SaleReturnItem>> GetPriorReturnItemsAsync(
        Guid saleId,
        CancellationToken cancellationToken)
    {
        var noteIds = await unitOfWork.Context.ReturnNotes
            .Where(note => note.SourceType == nameof(Sale) && note.SourceId == saleId)
            .Select(note => note.Id)
            .ToArrayAsync(cancellationToken);
        return await unitOfWork.Context.SaleReturnItems
            .Where(item => noteIds.Contains(item.ReturnNoteId))
            .ToListAsync(cancellationToken);
    }

    private static decimal ValidatePayments(SaveRetailSaleInput input, decimal total)
    {
        decimal applied = 0;
        foreach (var payment in input.Payments)
        {
            if (payment.Method is not ("Cash" or "UPI" or "Card" or "Credit"))
            {
                throw new InvalidOperationException($"Unsupported payment method '{payment.Method}'.");
            }

            if (payment.AppliedAmount < 0 || payment.TenderedAmount < 0)
            {
                throw new InvalidOperationException("Payment amounts cannot be negative.");
            }

            if (payment.Method == "Cash" &&
                payment.TenderedAmount > 0 &&
                payment.TenderedAmount < payment.AppliedAmount)
            {
                throw new InvalidOperationException("Cash tendered cannot be less than the cash amount applied.");
            }

            if (payment.Method == "UPI" && payment.AppliedAmount > 0 && !input.UpiPaymentReceived)
            {
                throw new InvalidOperationException("Confirm that the UPI payment was received before saving.");
            }

            if (payment.Method == "Credit" && payment.AppliedAmount != 0)
            {
                throw new InvalidOperationException("Credit is the unpaid balance, not a received payment.");
            }

            applied += RetailTaxCalculator.RoundMoney(payment.AppliedAmount);
        }

        if (applied > total)
        {
            throw new InvalidOperationException("Applied payments cannot exceed the bill total.");
        }

        return RetailTaxCalculator.RoundMoney(applied);
    }

    private static async Task<string?> CopyPrescriptionAsync(
        string? sourcePath,
        bool required,
        string? storageDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            if (required)
            {
                throw new InvalidOperationException("Attach a prescription image or PDF for H1, X, and NDPS items.");
            }

            return null;
        }

        var fullPath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".bmp"))
        {
            throw new InvalidOperationException("Prescription attachment must be a PDF or image.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The prescription attachment could not be found.", fullPath);
        }

        var destinationDirectory = storageDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PharmaBill",
                "prescriptions");
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, $"{Guid.NewGuid():N}{extension}");
        await using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(target, cancellationToken);
        return destination;
    }

    private static CatalogInfo? FindCatalogInfo(Drug drug, IReadOnlyCollection<CatalogInfo> infos) =>
        infos.FirstOrDefault(info => drug.CatalogMedicineId.HasValue &&
                                     info.CatalogMedicineId == drug.CatalogMedicineId) ??
        infos.FirstOrDefault(info => string.Equals(
            info.NameKey,
            drug.Name.Trim().ToLowerInvariant(),
            StringComparison.OrdinalIgnoreCase));

    private static string? ResolveSchedule(Drug drug, CatalogInfo? info, IReadOnlyCollection<ScheduleOverride> overrides)
    {
        var today = DateTime.UtcNow;
        var activeOverride = overrides
            .Where(item => (!item.EffectiveFromUtc.HasValue || item.EffectiveFromUtc <= today) &&
                           (!item.EffectiveToUtc.HasValue || item.EffectiveToUtc >= today))
            .OrderByDescending(item => item.EffectiveFromUtc)
            .FirstOrDefault();
        return NormalizeSchedule(activeOverride?.Schedule ?? drug.Schedule ?? info?.Schedule);
    }

    private static string? NormalizeSchedule(string? schedule) =>
        string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim().ToUpperInvariant();

    private static string NormalizePhone(string value) =>
        string.Concat(value.Where(character => char.IsDigit(character) || character == '+')).Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record PreparedSaleLine(
        RetailSaleLineInput Input,
        Drug Drug,
        Batch Batch,
        string? Schedule,
        bool RequiresPrescription,
        bool IsHabitForming,
        string? RegisterType,
        InclusiveTaxLine Tax);
}
