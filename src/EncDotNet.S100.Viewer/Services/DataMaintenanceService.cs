using EncDotNet.S100.Renderers.Mapsui;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Wipes the viewer's on-disk caches and performs a full "reset all"
/// clean slate (settings + crash markers + caches). Used by the Settings
/// panel's maintenance buttons.
/// </summary>
internal interface IDataMaintenanceService
{
    /// <summary>
    /// The disk-cache directories this run may populate, in deletion
    /// order. Exposed mainly for diagnostics and tests.
    /// </summary>
    IReadOnlyList<string> CacheDirectories { get; }

    /// <summary>
    /// Deletes every on-disk cache directory (pattern-clip,
    /// portrayal-instruction, warm tile cache). Best-effort: individual
    /// failures are logged and skipped. Leaves settings and crash history
    /// untouched.
    /// </summary>
    void ClearCaches();

    /// <summary>
    /// Performs a full reset for a clean-slate restart: suppresses further
    /// settings persistence, deletes the settings file and crash markers,
    /// and clears all disk caches. The caller is responsible for restarting
    /// the process afterwards.
    /// </summary>
    void ResetAll();
}

/// <inheritdoc />
internal sealed class DataMaintenanceService : IDataMaintenanceService
{
    private readonly ViewerDataPaths _paths;
    private readonly ViewerSettings _settings;
    private readonly ILogger<DataMaintenanceService> _logger;

    public DataMaintenanceService(
        ViewerDataPaths paths,
        ViewerSettings settings,
        ILogger<DataMaintenanceService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<string> CacheDirectories =>
        new[]
        {
            _paths.PatternClipCacheDirectory,
            _paths.PortrayalInstructionCacheDirectory,
            // The effective tile-cache root (data-dir, env, or OS-temp).
            S100VectorTileRenderer.ResolveTileDiskDirectory(),
        };

    public void ClearCaches()
    {
        foreach (var dir in CacheDirectories)
        {
            DeleteDirectory(dir);
        }
    }

    public void ResetAll()
    {
        // Stop any further settings writes and remove the settings file so
        // the next launch starts from defaults.
        _settings.ResetForRestart();

        // Crash markers are not "caches" — only wiped on a full reset.
        DeleteDirectory(_paths.CrashMarkersDirectory);

        ClearCaches();
    }

    private void DeleteDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a locked or in-use file must not abort the sweep.
            _logger.LogWarning(ex, "Failed to delete cache directory {Directory}.", dir);
        }
    }
}
