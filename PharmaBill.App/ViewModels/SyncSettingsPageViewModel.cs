using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public class SyncSettingsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly DatabaseDeviceId _localDevice;

	private readonly CurrentSession _session;

	private readonly IFilePickerService _filePicker;

	private readonly IConfirmationService _confirmation;

	private readonly SyncFolderExchangeService _folderSync;

	private readonly CloudSyncService _cloudSync;

	private readonly LocalSyncServer _localSync;

	private readonly ZeroConfigSyncCoordinator _zeroConfig;

	private readonly GoogleDriveSyncService _googleDrive;

	private readonly CloudOAuthClientStore _oauthClients;

	private readonly BranchNetworkSyncService _branchNetwork;

	private readonly CloudAutoBackupPreferencesStore _autoBackupPreferences;

	private readonly UsbBackupService _usbBackup;

	private readonly UsbBackupSettingsStore _usbBackupSettings;

	private bool _suppressAutoBackupPreferenceSave;

	private DeviceInfo? _selectedDevice;

	private SyncConflict? _selectedConflict;

	private bool _canAcceptRemote = true;

	private bool _canResolveConflict = true;

	private string _deviceName = Environment.MachineName;

	private string _peerDeviceId = string.Empty;

	private string _peerName = string.Empty;

	private string _peerPlatform = "Windows";

	private string _pairingCode = string.Empty;

	private string _generatedPairingCode = string.Empty;

	private string _conflictReason = string.Empty;

	private string _packageStatus = string.Empty;

	private string _errorMessage = string.Empty;

	private string _syncFolderPath = SyncFolderSettings.Default.FolderPath;

	private bool _autoSyncEnabled;

	private string _folderSyncStatus = "Idle";

	private string _folderSyncWarning = string.Empty;

	private DateTime? _lastFolderSyncAtUtc;

	private int _lastIncomingPackages;

	private int _lastOutgoingPackages;

	private int _lastAppliedChanges;

	private int _pendingChangeCount;

	private int _unresolvedConflictCount;

	private DateTime? _lastExchangeAtUtc;

	private string _cloudServerUrl = string.Empty;

	private string _cloudApiKey = string.Empty;

	private bool _cloudSyncEnabled;

	private string _cloudSyncStatus = "Idle";

	private string _cloudSyncWarning = string.Empty;

	private DateTime? _lastCloudSyncAtUtc;

	private int _lastCloudPushed;

	private int _lastCloudPulled;

	private LocalSyncState _lanSyncState;

	private string _lanStatusMessage = "Sync station is stopped";

	private string _lanStatusDetail = string.Empty;

	private string _wifiIpAddress = "Not on Wi-Fi";

	private int _lanPort = 5055;

	private ImageSource? _pairingQrCode;

	private string _pairingPayload = string.Empty;

	private string _firewallRuleStatus = string.Empty;

	private bool _firewallRulePresent;

	private string _discoveryStatus = string.Empty;

	private bool _cloudFallbackActive;

	private string _googleDriveEmail = string.Empty;

	private bool _googleDriveConnected;

	private string _googleDriveStatus = "Not Connected";

	private string _googleDriveWarning = string.Empty;

	private DateTime? _lastGoogleDriveSyncAtUtc;

	private bool _googleDriveSyncOnExit;

	private bool _googleDriveSyncOnBillSave;

	private bool _isGoogleDriveBusy;

	private bool _isGoogleDriveUploading;

	private bool _isGoogleDriveUploadFailed;

	private double _googleDriveUploadPercent;

	private string _googleDriveUploadStatus = string.Empty;

	private string _googleDriveBytesLabel = string.Empty;

	private bool _autoBackupGoogleDrive;

	private string _branchNetworkStatus = string.Empty;

	private string _cloudTestStatus = string.Empty;

	private bool _isCloudTestBusy;

	private RemovableDriveOption? _selectedRemovableDrive;

	private DateTime? _lastUsbBackupAtUtc;

	private string _usbBackupStatus = string.Empty;

	private bool _isUsbBackupBusy;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? refreshRemovableDrivesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? backupToUsbCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? publishBranchSnapshotCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? aggregatePeerBranchesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? chooseSyncFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? syncNowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveCloudSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? cloudSyncNowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? configureCloudOAuthKeysCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? connectGoogleDriveCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? disconnectGoogleDriveCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? uploadGoogleDriveBackupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? retryGoogleDriveUploadCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? downloadGoogleDriveBackupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? testGoogleDriveConnectionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? testCloudSyncNowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? refreshLanStationCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? rotateLanPairingSecretCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? checkAllowFirewallRuleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? generatePairingCodeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? pairDeviceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? revokeDeviceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportPackageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? importPackageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? keepLocalCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? acceptRemoteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<bool>? resolveConflictCommand;

	public ObservableCollection<DeviceInfo> Devices { get; } = new ObservableCollection<DeviceInfo>();

	public ObservableCollection<SyncConflict> Conflicts { get; } = new ObservableCollection<SyncConflict>();

	public ObservableCollection<FileTransferItem> FileTransfers { get; } = new ObservableCollection<FileTransferItem>();

	public ObservableCollection<RemovableDriveOption> RemovableDrives { get; } = new ObservableCollection<RemovableDriveOption>();

	public string GoogleDriveSyncFolder => "PharmaBill_Sync/";

	public string GoogleDriveConnectionLabel
	{
		get
		{
			if (!GoogleDriveConnected)
			{
				return "Not Connected";
			}
			if (!string.IsNullOrWhiteSpace(GoogleDriveEmail))
			{
				return "Connected: " + GoogleDriveEmail;
			}
			return "Connected";
		}
	}

	public bool ShowGoogleDriveIdleBadge
	{
		get
		{
			if (GoogleDriveConnected && !IsGoogleDriveUploading)
			{
				return !IsGoogleDriveUploadFailed;
			}
			return false;
		}
	}

	public string GoogleDriveIdleBadge
	{
		get
		{
			DateTime? lastGoogleDriveSyncAtUtc = LastGoogleDriveSyncAtUtc;
			if (lastGoogleDriveSyncAtUtc.HasValue)
			{
				DateTime valueOrDefault = lastGoogleDriveSyncAtUtc.GetValueOrDefault();
				return $"Up to date • Last synced: {valueOrDefault.ToLocalTime():dd-MMM HH:mm}";
			}
			return "Connected • Waiting for first sync";
		}
	}

	public string LastUsbBackupLabel
	{
		get
		{
			DateTime? lastUsbBackupAtUtc = LastUsbBackupAtUtc;
			if (lastUsbBackupAtUtc.HasValue)
			{
				DateTime valueOrDefault = lastUsbBackupAtUtc.GetValueOrDefault();
				return $"Last USB Backup: {valueOrDefault.ToLocalTime():dd-MMM-yyyy HH:mm}";
			}
			return "Last USB Backup: Never";
		}
	}

	public Guid LocalDeviceId => _localDevice.Value;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DeviceInfo? SelectedDevice
	{
		get
		{
			return _selectedDevice;
		}
		set
		{
			if (!EqualityComparer<DeviceInfo>.Default.Equals(_selectedDevice, value))
			{
				OnPropertyChanging(nameof(SelectedDevice));
				_selectedDevice = value;
				OnPropertyChanged(nameof(SelectedDevice));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public SyncConflict? SelectedConflict
	{
		get
		{
			return _selectedConflict;
		}
		set
		{
			if (!EqualityComparer<SyncConflict>.Default.Equals(_selectedConflict, value))
			{
				OnPropertyChanging(nameof(SelectedConflict));
				_selectedConflict = value;
				OnSelectedConflictChanged(value);
				OnPropertyChanged(nameof(SelectedConflict));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CanAcceptRemote
	{
		get
		{
			return _canAcceptRemote;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_canAcceptRemote, value))
			{
				OnPropertyChanging(nameof(CanAcceptRemote));
				_canAcceptRemote = value;
				OnPropertyChanged(nameof(CanAcceptRemote));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CanResolveConflict
	{
		get
		{
			return _canResolveConflict;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_canResolveConflict, value))
			{
				OnPropertyChanging(nameof(CanResolveConflict));
				_canResolveConflict = value;
				OnPropertyChanged(nameof(CanResolveConflict));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DeviceName
	{
		get
		{
			return _deviceName;
		}
		[MemberNotNull("_deviceName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_deviceName, value))
			{
				OnPropertyChanging(nameof(DeviceName));
				_deviceName = value;
				OnDeviceNameChanged(value);
				OnPropertyChanged(nameof(DeviceName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PeerDeviceId
	{
		get
		{
			return _peerDeviceId;
		}
		[MemberNotNull("_peerDeviceId")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_peerDeviceId, value))
			{
				OnPropertyChanging(nameof(PeerDeviceId));
				_peerDeviceId = value;
				OnPropertyChanged(nameof(PeerDeviceId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PeerName
	{
		get
		{
			return _peerName;
		}
		[MemberNotNull("_peerName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_peerName, value))
			{
				OnPropertyChanging(nameof(PeerName));
				_peerName = value;
				OnPropertyChanged(nameof(PeerName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PeerPlatform
	{
		get
		{
			return _peerPlatform;
		}
		[MemberNotNull("_peerPlatform")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_peerPlatform, value))
			{
				OnPropertyChanging(nameof(PeerPlatform));
				_peerPlatform = value;
				OnPropertyChanged(nameof(PeerPlatform));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PairingCode
	{
		get
		{
			return _pairingCode;
		}
		[MemberNotNull("_pairingCode")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pairingCode, value))
			{
				OnPropertyChanging(nameof(PairingCode));
				_pairingCode = value;
				OnPropertyChanged(nameof(PairingCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GeneratedPairingCode
	{
		get
		{
			return _generatedPairingCode;
		}
		[MemberNotNull("_generatedPairingCode")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_generatedPairingCode, value))
			{
				OnPropertyChanging(nameof(GeneratedPairingCode));
				_generatedPairingCode = value;
				OnPropertyChanged(nameof(GeneratedPairingCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ConflictReason
	{
		get
		{
			return _conflictReason;
		}
		[MemberNotNull("_conflictReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_conflictReason, value))
			{
				OnPropertyChanging(nameof(ConflictReason));
				_conflictReason = value;
				OnPropertyChanged(nameof(ConflictReason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PackageStatus
	{
		get
		{
			return _packageStatus;
		}
		[MemberNotNull("_packageStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_packageStatus, value))
			{
				OnPropertyChanging(nameof(PackageStatus));
				_packageStatus = value;
				OnPropertyChanged(nameof(PackageStatus));
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
	public string SyncFolderPath
	{
		get
		{
			return _syncFolderPath;
		}
		[MemberNotNull("_syncFolderPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_syncFolderPath, value))
			{
				OnPropertyChanging(nameof(SyncFolderPath));
				_syncFolderPath = value;
				OnPropertyChanged(nameof(SyncFolderPath));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AutoSyncEnabled
	{
		get
		{
			return _autoSyncEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_autoSyncEnabled, value))
			{
				OnPropertyChanging(nameof(AutoSyncEnabled));
				_autoSyncEnabled = value;
				OnAutoSyncEnabledChanged(value);
				OnPropertyChanged(nameof(AutoSyncEnabled));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FolderSyncStatus
	{
		get
		{
			return _folderSyncStatus;
		}
		[MemberNotNull("_folderSyncStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_folderSyncStatus, value))
			{
				OnPropertyChanging(nameof(FolderSyncStatus));
				_folderSyncStatus = value;
				OnPropertyChanged(nameof(FolderSyncStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FolderSyncWarning
	{
		get
		{
			return _folderSyncWarning;
		}
		[MemberNotNull("_folderSyncWarning")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_folderSyncWarning, value))
			{
				OnPropertyChanging(nameof(FolderSyncWarning));
				_folderSyncWarning = value;
				OnPropertyChanged(nameof(FolderSyncWarning));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastFolderSyncAtUtc
	{
		get
		{
			return _lastFolderSyncAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastFolderSyncAtUtc, value))
			{
				OnPropertyChanging(nameof(LastFolderSyncAtUtc));
				_lastFolderSyncAtUtc = value;
				OnPropertyChanged(nameof(LastFolderSyncAtUtc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LastIncomingPackages
	{
		get
		{
			return _lastIncomingPackages;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lastIncomingPackages, value))
			{
				OnPropertyChanging(nameof(LastIncomingPackages));
				_lastIncomingPackages = value;
				OnPropertyChanged(nameof(LastIncomingPackages));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LastOutgoingPackages
	{
		get
		{
			return _lastOutgoingPackages;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lastOutgoingPackages, value))
			{
				OnPropertyChanging(nameof(LastOutgoingPackages));
				_lastOutgoingPackages = value;
				OnPropertyChanged(nameof(LastOutgoingPackages));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LastAppliedChanges
	{
		get
		{
			return _lastAppliedChanges;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lastAppliedChanges, value))
			{
				OnPropertyChanging(nameof(LastAppliedChanges));
				_lastAppliedChanges = value;
				OnPropertyChanged(nameof(LastAppliedChanges));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int PendingChangeCount
	{
		get
		{
			return _pendingChangeCount;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_pendingChangeCount, value))
			{
				OnPropertyChanging(nameof(PendingChangeCount));
				_pendingChangeCount = value;
				OnPropertyChanged(nameof(PendingChangeCount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int UnresolvedConflictCount
	{
		get
		{
			return _unresolvedConflictCount;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_unresolvedConflictCount, value))
			{
				OnPropertyChanging(nameof(UnresolvedConflictCount));
				_unresolvedConflictCount = value;
				OnPropertyChanged(nameof(UnresolvedConflictCount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastExchangeAtUtc
	{
		get
		{
			return _lastExchangeAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastExchangeAtUtc, value))
			{
				OnPropertyChanging(nameof(LastExchangeAtUtc));
				_lastExchangeAtUtc = value;
				OnPropertyChanged(nameof(LastExchangeAtUtc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CloudServerUrl
	{
		get
		{
			return _cloudServerUrl;
		}
		[MemberNotNull("_cloudServerUrl")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cloudServerUrl, value))
			{
				OnPropertyChanging(nameof(CloudServerUrl));
				_cloudServerUrl = value;
				OnPropertyChanged(nameof(CloudServerUrl));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CloudApiKey
	{
		get
		{
			return _cloudApiKey;
		}
		[MemberNotNull("_cloudApiKey")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cloudApiKey, value))
			{
				OnPropertyChanging(nameof(CloudApiKey));
				_cloudApiKey = value;
				OnPropertyChanged(nameof(CloudApiKey));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CloudSyncEnabled
	{
		get
		{
			return _cloudSyncEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_cloudSyncEnabled, value))
			{
				OnPropertyChanging(nameof(CloudSyncEnabled));
				_cloudSyncEnabled = value;
				OnPropertyChanged(nameof(CloudSyncEnabled));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CloudSyncStatus
	{
		get
		{
			return _cloudSyncStatus;
		}
		[MemberNotNull("_cloudSyncStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cloudSyncStatus, value))
			{
				OnPropertyChanging(nameof(CloudSyncStatus));
				_cloudSyncStatus = value;
				OnPropertyChanged(nameof(CloudSyncStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CloudSyncWarning
	{
		get
		{
			return _cloudSyncWarning;
		}
		[MemberNotNull("_cloudSyncWarning")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cloudSyncWarning, value))
			{
				OnPropertyChanging(nameof(CloudSyncWarning));
				_cloudSyncWarning = value;
				OnPropertyChanged(nameof(CloudSyncWarning));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastCloudSyncAtUtc
	{
		get
		{
			return _lastCloudSyncAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastCloudSyncAtUtc, value))
			{
				OnPropertyChanging(nameof(LastCloudSyncAtUtc));
				_lastCloudSyncAtUtc = value;
				OnPropertyChanged(nameof(LastCloudSyncAtUtc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LastCloudPushed
	{
		get
		{
			return _lastCloudPushed;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lastCloudPushed, value))
			{
				OnPropertyChanging(nameof(LastCloudPushed));
				_lastCloudPushed = value;
				OnPropertyChanged(nameof(LastCloudPushed));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LastCloudPulled
	{
		get
		{
			return _lastCloudPulled;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lastCloudPulled, value))
			{
				OnPropertyChanging(nameof(LastCloudPulled));
				_lastCloudPulled = value;
				OnPropertyChanged(nameof(LastCloudPulled));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public LocalSyncState LanSyncState
	{
		get
		{
			return _lanSyncState;
		}
		set
		{
			if (!EqualityComparer<LocalSyncState>.Default.Equals(_lanSyncState, value))
			{
				OnPropertyChanging(nameof(LanSyncState));
				_lanSyncState = value;
				OnPropertyChanged(nameof(LanSyncState));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LanStatusMessage
	{
		get
		{
			return _lanStatusMessage;
		}
		[MemberNotNull("_lanStatusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_lanStatusMessage, value))
			{
				OnPropertyChanging(nameof(LanStatusMessage));
				_lanStatusMessage = value;
				OnPropertyChanged(nameof(LanStatusMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LanStatusDetail
	{
		get
		{
			return _lanStatusDetail;
		}
		[MemberNotNull("_lanStatusDetail")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_lanStatusDetail, value))
			{
				OnPropertyChanging(nameof(LanStatusDetail));
				_lanStatusDetail = value;
				OnPropertyChanged(nameof(LanStatusDetail));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WifiIpAddress
	{
		get
		{
			return _wifiIpAddress;
		}
		[MemberNotNull("_wifiIpAddress")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_wifiIpAddress, value))
			{
				OnPropertyChanging(nameof(WifiIpAddress));
				_wifiIpAddress = value;
				OnPropertyChanged(nameof(WifiIpAddress));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LanPort
	{
		get
		{
			return _lanPort;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lanPort, value))
			{
				OnPropertyChanging(nameof(LanPort));
				_lanPort = value;
				OnPropertyChanged(nameof(LanPort));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ImageSource? PairingQrCode
	{
		get
		{
			return _pairingQrCode;
		}
		set
		{
			if (!EqualityComparer<ImageSource>.Default.Equals(_pairingQrCode, value))
			{
				OnPropertyChanging(nameof(PairingQrCode));
				_pairingQrCode = value;
				OnPropertyChanged(nameof(PairingQrCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PairingPayload
	{
		get
		{
			return _pairingPayload;
		}
		[MemberNotNull("_pairingPayload")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pairingPayload, value))
			{
				OnPropertyChanging(nameof(PairingPayload));
				_pairingPayload = value;
				OnPropertyChanged(nameof(PairingPayload));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FirewallRuleStatus
	{
		get
		{
			return _firewallRuleStatus;
		}
		[MemberNotNull("_firewallRuleStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_firewallRuleStatus, value))
			{
				OnPropertyChanging(nameof(FirewallRuleStatus));
				_firewallRuleStatus = value;
				OnPropertyChanged(nameof(FirewallRuleStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool FirewallRulePresent
	{
		get
		{
			return _firewallRulePresent;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_firewallRulePresent, value))
			{
				OnPropertyChanging(nameof(FirewallRulePresent));
				_firewallRulePresent = value;
				OnPropertyChanged(nameof(FirewallRulePresent));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DiscoveryStatus
	{
		get
		{
			return _discoveryStatus;
		}
		[MemberNotNull("_discoveryStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_discoveryStatus, value))
			{
				OnPropertyChanging(nameof(DiscoveryStatus));
				_discoveryStatus = value;
				OnPropertyChanged(nameof(DiscoveryStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CloudFallbackActive
	{
		get
		{
			return _cloudFallbackActive;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_cloudFallbackActive, value))
			{
				OnPropertyChanging(nameof(CloudFallbackActive));
				_cloudFallbackActive = value;
				OnPropertyChanged(nameof(CloudFallbackActive));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GoogleDriveEmail
	{
		get
		{
			return _googleDriveEmail;
		}
		[MemberNotNull("_googleDriveEmail")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_googleDriveEmail, value))
			{
				OnPropertyChanging(nameof(GoogleDriveEmail));
				_googleDriveEmail = value;
				OnPropertyChanged(nameof(GoogleDriveEmail));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool GoogleDriveConnected
	{
		get
		{
			return _googleDriveConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_googleDriveConnected, value))
			{
				OnPropertyChanging(nameof(GoogleDriveConnected));
				_googleDriveConnected = value;
				OnGoogleDriveConnectedChanged(value);
				OnPropertyChanged(nameof(GoogleDriveConnected));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GoogleDriveStatus
	{
		get
		{
			return _googleDriveStatus;
		}
		[MemberNotNull("_googleDriveStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_googleDriveStatus, value))
			{
				OnPropertyChanging(nameof(GoogleDriveStatus));
				_googleDriveStatus = value;
				OnPropertyChanged(nameof(GoogleDriveStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GoogleDriveWarning
	{
		get
		{
			return _googleDriveWarning;
		}
		[MemberNotNull("_googleDriveWarning")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_googleDriveWarning, value))
			{
				OnPropertyChanging(nameof(GoogleDriveWarning));
				_googleDriveWarning = value;
				OnPropertyChanged(nameof(GoogleDriveWarning));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastGoogleDriveSyncAtUtc
	{
		get
		{
			return _lastGoogleDriveSyncAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastGoogleDriveSyncAtUtc, value))
			{
				OnPropertyChanging(nameof(LastGoogleDriveSyncAtUtc));
				_lastGoogleDriveSyncAtUtc = value;
				OnLastGoogleDriveSyncAtUtcChanged(value);
				OnPropertyChanged(nameof(LastGoogleDriveSyncAtUtc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool GoogleDriveSyncOnExit
	{
		get
		{
			return _googleDriveSyncOnExit;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_googleDriveSyncOnExit, value))
			{
				OnPropertyChanging(nameof(GoogleDriveSyncOnExit));
				_googleDriveSyncOnExit = value;
				OnGoogleDriveSyncOnExitChanged(value);
				OnPropertyChanged(nameof(GoogleDriveSyncOnExit));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool GoogleDriveSyncOnBillSave
	{
		get
		{
			return _googleDriveSyncOnBillSave;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_googleDriveSyncOnBillSave, value))
			{
				OnPropertyChanging(nameof(GoogleDriveSyncOnBillSave));
				_googleDriveSyncOnBillSave = value;
				OnGoogleDriveSyncOnBillSaveChanged(value);
				OnPropertyChanged(nameof(GoogleDriveSyncOnBillSave));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsGoogleDriveBusy
	{
		get
		{
			return _isGoogleDriveBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isGoogleDriveBusy, value))
			{
				OnPropertyChanging(nameof(IsGoogleDriveBusy));
				_isGoogleDriveBusy = value;
				OnPropertyChanged(nameof(IsGoogleDriveBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsGoogleDriveUploading
	{
		get
		{
			return _isGoogleDriveUploading;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isGoogleDriveUploading, value))
			{
				OnPropertyChanging(nameof(IsGoogleDriveUploading));
				_isGoogleDriveUploading = value;
				OnIsGoogleDriveUploadingChanged(value);
				OnPropertyChanged(nameof(IsGoogleDriveUploading));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsGoogleDriveUploadFailed
	{
		get
		{
			return _isGoogleDriveUploadFailed;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isGoogleDriveUploadFailed, value))
			{
				OnPropertyChanging(nameof(IsGoogleDriveUploadFailed));
				_isGoogleDriveUploadFailed = value;
				OnIsGoogleDriveUploadFailedChanged(value);
				OnPropertyChanged(nameof(IsGoogleDriveUploadFailed));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double GoogleDriveUploadPercent
	{
		get
		{
			return _googleDriveUploadPercent;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(_googleDriveUploadPercent, value))
			{
				OnPropertyChanging(nameof(GoogleDriveUploadPercent));
				_googleDriveUploadPercent = value;
				OnPropertyChanged(nameof(GoogleDriveUploadPercent));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GoogleDriveUploadStatus
	{
		get
		{
			return _googleDriveUploadStatus;
		}
		[MemberNotNull("_googleDriveUploadStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_googleDriveUploadStatus, value))
			{
				OnPropertyChanging(nameof(GoogleDriveUploadStatus));
				_googleDriveUploadStatus = value;
				OnPropertyChanged(nameof(GoogleDriveUploadStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GoogleDriveBytesLabel
	{
		get
		{
			return _googleDriveBytesLabel;
		}
		[MemberNotNull("_googleDriveBytesLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_googleDriveBytesLabel, value))
			{
				OnPropertyChanging(nameof(GoogleDriveBytesLabel));
				_googleDriveBytesLabel = value;
				OnPropertyChanged(nameof(GoogleDriveBytesLabel));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AutoBackupGoogleDrive
	{
		get
		{
			return _autoBackupGoogleDrive;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_autoBackupGoogleDrive, value))
			{
				OnPropertyChanging(nameof(AutoBackupGoogleDrive));
				_autoBackupGoogleDrive = value;
				OnPropertyChanged(nameof(AutoBackupGoogleDrive));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchNetworkStatus
	{
		get
		{
			return _branchNetworkStatus;
		}
		[MemberNotNull("_branchNetworkStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchNetworkStatus, value))
			{
				OnPropertyChanging(nameof(BranchNetworkStatus));
				_branchNetworkStatus = value;
				OnPropertyChanged(nameof(BranchNetworkStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CloudTestStatus
	{
		get
		{
			return _cloudTestStatus;
		}
		[MemberNotNull("_cloudTestStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cloudTestStatus, value))
			{
				OnPropertyChanging(nameof(CloudTestStatus));
				_cloudTestStatus = value;
				OnPropertyChanged(nameof(CloudTestStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCloudTestBusy
	{
		get
		{
			return _isCloudTestBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isCloudTestBusy, value))
			{
				OnPropertyChanging(nameof(IsCloudTestBusy));
				_isCloudTestBusy = value;
				OnPropertyChanged(nameof(IsCloudTestBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RemovableDriveOption? SelectedRemovableDrive
	{
		get
		{
			return _selectedRemovableDrive;
		}
		set
		{
			if (!EqualityComparer<RemovableDriveOption>.Default.Equals(_selectedRemovableDrive, value))
			{
				OnPropertyChanging(nameof(SelectedRemovableDrive));
				_selectedRemovableDrive = value;
				OnPropertyChanged(nameof(SelectedRemovableDrive));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastUsbBackupAtUtc
	{
		get
		{
			return _lastUsbBackupAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastUsbBackupAtUtc, value))
			{
				OnPropertyChanging(nameof(LastUsbBackupAtUtc));
				_lastUsbBackupAtUtc = value;
				OnLastUsbBackupAtUtcChanged(value);
				OnPropertyChanged(nameof(LastUsbBackupAtUtc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UsbBackupStatus
	{
		get
		{
			return _usbBackupStatus;
		}
		[MemberNotNull("_usbBackupStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_usbBackupStatus, value))
			{
				OnPropertyChanging(nameof(UsbBackupStatus));
				_usbBackupStatus = value;
				OnPropertyChanged(nameof(UsbBackupStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsUsbBackupBusy
	{
		get
		{
			return _isUsbBackupBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isUsbBackupBusy, value))
			{
				OnPropertyChanging(nameof(IsUsbBackupBusy));
				_isUsbBackupBusy = value;
				OnPropertyChanged(nameof(IsUsbBackupBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RefreshRemovableDrivesCommand => refreshRemovableDrivesCommand ?? (refreshRemovableDrivesCommand = new RelayCommand(RefreshRemovableDrives));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand BackupToUsbCommand => backupToUsbCommand ?? (backupToUsbCommand = new AsyncRelayCommand(BackupToUsbAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PublishBranchSnapshotCommand => publishBranchSnapshotCommand ?? (publishBranchSnapshotCommand = new AsyncRelayCommand(PublishBranchSnapshotAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AggregatePeerBranchesCommand => aggregatePeerBranchesCommand ?? (aggregatePeerBranchesCommand = new AsyncRelayCommand(AggregatePeerBranchesAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ChooseSyncFolderCommand => chooseSyncFolderCommand ?? (chooseSyncFolderCommand = new RelayCommand(ChooseSyncFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SyncNowCommand => syncNowCommand ?? (syncNowCommand = new AsyncRelayCommand(SyncNowAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCloudSettingsCommand => saveCloudSettingsCommand ?? (saveCloudSettingsCommand = new AsyncRelayCommand(SaveCloudSettingsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CloudSyncNowCommand => cloudSyncNowCommand ?? (cloudSyncNowCommand = new AsyncRelayCommand(CloudSyncNowAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ConfigureCloudOAuthKeysCommand => configureCloudOAuthKeysCommand ?? (configureCloudOAuthKeysCommand = new RelayCommand(ConfigureCloudOAuthKeys));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ConnectGoogleDriveCommand => connectGoogleDriveCommand ?? (connectGoogleDriveCommand = new AsyncRelayCommand(ConnectGoogleDriveAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand DisconnectGoogleDriveCommand => disconnectGoogleDriveCommand ?? (disconnectGoogleDriveCommand = new AsyncRelayCommand(DisconnectGoogleDriveAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand UploadGoogleDriveBackupCommand => uploadGoogleDriveBackupCommand ?? (uploadGoogleDriveBackupCommand = new AsyncRelayCommand(UploadGoogleDriveBackupAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RetryGoogleDriveUploadCommand => retryGoogleDriveUploadCommand ?? (retryGoogleDriveUploadCommand = new AsyncRelayCommand(RetryGoogleDriveUploadAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand DownloadGoogleDriveBackupCommand => downloadGoogleDriveBackupCommand ?? (downloadGoogleDriveBackupCommand = new AsyncRelayCommand(DownloadGoogleDriveBackupAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand TestGoogleDriveConnectionCommand => testGoogleDriveConnectionCommand ?? (testGoogleDriveConnectionCommand = new AsyncRelayCommand(TestGoogleDriveConnectionAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand TestCloudSyncNowCommand => testCloudSyncNowCommand ?? (testCloudSyncNowCommand = new AsyncRelayCommand(TestCloudSyncNowAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RefreshLanStationCommand => refreshLanStationCommand ?? (refreshLanStationCommand = new RelayCommand(RefreshLanStation));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RotateLanPairingSecretCommand => rotateLanPairingSecretCommand ?? (rotateLanPairingSecretCommand = new RelayCommand(RotateLanPairingSecret));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CheckAllowFirewallRuleCommand => checkAllowFirewallRuleCommand ?? (checkAllowFirewallRuleCommand = new AsyncRelayCommand(CheckAllowFirewallRuleAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand GeneratePairingCodeCommand => generatePairingCodeCommand ?? (generatePairingCodeCommand = new RelayCommand(GeneratePairingCode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PairDeviceCommand => pairDeviceCommand ?? (pairDeviceCommand = new AsyncRelayCommand(PairDeviceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RevokeDeviceCommand => revokeDeviceCommand ?? (revokeDeviceCommand = new AsyncRelayCommand(RevokeDeviceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportPackageCommand => exportPackageCommand ?? (exportPackageCommand = new AsyncRelayCommand(ExportPackageAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportPackageCommand => importPackageCommand ?? (importPackageCommand = new AsyncRelayCommand(ImportPackageAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand KeepLocalCommand => keepLocalCommand ?? (keepLocalCommand = new AsyncRelayCommand(KeepLocalAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AcceptRemoteCommand => acceptRemoteCommand ?? (acceptRemoteCommand = new AsyncRelayCommand(AcceptRemoteAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<bool> ResolveConflictCommand => resolveConflictCommand ?? (resolveConflictCommand = new AsyncRelayCommand<bool>(ResolveConflictAsync));

	public SyncSettingsPageViewModel(IServiceScopeFactory scopeFactory, DatabaseDeviceId localDevice, CurrentSession session, IFilePickerService filePicker, IConfirmationService confirmation, SyncFolderExchangeService folderSync, CloudSyncService cloudSync, LocalSyncServer localSync, ZeroConfigSyncCoordinator zeroConfig, GoogleDriveSyncService googleDrive, CloudOAuthClientStore oauthClients, BranchNetworkSyncService branchNetwork, CloudAutoBackupPreferencesStore autoBackupPreferences, UsbBackupService usbBackup, UsbBackupSettingsStore usbBackupSettings)
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
		_localSync = localSync;
		_zeroConfig = zeroConfig;
		_localSync.StatusChanged += OnLocalSyncStatusChanged;
		_zeroConfig.StateChanged += OnZeroConfigStateChanged;
		_googleDrive = googleDrive;
		_googleDrive.SettingsChanged += OnGoogleDriveSettingsChanged;
		_googleDrive.ProgressChanged += OnGoogleDriveProgress;
		_oauthClients = oauthClients;
		_branchNetwork = branchNetwork;
		_autoBackupPreferences = autoBackupPreferences;
		_usbBackup = usbBackup;
		_usbBackupSettings = usbBackupSettings;
		_usbBackupSettings.SettingsChanged += OnUsbBackupSettingsChanged;
		ApplyLanStatus(_localSync.Status);
		RefreshLanStation();
		ApplyAutoBackupPreferences(_autoBackupPreferences.Load());
		ApplyUsbBackupSettings(_usbBackupSettings.Load());
		RefreshRemovableDrives();
	}

	private void NotifyGoogleDriveCardState()
	{
		OnPropertyChanged("ShowGoogleDriveIdleBadge");
		OnPropertyChanged("GoogleDriveIdleBadge");
	}

	private void RefreshRemovableDrives()
	{
		string previousRoot = SelectedRemovableDrive?.Root;
		RemovableDrives.Clear();
		foreach (RemovableDriveOption item in _usbBackup.ListRemovableDrives())
		{
			RemovableDrives.Add(item);
		}
		SelectedRemovableDrive = RemovableDrives.FirstOrDefault((RemovableDriveOption drive) => string.Equals(drive.Root, previousRoot, StringComparison.OrdinalIgnoreCase)) ?? RemovableDrives.FirstOrDefault();
		UsbBackupStatus = ((RemovableDrives.Count == 0) ? "No removable USB drives detected. Plug in a drive and click Refresh." : $"{RemovableDrives.Count} removable drive(s) ready.");
	}

	private async Task BackupToUsbAsync()
	{
		if (IsUsbBackupBusy)
		{
			return;
		}
		if ((object)SelectedRemovableDrive == null)
		{
			UsbBackupStatus = "Select a USB drive first (or click Refresh Drives).";
			return;
		}
		IsUsbBackupBusy = true;
		try
		{
			UsbBackupStatus = "Backing up to " + SelectedRemovableDrive.Root + "…";
			string text = await Task.Run(async () => await _usbBackup.BackupToUsbAsync(SelectedRemovableDrive.Root));
			ApplyUsbBackupSettings(_usbBackupSettings.Load());
			UsbBackupStatus = "Encrypted backup saved: " + text;
			PackageStatus = UsbBackupStatus;
			ErrorMessage = string.Empty;
			_confirmation.Notify("USB backup complete", Path.GetFileName(text));
		}
		catch (Exception ex)
		{
			UsbBackupStatus = ex.Message;
			ErrorMessage = ex.Message;
			_confirmation.NotifyError("USB backup failed", ex.Message);
		}
		finally
		{
			IsUsbBackupBusy = false;
		}
	}

	private void OnUsbBackupSettingsChanged(UsbBackupSettings settings)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyUsbBackupSettings(settings);
			return;
		}
		dispatcher.BeginInvoke((Action)(() =>
		{
			ApplyUsbBackupSettings(settings);
		}));
	}

	private void ApplyUsbBackupSettings(UsbBackupSettings settings)
	{
		LastUsbBackupAtUtc = settings.LastUsbBackupUtc;
	}

	private async Task PublishBranchSnapshotAsync()
	{
		try
		{
			BranchNetworkStatus = "Publishing branch snapshot to PharmaBill_Sync/Branches/…";
			await Task.Run(async () =>
			{
				await _branchNetwork.PublishCurrentBranchAsync();
			});
			BranchNetworkStatus = "Branch snapshot and stock index uploaded.";
		}
		catch (Exception ex)
		{
			BranchNetworkStatus = ex.Message;
		}
	}

	private async Task AggregatePeerBranchesAsync()
	{
		try
		{
			BranchNetworkStatus = "Pulling peer branch indexes (head office only)…";
			await Task.Run(async () =>
			{
				await _branchNetwork.AggregatePeerBranchIndexesAsync();
			});
			BranchNetworkStatus = "Peer branch stock and sales summaries cached for consolidated reports.";
		}
		catch (Exception ex)
		{
			BranchNetworkStatus = ex.Message;
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 4;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			await scope.ServiceProvider.GetRequiredService<SyncDeviceService>().InitializeLocalDeviceAsync(DeviceName, "1.0.0", cancellationToken);
			await ReloadAsync(context, scope.ServiceProvider.GetRequiredService<FileTransferQueue>(), cancellationToken);
			ApplyFolderSettings(await _folderSync.GetSettingsAsync(cancellationToken));
			ApplyCloudSettings(await _cloudSync.GetSettingsAsync(cancellationToken));
			ApplyGoogleDriveSettings(await _googleDrive.GetSettingsAsync(cancellationToken));
			ApplyAutoBackupPreferences(_autoBackupPreferences.Load());
			ApplyUsbBackupSettings(_usbBackupSettings.Load());
			RefreshRemovableDrives();
			RefreshLanStation();
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ChooseSyncFolder()
	{
		string text = _filePicker.PickFolder("Choose Google Drive or another synced folder");
		if (text != null)
		{
			SaveSyncFolderAsync(text);
		}
	}

	private async Task SyncNowAsync()
	{
		_ = 1;
		try
		{
			await _folderSync.ConfigureAsync(SyncFolderPath, AutoSyncEnabled);
			await _folderSync.SyncNowAsync();
			ErrorMessage = string.Empty;
			PackageStatus = "Folder sync completed.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
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
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveAutoSyncPreferenceAsync(bool enabled)
	{
		try
		{
			await _folderSync.SetAutoSyncAsync(enabled);
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			AutoSyncEnabled = !enabled;
		}
	}

	private async Task SaveCloudSettingsAsync()
	{
		try
		{
			await _cloudSync.ConfigureAsync(CloudServerUrl, CloudApiKey, CloudSyncEnabled);
			ErrorMessage = string.Empty;
			PackageStatus = "Cloud sync settings saved.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task CloudSyncNowAsync()
	{
		_ = 1;
		try
		{
			await _cloudSync.ConfigureAsync(CloudServerUrl, CloudApiKey, CloudSyncEnabled);
			CloudSyncRunResult cloudSyncRunResult = await _cloudSync.SyncNowAsync();
			ErrorMessage = string.Empty;
			PackageStatus = $"Cloud sync completed: {cloudSyncRunResult.Pushed} sent, {cloudSyncRunResult.Pulled} received.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ConfigureCloudOAuthKeys()
	{
		try
		{
			CloudKeysWindow cloudKeysWindow = new CloudKeysWindow(_oauthClients)
			{
				Owner = Application.Current?.MainWindow
			};
			if (cloudKeysWindow.ShowDialog() == true || cloudKeysWindow.KeysApplied)
			{
				if (IsConfigWarning(GoogleDriveWarning))
				{
					GoogleDriveWarning = string.Empty;
				}
				ErrorMessage = string.Empty;
				PackageStatus = "Cloud OAuth keys saved. You can Link Google Drive now.";
				_confirmation.Notify("OAuth keys saved", PackageStatus);
			}
		}
		catch (Exception exception)
		{
			string message = (ErrorMessage = CloudOAuthClientStore.ToUserFriendlyMessage(exception));
			_confirmation.NotifyError("Could not open key settings", message);
		}
	}

	private static bool IsConfigWarning(string? warning)
	{
		if (!string.IsNullOrWhiteSpace(warning))
		{
			if (!warning.Contains("Configure Keys", StringComparison.OrdinalIgnoreCase) && !warning.Contains("not linked", StringComparison.OrdinalIgnoreCase) && !warning.Contains("not configured", StringComparison.OrdinalIgnoreCase) && !warning.Contains("OAuth client", StringComparison.OrdinalIgnoreCase))
			{
				return warning.Contains("Azure app", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private async Task ConnectGoogleDriveAsync()
	{
		if (IsGoogleDriveBusy)
		{
			return;
		}
		IsGoogleDriveBusy = true;
		try
		{
			PackageStatus = "Opening Google sign-in in your browser…";
			await Task.Run(async () =>
			{
				await _googleDrive.ConnectAsync(GoogleDriveEmail);
			});
			ApplyGoogleDriveSettings(await _googleDrive.GetSettingsAsync());
			ErrorMessage = string.Empty;
			PackageStatus = (GoogleDriveConnected ? ("Google Drive linked as " + GoogleDriveEmail + ".") : "Google Drive connected.");
			_confirmation.Notify("Google Drive connected", PackageStatus);
		}
		catch (Exception ex)
		{
			string message = (GoogleDriveWarning = (ErrorMessage = CloudOAuthClientStore.ToUserFriendlyMessage(ex)));
			_confirmation.NotifyError("Could not link Google Account", message);
			if (ex is CloudOAuthNotConfiguredException || ex.InnerException is CloudOAuthNotConfiguredException)
			{
				ConfigureCloudOAuthKeys();
			}
		}
		finally
		{
			IsGoogleDriveBusy = false;
		}
	}

	private async Task DisconnectGoogleDriveAsync()
	{
		try
		{
			await _googleDrive.DisconnectAsync();
			PackageStatus = "Google Drive disconnected.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task UploadGoogleDriveBackupAsync()
	{
		if (IsGoogleDriveBusy)
		{
			return;
		}
		IsGoogleDriveBusy = true;
		IsGoogleDriveUploadFailed = false;
		IsGoogleDriveUploading = true;
		GoogleDriveUploadStatus = "Preparing encrypted backup…";
		GoogleDriveBytesLabel = string.Empty;
		GoogleDriveUploadPercent = 5.0;
		try
		{
			await Task.Run(async () =>
			{
				await _googleDrive.UploadBackupAsync();
			});
			ErrorMessage = string.Empty;
			GoogleDriveWarning = string.Empty;
			PackageStatus = "Backup uploaded to Google Drive.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			IsGoogleDriveUploadFailed = true;
			GoogleDriveWarning = ex.Message;
			IsGoogleDriveUploading = false;
		}
		finally
		{
			IsGoogleDriveBusy = false;
		}
	}

	private Task RetryGoogleDriveUploadAsync()
	{
		IsGoogleDriveUploadFailed = false;
		GoogleDriveWarning = string.Empty;
		return UploadGoogleDriveBackupAsync();
	}

	private void OnGoogleDriveProgress(GoogleDriveProgress progress)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			Apply();
		}
		else
		{
			dispatcher.BeginInvoke(new Action(Apply));
		}
		void Apply()
		{
			GoogleDriveUploadPercent = progress.Percent;
			GoogleDriveUploadStatus = progress.Message;
			GoogleDriveBytesLabel = FormatDriveBytes(progress.BytesSent, progress.TotalBytes, progress.Percent);
			if (progress.IsError)
			{
				IsGoogleDriveUploading = false;
				IsGoogleDriveUploadFailed = true;
				GoogleDriveWarning = progress.Message;
			}
			else if (progress.IsComplete || progress.Percent >= 100)
			{
				IsGoogleDriveUploading = false;
				IsGoogleDriveUploadFailed = false;
				GoogleDriveWarning = string.Empty;
				NotifyGoogleDriveCardState();
			}
			else
			{
				IsGoogleDriveUploading = true;
				IsGoogleDriveUploadFailed = false;
			}
		}
	}

	private static string FormatDriveBytes(long bytesSent, long totalBytes, int percent)
	{
		if (totalBytes > 0)
		{
			return $"{percent}% ({FormatBytes(bytesSent)} / {FormatBytes(totalBytes)})";
		}
		if (percent <= 0)
		{
			return string.Empty;
		}
		return $"{percent}%";
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes >= 1024)
		{
			double num = bytes;
			string[] array = new string[3] { "KB", "MB", "GB" };
			int num2 = 0;
			num /= 1024.0;
			while (num >= 1024.0 && num2 < array.Length - 1)
			{
				num /= 1024.0;
				num2++;
			}
			return $"{num:0.#} {array[num2]}";
		}
		return $"{bytes} B";
	}

	private async Task DownloadGoogleDriveBackupAsync()
	{
		if (IsGoogleDriveBusy || !_confirmation.Confirm("This replaces the local PharmaBill database with the latest cloud backup. A safety backup is created first.", "Download & restore from Google Drive?"))
		{
			return;
		}
		IsGoogleDriveBusy = true;
		try
		{
			await Task.Run(async () =>
			{
				await _googleDrive.DownloadAndRestoreAsync();
			});
			ErrorMessage = string.Empty;
			PackageStatus = "Cloud backup restored. Restart PharmaBill if the screen looks stale.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsGoogleDriveBusy = false;
		}
	}

	private async Task PersistGoogleDrivePreferencesAsync()
	{
		try
		{
			await _googleDrive.SavePreferencesAsync(GoogleDriveSyncOnExit, GoogleDriveSyncOnBillSave);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void OnGoogleDriveSettingsChanged(GoogleDriveSyncSettings settings)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyGoogleDriveSettings(settings);
			return;
		}
		dispatcher.BeginInvoke((Action)(() =>
		{
			ApplyGoogleDriveSettings(settings);
		}));
	}

	private void ApplyGoogleDriveSettings(GoogleDriveSyncSettings settings)
	{
		bool flag = IsRealOAuthAccount(settings.AccountEmail, settings.Status, settings.RefreshToken);
		GoogleDriveEmail = (flag ? settings.AccountEmail : string.Empty);
		GoogleDriveConnected = settings.IsConnected & flag;
		GoogleDriveStatus = (GoogleDriveConnected ? "Connected" : "Not Connected");
		GoogleDriveWarning = settings.LastError ?? string.Empty;
		IsGoogleDriveUploadFailed = !string.IsNullOrWhiteSpace(settings.LastError);
		LastGoogleDriveSyncAtUtc = settings.LastSyncAtUtc;
		GoogleDriveSyncOnExit = settings.SyncOnExit;
		GoogleDriveSyncOnBillSave = settings.SyncOnBillSave;
		OnPropertyChanged("GoogleDriveConnectionLabel");
		NotifyGoogleDriveCardState();
		if (settings.IsConnected && !flag)
		{
			ClearStaleGoogleDriveAsync();
		}
	}

	private async Task ClearStaleGoogleDriveAsync()
	{
		try
		{
			await _googleDrive.DisconnectAsync();
		}
		catch
		{
		}
	}

	private async Task TestGoogleDriveConnectionAsync()
	{
		if (IsGoogleDriveBusy)
		{
			return;
		}
		IsGoogleDriveBusy = true;
		try
		{
			CloudTestStatus = await Task.Run(async () => await _googleDrive.TestConnectionAsync());
			ErrorMessage = string.Empty;
			PackageStatus = CloudTestStatus;
		}
		catch (Exception ex)
		{
			CloudTestStatus = ex.Message;
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsGoogleDriveBusy = false;
		}
	}

	private void PersistAutoBackupDestinations()
	{
		if (!_suppressAutoBackupPreferenceSave)
		{
			CloudAutoBackupDestination destinations = (AutoBackupGoogleDrive ? CloudAutoBackupDestination.GoogleDrive : CloudAutoBackupDestination.None);
			_autoBackupPreferences.Save(new CloudAutoBackupPreferences(destinations));
		}
	}

	private void ApplyAutoBackupPreferences(CloudAutoBackupPreferences preferences)
	{
		_suppressAutoBackupPreferenceSave = true;
		AutoBackupGoogleDrive = preferences.IncludesGoogle;
		_suppressAutoBackupPreferenceSave = false;
	}

	private static bool IsRealOAuthAccount(string? email, string? status, string? tokenOrAccountId)
	{
		if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
		{
			return false;
		}
		if (email.Contains("local-simulated", StringComparison.OrdinalIgnoreCase) || email.EndsWith("@pharmabill.local", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(status) && status.Contains("Simulated", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(tokenOrAccountId) && (tokenOrAccountId.Contains("local-simulated", StringComparison.OrdinalIgnoreCase) || tokenOrAccountId.Contains("simulated", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		return !string.IsNullOrWhiteSpace(tokenOrAccountId);
	}

	private async Task TestCloudSyncNowAsync()
	{
		if (IsCloudTestBusy)
		{
			return;
		}
		IsCloudTestBusy = true;
		List<string> parts = new List<string>();
		try
		{
			CloudTestStatus = "Testing linked cloud providers…";
			ErrorMessage = string.Empty;
			if (GoogleDriveConnected)
			{
				try
				{
					List<string> list = parts;
					list.Add(await Task.Run(async () => await _googleDrive.TestConnectionAsync()));
				}
				catch (Exception ex)
				{
					parts.Add("Google Drive failed: " + ex.Message);
				}
			}
			if (parts.Count == 0)
			{
				CloudTestStatus = "Link Google Drive before testing cloud sync.";
				ErrorMessage = CloudTestStatus;
				return;
			}
			CloudTestStatus = string.Join(" | ", parts);
			PackageStatus = CloudTestStatus;
			if (parts.Any((string part) => part.Contains("failed", StringComparison.OrdinalIgnoreCase)))
			{
				ErrorMessage = CloudTestStatus;
			}
		}
		finally
		{
			IsCloudTestBusy = false;
		}
	}

	private void OnCloudSettingsChanged(CloudSyncSettings settings)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyCloudSettings(settings);
			return;
		}
		dispatcher.BeginInvoke((Action)(() =>
		{
			ApplyCloudSettings(settings);
		}));
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
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyFolderSettings(settings);
			return;
		}
		dispatcher.BeginInvoke((Action)(() =>
		{
			ApplyFolderSettings(settings);
		}));
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

	private void RefreshLanStation()
	{
		WifiIpAddress = LocalNetworkInfo.GetLocalIPv4() ?? "Not on Wi-Fi";
		LanPort = _localSync.Port;
		ApplyLanStatus(_localSync.Status);
		string host = (WifiIpAddress.Contains('.', StringComparison.Ordinal) ? WifiIpAddress : string.Empty);
		PairingPayload = _localSync.BuildPairingPayload(host);
		PairingQrCode = QrCodeImage.FromText(PairingPayload);
		ApplyFirewallCheck(LanSyncFirewallHelper.CheckRule((LanPort > 0) ? LanPort : 5055));
		ApplyZeroConfigStatus();
	}

	private void OnZeroConfigStateChanged()
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyZeroConfigStatus();
		}
		else
		{
			dispatcher.BeginInvoke(new Action(ApplyZeroConfigStatus));
		}
	}

	private void ApplyZeroConfigStatus()
	{
		CloudFallbackActive = _zeroConfig.IsCloudFallbackActive;
		if (_zeroConfig.IsDiscoverable && !string.IsNullOrWhiteSpace(_zeroConfig.DiscoveryInstanceName))
		{
			DiscoveryStatus = $"Discoverable on Wi-Fi as {_zeroConfig.DiscoveryInstanceName}.{"_pharmabill-sync._tcp"}.local";
		}
		else if (CloudFallbackActive)
		{
			DiscoveryStatus = "LAN listener unavailable — using Google Drive / PharmaBill_Sync folder sync automatically.";
		}
		else
		{
			DiscoveryStatus = "LAN discovery idle.";
		}
	}

	private void RotateLanPairingSecret()
	{
		_localSync.RegenerateToken();
		RefreshLanStation();
		PackageStatus = "Generated a new LAN pairing secret. Scan the QR code again on the phone.";
	}

	private async Task CheckAllowFirewallRuleAsync()
	{
		int port = ((LanPort > 0) ? LanPort : 5055);
		LanSyncFirewallHelper.FirewallCheckResult firewallCheckResult = await Task.Run(() => LanSyncFirewallHelper.CheckRule(port));
		ApplyFirewallCheck(firewallCheckResult);
		if (firewallCheckResult.RulePresent)
		{
			PackageStatus = firewallCheckResult.Message;
			ErrorMessage = string.Empty;
			return;
		}
		if (!_confirmation.Confirm($"Android cannot reach this PC on TCP {port} until Windows Firewall allows inbound traffic.\n\n" + "PharmaBill will request Administrator permission to add:\n\"PharmaBill Sync Station (Port 5055)\"\n\nContinue?", "Allow Windows Firewall?"))
		{
			PackageStatus = "Firewall change cancelled. Port 5055 may still be blocked for the phone.";
			return;
		}
		LanSyncFirewallHelper.FirewallCheckResult firewallCheckResult2 = await Task.Run(() => LanSyncFirewallHelper.AllowRuleElevated(port));
		ApplyFirewallCheck(firewallCheckResult2);
		PackageStatus = firewallCheckResult2.Message;
		ErrorMessage = (firewallCheckResult2.RulePresent ? string.Empty : (firewallCheckResult2.Detail ?? firewallCheckResult2.Message));
		if (firewallCheckResult2.RulePresent)
		{
			RefreshLanStation();
		}
	}

	private void ApplyFirewallCheck(LanSyncFirewallHelper.FirewallCheckResult result)
	{
		FirewallRulePresent = result.RulePresent;
		FirewallRuleStatus = result.Message;
		bool flag = !result.RulePresent;
		if (flag)
		{
			LocalSyncState lanSyncState = LanSyncState;
			bool flag2 = (uint)(lanSyncState - 1) <= 1u;
			flag = flag2;
		}
		if (flag)
		{
			LanStatusDetail = result.Detail ?? result.Message;
		}
		else if (result.RulePresent && string.IsNullOrWhiteSpace(LanStatusDetail) && !string.IsNullOrWhiteSpace(result.Detail))
		{
			LanStatusDetail = result.Detail;
		}
		else if (result.RulePresent && LanStatusDetail.Contains("Firewall", StringComparison.OrdinalIgnoreCase))
		{
			LanStatusDetail = result.Detail ?? result.Message;
		}
	}

	private void OnLocalSyncStatusChanged(LocalSyncStatus status)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			ApplyLanStatus(status);
			return;
		}
		dispatcher.BeginInvoke((Action)(() =>
		{
			ApplyLanStatus(status);
		}));
	}

	private void ApplyLanStatus(LocalSyncStatus status)
	{
		LanSyncState = status.State;
		LanStatusMessage = status.Message;
		LanStatusDetail = status.Detail ?? string.Empty;
		LanPort = _localSync.Port;
	}

	private void GeneratePairingCode()
	{
		try
		{
			using IServiceScope serviceScope = _scopeFactory.CreateScope();
			PairingCode pairingCode = serviceScope.ServiceProvider.GetRequiredService<SyncDeviceService>().CreatePairingCode();
			GeneratedPairingCode = pairingCode.Code;
			PackageStatus = $"One-time pairing code; expires {pairingCode.ExpiresAtUtc.ToLocalTime():g}. Share it with the other device out of band.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task PairDeviceAsync()
	{
		if (!Guid.TryParse(PeerDeviceId, out var result))
		{
			ErrorMessage = "Enter the other device's ID.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<SyncDeviceService>().PairDeviceAsync(result, PeerName, PeerPlatform, PairingCode);
			PairingCode = string.Empty;
			GeneratedPairingCode = string.Empty;
			PackageStatus = "Paired with " + PeerName + ". Pair the same one-time code on both devices.";
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task RevokeDeviceAsync()
	{
		if (SelectedDevice == null || SelectedDevice.IsCurrentDevice)
		{
			ErrorMessage = "Select a paired peer device to revoke.";
		}
		else
		{
			if (!_confirmation.Confirm("Revoke " + SelectedDevice.DeviceName + "? Packages signed with its device key will no longer be accepted.", "Revoke paired device"))
			{
				return;
			}
			try
			{
				using IServiceScope scope = _scopeFactory.CreateScope();
				await scope.ServiceProvider.GetRequiredService<SyncDeviceService>().RevokeDeviceAsync(SelectedDevice.Id);
				await LoadAsync();
				PackageStatus = "Paired device revoked.";
				ErrorMessage = string.Empty;
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	private async Task ExportPackageAsync()
	{
		if (SelectedDevice == null || SelectedDevice.IsCurrentDevice)
		{
			ErrorMessage = "Select the paired destination device.";
			return;
		}
		string destination = _filePicker.PickExportDestination("pbsync", $"PharmaBill-sync-{DateTime.Now:yyyyMMdd-HHmm}");
		if (destination == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			int value = await scope.ServiceProvider.GetRequiredService<SyncPackageService>().ExportAsync(SelectedDevice.Id, destination);
			PackageStatus = $"Exported {value} change(s) to signed package {destination}.";
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ImportPackageAsync()
	{
		string text = _filePicker.PickSyncPackage();
		if (text == null || !_confirmation.Confirm("Verify and import all changes in this signed package? Changes are applied in one database transaction.", "Import sync package"))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			SyncImportResult syncImportResult = await scope.ServiceProvider.GetRequiredService<SyncPackageService>().ImportAsync(text);
			PackageStatus = $"Import complete: {syncImportResult.Applied} applied, {syncImportResult.Duplicates} already seen, {syncImportResult.Conflicts} data conflict(s), {syncImportResult.StockConflicts} negative-stock alert(s).";
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task KeepLocalAsync()
	{
		await ResolveConflictAsync(acceptRemote: false);
	}

	private async Task AcceptRemoteAsync()
	{
		await ResolveConflictAsync(acceptRemote: true);
	}

	private async Task ResolveConflictAsync(bool acceptRemote)
	{
		if (SelectedConflict == null || string.IsNullOrWhiteSpace(ConflictReason))
		{
			ErrorMessage = "Select a conflict and enter a reason for its resolution.";
			return;
		}
		string value = (acceptRemote ? "accept the remote version" : "keep the local version");
		if (!_confirmation.Confirm($"This records an audited resolution ({value}) for conflict {SelectedConflict.Id:D}. Continue?", "Resolve sync conflict"))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<ChangeApplier>().ResolveConflictAsync(SelectedConflict.Id, acceptRemote, ConflictReason, (_session.User ?? throw new UnauthorizedAccessException("Sign in before resolving sync conflicts.")).Id);
			ConflictReason = string.Empty;
			PackageStatus = "Conflict resolution recorded.";
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task UpdateLocalNameAsync(string name)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<SyncDeviceService>().InitializeLocalDeviceAsync(name, "1.0.0");
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ReloadAsync(PharmaBillDbContext context, FileTransferQueue fileQueue, CancellationToken cancellationToken)
	{
		List<DeviceInfo> devices = await (from item in context.DeviceInfos.AsNoTracking()
			orderby item.IsCurrentDevice descending, item.DeviceName
			select item).ToListAsync(cancellationToken);
		Devices.Clear();
		foreach (DeviceInfo item in devices)
		{
			Devices.Add(item);
		}
		PendingChangeCount = await context.ChangeLogs.AsNoTracking().CountAsync((ChangeLog item) => (int)item.SyncState == 0, cancellationToken);
		List<SyncConflict> list = await (from item in context.SyncConflicts.AsNoTracking()
			where item.Resolution == null || item.Resolution == "Open"
			orderby item.UpdatedAtUtc descending
			select item).ToListAsync(cancellationToken);
		Conflicts.Clear();
		foreach (SyncConflict item2 in list)
		{
			Conflicts.Add(item2);
		}
		UnresolvedConflictCount = list.Count;
		LastExchangeAtUtc = (from item in devices
			where item.LastSyncAtUtc.HasValue
			select item.LastSyncAtUtc).Max();
		FileTransfers.Clear();
		foreach (FileTransferItem item3 in await fileQueue.GetAllAsync(cancellationToken))
		{
			FileTransfers.Add(item3);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedConflictChanged(SyncConflict? value)
	{
		CanAcceptRemote = value?.EntityName != "StockConflict";
		CanResolveConflict = value?.EntityName != "StockConflict";
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDeviceNameChanged(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			UpdateLocalNameAsync(value);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnAutoSyncEnabledChanged(bool value)
	{
		SaveAutoSyncPreferenceAsync(value);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGoogleDriveConnectedChanged(bool value)
	{
		NotifyGoogleDriveCardState();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLastGoogleDriveSyncAtUtcChanged(DateTime? value)
	{
		OnPropertyChanged("GoogleDriveIdleBadge");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGoogleDriveSyncOnExitChanged(bool value)
	{
		PersistGoogleDrivePreferencesAsync();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGoogleDriveSyncOnBillSaveChanged(bool value)
	{
		PersistGoogleDrivePreferencesAsync();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsGoogleDriveUploadingChanged(bool value)
	{
		NotifyGoogleDriveCardState();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsGoogleDriveUploadFailedChanged(bool value)
	{
		NotifyGoogleDriveCardState();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLastUsbBackupAtUtcChanged(DateTime? value)
	{
		OnPropertyChanged("LastUsbBackupLabel");
	}
}
