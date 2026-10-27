using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Fluent;

namespace PharmaBill.App.Services;

public sealed class InvoicePrintService(IServiceScopeFactory scopeFactory, DocumentOutputSettingsStore settingsStore, RetailBillPdfService pdfService) : IInvoicePrintService
{
	public async Task<string> PrintAsync(Guid saleId, InvoiceTemplateType? templateOverride = null, bool? silentOverride = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		DocumentOutputSettings settings = settingsStore.Load();
		InvoiceTemplateType template = templateOverride ?? settings.DefaultInvoiceTemplate;
		if (template == InvoiceTemplateType.AlwaysAsk)
		{
			(InvoiceTemplateType, bool)? tuple = AskForTemplate(previewOnly: false);
			if (!tuple.HasValue)
			{
				return "cancelled";
			}
			template = tuple.Value.Item1;
		}
		bool silent = silentOverride ?? settings.SilentPrint;
		RetailBillPrintData bill = await LoadBillAsync(saleId, settings, cancellationToken);
		return template switch
		{
			InvoiceTemplateType.Thermal80mm => await PrintThermalAsync(bill, settings, cancellationToken),
			InvoiceTemplateType.StandardA4 => await PrintStandardInvoiceAsync(bill, settings, silent, cancellationToken),
			_ => await PrintThermalAsync(bill, settings, cancellationToken),
		};
	}

	/// <summary>Prints a bill a billing terminal received from its server, on this PC's own printers and layout.</summary>
	public static async Task<string> PrintReceivedAsync(RetailBillPrintData bill, DocumentOutputSettings settings, CancellationToken cancellationToken = default)
	{
		InvoiceTemplateType template = settings.DefaultInvoiceTemplate;
		if (template == InvoiceTemplateType.AlwaysAsk)
		{
			(InvoiceTemplateType, bool)? chosen = AskForTemplate(previewOnly: false);
			if (!chosen.HasValue)
			{
				return "cancelled";
			}
			template = chosen.Value.Item1;
		}
		return template == InvoiceTemplateType.StandardA4
			? await PrintStandardInvoiceAsync(bill, settings, settings.SilentPrint, cancellationToken)
			: await PrintThermalAsync(bill, settings, cancellationToken);
	}

	public async Task<string> PreviewAsync(Guid saleId, InvoiceTemplateType template = InvoiceTemplateType.StandardA4, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (template == InvoiceTemplateType.Thermal80mm)
		{
			DocumentOutputSettings settings = settingsStore.Load();
			RetailBillPrintData bill = await LoadBillAsync(saleId, settings, cancellationToken);
			string path = Path.Combine(Path.GetTempPath(), $"PharmaBill-thermal-{saleId:N}.txt");
			await File.WriteAllTextAsync(path, ThermalReceiptFormatter.Format(bill, settings), cancellationToken);
			Process.Start(new ProcessStartInfo(path)
			{
				UseShellExecute = true
			});
			return path;
		}
		RetailDocumentType documentType = RetailDocumentType.Invoice;
		return await pdfService.PreviewAsync(saleId, documentType, cancellationToken);
	}

	public async Task<string?> PickFormatAndPrintAsync(Guid saleId, CancellationToken cancellationToken = default(CancellationToken))
	{
		(InvoiceTemplateType, bool)? tuple = AskForTemplate(previewOnly: true);
		if (!tuple.HasValue)
		{
			return null;
		}
		if (tuple.Value.Item2)
		{
			return await PreviewAsync(saleId, tuple.Value.Item1, cancellationToken);
		}
		return await PrintAsync(saleId, tuple.Value.Item1, false, cancellationToken);
	}

