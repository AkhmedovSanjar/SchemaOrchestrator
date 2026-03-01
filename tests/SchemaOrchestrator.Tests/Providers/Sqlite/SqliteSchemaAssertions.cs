using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Tests.Providers.Sqlite;

internal static class SqliteSchemaAssertions
{
    public static async Task<bool> TableExistsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name";
        cmd.Parameters.AddWithValue("$name", tableName);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(value) > 0;
    }

    public static async Task<IReadOnlyList<string>> GetColumnsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string tableName)
    {
        var result = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(1));
        }

        return result;
    }

    public static async Task<int> CountByPrefixAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string tableName, string columnName, string prefix)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM {tableName} WHERE {columnName} LIKE $prefix";
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }
}
