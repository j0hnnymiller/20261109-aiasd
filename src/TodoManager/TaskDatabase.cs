using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TodoManager;

public sealed class TaskDatabase(IConfiguration configuration, IWebHostEnvironment environment)
{
    private static readonly (string? Column, string Sql)[] Migrations =
    [
        (null, """
            CREATE TABLE IF NOT EXISTS Tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL CHECK(length(trim(Title)) BETWEEN 1 AND 200),
                Notes TEXT NOT NULL CHECK(length(Notes) <= 2000),
                Priority TEXT NOT NULL CHECK(Priority IN ('Low', 'Medium', 'High')),
                DueDate TEXT,
                IsCompleted INTEGER NOT NULL CHECK(IsCompleted IN (0, 1)),
                CreatedAt TEXT NOT NULL
            );
            """),
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
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL";
        await command.ExecuteNonQueryAsync();

        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The task database schema version could not be read."));
        if (version < 0 || version > Migrations.Length)
            throw new InvalidOperationException(
                $"Task database schema version {version} is unsupported. Supported versions are 0 through {Migrations.Length}.");

        var legacyColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (version == 0)
        {
            // Unversioned releases may already contain either or both added columns.
            command.CommandText = "PRAGMA table_info(Tasks)";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                legacyColumns.Add(reader.GetString(1));
        }

        for (var index = (int)version; index < Migrations.Length; index++)
        {
            var migration = Migrations[index];
            if (migration.Column is null || !legacyColumns.Contains(migration.Column))
            {
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync();
            }
            command.CommandText = $"PRAGMA user_version = {(index + 1).ToString(CultureInfo.InvariantCulture)}";
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
