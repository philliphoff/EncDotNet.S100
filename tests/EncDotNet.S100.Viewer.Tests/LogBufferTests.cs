using EncDotNet.S100.Viewer.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The in-memory log buffer behind the Logs panel and feedback reports
/// (#1080): ring eviction, collapsing repeats, its own minimum level, batched
/// change notifications and thread safety.
/// </summary>
public sealed class LogBufferTests
{
    private const string Category = "EncDotNet.S100.Viewer.Services.DatasetLoaderService";

    [Fact]
    public void Keeps_the_newest_entries_up_to_its_capacity()
    {
        using var buffer = new LogBuffer(capacity: 3);
        for (var i = 1; i <= 5; i++)
            buffer.Add(LogLevel.Information, Category, $"message {i}");

        var entries = buffer.Snapshot();

        Assert.Equal(["message 3", "message 4", "message 5"], entries.Select(e => e.Message));
        Assert.Equal([3L, 4L, 5L], entries.Select(e => e.Sequence));
        Assert.Equal(5, buffer.TotalAdded);
    }

    [Fact]
    public void Collapses_consecutive_repeats_into_one_entry_with_a_count()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        using var buffer = new LogBuffer(timeProvider: time);

        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        time.Advance(TimeSpan.FromSeconds(2));
        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        time.Advance(TimeSpan.FromSeconds(3));
        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        buffer.Add(LogLevel.Information, Category, "Loaded");
        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");

        var entries = buffer.Snapshot();

