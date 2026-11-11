using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Headless state machine driving Measure Mode. Holds the ordered list
/// of waypoints, plus a transient rubber-band coordinate that follows
/// the cursor while the user is composing a path. UI-free and
/// fully testable: the <see cref="MeasureTool"/> wraps it with Avalonia
/// gesture plumbing.
/// </summary>
internal sealed class MeasurePathState
{
    private readonly List<GeoPosition> _waypoints = new();
    private GeoPosition? _rubberBand;
    private MeasurePhase _phase = MeasurePhase.Idle;

    /// <summary>Lifecycle of the current measurement.</summary>
    public enum MeasurePhase
    {
        /// <summary>No path. Next click starts one.</summary>
        Idle,

        /// <summary>Path under construction; rubber-band follows the cursor.</summary>
        Drawing,

        /// <summary>Path completed; visible until the user starts a new one or exits.</summary>
        Finalised,
    }

    public MeasurePhase Phase => _phase;

    /// <summary>The placed waypoints, in click order.</summary>
    public IReadOnlyList<GeoPosition> Waypoints => _waypoints;

    /// <summary>
    /// While <see cref="Phase"/> is <see cref="MeasurePhase.Drawing"/>,
    /// the cursor's current world position (or <c>null</c> if the cursor
    /// is off-map). Renderers use this to draw the dashed rubber-band
    /// segment from the last waypoint to the cursor.
    /// </summary>
    public GeoPosition? RubberBand => _rubberBand;

    /// <summary>
    /// Records a click at <paramref name="lat"/>/<paramref name="lon"/>.
    /// Idle / Drawing append a waypoint; Finalised clears the path and
    /// starts a new one with this waypoint.
    /// </summary>
    /// <returns>True when the click changed state.</returns>
    public bool Click(double lat, double lon)
    {
        SelectedLeg = null;
        switch (_phase)
        {
            case MeasurePhase.Finalised:
                _waypoints.Clear();
                _rubberBand = null;
                _waypoints.Add(new GeoPosition(lat, lon));
                _phase = MeasurePhase.Drawing;
                return true;

            case MeasurePhase.Idle:
                _waypoints.Add(new GeoPosition(lat, lon));
                _phase = MeasurePhase.Drawing;
                return true;

            case MeasurePhase.Drawing:
                _waypoints.Add(new GeoPosition(lat, lon));
                return true;
        }
        return false;
    }

    /// <summary>
    /// Updates the rubber-band cursor position. Pass <c>null</c> when the
    /// cursor leaves the map. Outside of <see cref="MeasurePhase.Drawing"/>
    /// this is a no-op so the finalised path doesn't grow a tail.
    /// </summary>
    public bool Hover(GeoPosition? cursor)
    {
        if (_phase != MeasurePhase.Drawing)
        {
            if (_rubberBand is null) return false;
            _rubberBand = null;
            return true;
        }

        if (Equals(cursor, _rubberBand))
            return false;

        _rubberBand = cursor;
        return true;
    }

    /// <summary>
    /// Finalises the current path. No-op unless we're drawing with at
    /// least one waypoint. Returns true when the phase actually
    /// transitioned to <see cref="MeasurePhase.Finalised"/>.
    /// </summary>
    public bool Finalise()
    {
        if (_phase != MeasurePhase.Drawing || _waypoints.Count == 0)
            return false;

        _phase = MeasurePhase.Finalised;
        _rubberBand = null;
        return true;
    }

