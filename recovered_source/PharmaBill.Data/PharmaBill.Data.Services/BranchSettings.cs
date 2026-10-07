using System;

namespace PharmaBill.Data.Services;

public sealed class BranchSettings
{
	public Guid? CurrentBranchId { get; set; }

	public string CurrentBranchCode { get; set; } = "BR01-MAIN";

	public string CurrentBranchName { get; set; } = "Main branch";

	public bool IsHeadOffice { get; set; } = true;
}
