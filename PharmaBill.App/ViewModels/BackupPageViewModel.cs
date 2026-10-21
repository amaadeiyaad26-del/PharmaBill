using PharmaBill.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class BackupPageViewModel : ObservableObject, ILoadablePage
{
	private readonly BackupSettingsStore _settingsStore;

	private readonly FolderBackupService _folderBackup;

	private readonly UsbBackupSettingsStore _usbSettings;

	private readonly IFilePickerService _filePicker;

	private readonly IConfirmationService _confirmationService;

	private readonly CurrentSession _session;

	private string _backupFolder = string.Empty;

	private string _newPassword = string.Empty;

	private string _confirmPassword = string.Empty;

	private string _restorePassword = string.Empty;

	private bool _hasSavedPassword;

	private bool _isEditingPassword;

	private string _revealedPassword = string.Empty;

	private bool _automaticEnabled;

	private bool _isBusy;

	private BackupReminderState _reminder = BackupReminder.Evaluate(null, null, null, BusinessClock.UtcNow);

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	public BackupPageViewModel(BackupSettingsStore settingsStore, FolderBackupService folderBackup, UsbBackupSettingsStore usbSettings, IFilePickerService filePicker, IConfirmationService confirmationService, CurrentSession session)
	{
		_settingsStore = settingsStore;
		_folderBackup = folderBackup;
		_usbSettings = usbSettings;
		_filePicker = filePicker;
		_confirmationService = confirmationService;
		_session = session;
		ChooseFolderCommand = new RelayCommand(ChooseFolder);
		SavePasswordCommand = new RelayCommand(SavePassword);
		ChangePasswordCommand = new RelayCommand(() => IsEditingPassword = true);
		ShowPasswordCommand = new RelayCommand(ShowPassword);
		BackUpNowCommand = new AsyncRelayCommand(BackUpNowAsync, () => !IsBusy);
		RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsBusy);
	}

	public string BackupFolder
	{
		get => _backupFolder;
		private set
		{
			if (SetProperty(ref _backupFolder, value))
			{
				OnPropertyChanged(nameof(HasBackupFolder));
			}
		}
	}

	public bool HasBackupFolder => !string.IsNullOrWhiteSpace(BackupFolder);

	public string NewPassword
	{
		get => _newPassword;
		set => SetProperty(ref _newPassword, value);
	}

	public string ConfirmPassword
	{
		get => _confirmPassword;
		set => SetProperty(ref _confirmPassword, value);
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

	public bool IsBusy
	{
		get => _isBusy;
		private set
		{
			if (SetProperty(ref _isBusy, value))
			{
				BackUpNowCommand.NotifyCanExecuteChanged();
				RestoreBackupCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string LastBackupLabel => _reminder.LastBackupUtc.HasValue
		? $"{_reminder.LastBackupUtc.Value.ToBusinessLocal():d MMM yyyy, h:mm tt}  ·  {DaysAgo(_reminder.DaysSinceLastBackup ?? 0)}"
		: "No backup yet";

	public bool IsBackupStale => _reminder.DaysSinceLastBackup is null or > BackupReminder.MaxAgeDays;

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

	public IAsyncRelayCommand RestoreBackupCommand { get; }

	public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			BackupSettings settings = _settingsStore.Load();
			BackupFolder = settings.PrimaryFolder ?? string.Empty;
			HasSavedPassword = !string.IsNullOrWhiteSpace(settings.Password);
			_automaticEnabled = settings.AutomaticEnabled;
			OnPropertyChanged(nameof(AutomaticEnabled));
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = ex.Message;
		}
		IsEditingPassword = false;
		RevealedPassword = string.Empty;
		NewPassword = ConfirmPassword = string.Empty;
		OnPropertyChanged(nameof(CanRevealPassword));
		RefreshLastBackup();
		return Task.CompletedTask;
	}

	private void RefreshLastBackup()
	{
		DateTime? folder = null;
		try
		{
			folder = _settingsStore.Load().LastBackupAtUtc;
		}
		catch (InvalidOperationException)
		{
		}
		_reminder = BackupReminder.Evaluate(folder, _usbSettings.Load().LastUsbBackupUtc, null, BusinessClock.UtcNow);
		OnPropertyChanged(nameof(LastBackupLabel));
		OnPropertyChanged(nameof(IsBackupStale));
	}

	private void ChooseFolder()
	{
		string? folder = _filePicker.PickFolder("Choose the folder for Pharma Remit backups");
		if (folder == null)
		{
			return;
		}
		try
		{
			_settingsStore.Save(_settingsStore.Load() with { PrimaryFolder = folder });
			BackupFolder = folder;
			ErrorMessage = string.Empty;
			StatusMessage = "Backups will be saved in " + folder;
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void SavePassword()
	{
		try
		{
			EncryptedBackupService.ValidatePassword(NewPassword);
			if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
			{
				throw new ArgumentException("The two passwords do not match.");
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
			ErrorMessage = ex.Message;
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
			ErrorMessage = ex.Message;
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
			ErrorMessage = ex.Message;
		}
	}

	private async Task BackUpNowAsync()
	{
		IsBusy = true;
		StatusMessage = "Backing up…";
		ErrorMessage = string.Empty;
		try
		{
			string path = await Task.Run(() => _folderBackup.BackUpNowAsync());
			StatusMessage = "Backed up to " + path;
		}
		catch (Exception ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = "Backup failed: " + ex.Message;
		}
		finally
		{
			IsBusy = false;
			RefreshLastBackup();
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
		try
		{
			BackupRestoreResult result = await _folderBackup.RestoreAsync(path, RestorePassword);
			RestorePassword = string.Empty;
			StatusMessage = "Restored. Safety copy of the previous data: " + result.SafetyBackupPath + ". Pharma Remit will now close; open it again.";
			_confirmationService.Notify("Restore complete", StatusMessage);
			Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = "Restore failed, nothing was changed: " + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static string DaysAgo(int days) => days switch
	{
		0 => "today",
		1 => "1 day ago",
		_ => $"{days} days ago"
	};
}
