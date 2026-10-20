using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The mariner's classification of a navigational warning's relevance to
/// the intended route (S-98 Edition 2.0.0 §12.12.2; #996).
/// </summary>
internal enum NavigationalWarningClassification
{
    /// <summary>Relevant to the route: always drawn (by the in-force rules). The default.</summary>
    OnChart,

    /// <summary>Not relevant to the route: not drawn, but still listed.</summary>
    OffChart,

    /// <summary>Relevant to the route, for information: not drawn, but still listed.</summary>
    Information,
}

/// <summary>
/// Each navigational warning's classification (#996), kept in the viewer's
/// data directory (<c>navigational-warning-classifications.json</c>) across
/// restarts and refreshes.
/// </summary>
/// <remarks>
/// <para>
/// Warnings are keyed as <see cref="NavigationalWarningSeenStore"/> keys
/// them: by series and number, which carries the year ("NAVAREA X|106/26"),
/// else by dataset and feature id. A refresh that brings the same warning
/// again keeps its classification; a cancellation or a new warning that
/// replaces it has its own number, so it starts On chart.
/// </para>
/// <para>
/// Only Off chart and Information are stored; On chart, the default,
/// removes the entry. The classification is global, not per route: S-98
/// says "against the intended route", and a later change may key it by the
/// active route.
/// </para>
/// </remarks>
internal sealed partial class NavigationalWarningClassificationStore
{
    /// <summary>The file format this store reads and writes; other formats are discarded.</summary>
    internal const int FileVersion = 1;

    private static readonly ClassificationJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<NavigationalWarningClassification>(JsonNamingPolicy.CamelCase) },
    });

    private readonly string? _path;
    private readonly bool _readOnly;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private Dictionary<string, Classified>? _warnings;

    /// <summary>Creates a store at <paramref name="path"/>; <see langword="null"/> keeps it in memory.</summary>
    public NavigationalWarningClassificationStore(string? path, bool readOnly = false, ILogger<NavigationalWarningClassificationStore>? logger = null)
    {
        _path = path;
        _readOnly = readOnly;
        _logger = logger;
    }

    /// <summary>The classification of the warning <paramref name="key"/>; <see cref="NavigationalWarningClassification.OnChart"/> unless set.</summary>
    public NavigationalWarningClassification Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
            return Warnings().TryGetValue(key, out var classified) ? classified.Classification : NavigationalWarningClassification.OnChart;
    }

    /// <summary>Sets the classification of the warning <paramref name="key"/> and saves it.</summary>
    /// <returns>True when it changed.</returns>
    public bool Set(string key, NavigationalWarningClassification classification, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            var warnings = Warnings();
            var before = warnings.TryGetValue(key, out var classified) ? classified.Classification : NavigationalWarningClassification.OnChart;
            if (before == classification)
                return false;
            if (classification == NavigationalWarningClassification.OnChart)
                warnings.Remove(key);
            else
                warnings[key] = new Classified { Classification = classification, SetAt = now };
            Save();
        }

        return true;
    }

    private Dictionary<string, Classified> Warnings()
    {
        if (_warnings is not null)
            return _warnings;
        _warnings = new Dictionary<string, Classified>(StringComparer.Ordinal);
        if (_path is null || !File.Exists(_path))
            return _warnings;
        try
        {
            using var stream = File.OpenRead(_path);
            switch (JsonSerializer.Deserialize(stream, Json.StoreFile))
            {
                case { Version: FileVersion, Warnings: { } saved }:
                    foreach (var (key, classified) in saved)
                    {
                        if (classified.Classification != NavigationalWarningClassification.OnChart)
                            _warnings[key] = classified;
                    }
                    break;
                case { } other:
                    _logger?.LogInformation("Discarding the navigational warning classifications in {Path}: format {Version} is not {Expected}", _path, other.Version, FileVersion);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Could not read the navigational warning classifications from {Path}", _path);
        }

        return _warnings;
    }

    private void Save()
    {
        if (_path is null || _readOnly || _warnings is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, new StoreFile { Warnings = _warnings }, Json.StoreFile);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save the navigational warning classifications to {Path}", _path);
        }
    }

    internal sealed class StoreFile
    {
        public int Version { get; set; } = FileVersion;

        public Dictionary<string, Classified> Warnings { get; set; } = [];
    }

    internal sealed class Classified
    {
        public NavigationalWarningClassification Classification { get; set; }

        /// <summary>When the mariner set it.</summary>
        public DateTimeOffset SetAt { get; set; }
    }

    [JsonSerializable(typeof(StoreFile))]
    private sealed partial class ClassificationJsonContext : JsonSerializerContext;
}
