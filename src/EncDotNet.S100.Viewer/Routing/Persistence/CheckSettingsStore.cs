using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Viewer.Routing.Persistence;

/// <summary>
/// The mariner's check settings for received routes (#960, decision 16),
/// kept locally by <see cref="RouteSource.CheckSettingsKey"/> (route ID and
/// edition) in <c>check-settings.json</c>, so reopening a received route
/// restores them and a newer edition starts again from its file's values.
/// The file also lists the route files open without a dataset (RTZ, #961;
/// handoff F8), which reopen after a restart.
/// </summary>
/// <remarks>
/// Like <see cref="RouteStore"/>, loading is tolerant: a missing or malformed
/// file gives an empty store. Not thread-safe; use it from the UI thread.
/// </remarks>
internal sealed partial class CheckSettingsStore
{
    private static readonly CheckSettingsJsonContext Json = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    private readonly Dictionary<string, RouteCheckSettings> _entries = new(StringComparer.Ordinal);
    private List<string> _openFiles = [];
    private readonly string? _path;

    /// <summary>Creates a store backed by <paramref name="path"/>, or in memory only when it's <c>null</c>.</summary>
    /// <param name="path">The <c>check-settings.json</c> path, or <c>null</c>.</param>
    public CheckSettingsStore(string? path = null)
    {
        _path = path;
    }

    /// <summary>The settings kept for <paramref name="key"/>, or <c>null</c>.</summary>
    /// <param name="key">A <see cref="RouteSource.CheckSettingsKey"/>.</param>
    /// <returns>A copy of the settings.</returns>
    public RouteCheckSettings? Get(string key)
        => _entries.TryGetValue(key, out var settings) ? settings.Clone() : null;

    /// <summary>Keeps <paramref name="settings"/> for <paramref name="key"/>; empty settings remove the entry.</summary>
    /// <param name="key">A <see cref="RouteSource.CheckSettingsKey"/>.</param>
    /// <param name="settings">The settings.</param>
    /// <returns><c>true</c> when the store changed.</returns>
    public bool Set(string key, RouteCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsEmpty)
            return _entries.Remove(key);
        if (_entries.TryGetValue(key, out var existing) && Same(existing, settings))
            return false;
        _entries[key] = settings.Clone();
        return true;
    }

    /// <summary>The route files open without a dataset (RTZ), in the order they were opened.</summary>
    public IReadOnlyList<string> OpenFiles => _openFiles;

    /// <summary>Replaces the list of route files open without a dataset.</summary>
    /// <param name="paths">The files, in order.</param>
    /// <returns><c>true</c> when the list changed.</returns>
    public bool SetOpenFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var list = paths.ToList();
        if (list.SequenceEqual(_openFiles, StringComparer.Ordinal))
            return false;
        _openFiles = list;
        return true;
    }

    /// <summary>Loads the file, replacing what the store holds. Failures leave it empty.</summary>
    public void Load()
    {
        _entries.Clear();
        _openFiles = [];
        if (_path is null || !File.Exists(_path))
            return;
        try
        {
            var document = JsonSerializer.Deserialize(File.ReadAllText(_path), Json.CheckSettingsDocument);
            if (document?.SchemaVersion != CheckSettingsDocument.CurrentSchemaVersion)
                return;
            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrEmpty(entry.Key))
                    continue;
                _entries[entry.Key] = new RouteCheckSettings
                {
                    DraftMeters = entry.DraftMeters,
                    SquatMeters = entry.SquatMeters,
                    RequiredUkcMeters = entry.RequiredUkcMeters,
                    Departure = entry.Departure,
                };
            }
            _openFiles = document.OpenFiles?.Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _entries.Clear();
            _openFiles = [];
        }
    }

    /// <summary>Writes the store to its file, staged through a temporary file. Failures are swallowed.</summary>
    public void Save()
    {
        if (_path is null)
            return;
        var document = new CheckSettingsDocument
        {
            Entries = _entries
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new CheckSettingsEntry
                {
                    Key = e.Key,
                    DraftMeters = e.Value.DraftMeters,
                    SquatMeters = e.Value.SquatMeters,
                    RequiredUkcMeters = e.Value.RequiredUkcMeters,
                    Departure = e.Value.Departure,
                })
                .ToList(),
            OpenFiles = _openFiles.Count > 0 ? _openFiles.ToList() : null,
        };
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, Json.CheckSettingsDocument));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort, like routes.json: the next change tries again.
        }
    }

    private static bool Same(RouteCheckSettings a, RouteCheckSettings b)
        => a.DraftMeters == b.DraftMeters && a.SquatMeters == b.SquatMeters
            && a.RequiredUkcMeters == b.RequiredUkcMeters && a.Departure == b.Departure;

    [JsonSerializable(typeof(CheckSettingsDocument))]
    private sealed partial class CheckSettingsJsonContext : JsonSerializerContext;
}

/// <summary>On-disk shape of <c>check-settings.json</c>.</summary>
internal sealed class CheckSettingsDocument
{
    /// <summary>The schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The document's schema version.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>One entry per received route (route ID + edition).</summary>
    public List<CheckSettingsEntry> Entries { get; set; } = new();

    /// <summary>Route files open without a dataset (RTZ), reopened at startup; absent when none.</summary>
    public List<string>? OpenFiles { get; set; }
}

/// <summary>One received route's check settings on disk.</summary>
internal sealed class CheckSettingsEntry
{
    /// <summary>The <see cref="RouteSource.CheckSettingsKey"/>.</summary>
    public string? Key { get; set; }

    /// <summary>Draft, in metres.</summary>
    public double? DraftMeters { get; set; }

    /// <summary>Squat allowance, in metres.</summary>
    public double? SquatMeters { get; set; }

    /// <summary>Required static UKC, in metres.</summary>
    public double? RequiredUkcMeters { get; set; }

    /// <summary>Planned departure.</summary>
    public DateTimeOffset? Departure { get; set; }
}
