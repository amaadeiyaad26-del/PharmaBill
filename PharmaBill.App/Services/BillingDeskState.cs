namespace PharmaBill.App.Services;

/// <summary>
/// The billing desk currently on screen, published by the main window so pages outside it (such as Quotations)
/// can tell whether a retail or wholesale bill can be opened.
/// </summary>
public sealed class BillingDeskState
{
	public ActiveBillingDeskMode Current { get; set; } = ActiveBillingDeskMode.Retail;

	public bool CanBillRetail => Current != ActiveBillingDeskMode.Wholesale;

	public bool CanBillWholesale => Current != ActiveBillingDeskMode.Retail;
}
