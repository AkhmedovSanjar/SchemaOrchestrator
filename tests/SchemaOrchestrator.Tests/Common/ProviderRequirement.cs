namespace SchemaOrchestrator.Tests.Common;

internal static class ProviderRequirement
{
    public static string? TryGetSqlServer()
    {
        return ProviderTestEnvironment.HasSqlServer
            ? ProviderTestEnvironment.SqlServerConnectionString
            : null;
    }

    public static string? TryGetPostgreSql()
    {
        return ProviderTestEnvironment.HasPostgreSql
            ? ProviderTestEnvironment.PostgreSqlConnectionString
            : null;
    }
}
