using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Tests;

/// <summary>
/// Realistic dummy dataset for module health checks (isolated SQLite per test run).
/// </summary>
internal static class E2EHealthSeed
{
	public sealed record Catalog(
		PharmacyProfile Profile,
		AppUser Admin,
		Supplier[] Suppliers,
		Patient[] RetailPatients,
		Customer BurhanMedicalHall,
		Customer ValleyHospitalPharmacy,
		Drug Paracetamol650,
		Batch ParacetamolBatch,
		Drug Amoxicillin500,
		Batch AmoxicillinBatch,
		Drug Alprazolam025,
		Batch AlprazolamBatch,
		Drug MorphineNdps,
		Batch MorphineBatch,
		Drug ZeroMrpA,
		Batch ZeroMrpBatchA,
		Drug ZeroMrpB,
		Batch ZeroMrpBatchB,
		Drug Cetirizine10,
		Batch CetirizineBatch,
		Drug Omeprazole20,
		Batch OmeprazoleBatch,
		Drug Metformin500,
		Batch MetforminBatch,
		Drug Azithromycin500,
		Batch AzithromycinBatch)
	{
		public IReadOnlyList<(Drug Drug, Batch Batch)> AllSellableBatches { get; init; } = [];
	}

	public static async Task<Catalog> SeedAsync(PharmaBillDbContext context)
	{
		var profile = new PharmacyProfile
		{
			Name = "PharmaBill Health Check Pharmacy",
			LegalName = "PharmaBill Health Check Pharmacy Pvt Ltd",
			Address = "12 Hospital Road, Srinagar",
			Phone = "9900000001",
			Gstin = "01ABCDE1234F1Z5",
			State = "Jammu and Kashmir",
			BusinessMode = BusinessMode.Both,
			InvoicePrefix = "HC",
			WholesaleBuyerLicenceRulesJson = JsonSerializer.Serialize(
				new Dictionary<string, string[]> { ["Distributor"] = ["W1"], ["Hospital"] = ["W1"] })
		};
		var admin = new AppUser
		{
			UserName = "health-admin",
			DisplayName = "Master Admin",
			PasswordHash = "test-hash",
			Role = UserRole.Owner
		};

		var suppliers = new[]
		{
			new Supplier
			{
				Name = "Abbott Healthcare",
				Gstin = "01AAAAA1111A1Z5",
				DrugLicenceNumber = "DL 20B/21B-AHB-001",
				Phone = "9900000101",
				Address = "Industrial Estate, Baddi"
			},
			new Supplier
			{
				Name = "Cipla Dist.",
				Gstin = "01BBBBB2222B1Z5",
				DrugLicenceNumber = "DL 20B/21B-CIP-002",
				Phone = "9900000102"
			},
			new Supplier
			{
				Name = "Sun Pharma Logistics",
				Gstin = "01CCCCC3333C1Z5",
				DrugLicenceNumber = "DL 20B/21B-SUN-003",
				Phone = "9900000103"
			}
		};

		var retailPatients = new[]
		{
			new Patient { Name = "Walk-in Cash", Phone = "9000000001" },
			new Patient { Name = "Mohammad Altaf (Chronic Diabetes)", Phone = "9000000002", Address = "14 Dal Gate" },
			new Patient
			{
				Name = "Hospital IPD Bed 12 - MRD-9042",
				Phone = "9000000003",
				Address = "SKIMS Ward 3"
			}
		};

		var burhan = new Customer
		{
			Name = "Burhan Medical Hall",
			Gstin = "01DDDDD4444D1Z5",
			BuyerType = "Distributor",
			State = "Jammu and Kashmir",
			Phone = "9900000201",
			Address = "Main Market, Sopore",
			CreditLimit = 500_000m,
			CreditDays = 30
		};
		var valley = new Customer
		{
			Name = "Valley Hospital Pharmacy",
			Gstin = "01EEEEE5555E1Z5",
			BuyerType = "Hospital",
			State = "Jammu and Kashmir",
			Phone = "9900000202",
			Address = "SMHS Hospital Block",
			CreditLimit = 1_000_000m,
			CreditDays = 45
		};

		var paracetamol = Drug("Paracetamol 650mg", schedule: null, gst: 12m, hsn: "3004");
		var paracetamolBatch = Batch(paracetamol, "PCM-650-A1", mrp: 30m, qty: 200m, suppliers[0].Id);

		var amoxicillin = Drug("Amoxicillin 500mg", "H", 5m, "3004");
		var amoxicillinBatch = Batch(amoxicillin, "AMX-500-B2", 85m, 120m, suppliers[1].Id);

		var alprazolam = Drug("Alprazolam 0.25mg", "H1", 12m, "3004");
		var alprazolamBatch = Batch(alprazolam, "ALP-025-C3", 45m, 40m, suppliers[2].Id);

		var morphine = Drug("Morphine Sulphate 10mg", "NDPS", 5m, "3004");
		morphine.IsBanned = true;
		var morphineBatch = Batch(morphine, "MOR-10-X1", 120m, 5m, suppliers[0].Id);

		var zeroA = Drug("Inward Zero-MRP Tablet A", null, 12m, "3004");
		var zeroBatchA = Batch(zeroA, "ZMRP-A1", 0m, 50m, suppliers[1].Id);
		var zeroB = Drug("Inward Zero-MRP Capsule B", null, 5m, "3004");
		var zeroBatchB = Batch(zeroB, "ZMRP-B1", 0m, 30m, suppliers[2].Id);

		var cetirizine = Drug("Cetirizine 10mg", null, 12m, "3004");
		var cetirizineBatch = Batch(cetirizine, "CET-10-D4", 25m, 80m, suppliers[0].Id);
		var omeprazole = Drug("Omeprazole 20mg", null, 12m, "3004");
		var omeprazoleBatch = Batch(omeprazole, "OME-20-E5", 55m, 60m, suppliers[1].Id);
		var metformin = Drug("Metformin 500mg", "H", 5m, "3004");
		var metforminBatch = Batch(metformin, "MET-500-F6", 18m, 150m, suppliers[2].Id);
		var azithromycin = Drug("Azithromycin 500mg", "H", 12m, "3004");
		var azithromycinBatch = Batch(azithromycin, "AZI-500-G7", 110m, 45m, suppliers[0].Id);

		context.AddRange(profile, admin);
		context.AddRange(suppliers);
		context.AddRange(retailPatients);
		context.AddRange(burhan, valley);
		context.AddRange(
			paracetamol, paracetamolBatch,
			amoxicillin, amoxicillinBatch,
			alprazolam, alprazolamBatch,
			morphine, morphineBatch,
			zeroA, zeroBatchA,
			zeroB, zeroBatchB,
			cetirizine, cetirizineBatch,
			omeprazole, omeprazoleBatch,
			metformin, metforminBatch,
			azithromycin, azithromycinBatch);

		context.CatalogInfos.Add(new CatalogInfo
		{
			NameKey = alprazolam.Name.ToLowerInvariant(),
			IsHabitForming = true,
			RegisterType = "H1"
		});

		foreach (var (drug, batch, stock) in new (Drug, Batch, decimal)[]
		         {
			         (paracetamol, paracetamolBatch, 200m),
			         (amoxicillin, amoxicillinBatch, 120m),
			         (alprazolam, alprazolamBatch, 40m),
			         (morphine, morphineBatch, 5m),
			         (zeroA, zeroBatchA, 50m),
			         (zeroB, zeroBatchB, 30m),
			         (cetirizine, cetirizineBatch, 80m),
			         (omeprazole, omeprazoleBatch, 60m),
			         (metformin, metforminBatch, 150m),
			         (azithromycin, azithromycinBatch, 45m)
		         })
		{
			context.StockMovements.Add(new StockMovement
			{
				DrugId = drug.Id,
				BatchId = batch.Id,
				QuantityChange = stock,
				MovementType = "PurchaseReceipt"
			});
		}

		context.CustomerLicences.AddRange(
			new CustomerLicence
			{
				CustomerId = burhan.Id,
				LicenceType = "W1",
				LicenceNumber = "JK/W1/BMH-2024",
				IssuedOn = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
				ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
				Authorisation = "General wholesale"
			},
			new CustomerLicence
			{
				CustomerId = valley.Id,
				LicenceType = "W1",
				LicenceNumber = "JK/W1/VHP-2024",
				IssuedOn = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
				ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
				Authorisation = "Hospital supply"
			});

		await context.SaveChangesAsync();

		var all = new List<(Drug, Batch)>
		{
			(paracetamol, paracetamolBatch),
			(amoxicillin, amoxicillinBatch),
			(alprazolam, alprazolamBatch),
			(morphine, morphineBatch),
			(zeroA, zeroBatchA),
			(zeroB, zeroBatchB),
			(cetirizine, cetirizineBatch),
			(omeprazole, omeprazoleBatch),
			(metformin, metforminBatch),
			(azithromycin, azithromycinBatch)
		};

		return new Catalog(
			profile,
			admin,
			suppliers,
			retailPatients,
			burhan,
			valley,
			paracetamol,
			paracetamolBatch,
			amoxicillin,
			amoxicillinBatch,
			alprazolam,
			alprazolamBatch,
			morphine,
			morphineBatch,
			zeroA,
			zeroBatchA,
			zeroB,
			zeroBatchB,
			cetirizine,
			cetirizineBatch,
			omeprazole,
			omeprazoleBatch,
			metformin,
			metforminBatch,
			azithromycin,
			azithromycinBatch)
		{
			AllSellableBatches = all
		};
	}

	private static Drug Drug(string name, string? schedule, decimal gst, string hsn) =>
		new()
		{
			Name = name,
			Schedule = schedule,
			GstRate = gst,
			HsnCode = hsn,
			Mrp = 100m,
			SalePrice = 95m
		};

	private static Batch Batch(Drug drug, string batchNo, decimal mrp, decimal qty, Guid supplierId) =>
		new()
		{
			DrugId = drug.Id,
			BatchNo = batchNo,
			ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(9)),
			Mrp = mrp,
			SalePrice = mrp > 0m ? mrp - 2m : null,
			PurchasePrice = mrp > 0m ? mrp * 0.6m : 10m,
			Quantity = qty,
			SupplierId = supplierId
		};
}
