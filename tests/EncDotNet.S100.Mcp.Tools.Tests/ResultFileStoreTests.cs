namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>The folder of large tool results (#1615): ids, resource URIs and expiry.</summary>
public sealed class ResultFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "s100-store-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_written_file_is_found_by_id_and_by_uri()
    {
        var store = new ResultFileStore(_directory, "grid", "s100://grids/");

        var file = await store.WriteAsync("npy", [1, 2, 3], TestContext.Current.CancellationToken);

        Assert.Equal("s100://grids/" + file.Id, file.Uri);
        Assert.Equal(3, file.Bytes);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken));
        Assert.True(store.TryGet(file.Id, out var byId));
        Assert.Equal(file, byId);
        Assert.True(store.TryGet(file.Uri, out var byUri));
        Assert.Equal(file, byUri);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("grid-20990101T000000-0000000.npy")]
    [InlineData("other.txt")]
    [InlineData("")]
    public async Task Names_the_store_could_not_have_written_are_not_served(string id)
    {
        var store = new ResultFileStore(_directory, "grid", "s100://grids/");
        await store.WriteAsync("npy", [1], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "other.txt"), "x", TestContext.Current.CancellationToken);

        Assert.False(store.TryGet(id, out _));
    }

    [Fact]
    public async Task Only_the_newest_files_are_kept_and_other_files_are_left_alone()
    {
        var store = new ResultFileStore(_directory, "grid", "s100://grids/", maxFiles: 2);
        Directory.CreateDirectory(_directory);
        var unrelated = Path.Combine(_directory, "notes.txt");
        await File.WriteAllTextAsync(unrelated, "keep me", TestContext.Current.CancellationToken);

        var first = await store.WriteAsync("bin", [1], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(first.Path, DateTime.UtcNow.AddMinutes(-2));
        var second = await store.WriteAsync("bin", [2], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(second.Path, DateTime.UtcNow.AddMinutes(-1));
        var third = await store.WriteAsync("bin", [3], TestContext.Current.CancellationToken);

        Assert.False(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
        Assert.True(File.Exists(third.Path));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task Files_older_than_the_age_limit_are_deleted()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var store = new ResultFileStore(_directory, "grid", "s100://grids/", maxAge: TimeSpan.FromHours(1), time: clock);

        var old = await store.WriteAsync("bin", [1], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(old.Path, clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var kept = await store.WriteAsync("bin", [2], TestContext.Current.CancellationToken);

        Assert.False(File.Exists(old.Path));
        Assert.True(File.Exists(kept.Path));
        Assert.False(store.TryGet(old.Uri, out _));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
