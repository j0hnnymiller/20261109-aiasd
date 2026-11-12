using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskDatabaseTests : IDisposable
{
    private const string BaseTable = """
        CREATE TABLE Tasks (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
            Notes TEXT NOT NULL, Priority TEXT NOT NULL, DueDate TEXT,
            IsCompleted INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
        """;

    private const string BaseRow = """
        INSERT INTO Tasks (Id, Title, Notes, Priority, DueDate, IsCompleted, CreatedAt) VALUES (
            7, 'Existing task', 'Original notes', 'High', '2026-11-25', 1, '2026-11-09T19:28:28+00:00');
        """;

    private static readonly string[] CurrentColumns =
        ["Id", "Title", "Notes", "Priority", "DueDate", "IsCompleted", "CreatedAt", "Recurrence", "HasGeneratedNext"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoManagerTests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "tasks.db");

    private TaskDatabase CreateDatabase() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TaskDatabase:Path"] = DatabasePath })
            .Build(),
        new TestEnvironment(directory));

    [Fact]
    public async Task FreshDatabaseInitializesToCurrentSchema()
    {
        await CreateDatabase().InitializeAsync();

        Assert.Equal(TaskDatabase.CurrentSchemaVersion, await ScalarAsync<long>("PRAGMA user_version"));
        Assert.Equal(CurrentColumns, await ColumnsAsync());
        Assert.Equal(0, await ScalarAsync<long>("SELECT COUNT(*) FROM Tasks"));
    }

    public static TheoryData<string, string, string, long> SupportedStartingSchemas => new()
    {
        { "unversioned original", BaseTable + BaseRow, "None", 0 },
        {
            "unversioned with recurrence",
            BaseTable + "ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None';" + BaseRow +
                "UPDATE Tasks SET Recurrence = 'Weekly';",
            "Weekly", 0
        },
        {
            "unversioned current",
            BaseTable + """
                ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None';
                ALTER TABLE Tasks ADD COLUMN HasGeneratedNext INTEGER NOT NULL DEFAULT 0;
                """ + BaseRow + "UPDATE Tasks SET Recurrence = 'Daily', HasGeneratedNext = 1;",
            "Daily", 1
        },
        {
            "version 2",
            BaseTable + "ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None';" + BaseRow +
                "UPDATE Tasks SET Recurrence = 'Monthly'; PRAGMA user_version = 2;",
            "Monthly", 0
        }
    };

    [Theory]
    [MemberData(nameof(SupportedStartingSchemas))]
    public async Task ExistingDatabaseUpgradesWithoutDataLoss(
        string schema, string setupSql, string expectedRecurrence, long expectedHasGeneratedNext)
    {
        await ExecuteAsync(setupSql);

        for (var run = 0; run < 2; run++)
        {
            await CreateDatabase().InitializeAsync();

            Assert.Equal(TaskDatabase.CurrentSchemaVersion, await ScalarAsync<long>("PRAGMA user_version"));
            Assert.Equal(CurrentColumns, await ColumnsAsync());
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Title, Notes, Priority, DueDate, IsCompleted, CreatedAt, Recurrence, HasGeneratedNext
                FROM Tasks
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), schema);
            Assert.Equal(7, reader.GetInt64(0));
            Assert.Equal("Existing task", reader.GetString(1));
            Assert.Equal("Original notes", reader.GetString(2));
            Assert.Equal("High", reader.GetString(3));
            Assert.Equal("2026-11-25", reader.GetString(4));
            Assert.Equal(1, reader.GetInt64(5));
            Assert.Equal("2026-11-09T19:28:28+00:00", reader.GetString(6));
            Assert.Equal(expectedRecurrence, reader.GetString(7));
            Assert.Equal(expectedHasGeneratedNext, reader.GetInt64(8));
            Assert.False(await reader.ReadAsync());
        }
    }

    [Fact]
    public async Task CurrentVersionDoesNotReapplyUpgrades()
    {
        await ExecuteAsync(BaseTable + BaseRow + $"PRAGMA user_version = {TaskDatabase.CurrentSchemaVersion};");

        await CreateDatabase().InitializeAsync();

        Assert.Equal(CurrentColumns[..7], await ColumnsAsync());
        Assert.Equal(1, await ScalarAsync<long>("SELECT COUNT(*) FROM Tasks"));
    }

    [Fact]
    public async Task NewerSchemaVersionIsRejectedWithoutChanges()
    {
        var newer = TaskDatabase.CurrentSchemaVersion + 1;
        await ExecuteAsync(BaseTable + BaseRow + $"PRAGMA user_version = {newer};");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDatabase().InitializeAsync());

        Assert.Contains($"version {newer}", error.Message);
        Assert.Equal(newer, await ScalarAsync<long>("PRAGMA user_version"));
        Assert.Equal(CurrentColumns[..7], await ColumnsAsync());
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private async Task<string[]> ColumnsAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('Tasks') ORDER BY cid";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return [.. columns];
    }

    public void Dispose()
    {
        using var applicationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        }.ToString());
        using var directConnection = new SqliteConnection($"Data Source={DatabasePath}");
        SqliteConnection.ClearPool(applicationConnection);
        SqliteConnection.ClearPool(directConnection);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "TodoManager";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }
}
