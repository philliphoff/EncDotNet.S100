using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Concrete <see cref="IDynamicFeatureSource"/> publishing the
/// vessel's own position as a single <c>DynamicFeature</c>
/// (Id <c>"ownship"</c>, Kind <c>"ownship"</c>). Bridges a thin
/// position provider (<see cref="IOwnShipPositionProvider"/>) — the
/// PR-D2 reference driver is the synthetic dead-reckoner; a future
/// real-GPS / NMEA-replay driver implements the same interface.
/// </summary>
/// <remarks>
/// <para>
/// The source always exposes either zero features (no fix yet, or
/// the toggle is off) or exactly one feature. Aging /
/// <c>DynamicFeatureTracker</c> is intentionally not used — a
/// singleton feature has no aging surface.
/// </para>
/// <para>
/// <c>RendererKey</c> is <c>"ownship"</c>, resolving to
/// <c>EncDotNet.S100.Renderers.Mapsui.DynamicSources.OwnShipRenderer</c>
/// — true-scale hull outline when zoomed in, disc pictogram when
/// zoomed out, arrowhead on the heading vector. The renderer reads
/// the per-feature <c>DynamicVesselGeometry</c> sidecar populated
/// here from <see cref="IOwnShipVesselGeometryProvider"/>; settings
/// edits propagate through that provider's <c>Changed</c> event,
/// which the source treats as a re-publish trigger so the new dims
/// take effect without waiting for the next fix.
/// </para>
/// <para>
/// Speed conversion: the provider supplies SOG in metres per second
/// (the SI convention). The published
/// <c>DynamicMotion.SpeedOverGroundKn</c> field is in knots —
/// converted with the maritime factor 1 m/s ≈ 1.9438444924406
/// kn (3600 / 1852).
/// </para>
/// <para>
/// <see cref="IsEnabled"/> is the toggle backing the viewer toolbar
/// button. When set to <see langword="false"/> the source raises a
/// <see cref="DynamicSourceChangeKind.Reset"/> with an empty
/// <see cref="CurrentFeatures"/>; when flipped back on the cached
/// most-recent fix (if any) is republished as <c>Added</c>.
/// </para>
/// </remarks>
internal sealed class OwnShipSource : IDynamicFeatureSource, INotifyPropertyChanged, IDisposable
{
    /// <summary>Stable singleton feature id.</summary>
    public const string FeatureId = "ownship";

    /// <summary>Renderer-dispatch hint published on the feature.</summary>
    public const string FeatureKind = "ownship";

    /// <summary>1 m/s expressed in knots (3600 / 1852).</summary>
    internal const double MetresPerSecondToKnots = 3600.0 / 1852.0;

    private static readonly IReadOnlyList<DynamicFeature> EmptyFeatures = Array.Empty<DynamicFeature>();

    private readonly IOwnShipPositionProvider _provider;
    private readonly IOwnShipVesselGeometryProvider? _geometryProvider;
    private readonly object _gate = new();
    private IReadOnlyList<DynamicFeature> _current = EmptyFeatures;
    private OwnShipPosition? _lastFix;
    private bool _isEnabled = true;
    private int _disposed;

    public OwnShipSource(IOwnShipPositionProvider provider)
        : this(provider, geometryProvider: null)
    {
    }

    public OwnShipSource(
        IOwnShipPositionProvider provider,
        IOwnShipVesselGeometryProvider? geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _geometryProvider = geometryProvider;

        Metadata = new DynamicSourceMetadata
        {
            DisplayName = Strings.OwnShip_DisplayName,
            Description = Strings.OwnShip_Description,
            RendererKey = FeatureKind,
        };

        _provider.Updated += OnProviderUpdated;
        if (_geometryProvider is not null)
        {
            _geometryProvider.Changed += OnGeometryChanged;
        }

        // If the provider already has a fix at construction time
        // (e.g. a test stub that was seeded synchronously) surface
        // it immediately so the first Changed-after-Register rebuild
        // paints something.
        if (_provider.Current is { } seed)
        {
            ApplyFix(seed, raise: false);
        }
    }

