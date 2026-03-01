namespace SchemaOrchestrator.Tests.Common;

internal static class TestDatabaseFiles
{
    public static string NewSqliteDbPath() => Path.Combine(Path.GetTempPath(), $"so_test_{Guid.NewGuid():N}.db");

    public static async Task DeleteFileWithRetryAsync(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }
}