        Assert.Equal(3, entries.Count);
        Assert.Equal(3, entries[0].Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), entries[0].Timestamp);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 5, TimeSpan.Zero), entries[0].LastTimestamp);
        Assert.Equal(1, entries[1].Count);
        Assert.Equal(1, entries[2].Count);
        Assert.Equal(5, buffer.TotalAdded);
        Assert.Equal(4, buffer.ProblemsAdded);
    }

    [Fact]
    public void Does_not_collapse_messages_that_differ_in_level_category_or_exception()
    {
        using var buffer = new LogBuffer();
        buffer.Add(LogLevel.Warning, Category, "same");
        buffer.Add(LogLevel.Error, Category, "same");
        buffer.Add(LogLevel.Error, "Other.Category", "same");
        buffer.Add(LogLevel.Error, "Other.Category", "same", new InvalidOperationException("boom"));

        Assert.Equal(4, buffer.Snapshot().Count);
    }

    [Fact]
    public void Has_its_own_minimum_level()
    {
        using var buffer = new LogBuffer(minimumLevel: LogLevel.Information);
        buffer.Add(LogLevel.Debug, Category, "hidden");
        buffer.Add(LogLevel.Information, Category, "kept");

        buffer.MinimumLevel = LogLevel.Debug;
        buffer.Add(LogLevel.Debug, Category, "now kept");

        Assert.Equal(["kept", "now kept"], buffer.Snapshot().Select(e => e.Message));
        Assert.False(buffer.IsEnabled(LogLevel.Trace));
        Assert.False(buffer.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void Clear_removes_entries_but_sequences_keep_counting()
    {
        using var buffer = new LogBuffer();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Add(LogLevel.Information, Category, "two");
        buffer.Clear();
        buffer.Add(LogLevel.Information, Category, "three");

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Equal(3, entry.Sequence);
    }

    [Fact]
    public void Batches_change_notifications()
    {
        var time = new FakeTimeProvider();
        using var buffer = new LogBuffer(timeProvider: time, notifyInterval: TimeSpan.FromMilliseconds(250));
        var raised = 0;
        buffer.Changed += (_, _) => Interlocked.Increment(ref raised);

        for (var i = 0; i < 500; i++)
            buffer.Add(LogLevel.Information, Category, $"message {i}");
        Assert.Equal(0, raised);

        time.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, raised);

        // Nothing new: no further notification.
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, raised);

        buffer.Add(LogLevel.Information, Category, "later");
        buffer.Flush();
        Assert.Equal(2, raised);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Concurrent_writers_lose_no_messages()
    {
        using var buffer = new LogBuffer(capacity: 10_000);
        const int writers = 8;
        const int perWriter = 1_000;

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var snapshot = buffer.Snapshot();
                for (var j = 1; j < snapshot.Count; j++)
                    Assert.True(snapshot[j].Sequence > snapshot[j - 1].Sequence);
            }
        }, TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
                buffer.Add(LogLevel.Information, $"Writer{w}", $"message {i}");
        }, TestContext.Current.CancellationToken)));
        await reader;

        var entries = buffer.Snapshot();
        Assert.Equal(writers * perWriter, entries.Sum(e => e.Count));
        Assert.Equal(writers * perWriter, buffer.TotalAdded);
        Assert.Equal(entries.Count, entries.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void Logger_provider_captures_below_the_global_minimum_when_the_buffer_asks()
    {
        // The pipeline's global floor stays at Information (what OTLP gets);
        // the buffer's own level decides what the Logs panel records.
        using var buffer = new LogBuffer(minimumLevel: LogLevel.Information);
        using var provider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                LogBufferLoggerProvider.AddLogBuffer(builder, buffer);
            })
            .BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(Category);

        logger.LogDebug("debug before verbose");
        logger.LogInformation("Loaded {Name} in {Ms} ms", "US5MA1BO", 42);
        buffer.MinimumLevel = LogLevel.Debug;
        logger.LogDebug("debug after verbose");
        logger.LogError(new InvalidOperationException("boom"), "Load failed");

        var entries = buffer.Snapshot();
        Assert.Equal(["Loaded US5MA1BO in 42 ms", "debug after verbose", "Load failed"], entries.Select(e => e.Message));
        Assert.Equal(Category, entries[0].Category);
        Assert.Equal("Datasets", entries[0].Area);
        Assert.Contains("System.InvalidOperationException: boom", entries[2].Exception);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.AspNetCore.Server.Kestrel")]
    [InlineData("ModelContextProtocol.Server.McpServer")]
    [InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
    public void Logger_provider_keeps_hosting_library_chatter_to_warnings(string category)
    {
        // MCP and HTTP traffic logs several entries per request; even with
        // Debug capture on, only its warnings and errors reach the buffer.
        using var buffer = new LogBuffer(minimumLevel: LogLevel.Debug);
        using var provider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                LogBufferLoggerProvider.AddLogBuffer(builder, buffer);
            })
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var chatty = factory.CreateLogger(category);
        var viewer = factory.CreateLogger("EncDotNet.S100.Viewer.Services.McpServerHost");

        chatty.LogDebug("connection opened");
        chatty.LogInformation("request finished");
        chatty.LogWarning("request failed");
        viewer.LogInformation("MCP server listening");
        viewer.LogDebug("MCP tool called");

        Assert.Equal(
            ["request failed", "MCP server listening", "MCP tool called"],
            buffer.Snapshot().Select(e => e.Message));
    }

    [Theory]
    [InlineData("EncDotNet.S100.Viewer.Services.Secom.SecomIdentityStore", "SECOM")]
    [InlineData("EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient", "Updates")]
    [InlineData("EncDotNet.S100.Viewer.Services.DynamicSources.Ais.DeferredAisFeatureSource", "AIS")]
    [InlineData("EncDotNet.S100.Collections.Library.LibrarySync", "Library")]
    [InlineData("EncDotNet.S100.Viewer.Library.UserCatalogueStore", "Library")]
    [InlineData("EncDotNet.S100.Viewer.Command", "Commands")]
    [InlineData("EncDotNet.S100.Viewer.Services.NavigationalWarningSeenStore", "Warnings")]
    [InlineData("EncDotNet.S100.Viewer.Services.LazyLoading.ExchangeSetLazyLoadCoordinator", "Datasets")]
    [InlineData("EncDotNet.S100.Viewer.Services.DataMaintenanceService", "Storage")]
    [InlineData("EncDotNet.S100.Viewer.App", "App")]
    [InlineData("Microsoft.Hosting.Lifetime", "System")]
    [InlineData("Some.Vendor.Widget", "Widget")]
    [InlineData("EncDotNet.S100.Viewer.Services.ApplicationControlService", "App")]
    public void Categories_map_to_friendly_areas(string category, string area) =>
        Assert.Equal(area, LogAreas.Resolve(category));

    [Fact]
    public void Every_area_has_a_display_name()
    {
        var resx = File.ReadAllText(LibraryTestContext.RepoFile("src", "EncDotNet.S100.Viewer", "Resources", "Strings.resx"));
        foreach (var area in LogAreas.KnownAreas)
            Assert.Contains($"<data name=\"LogArea_{area}\"", resx);
        Assert.Equal("Own ship", Resources.Strings.LogAreaName("OwnShip"));
        Assert.Equal("Widget", Resources.Strings.LogAreaName("Widget"));
    }

    [Fact]
    public void Entry_text_has_time_level_area_message_count_and_exception()
    {
        var at = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
        var entry = new LogEntry(7, at, at.AddSeconds(3), LogLevel.Warning, Category, "Tile fetch failed", "System.Exception: x", Count: 4);

        var text = entry.ToText();

        Assert.Contains("WARN  [Datasets] " + Category + ": Tile fetch failed (×4, last ", text);
        Assert.EndsWith(Environment.NewLine + "System.Exception: x", text);
    }
}
