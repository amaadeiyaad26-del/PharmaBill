using PharmaBill.Core;
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

	public DateTime EntryAtUtc { get; set; } = BusinessClock.UtcNow;

	public string? PatientName { get; set; }

	public string? BuyerName { get; set; }

	public string? BuyerAddress { get; set; }

	public string? BuyerPhone { get; set; }

	public string? BuyerLicenceNumber { get; set; }

	public string? PrescriberName { get; set; }

	public string? PrescriberRegistrationNumber { get; set; }

	public string? Notes { get; set; }

	/// <summary>Who made the entry. Null on entries saved before this was recorded.</summary>
	public Guid? RecordedByUserId { get; set; }

	/// <summary>The user's name when the entry was made, kept so the register reads correctly after a rename.</summary>
	public string? RecordedByName { get; set; }

	/// <summary>How the user is known: "AtEntry", or for older entries "FromBill" / "FromAuditLog". Null means not recorded.</summary>
	public string? RecordedBySource { get; set; }

	/// <summary>The PC the entry was made on: this PC, or the billing terminal that sent the bill. Null means not recorded.</summary>
	public string? RecordedOnMachine { get; set; }
}
