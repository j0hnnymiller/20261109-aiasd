using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TodoManager.Tests;

public abstract class TaskApiTestBase : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoManagerTests", Guid.NewGuid().ToString("N"));

    protected string DatabasePath => Path.Combine(directory, "tasks.db");

    protected WebApplicationFactory<Program> CreateApp(TimeProvider? timeProvider = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("TaskDatabase:Path", DatabasePath).UseEnvironment("Development");
                builder.ConfigureServices(services =>
                    services.AddSingleton<TimeProvider>(timeProvider ?? new FixedClock()));
            });

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2028, 1, 31, 23, 30, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Test local", TimeSpan.FromHours(2), "Test local", "Test local");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
