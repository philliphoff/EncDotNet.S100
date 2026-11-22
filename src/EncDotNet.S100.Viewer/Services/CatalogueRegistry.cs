using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The one owner of which feature and portrayal catalogue the viewer uses
/// for each product (design note <c>catalogue-panels.md</c> D7). It holds
/// three sources per product and kind, applies the winner to
/// <see cref="FeatureCatalogueManager"/> and
/// <see cref="PortrayalCatalogueManager"/>, exposes the effective state, and
/// raises <see cref="CataloguesChanged"/> when it changes.
/// </summary>
/// <remarks>
/// <para>
/// Precedence, highest first (D4): a catalogue given on the command line
/// (<see cref="CatalogueOrigin.CommandLine"/>, this session only); a custom
/// catalogue the user chose (<see cref="CatalogueOrigin.Custom"/>, kept in
/// <see cref="ViewerSettings.FeatureCataloguePaths"/> and
/// <see cref="ViewerSettings.PortrayalCataloguePaths"/>, or for this session
/// only); the bundled catalogue (<see cref="CatalogueOrigin.BuiltIn"/>).
/// </para>
/// <para>
/// A source that fails to load is logged under the <c>Catalogues</c> log
/// area (D11) and the next one down is used, usually the built-in: the
/// product's state is then <see cref="CatalogueStatus.Error"/> with the
/// reason. Every change applies live (D6): the managers evict their parsed
/// catalogues and caches, and <see cref="CatalogueRedrawCoordinator"/> redraws
/// the loaded datasets of the product.
/// </para>
/// <para>
/// Keys are normalised product spec names (<c>"S-101"</c>), one catalogue per
/// product and kind (Q1). Methods are called on the UI thread; state reads
/// are safe from any thread.
/// </para>
/// </remarks>
internal sealed class CatalogueRegistry
{
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features;
    private readonly PortrayalCatalogueManager _portrayals;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private readonly Dictionary<(string Spec, CatalogueKind Kind), string> _commandLine = [];
    private readonly Dictionary<(string Spec, CatalogueKind Kind), string> _sessionCustom = [];
    private readonly Dictionary<(string Spec, CatalogueKind Kind), CatalogueState> _states = [];
    private IReadOnlyList<CatalogueProductState>? _products;
    private bool _initialized;

