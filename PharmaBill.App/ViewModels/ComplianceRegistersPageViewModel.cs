using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PharmaBill.App.ViewModels;

public enum ComplianceTab
{
	StatutoryRegisters,
	DrugRecords
}

/// <summary>
/// Compliance &amp; Registers: the statutory registers and the drug records movement report under one sidebar item.
/// Both pages live for the whole visit, so switching tabs keeps their filters and results. Each still asks for the PIN
/// (and records the access) the first time its tab is opened, exactly as when they were separate screens.
/// </summary>
public sealed class ComplianceRegistersPageViewModel : SectionPageViewModel, ILoadablePage
{
	private ComplianceTab _selectedTab;
	private bool _registersOpened;
	private bool _drugRecordsOpened;
	private RelayCommand<ComplianceTab>? selectTabCommand;

	public ComplianceRegistersPageViewModel(string title, StatutoryRegistersPageViewModel registers, DrugRecordsPageViewModel drugRecords, ComplianceTab initialTab = ComplianceTab.StatutoryRegisters)
		: base(title)
	{
		Registers = registers;
		DrugRecords = drugRecords;
		_selectedTab = initialTab;
	}

	/// <summary>The sidebar key of this screen.</summary>
	public const string SectionKey = "Compliance";

	/// <summary>The screens this one replaced keep their keys (dashboard links, the assistant): each opens its tab here.</summary>
	public static ComplianceTab? TabForSection(string? sectionKey) => sectionKey switch
	{
		"Registers" => ComplianceTab.StatutoryRegisters,
		"DrugRecords" => ComplianceTab.DrugRecords,
		_ => null,
	};

	public StatutoryRegistersPageViewModel Registers { get; }

	public DrugRecordsPageViewModel DrugRecords { get; }

	public ComplianceTab SelectedTab => _selectedTab;

	public bool IsRegistersTab => _selectedTab == ComplianceTab.StatutoryRegisters;

	public bool IsDrugRecordsTab => _selectedTab == ComplianceTab.DrugRecords;

	/// <summary>The page shown under the tabs.</summary>
	public SectionPageViewModel ActivePage => IsRegistersTab ? Registers : DrugRecords;

	public IRelayCommand<ComplianceTab> SelectTabCommand => selectTabCommand ??= new RelayCommand<ComplianceTab>(tab => _ = SelectTabAsync(tab));

	public Task LoadAsync(CancellationToken cancellationToken = default) => OpenActiveTabAsync(cancellationToken);

	public async Task SelectTabAsync(ComplianceTab tab)
	{
		if (tab != _selectedTab)
		{
			_selectedTab = tab;
			OnPropertyChanged(nameof(SelectedTab));
			OnPropertyChanged(nameof(IsRegistersTab));
			OnPropertyChanged(nameof(IsDrugRecordsTab));
			OnPropertyChanged(nameof(ActivePage));
		}
		await OpenActiveTabAsync();
	}

	// A tab is unlocked once per visit. One whose PIN was cancelled asks again when it is chosen again.
	private async Task OpenActiveTabAsync(CancellationToken cancellationToken = default)
	{
		if (IsRegistersTab)
		{
			if (!_registersOpened || !Registers.IsUnlocked)
			{
				_registersOpened = true;
				await Registers.LoadAsync(cancellationToken);
			}
		}
		else if (!_drugRecordsOpened || !DrugRecords.IsUnlocked)
		{
			_drugRecordsOpened = true;
			await DrugRecords.LoadAsync(cancellationToken);
		}
	}
}
