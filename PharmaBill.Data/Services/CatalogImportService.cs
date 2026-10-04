using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record CatalogImportProgress(
    string ImportKey,
    string FileName,
    long ProcessedRows,
    long ImportedRows,
    long SkippedRows,
    long ExpectedRows,
    int Percentage,
    TimeSpan EstimatedTimeRemaining,
    string Status,
    string? LastError);

public sealed class CatalogImportService(PharmaBillDbContext context)
{
    public const int BatchSize = 1_000;
    public const long ExpectedCatalogRows = 253_973;
    public const long ExpectedInfoRows = 222_473;

    public event EventHandler<CatalogImportProgress>? ProgressChanged;

    public async Task ImportAllAsync(
        string catalogPath,
        string infoPath,
        CancellationToken cancellationToken = default,
        bool forceReimport = false)
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

    public async Task ImportCatalogAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        bool forceReimport = false)
    {
        await EnsureFtsIndexAsync(cancellationToken);
        var fullPath = Path.GetFullPath(filePath);
        var fingerprint = GetFingerprint(fullPath);
        var state = await PrepareStateAsync("catalog", fullPath, fingerprint, forceReimport, cancellationToken);
        if (state.Status == "Completed" && state.FileFingerprint == fingerprint)
        {
            RaiseProgress("catalog", fullPath, state.LastProcessedRecord, state.ImportedRows,
                state.SkippedRows, ExpectedCatalogRows, TimeSpan.Zero, state.Status, state.LastError);
            return;
        }
        var processedRows = state.LastProcessedRecord;
        var importedRows = state.ImportedRows;
        var skippedRows = state.SkippedRows;
        var rowNumber = 0L;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var batch = new List<CatalogMedicine>(BatchSize);
        var pendingSkipped = 0;
        var lastError = state.LastError;

        using var reader = OpenReader(fullPath);
        using var csv = new CsvReader(reader, CreateConfiguration());
        await ReadHeaderAsync(csv, ["id", "name", "price(\u20B9)", "Is_discontinued", "manufacturer_name",
            "type", "pack_size_label", "short_composition1", "short_composition2"], cancellationToken);

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
                var sourceId = GetRequired(csv, "id");
                var name = GetRequired(csv, "name");
                var priceText = Normalize(csv.GetField("price(\u20B9)"));
                decimal? price = null;
                if (priceText is not null)
                {
                    if (!decimal.TryParse(priceText, NumberStyles.Number,
                            CultureInfo.InvariantCulture, out var parsedPrice))
                    {
                        throw new InvalidDataException("Reference price is not a valid decimal.");
                    }

                    price = parsedPrice;
                }

                var discontinuedText = Normalize(csv.GetField("Is_discontinued"));
                if (!bool.TryParse(discontinuedText, out var discontinued))
                {
                    throw new InvalidDataException("Is_discontinued must be TRUE or FALSE.");
                }

                batch.Add(new CatalogMedicine
                {
                    Id = StableGuid($"catalog:{sourceId}"),
                    SourceId = sourceId,
                    Name = name,
                    ReferencePrice = price,
                    IsDiscontinued = discontinued,
                    Manufacturer = Normalize(csv.GetField("manufacturer_name")),
                    Type = Normalize(csv.GetField("type")),
                    PackSizeLabel = Normalize(csv.GetField("pack_size_label")),
                    ShortComposition1 = Normalize(csv.GetField("short_composition1")),
                    ShortComposition2 = Normalize(csv.GetField("short_composition2")),
                    CompositionKey = CompositionKeyBuilder.Build(
                        Normalize(csv.GetField("short_composition1")),
                        Normalize(csv.GetField("short_composition2")))
                });
            }
            catch (Exception exception) when (exception is CsvHelperException or InvalidDataException or FormatException)
            {
                pendingSkipped++;
                lastError = $"Record {rowNumber}: {exception.Message}";
            }

            if (batch.Count + pendingSkipped >= BatchSize)
            {
                var stateRow = await SaveCatalogBatchAsync(
                    batch,
                    state,
                    rowNumber,
                    importedRows,
                    skippedRows,
                    pendingSkipped,
                    lastError,
                    cancellationToken);
                state = stateRow;
                processedRows = rowNumber;
                importedRows = state.ImportedRows;
                skippedRows = state.SkippedRows;
                batch.Clear();
                pendingSkipped = 0;
                RaiseProgress("catalog", fullPath, processedRows, importedRows, skippedRows,
                    ExpectedCatalogRows, stopwatch.Elapsed, "Importing", state.LastError);
            }
        }

        if (batch.Count > 0 || pendingSkipped > 0 || rowNumber > state.LastProcessedRecord)
        {
            state = await SaveCatalogBatchAsync(
                batch,
                state,
                rowNumber,
                importedRows,
                skippedRows,
                pendingSkipped,
                lastError,
                cancellationToken);
            processedRows = rowNumber;
        }

        await CompleteAsync(state, processedRows, cancellationToken);
        RaiseProgress("catalog", fullPath, processedRows, state.ImportedRows, state.SkippedRows,
            ExpectedCatalogRows, stopwatch.Elapsed, "Completed", state.LastError);
    }

    public async Task ImportInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        bool forceReimport = false)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fingerprint = GetFingerprint(fullPath);
        var state = await PrepareStateAsync("info", fullPath, fingerprint, forceReimport, cancellationToken);
        if (state.Status == "Completed" && state.FileFingerprint == fingerprint)
        {
            RaiseProgress("info", fullPath, state.LastProcessedRecord, state.ImportedRows,
                state.SkippedRows, ExpectedInfoRows, TimeSpan.Zero, state.Status, state.LastError);
            return;
        }
        var rowNumber = 0L;
        var importedRows = state.ImportedRows;
        var skippedRows = state.SkippedRows;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var batch = new List<CatalogInfo>(BatchSize);
        var pendingSkipped = 0;
        var lastError = state.LastError;

        using var reader = OpenReader(fullPath);
        using var csv = new CsvReader(reader, CreateConfiguration());
        await ReadHeaderAsync(csv, ["name_key", "habit_forming", "therapeutic_class", "chemical_class",
            "action_class", "use"], cancellationToken);

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
                var nameKey = GetRequired(csv, "name_key").ToLowerInvariant();
                var habitFormingValue = GetRequired(csv, "habit_forming");
                if (habitFormingValue is not "Y" and not "N")
                {
                    throw new InvalidDataException("habit_forming must be Y or N.");
                }

                batch.Add(new CatalogInfo
                {
                    Id = StableGuid($"info:{nameKey}"),
                    NameKey = nameKey,
                    IsHabitForming = habitFormingValue == "Y",
                    TherapeuticClass = Normalize(csv.GetField("therapeutic_class")),
                    ChemicalClass = Normalize(csv.GetField("chemical_class")),
                    ActionClass = Normalize(csv.GetField("action_class")),
                    Use = Normalize(csv.GetField("use"))
                });
            }
            catch (Exception exception) when (exception is CsvHelperException or InvalidDataException or FormatException)
            {
                pendingSkipped++;
                lastError = $"Record {rowNumber}: {exception.Message}";
            }

            if (batch.Count + pendingSkipped >= BatchSize)
            {
                state = await SaveInfoBatchAsync(
                    batch,
                    state,
                    rowNumber,
                    importedRows,
                    skippedRows,
                    pendingSkipped,
                    lastError,
                    cancellationToken);
                importedRows = state.ImportedRows;
                skippedRows = state.SkippedRows;
                batch.Clear();
                pendingSkipped = 0;
                RaiseProgress("info", fullPath, rowNumber, importedRows, skippedRows,
                    ExpectedInfoRows, stopwatch.Elapsed, "Importing", state.LastError);
            }
        }

        if (batch.Count > 0 || pendingSkipped > 0 || rowNumber > state.LastProcessedRecord)
        {
            state = await SaveInfoBatchAsync(
                batch,
                state,
                rowNumber,
                importedRows,
                skippedRows,
                pendingSkipped,
                lastError,
                cancellationToken);
        }

        await CompleteAsync(state, rowNumber, cancellationToken);
        RaiseProgress("info", fullPath, rowNumber, state.ImportedRows, state.SkippedRows,
            ExpectedInfoRows, stopwatch.Elapsed, "Completed", state.LastError);
    }

    public async Task EnsureFtsIndexAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS CatalogMedicineFts USING fts5(
                MedicineId UNINDEXED,
                Name,
                Composition,
                Manufacturer,
                tokenize='unicode61 remove_diacritics 2'
            );
            """,
            cancellationToken);
    }

    private async Task<CatalogImportState> PrepareStateAsync(
        string key,
        string filePath,
        string fingerprint,
        bool forceReimport,
        CancellationToken cancellationToken)
    {
        var state = await context.CatalogImportStates.SingleOrDefaultAsync(
            item => item.ImportKey == key,
            cancellationToken);
        if (state is null)
        {
            state = new CatalogImportState { ImportKey = key };
            context.CatalogImportStates.Add(state);
        }

        var sameFile = state.FileFingerprint == fingerprint;
        if (!sameFile || forceReimport)
        {
            state.LastProcessedRecord = 0;
            state.ImportedRows = 0;
            state.SkippedRows = 0;
            state.LastError = null;
        }

        state.FilePath = filePath;
        state.FileFingerprint = fingerprint;
        if (sameFile && !forceReimport && state.Status == "Completed")
        {
            context.ChangeTracker.Clear();
            return state;
        }

        state.Status = "Importing";
        state.CompletedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return (await context.CatalogImportStates.SingleAsync(item => item.ImportKey == key, cancellationToken));
    }

    private async Task<CatalogImportState> SaveCatalogBatchAsync(
        IReadOnlyCollection<CatalogMedicine> rows,
        CatalogImportState previous,
        long lastProcessedRecord,
        long priorImported,
        long priorSkipped,
        int skippedThisBatch,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (rows.Count > 0)
        {
            var uniqueRows = rows.GroupBy(item => item.SourceId!, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var sourceIds = uniqueRows.Select(item => item.SourceId!).ToArray();
            var existing = await context.CatalogMedicines
                .Where(item => sourceIds.Contains(item.SourceId!))
                .ToDictionaryAsync(item => item.SourceId!, cancellationToken);
            foreach (var row in uniqueRows)
            {
                if (existing.TryGetValue(row.SourceId!, out var stored))
                {
                    stored.Name = row.Name;
                    stored.ReferencePrice = row.ReferencePrice;
                    stored.IsDiscontinued = row.IsDiscontinued;
                    stored.Manufacturer = row.Manufacturer;
                    stored.Type = row.Type;
                    stored.PackSizeLabel = row.PackSizeLabel;
                    stored.ShortComposition1 = row.ShortComposition1;
                    stored.ShortComposition2 = row.ShortComposition2;
                    stored.CompositionKey = row.CompositionKey;
                }
                else
                {
                    context.CatalogMedicines.Add(row);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await RefreshFtsRowsAsync(uniqueRows, transaction.GetDbTransaction(), cancellationToken);
        }

        var state = await context.CatalogImportStates.SingleAsync(item => item.Id == previous.Id, cancellationToken);
        state.LastProcessedRecord = lastProcessedRecord;
        state.ImportedRows = checked(priorImported + rows.Count);
        state.SkippedRows = checked(priorSkipped + skippedThisBatch);
        state.LastError = lastError;
        state.Status = "Importing";
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return await context.CatalogImportStates.SingleAsync(item => item.Id == state.Id, cancellationToken);
    }

    private async Task<CatalogImportState> SaveInfoBatchAsync(
        IReadOnlyCollection<CatalogInfo> rows,
        CatalogImportState previous,
        long lastProcessedRecord,
        long priorImported,
        long priorSkipped,
        int skippedThisBatch,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (rows.Count > 0)
        {
            var uniqueRows = rows.GroupBy(item => item.NameKey, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var keys = uniqueRows.Select(item => item.NameKey).ToArray();
            var existing = await context.CatalogInfos
                .Where(item => keys.Contains(item.NameKey))
                .ToDictionaryAsync(item => item.NameKey, cancellationToken);
            foreach (var row in uniqueRows)
            {
                if (existing.TryGetValue(row.NameKey, out var stored))
                {
                    stored.IsHabitForming = row.IsHabitForming;
                    stored.TherapeuticClass = row.TherapeuticClass;
                    stored.ChemicalClass = row.ChemicalClass;
                    stored.ActionClass = row.ActionClass;
                    stored.Use = row.Use;
                }
                else
                {
                    context.CatalogInfos.Add(row);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        var state = await context.CatalogImportStates.SingleAsync(item => item.Id == previous.Id, cancellationToken);
        state.LastProcessedRecord = lastProcessedRecord;
        state.ImportedRows = checked(priorImported + rows.Count);
        state.SkippedRows = checked(priorSkipped + skippedThisBatch);
        state.LastError = lastError;
        state.Status = "Importing";
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return await context.CatalogImportStates.SingleAsync(item => item.Id == state.Id, cancellationToken);
    }

    private async Task RefreshFtsRowsAsync(
        IReadOnlyList<CatalogMedicine> rows,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            var identifiers = new string[rows.Count];
            for (var index = 0; index < rows.Count; index++)
            {
                var parameter = delete.CreateParameter();
                parameter.ParameterName = $"$id{index}";
                parameter.Value = rows[index].Id.ToString("N");
                delete.Parameters.Add(parameter);
                identifiers[index] = parameter.ParameterName;
            }

            delete.CommandText = $"DELETE FROM CatalogMedicineFts WHERE MedicineId IN ({string.Join(",", identifiers)})";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        var values = new string[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var medicine = rows[index];
            AddParameter(insert, $"$id{index}", medicine.Id.ToString("N"));
            AddParameter(insert, $"$name{index}", medicine.Name);
            AddParameter(insert, $"$composition{index}",
                string.Join(" ", new[] { medicine.ShortComposition1, medicine.ShortComposition2 }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
            AddParameter(insert, $"$manufacturer{index}", medicine.Manufacturer ?? string.Empty);
            values[index] = $"($id{index}, $name{index}, $composition{index}, $manufacturer{index})";
        }

        insert.CommandText =
            $"INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES {string.Join(",", values)}";
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task CompleteAsync(
        CatalogImportState state,
        long processedRows,
        CancellationToken cancellationToken)
    {
        var stored = await context.CatalogImportStates.SingleAsync(item => item.Id == state.Id, cancellationToken);
        stored.LastProcessedRecord = processedRows;
        stored.Status = stored.SkippedRows > 0 ? "CompletedWithErrors" : "Completed";
        stored.CompletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private void RaiseProgress(
        string key,
        string path,
        long processed,
        long imported,
        long skipped,
        long expected,
        TimeSpan elapsed,
        string status,
        string? error)
    {
        var percentage = status == "Completed" || expected <= 0
            ? 100
            : (int)Math.Clamp(processed * 100d / expected, 0, 100);
        var remainingRows = Math.Max(0, expected - processed);
        var remaining = processed == 0 || elapsed == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(elapsed.TotalSeconds / processed * remainingRows);
        ProgressChanged?.Invoke(this, new CatalogImportProgress(
            key,
            Path.GetFileName(path),
            processed,
            imported,
            skipped,
            expected,
            percentage,
            remaining,
            status,
            error));
    }

    private static CsvConfiguration CreateConfiguration() => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        BadDataFound = null,
        MissingFieldFound = null,
        HeaderValidated = null,
        DetectDelimiter = false,
        Delimiter = ",",
        TrimOptions = TrimOptions.Trim
    };

    private static StreamReader OpenReader(string filePath) =>
        new(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

    private static async Task ReadHeaderAsync(
        CsvReader csv,
        IReadOnlyCollection<string> requiredHeaders,
        CancellationToken cancellationToken)
    {
        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            throw new InvalidDataException("CSV file is empty or has no header row.");
        }

        var actual = csv.HeaderRecord?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var missing = requiredHeaders.Where(header => !actual.Contains(header)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"CSV is missing required columns: {string.Join(", ", missing)}.");
        }
    }

    private static string GetRequired(CsvReader csv, string fieldName)
    {
        var value = Normalize(csv.GetField(fieldName));
        if (value is null)
        {
            throw new InvalidDataException($"'{fieldName}' is required.");
        }

        return value;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Equals("NA", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static string GetFingerprint(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
