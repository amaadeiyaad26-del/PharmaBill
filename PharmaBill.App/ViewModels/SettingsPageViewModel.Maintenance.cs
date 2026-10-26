using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace PharmaBill.App.ViewModels;

// Settings > Data & Maintenance hosts Backup & Sync, the medicine data import and System Diagnostics
// (DataMaintenanceViewModel). This part wires it into the Data category and the routes that open it.
public partial class SettingsPageViewModel
{
	private bool _suppressMaintenanceLoad;

	public DataMaintenanceViewModel? Maintenance { get; private set; }

	/// <summary>The Data &amp; Maintenance block: in the Data category, or when a search finds backup or sync.</summary>
	public bool ShowDataMaintenance => ShowSyncShortcut || ShowBackupShortcut;

	/// <summary>Gives Settings the Backup &amp; Sync page and a way to build the import and diagnostics panels.</summary>
	public void AttachMaintenance(DataSyncPageViewModel dataSync, IServiceScopeFactory scopes)
	{
		Maintenance = new DataMaintenanceViewModel(dataSync, scopes);
		OnPropertyChanged(nameof(Maintenance));
	}

	/// <summary>Opens Data &amp; Maintenance on a tab without loading it (opening Settings loads it).</summary>
	public void ShowMaintenance(MaintenanceTab tab, DataSyncTab? dataSyncTab = null)
	{
		_suppressMaintenanceLoad = true;
		try
		{
			SelectSettingsCategory(SettingsCategories.Data);
		}
		finally
		{
			_suppressMaintenanceLoad = false;
		}
		Maintenance?.ShowTab(tab, dataSyncTab);
	}

	/// <summary>Loads the shown maintenance tab, when Data &amp; Maintenance is on screen.</summary>
	public Task LoadMaintenanceAsync(CancellationToken cancellationToken = default) =>
		ShowDataMaintenance && Maintenance != null ? Maintenance.LoadAsync(cancellationToken) : Task.CompletedTask;

	private const string MaintenanceSearchImport = "dataImport";

	private const string MaintenanceSearchDiagnostics = "diagnostics";

	// A settings search for the import or diagnostics shows Data & Maintenance on that tab.
	private bool ShowMaintenanceForSearch(IReadOnlySet<string> found)
	{
		MaintenanceTab? tab = found.Contains(MaintenanceSearchDiagnostics) ? MaintenanceTab.Diagnostics
			: found.Contains(MaintenanceSearchImport) ? MaintenanceTab.Import
			: null;
		if (tab is MaintenanceTab shown && Maintenance != null)
		{
			// Each keystroke searches again: switch (and load) only when the tab changes.
			if (Maintenance.SelectedTab != shown)
			{
				_ = Maintenance.SelectTabAsync(shown);
			}
			return true;
		}
		return false;
	}

	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.PropertyName is nameof(ShowSyncShortcut) or nameof(ShowBackupShortcut))
		{
			base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowDataMaintenance)));
		}
	}
}
