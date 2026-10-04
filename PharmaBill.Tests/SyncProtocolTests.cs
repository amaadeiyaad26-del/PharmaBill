using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;
using Xunit;

namespace PharmaBill.Tests;

public sealed class SyncProtocolTests
{
    private static readonly Guid RemoteDeviceId = Guid.Parse("00000000-0000-4000-8000-000000000002");

    [Fact]
    public void SharedVectors_HlcAndFinancialYearMatch()
    {
        using var conflicts = ReadVector("conflicts.json");
        foreach (var testCase in conflicts.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!testCase.TryGetProperty("local", out var local))
            {
                continue;
            }

            var actualWinner = HybridLogicalClock.Compare(
                local.GetString()!,
                testCase.GetProperty("remote").GetString()!) < 0 ? "remote" : "local";
            Assert.Equal(testCase.GetProperty("winner").GetString(), actualWinner);
        }

        using var numbering = ReadVector("numbering.json");
        foreach (var testCase in numbering.RootElement.GetProperty("cases").EnumerateArray())
        {
            var date = DateOnly.Parse(testCase.GetProperty("date").GetString()!);
            var year = NumberSeriesService.GetFinancialYear(date);
            var prefix = testCase.GetProperty("prefix").GetString();
            var nextNumber = testCase.GetProperty("nextNumber").GetInt32();
            Assert.Equal(testCase.GetProperty("financialYear").GetString(), year);
            Assert.Equal(testCase.GetProperty("formatted").GetString(), $"{prefix}/{year}/{nextNumber:D6}");
        }
    }

    [Fact]
    public void SharedVectors_GstInclusiveSplitAndRoundingMatch()
    {
        using var gst = ReadVector("gst.json");
        foreach (var testCase in gst.RootElement.GetProperty("cases").EnumerateArray())
        {
            var gross = Money.FromPaise(testCase.GetProperty("pricePaise").GetInt64());
            var rate = testCase.GetProperty("gstRatePercent").GetDecimal();
            var quantity = testCase.GetProperty("quantity").GetDecimal();
            var line = RetailTaxCalculator.SplitInclusive(quantity, gross, rate, 0m);
            Assert.Equal(testCase.GetProperty("taxableValuePaise").GetInt64(), Money.ToPaise(line.NetAmount));
            Assert.Equal(testCase.GetProperty("taxPaise").GetInt64(), Money.ToPaise(line.TaxAmount));
            Assert.Equal(testCase.GetProperty("totalPaise").GetInt64(), Money.ToPaise(line.GrossAmount));
            Assert.Equal(
                testCase.GetProperty("taxPaise").GetInt64(),
                testCase.GetProperty("cgstPaise").GetInt64() + testCase.GetProperty("sgstPaise").GetInt64());
        }

        using var rounding = ReadVector("rounding.json");
        foreach (var testCase in rounding.RootElement.GetProperty("cases").EnumerateArray())
        {
            var amountInRupees = testCase.GetProperty("valuePaise").GetDecimal() / 100m;
            Assert.Equal(
                testCase.GetProperty("roundedPaise").GetInt64(),
                Money.ToPaise(amountInRupees));
        }
    }

    [Fact]
    public async Task ChangeApplier_IsIdempotentAndOrdersOutOfOrderChanges()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var serializer = new SyncPayloadSerializer();
        var applier = CreateApplier(database.Context, Guid.NewGuid(), serializer);
        var drugId = Guid.NewGuid();
        var older = CreateEnvelope(serializer, new Drug { Id = drugId, Name = "Older name" }, 1_770_000_000_000, 1);
        var newer = CreateEnvelope(serializer, new Drug { Id = drugId, Name = "Newer name" }, 1_770_000_000_000, 2);

        var result = await applier.ApplyAsync([newer, older]);
        var repeated = await applier.ApplyAsync([older, newer]);

        Assert.Equal(2, result.Applied);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(2, repeated.Duplicates);
        Assert.Equal("Newer name", (await database.Context.Drugs.SingleAsync(item => item.Id == drugId)).Name);
        Assert.Single(await database.Context.SyncConflicts.ToListAsync());
    }

    [Fact]
    public async Task ChangeApplier_RecomputesStockAndRaisesConflictForTwoDeviceOverdraw()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drugId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        database.Context.Batches.Add(new Batch { Id = batchId, DrugId = drugId, BatchNo = "B-1" });
        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = batchId,
            DrugId = drugId,
            QuantityChange = 10,
            MovementType = "Opening",
            DeviceId = database.Context.DeviceId
        });
        await database.Context.SaveChangesAsync();

        var serializer = new SyncPayloadSerializer();
        var applier = CreateApplier(database.Context, Guid.NewGuid(), serializer);
        var stockPhysicalTime = new DateTimeOffset(DateTime.UtcNow.AddMinutes(5)).ToUnixTimeMilliseconds();
        var movementA = CreateEnvelope(serializer, new StockMovement
        {
            BatchId = batchId, DrugId = drugId, QuantityChange = -6, MovementType = "Sale"
        }, stockPhysicalTime, 1);
        var movementB = CreateEnvelope(serializer, new StockMovement
        {
            BatchId = batchId, DrugId = drugId, QuantityChange = -7, MovementType = "Sale"
        }, stockPhysicalTime, 2);

        using var stockVector = ReadVector("stock.json");
        var testCase = stockVector.RootElement.GetProperty("cases")[0];
        Assert.Equal(-3m, testCase.GetProperty("expectedClosing").GetDecimal());
        Assert.True(testCase.GetProperty("expectStockConflict").GetBoolean());

        var result = await applier.ApplyAsync([movementB, movementA]);

        Assert.Equal(1, result.StockConflicts);
        Assert.Equal(-3m, (await database.Context.Batches.SingleAsync(item => item.Id == batchId)).Quantity);
        var conflict = await database.Context.SyncConflicts.SingleAsync(item => item.EntityName == "StockConflict");
        Assert.Equal("Open", conflict.Resolution);
        Assert.Contains(database.Context.DeviceId.ToString(), conflict.RemotePayload);
        Assert.Contains(RemoteDeviceId.ToString(), conflict.RemotePayload);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            applier.ResolveConflictAsync(conflict.Id, acceptRemote: false, "Keep local", Guid.NewGuid()));

        var adjustment = CreateEnvelope(serializer, new StockMovement
        {
            BatchId = batchId,
            DrugId = drugId,
            QuantityChange = 4,
            MovementType = "Adjustment",
            ReferenceType = nameof(StockAdjustment),
            Notes = "Physical count verified"
        }, stockPhysicalTime + 1, 0);
        await applier.ApplyAsync([adjustment]);

        Assert.Equal(1m, (await database.Context.Batches.SingleAsync(item => item.Id == batchId)).Quantity);
        Assert.StartsWith(
            "Resolved by stock adjustment: Physical count verified",
            (await database.Context.SyncConflicts.SingleAsync(item => item.Id == conflict.Id)).Resolution);
    }

    [Fact]
    public async Task ChangeApplier_RollsBackWholeBatchWhenAReferencedBatchIsMissing()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var serializer = new SyncPayloadSerializer();
        var applier = CreateApplier(database.Context, Guid.NewGuid(), serializer);
        var drugId = Guid.NewGuid();
        var validDrug = CreateEnvelope(serializer, new Drug { Id = drugId, Name = "Must roll back" }, 1_770_000_000_000, 1);
        var invalidMovement = CreateEnvelope(serializer, new StockMovement
        {
            DrugId = drugId,
            BatchId = Guid.NewGuid(),
            QuantityChange = 1,
            MovementType = "Purchase"
        }, 1_770_000_000_000, 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => applier.ApplyAsync([validDrug, invalidMovement]));

        Assert.False(await database.Context.Drugs.AsNoTracking().AnyAsync(item => item.Id == drugId));
        Assert.False(await database.Context.ChangeLogs.AsNoTracking().AnyAsync(item => item.Id == validDrug.ChangeId));
    }

    [Fact]
    public void PayloadSerializer_UsesPaiseEnumsAndOmitsLocalSyncStateAndBatchCache()
    {
        var serializer = new SyncPayloadSerializer();
        var batch = new Batch
        {
            BatchNo = "B-1",
            Quantity = 50,
            Mrp = 12.34m,
            SyncState = SyncState.Pending
        };

        using var document = JsonDocument.Parse(serializer.SerializeEntity(batch));
        var root = document.RootElement;

        Assert.Equal(1234, root.GetProperty("mrp").GetInt64());
        Assert.False(root.TryGetProperty("quantity", out _));
        Assert.False(root.TryGetProperty("syncState", out _));
        Assert.Equal("Pending", JsonSerializer.Serialize(SyncState.Pending, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }).Trim('"'));

        var invoiceItem = new WholesaleInvoiceItem
        {
            TaxableAmount = 123.45m,
            UnitPrice = 12.34m,
            TaxRate = 5m,
            Quantity = 3
        };
        using var invoiceItemDocument = JsonDocument.Parse(serializer.SerializeEntity(invoiceItem));
        Assert.Equal(12345, invoiceItemDocument.RootElement.GetProperty("taxableAmount").GetInt64());
        Assert.Equal(1234, invoiceItemDocument.RootElement.GetProperty("unitPrice").GetInt64());
        Assert.Equal(5, invoiceItemDocument.RootElement.GetProperty("taxRate").GetDecimal());
        Assert.Equal(3, invoiceItemDocument.RootElement.GetProperty("quantity").GetDecimal());

        var user = new AppUser { UserName = "owner", Role = UserRole.Owner, PasswordHash = "not-for-sync" };
        using var userDocument = JsonDocument.Parse(serializer.SerializeEntity(user));
        Assert.Equal("Owner", userDocument.RootElement.GetProperty("role").GetString());
        Assert.False(userDocument.RootElement.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task SyncPackage_ExportsSignedChangesAndImportsThemOnPairedDevice()
    {
        await using var source = await DatabaseTestContext.CreateAsync();
        await using var target = await DatabaseTestContext.CreateAsync();
        var exchangeRoot = Path.Combine(Path.GetTempPath(), $"PharmaBill.SyncTests.{Guid.NewGuid():N}");
        var sourceStorage = new DatabaseStorageOptions(Path.Combine(exchangeRoot, "source"));
        var targetStorage = new DatabaseStorageOptions(Path.Combine(exchangeRoot, "target"));
        var sourceKeyStore = new ProtectedPeerKeyStore(sourceStorage);
        var targetKeyStore = new ProtectedPeerKeyStore(targetStorage);
        var sourceId = source.Context.DeviceId;
        var targetId = target.Context.DeviceId;
        var sourceDevices = new SyncDeviceService(source.Context, new DatabaseDeviceId(sourceId), sourceKeyStore);
        var targetDevices = new SyncDeviceService(target.Context, new DatabaseDeviceId(targetId), targetKeyStore);
        var pairingCode = sourceDevices.CreatePairingCode().Code;
        await sourceDevices.InitializeLocalDeviceAsync("Source");
        await targetDevices.InitializeLocalDeviceAsync("Target");
        await sourceDevices.PairDeviceAsync(targetId, "Target", "Windows", pairingCode);
        await targetDevices.PairDeviceAsync(sourceId, "Source", "Windows", pairingCode);

        Directory.CreateDirectory(exchangeRoot);
        var invoiceImagePath = Path.Combine(exchangeRoot, "purchase-invoice.pdf");
        await File.WriteAllBytesAsync(invoiceImagePath, [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]);
        var drug = new Drug { Name = "USB exchange test", Mrp = 45.67m };
        source.Context.Drugs.Add(drug);
        source.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            SupplierId = Guid.NewGuid(),
            InvoiceNo = "SYNC-TEST-1",
            InvoiceDate = new DateOnly(2026, 1, 1),
            Notes = $"DocumentPath={invoiceImagePath}"
        });
        await source.Context.SaveChangesAsync();

        var serializer = new SyncPayloadSerializer();
        var sourceClock = new HybridLogicalClock(new HybridLogicalClockState(), new DatabaseDeviceId(sourceId));
        var targetClock = new HybridLogicalClock(new HybridLogicalClockState(), new DatabaseDeviceId(targetId));
        var sourceApplier = new ChangeApplier(source.Context, sourceClock, serializer);
        var targetApplier = new ChangeApplier(target.Context, targetClock, serializer);
        var sourceFiles = new FileTransferQueue(sourceStorage, new DatabaseDeviceId(sourceId));
        var targetFiles = new FileTransferQueue(targetStorage, new DatabaseDeviceId(targetId));
        var sourcePackages = new SyncPackageService(
            source.Context, new DatabaseDeviceId(sourceId), sourceDevices, sourceFiles,
            serializer, sourceApplier, sourceStorage);
        var targetPackages = new SyncPackageService(
            target.Context, new DatabaseDeviceId(targetId), targetDevices, targetFiles,
            serializer, targetApplier, targetStorage);
        var packagePath = Path.Combine(exchangeRoot, "exchange.pbsync");

        try
        {
            Assert.Equal(2, await sourcePackages.ExportAsync(targetId, packagePath));
            var imported = await targetPackages.ImportAsync(packagePath);

            Assert.Equal(2, imported.Applied);
            Assert.Equal(drug.Id, (await target.Context.Drugs.SingleAsync()).Id);
            Assert.Equal(4567, Money.ToPaise((await target.Context.Drugs.SingleAsync()).Mrp!.Value));
            var importedInvoice = await target.Context.PurchaseInvoices.SingleAsync();
            var restoredNotes = Assert.IsType<string>(importedInvoice.Notes);
            Assert.StartsWith("DocumentPath=", restoredNotes);
            Assert.True(File.Exists(restoredNotes["DocumentPath=".Length..]));

            var tamperedPath = Path.Combine(exchangeRoot, "tampered.pbsync");
            var tamperedPackage = await File.ReadAllBytesAsync(packagePath);
            tamperedPackage[^1] ^= 0x40;
            await File.WriteAllBytesAsync(tamperedPath, tamperedPackage);

            await Assert.ThrowsAsync<CryptographicException>(() => targetPackages.ImportAsync(tamperedPath));
            Assert.Single(await target.Context.Drugs.ToListAsync());

            await sourceDevices.RevokeDeviceAsync(targetId);
            Assert.False(sourceDevices.IsPaired(targetId));
            Assert.True((await source.Context.DeviceInfos.IgnoreQueryFilters()
                .SingleAsync(item => item.Id == targetId)).IsDeleted);
        }
        finally
        {
            if (Directory.Exists(exchangeRoot))
            {
                Directory.Delete(exchangeRoot, recursive: true);
            }
        }
    }

    private static ChangeApplier CreateApplier(
        PharmaBillDbContext context,
        Guid localDeviceId,
        SyncPayloadSerializer serializer) =>
        new(context, new HybridLogicalClock(new HybridLogicalClockState(), new DatabaseDeviceId(localDeviceId)), serializer);

    private static SyncChangeEnvelope CreateEnvelope(
        SyncPayloadSerializer serializer,
        EntityBase entity,
        long physicalMilliseconds,
        long counter)
    {
        var stamp = new HlcValue(physicalMilliseconds, counter, RemoteDeviceId).ToString();
        entity.DeviceId = RemoteDeviceId;
        entity.HlcStamp = stamp;
        using var payload = JsonDocument.Parse(serializer.SerializeEntity(entity));
        return new SyncChangeEnvelope(
            Guid.NewGuid(),
            entity.GetType().Name,
            entity.Id,
            "Upsert",
            1,
            stamp,
            RemoteDeviceId,
            DateTimeOffset.FromUnixTimeMilliseconds(physicalMilliseconds).UtcDateTime,
            payload.RootElement.Clone());
    }

    private static JsonDocument ReadVector(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", name)));
}
