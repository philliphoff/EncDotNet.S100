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
/// Motion units are carried end to end by strongly typed quantities
/// (<see cref="EncDotNet.S100.Quantities.Angle"/>,
/// <see cref="EncDotNet.S100.Quantities.Speed"/>): the provider's
/// <c>OwnShipPosition</c> and the published <c>DynamicMotion</c> both
/// use them, so this source forwards course, heading, and speed with
/// no unit conversion.
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
        : this(provider, geometryProvider, initiallyEnabled: true)
    {
    }

    /// <summary>
    /// Creates the source with an explicit initial enabled state.
    /// </summary>
    /// <param name="provider">Position provider feeding own-ship fixes.</param>
    /// <param name="geometryProvider">
    /// Optional vessel-geometry sidecar provider.
    /// </param>
    /// <param name="initiallyEnabled">
    /// Initial value of <see cref="IsEnabled"/>. When
    /// <see langword="false"/> the source publishes nothing until it is
    /// enabled — used to honour the persisted
    /// <c>ViewerSettings.OwnShipOverlayEnabled</c> gate (off by default)
    /// so the simulated own-ship overlay is hidden at launch.
    /// </param>
    public OwnShipSource(
        IOwnShipPositionProvider provider,
        IOwnShipVesselGeometryProvider? geometryProvider,
        bool initiallyEnabled)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _geometryProvider = geometryProvider;
        _isEnabled = initiallyEnabled;

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
        // Heading is published separately from Course Over Ground when
        // the provider knows it (a real gyro, or an impersonated AIS
        // target reporting heading independently of COG). When the
        // provider supplies no heading we mirror COG → Heading so the
        // default renderer's predictor line still draws — a COG-only
        // driver behaves exactly as before.
        var heading = fix.Heading ?? fix.CourseOverGround;
        var motion =
            fix.CourseOverGround is null && fix.SpeedOverGround is null
                && fix.Heading is null
                ? null
                : new DynamicMotion
                {
                    CourseOverGround = fix.CourseOverGround,
                    Heading = heading,
                    SpeedOverGround = fix.SpeedOverGround,
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
