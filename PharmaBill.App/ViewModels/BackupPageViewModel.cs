using PharmaBill.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// Everything about copies of the shop's data, in one place: the backup password, folder backup, USB backup,
// restore, and when the last backup was made. (Sync is for devices: phones and billing counters.)
public class BackupPageViewModel : ObservableObject, ILoadablePage
{
	/// <summary>False inside Backup &amp; Sync, whose own title sits above the tabs.</summary>
	public bool ShowPageTitle { get; set; } = true;

	private readonly BackupSettingsStore _settingsStore;

	private readonly FolderBackupService _folderBackup;

	private readonly UsbBackupService _usbBackup;

	private readonly UsbBackupSettingsStore _usbSettings;

	private readonly IFilePickerService _filePicker;

	private readonly IConfirmationService _confirmationService;

	private readonly CurrentSession _session;

	// Closes Pharma Remit after a restore. Replaceable so a test can see the page asked for it.
	public Action CloseApplication { get; set; } = () => Application.Current?.Shutdown();

	private string _backupFolder = string.Empty;

	private bool _isDefaultFolder;

	private string _folderWarning = string.Empty;

	private string _newPassword = string.Empty;

	private string _confirmPassword = string.Empty;

	private string _restorePassword = string.Empty;

	private bool _hasSavedPassword;

	private bool _isEditingPassword;

	private string _revealedPassword = string.Empty;

	private bool _automaticEnabled;

	private bool _isBusy;

	private BackupReminderState _reminder = BackupReminder.Evaluate(null, null, null, BusinessClock.UtcNow);

	private DateTime? _lastFolderBackupUtc;

	private DateTime? _lastUsbBackupUtc;

	private IReadOnlyList<RemovableDriveOption> _removableDrives = Array.Empty<RemovableDriveOption>();

