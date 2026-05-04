using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly List<(double Lat, double Lon)> _waypoints = new();
    private (double Lat, double Lon)? _rubberBand;
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
    public IReadOnlyList<(double Lat, double Lon)> Waypoints => _waypoints;

    /// <summary>
    /// While <see cref="Phase"/> is <see cref="MeasurePhase.Drawing"/>,
    /// the cursor's current world position (or <c>null</c> if the cursor
    /// is off-map). Renderers use this to draw the dashed rubber-band
    /// segment from the last waypoint to the cursor.
    /// </summary>
    public (double Lat, double Lon)? RubberBand => _rubberBand;

    /// <summary>
    /// Records a click at <paramref name="lat"/>/<paramref name="lon"/>.
    /// Idle / Drawing append a waypoint; Finalised clears the path and
    /// starts a new one with this waypoint.
    /// </summary>
    /// <returns>True when the click changed state.</returns>
    public bool Click(double lat, double lon)
    {
        switch (_phase)
        {
            case MeasurePhase.Finalised:
                _waypoints.Clear();
                _rubberBand = null;
                _waypoints.Add((lat, lon));
                _phase = MeasurePhase.Drawing;
                return true;

            case MeasurePhase.Idle:
                _waypoints.Add((lat, lon));
                _phase = MeasurePhase.Drawing;
                return true;

            case MeasurePhase.Drawing:
                _waypoints.Add((lat, lon));
                return true;
        }
        return false;
    }

    /// <summary>
    /// Updates the rubber-band cursor position. Pass <c>null</c> when the
    /// cursor leaves the map. Outside of <see cref="MeasurePhase.Drawing"/>
    /// this is a no-op so the finalised path doesn't grow a tail.
    /// </summary>
    public bool Hover((double Lat, double Lon)? cursor)
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
            _phase = MeasurePhase.Idle;
            return true;
        }

        _waypoints.RemoveAt(_waypoints.Count - 1);
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
        _phase = MeasurePhase.Idle;
        return true;
    }

    /// <summary>
    /// Returns the rhumb distance + bearing of each placed segment
    /// (waypoint i → waypoint i+1) plus, while drawing with a rubber-band
    /// in scope, the in-progress pending leg from the last waypoint to
    /// the cursor.
    /// </summary>
    public IReadOnlyList<MeasureLeg> ComputeLegs()
    {
        var result = new List<MeasureLeg>();
        for (int i = 1; i < _waypoints.Count; i++)
        {
            var a = _waypoints[i - 1];
            var b = _waypoints[i];
            result.Add(new MeasureLeg(
                Index: i,
                IsRubberBand: false,
                FromLat: a.Lat, FromLon: a.Lon,
                ToLat: b.Lat, ToLon: b.Lon,
                DistanceNm: MarineGeodesy.RhumbDistanceNm(a.Lat, a.Lon, b.Lat, b.Lon),
                BearingDeg: MarineGeodesy.RhumbBearingDegrees(a.Lat, a.Lon, b.Lat, b.Lon)));
        }

        if (_phase == MeasurePhase.Drawing && _rubberBand is { } rb && _waypoints.Count > 0)
        {
            var a = _waypoints[^1];
            result.Add(new MeasureLeg(
                Index: _waypoints.Count,
                IsRubberBand: true,
                FromLat: a.Lat, FromLon: a.Lon,
                ToLat: rb.Lat, ToLon: rb.Lon,
                DistanceNm: MarineGeodesy.RhumbDistanceNm(a.Lat, a.Lon, rb.Lat, rb.Lon),
                BearingDeg: MarineGeodesy.RhumbBearingDegrees(a.Lat, a.Lon, rb.Lat, rb.Lon)));
        }

        return result;
    }

    /// <summary>
    /// Total rhumb distance (NM) across all current legs, including the
    /// pending rubber-band leg when present.
    /// </summary>
    public double TotalDistanceNm()
    {
        double total = 0.0;
        foreach (var leg in ComputeLegs())
            total += leg.DistanceNm;
        return total;
    }

    /// <summary>
    /// Renders a localised summary string for the status bar. Returns
    /// <c>null</c> when there is nothing to show (Idle and no rubber-band).
    /// </summary>
    public string? FormatSummary()
    {
        if (_phase == MeasurePhase.Idle && _waypoints.Count == 0)
            return Strings.Status_MeasureNoData;

        var legs = ComputeLegs();
        if (legs.Count == 0)
        {
            // We have a single placed waypoint and no rubber-band yet.
            return Strings.Status_MeasureNoData;
        }

        var last = legs[^1];
        var legText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_MeasureLeg,
            last.Index,
            last.DistanceNm,
            last.BearingDeg);
        var totalText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_MeasureTotal,
            TotalDistanceNm());
        return $"{legText}  |  {totalText}";
    }
}

/// <summary>One segment of a measurement path.</summary>
internal readonly record struct MeasureLeg(
    int Index,
    bool IsRubberBand,
    double FromLat, double FromLon,
    double ToLat, double ToLon,
    double DistanceNm,
    double BearingDeg);
