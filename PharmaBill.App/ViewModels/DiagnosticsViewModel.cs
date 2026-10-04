using System.Data;
using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record DiagnosticFile(string Name, string Path, long SizeBytes);

public sealed record TableRowCount(string TableName, long Rows);

public sealed class ImportStatusViewModel(CatalogImportState state)
{
    public string ImportKey { get; } = state.ImportKey;
    public string FilePath { get; } = state.FilePath;
    public string Status { get; } = state.Status;
    public long ImportedRows { get; } = state.ImportedRows;
    public long SkippedRows { get; } = state.SkippedRows;
    public string? LastError { get; } = state.LastError;
}

public partial class DiagnosticsViewModel(
    IServiceScopeFactory scopeFactory,
    IFilePickerService filePicker) : ObservableObject
{
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<DiagnosticFile> Files { get; } = [];

    public ObservableCollection<TableRowCount> TableCounts { get; } = [];

    public ObservableCollection<ImportStatusViewModel> ImportStatuses { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var imports = await context.CatalogImportStates.AsNoTracking()
                .OrderBy(state => state.ImportKey)
                .ToListAsync();
            Files.Clear();
            var filePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv"),
                Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv")
            }.Concat(imports.Select(import => import.FilePath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    Files.Add(new DiagnosticFile(Path.GetFileName(filePath), filePath, new FileInfo(filePath).Length));
                }
            }

            TableCounts.Clear();
            foreach (var count in await ReadTableCountsAsync(context))
            {
                TableCounts.Add(count);
            }

            ImportStatuses.Clear();
            foreach (var import in imports)
            {
                ImportStatuses.Add(new ImportStatusViewModel(import));
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private static async Task<IReadOnlyList<TableRowCount>> ReadTableCountsAsync(PharmaBillDbContext context)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(type => type.GetTableName())
            .Where(name => name is not null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Cast<string>()
            .ToList();
        tables.Add("CatalogMedicineFts");
        var query = string.Join(
            " UNION ALL ",
            tables.Select(name =>
                $"SELECT '{name.Replace("'", "''", StringComparison.Ordinal)}' AS TableName, COUNT(*) AS RowCount FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));

        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync();
            var result = new List<TableRowCount>(tables.Count);
            while (await reader.ReadAsync())
            {
                result.Add(new TableRowCount(reader.GetString(0), reader.GetInt64(1)));
            }

            return result;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    [RelayCommand]
    private async Task ReimportAsync(ImportStatusViewModel? import)
    {
        if (import is null)
        {
            return;
        }

        var path = File.Exists(import.FilePath) ? import.FilePath : filePicker.PickCsvFile();
        if (path is null)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<CatalogImportService>();
            if (import.ImportKey == "catalog")
            {
                await importer.ImportCatalogAsync(path, forceReimport: true);
            }
            else
            {
                await importer.ImportInfoAsync(path, forceReimport: true);
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }
}