    /// <summary>
    /// Creates the registry and registers every bundled feature catalogue with
    /// <paramref name="features"/>, so feature catalogues are there from the
    /// start. Custom, command-line and portrayal catalogues are applied by
    /// <see cref="Initialize"/>.
    /// </summary>
    public CatalogueRegistry(
        ViewerSettings settings,
        FeatureCatalogueManager features,
        PortrayalCatalogueManager portrayals,
        ILogger<CatalogueRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(portrayals);
        _settings = settings;
        _features = features;
        _portrayals = portrayals;
        _logger = logger ?? (ILogger)NullLogger.Instance;

        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            if (Specifications.Specification.HasFeatureCatalogue(spec))
                _features.SetSource(Ref(spec), Specifications.Specification.CreateFeatureCatalogueSource(spec));
        }
    }

    /// <summary>The feature catalogue manager this registry configures.</summary>
    public FeatureCatalogueManager FeatureCatalogues => _features;

    /// <summary>The portrayal catalogue manager this registry configures.</summary>
    public PortrayalCatalogueManager PortrayalCatalogues => _portrayals;

    /// <summary>
    /// Raised after a product's catalogues changed and were applied to the
    /// managers, with the product in <see cref="CataloguesChangedEventArgs.Spec"/>,
    /// on the thread that made the change. Its state in <see cref="Products"/>
    /// is already the new one. The managers raise their own
    /// <c>CataloguesChanged</c> earlier, while they apply it.
    /// </summary>
    public event EventHandler<CataloguesChangedEventArgs>? CataloguesChanged;

    /// <summary>
    /// Every product the viewer knows: bundled, custom and from the command
    /// line, by spec.
    /// </summary>
    public IReadOnlyList<CatalogueProductState> Products
    {
        get
        {
            lock (_gate)
            {
                return _products ??= BuildProducts();
            }
        }
    }

    /// <summary>The state of <paramref name="spec"/>, or null when the viewer doesn't know the product.</summary>
    public CatalogueProductState? Find(string spec) =>
        SpecName.TryNormalize(spec, out var name)
            ? Products.FirstOrDefault(p => p.Spec == name)
            : null;

    /// <summary>
    /// Applies the command-line catalogues and every product's sources. Called
    /// once at startup; a later call does nothing.
    /// </summary>
    /// <param name="featureCatalogues">Feature catalogue files from <c>--feature-catalogue</c>.</param>
    /// <param name="portrayalCatalogues">Portrayal catalogue folders or packages from <c>--portrayal-catalogue</c>.</param>
    public void Initialize(IEnumerable<string>? featureCatalogues, IEnumerable<string>? portrayalCatalogues)
    {
        List<string> applied;
        lock (_gate)
        {
            if (_initialized)
                return;
            _initialized = true;

            foreach (var path in (featureCatalogues ?? []).Concat(portrayalCatalogues ?? []))
            {
                var detected = CatalogueSpecDetection.Detect(path);
                if (detected.Catalogue is { } catalogue)
                {
                    _commandLine[(catalogue.Spec, catalogue.Kind)] = catalogue.Path;
                }
                else
                {
                    _logger.LogWarning(
                        "Ignored the command-line catalogue {Path}: {Reason}", path, detected.Reason);
                }
            }

            applied = [];
            foreach (var spec in AllSpecs())
            {
                // Bundled feature catalogues are already registered; apply
                // only those something overrides.
                if (HasOverride(spec, CatalogueKind.Feature) || !Specifications.Specification.HasFeatureCatalogue(spec))
                    Apply(spec, CatalogueKind.Feature);
                else
                    _states[(spec, CatalogueKind.Feature)] = BuiltInState(spec, CatalogueKind.Feature);
                Apply(spec, CatalogueKind.Portrayal);
                applied.Add(spec);
            }

            _products = null;
        }

        foreach (var spec in applied)
            Raise(spec);
    }

    /// <summary>
    /// Uses <paramref name="catalogue"/> for its product and kind, replacing
    /// any custom one, and applies it live. A command-line catalogue still
    /// wins over it for this session.
    /// </summary>
    /// <param name="catalogue">A catalogue from <see cref="CatalogueSpecDetection.Detect"/>.</param>
    /// <param name="keepAfterRestart">Whether to keep it in the settings (true) or for this session only.</param>
    /// <returns>The product's new state.</returns>
    public CatalogueProductState Use(DetectedCatalogue catalogue, bool keepAfterRestart = true)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var key = (catalogue.Spec, catalogue.Kind);
        lock (_gate)
        {
            var persisted = PersistedPaths(catalogue.Kind);
            RemovePersisted(persisted, catalogue.Spec);
            _sessionCustom.Remove(key);
            if (keepAfterRestart)
                persisted[catalogue.Spec] = catalogue.Path;
            else
                _sessionCustom[key] = catalogue.Path;
            _settings.Save();
            Apply(catalogue.Spec, catalogue.Kind);
            _products = null;
        }

        Raise(catalogue.Spec);
        return Find(catalogue.Spec)!;
    }

    /// <summary>
    /// Drops the custom catalogue of <paramref name="kind"/> for
    /// <paramref name="spec"/>, so the built-in one is used again (or, for a
    /// product that isn't bundled, none), and applies it live. A command-line
    /// catalogue can't be reverted; restart without the flag.
    /// </summary>
    /// <returns>True when there was a custom catalogue to drop.</returns>
    public bool Revert(string spec, CatalogueKind kind)
    {
        if (!SpecName.TryNormalize(spec, out var name))
            return false;
        lock (_gate)
        {
            var removed = _sessionCustom.Remove((name, kind)) | RemovePersisted(PersistedPaths(kind), name);
            if (!removed)
                return false;
            _settings.Save();
            Apply(name, kind);
            _products = null;
        }

        Raise(name);
        return true;
    }

    /// <summary>
    /// Reads the catalogue of <paramref name="kind"/> for
    /// <paramref name="spec"/> from disk again and applies it live, for a
    /// developer editing a draft (D11).
    /// </summary>
    public void Reload(string spec, CatalogueKind kind)
    {
        if (!SpecName.TryNormalize(spec, out var name))
            return;
        lock (_gate)
        {
            Apply(name, kind);
            _products = null;
        }

        Raise(name);
    }

    private void Raise(string spec) =>
        CataloguesChanged?.Invoke(this, new CataloguesChangedEventArgs(Ref(spec)));

    // ── Applying ────────────────────────────────────────────────────────

    // Tries each source, highest first, and registers the first that loads.
    // Must hold _gate.
    private void Apply(string spec, CatalogueKind kind)
    {
        var sources = Sources(spec, kind);
        var tried = new List<CatalogueSourceInfo>(sources.Count);
        CatalogueSourceInfo? drawing = null;
        string? error = null;
        for (var i = 0; i < sources.Count && drawing is null; i++)
        {
            var source = sources[i];
            var metadata = source.Metadata;
            var failure = source.Origin == CatalogueOrigin.BuiltIn
                ? ApplyBuiltIn(spec, kind)
                : ApplyFile(spec, kind, source.Path!, out metadata);
            source = source with { Metadata = metadata };
            tried.Add(source);
            if (failure is null)
            {
                drawing = source;
                break;
            }

            error ??= failure;
            _logger.LogWarning(
                "Could not load the {Kind} catalogue for {Spec} from {Path}: {Reason}. Using {Fallback}",
                KindName(kind), spec, source.Path ?? "the built-in catalogue", failure,
                i + 1 < sources.Count ? OriginName(sources[i + 1].Origin) : "none");
        }

        if (drawing is null)
        {
            if (kind == CatalogueKind.Feature)
                _features.RemoveSource(Ref(spec));
            else
                _portrayals.Remove(Ref(spec));
        }

        var configured = tried.FirstOrDefault();
        var overrides = tried.Count > 1 ? tried[1]
            : sources.Count > 1 ? sources[1] with { Metadata = ReadMetadata(spec, kind, sources[1]) }
            : null;
        var status = configured is null
            ? kind == CatalogueKind.Portrayal && !NeedsPortrayalCatalogue(spec) ? CatalogueStatus.NotRequired : CatalogueStatus.Missing
            : error is null ? CatalogueStatus.InUse : CatalogueStatus.Error;
        _states[(spec, kind)] = new CatalogueState(
            kind,
            status,
            configured,
            drawing,
            overrides,
            error,
            configured?.Origin == CatalogueOrigin.Custom && !_sessionCustom.ContainsKey((spec, kind)));
    }

    private string? ApplyBuiltIn(string spec, CatalogueKind kind)
    {
        try
        {
            if (kind == CatalogueKind.Feature)
                _features.SetSource(Ref(spec), Specifications.Specification.CreateFeatureCatalogueSource(spec));
            else
                _portrayals.SetSource(Ref(spec), Specifications.Specification.CreatePortrayalCatalogueSource(spec));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string? ApplyFile(string spec, CatalogueKind kind, string path, out CatalogueMetadata? metadata)
    {
        metadata = null;
        var detected = CatalogueSpecDetection.Detect(path);
        var catalogue = detected.Catalogue;
        if (catalogue is null)
            return detected.Reason;
        if (catalogue.Kind != kind || catalogue.Spec != spec)
            return string.Format(Strings.Catalogues_Error_Mismatch, KindName(kind), spec, KindName(catalogue.Kind), catalogue.Spec);

        metadata = catalogue.Metadata;
        var source = catalogue.OpenSource();
        try
        {
            if (kind == CatalogueKind.Feature)
                _features.SetSource(Ref(spec), source);
            else
                _portrayals.SetSource(Ref(spec), source);
            return null;
        }
        catch (Exception ex)
        {
            // The portrayal manager opens the catalogue eagerly; a broken one
            // throws here, before it owns the source.
            source.Dispose();
            return ex.Message;
        }
    }

    // ── Sources and state ───────────────────────────────────────────────

    private List<CatalogueSourceInfo> Sources(string spec, CatalogueKind kind)
    {
        var sources = new List<CatalogueSourceInfo>(3);
        if (_commandLine.TryGetValue((spec, kind), out var cli))
            sources.Add(new CatalogueSourceInfo(CatalogueOrigin.CommandLine, cli, null));
        if (CustomPath(spec, kind) is { } custom)
            sources.Add(new CatalogueSourceInfo(CatalogueOrigin.Custom, custom, null));
        if (IsBundled(spec, kind))
            sources.Add(new CatalogueSourceInfo(CatalogueOrigin.BuiltIn, null, BuiltInMetadata(spec, kind)));
        return sources;
    }

    private string? CustomPath(string spec, CatalogueKind kind)
    {
        if (_sessionCustom.TryGetValue((spec, kind), out var session))
            return session;
        foreach (var (key, path) in PersistedPaths(kind))
        {
            if (Normalized(key) == spec && !string.IsNullOrWhiteSpace(path))
                return path;
        }

        return null;
    }

    private bool HasOverride(string spec, CatalogueKind kind) =>
        _commandLine.ContainsKey((spec, kind)) || CustomPath(spec, kind) is not null;

    private CatalogueState BuiltInState(string spec, CatalogueKind kind)
    {
        var builtIn = new CatalogueSourceInfo(CatalogueOrigin.BuiltIn, null, BuiltInMetadata(spec, kind));
        return new CatalogueState(kind, CatalogueStatus.InUse, builtIn, builtIn, null, null, false);
    }

    // Before Initialize, or for a product nothing has applied yet, the state
    // follows from the sources without loading anything.
    private CatalogueState StateOf(string spec, CatalogueKind kind)
    {
        if (_states.TryGetValue((spec, kind), out var state))
            return state;
        if (IsBundled(spec, kind) && (kind == CatalogueKind.Feature || !_initialized))
            return BuiltInState(spec, kind);
        var status = kind == CatalogueKind.Portrayal && !NeedsPortrayalCatalogue(spec)
            ? CatalogueStatus.NotRequired
            : CatalogueStatus.Missing;
        return new CatalogueState(kind, status, null, null, null, null, false);
    }

    private IReadOnlyList<CatalogueProductState> BuildProducts() =>
        [.. AllSpecs().Select(spec =>
        {
            var feature = StateOf(spec, CatalogueKind.Feature);
            var portrayal = StateOf(spec, CatalogueKind.Portrayal);
            var name = feature.Source?.Metadata?.Name ?? BuiltInMetadata(spec, CatalogueKind.Feature)?.Name;
            return new CatalogueProductState(spec, name, IsBundledProduct(spec), feature, portrayal);
        })];

    private IEnumerable<string> AllSpecs()
    {
        var specs = new SortedSet<string>(Specifications.Specification.AvailableSpecs, SpecComparer.Instance);
        foreach (var (spec, _) in _commandLine.Keys)
            specs.Add(spec);
        foreach (var (spec, _) in _sessionCustom.Keys)
            specs.Add(spec);
        foreach (var key in _settings.FeatureCataloguePaths.Keys.Concat(_settings.PortrayalCataloguePaths.Keys))
        {
            if (Normalized(key) is { } spec)
                specs.Add(spec);
        }

        return specs;
    }

    private CatalogueMetadata? ReadMetadata(string spec, CatalogueKind kind, CatalogueSourceInfo source) =>
        source.Origin == CatalogueOrigin.BuiltIn
            ? BuiltInMetadata(spec, kind)
            : source.Path is { } path ? CatalogueSpecDetection.Detect(path).Catalogue?.Metadata : null;

    private static CatalogueMetadata? BuiltInMetadata(string spec, CatalogueKind kind) =>
        kind == CatalogueKind.Feature
            ? CatalogueSpecDetection.ReadBuiltInFeatureCatalogue(spec)
            : CatalogueSpecDetection.ReadBuiltInPortrayalCatalogue(spec);

    private static bool IsBundledProduct(string spec) =>
        Specifications.Specification.AvailableSpecs.Contains(spec, StringComparer.OrdinalIgnoreCase);

    private static bool IsBundled(string spec, CatalogueKind kind) =>
        IsBundledProduct(spec) && (kind == CatalogueKind.Feature
            ? Specifications.Specification.HasFeatureCatalogue(spec)
            : Specifications.Specification.HasPortrayalCatalogue(spec));

    // A product drawn by an application portrayal (S-104, issue #937) needs
    // no portrayal catalogue.
    private static bool NeedsPortrayalCatalogue(string spec) =>
        ProductRegistry.Default.PortrayalOf(spec) is PortrayalBasis.CatalogueBasis;

    private Dictionary<string, string> PersistedPaths(CatalogueKind kind) =>
        kind == CatalogueKind.Feature ? _settings.FeatureCataloguePaths : _settings.PortrayalCataloguePaths;

    private static bool RemovePersisted(Dictionary<string, string> paths, string spec)
    {
        var keys = paths.Keys.Where(k => Normalized(k) == spec).ToList();
        foreach (var key in keys)
            paths.Remove(key);
        return keys.Count > 0;
    }

    private static string? Normalized(string key) =>
        SpecName.TryNormalize(key, out var spec) && !SpecName.IsNonS100Format(spec) ? spec : null;

    private static SpecRef Ref(string spec) => new(spec, default);

    private static string KindName(CatalogueKind kind) =>
        kind == CatalogueKind.Feature ? Strings.Catalogues_Kind_Feature : Strings.Catalogues_Kind_Portrayal;

    private static string OriginName(CatalogueOrigin origin) => origin switch
    {
        CatalogueOrigin.BuiltIn => "the built-in catalogue",
        CatalogueOrigin.Custom => "the custom catalogue",
        _ => "the command-line catalogue",
    };

    /// <summary>Orders "S-57" before "S-101" before "S-1xx" by number.</summary>
    private sealed class SpecComparer : IComparer<string>
    {
        public static readonly SpecComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var nx = Number(x);
            var ny = Number(y);
            return nx != ny ? nx.CompareTo(ny) : string.CompareOrdinal(x, y);
        }

        private static int Number(string? spec) =>
            spec is { Length: > 2 } && int.TryParse(spec.AsSpan(2), out var n) ? n : int.MaxValue;
    }
}