	private async Task<RetailBillPrintData> LoadBillAsync(Guid saleId, DocumentOutputSettings settings, CancellationToken cancellationToken)
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		return EnrichWithPrintBranding(await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetPrintableBillAsync(saleId, cancellationToken), settings);
	}

	/// <summary>Print-settings content recorded on a retail sale at save so reprints never pick up later settings.</summary>
	internal static RetailPrintBranding BuildPrintBranding(DocumentOutputSettings settings)
	{
		List<string> extra = new List<string>();
		if (!string.IsNullOrWhiteSpace(settings.DrugLicence20B))
		{
			extra.Add("20B " + settings.DrugLicence20B.Trim());
		}
		if (!string.IsNullOrWhiteSpace(settings.DrugLicence21B))
		{
			extra.Add("21B " + settings.DrugLicence21B.Trim());
		}
		return new RetailPrintBranding(
			extra,
			string.IsNullOrWhiteSpace(settings.FssaiNumber) ? null : settings.FssaiNumber.Trim(),
			string.IsNullOrWhiteSpace(settings.AddressLine2) ? null : settings.AddressLine2.Trim(),
			settings.PrintTermsDisclaimer && !string.IsNullOrWhiteSpace(settings.TermsAndDisclaimer) ? settings.TermsAndDisclaimer.Trim() : null);
	}

	internal static RetailBillPrintData EnrichWithPrintBranding(RetailBillPrintData bill, DocumentOutputSettings settings)
	{
		if (bill.DetailsRecorded)
		{
			return bill;
		}
		List<string> list = bill.LicenceNumbers.ToList();
		if (!string.IsNullOrWhiteSpace(settings.DrugLicence20B) && !list.Any((string item) => item.Contains(settings.DrugLicence20B, StringComparison.OrdinalIgnoreCase)))
		{
			list.Add("20B " + settings.DrugLicence20B.Trim());
		}
		if (!string.IsNullOrWhiteSpace(settings.DrugLicence21B) && !list.Any((string item) => item.Contains(settings.DrugLicence21B, StringComparison.OrdinalIgnoreCase)))
		{
			list.Add("21B " + settings.DrugLicence21B.Trim());
		}
		return bill with
		{
			LicenceNumbers = list,
			FssaiNumber = (string.IsNullOrWhiteSpace(settings.FssaiNumber) ? bill.FssaiNumber : settings.FssaiNumber.Trim()),
			AddressLine2 = (string.IsNullOrWhiteSpace(settings.AddressLine2) ? bill.AddressLine2 : settings.AddressLine2.Trim()),
			TermsText = ((!settings.PrintTermsDisclaimer) ? null : (string.IsNullOrWhiteSpace(settings.TermsAndDisclaimer) ? bill.TermsText : settings.TermsAndDisclaimer.Trim()))
		};
	}

	private static async Task<string> PrintThermalAsync(RetailBillPrintData bill, DocumentOutputSettings settings, CancellationToken cancellationToken)
	{
		string printerName = settings.RetailMemoPrinter;
		if (string.IsNullOrWhiteSpace(printerName))
		{
			throw new InvalidOperationException("Select an 80 mm thermal printer under Settings → Print & Invoice Layout before printing slips.");
		}
		byte[] buffer = EscPosReceiptBuilder.Build(ThermalReceiptFormatter.Format(bill, settings), bill.InvoiceNo, settings.CashDrawerPulse);
		await RawEscPosPrinter.PrintBytesAsync(printerName, buffer, cancellationToken);
		return printerName;
	}

	private static async Task<string> PrintStandardInvoiceAsync(RetailBillPrintData bill, DocumentOutputSettings settings, bool silent, CancellationToken cancellationToken)
	{
		FlowDocument flowDocument = InvoiceFlowDocumentFactory.Create(bill, settings);
		PrintDialog printDialog = new PrintDialog();
		if (!silent)
		{
			if (printDialog.ShowDialog() != true)
			{
				return "cancelled";
			}
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(settings.RetailInvoicePrinter))
			{
				try
				{
					using LocalPrintServer localPrintServer = new LocalPrintServer();
					printDialog.PrintQueue = localPrintServer.GetPrintQueue(settings.RetailInvoicePrinter);
				}
				catch
				{
				}
			}
			// Silent print to a printer that writes a file (Microsoft Print to PDF, XPS, OneNote, fax) would stop the
			// counter at a Save As box before anyone has seen the bill. Open the invoice instead, and keep its PDF.
			if (WritesToFile(printDialog.PrintQueue))
			{
				return await OpenForViewingAsync(bill, settings, InvoicesFolder, OpenWithShell, cancellationToken);
			}
		}
		printDialog.PrintDocument(((IDocumentPaginatorSource)flowDocument).DocumentPaginator, "Invoice " + bill.InvoiceNo);
		return printDialog.PrintQueue?.Name ?? "default-printer";
	}

	/// <summary>Where invoices opened for viewing are kept (the same folder WhatsApp sharing uses).</summary>
	private static string InvoicesFolder => Path.Combine(AppDataPaths.RootDirectory, "Invoices");

	/// <summary>True for what <see cref="PrintAsync"/> returns when the invoice was opened for viewing instead of printed.</summary>
	public static bool OpenedForViewing(string? printResult) => printResult != null && printResult.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Saves the A4 invoice as a PDF in <paramref name="folder"/>, named by its invoice number, and opens it in the PDF
	/// viewer, where it can be printed or saved elsewhere. Returns the PDF's path.
	/// </summary>
	internal static async Task<string> OpenForViewingAsync(RetailBillPrintData bill, DocumentOutputSettings settings, string folder, Action<string> open, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(folder);
		string name = string.Join("_", bill.InvoiceNo.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		string path = Path.Combine(folder, (name.Length == 0 ? "invoice" : name) + ".pdf");
		await Task.Run(() => RetailBillPdfService.CreateDocument(bill, settings, RetailDocumentType.Invoice).GeneratePdf(path), cancellationToken);
		open(path);
		return path;
	}

	private static void OpenWithShell(string path)
	{
		if (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }) == null)
		{
			throw new InvalidOperationException("Windows could not open the invoice PDF.");
		}
	}

	private static bool WritesToFile(PrintQueue? queue)
	{
		if (queue == null)
		{
			return false;
		}
		try
		{
			return WritesToFile(queue.Name, queue.QueuePort?.Name, queue.QueueDriver?.Name);
		}
		catch (Exception)
		{
			return WritesToFile(queue.Name, null, null);
		}
	}

	/// <summary>
	/// Printers that put the page in a file (or a fax) rather than on paper: Windows asks where to save before printing.
	/// </summary>
	internal static bool WritesToFile(string? queueName, string? portName, string? driverName)
	{
		string port = portName?.Trim().ToUpperInvariant() ?? string.Empty;
		if (port is "PORTPROMPT:" or "FILE:" || port.EndsWith(".PDF", StringComparison.Ordinal) || port.EndsWith(".XPS", StringComparison.Ordinal))
		{
			return true;
		}
		string text = ((queueName ?? string.Empty) + " " + (driverName ?? string.Empty)).ToUpperInvariant();
		return text.Contains("PDF", StringComparison.Ordinal) || text.Contains("XPS", StringComparison.Ordinal)
			|| text.Contains("ONENOTE", StringComparison.Ordinal) || text.Contains("FAX", StringComparison.Ordinal);
	}

	private static (InvoiceTemplateType Template, bool Preview)? AskForTemplate(bool previewOnly)
	{
		InvoiceFormatPickerWindow invoiceFormatPickerWindow = new InvoiceFormatPickerWindow(previewOnly)
		{
			Owner = Application.Current?.MainWindow
		};
		if (invoiceFormatPickerWindow.ShowDialog() != true)
		{
			return null;
		}
		return invoiceFormatPickerWindow.Result;
	}
}
