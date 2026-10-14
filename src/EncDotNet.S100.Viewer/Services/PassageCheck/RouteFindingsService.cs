using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The findings list for the checked route (#905, handoff F1–F5): built
/// from each <see cref="PassageCheckService"/> result, plus the selected
/// finding, which the map outlines and the profile cursor points at.
/// </summary>
/// <remarks>
/// <para>
/// Clearance findings come from <see cref="PassageFindings"/>. #888's route
/// checks will add theirs to the same list as <see cref="RouteFinding"/>s.
/// </para>
/// <para>
/// <see cref="Select"/> follows the Validation tab's click-to-zoom pattern
/// (<c>ValidationFindingViewModel.ZoomToFindingCommand</c>): it zooms the
/// map to the finding's location and moves the profile cursor to it.
/// </para>
/// </remarks>
internal sealed class RouteFindingsService : IDisposable
{
    /// <summary>Half the side of the box a point finding zooms to, in EPSG:3857 metres (as Validation).</summary>
    internal const double PointZoomHalfMetres = 500.0;

    private readonly PassageCheckService _check;
    private readonly RoutesService _routes;
    private readonly IDatasetCatalog _catalog;
    private readonly ICapabilityAccessor<IMapViewportController>? _viewport;
    private readonly IMarinerSettingsProvider? _mariner;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeProvider _clock;
    private readonly ICrsTransformFactory _transforms;
    private readonly object _engineGate = new();
    private PassageCheckSnapshot? _built;
    private IReadOnlyList<LoadedDataset>? _engineSnapshot;
    private PassageCheckEngine? _engine;
    private bool _disposed;

    public RouteFindingsService(
        PassageCheckService check,
        RoutesService routes,
        IDatasetCatalog catalog,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IMarinerSettingsProvider? mariner = null,
        ITimeFormatProvider? timeFormat = null,
        TimeProvider? clock = null,
        ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(catalog);
        _check = check;
        _routes = routes;
        _catalog = catalog;
        _viewport = viewport;
        _mariner = mariner;
        _timeFormat = timeFormat;
        _clock = clock ?? TimeProvider.System;
        _transforms = transforms ?? new ProjNetCrsTransformFactory();

        _check.Changed += OnCheckChanged;
        if (_mariner is not null)
            _mariner.Changed += OnFormatChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnFormatChanged;
        Rebuild(force: true);
    }

    /// <summary>The checked route's findings, grouped and in route order; empty without a result.</summary>
    public IReadOnlyList<RouteFinding> Findings { get; private set; } = [];

    /// <summary>The selected finding, or <c>null</c>.</summary>
    public RouteFinding? Selected { get; private set; }

    /// <summary>Raised when <see cref="Findings"/> or <see cref="Selected"/> changes.</summary>
    public event EventHandler? Changed;

    private DepthUnit DepthUnit => _mariner?.Current.DepthUnit ?? DepthUnit.Metres;

    private TimeFormat TimeFormat => _timeFormat?.Current ?? TimeFormat.Local;

    /// <summary>
    /// Selects a finding (or clears the selection with <c>null</c>). A placed
    /// finding moves the profile cursor to it and, when
    /// <paramref name="zoom"/> is set, zooms the map to it (F3).
    /// </summary>
    /// <param name="finding">A finding from <see cref="Findings"/>, or <c>null</c>.</param>
    /// <param name="zoom">Whether to zoom the map to the finding.</param>
    public void Select(RouteFinding? finding, bool zoom = true)
    {
        Selected = finding;
        if (finding is not null)
        {
            if (finding.DistanceMeters is { } distance)
                _check.SetCursor(distance);
            if (zoom && ResolveZoomExtent(finding) is { } extent)
                _viewport?.Current?.ZoomToExtent(extent);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The EPSG:3857 extent a finding zooms to: its span's box, or a 1 km box
    /// around a point; never smaller than that box. <c>null</c> without a location.
    /// </summary>
    internal static MRect? ResolveZoomExtent(RouteFinding finding)
    {
        if (finding.Location is not { Path.Count: > 0 } location)
            return null;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in location.Path)
        {
            var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var halfW = Math.Max((maxX - minX) / 2, PointZoomHalfMetres);
        var halfH = Math.Max((maxY - minY) / 2, PointZoomHalfMetres);
        return new MRect(cx - halfW, cy - halfH, cx + halfW, cy + halfH);
    }

    /// <summary>
    /// The findings for any route with a draft: the checked route's current
    /// list, or a one-off check of another route (or of the active route
    /// while Check passage is off). Returns <c>null</c> when the route can't
    /// be checked (no legs, or a leg without a draft).
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="cancellationToken">Cancels the one-off check.</param>
    /// <remarks>Call on the UI thread; the one-off check runs on the thread pool.</remarks>
    internal async Task<IReadOnlyList<RouteFinding>?> FindingsForAsync(Route route, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (_check.IsActive && _check.Current is { } current && current.RouteId == route.Id
            && ReferenceEquals(route, _routes.Routes.ActiveRoute))
        {
            Rebuild(force: false);
            return Findings;
        }

        if (route.Legs.Count == 0)
            return null;
        var legs = RouteEtas.ToPassageLegs(route);
        var named = new PassageLeg[legs.Count];
        for (var i = 0; i < legs.Count; i++)
        {
            if (route.GetEffectiveLegValue(i, RoutePassageField.Draft) is null)
                return null;
            named[i] = legs[i] with { Name = $"{i + 1} → {i + 2}" };
        }

        var plan = new PassagePlan(named, route.Passage.Departure ?? _routes.ViewTime.Current);
        var datasets = _catalog.Datasets;
        var depthUnit = DepthUnit;
        var timeFormat = TimeFormat;
        var routeId = route.Id;
        return await Task.Run(() =>
        {
            PassageCheckResult result;
            lock (_engineGate)
            {
                if (!ReferenceEquals(datasets, _engineSnapshot) || _engine is null)
                {
                    var sources = PassageDataSources.FromDatasets(datasets, _transforms);
                    _engine = new PassageCheckEngine(sources.Depth, sources.WaterLevel);
                    _engineSnapshot = datasets;
                }

                var profile = _engine.SampleDepths(plan.Legs, null, cancellationToken);
                result = _engine.Evaluate(profile, plan, cancellationToken);
            }

            var snapshot = new PassageCheckSnapshot(routeId, named, result, new string?[named.Length], null);
            return PassageFindings.Build(snapshot, ExpiredForecasts(datasets, named, _clock.GetUtcNow()), depthUnit, timeFormat);
        }, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Names of the S-111 datasets that overlap the route and whose forecast
    /// ended before <paramref name="now"/> (E4: shown for reference, not used in ETAs).
    /// </summary>
    internal static IReadOnlyList<string> ExpiredForecasts(IReadOnlyList<LoadedDataset> datasets, IReadOnlyList<PassageLeg> legs, DateTimeOffset now)
    {
        if (legs.Count == 0)
            return [];
        double south = 90, north = -90, west = 180, east = -180;
        foreach (var leg in legs)
        {
            foreach (var p in leg.Points)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
            }
        }

        var names = new List<string>();
        foreach (var dataset in datasets)
        {
            if (!string.Equals(dataset.Spec.Name, "S-111", StringComparison.OrdinalIgnoreCase)
                || dataset.TimeRange is not { } range || range.End >= now)
                continue;
            var b = dataset.Bounds;
            if (b.SouthLatitude > north || b.NorthLatitude < south || b.WestLongitude > east || b.EastLongitude < west)
                continue;
            names.Add(Path.GetFileNameWithoutExtension(dataset.Id.Value));
        }

        return names;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _check.Changed -= OnCheckChanged;
        if (_mariner is not null)
            _mariner.Changed -= OnFormatChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged -= OnFormatChanged;
    }

    private void OnCheckChanged(object? sender, EventArgs e) => Rebuild(force: false);

    private void OnFormatChanged<T>(T _) => Rebuild(force: true);

    // The check service raises Changed for cursor moves too; the list is
    // rebuilt only when the result itself changes (or a format does).
    private void Rebuild(bool force)
    {
        var current = _check.IsActive ? _check.Current : null;
        if (!force && ReferenceEquals(current, _built))
            return;
        _built = current;

        Findings = current is null
            ? []
            : PassageFindings.Build(current, ExpiredForecasts(_catalog.Datasets, current.Legs, _clock.GetUtcNow()), DepthUnit, TimeFormat);

        // Keep the selection when the recompute found the same thing.
        if (Selected is { } selected)
            Selected = Findings.FirstOrDefault(f => f.Id == selected.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
