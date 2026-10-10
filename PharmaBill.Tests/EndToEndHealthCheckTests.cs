using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class EndToEndHealthCheckTests
{
	[Fact]
	public async Task Seed_CreatesSuppliersPatientsBuyersAndTenDrugs()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);

		Assert.Equal(3, catalog.Suppliers.Length);
		Assert.Equal(3, catalog.RetailPatients.Length);
		Assert.All(catalog.Suppliers, s => Assert.False(string.IsNullOrWhiteSpace(s.Gstin)));
		Assert.All(catalog.Suppliers, s => Assert.Contains("20B", s.DrugLicenceNumber!, StringComparison.Ordinal));
		Assert.Equal(10, catalog.AllSellableBatches.Count);
		Assert.True(catalog.MorphineNdps.IsBanned);
		Assert.Equal(0m, catalog.ZeroMrpBatchA.Mrp);
		Assert.Equal(0m, catalog.ZeroMrpBatchB.Mrp);
	}

	[Fact]
	public async Task RetailBilling_ParacetamolSale_DeductsStockAndPrintsThermalPayload()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		var patient = catalog.RetailPatients[1];

		var before = await StockOnHandAsync(database, catalog.ParacetamolBatch.Id);
		var result = await retail.SaveSaleAsync(
			new SaveRetailSaleInput(
				patient.Name,
				patient.Phone!,
				patient.Address,
				null,
				null,
				null,
				[new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 2m, 0m, 30m)],
				[new RetailPaymentInput("Cash", 60m)],
				false,
				MrdNumber: "MRD-9042"),
			catalog.Admin.Id,
			UserRole.Pharmacist);

		var after = await StockOnHandAsync(database, catalog.ParacetamolBatch.Id);
		Assert.Equal(before - 2m, after);
		Assert.False(string.IsNullOrWhiteSpace(result.Sale.InvoiceNo));
		Assert.Equal(1, await database.Context.Sales.CountAsync());

		var printData = await retail.GetPrintableBillAsync(result.Sale.Id);
		var thermal = ThermalReceiptFormatter.Format(printData, new DocumentOutputSettings());
		Assert.Contains(result.Sale.InvoiceNo, thermal);
		Assert.Contains("60.00", thermal);
		Assert.DoesNotContain("Exception", thermal, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task RetailBilling_ZeroMrpBatch_RequiresMrpBeforeSave()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		var inventory = new InventoryService(database.Context, new UnitOfWork(database.Context), new HealthEntitlements());

		var input = new SaveRetailSaleInput(
			"Walk-in Cash",
			"9000000001",
			null,
			null,
			null,
			null,
			[new RetailSaleLineInput(catalog.ZeroMrpA.Id, catalog.ZeroMrpBatchA.Id, 1m, 0m, 15m)],
			[],
			false);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			retail.SaveSaleAsync(input, catalog.Admin.Id, UserRole.Owner));

		await inventory.UpdateBatchMrpAsync(catalog.ZeroMrpBatchA.Id, 15m);
		var saved = await retail.SaveSaleAsync(input, catalog.Admin.Id, UserRole.Owner);
		Assert.True(saved.TotalAmount > 0m);
	}

	[Fact]
	public async Task RetailBilling_H1Alprazolam_RequiresPrescriptionAndRegisters()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var storage = Path.Combine(Path.GetTempPath(), $"PharmaBill.E2E.Rx.{Guid.NewGuid():N}");
		Directory.CreateDirectory(storage);
		var rxFile = Path.Combine(storage, "dummy-rx.jpg");
		await File.WriteAllBytesAsync(rxFile, [0xFF, 0xD8, 0xFF, 0x00]);
		var retail = database.CreateRetailBillingService(new HealthEntitlements(), Path.Combine(storage, "archive"));

		try
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				retail.SaveSaleAsync(
					new SaveRetailSaleInput(
						catalog.RetailPatients[2].Name,
						catalog.RetailPatients[2].Phone!,
						"SKIMS Ward 3",
						"Dr Registrar",
						"JK-MCI-001",
						null,
						[new RetailSaleLineInput(catalog.Alprazolam025.Id, catalog.AlprazolamBatch.Id, 1m, 0m, 45m)],
						[],
						false,
						MrdNumber: "MRD-9042"),
					catalog.Admin.Id,
					UserRole.Pharmacist));

			var saved = await retail.SaveSaleAsync(
				new SaveRetailSaleInput(
					catalog.RetailPatients[2].Name,
					catalog.RetailPatients[2].Phone!,
					"SKIMS Ward 3",
					"Dr Registrar",
					"JK-MCI-001",
					rxFile,
					[new RetailSaleLineInput(catalog.Alprazolam025.Id, catalog.AlprazolamBatch.Id, 1m, 0m, 45m)],
					[],
					false,
					MrdNumber: "MRD-9042"),
				catalog.Admin.Id,
				UserRole.Pharmacist);

			var prescription = await database.Context.Prescriptions.SingleAsync(p => p.Id == saved.Sale.PrescriptionId);
			Assert.Contains("DocumentPath=", prescription.Notes);
			Assert.Contains(await database.Context.ScheduleRegisterEntries.ToListAsync(),
				e => e.SaleId == saved.Sale.Id && e.RegisterType.Contains("H1", StringComparison.OrdinalIgnoreCase));

			using var provider = new ServiceCollection()
				.AddSingleton<IUnitOfWork>(new UnitOfWork(database.Context))
				.BuildServiceProvider();
			var register = new StatutoryRegisterService(provider.GetRequiredService<IServiceScopeFactory>());
			var rows = await register.SearchAsync("H1", catalog.Admin.Id);
			Assert.Contains(rows, r => r.DrugName.Contains("Alprazolam", StringComparison.OrdinalIgnoreCase));
			Assert.False(string.IsNullOrWhiteSpace(rows[0].DoctorRegistrationNo));
		}
		finally
		{
			Directory.Delete(storage, recursive: true);
		}
	}

	[Fact]
	public async Task RetailBilling_NdpsMorphine_IsFlaggedBannedInSearch()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());

		var matches = await retail.SearchStockAsync("Morphine");
		var choice = Assert.Single(matches);
		Assert.True(choice.IsBanned);
		Assert.Equal("NDPS", choice.Schedule);
	}

	[Fact]
	public async Task WholesaleDashboard_LoadsKpisWithoutNullReference()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var wholesale = database.CreateWholesaleInvoiceService(new HealthEntitlements());
		await wholesale.SaveAsync(
			new SaveWholesaleInvoiceInput(
				catalog.BurhanMedicalHall.Id,
				[
					new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 5m, 0m, 28m),
					new WholesaleInvoiceLineInput(catalog.Amoxicillin500.Id, catalog.AmoxicillinBatch.Id, 2m, 0m, 80m)
				],
				0m,
				"Credit",
				null,
				false,
				90,
				false,
				null,
				"JK Transport",
				"JK01HC1234",
				null,
				null,
				"B2B health check"),
			catalog.Admin.Id,
			UserRole.Owner);

		var dashboard = database.CreateWholesaleDashboardService();
		var snapshot = await dashboard.GetSnapshotAsync(DateTime.Now);

		Assert.NotNull(snapshot);
		Assert.True(snapshot.TodayTurnover >= 0m);
		Assert.True(snapshot.OutstandingReceivables >= 0m);
		Assert.True(snapshot.PipelineReceivedCount >= 0);
		Assert.True(snapshot.Ageing0To30 >= 0m);
	}

	[Fact]
	public async Task WholesaleBilling_EInvoiceAndEWayJson_AreSchemaCompliant()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var wholesale = database.CreateWholesaleInvoiceService(new HealthEntitlements());
		var posted = await wholesale.SaveAsync(
			new SaveWholesaleInvoiceInput(
				catalog.ValleyHospitalPharmacy.Id,
				[new WholesaleInvoiceLineInput(catalog.Omeprazole20.Id, catalog.OmeprazoleBatch.Id, 10m, 1m, 52m)],
				0m,
				"Credit",
				null,
				false,
				90,
				false,
				null,
				"Carrier",
				"JK09XY9999",
				null,
				null,
				null),
			catalog.Admin.Id,
			UserRole.Owner);

		var gst = database.CreateGstService();
		var eInvoice = await gst.BuildEInvoiceJsonAsync(posted.Invoice.Id);
		using var einvDoc = JsonDocument.Parse(eInvoice.Json);
		Assert.Equal("1.03", einvDoc.RootElement.GetProperty("Version").GetString());
		Assert.Equal("INV", einvDoc.RootElement.GetProperty("DocDtls").GetProperty("Typ").GetString());
		Assert.True(einvDoc.RootElement.GetProperty("ItemList").GetArrayLength() > 0);

		var eWay = await gst.BuildEWayBillJsonAsync(posted.Invoice.Id, "29TRANS1234", "JK09XY9999", 42m);
		using var ewayDoc = JsonDocument.Parse(eWay.Json);
		Assert.True(ewayDoc.RootElement.TryGetProperty("billLists", out _));
	}

	[Fact]
	public async Task Purchases_CommitInward_WithCorrectedTotals_UpdatesStock()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var purchases = database.CreatePurchaseService(new HealthEntitlements());
		var supplier = catalog.Suppliers[0];
		var drug = catalog.Cetirizine10;
		var before = await StockOnHandAsync(database, catalog.CetirizineBatch.Id);

		var line = new PurchaseLineInput(
			drug.Id,
			"NEW-INWARD-1",
			DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
			10m,
			0m,
			25m,
			20m,
			12m,
			0m,
			200m,
			"R1");
		decimal subtotal = 200m;
		decimal tax = 24m;
		decimal grand = 224m;

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			purchases.SavePurchaseAsync(
				new SavePurchaseInput(supplier.Id, "DIST-INV-MISMATCH", DateOnly.FromDateTime(DateTime.Today), subtotal, 0m, tax, grand + 50m, [line]),
				catalog.Admin.Id,
				UserRole.Owner));

		await purchases.SavePurchaseAsync(
			new SavePurchaseInput(supplier.Id, "DIST-INV-OK", DateOnly.FromDateTime(DateTime.Today), subtotal, 0m, tax, grand, [line]),
			catalog.Admin.Id,
			UserRole.Owner);

		var batches = await database.Context.Batches
			.Where(b => b.DrugId == drug.Id && b.BatchNo == "NEW-INWARD-1")
			.ToListAsync();
		Assert.Single(batches);
		var newBatch = batches[0];
		Assert.Equal(10m, await StockOnHandAsync(database, newBatch.Id));
		Assert.True(await StockOnHandAsync(database, catalog.CetirizineBatch.Id) >= before);
	}

	[Fact]
	public void PurchasePaging_UsesTwentyRowsPerPageAndDateFilter()
	{
		const int pageSize = PurchasePageViewModel.PurchaseLinesPerPage;
		Assert.Equal(20, pageSize);

		var lines = Enumerable.Range(1, 45)
			.Select(i =>
			{
				var line = new PurchaseLineDraft(null) { InwardDate = new DateTime(2026, 3, i <= 30 ? 1 : 2, 1, 0, 0, DateTimeKind.Local) };
				return line;
			})
			.ToList();

		var page1 = Page(lines, pageSize, page: 1, from: new DateTime(2026, 3, 1), to: new DateTime(2026, 3, 31));
		Assert.Equal(20, page1.Count);
		var page2 = Page(lines, pageSize, page: 2, from: new DateTime(2026, 3, 1), to: new DateTime(2026, 3, 31));
		Assert.Equal(20, page2.Count);
		var page3 = Page(lines, pageSize, page: 3, from: new DateTime(2026, 3, 1), to: new DateTime(2026, 3, 31));
		Assert.Equal(5, page3.Count);

		var febOnly = Page(lines, pageSize, page: 1, from: new DateTime(2026, 2, 1), to: new DateTime(2026, 2, 28));
		Assert.Empty(febOnly);
	}

	[Fact]
	public async Task Stock_MrpUpdatePersistsAndMissingMrpFilterWorks()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var inventory = new InventoryService(database.Context, new UnitOfWork(database.Context), new HealthEntitlements());

		await inventory.UpdateBatchMrpAsync(catalog.ZeroMrpBatchB.Id, 22m);
		var batch = await database.Context.Batches.SingleAsync(b => b.Id == catalog.ZeroMrpBatchB.Id);
		Assert.Equal(22m, batch.Mrp);

		var allStock = await inventory.GetStockAsync(status: null);
		var missing = allStock.Where(row => row.Batches.Any(b => b.Mrp <= 0m)).ToList();
		Assert.Contains(missing, row => row.DrugId == catalog.ZeroMrpA.Id);
		Assert.DoesNotContain(missing, row => row.DrugId == catalog.ZeroMrpB.Id);

		var shortage = await inventory.GetStockAsync(status: "Shortage");
		Assert.NotNull(shortage);
	}

	[Fact]
	public async Task RecordLock_OldBillRequiresUnlockGrantAndSha256AuditHash()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		var saved = await retail.SaveSaleAsync(
			new SaveRetailSaleInput(
				"Walk-in Cash",
				"9000000001",
				null,
				null,
				null,
				null,
				[new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)],
				[new RetailPaymentInput("Cash", 30m)],
				false),
			catalog.Admin.Id,
			UserRole.Owner);

		var sale = await database.Context.Sales.SingleAsync(s => s.Id == saved.Sale.Id);
		sale.SaleAtUtc = DateTime.UtcNow.AddDays(-10);
		await database.Context.SaveChangesAsync();

		await Assert.ThrowsAsync<RecordLockedException>(() =>
			retail.UnlockSaleAsync(saved.Sale.Id, catalog.Admin.Id, UserRole.Owner, "Audit correction"));

		var grant = new RecordUnlockGrant
		{
			AdminUserId = catalog.Admin.Id,
			AdminUserName = catalog.Admin.DisplayName,
			AdminRole = UserRole.Owner,
			Reason = "Statutory audit unlock",
			EntityType = "RetailBill",
			EntityId = saved.Sale.Id,
			Action = "MODIFIED_AFTER_LOCK"
		};
		using (RecordUnlockContext.Use(grant))
		{
			await retail.UnlockSaleAsync(saved.Sale.Id, catalog.Admin.Id, UserRole.Owner, grant.Reason);
		}

		await database.Context.SaveChangesAsync();
		var hashLog = await database.Context.AuditLogs
			.OrderByDescending(a => a.ActionAtUtc)
			.FirstAsync(a => a.Action == "MODIFIED_AFTER_LOCK");
		Assert.Equal(64, hashLog.RecordHash?.Length);
		Assert.Matches("^[0-9A-F]+$", hashLog.RecordHash!);
		Assert.Equal("Statutory audit unlock", hashLog.Reason);
	}

	[Fact]
	public async Task StatutoryRegister_ExportPdfAndExcel_WritesWithoutLockErrors()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		_ = await E2EHealthSeed.SeedAsync(database.Context);
		var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.E2E.Export.{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
		var export = new TabularExportService();
		var rows = new IReadOnlyList<string>[] { new[] { "2026-03-09", "Alprazolam", "ALP-025", "1", "Dr Registrar" } };
		try
		{
			var pdf = Path.Combine(directory, "register.pdf");
			var xlsx = Path.Combine(directory, "register.xlsx");
			await export.ExportAsync(pdf, "Schedule H1 Register", ["Date", "Drug", "Batch", "Qty", "Doctor"], rows);
			await export.ExportAsync(xlsx, "Schedule H1 Register", ["Date", "Drug", "Batch", "Qty", "Doctor"], rows);
			Assert.True(new FileInfo(pdf).Length > 100);
			Assert.True(new FileInfo(xlsx).Length > 100);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public void DualHotkeyCommands_ExistOnMainWindowViewModel()
	{
		string[] expected =
		[
			nameof(MainWindowViewModel.ShortcutNewBillCommand),
			nameof(MainWindowViewModel.ShortcutSaveAndPrintCommand),
			nameof(MainWindowViewModel.ShortcutFocusMedicineCommand),
			nameof(MainWindowViewModel.ShortcutHoldBillCommand),
			nameof(MainWindowViewModel.ShortcutShowPaymentQrCommand),
			nameof(MainWindowViewModel.ShortcutOpenCalculatorCommand),
			nameof(MainWindowViewModel.ShortcutCancelCommand)
		];

		foreach (string name in expected)
		{
			var property = typeof(MainWindowViewModel).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(property);
			Assert.True(typeof(System.Windows.Input.ICommand).IsAssignableFrom(property!.PropertyType));
		}
	}

	private static async Task<decimal> StockOnHandAsync(DatabaseTestContext database, Guid batchId) =>
		await database.Context.StockMovements
			.Where(m => m.BatchId == batchId)
			.SumAsync(m => m.QuantityChange);

	private static List<PurchaseLineDraft> Page(
		IReadOnlyList<PurchaseLineDraft> lines,
		int pageSize,
		int page,
		DateTime? from,
		DateTime? to)
	{
		IEnumerable<PurchaseLineDraft> filtered = lines;
		if (from.HasValue)
		{
			filtered = filtered.Where(l => l.InwardDate.Date >= from.Value.Date);
		}

		if (to.HasValue)
		{
			filtered = filtered.Where(l => l.InwardDate.Date <= to.Value.Date);
		}

		return filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
	}

	private sealed class HealthEntitlements : IEntitlementService
	{
		public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(EntitlementStatus.TRIAL);

		public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);
	}
}
