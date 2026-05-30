using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Concrete <see cref="IDynamicFeatureSource"/> that consumes an
/// <see cref="IAisMessageSource"/>, projects every position report
/// to a <see cref="DynamicFeature"/>, merges in the most recent
/// <see cref="AisStaticVoyageData"/> per MMSI, and ages stale
/// targets out via a caller-driven sweep.
/// </summary>
/// <remarks>
/// <para>
/// The source is intentionally driver-agnostic. Tests and the
/// future "replay a captured aisstream.io JSON-lines fixture"
/// driver use exactly the same code path as the production
/// aisstream.io driver — the abstraction seam is
/// <see cref="IAisMessageSource"/>.
/// </para>
/// <para>
/// <b>Aging.</b> AIS reports per ITU-R M.1371-5 §4.2.1 vary from
/// 2 s (high-speed Class A) to 3 min (anchored Class A) to 3 min
/// (Class B "still"). A 6-minute default retain window covers all
/// nominal cases with margin; callers may override via the
/// constructor. The source itself does not run a timer — the host
/// (viewer or sample) calls <see cref="Sweep(DateTimeOffset)"/> on
/// its own cadence.
/// </para>
/// <para>
/// <b>Threading.</b> Snapshot reads on
/// <see cref="CurrentFeatures"/> and <see cref="Metadata"/> are
/// thread-safe via <see cref="DynamicFeatureTracker{TInbound}"/>.
/// <see cref="Changed"/> is raised on whatever thread the inbound
/// AIS event arrived on.
/// </para>
/// </remarks>
public sealed class AisDynamicFeatureSource : IDynamicFeatureSource, IAsyncDisposable
{
    /// <summary>
    /// Default retention window for stale targets — 6 minutes.
    /// Covers ITU-R M.1371-5 §4.2.1's longest nominal reporting
    /// interval (3 min, anchored Class A and Class B "still") with
    /// 2× margin for transmission gaps.
    /// </summary>
    public static readonly TimeSpan DefaultRetainWindow = TimeSpan.FromMinutes(6);

    private readonly IAisMessageSource _messageSource;
    private readonly IAisSubscription _subscription;
    private readonly DynamicFeatureTracker<AisPositionReport> _tracker;
    private readonly object _staticLock = new();
    private readonly Dictionary<uint, AisStaticVoyageData> _staticByMmsi = new();
    private readonly IReadOnlySet<AisShipTypeClass>? _shipTypeAllowList;
    private readonly TimeSpan _retain;
    private bool _disposed;

