using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class CatalogImportViewModel : ObservableObject
{
	private readonly CatalogImportService _importService;

	private readonly IFilePickerService _filePicker;

	private CancellationTokenSource? _cancellation;

	[ObservableProperty]
	private string _catalogPath;

	[ObservableProperty]
	private string _infoPath;

	[ObservableProperty]
	private CatalogImportProgress? _catalogProgress;

	[ObservableProperty]
	private CatalogImportProgress? _infoProgress;

	[ObservableProperty]
	private string _statusMessage = "Catalogue import is ready.";

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isImporting;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? chooseCatalogCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? chooseInfoCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? startCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelCommand;

	public bool CanContinue => !IsImporting;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CatalogPath
	{
		get
		{
			return _catalogPath;
		}
		[MemberNotNull("_catalogPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_catalogPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CatalogPath);
				_catalogPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CatalogPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InfoPath
	{
		get
		{
			return _infoPath;
		}
		[MemberNotNull("_infoPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_infoPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InfoPath);
				_infoPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InfoPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public CatalogImportProgress? CatalogProgress
	{
		get
		{
			return _catalogProgress;
		}
		set
		{
			if (!EqualityComparer<CatalogImportProgress>.Default.Equals(_catalogProgress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CatalogProgress);
				_catalogProgress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CatalogProgress);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public CatalogImportProgress? InfoProgress
	{
		get
		{
			return _infoProgress;
		}
		set
		{
			if (!EqualityComparer<CatalogImportProgress>.Default.Equals(_infoProgress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InfoProgress);
				_infoProgress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InfoProgress);
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
	public bool IsImporting
	{
		get
		{
			return _isImporting;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isImporting, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsImporting);
				_isImporting = value;
				OnIsImportingChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsImporting);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ChooseCatalogCommand => chooseCatalogCommand ?? (chooseCatalogCommand = new RelayCommand(ChooseCatalog));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ChooseInfoCommand => chooseInfoCommand ?? (chooseInfoCommand = new RelayCommand(ChooseInfo));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand StartCommand => startCommand ?? (startCommand = new AsyncRelayCommand(StartAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelCommand => cancelCommand ?? (cancelCommand = new RelayCommand(Cancel));

	public CatalogImportViewModel(CatalogImportService importService, IFilePickerService filePicker)
	{
		_importService = importService;
		_filePicker = filePicker;
		CatalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
		InfoPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
		_importService.ProgressChanged += OnProgressChanged;
	}

	[RelayCommand]
	private void ChooseCatalog()
	{
		string text = _filePicker.PickCsvFile();
		if (text != null)
		{
			CatalogPath = text;
		}
	}

	[RelayCommand]
	private void ChooseInfo()
	{
		string text = _filePicker.PickCsvFile();
		if (text != null)
		{
			InfoPath = text;
		}
	}

	[RelayCommand]
	private async Task StartAsync()
	{
		await ImportAsync(forceReimport: false);
	}

	public async Task ReimportAsync(string importKey, string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (importKey == "catalog")
		{
			CatalogPath = filePath;
			await ImportSingleCatalogAsync(filePath, cancellationToken);
			return;
		}
		if (importKey == "info")
		{
			InfoPath = filePath;
			await ImportSingleInfoAsync(filePath, cancellationToken);
			return;
		}
		throw new ArgumentOutOfRangeException("importKey", importKey, "Unknown catalog import key.");
	}

	[RelayCommand]
	private void Cancel()
	{
		_cancellation?.Cancel();
		StatusMessage = "Cancelling after the current committed batch...";
	}

	private async Task ImportAsync(bool forceReimport)
	{
		if (!File.Exists(CatalogPath) && !File.Exists(InfoPath))
		{
			ErrorMessage = "Choose at least one valid catalogue CSV file.";
			return;
		}
		ErrorMessage = string.Empty;
		IsImporting = true;
		_cancellation = new CancellationTokenSource();
		try
		{
			await _importService.ImportAllAsync(File.Exists(CatalogPath) ? CatalogPath : string.Empty, File.Exists(InfoPath) ? InfoPath : string.Empty, _cancellation.Token, forceReimport);
			StatusMessage = "Catalogue import complete.";
		}
		catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
		{
			StatusMessage = "Import cancelled. Completed batches have been saved and can be resumed.";
		}
		catch (Exception ex2)
		{
			ErrorMessage = ex2.Message;
			StatusMessage = "Import stopped. Committed batches are saved and can be resumed.";
		}
		finally
		{
			IsImporting = false;
			_cancellation.Dispose();
			_cancellation = null;
		}
	}

	private async Task ImportSingleCatalogAsync(string path, CancellationToken cancellationToken)
	{
		IsImporting = true;
		ErrorMessage = string.Empty;
		_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			await _importService.ImportCatalogAsync(path, _cancellation.Token, forceReimport: true);
			StatusMessage = "Medicine catalogue re-import completed.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsImporting = false;
			_cancellation.Dispose();
			_cancellation = null;
		}
	}

	private async Task ImportSingleInfoAsync(string path, CancellationToken cancellationToken)
	{
		IsImporting = true;
		ErrorMessage = string.Empty;
		_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			await _importService.ImportInfoAsync(path, _cancellation.Token, forceReimport: true);
			StatusMessage = "Medicine information re-import completed.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsImporting = false;
			_cancellation.Dispose();
			_cancellation = null;
		}
	}

	private void OnProgressChanged(object? sender, CatalogImportProgress progress)
	{
		Application.Current.Dispatcher.InvokeAsync(() =>
		{
			if (progress.ImportKey == "catalog")
			{
				CatalogProgress = progress;
			}
			else
			{
				InfoProgress = progress;
			}
			StatusMessage = progress.FileName + ": " + progress.Status;
			if (progress.LastError != null)
			{
				ErrorMessage = progress.LastError;
			}
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsImportingChanged(bool value)
	{
		OnPropertyChanged("CanContinue");
	}
}