    /// <inheritdoc />
    public string Id => FeatureId;

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>
    /// Whether the source is currently publishing the own-ship
    /// feature. Setting to <see langword="false"/> empties
    /// <see cref="CurrentFeatures"/> and raises
    /// <see cref="DynamicSourceChangeKind.Reset"/>; setting back to
    /// <see langword="true"/> republishes the most-recent fix as
    /// <see cref="DynamicSourceChangeKind.Added"/> (if one exists).
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            lock (_gate) return _isEnabled;
        }
        set
        {
            DynamicFeaturesChanged? toRaise = null;
            lock (_gate)
            {
                if (_isEnabled == value) return;
                _isEnabled = value;

                if (!value)
                {
                    if (_current.Count > 0)
                    {
                        _current = EmptyFeatures;
                        toRaise = new DynamicFeaturesChanged
                        {
                            Kind = DynamicSourceChangeKind.Reset,
                            ChangedIds = Array.Empty<string>(),
                        };
                    }
                }
                else if (_provider.Current is { } fix)
                {
                    _current = new[] { Project(fix) };
                    toRaise = new DynamicFeaturesChanged
                    {
                        Kind = DynamicSourceChangeKind.Added,
                        ChangedIds = new[] { FeatureId },
                    };
                }
            }

            OnPropertyChanged();
            if (toRaise is not null) Changed?.Invoke(this, toRaise);
        }
    }

    private void OnProviderUpdated(object? sender, OwnShipPosition fix)
    {
        ApplyFix(fix, raise: true);
    }

    private void OnGeometryChanged(object? sender, EventArgs e)
    {
        // Re-publish the most recent fix so the new vessel-geometry
        // sidecar reaches the renderer without waiting for the next
        // position update.
        OwnShipPosition? fix;
        lock (_gate)
        {
            fix = _lastFix;
        }
        if (fix is { } f) ApplyFix(f, raise: true);
    }

    private void ApplyFix(OwnShipPosition fix, bool raise)
    {
        DynamicFeaturesChanged? toRaise = null;
        lock (_gate)
        {
            _lastFix = fix;
            if (!_isEnabled) return;

            var wasEmpty = _current.Count == 0;
            _current = new[] { Project(fix) };

            if (raise)
            {
                toRaise = new DynamicFeaturesChanged
                {
                    Kind = wasEmpty
                        ? DynamicSourceChangeKind.Added
                        : DynamicSourceChangeKind.Updated,
                    ChangedIds = new[] { FeatureId },
                };
            }
        }

        if (toRaise is not null) Changed?.Invoke(this, toRaise);
    }

    private DynamicFeature Project(OwnShipPosition fix)
    {
        // Synthetic / GPS-only drivers expose Course Over Ground but
        // not a separate gyro Heading. Default renderer keys the
        // predictor line off Heading, so we mirror COG → Heading so
        // the predictor draws for v1. A future driver that
        // distinguishes the two will populate them separately.
        var motion =
            fix.CourseOverGroundDeg is null && fix.SpeedOverGroundMs is null
                ? null
                : new DynamicMotion
                {
                    CourseOverGroundDeg = fix.CourseOverGroundDeg,
                    HeadingDeg = fix.CourseOverGroundDeg,
                    SpeedOverGroundKn = fix.SpeedOverGroundMs is { } ms
                        ? ms * MetresPerSecondToKnots
                        : null,
                };

        return new DynamicFeature
        {
            Id = FeatureId,
            Kind = FeatureKind,
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (fix.Latitude, fix.Longitude) },
            Motion = motion,
            VesselGeometry = _geometryProvider?.Current,
            LastUpdated = fix.Timestamp,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _provider.Updated -= OnProviderUpdated;
        if (_geometryProvider is not null)
        {
            _geometryProvider.Changed -= OnGeometryChanged;
        }
    }
}
