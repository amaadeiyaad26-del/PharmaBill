using System.Collections.Generic;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public interface IPrescriptionDialogService
{
	string? CaptureFromWebcam(bool prescriptionOcrMode = false);

	bool HasHardwareScanner();

	string? CaptureFromHardwareScanner();

	IReadOnlyList<ConfirmedPrescriptionItem>? ReviewMatches(PrescriptionReviewViewModel viewModel);
}
