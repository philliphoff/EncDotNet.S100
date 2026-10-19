using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>Why a navigational warning is, or isn't, marked NEW (#997).</summary>
internal enum NavigationalWarningNewReason
{
    /// <summary>Received less than 24 hours ago (or marked new since), and its card hasn't been opened.</summary>
    New,

    /// <summary>Its card was opened after it was received.</summary>
    Opened,

    /// <summary>24 hours passed since it was received (or marked new) without its card being opened.</summary>
    Expired,

    /// <summary>It was listed when its source was first seen, which sets the baseline.</summary>
    Baseline,

    /// <summary>It was published before the source's previous refresh (less the tolerance): its dataset just loaded late.</summary>
    PublishedEarlier,

    /// <summary>The store hasn't observed it.</summary>
    NotObserved,

    /// <summary>It came from a file opened directly, which is never new.</summary>
    NoSource,

    /// <summary>New, but received longer ago than the "received within" filter allows.</summary>
    FilteredAge,

    /// <summary>New, but further from own ship and the active route than the distance filter allows.</summary>
    FilteredDistance,
}

/// <summary>Whether a warning is NEW, why, and the times that decide it (#997).</summary>
/// <param name="Reason">Why it is or isn't new.</param>
/// <param name="ReceivedAt">When this store first saw the warning with its current content; <c>null</c> when not observed.</param>
/// <param name="NewUntil">When NEW ends unless the card is opened first; <c>null</c> when it never was new.</param>
/// <param name="OpenedAt">When its card was opened while it was new.</param>
/// <param name="MarkedAt">When "Mark warnings since … as new" last made it new again.</param>
internal readonly record struct NavigationalWarningNewness(
    NavigationalWarningNewReason Reason,
    DateTimeOffset? ReceivedAt = null,
    DateTimeOffset? NewUntil = null,
    DateTimeOffset? OpenedAt = null,
    DateTimeOffset? MarkedAt = null)
{
    /// <summary>Whether the NEW indication shows.</summary>
    public bool IsNew => Reason == NavigationalWarningNewReason.New;

    /// <summary>Not new and not observed.</summary>
    public static NavigationalWarningNewness NotObserved { get; } = new(NavigationalWarningNewReason.NotObserved);
}

/// <summary>
/// Which navigational warnings are new (#932, #997), per Library source,
/// persisted in the viewer's data directory (<c>navigational-warnings.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rule follows S-98 Ed 2.0.0 §12.12.1 item 3: a newly received warning
/// is marked NEW until it has been displayed or 24 hours (<see cref="NewFor"/>)
/// have passed since it was received, whichever comes first. "Displayed"
/// means its Warning card was opened (from the panel, the map or MCP);
/// a warning drawn on the map that the chart is panned past doesn't count,
/// as that is too weak to say it was read. A later refresh doesn't clear it.
/// </para>
/// <para>
/// "Received" is the first time this store observes the warning with its
/// current content in its source's latest refresh, and it was published
/// after the source's previous refresh, less <see cref="PublishedTolerance"/>
/// for services that list late. The publication check keeps an old warning
/// from reading as new when its dataset loads later, as you pan. Each
/// warning records its receive time, so NEW survives restarts and expires
/// while the viewer is closed.
/// </para>
/// <para>
/// A source seen for the first time sets the baseline: nothing it lists is
/// new until it refreshes again. Warnings are keyed by series and number
/// (the number carries the year, e.g. "NAVAREA X 106/26"), else by dataset
/// and feature id; S-124 parts carry no interoperability identifier here.
/// Files opened directly have no source and are never observed.
/// </para>
/// <para>
/// File format 2 (#997) replaces format 1 (#983): a format 1 file is
/// discarded on load, so each source sets a new baseline and nothing reads
/// as new until it refreshes again.
/// </para>
/// </remarks>
internal sealed partial class NavigationalWarningSeenStore : IDisposable
{
    /// <summary>How long a newly received warning stays NEW unless its card is opened (S-98 §12.12.1 item 3).</summary>
    internal static readonly TimeSpan NewFor = TimeSpan.FromHours(24);

    /// <summary>How much earlier than the previous refresh a newly listed warning may be published and still be new.</summary>
    internal static readonly TimeSpan PublishedTolerance = TimeSpan.FromHours(12);

    /// <summary>How long a warning no source lists is remembered.</summary>
    internal static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(90);

