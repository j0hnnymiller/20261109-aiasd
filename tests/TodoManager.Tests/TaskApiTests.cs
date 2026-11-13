using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoManagerTests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "tasks.db");

    private WebApplicationFactory<Program> CreateApp() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TaskDatabase:Path", DatabasePath).UseEnvironment("Development")
                .UseSetting("FeatureFlags:Login", "false");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedClock()));
        });

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2028, 1, 31, 23, 30, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Test local", TimeSpan.FromHours(2), "Test local", "Test local");
    }

    [Fact]
    public async Task CrudAndPersistenceWorkAcrossHostRestarts()
    {
        long id;
        using (var app = CreateApp())
        using (var client = app.CreateClient())
        {
            Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
            var created = await client.PostAsJsonAsync("/api/tasks", new
            {
                title = "  Plan the week  ",
                notes = "  First steps  ",
                priority = "High",
                dueDate = "2026-11-16"
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var task = await created.Content.ReadFromJsonAsync<JsonElement>();
            id = task.GetProperty("id").GetInt64();
            Assert.Equal($"/api/tasks/{id}", created.Headers.Location?.ToString());
            Assert.Equal("Plan the week", task.GetProperty("title").GetString());
            Assert.Equal("First steps", task.GetProperty("notes").GetString());
            Assert.False(task.GetProperty("isCompleted").GetBoolean());
            Assert.Equal(
                new DateTimeOffset(2028, 1, 31, 23, 30, 0, TimeSpan.Zero),
                task.GetProperty("createdAt").GetDateTimeOffset());
        }
        using (var app = CreateApp())
        using (var client = app.CreateClient())
        {
            var persisted = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}");
            Assert.Equal("High", persisted.GetProperty("priority").GetString());
            Assert.Equal("2026-11-16", persisted.GetProperty("dueDate").GetString());
            var updated = await client.PutAsJsonAsync($"/api/tasks/{id}", new
            {
                title = "Finished planning",
                notes = "",
                priority = "Low",
                isCompleted = true
            });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var task = await updated.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(task.GetProperty("isCompleted").GetBoolean());
            Assert.Equal(JsonValueKind.Null, task.GetProperty("dueDate").ValueKind);
            Assert.Equal("Finished planning", task.GetProperty("title").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{id}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"\"}")]
    [InlineData("{\"title\":\"   \"}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"Valid\",\"priority\":\"Urgent\"}")]
    [InlineData("{\"title\":\"Valid\",\"priority\":99}")]
    [InlineData("{\"title\":\"Valid\",\"dueDate\":\"2026-02-30\"}")]
    [InlineData("{\"title\":\"Valid\",\"recurrence\":\"Yearly\"}")]
    [InlineData("{\"title\":\"Valid\",\"recurrence\":1}")]
    public async Task InvalidInputReturnsProblemDetailsWithoutSaving(string json)
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Post ? "/api/tasks" : "/api/tasks/1")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
    }

    [Theory]
    [InlineData(201, 0)]
    [InlineData(5, 2001)]
    public async Task OversizeFieldsAreRejected(int titleLength, int notesLength)
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tasks", new
        {
            title = new string('a', titleLength),
            notes = new string('b', notesLength)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaximumLengthFieldsAreAccepted()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var title = new string('t', 200);
        var notes = new string('n', 2000);

        var response = await client.PostAsJsonAsync("/api/tasks", new { title, notes });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(title, task.GetProperty("title").GetString());
        Assert.Equal(notes, task.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task MissingResourcesReturn404AndOpenApiListsEveryOperation()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tasks/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/tasks/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/tasks/0", new { title = "Missing" })).StatusCode);
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = spec.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/tasks").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/tasks").TryGetProperty("get", out _));
        foreach (var method in new[] { "get", "put", "delete" })
            Assert.True(paths.GetProperty("/api/tasks/{id}").TryGetProperty(method, out _));
    }

    [Fact]
    public async Task BrowserAssetsAreServedAndTaskTextIsStoredLiterally()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("daybook", await page.Content.ReadAsStringAsync());
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        foreach (var path in new[] { "/app.js", "/styles.css" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        const string title = "<script>alert('hello')</script> '; DROP TABLE Tasks; --";
        var created = await client.PostAsJsonAsync("/api/tasks", new { title });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tasks = await client.GetFromJsonAsync<JsonElement[]>("/api/tasks");
        Assert.Equal(title, Assert.Single(tasks!).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("Daily", "2028-02-02")]
    [InlineData("Weekly", "2028-02-08")]
    [InlineData("Monthly", "2028-03-01")]
    public async Task CompletingRecurringTaskCreatesOneSuccessorFromLocalCompletionDate(
        string recurrence, string expectedDue)
    {
        long id;
        using (var app = CreateApp())
        using (var client = app.CreateClient())
        {
            var input = new
            {
                title = "Review",
                notes = "Keep these",
                priority = "High",
                dueDate = "2020-03-20",
                recurrence,
                isCompleted = false
            };
            var created = await client.PostAsJsonAsync("/api/tasks", input);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var original = await created.Content.ReadFromJsonAsync<JsonElement>();
            id = original.GetProperty("id").GetInt64();
            Assert.Equal(recurrence, original.GetProperty("recurrence").GetString());
            Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);

            var completion = await client.PutAsJsonAsync($"/api/tasks/{id}", input with { isCompleted = true });
            Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
            var tasks = (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!;
            Assert.Equal(2, tasks.Length);
            Assert.True(tasks.Single(task => task.GetProperty("id").GetInt64() == id).GetProperty("isCompleted").GetBoolean());
            var next = tasks.Single(task => task.GetProperty("id").GetInt64() != id);
            Assert.False(next.GetProperty("isCompleted").GetBoolean());
            Assert.Equal(expectedDue, next.GetProperty("dueDate").GetString());
            Assert.Equal(recurrence, next.GetProperty("recurrence").GetString());
            Assert.Equal("Review", next.GetProperty("title").GetString());
            Assert.Equal("Keep these", next.GetProperty("notes").GetString());
            Assert.Equal("High", next.GetProperty("priority").GetString());

            var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
                client.PutAsJsonAsync($"/api/tasks/{id}", input with { isCompleted = true })));
            Assert.All(retries, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tasks/{id}", input)).StatusCode);
        }
        using (var app = CreateApp())
        using (var client = app.CreateClient())
        {
            var completion = await client.PutAsJsonAsync($"/api/tasks/{id}", new
            {
                title = "Review",
                recurrence,
                isCompleted = true
            });
            Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
            Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!.Length);
        }
    }

    [Fact]
    public async Task RecurrenceCanBeEditedDisabledAndUsedWithoutInitialDueDate()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/tasks", new { title = "No due date", recurrence = "Daily" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var initial = (await created.Content.ReadFromJsonAsync<JsonElement>())!;
        var id = initial.GetProperty("id").GetInt64();
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("dueDate").ValueKind);
        var completion = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            title = "Updated",
            recurrence = "Monthly",
            isCompleted = true
        });
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        var updated = (await completion.Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.True(updated.GetProperty("isCompleted").GetBoolean());
        Assert.Equal("Updated", updated.GetProperty("title").GetString());
        Assert.Equal("Monthly", updated.GetProperty("recurrence").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("dueDate").ValueKind);
        var tasks = (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!;
        Assert.Equal(2, tasks.Length);
        var original = tasks.Single(task => task.GetProperty("id").GetInt64() == id);
        Assert.True(original.GetProperty("isCompleted").GetBoolean());
        Assert.Equal("Monthly", original.GetProperty("recurrence").GetString());
        var next = tasks.Single(task => !task.GetProperty("isCompleted").GetBoolean());
        var nextId = next.GetProperty("id").GetInt64();
        Assert.Equal("Monthly", next.GetProperty("recurrence").GetString());
        Assert.Equal("2028-03-01", next.GetProperty("dueDate").GetString());
        Assert.Equal("Updated", next.GetProperty("title").GetString());
        Assert.Equal(string.Empty, next.GetProperty("notes").GetString());
        Assert.Equal("Medium", next.GetProperty("priority").GetString());
        var stopped = await client.PutAsJsonAsync($"/api/tasks/{nextId}", new
        {
            title = "Stop",
            recurrence = "None",
            isCompleted = true
        });
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        var stoppedTask = (await stopped.Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.True(stoppedTask.GetProperty("isCompleted").GetBoolean());
        Assert.Equal("None", stoppedTask.GetProperty("recurrence").GetString());
        Assert.Equal("Stop", stoppedTask.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, stoppedTask.GetProperty("dueDate").ValueKind);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!.Length);
    }

    [Fact]
    public async Task ConcurrentFirstCompletionsGenerateOnlyOneSuccessor()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Concurrent", recurrence = "Daily" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PutAsJsonAsync($"/api/tasks/{id}", new { title = "Concurrent", recurrence = "Daily", isCompleted = true })));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!.Length);
    }

    [Fact]
    public async Task FailedSuccessorInsertRollsBackCompletionAndAllowsRetry()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Atomic", recurrence = "Daily" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER FailSuccessor BEFORE INSERT ON Tasks
            BEGIN SELECT RAISE(ABORT, 'Test insert failure'); END;
            """;
        await command.ExecuteNonQueryAsync();
        var complete = new { title = "Atomic", recurrence = "Daily", isCompleted = true };
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PutAsJsonAsync($"/api/tasks/{id}", complete)).StatusCode);
        var unchanged = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
        Assert.False(unchanged.GetProperty("isCompleted").GetBoolean());
        command.CommandText = "DROP TRIGGER FailSuccessor";
        await command.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tasks/{id}", complete)).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!.Length);
    }

    [Fact]
    public async Task FreshDatabaseIsInitializedAtCurrentSchemaVersion()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);

        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task LegacyDatabaseIsUpgradedWithoutLosingRecords()
    {
        Directory.CreateDirectory(directory);
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
        await using var upgradedConnection = new SqliteConnection($"Data Source={DatabasePath}");
        await upgradedConnection.OpenAsync();
        await using var versionCommand = upgradedConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PreviouslyCurrentUnversionedDatabaseIsMarkedAtCurrentSchemaVersion()
    {
        Directory.CreateDirectory(directory);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Tasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
                    Notes TEXT NOT NULL, Priority TEXT NOT NULL, DueDate TEXT,
                    IsCompleted INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                    Recurrence TEXT NOT NULL DEFAULT 'None',
                    HasGeneratedNext INTEGER NOT NULL DEFAULT 0);
                INSERT INTO Tasks VALUES (
                    1, 'Existing task', 'Original notes', 'Medium', '2026-11-25', 0,
                    '2026-11-09T19:28:28+00:00', 'Weekly', 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var app = CreateApp();
        using var client = app.CreateClient();
        var task = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
        Assert.Equal("Existing task", task.GetProperty("title").GetString());
        Assert.Equal("Weekly", task.GetProperty("recurrence").GetString());

        await using var upgradedConnection = new SqliteConnection($"Data Source={DatabasePath}");
        await upgradedConnection.OpenAsync();
        await using var versionCommand = upgradedConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task VersionOneDatabaseIsUpgradedToCurrentSchema()
    {
        Directory.CreateDirectory(directory);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Tasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
                    Notes TEXT NOT NULL, Priority TEXT NOT NULL, DueDate TEXT,
                    IsCompleted INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                    Recurrence TEXT NOT NULL DEFAULT 'None');
                INSERT INTO Tasks VALUES (
                    1, 'Version one task', 'Original notes', 'Medium', '2026-11-25', 0,
                    '2026-11-09T19:28:28+00:00', 'Daily');
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var app = CreateApp();
        using var client = app.CreateClient();
        var task = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!);
        Assert.Equal("Version one task", task.GetProperty("title").GetString());
        Assert.Equal("Daily", task.GetProperty("recurrence").GetString());

        await using var upgradedConnection = new SqliteConnection($"Data Source={DatabasePath}");
        await upgradedConnection.OpenAsync();
        await using var versionCommand = upgradedConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)(await versionCommand.ExecuteScalarAsync())!);
        versionCommand.CommandText = "SELECT HasGeneratedNext FROM Tasks WHERE Id = 1";
        Assert.Equal(0L, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData(2028, 1, 31, "2028-02-29")]
    [InlineData(2027, 1, 31, "2027-02-28")]
    [InlineData(2028, 12, 31, "2029-01-31")]
    public async Task MonthlyRecurrenceClampsCalendarDates(int year, int month, int day, string expected)
    {
        using var app = CreateApp().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new CalendarClock(year, month, day))));
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Calendar", recurrence = "Monthly" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            title = "Calendar",
            recurrence = "Monthly",
            isCompleted = true
        })).StatusCode);
        var next = (await client.GetFromJsonAsync<JsonElement[]>("/api/tasks"))!
            .Single(task => !task.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(expected, next.GetProperty("dueDate").GetString());
    }

    private sealed class CalendarClock(int year, int month, int day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(year, month, day, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
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
}
