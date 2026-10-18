using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Which navigational warnings are new (#932), per Library source, persisted
/// in the viewer's data directory (<c>navigational-warnings.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rule: a warning is <b>new</b> when it first appeared in the latest
/// refresh of its source, and stays new until its card is opened or a later
/// refresh brings it again unchanged. "First appeared" means this store has
/// not seen the warning before (or has seen it with other content), and it
/// was published after the source's previous refresh, less
/// <see cref="PublishedTolerance"/> for services that list late. The
/// publication check keeps an old warning from reading as new when its
/// dataset loads later, as you pan.
/// </para>
/// <para>
/// A source seen for the first time sets the baseline: nothing it lists is
/// new until it refreshes again. Warnings are keyed by series and number
/// (the number carries the year, e.g. "NAVAREA X 106/26"), else by dataset
/// and feature id; S-124 parts carry no interoperability identifier here.
/// Files opened directly have no source and are never observed.
/// </para>
/// </remarks>
internal sealed partial class NavigationalWarningSeenStore
{
    /// <summary>How much earlier than the previous refresh a newly listed warning may be published and still be new.</summary>
    internal static readonly TimeSpan PublishedTolerance = TimeSpan.FromHours(12);

    /// <summary>How long a warning no source lists is remembered.</summary>
    internal static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(90);

    private static readonly SeenJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private readonly string? _path;
    private readonly bool _readOnly;
    private readonly ILogger? _logger;
    private Dictionary<string, SourceState>? _sources;

    /// <summary>Creates a store at <paramref name="path"/>; <see langword="null"/> keeps it in memory.</summary>
    public NavigationalWarningSeenStore(string? path, bool readOnly = false, ILogger<NavigationalWarningSeenStore>? logger = null)
    {
        _path = path;
        _readOnly = readOnly;
        _logger = logger;
    }

    /// <summary>One listed warning: its key and a stamp of its content.</summary>
    internal readonly record struct Observed(string Key, string Stamp, DateTimeOffset? Published);

    /// <summary>
    /// Records what <paramref name="sourceKey"/> lists now. A later
    /// <paramref name="refreshedAt"/> than last time starts a new refresh:
    /// the previous refresh's new warnings stop being new.
    /// </summary>
    /// <returns>True when which warnings are new changed.</returns>
    public bool Observe(string sourceKey, DateTimeOffset? refreshedAt, IEnumerable<Observed> warnings, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);
        ArgumentNullException.ThrowIfNull(warnings);
        var sources = Sources();
        var dirty = false;
        var changed = false;
        if (!sources.TryGetValue(sourceKey, out var state))
        {
            sources[sourceKey] = state = new SourceState { RefreshedAt = refreshedAt, Baseline = true };
            dirty = true;
        }
        else if (refreshedAt is { } at && (state.RefreshedAt is not { } last || at > last))
        {
            state.PreviousRefreshedAt = state.RefreshedAt;
            state.RefreshedAt = at;
            // The first refresh after the baseline is the first that can bring news.
            state.Baseline = state.PreviousRefreshedAt is null;
            changed |= state.New.Count > 0;
            state.New.Clear();
            foreach (var gone in state.Known.Where(k => now - k.Value.Seen > ForgetAfter).Select(k => k.Key).ToList())
                state.Known.Remove(gone);
            dirty = true;
        }

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        foreach (var warning in warnings)
        {
            if (state.Known.TryGetValue(warning.Key, out var known) && known.Stamp == warning.Stamp)
            {
                if (known.Seen < today)
                {
                    known.Seen = today;
                    dirty = true;
                }

                continue;
            }

            state.Known[warning.Key] = new KnownWarning { Stamp = warning.Stamp, Seen = today };
            dirty = true;
            var publishedSincePrevious = warning.Published is not { } published
                || state.PreviousRefreshedAt is not { } previous
                || published >= previous - PublishedTolerance;
            if (!state.Baseline && publishedSincePrevious && state.New.Add(warning.Key))
                changed = true;
        }

        if (dirty)
            Save();
        return changed;
    }

    /// <summary>True when the warning <paramref name="key"/> from <paramref name="sourceKey"/> is new.</summary>
    public bool IsNew(string sourceKey, string key) =>
        Sources().TryGetValue(sourceKey, out var state) && state.New.Contains(key);

    /// <summary>Marks a warning seen (its card was opened): it is no longer new.</summary>
    /// <returns>True when it was new.</returns>
    public bool MarkSeen(string sourceKey, string key)
    {
        if (!Sources().TryGetValue(sourceKey, out var state) || !state.New.Remove(key))
            return false;
        Save();
        return true;
    }

    /// <summary>The store key of a warning: series and number, else dataset and feature.</summary>
    internal static string KeyOf(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var w = entry.Warning;
        return w.Number is { Length: > 0 } number
            ? $"{w.Series?.Trim()}|{number.Trim()}"
            : $"{entry.DatasetId}#{entry.FeatureRef}";
    }

    /// <summary>A stamp of a warning's content: its publication time and messages, across its parts.</summary>
    internal static string StampOf(IEnumerable<NavigationalWarningInfo> parts)
    {
        var text = new StringBuilder();
        foreach (var part in parts.OrderBy(p => p.PublicationTime).ThenBy(p => p.Title, StringComparer.Ordinal))
        {
            text.Append(part.PublicationTime?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Append('\u001f');
            foreach (var t in part.Texts)
                text.Append(t.Language).Append('\u001f').Append(t.Text).Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private Dictionary<string, SourceState> Sources()
    {
        if (_sources is not null)
            return _sources;
        _sources = new Dictionary<string, SourceState>(StringComparer.Ordinal);
        if (_path is null || !File.Exists(_path))
            return _sources;
        try
        {
            using var stream = File.OpenRead(_path);
            if (JsonSerializer.Deserialize(stream, Json.StoreFile) is { Sources: { } saved })
            {
                foreach (var (key, state) in saved)
                    _sources[key] = state;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Could not read the seen navigational warnings from {Path}", _path);
        }

        return _sources;
    }

    private void Save()
    {
        if (_path is null || _readOnly || _sources is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, new StoreFile { Sources = _sources }, Json.StoreFile);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save the seen navigational warnings to {Path}", _path);
        }
    }

    internal sealed class StoreFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, SourceState> Sources { get; set; } = [];
    }

    internal sealed class SourceState
    {
        public DateTimeOffset? RefreshedAt { get; set; }

        public DateTimeOffset? PreviousRefreshedAt { get; set; }

        /// <summary>True until the source's first refresh after it was first seen.</summary>
        public bool Baseline { get; set; }

        public Dictionary<string, KnownWarning> Known { get; set; } = new(StringComparer.Ordinal);

        public HashSet<string> New { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed class KnownWarning
    {
        public string Stamp { get; set; } = string.Empty;

        public DateTimeOffset Seen { get; set; }
    }

    [JsonSerializable(typeof(StoreFile))]
    private sealed partial class SeenJsonContext : JsonSerializerContext;
}
