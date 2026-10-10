using System;
using PharmaBill.Core.Compliance;

namespace PharmaBill.Data.Services;

public sealed record RecentRetailBill(Guid SaleId, string InvoiceNo, DateTime SaleAtUtc, string PatientName, string? PatientPhone, decimal TotalAmount, decimal PaidAmount, bool IsLocked = true, Guid? BilledByUserId = null)
{
	public bool IsAgeLocked => RecordLockPolicy.IsLocked(SaleAtUtc);

	public string AuditStatus => IsAgeLocked ? "🔒 Locked" : RecordLockPolicy.EditableBadge;

	public string AuditToolTip => IsAgeLocked ? RecordLockPolicy.LockedToolTip : RecordLockPolicy.EditableBadge;
}