	private RemovableDriveOption? _selectedDrive;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	public BackupPageViewModel(BackupSettingsStore settingsStore, FolderBackupService folderBackup, UsbBackupService usbBackup, UsbBackupSettingsStore usbSettings, IFilePickerService filePicker, IConfirmationService confirmationService, CurrentSession session)
	{
		_settingsStore = settingsStore;
		_folderBackup = folderBackup;
		_usbBackup = usbBackup;
		_usbSettings = usbSettings;
		_filePicker = filePicker;
		_confirmationService = confirmationService;
		_session = session;
		ChooseFolderCommand = new RelayCommand(ChooseFolder);
		SavePasswordCommand = new RelayCommand(SavePassword);
		ChangePasswordCommand = new RelayCommand(() => IsEditingPassword = true);
		ShowPasswordCommand = new RelayCommand(ShowPassword);
		BackUpNowCommand = new AsyncRelayCommand(BackUpNowAsync, () => !IsBusy);
		RefreshDrivesCommand = new RelayCommand(RefreshDrives);
		BackUpToUsbCommand = new AsyncRelayCommand(BackUpToUsbAsync, () => !IsBusy);
		RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsBusy);
	}

	// ---- folder ----

	public string BackupFolder
	{
		get => _backupFolder;
		private set => SetProperty(ref _backupFolder, value);
	}

	public bool IsDefaultFolder
	{
		get => _isDefaultFolder;
		private set => SetProperty(ref _isDefaultFolder, value);
	}

	/// <summary>Set when the chosen folder is somewhere backups should not live (Downloads).</summary>
	public string FolderWarning
	{
		get => _folderWarning;
		private set
		{
			if (SetProperty(ref _folderWarning, value))
			{
				OnPropertyChanged(nameof(HasFolderWarning));
			}
		}
	}

	public bool HasFolderWarning => !string.IsNullOrEmpty(FolderWarning);

	// ---- password ----

	/// <summary>Typing a new password clears the last password error: it was about the previous attempt.</summary>
	public string NewPassword
	{
		get => _newPassword;
		set
		{
			if (SetProperty(ref _newPassword, value))
			{
				ErrorMessage = string.Empty;
			}
		}
	}

	public string ConfirmPassword
	{
		get => _confirmPassword;
		set
		{
			if (SetProperty(ref _confirmPassword, value))
			{
				ErrorMessage = string.Empty;
			}
		}
	}

	public string RestorePassword
	{
		get => _restorePassword;
		set => SetProperty(ref _restorePassword, value);
	}

	public bool HasSavedPassword
	{
		get => _hasSavedPassword;
		private set
		{
			if (SetProperty(ref _hasSavedPassword, value))
			{
				OnPropertyChanged(nameof(ShowPasswordEditor));
			}
		}
	}

	public bool IsEditingPassword
	{
		get => _isEditingPassword;
		private set
		{
			if (SetProperty(ref _isEditingPassword, value))
			{
				OnPropertyChanged(nameof(ShowPasswordEditor));
			}
		}
	}

	public bool ShowPasswordEditor => !HasSavedPassword || IsEditingPassword;

	public bool CanRevealPassword => _session.User?.Role == UserRole.Owner;

	public string RevealedPassword
	{
		get => _revealedPassword;
		private set => SetProperty(ref _revealedPassword, value);
	}

	public bool AutomaticEnabled
	{
		get => _automaticEnabled;
		set
		{
			if (SetProperty(ref _automaticEnabled, value))
			{
				SaveAutomatic(value);
			}
		}
	}

	// ---- USB ----

	public IReadOnlyList<RemovableDriveOption> RemovableDrives
	{
		get => _removableDrives;
		private set
		{
			if (SetProperty(ref _removableDrives, value))
			{
				OnPropertyChanged(nameof(HasRemovableDrives));
			}
		}
	}

	public bool HasRemovableDrives => RemovableDrives.Count > 0;

	public RemovableDriveOption? SelectedDrive
	{
		get => _selectedDrive;
		set => SetProperty(ref _selectedDrive, value);
	}

	// ---- last backup ----

	public string LastBackupLabel => _reminder.LastBackupUtc.HasValue
		? $"{_reminder.LastBackupUtc.Value.ToBusinessLocal():d MMM yyyy, h:mm tt}  ·  {DaysAgo(_reminder.DaysSinceLastBackup ?? 0)}"
		: "No backup yet";

	public string LastBackupDetail => $"Folder: {Describe(_lastFolderBackupUtc)}   ·   USB: {Describe(_lastUsbBackupUtc)}";

	public bool IsBackupStale => _reminder.DaysSinceLastBackup is null or > BackupReminder.MaxAgeDays;

	public bool IsBusy
	{
		get => _isBusy;
		private set
		{
			if (SetProperty(ref _isBusy, value))
			{
				BackUpNowCommand.NotifyCanExecuteChanged();
				BackUpToUsbCommand.NotifyCanExecuteChanged();
				RestoreBackupCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string StatusMessage
	{
		get => _statusMessage;
		private set => SetProperty(ref _statusMessage, value);
	}

	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public IRelayCommand ChooseFolderCommand { get; }

	public IRelayCommand SavePasswordCommand { get; }

	public IRelayCommand ChangePasswordCommand { get; }

	public IRelayCommand ShowPasswordCommand { get; }

	public IAsyncRelayCommand BackUpNowCommand { get; }

	public IRelayCommand RefreshDrivesCommand { get; }

	public IAsyncRelayCommand BackUpToUsbCommand { get; }

	public IAsyncRelayCommand RestoreBackupCommand { get; }

	public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			BackupSettings settings = _settingsStore.Load();
			ShowFolder(settings);
			HasSavedPassword = !string.IsNullOrWhiteSpace(settings.Password);
			_automaticEnabled = settings.AutomaticEnabled;
			OnPropertyChanged(nameof(AutomaticEnabled));
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = UserMessage.Of(ex);
		}
		IsEditingPassword = false;
		RevealedPassword = string.Empty;
		NewPassword = ConfirmPassword = string.Empty;
		OnPropertyChanged(nameof(CanRevealPassword));
		RefreshLastBackup();
		RefreshDrives();
		return Task.CompletedTask;
	}

	private void ShowFolder(BackupSettings settings)
	{
		BackupFolder = FolderBackupService.FolderFor(settings);
		IsDefaultFolder = string.IsNullOrWhiteSpace(settings.PrimaryFolder);
		FolderWarning = FolderBackupService.IsInDownloads(BackupFolder) ? FolderBackupService.DownloadsRefusal : string.Empty;
	}

	private void RefreshLastBackup()
	{
		try
		{
			_lastFolderBackupUtc = _settingsStore.Load().LastBackupAtUtc;
		}
		catch (InvalidOperationException)
		{
			_lastFolderBackupUtc = null;
		}
		_lastUsbBackupUtc = _usbSettings.Load().LastUsbBackupUtc;
		_reminder = BackupReminder.Evaluate(_lastFolderBackupUtc, _lastUsbBackupUtc, null, BusinessClock.UtcNow);
		OnPropertyChanged(nameof(LastBackupLabel));
		OnPropertyChanged(nameof(LastBackupDetail));
		OnPropertyChanged(nameof(IsBackupStale));
	}

	private void ChooseFolder()
	{
		string? folder = _filePicker.PickFolder("Choose the folder for Pharma Remit backups");
		if (folder == null)
		{
			return;
		}
		if (FolderBackupService.IsInDownloads(folder))
		{
			ErrorMessage = FolderBackupService.DownloadsRefusal;
			return;
		}
		try
		{
			BackupSettings settings = _settingsStore.Load() with { PrimaryFolder = folder };
			_settingsStore.Save(settings);
			ShowFolder(settings);
			ErrorMessage = string.Empty;
			StatusMessage = "Backups will be saved in " + folder;
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
		{
			ErrorMessage = UserMessage.Of(ex);
		}
	}

	private void SavePassword()
	{
		try
		{
			if (string.IsNullOrEmpty(NewPassword))
			{
				throw new ArgumentException("Type the backup password in both boxes, then press Save password.");
			}
			EncryptedBackupService.ValidatePassword(NewPassword);
			if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
			{
				throw new ArgumentException("The two passwords are not the same. Type the same password in both boxes.");
			}
			if (!_confirmationService.Confirm("Write this password down and keep it away from the PC.\n\nEvery backup is locked with it. If it is lost, nobody - including Pharma Remit support - can open those backups.\n\nSave this backup password?", "Backup password"))
			{
				return;
			}
			_settingsStore.Save(_settingsStore.Load() with { Password = NewPassword });
			HasSavedPassword = true;
			IsEditingPassword = false;
			NewPassword = ConfirmPassword = string.Empty;
			RevealedPassword = string.Empty;
			ErrorMessage = string.Empty;
			StatusMessage = "Backup password saved. Backups made from now on use it; earlier backups still need the password they were made with.";
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ErrorMessage = UserMessage.Of(ex);
		}
	}

	private void ShowPassword()
	{
		if (!CanRevealPassword)
		{
			return;
		}
		try
		{
			RevealedPassword = string.IsNullOrEmpty(RevealedPassword) ? _settingsStore.Load().Password ?? string.Empty : string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = UserMessage.Of(ex);
		}
	}

	private void SaveAutomatic(bool enabled)
	{
		try
		{
			_settingsStore.Save(_settingsStore.Load() with { AutomaticEnabled = enabled });
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = UserMessage.Of(ex);
		}
	}

	/// <summary>A backup never uses a password that is only typed on screen: it must be saved first, so the
	/// shop knows which password every backup is locked with.</summary>
	private string? PasswordNotReady()
	{
		if (ShowPasswordEditor && (!string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmPassword)))
		{
			return "Press Save password first. The password typed above is not saved yet, and backups are locked with the saved one.";
		}
		if (!HasSavedPassword)
		{
			return "Set a backup password and press Save password first. Without it nobody can restore the backup.";
		}
		return null;
	}

	private async Task BackUpNowAsync()
	{
		ErrorMessage = string.Empty;
		if (PasswordNotReady() is string notReady)
		{
			ErrorMessage = notReady;
			return;
		}
		IsBusy = true;
		StatusMessage = "Backing up…";
		try
		{
			string path = await Task.Run(() => _folderBackup.BackUpNowAsync());
			ShowFolder(_settingsStore.Load());
			StatusMessage = "Backed up to " + path;
		}
		catch (Exception ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = "Backup failed: " + UserMessage.Of(ex);
		}
		finally
		{
			IsBusy = false;
			RefreshLastBackup();
		}
	}

	private void RefreshDrives()
	{
		RemovableDrives = _usbBackup.ListRemovableDrives();
		SelectedDrive = RemovableDrives.FirstOrDefault(drive => drive.Root == SelectedDrive?.Root) ?? RemovableDrives.FirstOrDefault();
	}

	private async Task BackUpToUsbAsync()
	{
		ErrorMessage = string.Empty;
		if (PasswordNotReady() is string notReady)
		{
			ErrorMessage = notReady;
			return;
		}
		if (SelectedDrive == null)
		{
			ErrorMessage = "Plug in a USB drive and press Refresh drives.";
			return;
		}
		IsBusy = true;
		StatusMessage = "Backing up to " + SelectedDrive.Root + "…";
		try
		{
			string path = await Task.Run(() => _usbBackup.BackupToUsbAsync(SelectedDrive.Root));
			StatusMessage = "Backed up to USB: " + path + ". Keep the drive away from the PC.";
		}
		catch (Exception ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = "USB backup failed: " + UserMessage.Of(ex);
		}
		finally
		{
			IsBusy = false;
			RefreshLastBackup();
			RefreshDrives();
		}
	}

	private async Task RestoreBackupAsync()
	{
		string? path = _filePicker.PickBackupFile();
		if (path == null || !_confirmationService.Confirm("Restore replaces every bill, stock entry and register record on this PC with the ones in the backup. A safety backup of the current data is taken first. Pharma Remit closes when the restore finishes. Continue?", "Restore backup"))
		{
			return;
		}
		IsBusy = true;
		StatusMessage = "Checking the backup…";
		ErrorMessage = string.Empty;
		BackupRestoreResult result;
		try
		{
			result = await _folderBackup.RestoreAsync(path, RestorePassword);
		}
		catch (Exception ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = "Restore failed, nothing was changed: " + UserMessage.Of(ex);
			IsBusy = false;
			return;
		}
		// The records are now the backup's. Nothing below may report the restore as failed.
		RestorePassword = string.Empty;
		StatusMessage = "Restored. Safety copy of the previous data: " + result.SafetyBackupPath + ". Pharma Remit will now close; open it again.";
		try
		{
			_confirmationService.Notify("Restore complete", StatusMessage);
			CloseApplication();
		}
		catch (Exception ex)
		{
			ErrorMessage = "The restore finished, but Pharma Remit could not close itself: " + UserMessage.Of(ex) + " Close it and open it again before doing anything else.";
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static string Describe(DateTime? utc) => utc.HasValue ? utc.Value.ToBusinessLocal().ToString("d MMM yyyy, h:mm tt") : "never";

	private static string DaysAgo(int days) => days switch
	{
		0 => "today",
		1 => "1 day ago",
		_ => $"{days} days ago"
	};
}
