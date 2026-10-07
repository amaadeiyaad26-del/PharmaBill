namespace PharmaBill.App.Services;

public sealed class DocumentOutputSettings
{
	public DocumentPaperSize RetailMemoPaperSize { get; set; }

	public DocumentPaperSize RetailInvoicePaperSize { get; set; }

	public string? RetailMemoPrinter { get; set; }

	public string? RetailInvoicePrinter { get; set; }

	public string FooterText { get; set; } = string.Empty;

	public string? LogoPath { get; set; }

	public string WhatsAppNumber { get; set; } = string.Empty;

	public string SmtpHost { get; set; } = string.Empty;

	public int SmtpPort { get; set; } = 587;

	public string SmtpFromAddress { get; set; } = string.Empty;

	public string SmtpUserName { get; set; } = string.Empty;

	public bool SmtpEnableSsl { get; set; } = true;

	public bool CashDrawerPulse { get; set; }

	public string? InspectorPinHash { get; set; }

	public InvoiceTemplateType DefaultInvoiceTemplate { get; set; } = InvoiceTemplateType.AlwaysAsk;

	public bool SilentPrint { get; set; } = true;

	public bool PrintDoctorName { get; set; } = true;

	public bool PrintCustomerPhone { get; set; } = true;

	public bool PrintShopLogo { get; set; } = true;

	public bool PrintTermsDisclaimer { get; set; } = true;

	public string AddressLine2 { get; set; } = string.Empty;

	public string FssaiNumber { get; set; } = string.Empty;

	public string DrugLicence20B { get; set; } = string.Empty;

	public string DrugLicence21B { get; set; } = string.Empty;

	public string TermsAndDisclaimer { get; set; } = "Goods once sold will not be taken back. Schedule H/H1 medicines require a valid prescription. Please verify batch and expiry before use.";

	public bool ShowCustomerQrWindow { get; set; }

	public string UpiPayeeName { get; set; } = string.Empty;
}
