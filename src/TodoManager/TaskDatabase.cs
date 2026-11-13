using Microsoft.Data.Sqlite;

namespace TodoManager;

public sealed class TaskDatabase(IConfiguration configuration, IWebHostEnvironment environment)
{
    private static readonly (string Column, string Sql)[] Migrations =
    [
        ("Recurrence", """
            ALTER TABLE Tasks ADD COLUMN Recurrence TEXT NOT NULL DEFAULT 'None'
                CHECK(Recurrence IN ('None', 'Daily', 'Weekly', 'Monthly'));
            """),
        ("HasGeneratedNext", """
            ALTER TABLE Tasks ADD COLUMN HasGeneratedNext INTEGER NOT NULL DEFAULT 0
                CHECK(HasGeneratedNext IN (0, 1));
            """)
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
        await using (var journalModeCommand = connection.CreateCommand())
        {
            journalModeCommand.CommandText = "PRAGMA journal_mode=WAL";
            await journalModeCommand.ExecuteNonQueryAsync();
        }

        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var schemaVersion = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (schemaVersion < 0 || schemaVersion > Migrations.Length)
            throw new InvalidOperationException(
                $"Task database schema version {schemaVersion} is not supported.");

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

        while (schemaVersion < Migrations.Length)
        {
            var migration = Migrations[schemaVersion];
            var columns = await GetTaskColumnsAsync(command);
            if (!columns.Contains(migration.Column))
            {
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync();
            }

            schemaVersion++;
            command.CommandText = $"PRAGMA user_version = {schemaVersion}";
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task<HashSet<string>> GetTaskColumnsAsync(SqliteCommand command)
    {
        command.CommandText = "PRAGMA table_info(Tasks)";
        var columns = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
    }
}
