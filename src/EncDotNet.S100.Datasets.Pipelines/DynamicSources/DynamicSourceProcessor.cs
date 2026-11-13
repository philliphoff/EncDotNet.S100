using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using VectorGeometryType = EncDotNet.S100.Pipelines.Vector.GeometryType;

namespace EncDotNet.S100.Datasets.Pipelines.DynamicSources;

/// <summary>
/// An <see cref="IDatasetProcessor"/> over an <see cref="IDynamicFeatureSource"/>
/// and its <see cref="IDynamicFeaturePortrayal"/>, so a live source gets
/// everything a dataset gets: S-98 plane ordering, headless rendering, picks
/// and the pick contributors.
/// </summary>
/// <remarks>
/// <para>
/// Every call reads the source's current snapshot
/// (<see cref="IDynamicFeatureSource.CurrentFeatures"/>), and the source's
/// <see cref="IDynamicFeatureSource.Changed"/> is raised again as
/// <see cref="Changed"/>, so the map session draws the dataset again when the
/// source updates (<see cref="ILiveDatasetProcessor"/>).
/// </para>
/// <para>
/// The portrayal draws the features on one sub-layer, on the source's
/// <see cref="DynamicSourceMetadata.Plane"/> at its
/// <see cref="DynamicSourceMetadata.WithinPlanePriority"/>. A pick describes
/// a feature through the source's <see cref="IDynamicFeatureDescriber"/> when
/// it has one, and otherwise lists its motion and attributes.
/// </para>
/// <para>
/// Unless it's created with <c>ownsSource</c>, the processor doesn't own the
/// source: disposing it unsubscribes from the source and leaves the source
/// running. A processor built from a <see cref="SourceRegistration"/> owns
/// its source, and disposing it stops the source.
/// </para>
/// <para>
/// As an <see cref="IHostedDatasetProcessor"/>, the processor passes
/// <see cref="Start"/>, <see cref="IsActive"/> and <see cref="UpdateArea"/>
/// on to a source that implements <see cref="IStartableDynamicSource"/>,
/// <see cref="IPausableDynamicSource"/> or
/// <see cref="IAreaFilteredDynamicSource"/>, and ignores them otherwise.
/// </para>
/// </remarks>
public sealed class DynamicSourceProcessor :
    IDatasetProcessor,
    IVectorPortrayalSource,
    ILiveDatasetProcessor,
    IHostedDatasetProcessor,
    IDisposable
{
    private readonly IDynamicFeatureSource _source;
    private readonly IDynamicFeaturePortrayal _portrayal;
    private readonly IFormatProvider? _formatProvider;
    private readonly bool _ownsSource;
    private readonly IReadOnlyList<IDisposable> _ownedResources;
    private bool _isActive = true;
    private bool _disposed;

    /// <summary>Creates a processor over <paramref name="source"/>.</summary>
    /// <param name="source">The live source.</param>
    /// <param name="portrayal">The portrayal that draws the source's features.</param>
    /// <param name="spec">
    /// The product the source reports as, such as <c>NDBC</c>; a name for a
    /// source that isn't an S-100 product.
    /// </param>
    /// <param name="formatProvider">The culture the source's describer formats values with; the current culture when <see langword="null"/>.</param>
    /// <param name="ownsSource">
    /// Whether disposing the processor disposes the source too, as it does
    /// for a processor built from a <see cref="SourceRegistration"/>.
    /// </param>
    /// <param name="ownedResources">
    /// Other objects the processor disposes after the source, such as the
    /// <see cref="HttpClient"/> the source fetches with.
    /// </param>
    public DynamicSourceProcessor(
        IDynamicFeatureSource source,
        IDynamicFeaturePortrayal portrayal,
        SpecRef spec,
        IFormatProvider? formatProvider = null,
        bool ownsSource = false,
        IReadOnlyList<IDisposable>? ownedResources = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(portrayal);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        _source = source;
        _portrayal = portrayal;
        _formatProvider = formatProvider;
        _ownsSource = ownsSource;
        _ownedResources = ownedResources ?? [];
        Spec = spec;
        _source.Changed += OnSourceChanged;
    }

    /// <summary>The source the processor reads.</summary>
    public IDynamicFeatureSource Source => _source;

    /// <inheritdoc />
    public SpecRef Spec { get; }

    /// <inheritdoc />
    public DatasetMetadata Metadata => new()
    {
        Spec = Spec,
        Extent = Extent(_source.CurrentFeatures) is { } bounds
            ? new BoundingBox(bounds.MinLatitude, bounds.MinLongitude, bounds.MaxLatitude, bounds.MaxLongitude)
            : null,
        HorizontalCrsEpsg = 4326,
    };

    /// <inheritdoc />
    public event EventHandler<DatasetChangedEventArgs>? Changed;

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);
        foreach (var feature in _source.CurrentFeatures)
        {
            if (string.Equals(feature.Id, featureRef, StringComparison.Ordinal))
                return Describe(feature);
        }
        return null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The source's snapshot can change between a hit test and the pick that
    /// follows it, so an ordinal isn't stable: this returns
    /// <see langword="null"/>, and hosts fall back to
    /// <see cref="GetFeatureInfo"/> with the hit's feature reference.
    /// </remarks>
    public FeatureInfo? GetFeatureInfoAt(int ordinal) => null;

    /// <inheritdoc />
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(double latitude, double longitude, double radiusMeters)
    {
        var features = _source.CurrentFeatures.Select(f => new FeatureView(f, FeatureTypeOf(f))).ToList();
        return FeatureHitTester.HitTest(features, latitude, longitude, radiusMeters);
    }

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        var features = _source.CurrentFeatures;
        for (var i = 0; i < features.Count; i++)
        {
            yield return new FeatureSummary
            {
                FeatureRef = features[i].Id,
                Ordinal = i,
                FeatureType = FeatureTypeOf(features[i]),
            };
        }
    }

    /// <inheritdoc />
    public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var features = _source.CurrentFeatures;
        var instructions = new List<DrawingInstruction>();
        foreach (var feature in features)
        {
            if (_portrayal.CanPortray(feature))
                instructions.AddRange(_portrayal.Portray(feature));
        }

        var metadata = _source.Metadata;
        return Task.FromResult(new VectorPortrayalResult
        {
            SubLayers =
            [
                new VectorSubLayer
                {
                    LayerKey = _source.Id,
                    LayerName = metadata.DisplayName,
                    Instructions = instructions,
                    Plane = metadata.Plane,
                    WithinPlanePriority = metadata.WithinPlanePriority,
                    SourceFeatureType = _source.Id,
                },
            ],
            Palette = _portrayal.Palette.GetColorPalette(context?.Palette ?? PaletteType.Day),
            GeometryProvider = new DynamicFeatureGeometryProvider(features),
            Product = Spec.Name,
            Spec = Spec,
            SourceDatasetId = _source.Id,
            Info = string.Create(
                CultureInfo.InvariantCulture,
                $"{metadata.DisplayName}: {features.Count} live features."),
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = _portrayal.SymbolProvider,
            LayerNames = [_source.Id],
            GeographicExtent = Extent(features),
            PreferLiveDrawing = true,
        });
    }

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is IStartableDynamicSource startable)
            startable.Start();
    }

    /// <inheritdoc />
    /// <remarks>Defaults to <see langword="true"/>. A source that can't pause keeps working.</remarks>
    public bool IsActive
    {
        get => _source is IPausableDynamicSource pausable ? pausable.IsActive : _isActive;
        set
        {
            _isActive = value;
            if (_source is IPausableDynamicSource pausable)
                pausable.IsActive = value;
        }
    }

    /// <inheritdoc />
    public void UpdateArea(GeographicBounds? area)
    {
        if (_source is not IAreaFilteredDynamicSource filtered)
            return;
        filtered.UpdateArea(area is { } a
            ? new BoundingBox(
                southLatitude: a.MinLatitude,
                westLongitude: a.MinLongitude,
                northLatitude: a.MaxLatitude,
                eastLongitude: a.MaxLongitude)
            : null);
    }

    /// <summary>
    /// Stops listening to the source. A source the processor owns is disposed
    /// in the background; use <see cref="DisposeAsync"/> to wait for it.
    /// </summary>
    public void Dispose()
    {
        if (!Unsubscribe())
            return;
        if (_ownsSource && _source is IAsyncDisposable asyncDisposable and not IDisposable)
        {
            _ = DisposeOwnedInBackgroundAsync(asyncDisposable);
            return;
        }
        DisposeOwnedSync();
    }

    /// <summary>Stops listening to the source, and disposes it when the processor owns it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!Unsubscribe())
            return;
        if (_ownsSource)
        {
            if (_source is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (_source is IDisposable disposable)
                disposable.Dispose();
        }
        foreach (var resource in _ownedResources)
            resource.Dispose();
    }

    private bool Unsubscribe()
    {
        if (_disposed)
            return false;
        _disposed = true;
        _source.Changed -= OnSourceChanged;
        return true;
    }

    private void DisposeOwnedSync()
    {
        if (_ownsSource && _source is IDisposable disposable)
            disposable.Dispose();
        foreach (var resource in _ownedResources)
            resource.Dispose();
    }

    private async Task DisposeOwnedInBackgroundAsync(IAsyncDisposable source)
    {
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already gone.
        }
        finally
        {
            foreach (var resource in _ownedResources)
                resource.Dispose();
        }
    }

    private void OnSourceChanged(object? sender, DynamicFeaturesChanged e) =>
        Changed?.Invoke(this, e.Kind == DynamicSourceChangeKind.Reset || e.ChangedIds.Count == 0
            ? DatasetChangedEventArgs.Empty
            : new DatasetChangedEventArgs { FeatureRefs = e.ChangedIds });

    private FeatureInfo Describe(DynamicFeature feature)
    {
        var view = new FeatureView(feature, FeatureTypeOf(feature));
        if (_source is IDynamicFeatureDescriber describer
            && describer.Describe(feature, _formatProvider ?? CultureInfo.CurrentCulture) is { } description)
        {
            return new FeatureInfo
            {
                FeatureRef = feature.Id,
                FeatureType = FeatureTypeOf(feature),
                FeatureTypeName = description.Title,
                Attributes = [.. description.Properties.Select((p, i) => new PickAttribute
                {
                    Code = string.Create(CultureInfo.InvariantCulture, $"property{i}"),
                    Name = p.Label,
                    RawValue = p.Value,
                })],
                Geometry = view.GeometryType,
            };
        }

        var rows = new List<PickAttribute>();
        if (feature.Motion is { } motion)
        {
            if (motion.CourseOverGround is { } cog)
                rows.Add(Row("courseOverGround", "Course over ground", FormattableString.Invariant($"{cog.TotalDegrees:0.#}°")));
            if (motion.Heading is { } heading)
                rows.Add(Row("heading", "Heading", FormattableString.Invariant($"{heading.TotalDegrees:0.#}°")));
            if (motion.SpeedOverGround is { } sog)
                rows.Add(Row("speedOverGround", "Speed over ground", FormattableString.Invariant($"{sog.TotalKnots:0.#} kn")));
        }
        foreach (var (code, value) in view.Attributes)
            rows.Add(new PickAttribute { Code = code, RawValue = value });
        rows.Add(Row("lastUpdated", "Last updated", feature.LastUpdated.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)));

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = FeatureTypeOf(feature),
            FeatureTypeName = _source.Metadata.DisplayName,
            Attributes = rows,
            Geometry = view.GeometryType,
        };

        static PickAttribute Row(string code, string name, string value) =>
            new() { Code = code, Name = name, RawValue = value };
    }

    private string FeatureTypeOf(DynamicFeature feature) => feature.Kind ?? _source.Id;

    private static GeographicBounds? Extent(IReadOnlyList<DynamicFeature> features)
    {
        double minLat = double.PositiveInfinity, minLon = double.PositiveInfinity;
        double maxLat = double.NegativeInfinity, maxLon = double.NegativeInfinity;
        foreach (var feature in features)
        {
            foreach (var (latitude, longitude) in feature.Coordinates)
            {
                minLat = Math.Min(minLat, latitude);
                maxLat = Math.Max(maxLat, latitude);
                minLon = Math.Min(minLon, longitude);
                maxLon = Math.Max(maxLon, longitude);
            }
        }
        return double.IsInfinity(minLat) ? null : new GeographicBounds(minLon, minLat, maxLon, maxLat);
    }

    // A dynamic feature as an IS100Feature, for the shared hit tester.
    private sealed class FeatureView(DynamicFeature feature, string featureType) : IS100Feature
    {
        public string Id => feature.Id;

        public string FeatureType => featureType;

        public S100GeometryType GeometryType => feature.GeometryType switch
        {
            VectorGeometryType.Point => S100GeometryType.Point,
            VectorGeometryType.Curve => S100GeometryType.Curve,
            VectorGeometryType.Surface => S100GeometryType.Surface,
            _ => S100GeometryType.None,
        };

        public IReadOnlyList<GeoPosition> Points =>
            feature.GeometryType == VectorGeometryType.Point ? feature.Coordinates : [];

        public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves =>
            feature.GeometryType == VectorGeometryType.Curve ? [feature.Coordinates] : [];

        public IReadOnlyList<GeoPosition> ExteriorRing =>
            feature.GeometryType == VectorGeometryType.Surface ? feature.Coordinates : [];

        public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings => [];

        public IReadOnlyDictionary<string, string> Attributes { get; } = feature.Attributes
            .Where(a => a.Value is not null)
            .ToDictionary(
                a => a.Key,
                a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);

        public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes => [];
    }
}
