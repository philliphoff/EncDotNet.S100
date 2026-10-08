using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Library;

/// <summary>How a Library node's status line is marked (the colour of its dot).</summary>
public enum LibraryNodeStatusKind
{
    /// <summary>No status line.</summary>
    None,

    /// <summary>Work in progress: indexing, downloading.</summary>
    Busy,

    /// <summary>Something needs attention but still works (problems, an unreachable feed with a cached index).</summary>
    Warning,

    /// <summary>Not working (failed, access denied).</summary>
    Error,

    /// <summary>Healthy, worth saying (a reachable shared feed).</summary>
    Ok,

    /// <summary>A hint (the session collection).</summary>
    Info,
}

/// <summary>How many of a remote catalogue's datasets are on disk, and how many of those have a newer edition online.</summary>
/// <param name="Total">The datasets the source lists.</param>
/// <param name="Local">Those with a downloaded copy.</param>
/// <param name="Outdated">Those whose copy is an older edition than the catalogue's.</param>
public sealed record LibraryCatalogueCounts(int Total, int Local, int Outdated);

/// <summary>A forecast source's models (#685): how many have a run on disk, a newer run listed, or an ended run.</summary>
/// <param name="Models">The models the source lists.</param>
/// <param name="Local">Models with a downloaded run.</param>
/// <param name="NewerRun">Models whose downloaded run has a newer one listed.</param>
/// <param name="Expired">Models whose downloaded run has ended, with nothing newer listed.</param>
/// <param name="Left">The least time left among the current downloaded runs, if any.</param>
public sealed record LibraryForecastCounts(int Models, int Local, int NewerRun, int Expired, TimeSpan? Left);

/// <summary>
/// What a Library node (a collection, one of its sources, or a manifest group)
/// knows when its status line is worked out; see <see cref="LibraryNodeText.Status"/>.
/// </summary>
/// <param name="Collection">The collection the node is, or belongs to.</param>
/// <param name="Sources">The node's sources: its own, or the collection's.</param>
public sealed record LibraryNodeStatusInput(LibraryCollection Collection, IReadOnlyList<LibrarySource> Sources)
{
    /// <summary>The manifest group the node is, if it is one.</summary>
    public SourceIndexGroup? Group { get; init; }

    /// <summary>"Downloading 2 of 5 · 4,3 MB left" while a download runs under the node.</summary>
    public string? Downloading { get; init; }

    /// <summary>How a shared feed's or catalogue's server last answered.</summary>
    public Func<CollectionSource, FeedHealth?>? Health { get; init; }

    /// <summary>A synced source's last sync (one-source nodes only).</summary>
    public LibrarySyncStatus? Sync { get; init; }

    /// <summary>A remote S-100 catalogue's local and update counts.</summary>
    public LibraryCatalogueCounts? CatalogueCounts { get; init; }

    /// <summary>A forecast source's model counts.</summary>
    public LibraryForecastCounts? ForecastCounts { get; init; }

    /// <summary>Whether run times are shown in local time or UTC (#730).</summary>
    public LibraryTimeFormat TimeFormat { get; init; } = LibraryTimeFormat.Utc;
}

/// <summary>
/// The names, kind tags and status lines of Library collections and sources,
/// in the words the viewer's Library panel uses, for every host (#792).
/// </summary>
public static partial class LibraryNodeText
{
    private static readonly HashSet<string> LegacyGenericNames =
        new(["All ENCs", "All rivers", "All downloads", "All products"], StringComparer.Ordinal);

