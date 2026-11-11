namespace EncDotNet.S100.Viewer;

/// <summary>
/// Single source of truth for every on-disk location the viewer writes to:
/// the persisted settings file, the crash-marker directory, and the disk
/// caches (pattern-clip, portrayal-instruction, dataset-metadata sidecar,
/// S-57 catalogue descriptor cache, and warm tile cache).
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

    /// <summary>
    /// Re-roots only the render caches (portrayal instructions with the
    /// pre-warm ledger, pattern clips and tiles), like <c>--cache-dir</c>.
    /// </summary>
    internal const string CacheDirEnvironmentVariable = "S100_CACHE_DIR";

    private static readonly string DefaultSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EncDotNet.S100.Viewer");

    private static readonly string DefaultLocalDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EncDotNet.S100");

    private readonly string? _baseDirectory;
    private readonly string? _settingsFileOverride;
    private readonly string? _renderCacheDirectory;

    /// <summary>
    /// Creates a path set. <paramref name="baseDirectory"/> re-roots every
    /// location; <see langword="null"/> or whitespace keeps the legacy
    /// per-user defaults. <paramref name="settingsFileOverride"/> pins just
    /// the settings file (the <c>--settings</c> flag) and leaves caches
    /// under the base directory.
    /// </summary>
    /// <param name="baseDirectory">Re-roots every location.</param>
    /// <param name="settingsFileOverride">Pins just the settings file.</param>
    /// <param name="renderCacheDirectory">
    /// Re-roots only the render caches: the portrayal-instruction cache (and the
    /// pre-warm ledger in it), the pattern-clip cache and the warm tile cache.
    /// Settings, the Library and the other caches stay where they are, so a
    /// performance run can start from empty render caches without touching the
    /// user's own (the <c>--cache-dir</c> option).
    /// </param>
    public ViewerDataPaths(string? baseDirectory = null, string? settingsFileOverride = null, string? renderCacheDirectory = null)
    {
        _renderCacheDirectory = string.IsNullOrWhiteSpace(renderCacheDirectory)
            ? null
            : Path.GetFullPath(renderCacheDirectory);
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

    /// <summary>
    /// Absolute path of the persisted <c>routes.json</c> file holding the
    /// user's editable routes. Re-rooted under the base directory when one
    /// is in use (so an isolated <c>--data-dir</c> instance keeps its routes
    /// self-contained) and sits alongside <see cref="SettingsFilePath"/>
    /// otherwise. Unlike the settings file it is not affected by the
    /// <c>--settings</c> override, which pins only the settings file.
    /// </summary>
    public string RoutesFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "routes.json")
            : Path.Combine(DefaultSettingsDirectory, "routes.json");

    /// <summary>
    /// Absolute path of the persisted <c>check-settings.json</c> file holding
    /// the mariner's check settings for received routes (draft, squat,
    /// required UKC, departure), kept by route ID and edition (#960). Placed
    /// like <see cref="RoutesFilePath"/>.
    /// </summary>
    public string CheckSettingsFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "check-settings.json")
            : Path.Combine(DefaultSettingsDirectory, "check-settings.json");

    /// <summary>Directory holding unclean-shutdown crash markers.</summary>
    public string CrashMarkersDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "crash-markers")
            : Path.Combine(DefaultSettingsDirectory, "crash-markers");

    /// <summary>
    /// Directory holding the always-on rolling viewer log files, alongside
    /// the crash markers so the next launch can attach a crashed session's
    /// log to the feedback report.
    /// </summary>
    public string LogsDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "logs")
            : Path.Combine(DefaultSettingsDirectory, "logs");

    /// <summary>
    /// The folder of the viewer's content store for derived data that belongs
    /// with the profile: <c>caches</c> under the base directory, or the
    /// per-user local data folder. The dataset-metadata and S-57 catalogue
    /// kinds live here, and the render kinds too unless <c>--cache-dir</c>
    /// moved them (see <see cref="RenderStoreDirectory"/>).
    /// </summary>
    public string CacheStoreDirectory =>
        _baseDirectory is { } b ? Path.Combine(b, "caches") : DefaultLocalDataDirectory;

    /// <summary>
    /// The folder of the viewer's content store for render caches (warm
    /// tiles, portrayal display lists, pattern clips): the <c>--cache-dir</c>
    /// folder when given, else <see cref="CacheStoreDirectory"/>.
    /// </summary>
    public string RenderStoreDirectory => _renderCacheDirectory ?? CacheStoreDirectory;

    /// <summary>Directory for the shared disk pattern-clip cache (a render store kind).</summary>
    public string PatternClipCacheDirectory =>
        Path.Combine(RenderStoreDirectory, "PatternClipCache");

    /// <summary>Directory for the shared disk portrayal-instruction cache (a render store kind).</summary>
    public string PortrayalInstructionCacheDirectory =>
        Path.Combine(RenderStoreDirectory, "PortrayalInstructionCache");

    /// <summary>Directory for the shared cross-session dataset-metadata sidecar cache.</summary>
    public string DatasetMetadataCacheDirectory =>
        Path.Combine(CacheStoreDirectory, "DatasetMetadataCache");

    /// <summary>Directory for the cross-session S-57 exchange-set catalogue descriptor cache.</summary>
    public string S57CatalogCacheDirectory =>
        Path.Combine(CacheStoreDirectory, "S57CatalogCache");

    /// <summary>
    /// Absolute path of the persisted <c>collections.json</c> file holding
    /// the user's dataset collections (references to local folders, exchange
    /// sets and online feeds — never the data itself). Placed like
    /// <see cref="RoutesFilePath"/>. Issue #655.
    /// </summary>
    public string CollectionsFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "collections.json")
            : Path.Combine(DefaultSettingsDirectory, "collections.json");

    /// <summary>
    /// Absolute path of <c>navigational-warnings.json</c>: which navigational
    /// warnings each Library source brought and which are still new (#932).
    /// Placed like <see cref="CollectionsFilePath"/>.
    /// </summary>
    public string NavigationalWarningsFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "navigational-warnings.json")
            : Path.Combine(DefaultSettingsDirectory, "navigational-warnings.json");

    /// <summary>
    /// Path of the file listing the AML sets whose first-open notification
    /// has been shown (#1371). Placed like <see cref="CollectionsFilePath"/>.
    /// </summary>
    public string AmlNoticesFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "aml-notices.txt")
            : Path.Combine(DefaultSettingsDirectory, "aml-notices.txt");

    /// <summary>
    /// Absolute path of <c>navigational-warning-classifications.json</c>:
    /// the mariner's on chart / off chart / information classification of
    /// each navigational warning (#996). Placed like <see cref="CollectionsFilePath"/>.
    /// </summary>
    public string NavigationalWarningClassificationsFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "navigational-warning-classifications.json")
            : Path.Combine(DefaultSettingsDirectory, "navigational-warning-classifications.json");

    /// <summary>
    /// Absolute path of <c>catalogues.json</c>, the online catalogues the
    /// user added by URL (a known-sources document). Placed like
    /// <see cref="CollectionsFilePath"/>. Issue #670.
    /// </summary>
    public string UserCataloguesFilePath =>
        _baseDirectory is { } b
            ? Path.Combine(b, "catalogues.json")
            : Path.Combine(DefaultSettingsDirectory, "catalogues.json");

    /// <summary>
    /// Directory caching each collection source's index
    /// (<c>&lt;sourceId&gt;.index.json.gz</c>). Derived data: deleting it only
    /// forces the sources to be re-indexed.
    /// </summary>
    public string CollectionIndexCacheDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "caches", "CollectionIndexCache")
            : Path.Combine(DefaultLocalDataDirectory, "CollectionIndexCache");

    /// <summary>
    /// Directory caching downloaded online catalogues (for example NOAA's
    /// <c>ENCProdCat.xml</c>), revalidated with conditional requests.
    /// </summary>
    public string CollectionFeedCacheDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "caches", "CollectionFeedCache")
            : Path.Combine(DefaultLocalDataDirectory, "CollectionFeedCache");

    /// <summary>
    /// Directory the viewer downloads online datasets into (e.g. NOAA ENC
    /// cells under <c>noaa-enc/</c>). Not a cache: "clear caches" leaves it,
    /// because downloaded charts are user data. Issue #655.
    /// </summary>
    public string DownloadsDirectory =>
        _baseDirectory is { } b
            ? Path.Combine(b, "downloads")
            : Path.Combine(DefaultLocalDataDirectory, "Downloads");

    /// <summary>Directory for the warm tile cache (a render store kind).</summary>
    public string TileDiskCacheDirectory =>
        Path.Combine(RenderStoreDirectory, "tiles");

    /// <summary>
    /// The folder the render caches were re-rooted under with <c>--cache-dir</c>
    /// or <c>S100_CACHE_DIR</c>, or <see langword="null"/>.
    /// </summary>
    public string? RenderCacheDirectory => _renderCacheDirectory;

    /// <summary>
    /// Every disk-cache directory the viewer may populate this run,
    /// suitable for a "clear caches" sweep. Excludes the settings file and
    /// crash markers.
    /// </summary>
    public IReadOnlyList<string> CacheDirectories =>
    [
        PatternClipCacheDirectory,
        PortrayalInstructionCacheDirectory,
        DatasetMetadataCacheDirectory,
        S57CatalogCacheDirectory,
        CollectionIndexCacheDirectory,
        CollectionFeedCacheDirectory,
        TileDiskCacheDirectory,
    ];

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

        var cacheDir = options?.CacheDir;
        if (string.IsNullOrWhiteSpace(cacheDir))
        {
            cacheDir = Environment.GetEnvironmentVariable(CacheDirEnvironmentVariable);
        }

        return new ViewerDataPaths(baseDir, options?.SettingsPath, cacheDir);
    }
}
