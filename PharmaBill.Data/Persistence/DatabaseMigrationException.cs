using System;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// A schema migration failed. Startup must stop: running on a partly upgraded pharmacy database is never safe,
/// and nothing may "repair" the schema behind the migration history's back.
/// </summary>
public sealed class DatabaseMigrationException(string migrationId, string databasePath, Exception innerException)
	: InvalidOperationException(
		$"Database upgrade failed at migration '{migrationId}': {Innermost(innerException).Message} "
		+ "PharmaBill has stopped so it does not run on a partly upgraded database. "
		+ $"The database file was not deleted ({databasePath}). Restore a backup or contact support.",
		innerException)
{
	public string MigrationId { get; } = migrationId;

	public string DatabasePath { get; } = databasePath;

	private static Exception Innermost(Exception exception)
	{
		while (exception.InnerException != null)
		{
			exception = exception.InnerException;
		}
		return exception;
	}
}
