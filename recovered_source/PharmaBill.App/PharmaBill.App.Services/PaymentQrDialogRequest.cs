using System.Windows.Media;

namespace PharmaBill.App.Services;

public sealed record PaymentQrDialogRequest(string UpiId, string PharmacyName, decimal Amount, string InvoiceNo, ImageSource? QrImage, bool ShowOnCustomerDisplay);
