using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;

namespace PharmaBill.Data.Services;

/// <summary>
/// Stock entry chose a different schedule from the one saved for an existing medicine. Changing a medicine's statutory
/// schedule changes what billing demands for it (prescription, registers), so it needs an Admin's schedule override.
/// </summary>
public sealed class ScheduleChangeRequiredException(Guid drugId, string medicineName, string savedSchedule, string requestedSchedule)
	: InvalidOperationException($"{medicineName} is saved with schedule {savedSchedule}. Changing it to {requestedSchedule} needs a schedule override with an Admin PIN.")
{
	public Guid DrugId { get; } = drugId;

	public string MedicineName { get; } = medicineName;

	public string SavedSchedule { get; } = savedSchedule;

	public string RequestedSchedule { get; } = requestedSchedule;

	/// <summary>True when the new schedule asks less of billing than the saved one (for example H1 to OTC).</summary>
	public bool LowersControls => ScheduleChangeAuthorization.LowersControls(SavedSchedule, RequestedSchedule);
}

/// <summary>An Admin's sign-off, PIN verified by the app, to change a medicine's schedule during this stock entry.</summary>
public sealed record ScheduleChangeAuthorization(Guid AuthorizedByUserId, string Reason)
{
	/// <summary>Who may authorize a schedule override: the same Admin roles as every other admin override.</summary>
	public static bool CanAuthorize(UserRole role) => PermissionMatrix.Allows(role, AppPermission.EditPrintedBills);

	/// <summary>Schedules from least to most controlled; X and NDPS are equally strict.</summary>
	public static int Rank(string schedule) => schedule.Trim().ToUpperInvariant() switch
	{
		"OTC" => 0,
		"G" => 1,
		"H" => 2,
		"H1" => 3,
		_ => 4,
	};

	public static bool LowersControls(string from, string to) => Rank(to) < Rank(from);
}
