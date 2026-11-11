namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="CrashLog.ReadTail"/>: returning the trailing portion
/// of the crash log for attachment to a next-startup feedback report.
/// </summary>
public sealed class CrashLogTests : IDisposable
{
    private readonly string _path;

    public CrashLogTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"viewer-crash-{Guid.NewGuid():N}.log");
        CrashLog.ConfigurePath(_path);
    }

    public void Dispose()
    {
        CrashLog.ConfigurePath(null);
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void ReadTail_MissingFile_ReturnsNull()
    {
        Assert.Null(CrashLog.ReadTail(100));
    }

    [Fact]
    public void ReadTail_NonPositiveMax_ReturnsNull()
    {
        CrashLog.Append("FATAL", "boom");
        Assert.Null(CrashLog.ReadTail(0));
        Assert.Null(CrashLog.ReadTail(-5));
    }

    [Fact]
    public void ReadTail_ShorterThanMax_ReturnsWholeFile()
    {
        CrashLog.Append("FATAL", "boom");

        var tail = CrashLog.ReadTail(10_000);

        Assert.NotNull(tail);
        Assert.Contains("FATAL", tail);
        Assert.Contains("boom", tail);
    }

    [Fact]
    public void ReadTail_LongerThanMax_ReturnsTrailingCharacters()
    {
        File.WriteAllText(_path, new string('a', 100) + "TAILMARKER");

        var tail = CrashLog.ReadTail(10);

        Assert.Equal("TAILMARKER", tail);
    }
}
