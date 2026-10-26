using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

public enum MaintenanceTab
{
	Backup,
	Import,
	Diagnostics
}

/// <summary>
/// Settings &gt; Data &amp; Maintenance: Backup &amp; Sync, the medicine data import and System Diagnostics, which used
/// to be separate sidebar items. Each tab shows the same page or panel as before; nothing runs until its tab is shown,
/// and the import starts only when its button is pressed.
/// </summary>
public sealed class DataMaintenanceViewModel(DataSyncPageViewModel dataSync, IServiceScopeFactory scopes) : ObservableObject
{
	private MaintenanceTab _tab;
	private IServiceScope? _importScope;
	private CatalogImportViewModel? _catalogImport;
	private DiagnosticsViewModel? _diagnostics;
	private RelayCommand<MaintenanceTab>? selectTabCommand;

	/// <summary>Backup &amp; Sync, with its own two tabs.</summary>
	public DataSyncPageViewModel DataSync { get; } = dataSync;

	public CatalogImportViewModel? CatalogImport
	{
		get => _catalogImport;
		private set => SetProperty(ref _catalogImport, value);
	}

	public DiagnosticsViewModel? Diagnostics
	{
		get => _diagnostics;
		private set => SetProperty(ref _diagnostics, value);
	}

	public MaintenanceTab SelectedTab => _tab;

	public bool IsBackupTab => _tab == MaintenanceTab.Backup;

	public bool IsImportTab => _tab == MaintenanceTab.Import;

	public bool IsDiagnosticsTab => _tab == MaintenanceTab.Diagnostics;

	public IRelayCommand<MaintenanceTab> SelectTabCommand => selectTabCommand ??= new RelayCommand<MaintenanceTab>(tab => _ = SelectTabAsync(tab));

	/// <summary>Chooses the tab (and Backup &amp; Sync's own tab) without loading it.</summary>
	public void ShowTab(MaintenanceTab tab, DataSyncTab? dataSyncTab = null)
	{
		_tab = tab;
		OnPropertyChanged(nameof(SelectedTab));
		OnPropertyChanged(nameof(IsBackupTab));
		OnPropertyChanged(nameof(IsImportTab));
		OnPropertyChanged(nameof(IsDiagnosticsTab));
		if (dataSyncTab is DataSyncTab syncTab)
		{
			DataSync.ShowTab(syncTab);
		}
	}

	public Task SelectTabAsync(MaintenanceTab tab)
	{
		ShowTab(tab);
		return LoadAsync();
	}

	/// <summary>Loads the shown tab, as opening its old screen did.</summary>
	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		switch (_tab)
		{
		case MaintenanceTab.Backup:
			await DataSync.LoadAsync(cancellationToken);
			break;
		case MaintenanceTab.Import:
			PrepareImport();
			break;
		case MaintenanceTab.Diagnostics:
			if (Diagnostics == null)
			{
				// Its file picker is app-wide; the diagnostics open their own database scopes as they run.
				using IServiceScope scope = scopes.CreateScope();
				Diagnostics = new DiagnosticsViewModel(scopes, scope.ServiceProvider.GetRequiredService<IFilePickerService>());
			}
			await Diagnostics.RefreshCommand.ExecuteAsync(null);
			break;
		}
	}

	// The import panel works through a database scope of its own, as the import window did. A finished panel is
	// replaced on the next visit; one still importing is kept, so the import carries on and its progress stays.
	private void PrepareImport()
	{
		if (CatalogImport is { IsImporting: true })
		{
			return;
		}
		IServiceScope? previous = _importScope;
		_importScope = scopes.CreateScope();
		CatalogImport = _importScope.ServiceProvider.GetRequiredService<CatalogImportViewModel>();
		previous?.Dispose();
	}
}
