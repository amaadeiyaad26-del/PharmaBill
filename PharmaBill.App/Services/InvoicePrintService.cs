using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Services;

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
			InvoiceTemplateType.StandardA4 => PrintStandardInvoice(bill, settings, silent), 
			_ => await PrintThermalAsync(bill, settings, cancellationToken), 
		};
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

	internal static RetailBillPrintData EnrichWithPrintBranding(RetailBillPrintData bill, DocumentOutputSettings settings)
	{
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

	private static string PrintStandardInvoice(RetailBillPrintData bill, DocumentOutputSettings settings, bool silent)
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
		else if (!string.IsNullOrWhiteSpace(settings.RetailInvoicePrinter))
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
		printDialog.PrintDocument(((IDocumentPaginatorSource)flowDocument).DocumentPaginator, "Invoice " + bill.InvoiceNo);
		return printDialog.PrintQueue?.Name ?? "default-printer";
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