/// <summary>Where a catalogue comes from (D4).</summary>
internal enum CatalogueOrigin
{
    /// <summary>Bundled with the viewer.</summary>
    BuiltIn,

    /// <summary>Chosen by the user; kept after restart or for this session.</summary>
    Custom,

    /// <summary>Given on the command line; this session only, not revertable.</summary>
    CommandLine,
}

/// <summary>Whether a product's catalogue of one kind is in effect (D4).</summary>
internal enum CatalogueStatus
{
    /// <summary>The configured catalogue loaded and is drawing.</summary>
    InUse,

    /// <summary>
    /// The configured catalogue failed to load; <see cref="CatalogueState.Drawing"/>
    /// is the one used instead (usually the built-in), or null when there's none.
    /// </summary>
    Error,

    /// <summary>There's no catalogue; datasets of the product won't open.</summary>
    Missing,

    /// <summary>The product is drawn by the viewer itself and needs no portrayal catalogue (S-104).</summary>
    NotRequired,
}

/// <summary>One source of a catalogue.</summary>
/// <param name="Origin">Built-in, custom or command line.</param>
/// <param name="Path">The file, folder or package; null for built-in.</param>
/// <param name="Metadata">What the catalogue says about itself; null when unreadable.</param>
internal sealed record CatalogueSourceInfo(CatalogueOrigin Origin, string? Path, CatalogueMetadata? Metadata);

