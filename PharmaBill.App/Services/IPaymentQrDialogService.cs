namespace PharmaBill.App.Services;

public interface IPaymentQrDialogService
{
	PaymentQrDialogResult Show(PaymentQrDialogRequest request);
}