    /// <summary>
    /// Wraps an <see cref="IAisMessageSource"/> as a
    /// <see cref="IDynamicFeatureSource"/>.
    /// </summary>
    /// <param name="id">Stable instance id (e.g. <c>"ais"</c>).</param>
    /// <param name="messageSource">Driver supplying decoded AIS messages.</param>
    /// <param name="request">Initial subscription filters.</param>
    /// <param name="retain">
    /// Stale-target retention window. <see langword="null"/> uses
    /// <see cref="DefaultRetainWindow"/>.
    /// </param>
    public AisDynamicFeatureSource(
        string id,
        IAisMessageSource messageSource,
        AisSubscriptionRequest? request = null,
        TimeSpan? retain = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(messageSource);

        Id = id;
        _messageSource = messageSource;
        _retain = retain ?? DefaultRetainWindow;
        _tracker = new DynamicFeatureTracker<AisPositionReport>(Project);

        var resolved = request ?? new AisSubscriptionRequest();
        _shipTypeAllowList = resolved.ShipTypes is null
            ? null
            : new HashSet<AisShipTypeClass>(resolved.ShipTypes);

        Metadata = new DynamicSourceMetadata
        {
            DisplayName = messageSource.Metadata.DisplayName,
            Description = messageSource.Metadata.Description,
            RendererKey = "vessel.ais",
        };

        _subscription = messageSource.Subscribe(resolved);
        _subscription.PositionReportReceived += OnPositionReportReceived;
        _subscription.StaticVoyageDataReceived += OnStaticVoyageDataReceived;
        _subscription.TargetLost += OnTargetLost;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures => _tracker.Current;

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>
    /// Updates the spatial filter on the underlying subscription.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the underlying driver could
    /// update its filter in place; <see langword="false"/> when the
    /// caller must dispose this source and create a new one with a
    /// new <see cref="AisSubscriptionRequest"/>.
    /// </returns>
    public bool UpdateArea(BoundingBox? area)
    {
        ThrowIfDisposed();
        return _subscription.TryUpdateArea(area);
    }

    /// <summary>
    /// Removes feature entries whose last position report is older
    /// than the configured retention window. The host calls this on
    /// its own cadence (typically 1 Hz).
    /// </summary>
    public void Sweep(DateTimeOffset now)
    {
        ThrowIfDisposed();
        var change = _tracker.Sweep(now, _retain);
        if (change.Kind == DynamicSourceChangeKind.Removed)
        {
            // Drop static cache entries for swept MMSIs to keep the
            // dictionary bounded; an MMSI that comes back will get a
            // fresh ShipStaticData record before its next position.
            lock (_staticLock)
            {
                foreach (var id in change.ChangedIds)
                {
                    var mmsi = TryParseMmsiFromFeatureId(id);
                    if (mmsi is not null)
                    {
                        _staticByMmsi.Remove(mmsi.Value);
                    }
                }
            }
            Changed?.Invoke(this, change);
        }
    }

    /// <summary>
    /// Constructs the stable feature id for a given MMSI.
    /// </summary>
    public static string FeatureIdForMmsi(uint mmsi) => $"ais:{mmsi}";

    private void OnPositionReportReceived(object? sender, AisPositionReport report)
    {
        if (_disposed) return;
        if (!IsClassAllowed(report.Mmsi)) return;
        var change = _tracker.Apply(report);
        Changed?.Invoke(this, change);
    }

    private void OnStaticVoyageDataReceived(object? sender, AisStaticVoyageData data)
    {
        if (_disposed) return;

        bool refresh;
        bool evict = false;
        lock (_staticLock)
        {
            _staticByMmsi[data.Mmsi] = data;
            refresh = _tracker.Current.Any(f => f.Id == FeatureIdForMmsi(data.Mmsi));
            if (_shipTypeAllowList is not null
                && !_shipTypeAllowList.Contains(data.ShipTypeClass))
            {
                evict = refresh;
                refresh = false;
            }
        }

        if (evict)
        {
            // Static data revealed this MMSI is in a class the
            // caller filtered out — remove the already-projected
            // feature so per-class subscriptions converge.
            var removal = _tracker.Remove(FeatureIdForMmsi(data.Mmsi));
            if (removal.Kind == DynamicSourceChangeKind.Removed)
            {
                Changed?.Invoke(this, removal);
            }
            return;
        }

        if (!refresh) return;

        // Re-project the most recent feature for this MMSI so the
        // static-data merge takes effect immediately. We don't have
        // the original AisPositionReport at hand, so reconstruct one
        // from the snapshot's lat/lon/motion/timestamp — sufficient
        // for the projection.
        var existing = _tracker.Current.FirstOrDefault(f => f.Id == FeatureIdForMmsi(data.Mmsi));
        if (existing is null) return;

        var synthetic = ReconstructPositionReport(existing, data.Mmsi);
        var change = _tracker.Apply(synthetic);
        Changed?.Invoke(this, change);
    }

    private void OnTargetLost(object? sender, AisTargetLost lost)
    {
        if (_disposed) return;
        var featureId = FeatureIdForMmsi(lost.Mmsi);
        var change = _tracker.Remove(featureId);
        lock (_staticLock) _staticByMmsi.Remove(lost.Mmsi);
        if (change.Kind == DynamicSourceChangeKind.Removed)
        {
            Changed?.Invoke(this, change);
        }
    }

    /// <summary>
    /// Whether a position report for <paramref name="mmsi"/> should be
    /// admitted under the active <see cref="AisSubscriptionRequest.ShipTypes"/>
    /// allow-list. When no static data is yet cached for the MMSI the
    /// report is admitted optimistically; if subsequent
    /// <see cref="AisStaticVoyageData"/> classifies the vessel into a
    /// disallowed class the feature is evicted in
    /// <see cref="OnStaticVoyageDataReceived"/>.
    /// </summary>
    private bool IsClassAllowed(uint mmsi)
    {
        if (_shipTypeAllowList is null) return true;
        AisStaticVoyageData? cached;
        lock (_staticLock) _staticByMmsi.TryGetValue(mmsi, out cached);
        if (cached is null) return true;
        return _shipTypeAllowList.Contains(cached.ShipTypeClass);
    }

    private DynamicFeature Project(AisPositionReport report)
    {
        AisStaticVoyageData? staticData;
        lock (_staticLock) _staticByMmsi.TryGetValue(report.Mmsi, out staticData);

        var cls = staticData?.ShipTypeClass ?? AisShipTypeClass.Unknown;
        var attributes = new Dictionary<string, object?>
        {
            ["mmsi"] = report.Mmsi,
        };
        if (report.NavigationStatus is { } navStatus)
        {
            attributes["navigationStatus"] = navStatus;
        }
        if (report.RateOfTurnDegPerMin is { } rot)
        {
            attributes["rateOfTurnDegPerMin"] = rot;
        }
        if (staticData is not null)
        {
            if (staticData.VesselName is { } name) attributes["vesselName"] = name;
            if (staticData.CallSign is { } callSign) attributes["callSign"] = callSign;
            if (staticData.ImoNumber is { } imo) attributes["imoNumber"] = imo;
            if (staticData.Destination is { } destination) attributes["destination"] = destination;
            if (staticData.Eta is { } eta) attributes["eta"] = eta;
            if (staticData.DraughtMetres is { } draught) attributes["draughtMetres"] = draught;
            attributes["shipType"] = (int)staticData.ShipType;
            attributes["shipTypeClass"] = cls;
        }

        DynamicVesselGeometry? geometry = null;
        if (staticData?.Dimensions is { } dims
            && dims.LengthMetres > 0 && dims.BeamMetres > 0)
        {
            geometry = new DynamicVesselGeometry
            {
                LengthMetres = dims.LengthMetres,
                BeamMetres = dims.BeamMetres,
                BowOffsetMetres = dims.BowOffsetMetres,
                PortOffsetMetres = dims.PortOffsetMetres,
            };
        }

        return new DynamicFeature
        {
            Id = FeatureIdForMmsi(report.Mmsi),
            Kind = $"vessel.ais.{cls.ToKindToken()}",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (report.Latitude, report.Longitude) },
            Motion = new DynamicMotion
            {
                CourseOverGroundDeg = report.CourseOverGroundDeg,
                HeadingDeg = report.HeadingDeg,
                SpeedOverGroundKn = report.SpeedOverGroundKn,
            },
            VesselGeometry = geometry,
            Attributes = attributes,
            LastUpdated = report.Timestamp,
        };
    }

