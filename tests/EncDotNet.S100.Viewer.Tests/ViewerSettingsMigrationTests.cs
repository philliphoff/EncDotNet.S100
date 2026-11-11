namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-D2.1: legacy <c>OwnShipVisible</c> bool migrates into the
/// <see cref="ViewerSettings.DynamicSourceVisibility"/> dictionary
/// on first load, and downgrade compat is preserved on save.
/// </summary>
public class ViewerSettingsMigrationTests
{
    [Fact]
    public void Load_LegacyOwnShipVisibleFalse_MigratesIntoDict()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path, "{\"OwnShipVisible\":false}");

        try
        {
            var s = ViewerSettings.Load(path);

            Assert.True(s.DynamicSourceVisibility.TryGetValue(
                ViewerSettings.OwnShipVisibilityKey, out var v));
            Assert.False(v);
            Assert.False(s.OwnShipVisible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NewFormatPresent_PreservesDictAndDoesNotOverwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path,
            "{\"OwnShipVisible\":true,\"DynamicSourceVisibility\":{\"ownship\":false,\"ais\":true}}");

        try
        {
            var s = ViewerSettings.Load(path);

            Assert.False(s.DynamicSourceVisibility["ownship"]);
            Assert.True(s.DynamicSourceVisibility["ais"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_MirrorsDictBackToLegacyField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        try
        {
            var s = ViewerSettings.Load(path);
            s.DynamicSourceVisibility[ViewerSettings.OwnShipVisibilityKey] = false;
            s.Save();

            var reloaded = ViewerSettings.Load(path);
            Assert.False(reloaded.OwnShipVisible);
            Assert.False(reloaded.DynamicSourceVisibility[ViewerSettings.OwnShipVisibilityKey]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Issue #295: BasemapEnabled bool → BasemapMode migration ──────

    [Fact]
    public void Load_LegacyBasemapEnabledTrue_MigratesToOnline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path, "{\"BasemapEnabled\":true}");
        try
        {
            var s = ViewerSettings.Load(path);
            Assert.Equal(BasemapMode.Online, s.BasemapMode);
            Assert.True(s.BasemapEnabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_LegacyBasemapEnabledFalse_MigratesToNone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path, "{\"BasemapEnabled\":false}");
        try
        {
            var s = ViewerSettings.Load(path);
            Assert.Equal(BasemapMode.None, s.BasemapMode);
            Assert.False(s.BasemapEnabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_NewBasemapModePresent_IsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path, "{\"BasemapEnabled\":false,\"BasemapMode\":\"Offline\"}");
        try
        {
            var s = ViewerSettings.Load(path);
            Assert.Equal(BasemapMode.Offline, s.BasemapMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Default_BasemapMode_IsOffline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Path.GetRandomFileName()}.json");
        try
        {
            var s = ViewerSettings.Load(path);
            Assert.Equal(BasemapMode.Offline, s.BasemapMode);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
