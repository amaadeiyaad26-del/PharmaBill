using System.Collections.Generic;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public interface IPrescriptionDialogService
{
	string? CaptureFromWebcam();

	IReadOnlyList<ConfirmedPrescriptionItem>? ReviewMatches(PrescriptionReviewViewModel viewModel);
}
