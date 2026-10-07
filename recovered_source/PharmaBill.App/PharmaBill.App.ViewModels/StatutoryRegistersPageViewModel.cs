using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class StatutoryRegistersPageViewModel : SectionPageViewModel, ILoadablePage
{
	private readonly StatutoryRegisterService _registerService;

	private readonly SensitiveAccessService _sensitiveAccess;

	private readonly IConfirmationService _confirmation;

	private readonly IFilePickerService _filePicker;

	private readonly TabularExportService _exportService;

	private readonly CurrentSession _currentSession;

	private bool _authorized;

	[ObservableProperty]
	private string _selectedRegisterType = "All";

	[ObservableProperty]
	private StatutoryRegisterRow? _selectedEntry;

	[ObservableProperty]
	private string _correctionReason = string.Empty;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _hidePatientPhoneNumber;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? searchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? appendCorrectionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportPdfCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportExcelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? printCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showPdfForSharingCommand;

	public IReadOnlyList<string> RegisterTypes { get; }

	public ObservableCollection<StatutoryRegisterRow> Entries { get; } = new ObservableCollection<StatutoryRegisterRow>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedRegisterType
	{
		get
		{
			return _selectedRegisterType;
		}
		[MemberNotNull("_selectedRegisterType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedRegisterType, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedRegisterType);
				_selectedRegisterType = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedRegisterType);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public StatutoryRegisterRow? SelectedEntry
	{
		get
		{
			return _selectedEntry;
		}
		set
		{
			if (!EqualityComparer<StatutoryRegisterRow>.Default.Equals(_selectedEntry, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedEntry);
				_selectedEntry = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedEntry);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CorrectionReason
	{
		get
		{
			return _correctionReason;
		}
		[MemberNotNull("_correctionReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_correctionReason, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CorrectionReason);
				_correctionReason = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CorrectionReason);
			}
		}
	}

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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusMessage);
				_statusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusMessage);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ErrorMessage);
				_errorMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ErrorMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HidePatientPhoneNumber
	{
		get
		{
			return _hidePatientPhoneNumber;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hidePatientPhoneNumber, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HidePatientPhoneNumber);
				_hidePatientPhoneNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HidePatientPhoneNumber);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SearchCommand => searchCommand ?? (searchCommand = new AsyncRelayCommand(SearchAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AppendCorrectionCommand => appendCorrectionCommand ?? (appendCorrectionCommand = new AsyncRelayCommand(AppendCorrectionAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportPdfCommand => exportPdfCommand ?? (exportPdfCommand = new AsyncRelayCommand(ExportPdfAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportExcelCommand => exportExcelCommand ?? (exportExcelCommand = new AsyncRelayCommand(ExportExcelAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PrintCommand => printCommand ?? (printCommand = new AsyncRelayCommand(PrintAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowPdfForSharingCommand => showPdfForSharingCommand ?? (showPdfForSharingCommand = new AsyncRelayCommand(ShowPdfForSharingAsync));

	public StatutoryRegistersPageViewModel(StatutoryRegisterService registerService, SensitiveAccessService sensitiveAccess, IConfirmationService confirmation, IFilePickerService filePicker, TabularExportService exportService, CurrentSession currentSession)
		: base("Statutory registers")
	{
		_registerService = registerService;
		_sensitiveAccess = sensitiveAccess;
		_confirmation = confirmation;
		_filePicker = filePicker;
		_exportService = exportService;
		_currentSession = currentSession;
		RegisterTypes = new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "H1", "X", "NDPS", "Habit-forming", "All" });
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_authorized = await _sensitiveAccess.RequestCurrentUserPinAsync(Application.Current?.MainWindow, "open statutory registers", cancellationToken);
		if (!_authorized)
		{
			ErrorMessage = "Access cancelled or PIN verification failed.";
			Entries.Clear();
		}
		else
		{
			await SearchAsync();
		}
	}

	[RelayCommand]
	private async Task SearchAsync()
	{
		if (!_authorized || _currentSession.User == null)
		{
			ErrorMessage = "Unlock the registers before searching.";
			return;
		}
		try
		{
			IReadOnlyList<StatutoryRegisterRow> readOnlyList = await _registerService.SearchAsync(SelectedRegisterType, _currentSession.User.Id);
			Entries.Clear();
			foreach (StatutoryRegisterRow item in readOnlyList)
			{
				Entries.Add(item);
			}
			StatusMessage = $"{readOnlyList.Count} read-only register entry/entries.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private async Task AppendCorrectionAsync()
	{
		AppUser user = _currentSession.User;
		if (!_authorized || user == null || !PermissionMatrix.Allows(user.Role, AppPermission.ManageSettings))
		{
			ErrorMessage = "Only an authorised owner or manager can append a register correction.";
			return;
		}
		StatutoryRegisterRow selectedEntry = SelectedEntry;
		if ((object)selectedEntry == null || string.IsNullOrWhiteSpace(CorrectionReason))
		{
			ErrorMessage = "Select an entry and provide the correction reason.";
			return;
		}
		try
		{
			await _registerService.AppendCorrectionAsync(selectedEntry.EntryId, CorrectionReason, user.Id);
			CorrectionReason = string.Empty;
			StatusMessage = "Correction appended as a new register entry; the original remains unchanged.";
			await SearchAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private Task ExportPdfAsync()
	{
		return ExportAsync("pdf");
	}

	[RelayCommand]
	private Task ExportExcelAsync()
	{
		return ExportAsync("xlsx");
	}

	[RelayCommand]
	private async Task PrintAsync()
	{
		if (!(await AuthorizeExportAsync()))
		{
			return;
		}
		string path = _filePicker.PickExportDestination("pdf", "statutory-register");
		if (path == null)
		{
			return;
		}
		try
		{
			await ExportFileAsync(path);
			if (Process.Start(new ProcessStartInfo(path)
			{
				UseShellExecute = true,
				Verb = "print"
			}) == null)
			{
				throw new InvalidOperationException("Windows could not open the register PDF print handler.");
			}
			StatusMessage = "Register PDF sent to the default printer.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ShowPdfForSharingAsync()
	{
		if (!(await AuthorizeExportAsync()))
		{
			return;
		}
		string path = _filePicker.PickExportDestination("pdf", "statutory-register");
		if (path == null)
		{
			return;
		}
		try
		{
			await ExportFileAsync(path);
			RetailBillPdfService.ShowInExplorer(path);
			StatusMessage = "Register PDF ready to share: " + path;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ExportAsync(string extension)
	{
		if (!(await AuthorizeExportAsync()))
		{
			return;
		}
		string path = _filePicker.PickExportDestination(extension, "statutory-register");
		if (path == null)
		{
			return;
		}
		try
		{
			await ExportFileAsync(path);
			StatusMessage = "Register exported to " + path + ".";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task<bool> AuthorizeExportAsync()
	{
		if (!_authorized || _currentSession.User == null || !PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.Export))
		{
			ErrorMessage = "Unlock the registers and verify your export permission before exporting.";
			return false;
		}
		if (!(await _sensitiveAccess.RequestCurrentUserPinAsync(Application.Current?.MainWindow, "export statutory register patient details")))
		{
			ErrorMessage = "Export cancelled or PIN verification failed.";
			return false;
		}
		if (!_confirmation.Confirm("This file contains patient details. Share only with authorised persons.", "Confirm sensitive register export"))
		{
			return false;
		}
		return true;
	}

	private async Task ExportFileAsync(string path)
	{
		string[] columns = new string[13]
		{
			"Date/time", "Document no.", "Patient or buyer", "Address", "Phone", "Buyer licence no.", "Doctor", "Doctor registration no.", "Drug", "Batch",
			"Quantity", "Balance", "Reason / notes"
		};
		IReadOnlyList<IReadOnlyList<string>> rows = ((IEnumerable<StatutoryRegisterRow>)Entries).Select((Func<StatutoryRegisterRow, IReadOnlyList<string>>)((StatutoryRegisterRow item) => new _003C_003Ez__ReadOnlyArray<string>(new string[13]
		{
			item.EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
			item.DocumentNo,
			item.PartyName,
			item.Address,
			HidePatientPhoneNumber ? string.Empty : item.Phone,
			item.BuyerLicenceNo,
			item.DoctorName,
			item.DoctorRegistrationNo,
			item.DrugName,
			item.BatchNo,
			item.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
			item.Balance.ToString("0.##", CultureInfo.InvariantCulture),
			item.Reason
		}))).ToArray();
		await _exportService.ExportAsync(path, SelectedRegisterType + " register", columns, rows);
		await _sensitiveAccess.RecordAsync("StatutoryRegisterExported", $"Register={SelectedRegisterType}; format={Path.GetExtension(path)}; rows={Entries.Count}; patient phone hidden={HidePatientPhoneNumber}.");
	}
}
