using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core;
using PharmaBill.Data.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public sealed class RetailBillPdfService(IServiceScopeFactory scopeFactory, DocumentOutputSettingsStore settingsStore)
{
	public async Task<string> PrintAsync(Guid saleId, RetailDocumentType documentType = RetailDocumentType.CashMemo, CancellationToken cancellationToken = default(CancellationToken))
	{
		RetailBillPrintData bill = await GetBillAsync(saleId, cancellationToken);
		DocumentOutputSettings documentOutputSettings = settingsStore.Load();
		string printerName = SelectPrinter(documentOutputSettings, documentType);
		if (SelectPaperSize(documentOutputSettings, documentType) == DocumentPaperSize.Thermal80)
		{
			if (string.IsNullOrWhiteSpace(printerName))
			{
				throw new InvalidOperationException("Select an 80 mm thermal printer in Settings before raw printing.");
			}
			await RawEscPosPrinter.PrintAsync(printerName, CreateThermalReceipt(bill, documentOutputSettings), documentOutputSettings.CashDrawerPulse, cancellationToken);
			return printerName;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "printed-bills");
		Directory.CreateDirectory(text);
		string destination = Path.Combine(text, $"{saleId:N}-{documentType}.pdf");
		await ExportAsync(saleId, destination, documentType, cancellationToken);
		ProcessStartInfo processStartInfo = new ProcessStartInfo(destination)
		{
			UseShellExecute = true
		};
		if (!string.IsNullOrWhiteSpace(printerName))
		{
			processStartInfo.Verb = "printto";
			processStartInfo.Arguments = "\"" + printerName + "\"";
		}
		else
		{
			processStartInfo.Verb = "print";
		}
		if (Process.Start(processStartInfo) == null)
		{
			throw new InvalidOperationException("Windows could not start the PDF print handler.");
		}
		return destination;
	}

	public async Task<string> PreviewAsync(Guid saleId, RetailDocumentType documentType = RetailDocumentType.CashMemo, CancellationToken cancellationToken = default(CancellationToken))
	{
		string destination = Path.Combine(Path.GetTempPath(), $"PharmaBill-{saleId:N}-{documentType}-{Guid.NewGuid():N}.pdf");
		await ExportAsync(saleId, destination, documentType, cancellationToken);
		if (Process.Start(new ProcessStartInfo(destination)
		{
			UseShellExecute = true
		}) == null)
		{
			throw new InvalidOperationException("Windows could not open the PDF preview.");
		}
		return destination;
	}

	public async Task ExportAsync(Guid saleId, string destinationPath, RetailDocumentType documentType = RetailDocumentType.CashMemo, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, "destinationPath");
		RetailBillPrintData bill = await GetBillAsync(saleId, cancellationToken);
		DocumentOutputSettings settings = settingsStore.Load();
		bill = InvoicePrintService.EnrichWithPrintBranding(bill, settings);
		await Task.Run(() =>
		{
			CreateDocument(bill, settings, documentType).GeneratePdf(destinationPath);
		}, cancellationToken);
	}

	public async Task ShareWhatsAppAsync(Guid saleId, RetailDocumentType documentType = RetailDocumentType.CashMemo, CancellationToken cancellationToken = default(CancellationToken))
	{
		DocumentOutputSettings settings = settingsStore.Load();
		if (string.IsNullOrWhiteSpace(settings.WhatsAppNumber))
		{
			throw new InvalidOperationException("Set a WhatsApp phone number in Settings first.");
		}

		string destination = GetSharedPdfPath(saleId, documentType);
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		await ExportAsync(saleId, destination, documentType, cancellationToken);
		OpenWhatsAppChat(settings.WhatsAppNumber, await BuildDefaultWhatsAppMessageAsync(saleId, cancellationToken));
		ShowInExplorer(destination);
	}

	/// <summary>
	/// Exports the bill PDF under Invoices, opens wa.me with the invoice text,
	/// and places the PDF itself on the clipboard as a FileDropList so Ctrl+V attaches the file (not a path string).
	/// </summary>
	public async Task<string> ShareWhatsAppToCustomerAsync(Guid saleId, string customerPhone, RetailDocumentType documentType = RetailDocumentType.CashMemo, bool copyPdfFileToClipboard = true, CancellationToken cancellationToken = default(CancellationToken))
	{
		RetailBillPrintData bill = await GetBillAsync(saleId, cancellationToken);
		string digits = NormalizeWhatsAppPhone(customerPhone);
		if (digits.Length == 0)
		{
			throw new InvalidOperationException("Please enter customer phone number first.");
		}

		string invoicesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "Invoices");
		Directory.CreateDirectory(invoicesDir);
		string billNo = SanitizeFileName(bill.InvoiceNo);
		string destination = Path.Combine(invoicesDir, billNo + ".pdf");
		await ExportAsync(saleId, destination, documentType, cancellationToken);

		string patient = string.IsNullOrWhiteSpace(bill.PatientName) ? "Customer" : bill.PatientName.Trim();
		string pharmacy = string.IsNullOrWhiteSpace(bill.PharmacyName) ? "our pharmacy" : bill.PharmacyName.Trim();
		string message = "Hello " + patient + ", here is your digital invoice for " + pharmacy + ":\n"
			+ "Bill No: " + bill.InvoiceNo + "\n"
			+ "Amount: " + MoneyFormat.Rupees(bill.TotalAmount) + "\n"
			+ "Thank you for choosing us!";
		OpenWhatsAppChat(digits, message);

		if (copyPdfFileToClipboard)
		{
			try
			{
				CopyPdfFileDropToClipboard(destination);
			}
			catch
			{
				// Clipboard can be locked by another app; WhatsApp still opened with the text message.
			}
		}

		return destination;
	}

	/// <summary>Puts the PDF file on the clipboard as CF_HDROP / FileDropList so paste attaches the document.</summary>
	public static void CopyPdfFileDropToClipboard(string pdfPath)
	{
		if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
		{
			throw new FileNotFoundException("The invoice PDF was not found to copy.", pdfPath);
		}

		void Set()
		{
			StringCollection fileList = new StringCollection();
			fileList.Add(Path.GetFullPath(pdfPath));
			Clipboard.SetFileDropList(fileList);
		}

		System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			Set();
			return;
		}

		dispatcher.Invoke(Set);
	}

	private static string SanitizeFileName(string? name)
	{
		string raw = string.IsNullOrWhiteSpace(name) ? "bill" : name.Trim();
		string safe = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		return string.IsNullOrWhiteSpace(safe) ? "bill" : safe;
	}

	private async Task<string> BuildDefaultWhatsAppMessageAsync(Guid saleId, CancellationToken cancellationToken)
	{
		RetailBillPrintData bill = await GetBillAsync(saleId, cancellationToken);
		return "Hello, " + bill.PharmacyName + " has prepared bill " + bill.InvoiceNo + " for " + MoneyFormat.Rupees(bill.TotalAmount) + ". The PDF is saved on this device; attach it in WhatsApp.";
	}

	private static void OpenWhatsAppChat(string phoneOrDigits, string message)
	{
		string digits = NormalizeWhatsAppPhone(phoneOrDigits);
		if (digits.Length == 0)
		{
			digits = new string(phoneOrDigits.Where(char.IsDigit).ToArray());
		}

		if (digits.Length == 0)
		{
			throw new InvalidOperationException("Enter a WhatsApp number containing digits.");
		}

		string url = "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(message);
		if (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }) == null)
		{
			throw new InvalidOperationException("Windows could not open the WhatsApp link.");
		}
	}

	public static string NormalizeWhatsAppPhone(string? phone)
	{
		string digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
		if (digits.Length == 10)
		{
			digits = "91" + digits;
		}

		return digits.Length >= 10 ? digits : string.Empty;
	}

	public async Task EmailAsync(Guid saleId, RetailDocumentType documentType, string recipient, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(recipient, "recipient");
		RetailBillPrintData bill = await GetBillAsync(saleId, cancellationToken);
		DocumentOutputSettings settings = settingsStore.Load();
		string destination = GetSharedPdfPath(saleId, documentType);
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		await ExportAsync(saleId, destination, documentType, cancellationToken);
		string value = Uri.EscapeDataString("PharmaBill " + bill.InvoiceNo);
		string value2 = Uri.EscapeDataString("Please find the bill PDF at: " + destination);
		if (string.IsNullOrWhiteSpace(settings.SmtpHost))
		{
			if (Process.Start(new ProcessStartInfo($"mailto:{Uri.EscapeDataString(recipient)}?subject={value}&body={value2}")
			{
				UseShellExecute = true
			}) == null)
			{
				throw new InvalidOperationException("Windows could not open the default mail client.");
			}
			return;
		}
		if (string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
		{
			throw new InvalidOperationException("Set the SMTP sender address in Settings.");
		}
		using MailMessage message = new MailMessage(settings.SmtpFromAddress, recipient)
		{
			Subject = "PharmaBill " + bill.InvoiceNo,
			Body = "Please find the attached bill for " + bill.InvoiceNo + "."
		};
		message.Attachments.Add(new Attachment(destination));
		using SmtpClient client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
		{
			EnableSsl = settings.SmtpEnableSsl
		};
		string password = settingsStore.ReadSmtpPassword();
		if (!string.IsNullOrWhiteSpace(settings.SmtpUserName))
		{
			client.Credentials = new NetworkCredential(settings.SmtpUserName, password);
		}
		await client.SendMailAsync(message, cancellationToken);
	}

	public static IDocument CreateDocument(RetailBillPrintData bill, DocumentOutputSettings settings, RetailDocumentType documentType)
	{
		DocumentPaperSize paperSize = SelectPaperSize(settings, documentType);
		return Document.Create((IDocumentContainer container) =>
		{
			container.Page((PageDescriptor page) =>
			{
				if (paperSize == DocumentPaperSize.Thermal80)
				{
					int num = 30 + bill.Items.Count * 4;
					page.Size(226.77f, Math.Max(420f, (float)num * 9f));
					page.Margin(14f);
					page.DefaultTextStyle((TextStyle style) => style.FontFamily(PdfFonts.Family).FontSize(7f));
				}
				else
				{
					page.Size((paperSize == DocumentPaperSize.A5) ? PageSizes.A5 : PageSizes.A4);
					page.Margin(32f);
					page.DefaultTextStyle((TextStyle style) => style.FontFamily(PdfFonts.Family).FontSize(10f));
					PdfBrandWatermark.Apply(page);
				}
				page.Header().Column((ColumnDescriptor column) =>
				{
					string logoPath = settings.LogoPath;
					if (settings.PrintShopLogo && !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
					{
						column.Item().Height(42f).Image(logoPath)
							.FitArea();
					}
					column.Item().Text(bill.PharmacyName).FontSize((paperSize == DocumentPaperSize.Thermal80) ? 12 : 20)
						.Bold();
					AddIfPresent(column, bill.PharmacyAddress);
					AddIfPresent(column, bill.AddressLine2);
					AddIfPresent(column, (bill.PharmacyPhone == null) ? null : ("Phone: " + bill.PharmacyPhone));
					AddIfPresent(column, (bill.Gstin == null) ? null : ("GSTIN: " + bill.Gstin));
					AddIfPresent(column, string.IsNullOrWhiteSpace(bill.FssaiNumber) ? null : ("FSSAI: " + bill.FssaiNumber));
					if (bill.LicenceNumbers.Count > 0)
					{
						column.Item().Text("Drug licence no.: " + string.Join(", ", bill.LicenceNumbers));
					}
					column.Item().PaddingTop(8f).Text(((documentType == RetailDocumentType.CashMemo) ? "Cash memo" : "Tax invoice") + ": " + bill.InvoiceNo)
						.Bold();
					column.Item().Text($"Date: {bill.SaleAtUtc.ToLocalTime():dd-MMM-yyyy HH:mm}");
					string text = ((settings.PrintCustomerPhone && !string.IsNullOrWhiteSpace(bill.PatientPhone)) ? ("Patient: " + bill.PatientName + "  Phone: " + bill.PatientPhone) : ("Patient: " + bill.PatientName));
					column.Item().Text(text);
					AddIfPresent(column, bill.PatientAddress);
					AddIfPresent(column, string.IsNullOrWhiteSpace(bill.MrdNumber) ? null : ("MRD / IPD No: " + bill.MrdNumber));
					if (settings.PrintDoctorName && !string.IsNullOrWhiteSpace(bill.PrescriberName))
					{
						column.Item().Text("Doctor: " + bill.PrescriberName + (string.IsNullOrWhiteSpace(bill.PrescriberRegistrationNumber) ? string.Empty : ("  Reg. No: " + bill.PrescriberRegistrationNumber)));
					}
				});
				page.Content().PaddingVertical(10f).Column((ColumnDescriptor content) =>
				{
					content.Item().Table((TableDescriptor table) =>
					{
						table.ColumnsDefinition((TableColumnsDefinitionDescriptor columns) =>
						{
							columns.RelativeColumn(2.6f);
							columns.RelativeColumn(0.9f);
							columns.RelativeColumn(0.8f);
							columns.RelativeColumn(0.8f);
							columns.RelativeColumn(0.7f);
							columns.RelativeColumn(0.7f);
							columns.RelativeColumn(0.9f);
						});
						table.Header((TableCellDescriptor header) =>
						{
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 270).Text("Medicine / batch / expiry");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 271).Text("HSN");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 272).AlignRight().Text("Qty");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 273).AlignRight().Text("Rate");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 274).AlignRight().Text("CGST");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 275).AlignRight().Text("SGST");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 276).AlignRight().Text("Amount");
						});
						foreach (RetailBillPrintLine item in bill.Items)
						{
							decimal num2 = decimal.Round(item.TaxAmount / 2m, 2, MidpointRounding.AwayFromZero);
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 281).Text($"{item.DrugName}\nBatch {item.BatchNo}  Exp {item.ExpiryDate?.ToString("MM/yy") ?? "—"}");
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 283).Text(item.HsnCode ?? "—");
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 284).AlignRight().Text(item.Quantity.ToString("0.##"));
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 285).AlignRight().Text(item.UnitPrice.ToString("N2"));
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 286).AlignRight().Text(num2.ToString("N2"));
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 287).AlignRight().Text((item.TaxAmount - num2).ToString("N2"));
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "CreateDocument", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\RetailBillPdfService.cs", 288).AlignRight().Text(item.LineTotal.ToString("N2"));
						}
					});
					content.Item().PaddingTop(10f).Column((ColumnDescriptor column) =>
					{
						column.Item().AlignRight().Text("Taxable value: " + MoneyFormat.Rupees(bill.Subtotal));
						column.Item().AlignRight().Text("CGST: " + MoneyFormat.Rupees(bill.CgstAmount) + "    SGST: " + MoneyFormat.Rupees(bill.SgstAmount));
						column.Item().AlignRight().Text("GST included: " + MoneyFormat.Rupees(bill.TaxAmount));
						column.Item().AlignRight().Text("Discount: " + MoneyFormat.Rupees(bill.DiscountAmount));
						column.Item().AlignRight().Text("Total: " + MoneyFormat.Rupees(bill.TotalAmount))
							.FontSize(14f)
							.Bold();
						column.Item().AlignRight().Text("Paid: " + MoneyFormat.Rupees(bill.PaidAmount));
						column.Item().PaddingTop(4f).Text("Amount in words: " + IndianNumberWords.Convert(bill.TotalAmount))
							.Bold();
						AddIfPresent(column, bill.PharmacistName);
						AddIfPresent(column, bill.PharmacistQualification);
						AddIfPresent(column, (bill.PharmacistRegistrationNumber == null) ? null : ("Registration no.: " + bill.PharmacistRegistrationNumber));
						column.Item().PaddingTop(18f).Text("Authorized signatory: __________________________");
						if (settings.PrintTermsDisclaimer && !string.IsNullOrWhiteSpace(bill.TermsText))
						{
							column.Item().PaddingTop(8f).Text("Terms: " + bill.TermsText)
								.FontSize(8f);
						}
						else if (settings.PrintTermsDisclaimer && !string.IsNullOrWhiteSpace(settings.TermsAndDisclaimer))
						{
							column.Item().PaddingTop(8f).Text("Terms: " + settings.TermsAndDisclaimer)
								.FontSize(8f);
						}
						AddIfPresent(column, settings.FooterText);
					});
				});
			});
		});
	}

	public static string CreateThermalReceipt(RetailBillPrintData bill, DocumentOutputSettings settings)
	{
		return ThermalReceiptFormatter.Format(InvoicePrintService.EnrichWithPrintBranding(bill, settings), settings);
	}

	public static void ShowInExplorer(string path)
	{
		if (Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
		{
			UseShellExecute = true
		}) == null)
		{
			throw new InvalidOperationException("Windows Explorer could not show the saved PDF.");
		}
	}

	public static string GetSharedPdfPath(Guid saleId, RetailDocumentType documentType)
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "shared-bills", $"{saleId:N}-{documentType}.pdf");
	}

	public static string GetInvoicePdfPath(Guid saleId, string? invoiceNo)
	{
		string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "Invoices");
		Directory.CreateDirectory(folder);
		string safe = string.IsNullOrWhiteSpace(invoiceNo) ? saleId.ToString("N") : string.Join("_", invoiceNo.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		if (string.IsNullOrWhiteSpace(safe))
		{
			safe = saleId.ToString("N");
		}

		return Path.Combine(folder, safe + ".pdf");
	}

	private async Task<RetailBillPrintData> GetBillAsync(Guid saleId, CancellationToken cancellationToken)
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		return await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetPrintableBillAsync(saleId, cancellationToken);
	}

	private static DocumentPaperSize SelectPaperSize(DocumentOutputSettings settings, RetailDocumentType documentType)
	{
		if (documentType != RetailDocumentType.CashMemo)
		{
			return settings.RetailInvoicePaperSize;
		}
		return settings.RetailMemoPaperSize;
	}

	private static string? SelectPrinter(DocumentOutputSettings settings, RetailDocumentType documentType)
	{
		if (documentType != RetailDocumentType.CashMemo)
		{
			return settings.RetailInvoicePrinter;
		}
		return settings.RetailMemoPrinter;
	}

	private static void AddIfPresent(ColumnDescriptor column, string? text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			column.Item().Text(text);
		}
	}

	private static IContainer HeaderCell(IContainer container)
	{
		return container.Background(Colors.Grey.Lighten2).Padding(5f).DefaultTextStyle((TextStyle style) => style.Bold());
	}

	private static IContainer BodyCell(IContainer container)
	{
		return container.BorderBottom(1f).BorderColor(Colors.Grey.Lighten2).Padding(5f);
	}
}
