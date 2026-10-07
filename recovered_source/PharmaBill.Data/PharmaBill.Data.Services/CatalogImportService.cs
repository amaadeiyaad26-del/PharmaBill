using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class CatalogImportService(PharmaBillDbContext context)
{
	public const int BatchSize = 1000;

	public const long ExpectedCatalogRows = 253973L;

	public const long ExpectedInfoRows = 222473L;

	public event EventHandler<CatalogImportProgress>? ProgressChanged;

	public async Task ImportAllAsync(string catalogPath, string infoPath, CancellationToken cancellationToken = default(CancellationToken), bool forceReimport = false)
	{
		await EnsureFtsIndexAsync(cancellationToken);
		if (File.Exists(catalogPath))
		{
			await ImportCatalogAsync(catalogPath, cancellationToken, forceReimport);
		}
		if (!cancellationToken.IsCancellationRequested && File.Exists(infoPath))
		{
			await ImportInfoAsync(infoPath, cancellationToken, forceReimport);
		}
	}

	public async Task ImportCatalogAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken), bool forceReimport = false)
	{
		await EnsureFtsIndexAsync(cancellationToken);
		string fullPath = Path.GetFullPath(filePath);
		string fingerprint = GetFingerprint(fullPath);
		CatalogImportState state = await PrepareStateAsync("catalog", fullPath, fingerprint, forceReimport, cancellationToken);
		if (state.Status == "Completed" && state.FileFingerprint == fingerprint)
		{
			RaiseProgress("catalog", fullPath, state.LastProcessedRecord, state.ImportedRows, state.SkippedRows, 253973L, TimeSpan.Zero, state.Status, state.LastError);
			return;
		}
		long processedRows = state.LastProcessedRecord;
		long importedRows = state.ImportedRows;
		long skippedRows = state.SkippedRows;
		long rowNumber = 0L;
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<CatalogMedicine> batch = new List<CatalogMedicine>(1000);
		int pendingSkipped = 0;
		string lastError = state.LastError;
		using StreamReader reader = OpenReader(fullPath);
		using CsvReader csv = new CsvReader(reader, CreateConfiguration());
		await ReadHeaderAsync(csv, new _003C_003Ez__ReadOnlyArray<string>(new string[9] { "id", "name", "price(₹)", "Is_discontinued", "manufacturer_name", "type", "pack_size_label", "short_composition1", "short_composition2" }), cancellationToken);
		while (await csv.ReadAsync())
		{
			cancellationToken.ThrowIfCancellationRequested();
			rowNumber++;
			if (rowNumber <= state.LastProcessedRecord)
			{
				continue;
			}
			try
			{
				string required = GetRequired(csv, "id");
				string required2 = GetRequired(csv, "name");
				string text = Normalize(csv.GetField("price(₹)"));
				decimal? referencePrice = null;
				if (text != null)
				{
					if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
					{
						throw new InvalidDataException("Reference price is not a valid decimal.");
					}
					referencePrice = result;
				}
				if (!bool.TryParse(Normalize(csv.GetField("Is_discontinued")), out var result2))
				{
					throw new InvalidDataException("Is_discontinued must be TRUE or FALSE.");
				}
				batch.Add(new CatalogMedicine
				{
					Id = StableGuid("catalog:" + required),
					SourceId = required,
					Name = required2,
					ReferencePrice = referencePrice,
					IsDiscontinued = result2,
					Manufacturer = Normalize(csv.GetField("manufacturer_name")),
					Type = Normalize(csv.GetField("type")),
					PackSizeLabel = Normalize(csv.GetField("pack_size_label")),
					ShortComposition1 = Normalize(csv.GetField("short_composition1")),
					ShortComposition2 = Normalize(csv.GetField("short_composition2")),
					CompositionKey = CompositionKeyBuilder.Build(Normalize(csv.GetField("short_composition1")), Normalize(csv.GetField("short_composition2")))
				});
			}
			catch (Exception ex) when ((ex is CsvHelperException || ex is InvalidDataException || ex is FormatException) ? true : false)
			{
				pendingSkipped++;
				lastError = $"Record {rowNumber}: {ex.Message}";
			}
			if (batch.Count + pendingSkipped >= 1000)
			{
				state = await SaveCatalogBatchAsync(batch, state, rowNumber, importedRows, skippedRows, pendingSkipped, lastError, cancellationToken);
				processedRows = rowNumber;
				importedRows = state.ImportedRows;
				skippedRows = state.SkippedRows;
				batch.Clear();
				pendingSkipped = 0;
				RaiseProgress("catalog", fullPath, processedRows, importedRows, skippedRows, 253973L, stopwatch.Elapsed, "Importing", state.LastError);
			}
		}
		if (batch.Count > 0 || pendingSkipped > 0 || rowNumber > state.LastProcessedRecord)
		{
			state = await SaveCatalogBatchAsync(batch, state, rowNumber, importedRows, skippedRows, pendingSkipped, lastError, cancellationToken);
			processedRows = rowNumber;
		}
		await CompleteAsync(state, processedRows, cancellationToken);
		RaiseProgress("catalog", fullPath, processedRows, state.ImportedRows, state.SkippedRows, 253973L, stopwatch.Elapsed, "Completed", state.LastError);
	}

	public async Task ImportInfoAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken), bool forceReimport = false)
	{
		string fullPath = Path.GetFullPath(filePath);
		string fingerprint = GetFingerprint(fullPath);
		CatalogImportState state = await PrepareStateAsync("info", fullPath, fingerprint, forceReimport, cancellationToken);
		if (state.Status == "Completed" && state.FileFingerprint == fingerprint)
		{
			RaiseProgress("info", fullPath, state.LastProcessedRecord, state.ImportedRows, state.SkippedRows, 222473L, TimeSpan.Zero, state.Status, state.LastError);
			return;
		}
		long rowNumber = 0L;
		long importedRows = state.ImportedRows;
		long skippedRows = state.SkippedRows;
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<CatalogInfo> batch = new List<CatalogInfo>(1000);
		int pendingSkipped = 0;
		string lastError = state.LastError;
		using StreamReader reader = OpenReader(fullPath);
		using CsvReader csv = new CsvReader(reader, CreateConfiguration());
		await ReadHeaderAsync(csv, new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "name_key", "habit_forming", "therapeutic_class", "chemical_class", "action_class", "use" }), cancellationToken);
		while (await csv.ReadAsync())
		{
			cancellationToken.ThrowIfCancellationRequested();
			rowNumber++;
			if (rowNumber <= state.LastProcessedRecord)
			{
				continue;
			}
			try
			{
				string text = GetRequired(csv, "name_key").ToLowerInvariant();
				string required = GetRequired(csv, "habit_forming");
				if (!(required == "Y") && !(required == "N"))
				{
					throw new InvalidDataException("habit_forming must be Y or N.");
				}
				batch.Add(new CatalogInfo
				{
					Id = StableGuid("info:" + text),
					NameKey = text,
					IsHabitForming = (required == "Y"),
					TherapeuticClass = Normalize(csv.GetField("therapeutic_class")),
					ChemicalClass = Normalize(csv.GetField("chemical_class")),
					ActionClass = Normalize(csv.GetField("action_class")),
					Use = Normalize(csv.GetField("use"))
				});
			}
			catch (Exception ex) when ((ex is CsvHelperException || ex is InvalidDataException || ex is FormatException) ? true : false)
			{
				pendingSkipped++;
				lastError = $"Record {rowNumber}: {ex.Message}";
			}
			if (batch.Count + pendingSkipped >= 1000)
			{
				state = await SaveInfoBatchAsync(batch, state, rowNumber, importedRows, skippedRows, pendingSkipped, lastError, cancellationToken);
				importedRows = state.ImportedRows;
				skippedRows = state.SkippedRows;
				batch.Clear();
				pendingSkipped = 0;
				RaiseProgress("info", fullPath, rowNumber, importedRows, skippedRows, 222473L, stopwatch.Elapsed, "Importing", state.LastError);
			}
		}
		if (batch.Count > 0 || pendingSkipped > 0 || rowNumber > state.LastProcessedRecord)
		{
			state = await SaveInfoBatchAsync(batch, state, rowNumber, importedRows, skippedRows, pendingSkipped, lastError, cancellationToken);
		}
		await CompleteAsync(state, rowNumber, cancellationToken);
		RaiseProgress("info", fullPath, rowNumber, state.ImportedRows, state.SkippedRows, 222473L, stopwatch.Elapsed, "Completed", state.LastError);
	}

	public async Task EnsureFtsIndexAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await context.Database.ExecuteSqlRawAsync("CREATE VIRTUAL TABLE IF NOT EXISTS CatalogMedicineFts USING fts5(\r\n    MedicineId UNINDEXED,\r\n    Name,\r\n    Composition,\r\n    Manufacturer,\r\n    tokenize='unicode61 remove_diacritics 2'\r\n);", cancellationToken);
	}

	private async Task<CatalogImportState> PrepareStateAsync(string key, string filePath, string fingerprint, bool forceReimport, CancellationToken cancellationToken)
	{
		CatalogImportState catalogImportState = await context.CatalogImportStates.SingleOrDefaultAsync((CatalogImportState item) => item.ImportKey == key, cancellationToken);
		if (catalogImportState == null)
		{
			catalogImportState = new CatalogImportState
			{
				ImportKey = key
			};
			context.CatalogImportStates.Add(catalogImportState);
		}
		bool flag = catalogImportState.FileFingerprint == fingerprint;
		if (!flag | forceReimport)
		{
			catalogImportState.LastProcessedRecord = 0L;
			catalogImportState.ImportedRows = 0L;
			catalogImportState.SkippedRows = 0L;
			catalogImportState.LastError = null;
		}
		catalogImportState.FilePath = filePath;
		catalogImportState.FileFingerprint = fingerprint;
		if (flag && !forceReimport && catalogImportState.Status == "Completed")
		{
			context.ChangeTracker.Clear();
			return catalogImportState;
		}
		catalogImportState.Status = "Importing";
		catalogImportState.CompletedAtUtc = null;
		await context.SaveChangesAsync(cancellationToken);
		context.ChangeTracker.Clear();
		return await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.ImportKey == key, cancellationToken);
	}

	private async Task<CatalogImportState> SaveCatalogBatchAsync(IReadOnlyCollection<CatalogMedicine> rows, CatalogImportState previous, long lastProcessedRecord, long priorImported, long priorSkipped, int skippedThisBatch, string? lastError, CancellationToken cancellationToken)
	{
		checked
		{
			CatalogImportState result;
			await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken))
			{
				if (rows.Count > 0)
				{
					CatalogMedicine[] uniqueRows = (from @group in rows.GroupBy((CatalogMedicine item) => item.SourceId, StringComparer.Ordinal)
						select @group.Last()).ToArray();
					string[] sourceIds = uniqueRows.Select((CatalogMedicine item) => item.SourceId).ToArray();
					Dictionary<string, CatalogMedicine> dictionary = await context.CatalogMedicines.Where((CatalogMedicine item) => sourceIds.Contains(item.SourceId)).ToDictionaryAsync((CatalogMedicine item) => item.SourceId, cancellationToken);
					CatalogMedicine[] array = uniqueRows;
					foreach (CatalogMedicine catalogMedicine in array)
					{
						if (dictionary.TryGetValue(catalogMedicine.SourceId, out var value))
						{
							value.Name = catalogMedicine.Name;
							value.ReferencePrice = catalogMedicine.ReferencePrice;
							value.IsDiscontinued = catalogMedicine.IsDiscontinued;
							value.Manufacturer = catalogMedicine.Manufacturer;
							value.Type = catalogMedicine.Type;
							value.PackSizeLabel = catalogMedicine.PackSizeLabel;
							value.ShortComposition1 = catalogMedicine.ShortComposition1;
							value.ShortComposition2 = catalogMedicine.ShortComposition2;
							value.CompositionKey = catalogMedicine.CompositionKey;
						}
						else
						{
							context.CatalogMedicines.Add(catalogMedicine);
						}
					}
					await context.SaveChangesAsync(cancellationToken);
					await RefreshFtsRowsAsync(uniqueRows, transaction.GetDbTransaction(), cancellationToken);
				}
				CatalogImportState state = await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.Id == previous.Id, cancellationToken);
				state.LastProcessedRecord = lastProcessedRecord;
				state.ImportedRows = priorImported + rows.Count;
				state.SkippedRows = priorSkipped + skippedThisBatch;
				state.LastError = lastError;
				state.Status = "Importing";
				await context.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				context.ChangeTracker.Clear();
				result = await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.Id == state.Id, cancellationToken);
			}
			return result;
		}
	}

	private async Task<CatalogImportState> SaveInfoBatchAsync(IReadOnlyCollection<CatalogInfo> rows, CatalogImportState previous, long lastProcessedRecord, long priorImported, long priorSkipped, int skippedThisBatch, string? lastError, CancellationToken cancellationToken)
	{
		checked
		{
			CatalogImportState result;
			await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken))
			{
				if (rows.Count > 0)
				{
					CatalogInfo[] uniqueRows = (from @group in rows.GroupBy((CatalogInfo item) => item.NameKey, StringComparer.Ordinal)
						select @group.Last()).ToArray();
					string[] keys = uniqueRows.Select((CatalogInfo item) => item.NameKey).ToArray();
					Dictionary<string, CatalogInfo> dictionary = await context.CatalogInfos.Where((CatalogInfo item) => keys.Contains(item.NameKey)).ToDictionaryAsync((CatalogInfo item) => item.NameKey, cancellationToken);
					CatalogInfo[] array = uniqueRows;
					foreach (CatalogInfo catalogInfo in array)
					{
						if (dictionary.TryGetValue(catalogInfo.NameKey, out var value))
						{
							value.IsHabitForming = catalogInfo.IsHabitForming;
							value.TherapeuticClass = catalogInfo.TherapeuticClass;
							value.ChemicalClass = catalogInfo.ChemicalClass;
							value.ActionClass = catalogInfo.ActionClass;
							value.Use = catalogInfo.Use;
						}
						else
						{
							context.CatalogInfos.Add(catalogInfo);
						}
					}
					await context.SaveChangesAsync(cancellationToken);
				}
				CatalogImportState state = await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.Id == previous.Id, cancellationToken);
				state.LastProcessedRecord = lastProcessedRecord;
				state.ImportedRows = priorImported + rows.Count;
				state.SkippedRows = priorSkipped + skippedThisBatch;
				state.LastError = lastError;
				state.Status = "Importing";
				await context.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				context.ChangeTracker.Clear();
				result = await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.Id == state.Id, cancellationToken);
			}
			return result;
		}
	}

	private async Task RefreshFtsRowsAsync(IReadOnlyList<CatalogMedicine> rows, DbTransaction transaction, CancellationToken cancellationToken)
	{
		DbConnection connection = context.Database.GetDbConnection();
		await using (DbCommand delete = connection.CreateCommand())
		{
			delete.Transaction = transaction;
			string[] array = new string[rows.Count];
			for (int i = 0; i < rows.Count; i++)
			{
				DbParameter dbParameter = delete.CreateParameter();
				dbParameter.ParameterName = $"$id{i}";
				dbParameter.Value = rows[i].Id.ToString("N");
				delete.Parameters.Add(dbParameter);
				array[i] = dbParameter.ParameterName;
			}
			delete.CommandText = "DELETE FROM CatalogMedicineFts WHERE MedicineId IN (" + string.Join(",", array) + ")";
			await delete.ExecuteNonQueryAsync(cancellationToken);
		}
		await using DbCommand insert = connection.CreateCommand();
		insert.Transaction = transaction;
		string[] array2 = new string[rows.Count];
		for (int j = 0; j < rows.Count; j++)
		{
			CatalogMedicine catalogMedicine = rows[j];
			AddParameter(insert, $"$id{j}", catalogMedicine.Id.ToString("N"));
			AddParameter(insert, $"$name{j}", catalogMedicine.Name);
			AddParameter(insert, $"$composition{j}", string.Join(" ", new string[2] { catalogMedicine.ShortComposition1, catalogMedicine.ShortComposition2 }.Where((string value) => !string.IsNullOrWhiteSpace(value))));
			AddParameter(insert, $"$manufacturer{j}", catalogMedicine.Manufacturer ?? string.Empty);
			array2[j] = $"($id{j}, $name{j}, $composition{j}, $manufacturer{j})";
		}
		insert.CommandText = "INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES " + string.Join(",", array2);
		await insert.ExecuteNonQueryAsync(cancellationToken);
	}

	private static void AddParameter(DbCommand command, string name, string value)
	{
		DbParameter dbParameter = command.CreateParameter();
		dbParameter.ParameterName = name;
		dbParameter.Value = value;
		command.Parameters.Add(dbParameter);
	}

	private async Task CompleteAsync(CatalogImportState state, long processedRows, CancellationToken cancellationToken)
	{
		CatalogImportState catalogImportState = await context.CatalogImportStates.SingleAsync((CatalogImportState item) => item.Id == state.Id, cancellationToken);
		catalogImportState.LastProcessedRecord = processedRows;
		catalogImportState.Status = ((catalogImportState.SkippedRows > 0) ? "CompletedWithErrors" : "Completed");
		catalogImportState.CompletedAtUtc = DateTime.UtcNow;
		await context.SaveChangesAsync(cancellationToken);
	}

	private void RaiseProgress(string key, string path, long processed, long imported, long skipped, long expected, TimeSpan elapsed, string status, string? error)
	{
		int percentage = ((status == "Completed" || expected <= 0) ? 100 : ((int)Math.Clamp((double)processed * 100.0 / (double)expected, 0.0, 100.0)));
		long num = Math.Max(0L, expected - processed);
		TimeSpan estimatedTimeRemaining = ((processed == 0L || elapsed == TimeSpan.Zero) ? TimeSpan.Zero : TimeSpan.FromSeconds(elapsed.TotalSeconds / (double)processed * (double)num));
		ProgressChanged?.Invoke(this, new CatalogImportProgress(key, Path.GetFileName(path), processed, imported, skipped, expected, percentage, estimatedTimeRemaining, status, error));
	}

	private static CsvConfiguration CreateConfiguration()
	{
		return new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			HasHeaderRecord = true,
			BadDataFound = null,
			MissingFieldFound = null,
			HeaderValidated = null,
			DetectDelimiter = false,
			Delimiter = ",",
			TrimOptions = TrimOptions.Trim
		};
	}

	private static StreamReader OpenReader(string filePath)
	{
		return new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 65536);
	}

	private static async Task ReadHeaderAsync(CsvReader csv, IReadOnlyCollection<string> requiredHeaders, CancellationToken cancellationToken)
	{
		if (!(await csv.ReadAsync()) || !csv.ReadHeader())
		{
			throw new InvalidDataException("CSV file is empty or has no header row.");
		}
		HashSet<string> actual = csv.HeaderRecord?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
		string[] array = requiredHeaders.Where((string header) => !actual.Contains(header)).ToArray();
		if (array.Length != 0)
		{
			throw new InvalidDataException("CSV is missing required columns: " + string.Join(", ", array) + ".");
		}
	}

	private static string GetRequired(CsvReader csv, string fieldName)
	{
		return Normalize(csv.GetField(fieldName)) ?? throw new InvalidDataException("'" + fieldName + "' is required.");
	}

	private static string? Normalize(string? value)
	{
		string text = value?.Trim();
		if (!string.IsNullOrEmpty(text) && !text.Equals("NA", StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		return null;
	}

	private static string GetFingerprint(string filePath)
	{
		using FileStream source = File.OpenRead(filePath);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static Guid StableGuid(string value)
	{
		return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
	}
}
