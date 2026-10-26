using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

/// <summary>
/// Screens that became tabs keep their old sidebar keys, because dashboard links, Settings, reminders and the assistant
/// still ask for them: "Registers" / "DrugRecords" open a tab of Compliance &amp; Registers, "Backup" / "Sync" a tab of
/// Backup &amp; Sync. When that screen is already showing only the tab changes, so nothing reloads or asks for a PIN again.
/// </summary>
public sealed class MergedScreenRoutes(INavigationService navigation)
{
	private bool _keepCurrentPage;
	private ComplianceTab? _pendingCompliance;
	private DataSyncTab? _pendingDataSync;

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
		if (DataSyncPageViewModel.TabForSection(sectionKey) is DataSyncTab dataTab)
		{
			if (currentPage is DataSyncPageViewModel data)
			{
				_ = data.SelectTabAsync(dataTab);
				KeepSection(DataSyncPageViewModel.SectionKey);
			}
			else
			{
				_pendingDataSync = dataTab;
				navigation.Navigate(DataSyncPageViewModel.SectionKey);
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

	/// <summary>The tab asked for by an old key, if any; otherwise Backup &amp; Sync stays on the tab it was left on.</summary>
	public DataSyncTab? TakeDataSyncTab()
	{
		DataSyncTab? tab = _pendingDataSync;
		_pendingDataSync = null;
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