    private static AisPositionReport ReconstructPositionReport(DynamicFeature feature, uint mmsi)
    {
        var (lat, lon) = feature.Coordinates[0];
        AisNavigationStatus? navStatus = null;
        double? rot = null;
        if (feature.Attributes.TryGetValue("navigationStatus", out var navObj)
            && navObj is AisNavigationStatus n)
        {
            navStatus = n;
        }
        if (feature.Attributes.TryGetValue("rateOfTurnDegPerMin", out var rotObj)
            && rotObj is double r)
        {
            rot = r;
        }
        return new AisPositionReport
        {
            Mmsi = mmsi,
            Timestamp = feature.LastUpdated,
            Latitude = lat,
            Longitude = lon,
            CourseOverGroundDeg = feature.Motion?.CourseOverGroundDeg,
            HeadingDeg = feature.Motion?.HeadingDeg,
            SpeedOverGroundKn = feature.Motion?.SpeedOverGroundKn,
            NavigationStatus = navStatus,
            RateOfTurnDegPerMin = rot,
        };
    }

    private static uint? TryParseMmsiFromFeatureId(string id)
    {
        const string prefix = "ais:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return uint.TryParse(id.AsSpan(prefix.Length), out var mmsi) ? mmsi : null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.PositionReportReceived -= OnPositionReportReceived;
        _subscription.StaticVoyageDataReceived -= OnStaticVoyageDataReceived;
        _subscription.TargetLost -= OnTargetLost;
        await _subscription.DisposeAsync().ConfigureAwait(false);
    }
}
