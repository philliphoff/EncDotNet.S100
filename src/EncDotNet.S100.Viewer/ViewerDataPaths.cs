using System;
using System.Collections.Generic;
using System.IO;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Single source of truth for every on-disk location the viewer writes to:
/// the persisted settings file, the crash-marker directory, and the three
/// disk caches (pattern-clip, portrayal-instruction, warm tile cache).
/// </summary>
/// <remarks>
/// <para>
/// By default each location keeps its historical per-user path so existing
/// installs are unaffected. When a <em>base directory</em> is supplied — via
/// the <c>--data-dir</c> command-line option or the <c>S100_DATA_DIR</c>
/// environment variable — <b>all</b> of these are re-rooted underneath it.
/// Pointing the base directory at an empty temp folder therefore yields a
/// guaranteed-fresh viewer instance (settings <em>and</em> caches), and
/// deleting that folder disposes of the instance's entire footprint. The
/// same mechanism lets an agent pre-seed settings or caches by populating
/// the folder before launch.
/// </para>
/// <para>
/// An explicit <c>--settings &lt;PATH&gt;</c> still overrides just the
/// settings-file location while the caches remain under the base directory.
/// </para>
/// </remarks>
internal sealed class ViewerDataPaths
{
    private const string DataDirEnvironmentVariable = "S100_DATA_DIR";

    private static readonly string DefaultSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EncDotNet.S100.Viewer");

    private static readonly string DefaultLocalDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EncDotNet.S100");

    private readonly string? _baseDirectory;
    private readonly string? _settingsFileOverride;

    /// <summary>
    /// Creates a path set. <paramref name="baseDirectory"/> re-roots every
    /// location; <see langword="null"/> or whitespace keeps the legacy
    /// per-user defaults. <paramref name="settingsFileOverride"/> pins just
    /// the settings file (the <c>--settings</c> flag) and leaves caches
    /// under the base directory.
    /// </summary>
    public ViewerDataPaths(string? baseDirectory = null, string? settingsFileOverride = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.GetFullPath(baseDirectory);
        _settingsFileOverride = string.IsNullOrWhiteSpace(settingsFileOverride)
            ? null
            : Path.GetFullPath(settingsFileOverride);
    }

    /// <summary>
    /// The base data directory all locations are re-rooted under, or
    /// <see langword="null"/> when the legacy per-user defaults are in use.
    /// </summary>
    public string? BaseDirectory => _baseDirectory;

    /// <summary>Absolute path of the persisted <c>settings.json</c> file.</summary>
    public string SettingsFilePath =>
        _settingsFileOverride
        ?? (_baseDirectory is { } b ? Path.Combine(b, "settings.json")
            : Path.Combine(DefaultSettingsDirectory, "settings.json"));

    /// <summary>Directory holding unclean-shutdown crash markers.</summary>
    public string CrashMarkersDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "crash-markers")
            : Path.Combine(DefaultSettingsDirectory, "crash-markers");

    /// <summary>Directory for the shared disk pattern-clip cache.</summary>
    public string PatternClipCacheDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "caches", "PatternClipCache")
            : Path.Combine(DefaultLocalDataDirectory, "PatternClipCache");

    /// <summary>Directory for the shared disk portrayal-instruction cache.</summary>
    public string PortrayalInstructionCacheDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "caches", "PortrayalInstructionCache")
            : Path.Combine(DefaultLocalDataDirectory, "PortrayalInstructionCache");

    /// <summary>
    /// Directory for the warm tile disk cache when a base directory is in
    /// use, or <see langword="null"/> to let the renderer pick its own
    /// default (the <c>S100_VECTOR_TILE_DISK_DIR</c> env var or an OS-temp
    /// subdirectory). Re-rooting it under the base directory keeps an
    /// isolated instance fully self-contained.
    /// </summary>
    public string? TileDiskCacheDirectory =>
        _baseDirectory is { } b ? Path.Combine(b, "caches", "tiles") : null;

    /// <summary>
    /// Every disk-cache directory the viewer may populate this run,
    /// suitable for a "clear caches" sweep. Excludes the settings file and
    /// crash markers. The tile-cache directory is included only when it is
    /// known (i.e. a base directory is in use); otherwise the renderer's
    /// own temp-rooted tile cache is enumerated by
    /// <see cref="Services.DataMaintenanceService"/> directly.
    /// </summary>
    public IReadOnlyList<string> CacheDirectories
    {
        get
        {
            var dirs = new List<string>(3)
            {
                PatternClipCacheDirectory,
                PortrayalInstructionCacheDirectory,
            };
            if (TileDiskCacheDirectory is { } tiles)
            {
                dirs.Add(tiles);
            }

            return dirs;
        }
    }

    /// <summary>
    /// Resolves the data paths for a run from the supplied command-line
    /// options, falling back to the <c>S100_DATA_DIR</c> environment
    /// variable when <c>--data-dir</c> is absent.
    /// </summary>
    public static ViewerDataPaths Resolve(ViewerCommandSettings? options)
    {
        var baseDir = options?.DataDir;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        }

        return new ViewerDataPaths(baseDir, options?.SettingsPath);
    }
}
