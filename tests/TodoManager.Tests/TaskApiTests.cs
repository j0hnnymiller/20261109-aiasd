using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TodoManager.Tests;

public sealed class TaskApiTests : TaskApiTestBase
{
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
            Assert.True(task.TryGetProperty("createdAt", out _));
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

}
