using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskRecurrenceApiTests : TaskApiTestBase
{
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

    [Theory]
    [InlineData(2028, 1, 31, "2028-02-29")]
    [InlineData(2027, 1, 31, "2027-02-28")]
    [InlineData(2028, 12, 31, "2029-01-31")]
    public async Task MonthlyRecurrenceClampsCalendarDates(int year, int month, int day, string expected)
    {
        using var app = CreateApp(new CalendarClock(year, month, day));
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
}