/// <summary>A product's catalogue of one kind, as in effect.</summary>
/// <param name="Kind">Feature or portrayal.</param>
/// <param name="Status">In use, error, missing or not required.</param>
/// <param name="Source">The highest-precedence source configured, whether or not it loaded; null when none.</param>
/// <param name="Drawing">The source actually in use; differs from <paramref name="Source"/> after an error; null when none.</param>
/// <param name="Overrides">The next source down that <paramref name="Source"/> overrides, e.g. the built-in; null when none.</param>
/// <param name="Error">Why <paramref name="Source"/> failed to load, for the user; null unless <see cref="CatalogueStatus.Error"/>.</param>
/// <param name="KeptAfterRestart">True when <paramref name="Source"/> is custom and saved in the settings.</param>
internal sealed record CatalogueState(
    CatalogueKind Kind,
    CatalogueStatus Status,
    CatalogueSourceInfo? Source,
    CatalogueSourceInfo? Drawing,
    CatalogueSourceInfo? Overrides,
    string? Error,
    bool KeptAfterRestart)
{
    /// <summary>The origin of <see cref="Source"/>; null when there is none.</summary>
    public CatalogueOrigin? Origin => Source?.Origin;

    /// <summary>True for <see cref="CatalogueStatus.Error"/> and <see cref="CatalogueStatus.Missing"/>.</summary>
    public bool HasProblem => Status is CatalogueStatus.Error or CatalogueStatus.Missing;
}

