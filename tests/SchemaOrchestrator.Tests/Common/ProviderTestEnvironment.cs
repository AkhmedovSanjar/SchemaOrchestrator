namespace SchemaOrchestrator.Tests.Common;

internal static class ProviderTestEnvironment
{
    public static string? SqlServerConnectionString => Environment.GetEnvironmentVariable("SO_TEST_SQLSERVER_CONN");
    public static string? PostgreSqlConnectionString => Environment.GetEnvironmentVariable("SO_TEST_POSTGRES_CONN");

    public static bool HasSqlServer => !string.IsNullOrWhiteSpace(SqlServerConnectionString);
    public static bool HasPostgreSql => !string.IsNullOrWhiteSpace(PostgreSqlConnectionString);
}
