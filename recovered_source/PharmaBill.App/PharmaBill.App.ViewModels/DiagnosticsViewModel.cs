using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class DiagnosticsViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker) : ObservableObject
{
	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<ImportStatusViewModel?>? reimportCommand;

	public ObservableCollection<DiagnosticFile> Files { get; } = new ObservableCollection<DiagnosticFile>();

	public ObservableCollection<TableRowCount> TableCounts { get; } = new ObservableCollection<TableRowCount>();

	public ObservableCollection<ImportStatusViewModel> ImportStatuses { get; } = new ObservableCollection<ImportStatusViewModel>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		[MemberNotNull("_errorMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_errorMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ErrorMessage);
				_errorMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ErrorMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<ImportStatusViewModel?> ReimportCommand => reimportCommand ?? (reimportCommand = new AsyncRelayCommand<ImportStatusViewModel>(ReimportAsync));

	[RelayCommand]
	private async Task RefreshAsync()
	{
		ErrorMessage = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			List<CatalogImportState> imports = await (from state in context.CatalogImportStates.AsNoTracking()
				orderby state.ImportKey
				select state).ToListAsync();
			Files.Clear();
			foreach (string item in (from path in new string[2]
				{
					Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv"),
					Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv")
				}.Concat(imports.Select((CatalogImportState import) => import.FilePath))
				where !string.IsNullOrWhiteSpace(path)
				select path).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (File.Exists(item))
				{
					Files.Add(new DiagnosticFile(Path.GetFileName(item), item, new FileInfo(item).Length));
				}
			}
			TableCounts.Clear();
			foreach (TableRowCount item2 in await ReadTableCountsAsync(context))
			{
				TableCounts.Add(item2);
			}
			ImportStatuses.Clear();
			foreach (CatalogImportState item3 in imports)
			{
				ImportStatuses.Add(new ImportStatusViewModel(item3));
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private static async Task<IReadOnlyList<TableRowCount>> ReadTableCountsAsync(PharmaBillDbContext context)
	{
		List<string> tables = (from type in context.Model.GetEntityTypes()
			select type.GetTableName() into name
			where name != null
			select name).Distinct(StringComparer.Ordinal).OrderBy((string name) => name, StringComparer.Ordinal).Cast<string>()
			.ToList();
		tables.Add("CatalogMedicineFts");
		string query = string.Join(" UNION ALL ", tables.Select((string name) => $"SELECT '{name.Replace("'", "''", StringComparison.Ordinal)}' AS TableName, COUNT(*) AS RowCount FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
		DbConnection connection = context.Database.GetDbConnection();
		bool closeConnection = connection.State != ConnectionState.Open;
		if (closeConnection)
		{
			await connection.OpenAsync();
		}
		IReadOnlyList<TableRowCount> result2;
		try
		{
			IReadOnlyList<TableRowCount> readOnlyList2;
			await using (DbCommand command = connection.CreateCommand())
			{
				command.CommandText = query;
				IReadOnlyList<TableRowCount> readOnlyList;
				await using (DbDataReader reader = await command.ExecuteReaderAsync())
				{
					List<TableRowCount> result = new List<TableRowCount>(tables.Count);
					while (await reader.ReadAsync())
					{
						result.Add(new TableRowCount(reader.GetString(0), reader.GetInt64(1)));
					}
					readOnlyList = result;
				}
				readOnlyList2 = readOnlyList;
			}
			result2 = readOnlyList2;
		}
		finally
		{
			if (closeConnection)
			{
				await connection.CloseAsync();
			}
		}
		return result2;
	}

	[RelayCommand]
	private async Task ReimportAsync(ImportStatusViewModel? import)
	{
		if (import == null)
		{
			return;
		}
		string text = (File.Exists(import.FilePath) ? import.FilePath : filePicker.PickCsvFile());
		if (text == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			CatalogImportService requiredService = scope.ServiceProvider.GetRequiredService<CatalogImportService>();
			if (!(import.ImportKey == "catalog"))
			{
				await requiredService.ImportInfoAsync(text, default, forceReimport: true);
			}
			else
			{
				await requiredService.ImportCatalogAsync(text, default, forceReimport: true);
			}
			await RefreshAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}
}
