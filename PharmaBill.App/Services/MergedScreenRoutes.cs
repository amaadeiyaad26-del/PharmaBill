using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

/// <summary>
/// Screens that became tabs keep their old sidebar keys, because dashboard links, Settings, reminders and the assistant
/// still ask for them: "Registers" / "DrugRecords" open a tab of Compliance &amp; Registers, "Stock" / "StockInHand" /
/// "StockTransfer" a tab of Stock Management, the customer, supplier and ledger keys a tab of Accounts &amp; Parties, and
/// "Backup" / "Sync" the Data &amp; Maintenance part of Settings. When that screen is already showing only the tab
/// changes, so nothing reloads or asks for a PIN again.
/// </summary>
public sealed class MergedScreenRoutes(INavigationService navigation)
{
	/// <summary>The sidebar key of Settings, where Backup &amp; Sync now lives.</summary>
	public const string SettingsSectionKey = "Settings";

	private readonly Dictionary<string, (string Hub, string Tab)> _hubAliases = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _pendingHubTabs = new(StringComparer.Ordinal);
	private bool _keepCurrentPage;
	private ComplianceTab? _pendingCompliance;

	/// <summary>
	/// Shows Backup &amp; Sync (on the tab given, if any) inside Settings. The flag says whether Settings is already on
	/// screen, in which case the page is loaded at once; otherwise opening Settings loads it.
	/// </summary>
	public Func<DataSyncTab?, bool, Task>? ShowDataSyncInSettings { get; set; }

	/// <summary>Makes an old key open a tab of a hub.</summary>
	public void AddHubAlias(string oldKey, string hubKey, string tabKey) => _hubAliases[oldKey] = (hubKey, tabKey);

	/// <summary>Handles an old key; false for any other key. <paramref name="currentPage"/> is the screen showing now.</summary>
	public bool TryOpen(string sectionKey, object? currentPage)
	{
		if (ComplianceRegistersPageViewModel.TabForSection(sectionKey) is ComplianceTab complianceTab)
		{
			if (currentPage is ComplianceRegistersPageViewModel compliance)
			{
				_ = compliance.SelectTabAsync(complianceTab);
				KeepSection(ComplianceRegistersPageViewModel.SectionKey);
			}
			else
			{
				_pendingCompliance = complianceTab;
				navigation.Navigate(ComplianceRegistersPageViewModel.SectionKey);
			}
			return true;
		}
		if (ShowDataSyncInSettings != null && (sectionKey == DataSyncPageViewModel.SectionKey || DataSyncPageViewModel.TabForSection(sectionKey) is not null))
		{
			bool onSettings = currentPage is SettingsPageViewModel;
			_ = ShowDataSyncInSettings(DataSyncPageViewModel.TabForSection(sectionKey), onSettings);
			if (onSettings)
			{
				KeepSection(SettingsSectionKey);
			}
			else
			{
				navigation.Navigate(SettingsSectionKey);
			}
			return true;
		}
		if (_hubAliases.TryGetValue(sectionKey, out (string Hub, string Tab) alias))
		{
			if (currentPage is HubPageViewModel hub && hub.SectionKey == alias.Hub)
			{
				_ = hub.SelectTabAsync(alias.Tab);
				KeepSection(alias.Hub);
			}
			else
			{
				_pendingHubTabs[alias.Hub] = alias.Tab;
				navigation.Navigate(alias.Hub);
			}
			return true;
		}
		return false;
	}

	/// <summary>True once after only the sidebar key was corrected: the screen showing already is that section.</summary>
	public bool ConsumeKeepCurrentPage()
	{
		bool keep = _keepCurrentPage;
		_keepCurrentPage = false;
		return keep;
	}

	/// <summary>The tab a new Compliance &amp; Registers screen opens on: the one asked for, else the registers.</summary>
	public ComplianceTab TakeComplianceTab()
	{
		ComplianceTab tab = _pendingCompliance ?? ComplianceTab.StatutoryRegisters;
		_pendingCompliance = null;
		return tab;
	}

	/// <summary>The tab an old key asked a hub to open on, if any; otherwise the hub stays on the tab it was left on.</summary>
	public string? TakeHubTab(string hubKey)
	{
		_pendingHubTabs.Remove(hubKey, out string? tab);
		return tab;
	}

	private void KeepSection(string sectionKey)
	{
		if (navigation.CurrentSectionKey != sectionKey)
		{
			_keepCurrentPage = true;
			navigation.Navigate(sectionKey);
		}
	}
}
