using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TodoManager;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskDatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TaskDatabaseTests", Guid.NewGuid().ToString("N"));
    private readonly TaskDatabase database;

    public TaskDatabaseTests()
    {
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaskDatabase:Path"] = Path.Combine(directory, "tasks.db")
            })
            .Build();
        database = new TaskDatabase(configuration, new TestEnvironment());
    }

    [Fact]
    public async Task FreshDatabaseReachesCurrentSchemaAndRepeatedInitializationDoesNotChangeIt()
    {
        await database.InitializeAsync();
        await using var connection = database.OpenConnection();
        await connection.OpenAsync();

        await AssertCurrentSchemaAsync(connection);
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks"));
        var schemaVersion = await ScalarAsync(connection, "PRAGMA schema_version");

        await database.InitializeAsync();

        Assert.Equal(schemaVersion, await ScalarAsync(connection, "PRAGMA schema_version"));
        await AssertCurrentSchemaAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO Tasks (Title, Notes, Priority, IsCompleted, CreatedAt)
            VALUES ('New task', '', 'Medium', 0, '2026-11-12T20:00:00+00:00');
            """);
        Assert.Equal("None", await ScalarAsync(connection, "SELECT Recurrence FROM Tasks"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT HasGeneratedNext FROM Tasks"));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    public async Task ExistingSchemasUpgradeOnceAndPreserveEveryTaskField(
        int version, bool recurrence, bool generatedNext)
    {
        await using var connection = database.OpenConnection();
        await connection.OpenAsync();
        await SeedAsync(connection, version, recurrence, generatedNext);
        object? schemaVersion = null;

        for (var initialization = 0; initialization < 2; initialization++)
        {
            await database.InitializeAsync();

            await AssertCurrentSchemaAsync(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Tasks";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(42, reader.GetInt64(reader.GetOrdinal("Id")));
                Assert.Equal("Existing task", reader.GetString(reader.GetOrdinal("Title")));
                Assert.Equal("Original notes", reader.GetString(reader.GetOrdinal("Notes")));
                Assert.Equal("High", reader.GetString(reader.GetOrdinal("Priority")));
                Assert.Equal("2026-11-25", reader.GetString(reader.GetOrdinal("DueDate")));
                Assert.Equal(1, reader.GetInt64(reader.GetOrdinal("IsCompleted")));
                Assert.Equal("2026-11-09T19:28:28+00:00", reader.GetString(reader.GetOrdinal("CreatedAt")));
                Assert.Equal(recurrence ? "Weekly" : "None", reader.GetString(reader.GetOrdinal("Recurrence")));
                Assert.Equal(generatedNext ? 1 : 0, reader.GetInt64(reader.GetOrdinal("HasGeneratedNext")));
                Assert.False(await reader.ReadAsync());
            }

            var currentSchemaVersion = await ScalarAsync(connection, "PRAGMA schema_version");
            if (initialization == 1)
                Assert.Equal(schemaVersion, currentSchemaVersion);
            schemaVersion = currentSchemaVersion;
        }

        await ExecuteAsync(connection, """
            INSERT INTO Tasks (Title, Notes, Priority, IsCompleted, CreatedAt)
            VALUES ('Next task', '', 'Low', 0, '2026-11-12T20:00:00+00:00');
            """);
        Assert.Equal(43L, await ScalarAsync(connection, "SELECT MAX(Id) FROM Tasks"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task UnsupportedVersionFailsWithoutChangingSchemaOrData(int version)
    {
        await using var connection = database.OpenConnection();
        await connection.OpenAsync();
        await SeedAsync(connection, version, true, true);
        var schemaVersion = await ScalarAsync(connection, "PRAGMA schema_version");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((long)version, await ScalarAsync(connection, "PRAGMA user_version"));
        Assert.Equal(schemaVersion, await ScalarAsync(connection, "PRAGMA schema_version"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Id = 42"));
        Assert.Equal("Weekly", await ScalarAsync(connection, "SELECT Recurrence FROM Tasks"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT HasGeneratedNext FROM Tasks"));
    }

    [Fact]
    public async Task FailedUpgradeRollsBackSchemaAndVersionAndCanBeRetried()
    {
        await using var connection = database.OpenConnection();
        await connection.OpenAsync();
        // A conflicting column makes migration 3 fail after migration 2 has run.
        await SeedAsync(connection, 1, false, true);
        var schemaVersion = await ScalarAsync(connection, "PRAGMA schema_version");

        await Assert.ThrowsAsync<SqliteException>(() => database.InitializeAsync());

        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA user_version"));
        Assert.Equal(schemaVersion, await ScalarAsync(connection, "PRAGMA schema_version"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('Tasks') WHERE name = 'Recurrence'"));
        Assert.Equal("Existing task", await ScalarAsync(connection, "SELECT Title FROM Tasks WHERE Id = 42"));
        await ExecuteAsync(connection, "ALTER TABLE Tasks DROP COLUMN HasGeneratedNext");

        await database.InitializeAsync();

        await AssertCurrentSchemaAsync(connection);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Id = 42"));
    }

    [Theory]
    [InlineData("Recurrence", "'Yearly'")]
    [InlineData("HasGeneratedNext", "2")]
    public async Task NewColumnsRetainCheckConstraints(string column, string value)
    {
        await using var connection = database.OpenConnection();
        await connection.OpenAsync();
        await SeedAsync(connection, 1, false, false);
        await database.InitializeAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteAsync(connection, $"UPDATE Tasks SET {column} = {value} WHERE Id = 42"));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(275, exception.SqliteExtendedErrorCode);
    }

    private static async Task AssertCurrentSchemaAsync(SqliteConnection connection)
    {
        Assert.Equal(3L, await ScalarAsync(connection, "PRAGMA user_version"));
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Tasks)";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        string[] expected =
            ["Id", "Title", "Notes", "Priority", "DueDate", "IsCompleted", "CreatedAt", "Recurrence", "HasGeneratedNext"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));
    }

    private static async Task SeedAsync(SqliteConnection connection, int version, bool recurrence, bool generatedNext)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE Tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
                Notes TEXT NOT NULL, Priority TEXT NOT NULL, DueDate TEXT,
                IsCompleted INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
            INSERT INTO Tasks VALUES (
                42, 'Existing task', 'Original notes', 'High', '2026-11-25', 1,
                '2026-11-09T19:28:28+00:00');
            """);
        if (recurrence)
            await ExecuteAsync(connection, """
                ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None'
                    CHECK(Recurrence IN ('None', 'Daily', 'Weekly', 'Monthly'));
                UPDATE Tasks SET Recurrence = 'Weekly';
                """);
        if (generatedNext)
            await ExecuteAsync(connection, """
                ALTER TABLE Tasks ADD COLUMN HasGeneratedNext INTEGER NOT NULL DEFAULT 0
                    CHECK(HasGeneratedNext IN (0, 1));
                UPDATE Tasks SET HasGeneratedNext = 1;
                """);
        await ExecuteAsync(connection, $"PRAGMA user_version = {version}");
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        using var connection = database.OpenConnection();
        SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TodoManager.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
