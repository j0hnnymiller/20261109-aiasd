using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskDatabaseMigrationTests : TaskApiTestBase
{
    [Fact]
    public async Task LegacyDatabaseIsUpgradedWithoutLosingRecords()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Tasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
                    Notes TEXT NOT NULL, Priority TEXT NOT NULL, DueDate TEXT,
                    IsCompleted INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
                INSERT INTO Tasks VALUES (
                    1, 'Existing task', 'Original notes', 'Medium', '2026-11-25', 0,
                    '2026-11-09T19:28:28+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }
        for (var restart = 0; restart < 2; restart++)
        {
            using var app = CreateApp();
            using var client = app.CreateClient();
            var tasks = (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!;
            var original = Assert.Single(tasks);
            Assert.Equal(1, original.GetProperty("id").GetInt64());
            Assert.Equal("Existing task", original.GetProperty("title").GetString());
            Assert.Equal("Original notes", original.GetProperty("notes").GetString());
            Assert.Equal("2026-11-25", original.GetProperty("dueDate").GetString());
            Assert.Equal("None", original.GetProperty("recurrence").GetString());
        }
    }
}
