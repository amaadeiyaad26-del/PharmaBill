
namespace PharmaBill.Data.Persistence;

/// <summary>
/// A schema migration failed. Startup must stop: running on a partly upgraded pharmacy database is never safe,
/// and nothing may "repair" the schema behind the migration history's back. When a pre-migration copy was made it
/// has been put back (<see cref="Restored"/>), so the database is exactly as it was before the upgrade began.
/// </summary>
public sealed class DatabaseMigrationException(string migrationId, string databasePath, Exception innerException, string? safeCopyPath = null, bool restored = false)
	: InvalidOperationException(
		$"Database upgrade failed at migration '{migrationId}': {Innermost(innerException).Message} "
		+ "Pharma Remit has stopped so it does not run on a partly upgraded database. "
		+ (restored
			? $"The database was put back exactly as it was before the upgrade, from {safeCopyPath}. "
			: safeCopyPath != null ? $"A copy made before the upgrade is at {safeCopyPath}. " : "")
		+ $"The database file was not deleted ({databasePath}). Contact support.",
		innerException)
{
	public string MigrationId { get; } = migrationId;

	public string DatabasePath { get; } = databasePath;

	/// <summary>The copy of the database made just before the upgrade, if one was made.</summary>
	public string? SafeCopyPath { get; } = safeCopyPath;

	/// <summary>True when the pre-migration copy was put back in place of the database.</summary>
	public bool Restored { get; } = restored;

	/// <summary>What the pharmacist is told: their data is safe, where it is, and to contact support.</summary>
	public string PharmacistMessage =>
		"Pharma Remit could not finish updating your database, so it has stopped before opening." + Environment.NewLine + Environment.NewLine
		+ (Restored
			? "Your data is safe. Your database has been put back exactly as it was before the update." + Environment.NewLine
			  + "A copy of it is also kept in this file:" + Environment.NewLine + SafeCopyPath
			: SafeCopyPath != null
				? "Your data is safe. A complete copy made before the update is in this file:" + Environment.NewLine + SafeCopyPath
				  + Environment.NewLine + "Your database is still at:" + Environment.NewLine + DatabasePath
				: "Your data is safe: nothing was changed. Your database is at:" + Environment.NewLine + DatabasePath)
		+ Environment.NewLine + Environment.NewLine
		+ "Please contact Pharma Remit support before using the app again, and do not delete these files." + Environment.NewLine
		+ "(Update step that failed: " + MigrationId + ")";

	private static Exception Innermost(Exception exception)
	{
		while (exception.InnerException != null)
		{
			exception = exception.InnerException;
		}
		return exception;
	}
}
