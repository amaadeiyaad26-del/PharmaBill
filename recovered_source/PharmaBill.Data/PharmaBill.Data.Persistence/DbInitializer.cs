using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Services;

namespace PharmaBill.Data.Persistence;

public sealed class DbInitializer(PharmaBillDbContext context, StorageLocationService storageLocations, BranchService branchService)
{
	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await context.Database.MigrateAsync(cancellationToken);
		await EnsureSocialAuthColumnsAsync(cancellationToken);
		await EnsureUserManualEmailColumnsAsync(cancellationToken);
		await storageLocations.EnsureDefaultRetailLocationAsync(cancellationToken);
		await branchService.EnsureCurrentBranchAsync(cancellationToken);
	}

	private async Task EnsureUserManualEmailColumnsAsync(CancellationToken cancellationToken)
	{
		HashSet<string> columns = await GetTableColumnsAsync("PharmacyProfiles", cancellationToken);
		if (!columns.Contains("UserManualEmailSent"))
		{
			await context.Database.ExecuteSqlRawAsync("ALTER TABLE PharmacyProfiles ADD COLUMN UserManualEmailSent INTEGER NOT NULL DEFAULT 0;", cancellationToken);
		}
		if (!columns.Contains("UserManualSentAtUtc"))
		{
			await context.Database.ExecuteSqlRawAsync("ALTER TABLE PharmacyProfiles ADD COLUMN UserManualSentAtUtc TEXT NULL;", cancellationToken);
		}
	}

	private async Task EnsureSocialAuthColumnsAsync(CancellationToken cancellationToken)
	{
		HashSet<string> columns = await GetTableColumnsAsync("AppUsers", cancellationToken);
		if (!columns.Contains("AuthProvider"))
		{
			await context.Database.ExecuteSqlRawAsync("ALTER TABLE AppUsers ADD COLUMN AuthProvider TEXT NULL;", cancellationToken);
		}
		if (!columns.Contains("ProviderSubjectId"))
		{
			await context.Database.ExecuteSqlRawAsync("ALTER TABLE AppUsers ADD COLUMN ProviderSubjectId TEXT NULL;", cancellationToken);
		}
		columns = await GetTableColumnsAsync("AppUsers", cancellationToken);
		if (columns.Contains("AuthProvider") && columns.Contains("ProviderSubjectId"))
		{
			await EnsureIndexAsync("IX_AppUsers_AuthProvider_ProviderSubjectId", "CREATE INDEX IF NOT EXISTS IX_AppUsers_AuthProvider_ProviderSubjectId ON AppUsers (AuthProvider, ProviderSubjectId);", cancellationToken);
		}
		if (columns.Contains("Email"))
		{
			await EnsureIndexAsync("IX_AppUsers_Email", "CREATE INDEX IF NOT EXISTS IX_AppUsers_Email ON AppUsers (Email);", cancellationToken);
		}
	}

	private async Task<HashSet<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken)
	{
		HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		DbConnection connection = context.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
		}
		HashSet<string> result;
		await using (DbCommand command = connection.CreateCommand())
		{
			command.CommandText = "PRAGMA table_info(\"" + tableName + "\");";
			HashSet<string> hashSet;
			await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
			{
				while (await reader.ReadAsync(cancellationToken))
				{
					if (!reader.IsDBNull(1))
					{
						columns.Add(reader.GetString(1));
					}
				}
				hashSet = columns;
			}
			result = hashSet;
		}
		return result;
	}

	private async Task EnsureIndexAsync(string indexName, string createSql, CancellationToken cancellationToken)
	{
		DbConnection connection = context.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
		}
		await using (DbCommand check = connection.CreateCommand())
		{
			check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1;";
			DbParameter dbParameter = check.CreateParameter();
			dbParameter.ParameterName = "$name";
			dbParameter.Value = indexName;
			check.Parameters.Add(dbParameter);
			if (await check.ExecuteScalarAsync(cancellationToken) != null)
			{
				return;
			}
		}
		await context.Database.ExecuteSqlRawAsync(createSql, cancellationToken);
	}
}
