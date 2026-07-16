using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Geodesy;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// A first-class, persistent, editable route: an ordered list of
/// <see cref="RouteWaypoint"/>s plus the <see cref="RouteLeg"/>s joining
/// them and route-level <see cref="RouteInfo"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the mutable counterpart to the immutable S-421 reader projection
/// (<see cref="EncDotNet.S100.Datasets.S421.DataModel.S421Route"/>). It is
/// the formalisation of a Measure Mode path: where
/// <see cref="EncDotNet.S100.Viewer.Tools.MeasurePathState"/> is a transient
/// ruler, a <see cref="Route"/> is a named, editable object that survives
/// beyond a single gesture and can be manipulated by the interactive editor
/// and by agents over the MCP endpoint.
/// </para>
/// <para>
/// The route maintains the invariant that the number of legs is always
/// <c>max(0, waypointCount - 1)</c>, with leg <c>i</c> joining waypoint
/// <c>i</c> to waypoint <c>i+1</c>. All structural mutations go through this
/// type's methods so the invariant and the <see cref="Changed"/> event stay
/// consistent. Leg attributes are preserved across waypoint edits wherever
/// the underlying geographic segment is preserved.
/// </para>
/// </remarks>
public sealed class Route
{
    private readonly List<RouteWaypoint> _waypoints = new();
    private readonly List<RouteLeg> _legs = new();

    /// <summary>
    /// Creates an empty route.
    /// </summary>
    /// <param name="id">Stable identifier for the route. When <c>null</c> or
    /// whitespace a new GUID-based id is generated.</param>
    /// <param name="name">Optional initial route name, copied into
    /// <see cref="Info"/>.</param>
    public Route(string? id = null, string? name = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id;
        Info = new RouteInfo { Name = name };
    }

    /// <summary>Stable identifier, unique within a <see cref="RouteCollection"/>.</summary>
    public string Id { get; }

    /// <summary>Route-level metadata. Never <c>null</c>.</summary>
    public RouteInfo Info { get; }

    /// <summary>
    /// Convenience accessor for <see cref="RouteInfo.Name"/>.
    /// </summary>
    public string? Name
    {
        get => Info.Name;
        set
        {
            if (Info.Name == value)
                return;
            Info.Name = value;
            OnChanged();
        }
    }

    /// <summary>Waypoints in route order.</summary>
    public IReadOnlyList<RouteWaypoint> Waypoints => _waypoints;

    /// <summary>
    /// Legs in route order. <c>Legs[i]</c> joins <c>Waypoints[i]</c> to
    /// <c>Waypoints[i+1]</c>; the count is always
    /// <c>max(0, Waypoints.Count - 1)</c>.
    /// </summary>
    public IReadOnlyList<RouteLeg> Legs => _legs;

    /// <summary>
    /// Raised after any structural or attribute change to the route, its
    /// waypoints, or its legs made through this type's methods. Direct
    /// mutation of a <see cref="RouteLeg"/> or <see cref="RouteInfo"/>
    /// property does not raise this event; call <see cref="NotifyChanged"/>
    /// to signal such edits.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Appends a waypoint to the end of the route, adding a trailing leg
    /// when this produces a second-or-later waypoint.
    /// </summary>
    /// <param name="position">The new waypoint's position.</param>
    /// <returns>The created waypoint.</returns>
    public RouteWaypoint AppendWaypoint(GeoPosition position)
        => InsertWaypoint(_waypoints.Count, position);

