using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed partial class StatutoryRegistersPageViewModel : SectionPageViewModel, ILoadablePage
{
    private readonly StatutoryRegisterService _registerService;
    private readonly SensitiveAccessService _sensitiveAccess;
    private readonly IConfirmationService _confirmation;
    private readonly IFilePickerService _filePicker;
    private readonly TabularExportService _exportService;
    private readonly CurrentSession _currentSession;
    private bool _authorized;

    public StatutoryRegistersPageViewModel(
        StatutoryRegisterService registerService,
        SensitiveAccessService sensitiveAccess,
        IConfirmationService confirmation,
        IFilePickerService filePicker,
        TabularExportService exportService,
        CurrentSession currentSession)
        : base("Statutory registers")
    {
        _registerService = registerService;
        _sensitiveAccess = sensitiveAccess;
        _confirmation = confirmation;
        _filePicker = filePicker;
        _exportService = exportService;
        _currentSession = currentSession;
        RegisterTypes = ["H1", "X", "NDPS", "Habit-forming", "All"];
    }

    public IReadOnlyList<string> RegisterTypes { get; }
    public ObservableCollection<StatutoryRegisterRow> Entries { get; } = [];

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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _authorized = await _sensitiveAccess.RequestCurrentUserPinAsync(
            System.Windows.Application.Current?.MainWindow,
            "open statutory registers",
            cancellationToken);
        if (!_authorized)
        {
            ErrorMessage = "Access cancelled or PIN verification failed.";
            Entries.Clear();
            return;
        }

        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!_authorized || _currentSession.User is null)
        {
            ErrorMessage = "Unlock the registers before searching.";
            return;
        }

        try
        {
            var entries = await _registerService.SearchAsync(
                SelectedRegisterType,
                _currentSession.User.Id);
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            StatusMessage = $"{entries.Count} read-only register entry/entries.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task AppendCorrectionAsync()
    {
        var user = _currentSession.User;
        if (!_authorized || user is null ||
            !PermissionMatrix.Allows(user.Role, AppPermission.ManageSettings))
        {
            ErrorMessage = "Only an authorised owner or manager can append a register correction.";
            return;
        }

        var selectedEntry = SelectedEntry;
        if (selectedEntry is null || string.IsNullOrWhiteSpace(CorrectionReason))
        {
            ErrorMessage = "Select an entry and provide the correction reason.";
            return;
        }

        try
        {
            await _registerService.AppendCorrectionAsync(
                selectedEntry.EntryId,
                CorrectionReason,
                user.Id);
            CorrectionReason = string.Empty;
            StatusMessage = "Correction appended as a new register entry; the original remains unchanged.";
            await SearchAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private Task ExportPdfAsync() => ExportAsync("pdf");

    [RelayCommand]
    private Task ExportExcelAsync() => ExportAsync("xlsx");

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (!await AuthorizeExportAsync())
        {
            return;
        }

        var path = _filePicker.PickExportDestination("pdf", "statutory-register");
        if (path is null)
        {
            return;
        }

        try
        {
            await ExportFileAsync(path);
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "print"
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows could not open the register PDF print handler.");
            }

            StatusMessage = "Register PDF sent to the default printer.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ShowPdfForSharingAsync()
    {
        if (!await AuthorizeExportAsync())
        {
            return;
        }

        var path = _filePicker.PickExportDestination("pdf", "statutory-register");
        if (path is null)
        {
            return;
        }

        try
        {
            await ExportFileAsync(path);
            RetailBillPdfService.ShowInExplorer(path);
            StatusMessage = $"Register PDF ready to share: {path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task ExportAsync(string extension)
    {
        if (!await AuthorizeExportAsync())
        {
            return;
        }

        var path = _filePicker.PickExportDestination(extension, "statutory-register");
        if (path is null)
        {
            return;
        }

        try
        {
            await ExportFileAsync(path);
            StatusMessage = $"Register exported to {path}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task<bool> AuthorizeExportAsync()
    {
        if (!_authorized || _currentSession.User is null ||
            !PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.Export))
        {
            ErrorMessage = "Unlock the registers and verify your export permission before exporting.";
            return false;
        }

        if (!await _sensitiveAccess.RequestCurrentUserPinAsync(
                System.Windows.Application.Current?.MainWindow,
                "export statutory register patient details"))
        {
            ErrorMessage = "Export cancelled or PIN verification failed.";
            return false;
        }

        if (!_confirmation.Confirm(
                "This file contains patient details. Share only with authorised persons.",
                "Confirm sensitive register export"))
        {
            return false;
        }

        return true;
    }

    private async Task ExportFileAsync(string path)
    {
        var columns = new[]
        {
            "Date/time", "Document no.", "Patient or buyer", "Address", "Phone", "Buyer licence no.",
            "Doctor", "Doctor registration no.", "Drug", "Batch", "Quantity", "Balance", "Reason / notes"
        };
        IReadOnlyList<IReadOnlyList<string>> rows = Entries.Select(item => (IReadOnlyList<string>)
        [
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
        ]).ToArray();
        await _exportService.ExportAsync(path, $"{SelectedRegisterType} register", columns, rows);
        await _sensitiveAccess.RecordAsync(
            "StatutoryRegisterExported",
            $"Register={SelectedRegisterType}; format={System.IO.Path.GetExtension(path)}; rows={Entries.Count}; patient phone hidden={HidePatientPhoneNumber}.");
    }
}
