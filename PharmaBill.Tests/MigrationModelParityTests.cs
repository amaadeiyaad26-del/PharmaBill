using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>
/// The schema a fresh install gets from migrations must be exactly the schema the EF model expects.
/// Any column, table or index that only exists in the model means a migration was never written
/// (historically hidden by startup patchers), and fresh installs break at runtime.
/// </summary>
public sealed class MigrationModelParityTests
{
    // Created deliberately by migrations/EF but not part of the entity model.
    private static readonly HashSet<string> MigrationOnlyTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "__EFMigrationsHistory",
        "__EFMigrationsLock",
        "CatalogMedicineFts",
        "CatalogMedicineFts_data",
        "CatalogMedicineFts_idx",
        "CatalogMedicineFts_content",
        "CatalogMedicineFts_docsize",
        "CatalogMedicineFts_config"
    };

    [Fact]
    public async Task MigrationsCreateExactlyTheModelSchema()
    {
        await using var migrated = await DatabaseTestContext.CreateAsync(createSchema: false);
        await migrated.Context.Database.MigrateAsync();
        await using var model = await DatabaseTestContext.CreateAsync(createSchema: true);

        Dictionary<string, TableShape> fromMigrations = await ReadSchemaAsync(migrated.Connection);
        Dictionary<string, TableShape> fromModel = await ReadSchemaAsync(model.Connection);

        List<string> problems = new();
        foreach ((string table, TableShape expected) in fromModel.OrderBy(t => t.Key))
        {
            if (!fromMigrations.TryGetValue(table, out TableShape? actual))
            {
                problems.Add($"table {table}: in the model, no migration creates it");
                continue;
            }
            foreach ((string column, string definition) in expected.Columns.OrderBy(c => c.Key))
            {
                if (!actual.Columns.TryGetValue(column, out string? actualDefinition))
                {
                    problems.Add($"column {table}.{column} ({definition}): in the model, no migration creates it");
                }
                else if (!string.Equals(actualDefinition, definition, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"column {table}.{column}: model {definition}, migrations {actualDefinition}");
                }
            }
            foreach (string column in actual.Columns.Keys.Except(expected.Columns.Keys, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"column {table}.{column}: created by a migration but not in the model");
            }
            foreach (string index in expected.Indexes.Except(actual.Indexes, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"index {table}.{index}: in the model, no migration creates it");
            }
            foreach (string index in actual.Indexes.Except(expected.Indexes, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"index {table}.{index}: created by a migration but not in the model");
            }
        }
        foreach (string table in fromMigrations.Keys.Except(fromModel.Keys, StringComparer.OrdinalIgnoreCase).Where(t => !MigrationOnlyTables.Contains(t)))
        {
            problems.Add($"table {table}: created by a migration but not in the model");
        }

        Assert.True(problems.Count == 0,
            "Migrations and the EF model disagree. Write a migration for each item:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private sealed record TableShape(Dictionary<string, string> Columns, HashSet<string> Indexes);

    private static async Task<Dictionary<string, TableShape>> ReadSchemaAsync(SqliteConnection connection)
    {
        List<string> tables = new();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Dictionary<string, TableShape> schema = new(StringComparer.OrdinalIgnoreCase);
        foreach (string table in tables.Where(t => !MigrationOnlyTables.Contains(t)))
        {
            Dictionary<string, string> columns = new(StringComparer.OrdinalIgnoreCase);
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns[reader.GetString(1)] = reader.GetString(2) + (reader.GetInt32(3) == 1 ? " NOT NULL" : " NULL");
                }
            }
            HashSet<string> indexes = new(StringComparer.OrdinalIgnoreCase);
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $table AND name NOT LIKE 'sqlite_autoindex%'";
                command.Parameters.AddWithValue("$table", table);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    indexes.Add(reader.GetString(0));
                }
            }
            schema[table] = new TableShape(columns, indexes);
        }
        return schema;
    }
}
