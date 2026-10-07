using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services.Reconciliation;

namespace PharmaBill.App.ViewModels;

public class WholesaleReconciliationPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private readonly IFilePickerService _filePicker;

	private readonly PurchaseSpreadsheetReader _spreadsheetReader;

	private string _statusMessage = "Import a bank CSV/Excel statement to begin auto-reconciliation.";

	private string _errorMessage = string.Empty;

	private string _importPath = string.Empty;

	private bool _isBusy;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? importStatementCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? approveAllHighMediumCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? batchSettleCommand;

	public ObservableCollection<BankStatementMatchRow> Matches { get; } = new ObservableCollection<BankStatementMatchRow>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		[MemberNotNull("_statusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusMessage, value))
			{
				OnPropertyChanging(nameof(StatusMessage));
				_statusMessage = value;
				OnPropertyChanged(nameof(StatusMessage));
			}
		}
	}

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
				OnPropertyChanging(nameof(ErrorMessage));
				_errorMessage = value;
				OnPropertyChanged(nameof(ErrorMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ImportPath
	{
		get
		{
			return _importPath;
		}
		[MemberNotNull("_importPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_importPath, value))
			{
				OnPropertyChanging(nameof(ImportPath));
				_importPath = value;
				OnPropertyChanged(nameof(ImportPath));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isBusy, value))
			{
				OnPropertyChanging(nameof(IsBusy));
				_isBusy = value;
				OnPropertyChanged(nameof(IsBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportStatementCommand => importStatementCommand ?? (importStatementCommand = new AsyncRelayCommand(ImportStatementAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ApproveAllHighMediumCommand => approveAllHighMediumCommand ?? (approveAllHighMediumCommand = new RelayCommand(ApproveAllHighMedium));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand BatchSettleCommand => batchSettleCommand ?? (batchSettleCommand = new AsyncRelayCommand(BatchSettleAsync));

	public WholesaleReconciliationPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IFilePickerService filePicker, PurchaseSpreadsheetReader spreadsheetReader)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_filePicker = filePicker;
		_spreadsheetReader = spreadsheetReader;
	}

	public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.CompletedTask;
	}

	private async Task ImportStatementAsync()
	{
		string text = _filePicker.PickPurchaseSpreadsheet() ?? _filePicker.PickCsvFile();
		if (!string.IsNullOrWhiteSpace(text))
		{
			await ImportFromPathAsync(text);
		}
	}

	public async Task ImportFromPathAsync(string path)
	{
		_ = 1;
		try
		{
			IsBusy = true;
			ErrorMessage = string.Empty;
			ImportPath = path;
			IReadOnlyList<BankStatementEntry> readOnlyList = ParseBankRows(await _spreadsheetReader.ReadAsync(path));
			if (readOnlyList.Count == 0)
			{
				ErrorMessage = "No deposit rows found. Expected columns: Date, Reference/UTR, Narration, DepositAmount (or Credit/Amount).";
				return;
			}
			using IServiceScope scope = _scopeFactory.CreateScope();
			ReconciliationResult reconciliationResult = await scope.ServiceProvider.GetRequiredService<IReconciliationService>().MatchAsync(readOnlyList);
			Matches.Clear();
			foreach (ReconciliationMatch match in reconciliationResult.Matches)
			{
				Matches.Add(new BankStatementMatchRow
				{
					Match = match,
					Approve = match.IsConfirmed
				});
			}
			StatusMessage = $"Matched {reconciliationResult.Matches.Count} line(s): High {reconciliationResult.HighCount}, Medium {reconciliationResult.MediumCount}, Low {reconciliationResult.LowCount}, unmatched {reconciliationResult.UnmatchedCount}.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void ApproveAllHighMedium()
	{
		foreach (BankStatementMatchRow item in Matches.Where((BankStatementMatchRow item) =>
		{
			ReconciliationConfidence confidence = item.Match.Confidence;
			return (uint)(confidence - 1) <= 1u && item.Match.MatchedDocuments.Count > 0;
		}))
		{
			item.Approve = true;
		}
	}

	private async Task BatchSettleAsync()
	{
		_ = 1;
		try
		{
			IsBusy = true;
			ErrorMessage = string.Empty;
			ReconciliationMatch[] confirmedMatches = (from row in Matches
				where row.Approve
				select row.Match with
				{
					IsConfirmed = true
				}).ToArray();
			using IServiceScope scope = _scopeFactory.CreateScope();
			BatchSettleResult batchSettleResult = await scope.ServiceProvider.GetRequiredService<IReconciliationService>().BatchSettleConfirmedMatchesAsync(confirmedMatches, (_session.User ?? throw new UnauthorizedAccessException("Sign in before posting receipts.")).Id);
			StatusMessage = batchSettleResult.Message;
			if (batchSettleResult.SettledCount > 0 && !string.IsNullOrWhiteSpace(ImportPath))
			{
				await ImportFromPathAsync(ImportPath);
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static IReadOnlyList<BankStatementEntry> ParseBankRows(PurchaseSpreadsheetData data)
	{
		IReadOnlyList<string> headers = data.Headers;
		string text = Find(new string[4] { "Date", "Txn Date", "Transaction Date", "Value Date" });
		string text2 = Find(new string[7] { "Reference", "UTR", "Ref No", "Reference/UTR", "Cheque No", "Chq No", "Transaction Ref" });
		string text3 = Find(new string[5] { "Narration", "Description", "Particulars", "Remarks", "Details" });
		string text4 = Find(new string[6] { "DepositAmount", "Deposit", "Credit", "Amount", "Cr Amount", "Credit Amount" });
		if (text == null || text4 == null)
		{
			throw new InvalidDataException("Bank file needs at least Date and DepositAmount/Credit/Amount columns.");
		}
		List<BankStatementEntry> list = new List<BankStatementEntry>();
		int num = 0;
		foreach (IReadOnlyDictionary<string, string> row in data.Rows)
		{
			if (TryParseDate(Get(row, text), out var date) && TryParseAmount(Get(row, text4), out var amount) && !(amount <= 0m))
			{
				list.Add(new BankStatementEntry(date, NullIfEmpty((text2 == null) ? null : Get(row, text2)), (text3 == null) ? string.Empty : Get(row, text3), amount, num++));
			}
		}
		return list;
		string? Find(params string[] aliases)
		{
			return headers.FirstOrDefault((string header) => aliases.Any((string alias) => string.Equals(header, alias, StringComparison.OrdinalIgnoreCase)));
		}
	}

	private static string Get(IReadOnlyDictionary<string, string> row, string key)
	{
		if (!row.TryGetValue(key, out string value))
		{
			return string.Empty;
		}
		return value;
	}

	private static string? NullIfEmpty(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}

	private static bool TryParseDate(string text, out DateOnly date)
	{
		if (DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date) || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
		{
			return true;
		}
		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result) || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
		{
			date = DateOnly.FromDateTime(result);
			return true;
		}
		date = default;
		return false;
	}

	private static bool TryParseAmount(string text, out decimal amount)
	{
		string s = text.Replace("₹", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
		if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
		{
			return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
		}
		return true;
	}
}