    /// <summary>
    /// Removes the most recently placed waypoint. When the last
    /// waypoint is removed the tool drops to <see cref="MeasurePhase.Idle"/>.
    /// In <see cref="MeasurePhase.Finalised"/> this clears the entire
    /// path. Returns true when the state changed.
    /// </summary>
    public bool Backstep()
    {
        if (_phase == MeasurePhase.Idle || _waypoints.Count == 0)
            return false;

        if (_phase == MeasurePhase.Finalised)
        {
            _waypoints.Clear();
            _rubberBand = null;
            SelectedLeg = null;
            _phase = MeasurePhase.Idle;
            return true;
        }

        _waypoints.RemoveAt(_waypoints.Count - 1);
        SelectedLeg = null;
        if (_waypoints.Count == 0)
        {
            _rubberBand = null;
            _phase = MeasurePhase.Idle;
        }
        return true;
    }

    /// <summary>Discards the entire path and returns to Idle.</summary>
    public bool Discard()
    {
        if (_phase == MeasurePhase.Idle && _waypoints.Count == 0 && _rubberBand is null)
            return false;
        _waypoints.Clear();
        _rubberBand = null;
        SelectedLeg = null;
        _phase = MeasurePhase.Idle;
        return true;
    }

    /// <summary>
    /// The geometry every leg of the path is measured along: rhumb lines
    /// (constant bearing) or great circles (shortest distance).
    /// </summary>
    public LegGeometry Geometry { get; private set; } = LegGeometry.Rhumb;

    /// <summary>
    /// The one-based <see cref="MeasureLeg.Index"/> of the leg the card shows,
    /// or <c>null</c> to follow the last leg as the path grows.
    /// </summary>
    public int? SelectedLeg { get; private set; }

    /// <summary>Sets <see cref="Geometry"/>. Returns true when it changed.</summary>
    public bool SetGeometry(LegGeometry geometry)
    {
        if (Geometry == geometry)
            return false;
        Geometry = geometry;
        return true;
    }

    /// <summary>
    /// Selects the leg with one-based <paramref name="index"/>, or follows the
    /// last leg when <c>null</c>. Selecting the last leg follows it too, so a
    /// growing path keeps showing its newest leg. Returns true when the
    /// selection changed.
    /// </summary>
    public bool SelectLeg(int? index)
    {
        var legCount = ComputeLegs().Count;
        if (index is { } i && (i < 1 || i > legCount))
            return false;
        if (index == legCount)
            index = null;
        if (SelectedLeg == index)
            return false;
        SelectedLeg = index;
        return true;
    }

    /// <summary>
    /// The leg the card shows: the selected leg, or the last leg (the pending
    /// one while drawing). <c>null</c> when the path has no leg yet.
    /// </summary>
    public MeasureLeg? CurrentLeg
    {
        get
        {
            var legs = ComputeLegs();
            if (legs.Count == 0)
                return null;
            return SelectedLeg is { } i && i <= legs.Count ? legs[i - 1] : legs[^1];
        }
    }

    /// <summary>
    /// Replaces the path with <paramref name="waypoints"/>, finalised, as typed
    /// or set from outside the map. An empty list clears the path.
    /// </summary>
    public void SetWaypoints(IEnumerable<GeoPosition> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        _waypoints.Clear();
        _waypoints.AddRange(waypoints);
        _rubberBand = null;
        SelectedLeg = null;
        _phase = _waypoints.Count == 0 ? MeasurePhase.Idle : MeasurePhase.Finalised;
    }

    /// <summary>
    /// Moves waypoint <paramref name="index"/> (zero-based) to
    /// <paramref name="position"/>. Returns false when there's no such
    /// waypoint.
    /// </summary>
    public bool MoveWaypoint(int index, GeoPosition position)
    {
        if (index < 0 || index >= _waypoints.Count)
            return false;
        _waypoints[index] = position;
        return true;
    }

    /// <summary>
    /// Adds a typed waypoint to the end of the path. The first one starts
    /// drawing, as a click does; a later one also finalises the path, so the
    /// typed leg stays on the card instead of giving way to the rubber band.
    /// </summary>
    public void AppendTypedWaypoint(GeoPosition position)
    {
        if (_phase == MeasurePhase.Finalised && _waypoints.Count == 0)
            _phase = MeasurePhase.Idle;
        _waypoints.Add(position);
        SelectedLeg = null;
        if (_waypoints.Count == 1)
        {
            _phase = MeasurePhase.Drawing;
        }
        else
        {
            _phase = MeasurePhase.Finalised;
            _rubberBand = null;
        }
    }

