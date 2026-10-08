using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class AddStockService(IUnitOfWork unitOfWork, PurchaseService purchaseService, IEntitlementService entitlementService, ILicenseRuntimeGuard licenseRuntimeGuard)
{
	public static readonly IReadOnlyList<string> Schedules = new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "OTC", "G", "H", "H1", "X", "NDPS" });

	public async Task<Supplier> AddSupplierAsync(string name, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureAllowed(role);
		ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
		string trimmed = name.Trim();
		Supplier supplier = await unitOfWork.Context.Suppliers.FirstOrDefaultAsync((Supplier supplier3) => supplier3.Name.ToLower() == trimmed.ToLower(), cancellationToken);
		if (supplier != null)
		{
			return supplier;
		}
		Supplier supplier2 = new Supplier
		{
			Name = trimmed
		};
		unitOfWork.Context.Suppliers.Add(supplier2);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "SupplierCreated",
			EntityName = "Supplier",
			EntityId = supplier2.Id,
			Details = trimmed
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return supplier2;
	}

	public async Task<string?> SuggestScheduleAsync(Guid? drugId, Guid? catalogMedicineId, string medicineName, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		Guid? guid = drugId ?? (await FindDrugIdAsync(catalogMedicineId, cancellationToken));
		Guid? resolvedDrugId = guid;
		if (resolvedDrugId.HasValue)
		{
			DateTime today = DateTime.UtcNow;
			string text = await (from item in context.ScheduleOverrides.AsNoTracking()
				where item.DrugId == resolvedDrugId && (item.EffectiveFromUtc == null || item.EffectiveFromUtc <= today) && (item.EffectiveToUtc == null || item.EffectiveToUtc >= today)
				orderby item.UpdatedAtUtc descending
				select item.Schedule).FirstOrDefaultAsync(cancellationToken);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text.Trim().ToUpperInvariant();
			}
		}
		string nameKey = medicineName.Trim().ToLowerInvariant();
		CatalogInfo catalogInfo = await (from item in context.CatalogInfos.AsNoTracking()
			where (catalogMedicineId != null && item.CatalogMedicineId == catalogMedicineId) || item.NameKey == nameKey
			select item).FirstOrDefaultAsync(cancellationToken);
		if (!string.IsNullOrWhiteSpace(catalogInfo?.Schedule))
		{
			return catalogInfo.Schedule.Trim().ToUpperInvariant();
		}
		return (catalogInfo != null && catalogInfo.RequiresPrescription) ? "H" : null;
	}

	public async Task<decimal?> SuggestMrpAsync(Guid? drugId, Guid? catalogMedicineId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		Guid? guid = drugId ?? (await FindDrugIdAsync(catalogMedicineId, cancellationToken));
		Guid? resolvedDrugId = guid;
		Guid? resolvedCatalogId = catalogMedicineId;
		decimal? mrp;
		if (resolvedDrugId.HasValue)
		{
			decimal? result = await (from batch in context.Batches.AsNoTracking()
				where batch.DrugId == resolvedDrugId && !batch.IsDeleted && batch.Mrp > (decimal?)(decimal)0
				orderby batch.CreatedAtUtc descending
				select batch.Mrp).FirstOrDefaultAsync(cancellationToken);
			if (result.HasValue)
			{
				return result;
			}
			var anon = await (from item in context.Drugs.AsNoTracking()
				where item.Id == resolvedDrugId
				select new { item.Mrp, item.CatalogMedicineId }).FirstOrDefaultAsync(cancellationToken);
			if (anon != null)
			{
				mrp = anon.Mrp;
				if ((mrp.GetValueOrDefault() > 0m) & mrp.HasValue)
				{
					return anon.Mrp;
				}
			}
			guid = resolvedCatalogId;
			if (!guid.HasValue)
			{
				resolvedCatalogId = anon?.CatalogMedicineId;
			}
		}
		if (!resolvedCatalogId.HasValue)
		{
			return null;
		}
		decimal? num = await (from item in context.CatalogMedicines.AsNoTracking()
			where item.Id == resolvedCatalogId
			select item.ReferencePrice).FirstOrDefaultAsync(cancellationToken);
		mrp = num;
		return ((mrp.GetValueOrDefault() > 0m) & mrp.HasValue) ? num : ((decimal?)null);
	}

	/// <summary>
	/// Suggests last purchase / PTR rate for a drug (purchase line, then batch purchase price / PTR).
	/// </summary>
	public async Task<decimal?> SuggestPurchaseRateAsync(Guid? drugId, Guid? catalogMedicineId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		Guid? resolvedDrugId = drugId ?? (await FindDrugIdAsync(catalogMedicineId, cancellationToken));
		if (!resolvedDrugId.HasValue)
		{
			return null;
		}

		decimal? fromPurchase = await (from item in context.PurchaseItems.AsNoTracking()
			where item.DrugId == resolvedDrugId && !item.IsDeleted && (item.UnitPrice > 0m || (item.Ptr != null && item.Ptr > 0m))
			orderby item.CreatedAtUtc descending
			select (decimal?)(item.UnitPrice > 0m ? item.UnitPrice : item.Ptr!.Value)).FirstOrDefaultAsync(cancellationToken);
		if (fromPurchase is > 0m)
		{
			return fromPurchase;
		}

		decimal? fromBatch = await (from batch in context.Batches.AsNoTracking()
			where batch.DrugId == resolvedDrugId && !batch.IsDeleted
			orderby batch.CreatedAtUtc descending
			select (decimal?)(batch.PurchasePrice > 0m
				? batch.PurchasePrice
				: (batch.Ptr != null && batch.Ptr > 0m ? batch.Ptr.Value : 0m))).FirstOrDefaultAsync(cancellationToken);
		return fromBatch is > 0m ? fromBatch : null;
	}

	/// <summary>
	/// Returns Drug Bank pack size label / dosage form for auto-fill on purchase entry.
	/// </summary>
	public async Task<(string? PackSizeLabel, string? DosageForm, decimal? ReferencePrice, decimal? GstRate, string? HsnCode)> SuggestCatalogDefaultsAsync(Guid? drugId, Guid? catalogMedicineId, CancellationToken cancellationToken = default(CancellationToken))
	{
		PharmaBillDbContext context = unitOfWork.Context;
		Guid? resolvedCatalogId = catalogMedicineId;
		if (!resolvedCatalogId.HasValue && drugId.HasValue)
		{
			resolvedCatalogId = await context.Drugs.AsNoTracking()
				.Where(d => d.Id == drugId)
				.Select(d => d.CatalogMedicineId)
				.FirstOrDefaultAsync(cancellationToken);
		}

		string? pack = null;
		string? form = null;
		decimal? refPrice = null;
		if (resolvedCatalogId.HasValue)
		{
			var catalog = await context.CatalogMedicines.AsNoTracking()
				.Where(c => c.Id == resolvedCatalogId)
				.Select(c => new { c.PackSizeLabel, c.DosageForm, c.ReferencePrice })
				.FirstOrDefaultAsync(cancellationToken);
			if (catalog != null)
			{
				pack = string.IsNullOrWhiteSpace(catalog.PackSizeLabel) ? null : catalog.PackSizeLabel.Trim();
				form = string.IsNullOrWhiteSpace(catalog.DosageForm) ? null : catalog.DosageForm.Trim();
				refPrice = catalog.ReferencePrice is > 0m ? catalog.ReferencePrice : null;
			}
		}

		decimal? gst = null;
		string? hsn = null;
		Guid? resolvedDrugId = drugId ?? (await FindDrugIdAsync(resolvedCatalogId, cancellationToken));
		if (resolvedDrugId.HasValue)
		{
			var drug = await context.Drugs.AsNoTracking()
				.Where(d => d.Id == resolvedDrugId)
				.Select(d => new { d.GstRate, d.HsnCode, d.DosageForm, d.Unit })
				.FirstOrDefaultAsync(cancellationToken);
			if (drug != null)
			{
				gst = drug.GstRate is > 0m ? drug.GstRate : null;
				hsn = string.IsNullOrWhiteSpace(drug.HsnCode) ? null : drug.HsnCode;
				if (string.IsNullOrWhiteSpace(form) && !string.IsNullOrWhiteSpace(drug.DosageForm))
				{
					form = drug.DosageForm.Trim();
				}
				if (string.IsNullOrWhiteSpace(pack) && !string.IsNullOrWhiteSpace(drug.Unit))
				{
					pack = drug.Unit.Trim();
				}
			}
		}

		return (pack, form, refPrice, gst, hsn);
	}

	public async Task<bool> BatchExistsAsync(Guid? drugId, Guid? catalogMedicineId, Guid supplierId, string batchNo, DateOnly expiryDate, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid? guid = drugId ?? (await FindDrugIdAsync(catalogMedicineId, cancellationToken));
		Guid? resolvedDrugId = guid;
		if (!resolvedDrugId.HasValue)
		{
			return false;
		}
		string trimmed = batchNo.Trim();
		return await unitOfWork.Context.Batches.AnyAsync((Batch batch) => batch.DrugId == resolvedDrugId && batch.SupplierId == supplierId && batch.BatchNo == trimmed && batch.ExpiryDate == expiryDate, cancellationToken);
	}

	public async Task<AddStockResult> AddStockAsync(AddStockInput input, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureAllowed(role);
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
		}
		string schedule = input.Schedule?.Trim().ToUpperInvariant();
		if (string.IsNullOrWhiteSpace(schedule) || !Schedules.Contains(schedule))
		{
			throw new InvalidOperationException("Choose the schedule (OTC, G, H, H1, X or NDPS).");
		}
		if (string.IsNullOrWhiteSpace(input.MedicineName))
		{
			throw new InvalidOperationException("Medicine name is required.");
		}
		if (input.Quantity <= 0m || input.FreeQuantity < 0m || input.Mrp < 0m || input.PurchaseRate < 0m || input.GstRate < 0m || input.PackSize < 0m)
		{
			throw new InvalidOperationException("Enter a positive quantity and non-negative prices, GST and pack size.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		AddStockResult result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			Guid? guid = input.DrugId ?? (await FindDrugIdAsync(input.CatalogMedicineId, cancellationToken));
			Guid? drugId = guid;
			Drug drug;
			if (drugId.HasValue)
			{
				drug = await context.Drugs.SingleAsync((Drug item) => item.Id == ((Guid?)drugId).Value && item.IsActive, cancellationToken);
				if (string.IsNullOrWhiteSpace(drug.Schedule))
				{
					drug.Schedule = schedule;
				}
				else if (!string.Equals(drug.Schedule.Trim(), schedule, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("This medicine is saved with schedule " + drug.Schedule + ". Use a schedule override to change it.");
				}
			}
			else
			{
				drug = new Drug
				{
					Name = input.MedicineName.Trim(),
					GenericName = (string.IsNullOrWhiteSpace(input.Composition) ? null : input.Composition.Trim()),
					Schedule = schedule,
					GstRate = input.GstRate,
					Mrp = input.Mrp,
					SalePrice = input.Mrp,
					CatalogMedicineId = input.CatalogMedicineId
				};
				context.Drugs.Add(drug);
				if (input.PackSize > 0m)
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
					EntityName = "Drug",
					EntityId = drug.Id,
					Details = drug.Name + "; schedule " + schedule
				});
				await unitOfWork.SaveChangesAsync(cancellationToken);
			}
			string batchNo = input.BatchNo.Trim();
			bool merged = await context.Batches.AnyAsync((Batch batch2) => batch2.DrugId == drug.Id && batch2.SupplierId == input.SupplierId && batch2.BatchNo == batchNo && batch2.ExpiryDate == input.ExpiryDate, cancellationToken);
			decimal num = Round(input.Quantity * input.PurchaseRate);
			decimal num2 = Round(num * input.GstRate / 100m);
			await purchaseService.SavePurchaseAsync(new SavePurchaseInput(input.SupplierId, input.SupplierInvoiceNo, input.InvoiceDate, num, 0m, num2, num + num2, new _003C_003Ez__ReadOnlySingleElementList<PurchaseLineInput>(new PurchaseLineInput(drug.Id, batchNo, input.ExpiryDate, input.Quantity, input.FreeQuantity, input.Mrp, input.PurchaseRate, input.GstRate, 0m, num, input.Rack))), actingUserId, role, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			if (!licenseRuntimeGuard.OnTransactionCommitted())
			{
				throw new InvalidOperationException("System clock manipulation detected. Please set your system time correctly to resume.");
			}
			Batch batch = await context.Batches.AsNoTracking().FirstAsync((Batch item) => item.DrugId == drug.Id && item.SupplierId == input.SupplierId && item.BatchNo == batchNo && item.ExpiryDate == input.ExpiryDate, cancellationToken);
			decimal valueOrDefault = (await (from movement in context.StockMovements.AsNoTracking()
				where movement.DrugId == drug.Id
				select movement).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
			result = new AddStockResult(drug.Id, batch.Id, valueOrDefault, merged);
		}
		return result;
	}

	private async Task<Guid?> FindDrugIdAsync(Guid? catalogMedicineId, CancellationToken cancellationToken)
	{
		if (!catalogMedicineId.HasValue)
		{
			return null;
		}
		return await ((IQueryable<Drug>)(from drug in unitOfWork.Context.Drugs.AsNoTracking()
			where drug.CatalogMedicineId == catalogMedicineId && drug.IsActive
			orderby drug.CreatedAtUtc
			select drug)).Select((Expression<Func<Drug, Guid?>>)((Drug drug) => drug.Id)).FirstOrDefaultAsync(cancellationToken);
	}

	private static void EnsureAllowed(UserRole role)
	{
		if (!PermissionMatrix.Allows(role, AppPermission.ManagePurchases))
		{
			throw new UnauthorizedAccessException("Your role cannot add stock.");
		}
	}

	private static decimal Round(decimal value)
	{
		return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
	}
}