/// <summary>A product's two catalogues, as in effect (D2, D4).</summary>
/// <param name="Spec">The product spec, e.g. <c>"S-101"</c>.</param>
/// <param name="DeclaredName">The feature catalogue's declared name; null when none.</param>
/// <param name="IsBuiltInProduct">False for a product the viewer doesn't bundle (D12).</param>
/// <param name="Feature">The feature catalogue.</param>
/// <param name="Portrayal">The portrayal catalogue.</param>
internal sealed record CatalogueProductState(
    string Spec,
    string? DeclaredName,
    bool IsBuiltInProduct,
    CatalogueState Feature,
    CatalogueState Portrayal)
{
    /// <summary>The catalogue of <paramref name="kind"/>.</summary>
    public CatalogueState this[CatalogueKind kind] => kind == CatalogueKind.Feature ? Feature : Portrayal;

    /// <summary>True when either catalogue is in error or missing.</summary>
    public bool HasProblem => Feature.HasProblem || Portrayal.HasProblem;

    /// <summary>True when anything isn't the default: a custom or command-line catalogue, or a product that isn't bundled.</summary>
    public bool IsChanged =>
        !IsBuiltInProduct
        || Feature.Origin is CatalogueOrigin.Custom or CatalogueOrigin.CommandLine
        || Portrayal.Origin is CatalogueOrigin.Custom or CatalogueOrigin.CommandLine;
}