    /// <summary>
    /// A short mono tag naming the kind of source: <c>DIR</c>, <c>ZIP</c>,
    /// <c>WEB</c> (an online catalogue), <c>AWS</c> (a catalogue on AWS Open
    /// Data), <c>LIST</c> (a community list), <c>FEED</c> (a shared feed),
    /// <c>SECOM</c>, <c>JSON</c> (a collection manifest) or <c>S-128</c>.
    /// </summary>
    /// <param name="source">The source.</param>
    public static string KindOf(CollectionSource source) => source switch
    {
        ExchangeSetSource { Path: var p } when p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => "ZIP",
        NoaaEncFeedSource or UsaceIencFeedSource => "WEB",
        S100CatalogueFeedSource { CatalogUri.Host: var host }
            when host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) => "AWS",
        S100CatalogueFeedSource => "WEB",
        S100ForecastFeedSource { ModelsUri.Host: var forecastHost }
            when forecastHost.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) => "AWS",
        S100ForecastFeedSource => "WEB",
        ChartCatalogsFeedSource => "LIST",
        S100FeedSource => "FEED",
        SecomSource => "SECOM",
        S128CatalogueSource => "S-128",
        LocalManifestSource => "JSON",
        null => throw new ArgumentNullException(nameof(source)),
        _ => "DIR",
    };

    /// <summary>A collection's kind tag: its sources' kind, or <c>S-128</c> for the session collection.</summary>
    /// <param name="collection">The collection.</param>
    public static string KindOf(LibraryCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return collection.IsSession ? "S-128" : collection.Sources.Select(x => KindOf(x.Definition)).FirstOrDefault() ?? "DIR";
    }

    /// <summary>
    /// The source's name: the user's (or the dialog's) display name, unless it
    /// only repeats something generic — a legacy "All …" name, the collection's
    /// name or the catalogue's — in which case it is derived: a community list
    /// holding a single package is named by that package's description.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="collection">The collection it belongs to.</param>
    public static string SourceName(LibrarySource source, LibraryCollection collection)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(collection);
        var definition = source.Definition;
        var catalogue = CatalogueUri(definition) is { } uri
            ? KnownCatalogueSources.All.FirstOrDefault(k => k.CatalogUri == uri)?.Name
            : null;
        var generic = definition.DisplayName is { } name
            && CatalogueUri(definition) is not null
            && (LegacyGenericNames.Contains(name) || name == collection.Definition.Name || name == catalogue);
        if (definition.DisplayName is { } display && !generic)
            return display;

        if (definition is ChartCatalogsFeedSource && SinglePackageTitle(source.Index) is { } package)
            return package;
        return catalogue ?? Describe(definition with { DisplayName = null });
    }

    /// <summary>A catalogue's URL, as the directory lists it (a shared feed is not one).</summary>
    private static Uri? CatalogueUri(CollectionSource source) => source is S100FeedSource ? null : SourceUrl(source);

    /// <summary>The online source's URL, or null for a local source.</summary>
    /// <param name="source">The source.</param>
    public static Uri? SourceUrl(CollectionSource source) => source switch
    {
        NoaaEncFeedSource n => n.CatalogUri,
        UsaceIencFeedSource u => u.CatalogUri,
        ChartCatalogsFeedSource c => c.CatalogUri,
        S100FeedSource f => f.FeedUri,
        S100CatalogueFeedSource r => r.CatalogUri,
        S100ForecastFeedSource f => f.ModelsUri,
        SecomSource s => s.ServiceUri,
        _ => null,
    };

    /// <summary>Where the source is: its path or URL (a shared feed's token masked).</summary>
    /// <param name="source">The source.</param>
    public static string SourceLocation(CollectionSource source) => source switch
    {
        LocalFolderSource f => f.Path,
        ExchangeSetSource e => e.Path,
        S128CatalogueSource c => c.Path,
        LocalManifestSource m => m.Path,
        S100FeedSource f => LibraryTextFormat.MaskToken(f.FeedUri),
        _ => SourceUrl(source)?.AbsoluteUri ?? string.Empty,
    };

    /// <summary>A source's name from its definition alone: its display name, else its file or host.</summary>
    private static string Describe(CollectionSource source) => source.DisplayName ?? source switch
    {
        LocalFolderSource f => LeafName(f.Path),
        ExchangeSetSource e => LeafName(e.Path),
        S128CatalogueSource c => LeafName(c.Path),
        LocalManifestSource m => ManifestName(m.Path),
        NoaaEncFeedSource => LibraryText.Get("Library_NoaaFeed"),
        UsaceIencFeedSource => LibraryText.Get("Library_UsaceFeed"),
        ChartCatalogsFeedSource c => c.CatalogUri.Host,
        S100FeedSource f => f.FeedUri.Host,
        S100CatalogueFeedSource r => r.CatalogUri.Host,
        S100ForecastFeedSource f => f.ModelsUri.Host,
        SecomSource s => s.ServiceUri.Host,
        _ => source.GetType().Name,
    };

    /// <summary>The one package a community list's index holds, by description, or null.</summary>
    private static string? SinglePackageTitle(SourceIndex? index)
    {
        if (index is null)
            return null;
        var titles = index.Items
            .Select(i => i.Properties.GetValueOrDefault("packageTitle") ?? (i.Properties.ContainsKey("package") ? i.Title : null))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return titles.Length == 1 ? LibraryTextFormat.PackageTitle(titles[0]) : null;
    }

    /// <summary>A manifest's file name without <c>.s100collection.json</c> (or <c>.json</c>).</summary>
    private static string ManifestName(string path)
    {
        var name = LeafName(path);
        return name.EndsWith(CollectionManifest.FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^CollectionManifest.FileSuffix.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    /// A remote S-100 catalogue's local and update counts, from the copies on disk.
    /// </summary>
    /// <param name="index">The catalogue's index.</param>
    /// <param name="copies">The downloaded copies.</param>
    public static LibraryCatalogueCounts CatalogueCounts(SourceIndex index, ILibraryLocalCopies copies)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(copies);
        int local = 0, outdated = 0;
        foreach (var item in index.Items)
        {
            if (copies.Localize(item).Location is not LocalItemLocation)
                continue;
            local++;
            if (copies.IsOutdated(item))
                outdated++;
        }

        return new LibraryCatalogueCounts(index.Items.Count, local, outdated);
    }

    /// <summary>
    /// Per model of a forecast source: whether a run is on disk, whether a
    /// newer run is listed, whether the downloaded run has ended, and the least
    /// time left among the downloaded runs.
    /// </summary>
    /// <param name="index">The forecast source's index.</param>
    /// <param name="copies">The downloaded copies.</param>
    /// <param name="now">The current time.</param>
    public static LibraryForecastCounts ForecastCounts(SourceIndex index, ILibraryLocalCopies copies, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(copies);
        int models = 0, local = 0, newer = 0, expired = 0;
        TimeSpan? left = null;
        foreach (var model in index.Items.GroupBy(i => ForecastRuns.ModelOf(i) ?? i.Name, StringComparer.Ordinal))
        {
            models++;
            var downloaded = model.FirstOrDefault(i => copies.LocalPublishedAtOf(i) is not null);
            if (downloaded is null)
                continue;

            local++;
            if (model.Any(copies.IsOutdated))
            {
                newer++;
                continue;
            }

            var end = copies.LocalPublishedAtOf(downloaded) + ForecastRuns.Horizon(downloaded);
            if (end is { } e && e <= now)
                expired++;
            else if (end is { } e2 && (left is null || e2 - now < left))
                left = e2 - now;
        }

        return new LibraryForecastCounts(models, local, newer, expired, left);
    }

    /// <summary>"Downloading 2 of 5 · 4,3 MB left".</summary>
    /// <param name="current">The download in progress (1-based).</param>
    /// <param name="total">The downloads under the node.</param>
    /// <param name="bytesLeft">The bytes still to download.</param>
    public static string Downloading(int current, int total, long bytesLeft) =>
        string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_StatusLine_DownloadingFormat"),
            current, total, LibraryTextFormat.Bytes(bytesLeft));

    /// <summary>
    /// A second line for a node, only when something is off-normal: indexing,
    /// downloading, problems, a shared feed's reachability, a catalogue's or
    /// forecast's bookkeeping, a sync, or the session's hint. A null line keeps
    /// the node to one line.
    /// </summary>
    /// <param name="input">What the node knows.</param>
    public static (string? Line, LibraryNodeStatusKind Kind) Status(LibraryNodeStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.CurrentCulture;
        var sources = input.Sources;
        if (input.Downloading is { } downloading)
            return (downloading, LibraryNodeStatusKind.Busy);
        if (sources.Any(x => x.State == LibrarySourceState.Indexing))
            return (LibraryText.Get("Library_Status_Indexing"), LibraryNodeStatusKind.Busy);
        if (input.Group is { MissingPathCount: > 0 })
            return (LibraryText.Get("Library_StatusLine_PathNotFound"), LibraryNodeStatusKind.Warning);
        if (input.Group is not null)
            return (null, LibraryNodeStatusKind.None);
        if (sources.FirstOrDefault(x => x.State == LibrarySourceState.Failed && x.Index is null) is { } failed)
            return (string.Format(c, LibraryText.Get("Library_StatusLine_FailedFormat"), failed.Error ?? string.Empty), LibraryNodeStatusKind.Error);

        // A shared feed says whether its server is reachable (for a source, or a one-source collection).
        if (sources is [{ Definition: S100FeedSource feed }] && input.Health?.Invoke(feed) is { } health)
            return FeedStatus(feed, health, c);

        var problems = sources.Sum(x => x.Index?.Diagnostics.Count(d => d.Severity >= IndexDiagnosticSeverity.Warning) ?? 0);
        // A sync that needs attention says so first; otherwise a forecast feed or
        // remote catalogue keeps its own line, and other synced sources show the sync.
        var sync = sources is [_] ? input.Sync : null;
        if (sync is { } attention && (attention.NeededBytes is not null || (attention.Failed > 0 && problems == 0)))
            return SyncStatus(attention, c);
        if (sources is [{ Definition: S100ForecastFeedSource forecast, Index: { } runs }] && problems == 0)
            return ForecastStatus(runs, input.Health?.Invoke(forecast), input.ForecastCounts, c, input.TimeFormat);
        if (sources is [{ Definition: S100CatalogueFeedSource catalogue, Index: { } catalogueIndex }] && problems == 0)
            return CatalogueStatus(catalogueIndex, input.Health?.Invoke(catalogue), input.CatalogueCounts, c);
        if (sync is { } synced && problems == 0)
            return SyncStatus(synced, c);
        if (problems > 0)
            return (string.Format(c, LibraryText.Get("Library_StatusLine_ProblemsFormat"), problems), LibraryNodeStatusKind.Warning);
        if (input.Collection.IsSession)
            return (LibraryText.Get("Library_StatusLine_Session"), LibraryNodeStatusKind.Info);
        return (null, LibraryNodeStatusKind.None);
    }

    /// <summary>"Synced 1,732 of 1,732 · 14:05", or why a synced source is not fully local.</summary>
    private static (string, LibraryNodeStatusKind) SyncStatus(LibrarySyncStatus sync, CultureInfo c)
    {
        if (sync.NeededBytes is { } needed)
            return (string.Format(c, LibraryText.Get("Library_StatusLine_SyncTooLargeFormat"), LibraryTextFormat.Bytes(needed)), LibraryNodeStatusKind.Warning);
        if (sync.Failed > 0)
            return (string.Format(c, LibraryText.Get("Library_StatusLine_SyncFailedFormat"), sync.Local, sync.Listed, sync.Failed), LibraryNodeStatusKind.Warning);
        return (string.Format(c, LibraryText.Get("Library_StatusLine_SyncedFormat"), sync.Local, sync.Listed, LibraryTextFormat.When(sync.SyncedAt.ToLocalTime())),
            sync.Local == sync.Listed ? LibraryNodeStatusKind.Ok : LibraryNodeStatusKind.Info);
    }

    private static (string, LibraryNodeStatusKind) FeedStatus(S100FeedSource feed, FeedHealth health, CultureInfo c)
    {
        if (health.IsReachable)
            return (string.Format(c, LibraryText.Get("Library_StatusLine_ReachableFormat"), feed.FeedUri.Authority), LibraryNodeStatusKind.Ok);
        if (AccessDenied().IsMatch(health.Failure!))
            return (LibraryText.Get("Library_StatusLine_AccessDenied"), LibraryNodeStatusKind.Error);
        if (health.CopyFetchedAt is { } copied)
        {
            var since = (health.FailingSince ?? health.CheckedAt).ToLocalTime();
            return (string.Format(c, LibraryText.Get("Library_StatusLine_UnreachableFormat"), since, LibraryTextFormat.Age(health.CheckedAt - copied)),
                LibraryNodeStatusKind.Warning);
        }

        return (string.Format(c, LibraryText.Get("Library_StatusLine_UnreachableNoCopyFormat"), health.Failure), LibraryNodeStatusKind.Error);
    }

    [GeneratedRegex(@"\b(401|403|404)\b")]
    private static partial Regex AccessDenied();

    /// <summary>
    /// A remote S-100 catalogue's bookkeeping: "Catalogue 30.09.2026 · 140
    /// updates · not for navigation" (amber), "… · 152 of 307 local …" (green)
    /// or "… · nothing local …" (grey); or "Offline · catalogue cached
    /// 30.09.2026 · 152 local" while its server cannot be reached.
    /// </summary>
    private static (string, LibraryNodeStatusKind) CatalogueStatus(
        SourceIndex index, FeedHealth? health, LibraryCatalogueCounts? counts, CultureInfo c)
    {
        var notForNavigation = index.Items.Count > 0
            && index.Items.All(i => i.Properties.GetValueOrDefault("notForNavigation") == "true");
        if (health is { IsReachable: false, CopyFetchedAt: { } cached })
        {
            var offline = string.Format(c, LibraryText.Get("Library_StatusLine_CatalogueOfflineFormat"), LibraryTextFormat.When(cached.ToLocalTime()));
            if (counts is { Local: > 0 })
                offline += " · " + string.Format(c, LibraryText.Get("Library_StatusLine_LocalFormat"), counts.Local);
            return (offline, LibraryNodeStatusKind.Info);
        }

        var dated = (index.PublishedAt ?? index.IndexedAt).ToLocalTime();
        var parts = new List<string>(3) { string.Format(c, LibraryText.Get("Library_StatusLine_CatalogueFormat"), dated.ToString("d", c)) };
        var kind = LibraryNodeStatusKind.Info;
        switch (counts)
        {
            case { Outdated: > 0 }:
                parts.Add(string.Format(c, LibraryText.Get("Library_StatusLine_UpdatesFormat"), counts.Outdated));
                kind = LibraryNodeStatusKind.Warning;
                break;
            case { Local: 0 }:
                parts.Add(LibraryText.Get("Library_StatusLine_NothingLocal"));
                break;
            case { } some:
                parts.Add(some.Local == some.Total
                    ? string.Format(c, LibraryText.Get("Library_StatusLine_AllLocalFormat"), some.Total)
                    : string.Format(c, LibraryText.Get("Library_StatusLine_SomeLocalFormat"), some.Local, some.Total));
                kind = LibraryNodeStatusKind.Ok;
                break;
        }

        if (notForNavigation)
            parts.Add(LibraryText.Get("Library_StatusLine_NotForNavigation"));
        return (string.Join(" · ", parts), kind);
    }

    /// <summary>
    /// A forecast source's bookkeeping: "Checked 21:04 · newer run for 1 model"
    /// (amber), "Checked 28.09 · 2 runs expired · Refresh to look for new runs"
    /// (red), "Checked 21:04 · 1 of 2 runs local · 45 h left" (green), "Latest
    /// runs 30.09 18:00Z · nothing local" (grey), or offline.
    /// </summary>
    private static (string, LibraryNodeStatusKind) ForecastStatus(
        SourceIndex index, FeedHealth? health, LibraryForecastCounts? counts, CultureInfo c, LibraryTimeFormat format)
    {
        if (health is { IsReachable: false, CopyFetchedAt: { } cached })
        {
            var offline = string.Format(c, LibraryText.Get("Library_StatusLine_ForecastOfflineFormat"), LibraryTextFormat.When(cached.ToLocalTime()));
            if (counts is { Local: > 0 })
                offline += " · " + string.Format(c, LibraryText.Get("Library_StatusLine_RunsOnDiskFormat"), counts.Local);
            return (offline, LibraryNodeStatusKind.Info);
        }

        var checkedText = string.Format(c, LibraryText.Get("Library_StatusLine_CheckedFormat"),
            LibraryTextFormat.When((health?.CheckedAt ?? index.IndexedAt).ToLocalTime()));
        switch (counts)
        {
            case { NewerRun: > 0 }:
                var newer = string.Format(c, LibraryText.Get(counts.NewerRun == 1 ? "Library_StatusLine_NewerRunOne" : "Library_StatusLine_NewerRunFormat"), counts.NewerRun);
                return ($"{checkedText} · {newer}", LibraryNodeStatusKind.Warning);
            case { Expired: > 0 }:
                return ($"{checkedText} · {string.Format(c, LibraryText.Get("Library_StatusLine_RunsExpiredFormat"), counts.Expired)} · {LibraryText.Get("Library_StatusLine_RefreshForRuns")}",
                    LibraryNodeStatusKind.Error);
            case { Local: > 0 } some:
                var line = $"{checkedText} · {string.Format(c, LibraryText.Get("Library_StatusLine_RunsLocalFormat"), some.Local, some.Models)}";
                if (some.Left is { } left)
                    line += " · " + LibraryTextFormat.TimeLeft(left);
                return (line, LibraryNodeStatusKind.Ok);
            default:
                var latest = index.PublishedAt is { } run
                    ? string.Format(c, LibraryText.Get("Library_StatusLine_LatestRunsFormat"), LibraryTextFormat.Run(run, format, TimeZoneInfo.Local))
                    : checkedText;
                return ($"{latest} · {LibraryText.Get("Library_StatusLine_NothingLocal")}", LibraryNodeStatusKind.Info);
        }
    }
}
