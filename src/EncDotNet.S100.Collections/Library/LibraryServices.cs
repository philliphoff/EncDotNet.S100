using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Collections.Library;

/// <summary>Where a Library keeps its state on disk.</summary>
/// <param name="Root">The data directory everything else defaults under.</param>
public sealed record LibraryDataPaths(string Root)
{
    /// <summary>The persisted collections (<c>collections.json</c>).</summary>
    public string CollectionsFile { get; init; } = Path.Combine(Root, "collections.json");

    /// <summary>The per-source index cache.</summary>
    public string IndexCacheDirectory { get; init; } = Path.Combine(Root, "CollectionIndexCache");

    /// <summary>The online catalogues' cache.</summary>
    public string FeedCacheDirectory { get; init; } = Path.Combine(Root, "CollectionFeedCache");

    /// <summary>Downloaded items, one managed folder per provider.</summary>
    public string DownloadsDirectory { get; init; } = Path.Combine(Root, "Downloads");
}

/// <summary>
/// A Library and everything it reads and downloads with, for a host without
/// the viewer's dependency injection (#792; <c>s100 mcp serve</c>): the
/// indexers of every source kind over one feed cache, the Library, its
/// downloads and sync, the catalogue readers that add sources, and the
/// catalogue-URL probe.
/// </summary>
/// <remarks>
/// SECOM requests trust the built-in MCP anchors and check revocation; no
/// client identity is set until one is given to <see cref="SecomTrust"/>.
/// </remarks>
public sealed class LibraryServices : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    private LibraryServices(LibraryDataPaths paths, DatasetProbe? probe, LibraryServicesOptions options)
    {
        Paths = paths;
        var feedCache = paths.FeedCacheDirectory;
        var time = options.TimeProvider;

        Revocation = new SecomRevocation(Client(TimeSpan.FromMinutes(2)), feedCache, time);
        SecomTrust = new SecomServerTrust(timeProvider: time, revocation: Revocation);

        var noaa = new NoaaEncFeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, timeProvider: time);
        var usace = new UsaceIencFeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, timeProvider: time);
        var community = new ChartCatalogsFeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, paths.DownloadsDirectory, probe, timeProvider: time);
        var s100Feeds = new S100FeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, timeProvider: time);
        var s100Catalogues = new S100CatalogueFeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, timeProvider: time);
        var forecasts = new S100ForecastFeedIndexer(Client(TimeSpan.FromMinutes(2)), feedCache, timeProvider: time);
        var secom = new SecomSourceIndexer(SecomClient(TimeSpan.FromMinutes(2)), feedCache, paths.DownloadsDirectory, probe, timeProvider: time)
        {
            Revocation = Revocation,
            Signer = SecomTrust.CreateSigner,
        };

        Indexer = CollectionIndexer.CreateDefault(probe, [noaa, usace, community, s100Feeds, s100Catalogues, forecasts, secom]);
        Library = new CollectionLibrary(
            Indexer,
            new CollectionLibraryOptions(paths.CollectionsFile, paths.IndexCacheDirectory) { ReadOnly = options.ReadOnly },
            options.LoggerFactory?.CreateLogger<CollectionLibrary>());
        Downloads = new LibraryDownloads(LibraryDownloads.ManagedFolders(
            Client(TimeSpan.FromMinutes(10)), paths.DownloadsDirectory, SecomClient(TimeSpan.FromMinutes(10)),
            Revocation, SecomTrust.CreateSigner, () => SecomTrust.Anchors));
        Sync = new LibrarySync(Library, Downloads, new LibrarySyncOptions { IsInUse = options.IsInUse }, logger: options.LoggerFactory?.CreateLogger<LibrarySync>(), timeProvider: time);

        Readers = new LibraryCatalogueReaders
        {
            NoaaEnc = (uri, ct) => noaa.GetCatalogAsync(uri, cancellationToken: ct),
            UsaceIenc = (uri, ct) => usace.GetCatalogAsync(uri, cancellationToken: ct),
            CommunityList = (uri, ct) => community.GetCatalogAsync(uri, cancellationToken: ct),
            S100Feed = (uri, ct) => s100Feeds.GetFeedAsync(uri, cancellationToken: ct),
            S100Catalogue = (uri, ct) => s100Catalogues.GetCatalogueAsync(uri, cancellationToken: ct),
            ListS100Folders = (catalogue, folders, ct) => s100Catalogues.ListAsync(catalogue, folders, ct),
            ForecastModels = (uri, models, ct) => forecasts.GetModelsAsync(uri, models, ct),
            Secom = (uri, area, ct) => secom.DescribeAsync(uri, area, ct),
        };

        var probeHttp = Client(TimeSpan.FromSeconds(30));
        var probeSecom = SecomClient(TimeSpan.FromSeconds(30));
        ProbeCatalogue = (uri, ct) => CatalogueFormatDetector.ProbeAsync(probeHttp, uri, probeSecom, ct);
        SecomRegistry = new SecomRegistry(SecomClient(TimeSpan.FromSeconds(20)), feedCache, timeProvider: time, serverTrust: SecomTrust);
    }

    /// <summary>Creates the services over <paramref name="paths"/>; call <see cref="CollectionLibrary.Initialize"/> on <see cref="Library"/> to read the collections.</summary>
    /// <param name="paths">Where the Library keeps its state.</param>
    /// <param name="probe">Reads a local dataset's metadata (bounds, edition) while indexing; files are listed without it.</param>
    /// <param name="options">The clock, logging and whether changes are saved; defaults when null.</param>
    public static LibraryServices Create(LibraryDataPaths paths, DatasetProbe? probe = null, LibraryServicesOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new LibraryServices(paths, probe, options ?? new LibraryServicesOptions());
    }

    /// <summary>Where the Library keeps its state.</summary>
    public LibraryDataPaths Paths { get; }

    /// <summary>Indexes every kind of source.</summary>
    public CollectionIndexer Indexer { get; }

    /// <summary>The Library.</summary>
    public CollectionLibrary Library { get; }

    /// <summary>Downloads the Library's online items.</summary>
    public LibraryDownloads Downloads { get; }

    /// <summary>Keeps synced sources downloaded.</summary>
    public LibrarySync Sync { get; }

    /// <summary>Reads online catalogues when a source is added.</summary>
    public LibraryCatalogueReaders Readers { get; }

    /// <summary>Recognises a catalogue URL's format, for adding by URL.</summary>
    public Func<Uri, CancellationToken, Task<CatalogueProbe>> ProbeCatalogue { get; }

    /// <summary>Which SECOM servers are trusted, and the client identity used with them.</summary>
    public SecomServerTrust SecomTrust { get; }

    /// <summary>Revocation of MCP-issued certificates.</summary>
    public SecomRevocation Revocation { get; }

    /// <summary>The MCP service registry, for finding SECOM services.</summary>
    public SecomRegistry SecomRegistry { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Sync.Dispose();
        Library.Dispose();
        foreach (var client in _clients)
            client.Dispose();
    }

    private HttpClient Client(TimeSpan timeout) => Track(new HttpClient { Timeout = timeout });

    private HttpClient SecomClient(TimeSpan timeout) => Track(new HttpClient(SecomTrust.CreateHandler()) { Timeout = timeout });

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }
}

/// <summary>How <see cref="LibraryServices"/> runs.</summary>
public sealed record LibraryServicesOptions
{
    /// <summary>The clock; the system clock by default.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Logs the Library and its sync; nothing is logged when null.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>True for a synced copy (a file or folder) that is open, so the sync does not prune it; none is when null.</summary>
    public Func<string, bool>? IsInUse { get; init; }

    /// <summary>True to leave the collections file unchanged (changes last for the run).</summary>
    public bool ReadOnly { get; init; }
}
