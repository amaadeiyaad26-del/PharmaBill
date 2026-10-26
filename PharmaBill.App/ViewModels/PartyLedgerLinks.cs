using CommunityToolkit.Mvvm.Input;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// Accounts & Parties: a patient, buyer or supplier in the directory opens their ledger in the Ledgers tab, so the
// directory and the balances are one place rather than two copies.

public partial class WholesaleAccountsPageViewModel
{
	private int _selectedLedgerTab;

	/// <summary>The ledger page's own tabs: 0 supplier payables, 1 customer accounts.</summary>
	public int SelectedLedgerTab
	{
		get => _selectedLedgerTab;
		set => SetProperty(ref _selectedLedgerTab, value);
	}

	/// <summary>Shows one party's ledger: a customer's account or a supplier's payables. Call after the page has loaded.</summary>
	public bool ShowLedgerOf(Guid? customerId, Guid? supplierId)
	{
		if (customerId is Guid customer && Customers.FirstOrDefault(item => item.Id == customer) is Customer found)
		{
			SelectedLedgerTab = 1;
			SelectedCustomer = found;
			return true;
		}
		if (supplierId is Guid supplier && Suppliers.FirstOrDefault(item => item.Id == supplier) is Supplier match)
		{
			SelectedLedgerTab = 0;
			SelectedSupplier = match;
			return true;
		}
		return false;
	}
}

public partial class WholesaleCustomerPageViewModel
{
	private RelayCommand? openLedgerCommand;

	/// <summary>Asks for the selected patient's or buyer's ledger (Accounts &amp; Parties opens it in the Ledgers tab).</summary>
	public event Action<Guid>? OpenLedgerRequested;

	// The button is disabled while nothing is selected (a trigger on the selection in the page's template).
	public IRelayCommand OpenLedgerCommand => openLedgerCommand ??= new RelayCommand(() =>
	{
		if (SelectedRecord is WholesaleCustomerRecord { Customer.Id: var id } && id != Guid.Empty)
		{
			OpenLedgerRequested?.Invoke(id);
		}
	});
}

public sealed partial class SupplierDirectoryPageViewModel
{
	private RelayCommand? openLedgerCommand;

	/// <summary>Asks for the selected supplier's payables ledger (Accounts &amp; Parties opens it in the Ledgers tab).</summary>
	public event Action<Guid>? OpenLedgerRequested;

	// The button is disabled while nothing is selected (a trigger on the selection in the page's template).
	public IRelayCommand OpenLedgerCommand => openLedgerCommand ??= new RelayCommand(() =>
	{
		if (SelectedSupplier is { Id: var id } && id != Guid.Empty)
		{
			OpenLedgerRequested?.Invoke(id);
		}
	});
}

/// <summary>Connects the directory tabs of Accounts &amp; Parties to its Ledgers tab.</summary>
public static class PartyLedgerLinks
{
	public const string LedgersTab = "ledgers";

	public static void Connect(HubPageViewModel hub, WholesaleAccountsPageViewModel accounts, WholesaleCustomerPageViewModel customers, SupplierDirectoryPageViewModel suppliers)
	{
		customers.OpenLedgerRequested += customerId => _ = OpenAsync(hub, accounts, customerId, null);
		suppliers.OpenLedgerRequested += supplierId => _ = OpenAsync(hub, accounts, null, supplierId);
	}

	/// <summary>Shows the Ledgers tab (which loads the balances) on that party's account.</summary>
	public static async Task OpenAsync(HubPageViewModel hub, WholesaleAccountsPageViewModel accounts, Guid? customerId, Guid? supplierId)
	{
		await hub.SelectTabAsync(LedgersTab);
		accounts.ShowLedgerOf(customerId, supplierId);
	}
}
