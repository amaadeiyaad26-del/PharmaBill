using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using PharmaBill.Core;
using PharmaBill.Data.Services;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public enum RetailDocumentType
{
    CashMemo,
    Invoice
}

public sealed class RetailBillPdfService(
    IServiceScopeFactory scopeFactory,
    DocumentOutputSettingsStore settingsStore)
{
    public async Task<string> PrintAsync(
        Guid saleId,
        RetailDocumentType documentType = RetailDocumentType.CashMemo,
        CancellationToken cancellationToken = default)
    {
        var bill = await GetBillAsync(saleId, cancellationToken);
        var settings = settingsStore.Load();
        var printerName = SelectPrinter(settings, documentType);
        if (SelectPaperSize(settings, documentType) == DocumentPaperSize.Thermal80)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new InvalidOperationException("Select an 80 mm thermal printer in Settings before raw printing.");
            }

            await RawEscPosPrinter.PrintAsync(
                printerName,
                CreateThermalReceipt(bill, settings),
                settings.CashDrawerPulse,
                cancellationToken);
            return printerName;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "printed-bills");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{saleId:N}-{documentType}.pdf");
        await ExportAsync(saleId, destination, documentType, cancellationToken);

        var startInfo = new ProcessStartInfo(destination) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            startInfo.Verb = "printto";
            startInfo.Arguments = $"\"{printerName}\"";
        }
        else
        {
            startInfo.Verb = "print";
        }

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Windows could not start the PDF print handler.");
        }

        return destination;
    }

    public async Task<string> PreviewAsync(
        Guid saleId,
        RetailDocumentType documentType = RetailDocumentType.CashMemo,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"PharmaBill-{saleId:N}-{documentType}-{Guid.NewGuid():N}.pdf");
        await ExportAsync(saleId, destination, documentType, cancellationToken);
        var process = Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        if (process is null)
        {
            throw new InvalidOperationException("Windows could not open the PDF preview.");
        }

        return destination;
    }

    public async Task ExportAsync(
        Guid saleId,
        string destinationPath,
        RetailDocumentType documentType = RetailDocumentType.CashMemo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var bill = await GetBillAsync(saleId, cancellationToken);
        var settings = settingsStore.Load();
        await Task.Run(
            () => CreateDocument(bill, settings, documentType).GeneratePdf(destinationPath),
            cancellationToken);
    }

    public async Task ShareWhatsAppAsync(
        Guid saleId,
        RetailDocumentType documentType = RetailDocumentType.CashMemo,
        CancellationToken cancellationToken = default)
    {
        var bill = await GetBillAsync(saleId, cancellationToken);
        var settings = settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.WhatsAppNumber))
        {
            throw new InvalidOperationException("Set a WhatsApp phone number in Settings first.");
        }

        var destination = GetSharedPdfPath(saleId, documentType);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await ExportAsync(saleId, destination, documentType, cancellationToken);
        var number = new string(settings.WhatsAppNumber.Where(char.IsDigit).ToArray());
        if (number.Length == 0)
        {
            throw new InvalidOperationException("Enter a WhatsApp number containing digits in Settings.");
        }
        var message = Uri.EscapeDataString(
            $"Hello, {bill.PharmacyName} has prepared bill {bill.InvoiceNo} for {MoneyFormat.Rupees(bill.TotalAmount)}. The PDF is saved on this device; attach it in WhatsApp.");
        var process = Process.Start(new ProcessStartInfo($"https://wa.me/{number}?text={message}")
        {
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new InvalidOperationException("Windows could not open the WhatsApp link.");
        }

        ShowInExplorer(destination);
    }

    public async Task EmailAsync(
        Guid saleId,
        RetailDocumentType documentType,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        var bill = await GetBillAsync(saleId, cancellationToken);
        var settings = settingsStore.Load();
        var destination = GetSharedPdfPath(saleId, documentType);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await ExportAsync(saleId, destination, documentType, cancellationToken);

        var subject = Uri.EscapeDataString($"PharmaBill {bill.InvoiceNo}");
        var body = Uri.EscapeDataString($"Please find the bill PDF at: {destination}");
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            var process = Process.Start(new ProcessStartInfo($"mailto:{Uri.EscapeDataString(recipient)}?subject={subject}&body={body}")
            {
                UseShellExecute = true
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows could not open the default mail client.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            throw new InvalidOperationException("Set the SMTP sender address in Settings.");
        }

        using var message = new System.Net.Mail.MailMessage(settings.SmtpFromAddress, recipient)
        {
            Subject = $"PharmaBill {bill.InvoiceNo}",
            Body = $"Please find the attached bill for {bill.InvoiceNo}."
        };
        message.Attachments.Add(new System.Net.Mail.Attachment(destination));
        using var client = new System.Net.Mail.SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.SmtpEnableSsl
        };
        var password = settingsStore.ReadSmtpPassword();
        if (!string.IsNullOrWhiteSpace(settings.SmtpUserName))
        {
            client.Credentials = new System.Net.NetworkCredential(settings.SmtpUserName, password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }

    public static IDocument CreateDocument(
        RetailBillPrintData bill,
        DocumentOutputSettings settings,
        RetailDocumentType documentType)
    {
        var paperSize = SelectPaperSize(settings, documentType);
        return Document.Create(container =>
            container.Page(page =>
            {
                if (paperSize == DocumentPaperSize.Thermal80)
                {
                    var estimatedLines = 30 + bill.Items.Count * 4;
                    page.Size(226.77f, Math.Max(420f, estimatedLines * 9f), Unit.Point);
                    page.Margin(14);
                    page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(7));
                }
                else
                {
                    page.Size(paperSize == DocumentPaperSize.A5 ? PageSizes.A5 : PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(10));
                }

                page.Header().Column(column =>
                {
                    var logoPath = settings.LogoPath;
                    if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                    {
                        column.Item().Height(42).Image(logoPath).FitArea();
                    }

                    column.Item().Text(bill.PharmacyName).FontSize(paperSize == DocumentPaperSize.Thermal80 ? 12 : 20).Bold();
                    AddIfPresent(column, bill.PharmacyAddress);
                    AddIfPresent(column, bill.PharmacyPhone is null ? null : $"Phone: {bill.PharmacyPhone}");
                    AddIfPresent(column, bill.Gstin is null ? null : $"GSTIN: {bill.Gstin}");
                    if (bill.LicenceNumbers.Count > 0)
                    {
                        column.Item().Text($"Drug licence no.: {string.Join(", ", bill.LicenceNumbers)}");
                    }

                    column.Item().PaddingTop(8)
                        .Text($"{(documentType == RetailDocumentType.CashMemo ? "Cash memo" : "Tax invoice")}: {bill.InvoiceNo}")
                        .Bold();
                    column.Item().Text($"Date: {bill.SaleAtUtc.ToLocalTime():dd-MMM-yyyy HH:mm}");
                    column.Item().Text($"Patient: {bill.PatientName}  Phone: {bill.PatientPhone}");
                    AddIfPresent(column, bill.PatientAddress);
                });
                page.Content().PaddingVertical(10).Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(0.8f);
                            columns.RelativeColumn(0.8f);
                            columns.RelativeColumn(0.8f);
                            columns.RelativeColumn(0.8f);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Medicine / batch / expiry");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Rate");
                            header.Cell().Element(HeaderCell).AlignRight().Text("GST %");
                            header.Cell().Element(HeaderCell).AlignRight().Text("GST Amt");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                        });
                        foreach (var item in bill.Items)
                        {
                            table.Cell().Element(BodyCell)
                                .Text($"{item.DrugName}\nBatch {item.BatchNo}  Exp {item.ExpiryDate?.ToString("MM/yy") ?? "—"}");
                            table.Cell().Element(BodyCell).AlignRight().Text(item.Quantity.ToString("0.##"));
                            table.Cell().Element(BodyCell).AlignRight().Text(item.UnitPrice.ToString("N2"));
                            table.Cell().Element(BodyCell).AlignRight().Text($"{item.TaxRate:N2}%");
                            table.Cell().Element(BodyCell).AlignRight().Text(item.TaxAmount.ToString("N2"));
                            table.Cell().Element(BodyCell).AlignRight().Text(item.LineTotal.ToString("N2"));
                        }
                    });
                    content.Item().PaddingTop(10).Column(column =>
                    {
                        column.Item().AlignRight().Text($"Taxable value: {MoneyFormat.Rupees(bill.Subtotal)}");
                        column.Item().AlignRight().Text($"GST included: {MoneyFormat.Rupees(bill.TaxAmount)}");
                        column.Item().AlignRight().Text($"Discount: {MoneyFormat.Rupees(bill.DiscountAmount)}");
                        column.Item().AlignRight().Text($"Total: {MoneyFormat.Rupees(bill.TotalAmount)}").FontSize(14).Bold();
                        column.Item().AlignRight().Text($"Paid: {MoneyFormat.Rupees(bill.PaidAmount)}");
                        column.Item().PaddingTop(4).Text($"Amount in words: {IndianNumberWords.Convert(bill.TotalAmount)}").Bold();
                        AddIfPresent(column, bill.PharmacistName);
                        AddIfPresent(column, bill.PharmacistQualification);
                        AddIfPresent(column, bill.PharmacistRegistrationNumber is null
                            ? null
                            : $"Registration no.: {bill.PharmacistRegistrationNumber}");
                        column.Item().PaddingTop(18).Text("Pharmacist signature: __________________________");
                        AddIfPresent(column, settings.FooterText);
                    });
                });
            }));
    }

    public static string CreateThermalReceipt(RetailBillPrintData bill, DocumentOutputSettings settings)
    {
        var builder = new StringBuilder();
        builder.AppendLine(bill.PharmacyName);
        AppendLineIfPresent(builder, bill.PharmacyAddress);
        AppendLineIfPresent(builder, bill.PharmacyPhone);
        AppendLineIfPresent(builder, bill.Gstin is null ? null : $"GSTIN {bill.Gstin}");
        foreach (var licence in bill.LicenceNumbers)
        {
            builder.AppendLine($"DL {licence}");
        }

        builder.AppendLine($"CASH MEMO {bill.InvoiceNo}");
        builder.AppendLine(bill.SaleAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture));
        builder.AppendLine($"Patient: {bill.PatientName}");
        builder.AppendLine(new string('-', 32));
        foreach (var item in bill.Items)
        {
            builder.AppendLine(Ascii(item.DrugName));
            builder.AppendLine($"B:{Ascii(item.BatchNo)} E:{item.ExpiryDate?.ToString("MM/yy") ?? "--"}");
            builder.AppendLine($"{item.Quantity:0.##} x {item.UnitPrice:N2} GST {item.TaxRate:N2}% / {item.TaxAmount:N2} = {item.LineTotal:N2}");
        }

        builder.AppendLine(new string('-', 32));
        builder.AppendLine($"Taxable: {bill.Subtotal:N2}");
        builder.AppendLine($"GST: {bill.TaxAmount:N2}");
        builder.AppendLine($"TOTAL: {bill.TotalAmount:N2}");
        builder.AppendLine(IndianNumberWords.Convert(bill.TotalAmount));
        AppendLineIfPresent(builder, bill.PharmacistName);
        AppendLineIfPresent(builder, bill.PharmacistRegistrationNumber);
        builder.AppendLine("Signature: ______________");
        AppendLineIfPresent(builder, settings.FooterText);
        builder.AppendLine();
        return builder.ToString();
    }

    public static void ShowInExplorer(string path)
    {
        var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new InvalidOperationException("Windows Explorer could not show the saved PDF.");
        }
    }

    public static string GetSharedPdfPath(Guid saleId, RetailDocumentType documentType) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "shared-bills",
            $"{saleId:N}-{documentType}.pdf");

    private async Task<RetailBillPrintData> GetBillAsync(Guid saleId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
        return await billing.GetPrintableBillAsync(saleId, cancellationToken);
    }

    private static DocumentPaperSize SelectPaperSize(
        DocumentOutputSettings settings,
        RetailDocumentType documentType) =>
        documentType == RetailDocumentType.CashMemo
            ? settings.RetailMemoPaperSize
            : settings.RetailInvoicePaperSize;

    private static string? SelectPrinter(DocumentOutputSettings settings, RetailDocumentType documentType) =>
        documentType == RetailDocumentType.CashMemo
            ? settings.RetailMemoPrinter
            : settings.RetailInvoicePrinter;

    private static void AddIfPresent(ColumnDescriptor column, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            column.Item().Text(text);
        }
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten2).Padding(5).DefaultTextStyle(style => style.Bold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);

    private static void AppendLineIfPresent(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine(Ascii(value));
        }
    }

    private static string Ascii(string value) =>
        new(value.Select(character => character <= 127 ? character : '?').ToArray());
}
