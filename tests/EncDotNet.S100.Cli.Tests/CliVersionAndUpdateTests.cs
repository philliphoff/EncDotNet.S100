using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Covers CLI version reporting and automatic update notifications (issue #504).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class CliVersionAndUpdateTests
{
    [Fact]
    public async Task VersionOptionWritesInformationalVersionToStdout()
    {
        var version = new CliVersionInfo("2.4.1", "2.4.1+abc1234");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var exitCode = await CliRunner.RunAsync(
            ["--version"],
            version,
            new FixedUpdateChecker(null),
            standardError,
            CreateConsole(standardOut));

        Assert.Equal(0, exitCode);
        Assert.Equal($"2.4.1+abc1234{Environment.NewLine}", standardOut.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task UpdateNoticeIsWrittenOnlyToStderr()
    {
        var version = new CliVersionInfo("2.4.1", "2.4.1");
        var notice = new CliUpdateNotice(
            "2.4.1",
            "2.5.0",
            "https://github.com/philliphoff/EncDotNet.S100/releases/tag/v2.5.0");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var exitCode = await CliRunner.RunAsync(
            ["--version"],
            version,
            new FixedUpdateChecker(notice),
            standardError,
            CreateConsole(standardOut));

        Assert.Equal(0, exitCode);
        Assert.Equal($"2.4.1{Environment.NewLine}", standardOut.ToString());
        Assert.Equal($"{notice.Message}{Environment.NewLine}", standardError.ToString());
    }

    [Fact]
    public async Task UpdateTimeoutDoesNotChangeCommandResult()
    {
        var version = new CliVersionInfo("2.4.1", "2.4.1");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var exitCode = await CliRunner.RunAsync(
            ["--version"],
            version,
            new NeverCompletingUpdateChecker(),
            standardError,
            CreateConsole(standardOut),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, exitCode);
        Assert.Equal($"2.4.1{Environment.NewLine}", standardOut.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task UnavailableStderrDoesNotChangeCommandResult()
    {
        var version = new CliVersionInfo("2.4.1", "2.4.1");
        var notice = new CliUpdateNotice("2.4.1", "2.5.0", "https://example.test/v2.5.0");
        var standardOut = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            ["--version"],
            version,
            new FixedUpdateChecker(notice),
            new ThrowingTextWriter(),
            CreateConsole(standardOut));

        Assert.Equal(0, exitCode);
        Assert.Equal($"2.4.1{Environment.NewLine}", standardOut.ToString());
    }

    [Theory]
    [InlineData("v2.5.0", "2.4.1", true)]
    [InlineData("2.5.0+abc1234", "2.4.1", true)]
    [InlineData("2.5.0-rc.1", "2.4.1", true)]
    [InlineData("2.4.1", "2.4.1", false)]
    [InlineData("2.4.0", "2.4.1", false)]
    [InlineData("garbage", "2.4.1", false)]
    public void ReleaseComparisonHandlesReleaseTagForms(
        string candidate,
        string current,
        bool expected)
    {
        Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, current));
    }

    [Fact]
    public async Task DevelopmentBuildSkipsCacheAndNetwork()
    {
        var cache = new MemoryUpdateCache();
        var client = new FakeReleaseClient(new GitHubReleaseInfo("v9.9.9", null));
        var checker = CreateChecker("0.0.0-dev", client, cache);

        var notice = await checker.CheckAsync();

        Assert.Null(notice);
        Assert.Equal(0, cache.LoadCount);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task FreshCachedUpdateIsReportedOnEveryInvocationWithoutNetwork()
    {
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new MemoryUpdateCache
        {
            Entry = new CliUpdateCacheEntry(
                now - TimeSpan.FromHours(1),
                "2.5.0",
                "https://example.test/v2.5.0"),
        };
        var client = new FakeReleaseClient(new GitHubReleaseInfo("v9.9.9", null));
        var checker = CreateChecker("2.4.1", client, cache, now);

        var first = await checker.CheckAsync();
        var second = await checker.CheckAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("2.5.0", first.LatestVersion);
        Assert.Equal("2.5.0", second.LatestVersion);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task StaleCacheRefreshesAndPersistsLatestRelease()
    {
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new MemoryUpdateCache
        {
            Entry = new CliUpdateCacheEntry(
                now - CliUpdateChecker.ThrottleWindow,
                "2.5.0",
                "https://example.test/v2.5.0"),
        };
        var client = new FakeReleaseClient(
            new GitHubReleaseInfo("v2.6.0", "https://example.test/v2.6.0"));
        var checker = CreateChecker("2.4.1", client, cache, now);

        var notice = await checker.CheckAsync();

        Assert.NotNull(notice);
        Assert.Equal("2.6.0", notice.LatestVersion);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(1, cache.SaveCount);
        Assert.Equal("2.6.0", cache.Entry?.LatestVersion);
        Assert.Equal(now, cache.Entry?.CheckedAtUtc);
    }

    [Fact]
    public async Task FailedRefreshPreservesCachedNoticeAndThrottlesNextAttempt()
    {
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new MemoryUpdateCache
        {
            Entry = new CliUpdateCacheEntry(
                now - CliUpdateChecker.ThrottleWindow,
                "2.5.0",
                "https://example.test/v2.5.0"),
        };
        var client = new FakeReleaseClient(null);
        var checker = CreateChecker("2.4.1", client, cache, now);

        var first = await checker.CheckAsync();
        var second = await checker.CheckAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("2.5.0", first.LatestVersion);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(now, cache.Entry?.CheckedAtUtc);
    }

    [Theory]
    [InlineData("2.4.1")]
    [InlineData("2.4.0")]
    [InlineData("garbage")]
    public async Task CurrentOlderOrInvalidCachedReleaseIsSilent(string latestVersion)
    {
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new MemoryUpdateCache
        {
            Entry = new CliUpdateCacheEntry(now, latestVersion, "https://example.test"),
        };
        var checker = CreateChecker("2.4.1", new FakeReleaseClient(null), cache, now);

        Assert.Null(await checker.CheckAsync());
    }

    [Fact]
    public void GitHubReleaseParserMapsTagAndUrl()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "tag_name": "v2.5.0",
              "html_url": "https://github.com/philliphoff/EncDotNet.S100/releases/tag/v2.5.0"
            }
            """);

        var release = GitHubReleaseClient.Parse(document.RootElement);

        Assert.NotNull(release);
        Assert.Equal("v2.5.0", release.TagName);
        Assert.EndsWith("/v2.5.0", release.HtmlUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposedHttpClientFailsSilently()
    {
        var httpClient = new HttpClient();
        httpClient.Dispose();
        var client = new GitHubReleaseClient(httpClient);

        Assert.Null(await client.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task JsonCacheIgnoresCorruptOrUnwritablePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"s100-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var corruptPath = Path.Combine(directory, "corrupt.json");
            await File.WriteAllTextAsync(corruptPath, "{");
            var corruptCache = new JsonCliUpdateCache(corruptPath);
            Assert.Null(await corruptCache.LoadAsync());

            var directoryCache = new JsonCliUpdateCache(directory);
            await directoryCache.SaveAsync(
                new CliUpdateCacheEntry(DateTimeOffset.UtcNow, "2.5.0", "https://example.test"));
            Assert.True(Directory.Exists(directory));
        }

        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedFirstCheckIsCachedToAvoidRepeatedRequests()
    {
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new MemoryUpdateCache();
        var client = new FakeReleaseClient(null);
        var checker = CreateChecker("2.4.1", client, cache, now);

        Assert.Null(await checker.CheckAsync());
        Assert.Null(await checker.CheckAsync());

        Assert.Equal(1, client.CallCount);
        Assert.Equal(now, cache.Entry?.CheckedAtUtc);
        Assert.Null(cache.Entry?.LatestVersion);
    }

    [Fact]
    public async Task JsonCacheRoundTripsEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"s100-update-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "update-cache.json");
        var entry = new CliUpdateCacheEntry(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            "2.5.0",
            "https://example.test/v2.5.0");
        try
        {
            var cache = new JsonCliUpdateCache(path);

            await cache.SaveAsync(entry);
            var loaded = await cache.LoadAsync();

            Assert.Equal(entry, loaded);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CliUpdateChecker CreateChecker(
        string currentVersion,
        ICliReleaseClient releaseClient,
        ICliUpdateCache cache,
        DateTimeOffset? now = null)
    {
        return new CliUpdateChecker(
            new CliVersionInfo(currentVersion, currentVersion),
            releaseClient,
            cache,
            new FixedTimeProvider(now ?? DateTimeOffset.UtcNow));
    }

    private static IAnsiConsole CreateConsole(TextWriter writer)
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
    }

    private sealed class FixedUpdateChecker(CliUpdateNotice? notice) : ICliUpdateChecker
    {
        public Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(notice);
        }
    }

    private sealed class NeverCompletingUpdateChecker : ICliUpdateChecker
    {
        public Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<CliUpdateNotice?>();
            return completion.Task;
        }
    }

    private sealed class FakeReleaseClient(GitHubReleaseInfo? release) : ICliReleaseClient
    {
        public int CallCount { get; private set; }

        public Task<GitHubReleaseInfo?> GetLatestReleaseAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(release);
        }
    }

    private sealed class MemoryUpdateCache : ICliUpdateCache
    {
        public CliUpdateCacheEntry? Entry { get; set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<CliUpdateCacheEntry?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(Entry);
        }

        public Task SaveAsync(
            CliUpdateCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingTextWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            return Task.FromException(new IOException("Standard error is unavailable."));
        }
    }
}
