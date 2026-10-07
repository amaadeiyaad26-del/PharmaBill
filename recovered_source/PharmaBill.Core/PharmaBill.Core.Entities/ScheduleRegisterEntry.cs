using System;

namespace PharmaBill.Core.Entities;

public sealed class ScheduleRegisterEntry : EntityBase
{
	public string RegisterType { get; set; } = string.Empty;

	public Guid? SaleId { get; set; }

	public Guid? PatientId { get; set; }

	public Guid? DrugId { get; set; }

	public Guid? SupplierId { get; set; }

	public Guid? CustomerId { get; set; }

	public Guid? BatchId { get; set; }

	public decimal? Quantity { get; set; }

	public string? BatchNo { get; set; }

	public DateTime EntryAtUtc { get; set; } = DateTime.UtcNow;

	public string? PatientName { get; set; }

	public string? BuyerName { get; set; }

	public string? BuyerAddress { get; set; }

	public string? BuyerPhone { get; set; }

	public string? BuyerLicenceNumber { get; set; }

	public string? PrescriberName { get; set; }

	public string? PrescriberRegistrationNumber { get; set; }

	public string? Notes { get; set; }
}