    /// <summary>The file format this store reads and writes; other formats are discarded.</summary>
    internal const int FileVersion = 2;

    private static readonly SeenJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<WarningArrival>(JsonNamingPolicy.CamelCase) },
    });

    private readonly string? _path;
    private readonly bool _readOnly;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private readonly DebouncedSettingsSaver _saver;
    private readonly ConditionalWeakTable<NavigationalWarningInfo, string> _stamps = [];
    private Dictionary<string, SourceState>? _sources;

    /// <summary>Creates a store at <paramref name="path"/>; <see langword="null"/> keeps it in memory.</summary>
    /// <remarks>
    /// Writes are debounced: datasets load one by one, and each load is
    /// observed. <see cref="Flush"/> (or disposing) writes what is pending.
    /// </remarks>
    public NavigationalWarningSeenStore(string? path, bool readOnly = false, ILogger<NavigationalWarningSeenStore>? logger = null)
    {
        _path = path;
        _readOnly = readOnly;
        _logger = logger;
        _saver = new DebouncedSettingsSaver(Save, delayMilliseconds: 2000);
    }

    /// <summary>Writes pending changes now.</summary>
    public void Flush() => _saver.Flush();

    /// <inheritdoc />
    public void Dispose()
    {
        _saver.Flush();
        _saver.Dispose();
    }

    /// <summary>
    /// <see cref="StampOf"/> of one part, cached per part: the same loaded part
    /// is observed on every rebuild.
    /// </summary>
    internal string StampOfPart(NavigationalWarningInfo part) =>
        _stamps.GetValue(part, static p => StampOf([p]));

    /// <summary>One listed warning: its key and a stamp of its content.</summary>
    internal readonly record struct Observed(string Key, string Stamp, DateTimeOffset? Published);

    /// <summary>
    /// Records what <paramref name="sourceKey"/> lists now. A later
    /// <paramref name="refreshedAt"/> than last time starts a new refresh,
    /// whose newly listed warnings are received at <paramref name="now"/>.
    /// </summary>
    /// <returns>True when a warning became new.</returns>
    public bool Observe(string sourceKey, DateTimeOffset? refreshedAt, IEnumerable<Observed> warnings, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);
        ArgumentNullException.ThrowIfNull(warnings);
        lock (_gate)
            return ObserveCore(sourceKey, refreshedAt, warnings, now);
    }

    private bool ObserveCore(string sourceKey, DateTimeOffset? refreshedAt, IEnumerable<Observed> warnings, DateTimeOffset now)
    {
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
            foreach (var gone in state.Known.Where(k => now - k.Value.Listed > ForgetAfter).Select(k => k.Key).ToList())
                state.Known.Remove(gone);
            dirty = true;
        }

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        foreach (var warning in warnings)
        {
            if (state.Known.TryGetValue(warning.Key, out var known) && known.Stamp == warning.Stamp)
            {
                if (known.Listed < today)
                {
                    known.Listed = today;
                    dirty = true;
                }

                continue;
            }

            var publishedSincePrevious = warning.Published is not { } published
                || state.PreviousRefreshedAt is not { } previous
                || published >= previous - PublishedTolerance;
            var arrival = state.Baseline ? WarningArrival.Baseline
                : publishedSincePrevious ? WarningArrival.New
                : WarningArrival.PublishedEarlier;
            state.Known[warning.Key] = new KnownWarning
            {
                Stamp = warning.Stamp,
                Listed = today,
                Received = now,
                Arrival = arrival,
                NewUntil = arrival == WarningArrival.New ? now + NewFor : null,
            };
            dirty = true;
            changed |= arrival == WarningArrival.New;
        }

        if (dirty)
            _saver.RequestSave();
        return changed;
    }

    /// <summary>True when the warning <paramref name="key"/> from <paramref name="sourceKey"/> is new at <paramref name="now"/>.</summary>
    public bool IsNew(string sourceKey, string key, DateTimeOffset now) => Newness(sourceKey, key, now).IsNew;

    /// <summary>Whether the warning <paramref name="key"/> from <paramref name="sourceKey"/> is new at <paramref name="now"/>, and why.</summary>
    public NavigationalWarningNewness Newness(string sourceKey, string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!Sources().TryGetValue(sourceKey, out var state) || !state.Known.TryGetValue(key, out var known))
                return NavigationalWarningNewness.NotObserved;
            var reason = known switch
            {
                { OpenedAt: not null } => NavigationalWarningNewReason.Opened,
                { NewUntil: { } until } when now < until => NavigationalWarningNewReason.New,
                { NewUntil: not null } => NavigationalWarningNewReason.Expired,
                { Arrival: WarningArrival.Baseline } => NavigationalWarningNewReason.Baseline,
                _ => NavigationalWarningNewReason.PublishedEarlier,
            };
            return new NavigationalWarningNewness(reason, known.Received, known.NewUntil, known.OpenedAt, known.MarkedAt);
        }
    }

    /// <summary>Marks a warning displayed (its card was opened): it is no longer new.</summary>
    /// <returns>True when it was new.</returns>
    public bool MarkSeen(string sourceKey, string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!Sources().TryGetValue(sourceKey, out var state)
                || !state.Known.TryGetValue(key, out var known)
                || known.OpenedAt is not null
                || known.NewUntil is not { } until
                || now >= until)
            {
                return false;
            }

            known.OpenedAt = now;
        }

        _saver.RequestSave();
        return true;
    }

    /// <summary>
    /// Marks every warning received at or after <paramref name="since"/> new
    /// again, for a watch handover (2019 IHO S-124 workshop): each stays new
    /// until its card is opened or <see cref="NewFor"/> after
    /// <paramref name="now"/>. The baseline and late-loading warnings count
    /// too; what matters is when this store received them.
    /// </summary>
    /// <returns>How many warnings were marked.</returns>
    public int MarkNewSince(DateTimeOffset since, DateTimeOffset now)
    {
        var count = 0;
        lock (_gate)
        {
            foreach (var known in Sources().Values.SelectMany(s => s.Known.Values).Where(k => k.Received >= since))
            {
                known.NewUntil = now + NewFor;
                known.OpenedAt = null;
                known.MarkedAt = now;
                count++;
            }
        }

        if (count > 0)
            _saver.RequestSave();
        return count;
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
            switch (JsonSerializer.Deserialize(stream, Json.StoreFile))
            {
                case { Version: FileVersion, Sources: { } saved }:
                    foreach (var (key, state) in saved)
                        _sources[key] = state;
                    break;
                case { } other:
                    // Format 1 (#983) kept no receive times: start over, so each source sets a new baseline.
                    _logger?.LogInformation("Discarding the seen navigational warnings in {Path}: format {Version} is not {Expected}", _path, other.Version, FileVersion);
                    break;
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
        if (_path is null || _readOnly)
            return;
        lock (_gate)
            SaveCore();
    }

    private void SaveCore()
    {
        if (_sources is null || _path is null)
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
        public int Version { get; set; } = FileVersion;

        public Dictionary<string, SourceState> Sources { get; set; } = [];
    }

    internal sealed class SourceState
    {
        public DateTimeOffset? RefreshedAt { get; set; }

        public DateTimeOffset? PreviousRefreshedAt { get; set; }

        /// <summary>True until the source's first refresh after it was first seen.</summary>
        public bool Baseline { get; set; }

        public Dictionary<string, KnownWarning> Known { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>How a warning arrived, which decides whether it was ever new.</summary>
    internal enum WarningArrival
    {
        /// <summary>A refresh brought it: new for <see cref="NewFor"/>.</summary>
        New,

        /// <summary>Listed when its source was first seen.</summary>
        Baseline,

        /// <summary>Published before the previous refresh; its dataset loaded late.</summary>
        PublishedEarlier,
    }

    internal sealed class KnownWarning
    {
        public string Stamp { get; set; } = string.Empty;

        /// <summary>The day it was last listed, to forget it <see cref="ForgetAfter"/> later.</summary>
        public DateTimeOffset Listed { get; set; }

        /// <summary>When it was first observed with this content.</summary>
        public DateTimeOffset Received { get; set; }

        public WarningArrival Arrival { get; set; }

        /// <summary>When NEW ends unless its card is opened; <c>null</c> when it was never new.</summary>
        public DateTimeOffset? NewUntil { get; set; }

        /// <summary>When its card was opened while it was new.</summary>
        public DateTimeOffset? OpenedAt { get; set; }

        /// <summary>When "Mark warnings since … as new" last made it new.</summary>
        public DateTimeOffset? MarkedAt { get; set; }
    }

    [JsonSerializable(typeof(StoreFile))]
    private sealed partial class SeenJsonContext : JsonSerializerContext;
}
