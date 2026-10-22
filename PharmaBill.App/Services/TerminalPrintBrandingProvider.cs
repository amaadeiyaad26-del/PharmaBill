using PharmaBill.Data.Services;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App.Services;

/// <summary>Bills saved from a terminal carry this server PC's print settings, the same as a bill made at its own counter.</summary>
public sealed class TerminalPrintBrandingProvider(DocumentOutputSettingsStore settings) : ITerminalPrintBrandingProvider
{
	public RetailPrintBranding? Current() => InvoicePrintService.BuildPrintBranding(settings.Load());
}
