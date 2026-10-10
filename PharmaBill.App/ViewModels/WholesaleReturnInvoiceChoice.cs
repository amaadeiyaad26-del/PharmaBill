using System;
using PharmaBill.Core.Compliance;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleReturnInvoiceChoice(Guid Id, string InvoiceNo, string CustomerName, DateTime InvoiceAtUtc)
{
	public bool IsAgeLocked => RecordLockPolicy.IsLocked(InvoiceAtUtc);

	public string Label => (IsAgeLocked ? "🔒 " : string.Empty) + InvoiceNo + " — " + CustomerName;

	public string AuditToolTip => IsAgeLocked ? RecordLockPolicy.LockedToolTip : RecordLockPolicy.EditableBadge;
}
