using System.Collections.Generic;
using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public sealed class PrescriptionDialogService : IPrescriptionDialogService
{
	public string? CaptureFromWebcam(bool prescriptionOcrMode = false)
	{
		WebcamCaptureWindow webcamCaptureWindow = new WebcamCaptureWindow(prescriptionOcrMode)
		{
			Owner = Application.Current?.MainWindow
		};
		if (webcamCaptureWindow.ShowDialog() != true)
		{
			return null;
		}
		return webcamCaptureWindow.CapturedPath;
	}

	public bool HasHardwareScanner()
	{
		return WiaScannerCapture.HasScanner();
	}

	public string? CaptureFromHardwareScanner()
	{
		return WiaScannerCapture.ScanToJpeg();
	}

	public IReadOnlyList<ConfirmedPrescriptionItem>? ReviewMatches(PrescriptionReviewViewModel viewModel)
	{
		if (new PrescriptionReviewWindow(viewModel)
		{
			Owner = Application.Current?.MainWindow
		}.ShowDialog() != true)
		{
			return null;
		}
		return viewModel.GetConfirmedItems();
	}
}
