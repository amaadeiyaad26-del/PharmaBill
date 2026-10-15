namespace PharmaBill.Core.Gst;

public static class GstThresholds
{
	/// <summary>
	/// GSTR-1 Table 5 (B2C Large): inter-state supplies to unregistered persons with invoice value above this
	/// amount are reported invoice-wise with place of supply. ₹1,00,000 per CBIC Notification No. 12/2024–Central
	/// Tax (10 July 2024), effective 1 August 2024; it was ₹2,50,000 before. Configurable per pharmacy because the
	/// limit is set by notification and can change again. Not yet verified against the notification text; see
	/// docs/GST_B2CL_THRESHOLD.md before changing it.
	/// </summary>
	public const decimal DefaultB2cLargeInvoiceThreshold = 100000m;
}
