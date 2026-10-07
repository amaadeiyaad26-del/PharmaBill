namespace PharmaBill.Core.Entities;

public sealed class Branch : EntityBase
{
	public string Code { get; set; } = "BR01-MAIN";

	public string BranchName { get; set; } = string.Empty;

	public string? Address { get; set; }

	public string? ContactPhone { get; set; }

	public string? Gstin { get; set; }

	public string? DrugLicenseNo { get; set; }

	public bool IsHeadOffice { get; set; }

	public string InvoicePrefix { get; set; } = "BR01-INV";

	public bool IsActive { get; set; } = true;
}
