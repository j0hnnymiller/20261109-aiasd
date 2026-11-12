using Microsoft.Data.Sqlite;

namespace TodoManager;

public sealed class TaskDatabase(IConfiguration configuration, IWebHostEnvironment environment)
{
    public const int CurrentSchemaVersion = 3;

    // Ordered task-schema upgrades. Each runs once, tracked by PRAGMA user_version.
    // Databases created before versioning report version 0, so column upgrades check
    // whether the column already exists.
    private static readonly SchemaUpgrade[] Upgrades =
    [
        new(1, CreateTasksTableAsync),
        new(2, (connection, transaction) => AddColumnIfMissingAsync(connection, transaction, "Recurrence", """
            ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None'
                CHECK(Recurrence IN ('None', 'Daily', 'Weekly', 'Monthly'));
            """)),
        new(3, (connection, transaction) => AddColumnIfMissingAsync(connection, transaction, "HasGeneratedNext", """
            ALTER TABLE Tasks ADD COLUMN HasGeneratedNext INTEGER NOT NULL DEFAULT 0
                CHECK(HasGeneratedNext IN (0, 1));
            """))
    ];

    private readonly string path = Path.GetFullPath(
        configuration["TaskDatabase:Path"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "tasks.db"));

    public SqliteConnection OpenConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        ForeignKeys = true
    }.ToString());

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync();
        }

        using var transaction = connection.BeginTransaction();
        var version = await GetSchemaVersionAsync(connection, transaction);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Task database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        if (version == CurrentSchemaVersion)
            return;

        foreach (var upgrade in Upgrades.Where(upgrade => upgrade.Version > version))
            await upgrade.ApplyAsync(connection, transaction);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task<long> GetSchemaVersionAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task CreateTasksTableAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL CHECK(length(trim(Title)) BETWEEN 1 AND 200),
                Notes TEXT NOT NULL CHECK(length(Notes) <= 2000),
                Priority TEXT NOT NULL CHECK(Priority IN ('Low', 'Medium', 'High')),
                DueDate TEXT,
                IsCompleted INTEGER NOT NULL CHECK(IsCompleted IN (0, 1)),
                CreatedAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, SqliteTransaction transaction, string column, string alterSql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tasks') WHERE name = $name;";
        command.Parameters.AddWithValue("$name", column);
        if ((long)(await command.ExecuteScalarAsync() ?? 0L) > 0)
            return;

        command.Parameters.Clear();
        command.CommandText = alterSql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SchemaUpgrade(int Version, Func<SqliteConnection, SqliteTransaction, Task> ApplyAsync);
}
