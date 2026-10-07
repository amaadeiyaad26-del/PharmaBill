using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class BackupPageViewModel(BackupSettingsStore settingsStore, IServiceScopeFactory scopeFactory, IFilePickerService filePicker, IConfirmationService confirmationService) : ObservableObject, ILoadablePage
{
	private string _primaryFolder = string.Empty;

	private string _secondaryFolder = string.Empty;

	private string _backupPassword = string.Empty;

	private bool _automaticEnabled;

	private DateTime? _lastBackupAtUtc;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? choosePrimaryFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? chooseSecondaryFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? createBackupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreBackupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportEverythingCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? importOnNewPcCommand;

	public bool IsBackupOlderThanSevenDays
	{
		get
		{
			if (LastBackupAtUtc.HasValue)
			{
				return DateTime.UtcNow - LastBackupAtUtc.Value > TimeSpan.FromDays(7);
			}
			return true;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PrimaryFolder
	{
		get
		{
			return _primaryFolder;
		}
		[MemberNotNull("_primaryFolder")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_primaryFolder, value))
			{
				OnPropertyChanging(nameof(PrimaryFolder));
				_primaryFolder = value;
				OnPropertyChanged(nameof(PrimaryFolder));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SecondaryFolder
	{
		get
		{
			return _secondaryFolder;
		}
		[MemberNotNull("_secondaryFolder")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_secondaryFolder, value))
			{
				OnPropertyChanging(nameof(SecondaryFolder));
				_secondaryFolder = value;
				OnPropertyChanged(nameof(SecondaryFolder));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BackupPassword
	{
		get
		{
			return _backupPassword;
		}
		[MemberNotNull("_backupPassword")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_backupPassword, value))
			{
				OnPropertyChanging(nameof(BackupPassword));
				_backupPassword = value;
				OnPropertyChanged(nameof(BackupPassword));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AutomaticEnabled
	{
		get
		{
			return _automaticEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_automaticEnabled, value))
			{
				OnPropertyChanging(nameof(AutomaticEnabled));
				_automaticEnabled = value;
				OnPropertyChanged(nameof(AutomaticEnabled));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? LastBackupAtUtc
	{
		get
		{
			return _lastBackupAtUtc;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_lastBackupAtUtc, value))
			{
				OnPropertyChanging(nameof(LastBackupAtUtc));
				_lastBackupAtUtc = value;
				OnPropertyChanged(nameof(LastBackupAtUtc));
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
				OnPropertyChanging(nameof(StatusMessage));
				_statusMessage = value;
				OnPropertyChanged(nameof(StatusMessage));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ChoosePrimaryFolderCommand => choosePrimaryFolderCommand ?? (choosePrimaryFolderCommand = new RelayCommand(ChoosePrimaryFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ChooseSecondaryFolderCommand => chooseSecondaryFolderCommand ?? (chooseSecondaryFolderCommand = new RelayCommand(ChooseSecondaryFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveSettingsCommand => saveSettingsCommand ?? (saveSettingsCommand = new RelayCommand(SaveSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CreateBackupCommand => createBackupCommand ?? (createBackupCommand = new AsyncRelayCommand(CreateBackupAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreBackupCommand => restoreBackupCommand ?? (restoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportEverythingCommand => exportEverythingCommand ?? (exportEverythingCommand = new AsyncRelayCommand(ExportEverythingAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportOnNewPcCommand => importOnNewPcCommand ?? (importOnNewPcCommand = new AsyncRelayCommand(ImportOnNewPcAsync));

	public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		BackupSettings backupSettings = settingsStore.Load();
		PrimaryFolder = backupSettings.PrimaryFolder ?? string.Empty;
		SecondaryFolder = backupSettings.SecondaryFolder ?? string.Empty;
		BackupPassword = backupSettings.Password ?? string.Empty;
		AutomaticEnabled = backupSettings.AutomaticEnabled;
		LastBackupAtUtc = backupSettings.LastBackupAtUtc;
		OnPropertyChanged("IsBackupOlderThanSevenDays");
		return Task.CompletedTask;
	}

	private void ChoosePrimaryFolder()
	{
		string text = filePicker.PickFolder("Choose the automatic backup folder");
		if (text != null)
		{
			PrimaryFolder = text;
		}
	}

	private void ChooseSecondaryFolder()
	{
		string text = filePicker.PickFolder("Choose optional second backup location (USB or synced drive)");
		if (text != null)
		{
			SecondaryFolder = text;
		}
	}

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
			BackupSettings backupSettings = settingsStore.Load();
			settingsStore.Save(new BackupSettings(NullIfEmpty(PrimaryFolder), NullIfEmpty(SecondaryFolder), NullIfEmpty(BackupPassword), AutomaticEnabled, backupSettings.LastBackupAtUtc));
			StatusMessage = "Backup settings saved. Automatic backup runs daily while PharmaBill is open.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task CreateBackupAsync()
	{
		_ = 4;
		try
		{
			EncryptedBackupService.ValidatePassword(BackupPassword);
			string target = filePicker.PickExportDestination("pbbak", $"PharmaBill-backup-{DateTime.Now:yyyyMMdd-HHmm}");
			if (target == null)
			{
				return;
			}
			using IServiceScope scope = scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(target, BackupPassword);
			BackupSettings settings = settingsStore.Load();
			if (!string.IsNullOrWhiteSpace(settings.SecondaryFolder))
			{
				Directory.CreateDirectory(settings.SecondaryFolder);
				await using FileStream source = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
				await using FileStream secondCopy = new FileStream(Path.Combine(settings.SecondaryFolder, Path.GetFileName(target)), FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
				await source.CopyToAsync(secondCopy);
				await secondCopy.FlushAsync();
			}
			LastBackupAtUtc = DateTime.UtcNow;
			settingsStore.Save(settings with
			{
				LastBackupAtUtc = LastBackupAtUtc
			});
			OnPropertyChanged("IsBackupOlderThanSevenDays");
			StatusMessage = "Encrypted backup created: " + target;
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task RestoreBackupAsync()
	{
		string text = filePicker.PickBackupFile();
		if (text == null || !confirmationService.Confirm("Restore replaces the current database and attachments. A safety backup will be taken first. PharmaBill must restart after restore. Continue?", "Restore encrypted backup"))
		{
			return;
		}
		try
		{
			EncryptedBackupService.ValidatePassword(BackupPassword);
			using IServiceScope scope = scopeFactory.CreateScope();
			StatusMessage = "Restore validated. Safety backup: " + (await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().RestoreBackupAsync(text, BackupPassword)).SafetyBackupPath + ". Restarting PharmaBill.";
			ErrorMessage = string.Empty;
			await Task.Delay(500);
			Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ExportEverythingAsync()
	{
		await CreateBackupAsync();
	}

	private async Task ImportOnNewPcAsync()
	{
		await RestoreBackupAsync();
	}

	private static string? NullIfEmpty(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
