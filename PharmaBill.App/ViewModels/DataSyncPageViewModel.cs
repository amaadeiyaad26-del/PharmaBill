using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PharmaBill.App.ViewModels;

public enum DataSyncTab
{
	Backup,
	Sync
}

/// <summary>
/// Backup &amp; Sync: local backup and PC migration, and device and cloud sync, as two tabs of one screen. The two pages
/// are the same long-lived ones as before; a tab loads its page each time it is shown, as opening the old screens did.
/// </summary>
public sealed class DataSyncPageViewModel : SectionPageViewModel, ILoadablePage
{
	/// <summary>The sidebar key of this screen.</summary>
	public const string SectionKey = "DataSync";

	private DataSyncTab _selectedTab;
	private RelayCommand<DataSyncTab>? selectTabCommand;

	public DataSyncPageViewModel(string title, BackupPageViewModel backup, SyncSettingsPageViewModel sync)
		: base(title)
	{
		Backup = backup;
		Sync = sync;
		backup.ShowPageTitle = false;
		sync.ShowPageTitle = false;
	}

	/// <summary>The screens this one replaced keep their keys (Settings links, reminders, the assistant): each opens its tab here.</summary>
	public static DataSyncTab? TabForSection(string? sectionKey) => sectionKey switch
	{
		"Backup" => DataSyncTab.Backup,
		"Sync" => DataSyncTab.Sync,
		_ => null,
	};

	public BackupPageViewModel Backup { get; }

	public SyncSettingsPageViewModel Sync { get; }

	public DataSyncTab SelectedTab => _selectedTab;

	public bool IsBackupTab => _selectedTab == DataSyncTab.Backup;

	public bool IsSyncTab => _selectedTab == DataSyncTab.Sync;

	/// <summary>The page shown under the tabs.</summary>
	public ILoadablePage ActivePage => IsBackupTab ? Backup : Sync;

	public IRelayCommand<DataSyncTab> SelectTabCommand => selectTabCommand ??= new RelayCommand<DataSyncTab>(tab => _ = SelectTabAsync(tab));

	public Task LoadAsync(CancellationToken cancellationToken = default) => ActivePage.LoadAsync(cancellationToken);

	/// <summary>Chooses the tab the next visit opens on, without loading it.</summary>
	public void ShowTab(DataSyncTab tab)
	{
		if (tab == _selectedTab)
		{
			return;
		}
		_selectedTab = tab;
		OnPropertyChanged(nameof(SelectedTab));
		OnPropertyChanged(nameof(IsBackupTab));
		OnPropertyChanged(nameof(IsSyncTab));
		OnPropertyChanged(nameof(ActivePage));
	}

	public Task SelectTabAsync(DataSyncTab tab)
	{
		ShowTab(tab);
		return LoadAsync();
	}
}
