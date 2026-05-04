using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class RecentFilesServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public RecentFilesServiceTests()
    {
        // Each test file gets its own temp settings path so Save() doesn't
        // touch the real per-user settings.json under ApplicationData.
        _tempSettingsPath = Path.Combine(
            Path.GetTempPath(),
            $"viewer-settings-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath))
            File.Delete(_tempSettingsPath);
    }

    private (RecentFilesService Service, ViewerSettings Settings) Create()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        return (new RecentFilesService(settings), settings);
    }

    [Fact]
    public void Add_StoresPath_AndRaisesChanged()
    {
        var (svc, settings) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Add("/tmp/a.000");

        Assert.Equal(new[] { "/tmp/a.000" }, svc.Items);
        Assert.Equal(new[] { "/tmp/a.000" }, settings.RecentDatasetPaths);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Add_IgnoresNullOrWhitespace()
    {
        var (svc, _) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Add("");
        svc.Add("   ");

        Assert.Empty(svc.Items);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Add_DeduplicatesAndPromotes()
    {
        var (svc, _) = Create();
        svc.Add("/tmp/a");
        svc.Add("/tmp/b");
        svc.Add("/tmp/a");

        Assert.Equal(new[] { "/tmp/a", "/tmp/b" }, svc.Items);
    }

    [Fact]
    public void Remove_DropsPath_AndRaisesChanged_OnlyWhenSomethingRemoved()
    {
        var (svc, _) = Create();
        svc.Add("/tmp/a");
        svc.Add("/tmp/b");

        var fired = 0;
        svc.Changed += () => fired++;

        svc.Remove("/tmp/missing");
        Assert.Equal(0, fired);

        svc.Remove("/tmp/a");
        Assert.Equal(new[] { "/tmp/b" }, svc.Items);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_EmptiesList_AndRaisesChanged_OnlyWhenNonEmpty()
    {
        var (svc, _) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Clear();
        Assert.Equal(0, fired);

        svc.Add("/tmp/a");
        fired = 0;
        svc.Clear();

        Assert.Empty(svc.Items);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Constructor_PrunesEntriesPointingToMissingFiles()
    {
        // Pre-seed settings with a mix of existing and missing files, then
        // confirm the service drops the missing ones at construction time.
        var existing = Path.Combine(Path.GetTempPath(), $"viewer-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(existing, "stub");
        try
        {
            var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
            settings.RecentDatasetPaths.Add("/tmp/does-not-exist-xyzzy");
            settings.RecentDatasetPaths.Add(existing);
            settings.RecentDatasetPaths.Add("");
            settings.RecentDatasetPaths.Add("/another/missing");

            var svc = new RecentFilesService(settings);

            Assert.Equal(new[] { existing }, svc.Items);
        }
        finally
        {
            File.Delete(existing);
        }
    }
}