    /// <summary>
    /// Inserts a waypoint at <paramref name="index"/>, splitting or
    /// extending the leg list so the leg invariant is preserved. Leg
    /// attributes on the segment being split are retained on the segment
    /// leading into the new waypoint; the new outgoing segment starts with
    /// default leg attributes.
    /// </summary>
    /// <param name="index">Insertion index in <c>[0, Waypoints.Count]</c>.</param>
    /// <param name="position">The new waypoint's position.</param>
    /// <returns>The created waypoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>[0, Waypoints.Count]</c>.
    /// </exception>
    public RouteWaypoint InsertWaypoint(int index, GeoPosition position)
    {
        if (index < 0 || index > _waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var waypoint = new RouteWaypoint { Position = position };
        _waypoints.Insert(index, waypoint);

        // Adding the second-or-later waypoint always introduces exactly one
        // new leg. For an insert at the front the new leg leads the list; for
        // any other insert it sits immediately after the (now-shortened)
        // preceding leg, clamped to the current leg count for an append.
        if (_waypoints.Count >= 2)
        {
            var legIndex = index == 0 ? 0 : Math.Min(index, _legs.Count);
            _legs.Insert(legIndex, new RouteLeg());
        }

        OnChanged();
        return waypoint;
    }

    /// <summary>
    /// Moves the waypoint at <paramref name="index"/> to a new position. The
    /// adjacent legs keep their attributes; their distances and bearings are
    /// recomputed on the next call to <see cref="ComputeLegMetrics"/>.
    /// </summary>
    /// <param name="index">Index of the waypoint to move.</param>
    /// <param name="position">The new position.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not a valid waypoint index.
    /// </exception>
    public void MoveWaypoint(int index, GeoPosition position)
    {
        if (index < 0 || index >= _waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var waypoint = _waypoints[index];
        if (waypoint.Position.Equals(position))
            return;

        waypoint.Position = position;
        OnChanged();
    }

    /// <summary>
    /// Removes the waypoint at <paramref name="index"/>, merging the two
    /// legs that met at it into one (whose attributes are those of the
    /// inbound leg) when the waypoint is interior, or dropping the single
    /// dangling leg when it is an endpoint.
    /// </summary>
    /// <param name="index">Index of the waypoint to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not a valid waypoint index.
    /// </exception>
    public void RemoveWaypoint(int index)
    {
        if (index < 0 || index >= _waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var wasLast = index == _waypoints.Count - 1;
        _waypoints.RemoveAt(index);

        if (_legs.Count > 0)
        {
            // Removing the trailing waypoint drops the final leg; removing any
            // other drops the leg leaving that waypoint, so the inbound leg's
            // attributes survive on the merged segment.
            var legIndex = wasLast ? _legs.Count - 1 : index;
            _legs.RemoveAt(legIndex);
        }

        OnChanged();
    }

    /// <summary>
    /// Removes all waypoints and legs, returning the route to empty.
    /// </summary>
    public void Clear()
    {
        if (_waypoints.Count == 0 && _legs.Count == 0)
            return;
        _waypoints.Clear();
        _legs.Clear();
        OnChanged();
    }

    /// <summary>
    /// Reverses the order of the route's waypoints (and, correspondingly, its
    /// legs), so the former end becomes the new start. Each leg's attributes
    /// travel with the geographic segment it described, so a leg's geometry
    /// type and navigational envelope are preserved on the same pair of
    /// waypoints after the reversal.
    /// </summary>
    public void Reverse()
    {
        if (_waypoints.Count < 2)
            return;

        _waypoints.Reverse();
        _legs.Reverse();
        OnChanged();
    }

    /// <summary>
    /// Computes the distance and initial bearing of the leg at
    /// <paramref name="legIndex"/> from its waypoint positions and geometry
    /// type.
    /// </summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <returns>The computed leg metrics.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="legIndex"/> is not a valid leg index.
    /// </exception>
    public RouteLegMetrics ComputeLegMetrics(int legIndex)
    {
        if (legIndex < 0 || legIndex >= _legs.Count)
            throw new ArgumentOutOfRangeException(nameof(legIndex));

        var a = _waypoints[legIndex].Position;
        var b = _waypoints[legIndex + 1].Position;
        var leg = _legs[legIndex];

        double distance;
        double bearing;
        if (leg.GeometryType == RouteLegGeometryType.Geodesic)
        {
            distance = MarineGeodesy.GreatCircleDistanceNm(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            bearing = MarineGeodesy.GreatCircleInitialBearingDegrees(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        }
        else
        {
            distance = MarineGeodesy.RhumbDistanceNm(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            bearing = MarineGeodesy.RhumbBearingDegrees(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        }

        return new RouteLegMetrics(legIndex, distance, bearing);
    }

    /// <summary>
    /// Computes metrics for every leg in route order.
    /// </summary>
    /// <returns>One <see cref="RouteLegMetrics"/> per leg.</returns>
    public IReadOnlyList<RouteLegMetrics> ComputeAllLegMetrics()
    {
        var result = new List<RouteLegMetrics>(_legs.Count);
        for (var i = 0; i < _legs.Count; i++)
            result.Add(ComputeLegMetrics(i));
        return result;
    }

    /// <summary>
    /// Total length of the route in nautical miles, summed across all legs
    /// using each leg's geometry type.
    /// </summary>
    /// <returns>The total distance in nautical miles (0 for a route with
    /// fewer than two waypoints).</returns>
    public double TotalDistanceNm()
    {
        double total = 0.0;
        for (var i = 0; i < _legs.Count; i++)
            total += ComputeLegMetrics(i).DistanceNm;
        return total;
    }

    /// <summary>
    /// Raises <see cref="Changed"/> to signal an in-place edit to a leg or
    /// route-info property that did not go through one of this type's
    /// mutating methods.
    /// </summary>
    public void NotifyChanged() => OnChanged();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
