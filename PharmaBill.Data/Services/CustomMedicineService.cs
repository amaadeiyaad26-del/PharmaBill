using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record CustomMedicineRequest(
	string BrandName,
	string? GenericName,
	string? Manufacturer,
	string? PackSizeLabel,
	string? HsnCode,
	decimal GstRate,
	decimal? Mrp,
	decimal? PurchaseRate,
	string? Barcode = null);

public sealed record CustomMedicineResult(
	Guid CatalogMedicineId,
	Guid DrugId,
	string Name,
	decimal? Mrp,
	decimal? PurchaseRate,
	decimal GstRate,
	string? HsnCode,
	string? PackSizeLabel,
	string? GenericName);

public sealed class CustomMedicineService(PharmaBillDbContext context)
{
	public async Task<CustomMedicineResult> SaveAsync(CustomMedicineRequest request, Guid? userId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		string name = request.BrandName.Trim();
		if (name.Length == 0)
		{
			throw new InvalidOperationException("Brand name is required.");
		}

		decimal gst = request.GstRate;
		if (gst < 0m || gst > 100m)
		{
			gst = 12m;
		}

		string? generic = NullIfEmpty(request.GenericName);
		string? manufacturer = NullIfEmpty(request.Manufacturer);
		string? pack = NullIfEmpty(request.PackSizeLabel);
		string? hsn = NullIfEmpty(request.HsnCode);
		string compositionKey = CompositionKeyBuilder.Build(generic);

		string nameKey = name.ToLowerInvariant();
		Drug? drug = await context.Drugs.FirstOrDefaultAsync(
			item => item.IsActive && item.Name.ToLower() == nameKey,
			cancellationToken);
		CatalogMedicine? medicine = null;
		if (drug?.CatalogMedicineId is Guid linkedId)
		{
			medicine = await context.CatalogMedicines.FirstOrDefaultAsync(item => item.Id == linkedId, cancellationToken);
		}

		if (medicine == null)
		{
			medicine = await context.CatalogMedicines.FirstOrDefaultAsync(
				item => item.Name.ToLower() == nameKey,
				cancellationToken);
		}

		if (medicine == null)
		{
			medicine = new CatalogMedicine
			{
				Name = name,
				BrandName = name,
				GenericName = generic,
				ShortComposition1 = generic,
				CompositionKey = compositionKey,
				Manufacturer = manufacturer,
				PackSizeLabel = pack,
				ReferencePrice = request.Mrp,
				IsCustom = true,
				IsDiscontinued = false,
				SourceId = "custom:" + Guid.NewGuid().ToString("N")
			};
			context.CatalogMedicines.Add(medicine);
		}
		else
		{
			if (string.IsNullOrWhiteSpace(medicine.SourceId) || medicine.SourceId.StartsWith("custom:", StringComparison.Ordinal))
			{
				medicine.IsCustom = true;
			}

			if (!string.IsNullOrWhiteSpace(generic))
			{
				medicine.GenericName = generic;
				medicine.ShortComposition1 = generic;
				medicine.CompositionKey = compositionKey;
			}

			if (!string.IsNullOrWhiteSpace(manufacturer))
			{
				medicine.Manufacturer = manufacturer;
			}

			if (!string.IsNullOrWhiteSpace(pack))
			{
				medicine.PackSizeLabel = pack;
			}

			if (request.Mrp is > 0m)
			{
				medicine.ReferencePrice = request.Mrp;
			}

			medicine.UpdatedAtUtc = DateTime.UtcNow;
		}

		if (drug == null)
		{
			drug = new Drug
			{
				Name = name,
				BrandName = name,
				GenericName = generic ?? medicine.GenericName,
				DosageForm = pack,
				Unit = pack,
				HsnCode = hsn,
				GstRate = gst,
				Mrp = request.Mrp,
				SalePrice = request.Mrp,
				Barcode = NullIfEmpty(request.Barcode),
				CatalogMedicineId = medicine.Id,
				IsActive = true
			};
			context.Drugs.Add(drug);
		}
		else
		{
			drug.CatalogMedicineId ??= medicine.Id;
			drug.BrandName = string.IsNullOrWhiteSpace(drug.BrandName) ? name : drug.BrandName;
			if (!string.IsNullOrWhiteSpace(generic))
			{
				drug.GenericName = generic;
			}

			if (!string.IsNullOrWhiteSpace(hsn))
			{
				drug.HsnCode = hsn;
			}

			drug.GstRate = gst;
			if (request.Mrp is > 0m)
			{
				drug.Mrp = request.Mrp;
				drug.SalePrice ??= request.Mrp;
			}

			if (!string.IsNullOrWhiteSpace(pack))
			{
				drug.Unit = pack;
				drug.DosageForm = string.IsNullOrWhiteSpace(drug.DosageForm) ? pack : drug.DosageForm;
			}

			if (string.IsNullOrWhiteSpace(drug.Barcode))
			{
				drug.Barcode = NullIfEmpty(request.Barcode);
			}

			drug.UpdatedAtUtc = DateTime.UtcNow;
		}

		if (userId.HasValue)
		{
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				Action = "CustomMedicineSaved",
				EntityName = "Drug",
				EntityId = drug.Id,
				Details = name
			});
		}

		await context.SaveChangesAsync(cancellationToken);
		await UpsertSearchIndexAsync(medicine, cancellationToken);
		return new CustomMedicineResult(medicine.Id, drug.Id, drug.Name, drug.Mrp, request.PurchaseRate, gst, drug.HsnCode, drug.Unit ?? medicine.PackSizeLabel, drug.GenericName);
	}

	public async Task UpsertSearchIndexAsync(CatalogMedicine medicine, CancellationToken cancellationToken = default)
	{
		DbConnection connection = context.Database.GetDbConnection();
		bool close = connection.State != System.Data.ConnectionState.Open;
		if (close)
		{
			await connection.OpenAsync(cancellationToken);
		}

		try
		{
			await using DbCommand delete = connection.CreateCommand();
			DbParameter id = delete.CreateParameter();
			id.ParameterName = "$id";
			id.Value = medicine.Id.ToString("N");
			delete.Parameters.Add(id);
			delete.CommandText = "DELETE FROM CatalogMedicineFts WHERE MedicineId = $id";
			await delete.ExecuteNonQueryAsync(cancellationToken);

			await using DbCommand insert = connection.CreateCommand();
			Add(insert, "$id", medicine.Id.ToString("N"));
			Add(insert, "$name", medicine.Name);
			Add(insert, "$composition", string.Join(' ', new[] { medicine.ShortComposition1, medicine.ShortComposition2, medicine.GenericName }.Where(value => !string.IsNullOrWhiteSpace(value))));
			Add(insert, "$manufacturer", medicine.Manufacturer ?? string.Empty);
			insert.CommandText = "INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES ($id, $name, $composition, $manufacturer)";
			await insert.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
		{
			// Catalogue FTS is created by the import migration. Name search still unions CatalogMedicines and Drugs.
		}
		finally
		{
			if (close)
			{
				await connection.CloseAsync();
			}
		}
	}

	private static void Add(DbCommand command, string name, string value)
	{
		DbParameter parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}

	private static string? NullIfEmpty(string? value)
	{
		string trimmed = value?.Trim() ?? string.Empty;
		return trimmed.Length == 0 ? null : trimmed;
	}
}