    /// <summary>
    /// Returns the distance and bearings of each placed segment (waypoint
    /// i → waypoint i+1) along <see cref="Geometry"/> plus, while drawing
    /// with a rubber-band in scope, the in-progress pending leg from the last
    /// waypoint to the cursor.
    /// </summary>
    public IReadOnlyList<MeasureLeg> ComputeLegs()
    {
        var result = new List<MeasureLeg>();
        for (int i = 1; i < _waypoints.Count; i++)
            result.Add(MeasureLeg.Between(i, isRubberBand: false, Geometry, _waypoints[i - 1], _waypoints[i]));

        if (_phase == MeasurePhase.Drawing && _rubberBand is { } rb && _waypoints.Count > 0)
            result.Add(MeasureLeg.Between(_waypoints.Count, isRubberBand: true, Geometry, _waypoints[^1], rb));

        return result;
    }

    /// <summary>
    /// Total distance in metres across all current legs, including the
    /// pending rubber-band leg when present.
    /// </summary>
    public double TotalDistanceMetres()
    {
        double total = 0.0;
        foreach (var leg in ComputeLegs())
            total += leg.DistanceMetres;
        return total;
    }

    /// <summary>
    /// Total distance (NM) across all current legs, including the pending
    /// rubber-band leg when present.
    /// </summary>
    public double TotalDistanceNm() => TotalDistanceMetres() / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// Renders a localised summary for the status bar in the primary
    /// <paramref name="unit"/> only (D6): the shown leg and the total.
    /// </summary>
    public string? FormatSummary(DistanceUnit unit = DistanceUnit.NauticalMiles)
    {
        if (CurrentLeg is not { } leg)
            return Strings.Status_MeasureNoData;

        var legText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_MeasureLeg,
            leg.Index,
            MeasureFormat.Distance(leg.DistanceMetres, unit),
            MeasureFormat.Bearing(leg.BearingDeg));
        var totalText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_MeasureTotal,
            MeasureFormat.Distance(TotalDistanceMetres(), unit));
        return $"{legText}  |  {totalText}";
    }
}

/// <summary>One segment of a measurement path.</summary>
/// <param name="Index">One-based leg number: leg 1 joins waypoints 1 and 2.</param>
/// <param name="IsRubberBand">True for the pending leg to the cursor.</param>
/// <param name="Geometry">The geometry the leg is measured along.</param>
/// <param name="From">The leg's start.</param>
/// <param name="To">The leg's end.</param>
/// <param name="DistanceMetres">Length along <paramref name="Geometry"/>.</param>
/// <param name="BearingDeg">True bearing at <paramref name="From"/> towards <paramref name="To"/>.</param>
/// <param name="ReverseBearingDeg">True bearing at <paramref name="To"/> back towards <paramref name="From"/>.</param>
internal readonly record struct MeasureLeg(
    int Index,
    bool IsRubberBand,
    LegGeometry Geometry,
    GeoPosition From,
    GeoPosition To,
    double DistanceMetres,
    double BearingDeg,
    double ReverseBearingDeg)
{
    /// <summary>Length in nautical miles.</summary>
    public double DistanceNm => DistanceMetres / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>Measures the leg from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static MeasureLeg Between(int index, bool isRubberBand, LegGeometry geometry, GeoPosition from, GeoPosition to) =>
        new(index, isRubberBand, geometry, from, to,
            MarineGeodesy.DistanceMetres(geometry, from, to),
            MarineGeodesy.InitialBearingDegrees(geometry, from, to),
            MarineGeodesy.ReverseBearingDegrees(geometry, from, to));
}
