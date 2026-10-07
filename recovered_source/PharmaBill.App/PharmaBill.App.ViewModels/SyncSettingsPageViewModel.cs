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
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
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

	[ObservableProperty]
	private DeviceInfo? _selectedDevice;

	[ObservableProperty]
	private SyncConflict? _selectedConflict;

	[ObservableProperty]
	private bool _canAcceptRemote = true;

	[ObservableProperty]
	private bool _canResolveConflict = true;

	[ObservableProperty]
	private string _deviceName = Environment.MachineName;

	[ObservableProperty]
	private string _peerDeviceId = string.Empty;

	[ObservableProperty]
	private string _peerName = string.Empty;

	[ObservableProperty]
	private string _peerPlatform = "Windows";

	[ObservableProperty]
	private string _pairingCode = string.Empty;

	[ObservableProperty]
	private string _generatedPairingCode = string.Empty;

	[ObservableProperty]
	private string _conflictReason = string.Empty;

	[ObservableProperty]
	private string _packageStatus = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private string _syncFolderPath = SyncFolderSettings.Default.FolderPath;

	[ObservableProperty]
	private bool _autoSyncEnabled;

	[ObservableProperty]
	private string _folderSyncStatus = "Idle";

	[ObservableProperty]
	private string _folderSyncWarning = string.Empty;

	[ObservableProperty]
	private DateTime? _lastFolderSyncAtUtc;

	[ObservableProperty]
	private int _lastIncomingPackages;

	[ObservableProperty]
	private int _lastOutgoingPackages;

	[ObservableProperty]
	private int _lastAppliedChanges;

	[ObservableProperty]
	private int _pendingChangeCount;

	[ObservableProperty]
	private int _unresolvedConflictCount;

	[ObservableProperty]
	private DateTime? _lastExchangeAtUtc;

	[ObservableProperty]
	private string _cloudServerUrl = string.Empty;

	[ObservableProperty]
	private string _cloudApiKey = string.Empty;

	[ObservableProperty]
	private bool _cloudSyncEnabled;

	[ObservableProperty]
	private string _cloudSyncStatus = "Idle";

	[ObservableProperty]
	private string _cloudSyncWarning = string.Empty;

	[ObservableProperty]
	private DateTime? _lastCloudSyncAtUtc;

	[ObservableProperty]
	private int _lastCloudPushed;

	[ObservableProperty]
	private int _lastCloudPulled;

	[ObservableProperty]
	private LocalSyncState _lanSyncState;

	[ObservableProperty]
	private string _lanStatusMessage = "Sync station is stopped";

	[ObservableProperty]
	private string _lanStatusDetail = string.Empty;

	[ObservableProperty]
	private string _wifiIpAddress = "Not on Wi-Fi";

	[ObservableProperty]
	private int _lanPort = 5055;

	[ObservableProperty]
	private ImageSource? _pairingQrCode;

	[ObservableProperty]
	private string _pairingPayload = string.Empty;

	[ObservableProperty]
	private string _firewallRuleStatus = string.Empty;

	[ObservableProperty]
	private bool _firewallRulePresent;

	[ObservableProperty]
	private string _discoveryStatus = string.Empty;

	[ObservableProperty]
	private bool _cloudFallbackActive;

	[ObservableProperty]
	private string _googleDriveEmail = string.Empty;

	[ObservableProperty]
	private bool _googleDriveConnected;

	[ObservableProperty]
	private string _googleDriveStatus = "Not Connected";

	[ObservableProperty]
	private string _googleDriveWarning = string.Empty;

	[ObservableProperty]
	private DateTime? _lastGoogleDriveSyncAtUtc;

	[ObservableProperty]
	private bool _googleDriveSyncOnExit;

	[ObservableProperty]
	private bool _googleDriveSyncOnBillSave;

	[ObservableProperty]
	private bool _isGoogleDriveBusy;

	[ObservableProperty]
	private bool _isGoogleDriveUploading;

	[ObservableProperty]
	private bool _isGoogleDriveUploadFailed;

	[ObservableProperty]
	private double _googleDriveUploadPercent;

	[ObservableProperty]
	private string _googleDriveUploadStatus = string.Empty;

	[ObservableProperty]
	private string _googleDriveBytesLabel = string.Empty;

	[ObservableProperty]
	private bool _autoBackupGoogleDrive;

	[ObservableProperty]
	private string _branchNetworkStatus = string.Empty;

	[ObservableProperty]
	private string _cloudTestStatus = string.Empty;

	[ObservableProperty]
	private bool _isCloudTestBusy;

	[ObservableProperty]
	private RemovableDriveOption? _selectedRemovableDrive;

	[ObservableProperty]
	private DateTime? _lastUsbBackupAtUtc;

	[ObservableProperty]
	private string _usbBackupStatus = string.Empty;

	[ObservableProperty]
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedDevice);
				_selectedDevice = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedDevice);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedConflict);
				_selectedConflict = value;
				OnSelectedConflictChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedConflict);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanAcceptRemote);
				_canAcceptRemote = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanAcceptRemote);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanResolveConflict);
				_canResolveConflict = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanResolveConflict);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DeviceName);
				_deviceName = value;
				OnDeviceNameChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DeviceName);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PeerDeviceId);
				_peerDeviceId = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PeerDeviceId);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PeerName);
				_peerName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PeerName);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PeerPlatform);
				_peerPlatform = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PeerPlatform);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PairingCode);
				_pairingCode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PairingCode);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GeneratedPairingCode);
				_generatedPairingCode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GeneratedPairingCode);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ConflictReason);
				_conflictReason = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ConflictReason);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PackageStatus);
				_packageStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PackageStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SyncFolderPath);
				_syncFolderPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SyncFolderPath);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AutoSyncEnabled);
				_autoSyncEnabled = value;
				OnAutoSyncEnabledChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AutoSyncEnabled);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FolderSyncStatus);
				_folderSyncStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FolderSyncStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FolderSyncWarning);
				_folderSyncWarning = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FolderSyncWarning);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastFolderSyncAtUtc);
				_lastFolderSyncAtUtc = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastFolderSyncAtUtc);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastIncomingPackages);
				_lastIncomingPackages = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastIncomingPackages);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastOutgoingPackages);
				_lastOutgoingPackages = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastOutgoingPackages);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastAppliedChanges);
				_lastAppliedChanges = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastAppliedChanges);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PendingChangeCount);
				_pendingChangeCount = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PendingChangeCount);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UnresolvedConflictCount);
				_unresolvedConflictCount = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UnresolvedConflictCount);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastExchangeAtUtc);
				_lastExchangeAtUtc = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastExchangeAtUtc);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudServerUrl);
				_cloudServerUrl = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudServerUrl);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudApiKey);
				_cloudApiKey = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudApiKey);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudSyncEnabled);
				_cloudSyncEnabled = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudSyncEnabled);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudSyncStatus);
				_cloudSyncStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudSyncStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudSyncWarning);
				_cloudSyncWarning = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudSyncWarning);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastCloudSyncAtUtc);
				_lastCloudSyncAtUtc = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastCloudSyncAtUtc);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastCloudPushed);
				_lastCloudPushed = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastCloudPushed);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastCloudPulled);
				_lastCloudPulled = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastCloudPulled);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LanSyncState);
				_lanSyncState = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LanSyncState);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LanStatusMessage);
				_lanStatusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LanStatusMessage);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LanStatusDetail);
				_lanStatusDetail = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LanStatusDetail);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.WifiIpAddress);
				_wifiIpAddress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.WifiIpAddress);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LanPort);
				_lanPort = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LanPort);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PairingQrCode);
				_pairingQrCode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PairingQrCode);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PairingPayload);
				_pairingPayload = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PairingPayload);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FirewallRuleStatus);
				_firewallRuleStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FirewallRuleStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FirewallRulePresent);
				_firewallRulePresent = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FirewallRulePresent);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DiscoveryStatus);
				_discoveryStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscoveryStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudFallbackActive);
				_cloudFallbackActive = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudFallbackActive);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveEmail);
				_googleDriveEmail = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveEmail);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveConnected);
				_googleDriveConnected = value;
				OnGoogleDriveConnectedChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveConnected);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveStatus);
				_googleDriveStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveWarning);
				_googleDriveWarning = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveWarning);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastGoogleDriveSyncAtUtc);
				_lastGoogleDriveSyncAtUtc = value;
				OnLastGoogleDriveSyncAtUtcChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastGoogleDriveSyncAtUtc);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveSyncOnExit);
				_googleDriveSyncOnExit = value;
				OnGoogleDriveSyncOnExitChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveSyncOnExit);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveSyncOnBillSave);
				_googleDriveSyncOnBillSave = value;
				OnGoogleDriveSyncOnBillSaveChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveSyncOnBillSave);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsGoogleDriveBusy);
				_isGoogleDriveBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsGoogleDriveBusy);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsGoogleDriveUploading);
				_isGoogleDriveUploading = value;
				OnIsGoogleDriveUploadingChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsGoogleDriveUploading);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsGoogleDriveUploadFailed);
				_isGoogleDriveUploadFailed = value;
				OnIsGoogleDriveUploadFailedChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsGoogleDriveUploadFailed);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveUploadPercent);
				_googleDriveUploadPercent = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveUploadPercent);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveUploadStatus);
				_googleDriveUploadStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveUploadStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GoogleDriveBytesLabel);
				_googleDriveBytesLabel = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GoogleDriveBytesLabel);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AutoBackupGoogleDrive);
				_autoBackupGoogleDrive = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AutoBackupGoogleDrive);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchNetworkStatus);
				_branchNetworkStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchNetworkStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloudTestStatus);
				_cloudTestStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloudTestStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsCloudTestBusy);
				_isCloudTestBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsCloudTestBusy);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedRemovableDrive);
				_selectedRemovableDrive = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedRemovableDrive);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastUsbBackupAtUtc);
				_lastUsbBackupAtUtc = value;
				OnLastUsbBackupAtUtcChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastUsbBackupAtUtc);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UsbBackupStatus);
				_usbBackupStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UsbBackupStatus);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsUsbBackupBusy);
				_isUsbBackupBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsUsbBackupBusy);
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
	private void ChooseSyncFolder()
	{
		string text = _filePicker.PickFolder("Choose Google Drive or another synced folder");
		if (text != null)
		{
			SaveSyncFolderAsync(text);
		}
	}

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
	private void RotateLanPairingSecret()
	{
		_localSync.RegenerateToken();
		RefreshLanStation();
		PackageStatus = "Generated a new LAN pairing secret. Scan the QR code again on the phone.";
	}

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
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

	[RelayCommand]
	private async Task KeepLocalAsync()
	{
		await ResolveConflictAsync(acceptRemote: false);
	}

	[RelayCommand]
	private async Task AcceptRemoteAsync()
	{
		await ResolveConflictAsync(acceptRemote: true);
	}

	[RelayCommand]
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
