using System.IO;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class CatalogImportViewModel : ObservableObject
{
    private readonly CatalogImportService _importService;
    private readonly IFilePickerService _filePicker;
    private CancellationTokenSource? _cancellation;

    public CatalogImportViewModel(CatalogImportService importService, IFilePickerService filePicker)
    {
        _importService = importService;
        _filePicker = filePicker;
        CatalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
        InfoPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
        _importService.ProgressChanged += OnProgressChanged;
    }

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

    public bool CanContinue => !IsImporting;

    partial void OnIsImportingChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    [RelayCommand]
    private void ChooseCatalog()
    {
        var path = _filePicker.PickCsvFile();
        if (path is not null)
        {
            CatalogPath = path;
        }
    }

    [RelayCommand]
    private void ChooseInfo()
    {
        var path = _filePicker.PickCsvFile();
        if (path is not null)
        {
            InfoPath = path;
        }
    }

    [RelayCommand]
    private async Task StartAsync() => await ImportAsync(forceReimport: false);

    public async Task ReimportAsync(string importKey, string filePath, CancellationToken cancellationToken = default)
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

        throw new ArgumentOutOfRangeException(nameof(importKey), importKey, "Unknown catalog import key.");
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
            await _importService.ImportAllAsync(
                File.Exists(CatalogPath) ? CatalogPath : string.Empty,
                File.Exists(InfoPath) ? InfoPath : string.Empty,
                _cancellation.Token,
                forceReimport);
            StatusMessage = "Catalogue import complete.";
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            StatusMessage = "Import cancelled. Completed batches have been saved and can be resumed.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
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
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
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
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
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
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (progress.ImportKey == "catalog")
            {
                CatalogProgress = progress;
            }
            else
            {
                InfoProgress = progress;
            }

            StatusMessage = $"{progress.FileName}: {progress.Status}";
            if (progress.LastError is not null)
            {
                ErrorMessage = progress.LastError;
            }
        });
    }
}
