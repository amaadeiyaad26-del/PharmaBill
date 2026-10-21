using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

// Who made each Schedule H / H1 / X register entry. Called once per transaction, just before saving: every
// entry that transaction adds is stamped with the acting user and a snapshot of their name, so the register
// still reads correctly after the user is renamed or removed.
public static class RegisterRecordedBy
{
	// RecordedBySource values. Null means the entry was saved before this was recorded ("Not recorded").
	public const string AtEntry = "AtEntry";

	public const string FromBill = "FromBill";

	public const string FromAuditLog = "FromAuditLog";

	public static async Task StampAsync(PharmaBillDbContext context, Guid userId, CancellationToken cancellationToken = default)
	{
		ScheduleRegisterEntry[] added = context.ChangeTracker.Entries<ScheduleRegisterEntry>()
			.Where(entry => entry.State == EntityState.Added && entry.Entity.RecordedByUserId == null)
			.Select(entry => entry.Entity)
			.ToArray();
		if (added.Length == 0)
		{
			return;
		}
		string? name = await context.AppUsers.IgnoreQueryFilters().Where(user => user.Id == userId)
			.Select(user => user.DisplayName != "" ? user.DisplayName : user.UserName)
			.FirstOrDefaultAsync(cancellationToken);
		foreach (ScheduleRegisterEntry entry in added)
		{
			entry.RecordedByUserId = userId;
			entry.RecordedByName = name;
			entry.RecordedBySource = AtEntry;
		}
	}

	public static string Describe(ScheduleRegisterEntry entry) => entry.RecordedBySource switch
	{
		AtEntry => entry.RecordedByName ?? "Unknown user",
		FromBill => (entry.RecordedByName ?? "Unknown user") + " (from bill)",
		FromAuditLog => (entry.RecordedByName ?? "Unknown user") + " (from audit log)",
		_ => "Not recorded"
	};
}
