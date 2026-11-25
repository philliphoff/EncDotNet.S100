namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="ViewerDataPaths"/>, the single source of truth that maps
/// the settings file and every disk cache either to their legacy per-user
/// locations or — when <c>--data-dir</c> / <c>S100_DATA_DIR</c> is supplied —
/// under one re-rooted base directory.
/// </summary>
public class ViewerDataPathsTests
{
    [Fact]
    public void Default_UsesLegacyPerUserLocations()
    {
        var paths = new ViewerDataPaths();

        Assert.Null(paths.BaseDirectory);
        Assert.EndsWith(Path.Combine("EncDotNet.S100", "tiles"), paths.TileDiskCacheDirectory);
        Assert.EndsWith(Path.Combine("EncDotNet.S100.Viewer", "settings.json"), paths.SettingsFilePath);
        Assert.EndsWith(Path.Combine("EncDotNet.S100.Viewer", "crash-markers"), paths.CrashMarkersDirectory);
        Assert.EndsWith(Path.Combine("EncDotNet.S100", "PatternClipCache"), paths.PatternClipCacheDirectory);
        Assert.EndsWith(Path.Combine("EncDotNet.S100", "PortrayalInstructionCache"), paths.PortrayalInstructionCacheDirectory);
    }

    [Fact]
    public void Default_LocationsAreAbsolutePathsInTheUsersFolders()
    {
        // GetFolderPath returns "" for a folder that doesn't exist yet (a new
        // Linux account's ~/.config or ~/.local/share), which made these paths
        // relative to wherever SoundCharts was started. ViewerUserFolders asks
        // without verifying, so they're always rooted in the user's folders.
        var paths = new ViewerDataPaths();

        Assert.True(Path.IsPathRooted(ViewerUserFolders.ApplicationData), ViewerUserFolders.ApplicationData);
        Assert.True(Path.IsPathRooted(ViewerUserFolders.LocalApplicationData), ViewerUserFolders.LocalApplicationData);
        Assert.Equal(Path.Combine(ViewerUserFolders.ApplicationData, "EncDotNet.S100.Viewer", "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(ViewerUserFolders.ApplicationData, "EncDotNet.S100.Viewer", "tide-stations.json.gz"), paths.TideStationsFilePath);
        Assert.StartsWith(ViewerUserFolders.LocalApplicationData, paths.CacheStoreDirectory, StringComparison.Ordinal);
        Assert.Equal(
            Path.Combine(ViewerUserFolders.ApplicationData, "EncDotNet.S100.Viewer", "crash-markers"),
            EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.DefaultDirectory);
    }

    [Fact]
    public void BaseDirectory_ReRootsEveryLocation()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-test-" + Guid.NewGuid().ToString("N"));

        var paths = new ViewerDataPaths(baseDir);

        Assert.Equal(Path.GetFullPath(baseDir), paths.BaseDirectory);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "crash-markers"), paths.CrashMarkersDirectory);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "caches", "PatternClipCache"), paths.PatternClipCacheDirectory);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "caches", "PortrayalInstructionCache"), paths.PortrayalInstructionCacheDirectory);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "caches", "tiles"), paths.TileDiskCacheDirectory);
    }

    [Fact]
    public void SettingsFileOverride_PinsSettingsButLeavesCachesUnderBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-test-" + Guid.NewGuid().ToString("N"));
        var settingsOverride = Path.Combine(Path.GetTempPath(), "custom-" + Guid.NewGuid().ToString("N"), "my-settings.json");

        var paths = new ViewerDataPaths(baseDir, settingsOverride);

        Assert.Equal(Path.GetFullPath(settingsOverride), paths.SettingsFilePath);
        // Caches stay under the base directory even though settings is pinned.
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "caches", "PatternClipCache"), paths.PatternClipCacheDirectory);
        Assert.Equal(Path.Combine(paths.BaseDirectory!, "caches", "tiles"), paths.TileDiskCacheDirectory);
    }

    [Fact]
    public void CacheDirectories_IncludeTheTileCache()
    {
        var withoutBase = new ViewerDataPaths();
        Assert.Equal(8, withoutBase.CacheDirectories.Count);
        Assert.Contains(withoutBase.CacheDirectories, d => d.EndsWith("tiles"));
        Assert.Contains(withoutBase.RuleProbeCacheDirectory, withoutBase.CacheDirectories);

        var withBase = new ViewerDataPaths(Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(8, withBase.CacheDirectories.Count);
        Assert.Contains(withBase.CacheDirectories, d => d.EndsWith("tiles"));
    }

    [Fact]
    public void Resolve_PrefersDataDirOptionOverEnvironmentVariable()
    {
        var optionDir = Path.Combine(Path.GetTempPath(), "opt-" + Guid.NewGuid().ToString("N"));
        var envDir = Path.Combine(Path.GetTempPath(), "env-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("S100_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("S100_DATA_DIR", envDir);

            var paths = ViewerDataPaths.Resolve(new ViewerCommandSettings { DataDir = optionDir });

            Assert.Equal(Path.GetFullPath(optionDir), paths.BaseDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("S100_DATA_DIR", previous);
        }
    }

    [Fact]
    public void Resolve_FallsBackToEnvironmentVariableWhenOptionAbsent()
    {
        var envDir = Path.Combine(Path.GetTempPath(), "env-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("S100_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("S100_DATA_DIR", envDir);

            var paths = ViewerDataPaths.Resolve(new ViewerCommandSettings());

            Assert.Equal(Path.GetFullPath(envDir), paths.BaseDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("S100_DATA_DIR", previous);
        }
    }

    [Fact]
    public void Resolve_PassesSettingsOverrideThrough()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "base-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(Path.GetTempPath(), "s-" + Guid.NewGuid().ToString("N") + ".json");

        var paths = ViewerDataPaths.Resolve(new ViewerCommandSettings
        {
            DataDir = baseDir,
            SettingsPath = settingsFile,
        });

        Assert.Equal(Path.GetFullPath(settingsFile), paths.SettingsFilePath);
        Assert.Equal(Path.GetFullPath(baseDir), paths.BaseDirectory);
    }
}
