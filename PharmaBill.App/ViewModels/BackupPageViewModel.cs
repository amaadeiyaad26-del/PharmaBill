using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace PharmaBill.App.ViewModels;

public partial class BackupPageViewModel(
    BackupSettingsStore settingsStore,
    IServiceScopeFactory scopeFactory,
    IFilePickerService filePicker,
    IConfirmationService confirmationService) : ObservableObject, ILoadablePage
{
    [ObservableProperty] private string _primaryFolder = string.Empty;
    [ObservableProperty] private string _secondaryFolder = string.Empty;
    [ObservableProperty] private string _backupPassword = string.Empty;
    [ObservableProperty] private bool _automaticEnabled;
    [ObservableProperty] private DateTime? _lastBackupAtUtc;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public bool IsBackupOlderThanSevenDays => !LastBackupAtUtc.HasValue ||
        DateTime.UtcNow - LastBackupAtUtc.Value > TimeSpan.FromDays(7);

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = settingsStore.Load();
        PrimaryFolder = settings.PrimaryFolder ?? string.Empty;
        SecondaryFolder = settings.SecondaryFolder ?? string.Empty;
        BackupPassword = settings.Password ?? string.Empty;
        AutomaticEnabled = settings.AutomaticEnabled;
        LastBackupAtUtc = settings.LastBackupAtUtc;
        OnPropertyChanged(nameof(IsBackupOlderThanSevenDays));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ChoosePrimaryFolder()
    {
        var folder = filePicker.PickFolder("Choose the automatic backup folder");
        if (folder is not null)
        {
            PrimaryFolder = folder;
        }
    }

    [RelayCommand]
    private void ChooseSecondaryFolder()
    {
        var folder = filePicker.PickFolder("Choose optional second backup location (USB or synced drive)");
        if (folder is not null)
        {
            SecondaryFolder = folder;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            if (AutomaticEnabled && string.IsNullOrWhiteSpace(PrimaryFolder))
            {
                throw new InvalidOperationException("Choose a primary backup folder before enabling automatic backup.");
            }

            if (AutomaticEnabled)
            {
                EncryptedBackupService.ValidatePassword(BackupPassword);
            }

            if (!string.IsNullOrWhiteSpace(BackupPassword))
            {
                EncryptedBackupService.ValidatePassword(BackupPassword);
            }

            var prior = settingsStore.Load();
            settingsStore.Save(new BackupSettings(
                NullIfEmpty(PrimaryFolder),
                NullIfEmpty(SecondaryFolder),
                NullIfEmpty(BackupPassword),
                AutomaticEnabled,
                prior.LastBackupAtUtc));
            StatusMessage = "Backup settings saved. Automatic backup runs daily while PharmaBill is open.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            EncryptedBackupService.ValidatePassword(BackupPassword);
            var target = filePicker.PickExportDestination("pbbak", $"PharmaBill-backup-{DateTime.Now:yyyyMMdd-HHmm}");
            if (target is null)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>()
                .CreateBackupAsync(target, BackupPassword);
            var settings = settingsStore.Load();
            if (!string.IsNullOrWhiteSpace(settings.SecondaryFolder))
            {
                Directory.CreateDirectory(settings.SecondaryFolder);
                await using var source = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                await using var secondCopy = new FileStream(
                    Path.Combine(settings.SecondaryFolder, Path.GetFileName(target)),
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    131072,
                    true);
                await source.CopyToAsync(secondCopy);
                await secondCopy.FlushAsync();
            }

            LastBackupAtUtc = DateTime.UtcNow;
            settingsStore.Save(settings with { LastBackupAtUtc = LastBackupAtUtc });
            OnPropertyChanged(nameof(IsBackupOlderThanSevenDays));
            StatusMessage = $"Encrypted backup created: {target}";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var source = filePicker.PickBackupFile();
        if (source is null)
        {
            return;
        }

        if (!confirmationService.Confirm(
                "Restore replaces the current database and attachments. A safety backup will be taken first. PharmaBill must restart after restore. Continue?",
                "Restore encrypted backup"))
        {
            return;
        }

        try
        {
            EncryptedBackupService.ValidatePassword(BackupPassword);
            using var scope = scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>()
                .RestoreBackupAsync(source, BackupPassword);
            StatusMessage = $"Restore validated. Safety backup: {result.SafetyBackupPath}. Restarting PharmaBill.";
            ErrorMessage = string.Empty;
            await Task.Delay(500);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportEverythingAsync()
    {
        await CreateBackupAsync();
    }

    [RelayCommand]
    private async Task ImportOnNewPcAsync() => await RestoreBackupAsync();

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
