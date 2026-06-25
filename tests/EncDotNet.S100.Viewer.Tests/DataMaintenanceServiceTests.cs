using System;
using System.IO;
using System.Linq;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="DataMaintenanceService"/>: the cache-directory
/// enumeration used by "Clear caches" and the clean-slate "Reset all"
/// behaviour. Tests use a base directory so all deletions stay contained
/// under a temp folder.
/// </summary>
public class DataMaintenanceServiceTests
{
    private static DataMaintenanceService NewService(string baseDir, out ViewerDataPaths paths, out ViewerSettings settings)
    {
        paths = new ViewerDataPaths(baseDir);
        settings = new ViewerSettings { SettingsFilePath = paths.SettingsFilePath };
        return new DataMaintenanceService(paths, settings, NullLogger<DataMaintenanceService>.Instance);
    }

    [Fact]
    public void CacheDirectories_IncludePatternPortrayalAndTile()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        var service = NewService(baseDir, out var paths, out _);

        Assert.Contains(paths.PatternClipCacheDirectory, service.CacheDirectories);
        Assert.Contains(paths.PortrayalInstructionCacheDirectory, service.CacheDirectories);
        // The third entry is the renderer's effective tile-cache directory.
        Assert.Equal(3, service.CacheDirectories.Count);
    }

    [Fact]
    public void ClearCaches_DeletesContainedCacheDirectories()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        var service = NewService(baseDir, out var paths, out _);

        Directory.CreateDirectory(paths.PatternClipCacheDirectory);
        File.WriteAllText(Path.Combine(paths.PatternClipCacheDirectory, "x.bin"), "data");
        Directory.CreateDirectory(paths.PortrayalInstructionCacheDirectory);

        try
        {
            service.ClearCaches();

            Assert.False(Directory.Exists(paths.PatternClipCacheDirectory));
            Assert.False(Directory.Exists(paths.PortrayalInstructionCacheDirectory));
        }
        finally
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResetAll_DeletesSettingsCrashMarkersAndCaches_AndSuppressesSaves()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        var service = NewService(baseDir, out var paths, out var settings);

        Directory.CreateDirectory(paths.BaseDirectory!);
        File.WriteAllText(paths.SettingsFilePath, "{}");
        Directory.CreateDirectory(paths.CrashMarkersDirectory);
        Directory.CreateDirectory(paths.PatternClipCacheDirectory);

        try
        {
            service.ResetAll();

            Assert.True(settings.IsReadOnly);
            Assert.False(File.Exists(paths.SettingsFilePath));
            Assert.False(Directory.Exists(paths.CrashMarkersDirectory));
            Assert.False(Directory.Exists(paths.PatternClipCacheDirectory));
        }
        finally
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { }
        }
    }
}
