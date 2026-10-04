using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public partial class SyncSettingsPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseDeviceId _localDevice;
    private readonly CurrentSession _session;
    private readonly IFilePickerService _filePicker;
    private readonly IConfirmationService _confirmation;
    private readonly SyncFolderExchangeService _folderSync;
    private readonly CloudSyncService _cloudSync;

    public SyncSettingsPageViewModel(
        IServiceScopeFactory scopeFactory,
        DatabaseDeviceId localDevice,
        CurrentSession session,
        IFilePickerService filePicker,
        IConfirmationService confirmation,
        SyncFolderExchangeService folderSync,
        CloudSyncService cloudSync)
    {
        _scopeFactory = scopeFactory;
        _localDevice = localDevice;
        _session = session;
        _filePicker = filePicker;
        _confirmation = confirmation;
        _folderSync = folderSync;
        _folderSync.SettingsChanged += OnFolderSettingsChanged;
        _cloudSync = cloudSync;
        _cloudSync.SettingsChanged += OnCloudSettingsChanged;
    }

    public ObservableCollection<DeviceInfo> Devices { get; } = [];
    public ObservableCollection<SyncConflict> Conflicts { get; } = [];
    public ObservableCollection<FileTransferItem> FileTransfers { get; } = [];

    [ObservableProperty] private DeviceInfo? _selectedDevice;
    [ObservableProperty] private SyncConflict? _selectedConflict;
    [ObservableProperty] private bool _canAcceptRemote = true;
    [ObservableProperty] private bool _canResolveConflict = true;
    [ObservableProperty] private string _deviceName = Environment.MachineName;
    [ObservableProperty] private string _peerDeviceId = string.Empty;
    [ObservableProperty] private string _peerName = string.Empty;
    [ObservableProperty] private string _peerPlatform = "Windows";
    [ObservableProperty] private string _pairingCode = string.Empty;
    [ObservableProperty] private string _generatedPairingCode = string.Empty;
    [ObservableProperty] private string _conflictReason = string.Empty;
    [ObservableProperty] private string _packageStatus = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _syncFolderPath = SyncFolderSettings.Default.FolderPath;
    [ObservableProperty] private bool _autoSyncEnabled;
    [ObservableProperty] private string _folderSyncStatus = "Idle";
    [ObservableProperty] private string _folderSyncWarning = string.Empty;
    [ObservableProperty] private DateTime? _lastFolderSyncAtUtc;
    [ObservableProperty] private int _lastIncomingPackages;
    [ObservableProperty] private int _lastOutgoingPackages;
    [ObservableProperty] private int _lastAppliedChanges;
    [ObservableProperty] private int _pendingChangeCount;
    [ObservableProperty] private int _unresolvedConflictCount;
    [ObservableProperty] private DateTime? _lastExchangeAtUtc;
    [ObservableProperty] private string _cloudServerUrl = string.Empty;
    [ObservableProperty] private string _cloudApiKey = string.Empty;
    [ObservableProperty] private bool _cloudSyncEnabled;
    [ObservableProperty] private string _cloudSyncStatus = "Idle";
    [ObservableProperty] private string _cloudSyncWarning = string.Empty;
    [ObservableProperty] private DateTime? _lastCloudSyncAtUtc;
    [ObservableProperty] private int _lastCloudPushed;
    [ObservableProperty] private int _lastCloudPulled;

    public Guid LocalDeviceId => _localDevice.Value;

    partial void OnSelectedConflictChanged(SyncConflict? value)
    {
        CanAcceptRemote = value?.EntityName != "StockConflict";
        CanResolveConflict = value?.EntityName != "StockConflict";
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var deviceService = scope.ServiceProvider.GetRequiredService<SyncDeviceService>();
            await deviceService.InitializeLocalDeviceAsync(DeviceName, "1.0.0", cancellationToken);
            await ReloadAsync(context, scope.ServiceProvider.GetRequiredService<FileTransferQueue>(), cancellationToken);
            ApplyFolderSettings(await _folderSync.GetSettingsAsync(cancellationToken));
            ApplyCloudSettings(await _cloudSync.GetSettingsAsync(cancellationToken));
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    partial void OnAutoSyncEnabledChanged(bool value)
    {
        _ = SaveAutoSyncPreferenceAsync(value);
    }

    [RelayCommand]
    private void ChooseSyncFolder()
    {
        var path = _filePicker.PickFolder("Choose Google Drive or another synced folder");
        if (path is null)
        {
            return;
        }

        _ = SaveSyncFolderAsync(path);
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        try
        {
            await _folderSync.ConfigureAsync(SyncFolderPath, AutoSyncEnabled);
            await _folderSync.SyncNowAsync();
            ErrorMessage = string.Empty;
            PackageStatus = "Folder sync completed.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task SaveSyncFolderAsync(string path)
    {
        try
        {
            SyncFolderPath = path;
            await _folderSync.ConfigureAsync(path, AutoSyncEnabled);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task SaveAutoSyncPreferenceAsync(bool enabled)
    {
        try
        {
            await _folderSync.SetAutoSyncAsync(enabled);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            AutoSyncEnabled = !enabled;
        }
    }


    [RelayCommand]
    private async Task SaveCloudSettingsAsync()
    {
        try
        {
            await _cloudSync.ConfigureAsync(CloudServerUrl, CloudApiKey, CloudSyncEnabled);
            ErrorMessage = string.Empty;
            PackageStatus = "Cloud sync settings saved.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task CloudSyncNowAsync()
    {
        try
        {
            await _cloudSync.ConfigureAsync(CloudServerUrl, CloudApiKey, CloudSyncEnabled);
            var result = await _cloudSync.SyncNowAsync();
            ErrorMessage = string.Empty;
            PackageStatus = $"Cloud sync completed: {result.Pushed} sent, {result.Pulled} received.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnCloudSettingsChanged(CloudSyncSettings settings)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyCloudSettings(settings);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplyCloudSettings(settings));
        }
    }

    private void ApplyCloudSettings(CloudSyncSettings settings)
    {
        CloudServerUrl = settings.ServerUrl;
        CloudApiKey = settings.ApiKey;
        CloudSyncEnabled = settings.Enabled;
        CloudSyncStatus = settings.Status;
        CloudSyncWarning = settings.LastError ?? string.Empty;
        LastCloudSyncAtUtc = settings.LastPullAtUtc;
        LastCloudPushed = settings.LastPushedChanges;
        LastCloudPulled = settings.LastPulledChanges;
    }
    private void OnFolderSettingsChanged(SyncFolderSettings settings)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyFolderSettings(settings);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplyFolderSettings(settings));
        }
    }

    private void ApplyFolderSettings(SyncFolderSettings settings)
    {
        SyncFolderPath = settings.FolderPath;
        AutoSyncEnabled = settings.AutoSyncEnabled;
        FolderSyncStatus = settings.Status;
        FolderSyncWarning = settings.LastError ?? string.Empty;
        LastFolderSyncAtUtc = settings.LastSyncAtUtc;
        LastIncomingPackages = settings.LastIncomingPackages;
        LastOutgoingPackages = settings.LastOutgoingPackages;
        LastAppliedChanges = settings.LastAppliedChanges;
    }

    partial void OnDeviceNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = UpdateLocalNameAsync(value);
        }
    }

    [RelayCommand]
    private void GeneratePairingCode()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var code = scope.ServiceProvider.GetRequiredService<SyncDeviceService>().CreatePairingCode();
            GeneratedPairingCode = code.Code;
            PackageStatus = $"One-time pairing code; expires {code.ExpiresAtUtc.ToLocalTime():g}. Share it with the other device out of band.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task PairDeviceAsync()
    {
        if (!Guid.TryParse(PeerDeviceId, out var peerId))
        {
            ErrorMessage = "Enter the other device's ID.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SyncDeviceService>().PairDeviceAsync(
                peerId, PeerName, PeerPlatform, PairingCode);
            PairingCode = string.Empty;
            GeneratedPairingCode = string.Empty;
            PackageStatus = $"Paired with {PeerName}. Pair the same one-time code on both devices.";
            ErrorMessage = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RevokeDeviceAsync()
    {
        if (SelectedDevice is null || SelectedDevice.IsCurrentDevice)
        {
            ErrorMessage = "Select a paired peer device to revoke.";
            return;
        }

        if (!_confirmation.Confirm(
                $"Revoke {SelectedDevice.DeviceName}? Packages signed with its device key will no longer be accepted.",
                "Revoke paired device"))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SyncDeviceService>()
                .RevokeDeviceAsync(SelectedDevice.Id);
            await LoadAsync();
            PackageStatus = "Paired device revoked.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportPackageAsync()
    {
        if (SelectedDevice is null || SelectedDevice.IsCurrentDevice)
        {
            ErrorMessage = "Select the paired destination device.";
            return;
        }

        var destination = _filePicker.PickExportDestination(
            "pbsync",
            $"PharmaBill-sync-{DateTime.Now:yyyyMMdd-HHmm}");
        if (destination is null)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var count = await scope.ServiceProvider.GetRequiredService<SyncPackageService>()
                .ExportAsync(SelectedDevice.Id, destination);
            PackageStatus = $"Exported {count} change(s) to signed package {destination}.";
            ErrorMessage = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ImportPackageAsync()
    {
        var source = _filePicker.PickSyncPackage();
        if (source is null)
        {
            return;
        }

        if (!_confirmation.Confirm(
                "Verify and import all changes in this signed package? Changes are applied in one database transaction.",
                "Import sync package"))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<SyncPackageService>().ImportAsync(source);
            PackageStatus = $"Import complete: {result.Applied} applied, {result.Duplicates} already seen, " +
                            $"{result.Conflicts} data conflict(s), {result.StockConflicts} negative-stock alert(s).";
            ErrorMessage = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task KeepLocalAsync() => await ResolveConflictAsync(acceptRemote: false);

    [RelayCommand]
    private async Task AcceptRemoteAsync() => await ResolveConflictAsync(acceptRemote: true);

    [RelayCommand]
    private async Task ResolveConflictAsync(bool acceptRemote)
    {
        if (SelectedConflict is null || string.IsNullOrWhiteSpace(ConflictReason))
        {
            ErrorMessage = "Select a conflict and enter a reason for its resolution.";
            return;
        }

        var choice = acceptRemote ? "accept the remote version" : "keep the local version";
        if (!_confirmation.Confirm(
                $"This records an audited resolution ({choice}) for conflict {SelectedConflict.Id:D}. Continue?",
                "Resolve sync conflict"))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ChangeApplier>().ResolveConflictAsync(
                SelectedConflict.Id,
                acceptRemote,
                ConflictReason,
                _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before resolving sync conflicts."));
            ConflictReason = string.Empty;
            PackageStatus = "Conflict resolution recorded.";
            ErrorMessage = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task UpdateLocalNameAsync(string name)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SyncDeviceService>()
                .InitializeLocalDeviceAsync(name, "1.0.0");
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task ReloadAsync(
        PharmaBillDbContext context,
        FileTransferQueue fileQueue,
        CancellationToken cancellationToken)
    {
        var devices = await context.DeviceInfos.AsNoTracking()
            .OrderByDescending(item => item.IsCurrentDevice)
            .ThenBy(item => item.DeviceName)
            .ToListAsync(cancellationToken);
        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        PendingChangeCount = await context.ChangeLogs.AsNoTracking()
            .CountAsync(item => item.SyncState == SyncState.Pending, cancellationToken);
        var conflicts = await context.SyncConflicts.AsNoTracking()
            .Where(item => item.Resolution == null || item.Resolution == "Open")
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
        Conflicts.Clear();
        foreach (var conflict in conflicts)
        {
            Conflicts.Add(conflict);
        }

        UnresolvedConflictCount = conflicts.Count;
        LastExchangeAtUtc = devices.Where(item => item.LastSyncAtUtc.HasValue)
            .Select(item => item.LastSyncAtUtc)
            .Max();
        FileTransfers.Clear();
        foreach (var transfer in await fileQueue.GetAllAsync(cancellationToken))
        {
            FileTransfers.Add(transfer);
        }
    }
}

