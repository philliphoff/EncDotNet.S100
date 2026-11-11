using System.Globalization;
using System.Xml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.Geometry;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>
/// An S-129 under-keel clearance plan (an Actual Plan) that a UKC service
/// computed for one of the scenario's S-421 routes.
/// </summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="PlanId">The plan's <c>gml:id</c>.</param>
/// <param name="Route">The <see cref="S421RouteSpec.DatasetName"/> of the route the plan follows; its passage values and departure are the plan's.</param>
/// <param name="ControlPoints">Place names on that route, in route order.</param>
/// <param name="UkcLimitMeters">The waterway's UKC limit, in metres.</param>
/// <param name="AlmostMarginMeters">How far above the limit counts as almost non-navigable, in metres.</param>
/// <param name="PlanArea">The plan area.</param>
/// <param name="AreaSpacing">The sampling step for the (almost) non-navigable areas, in metres.</param>
public sealed record S129PlanSpec(
    string DatasetName,
    string PlanId,
    string Route,
    IReadOnlyList<string> ControlPoints,
    double UkcLimitMeters,
    double AlmostMarginMeters,
    LocalRect PlanArea,
    double AreaSpacing)
{
    /// <summary>The file name, with the <c>.gml</c> extension.</summary>
    public string FileName => DatasetName + ".gml";
}

/// <summary>A control point as the world model computes it.</summary>
/// <param name="Name">The place name.</param>
/// <param name="Position">The position, rounded to 10⁻⁷ degrees.</param>
/// <param name="DistanceMeters">Distance along the route from departure, in local metres.</param>
/// <param name="ExpectedPassingTime">When the vessel passes (UTC, to the second).</param>
/// <param name="DistanceAboveUkcLimit">Depth + tide − draft − squat − UKC limit at that time, rounded to millimetres.</param>
/// <param name="WindowStart">The first whole minute, around the passing time, at which the point is above the limit.</param>
/// <param name="WindowEnd">The last such minute.</param>
public sealed record S129ControlPointTruth(
    string Name,
    GeoPosition Position,
    double DistanceMeters,
    DateTime ExpectedPassingTime,
    double DistanceAboveUkcLimit,
    DateTime WindowStart,
    DateTime WindowEnd);

/// <summary>
/// Computes an S-129 plan from the world model and writes it as S-129
/// Edition 2.0.0 GML (S-100 GML 5.0).
/// </summary>
/// <remarks>
/// <para>
/// The plan (<c>UnderKeelClearancePlan</c>) names its source route by the
/// route's <c>routeID</c> and edition, gives the maximum draught and the
/// plan's time range (six hours either side of the passage, about a tidal
/// half-cycle), and is an Actual Plan (purpose 2) for a time window
/// (calculation 1).
/// </para>
/// <para>
/// Each control point's expected passing time is the route's ETA there;
/// <c>distanceAboveUKCLimit</c> is the keel's height above the UKC limit at
/// that time; and <c>fixedTimeRange</c> is the window, to the minute and
/// within the plan's time range, in which the point stays above the limit
/// (S-129 Ed 2.0.0, "Control Point").
/// </para>
/// <para>
/// The non-navigable areas are where the keel would be below the limit at
/// the lowest tide during the passage (departure to arrival), and the almost non-navigable
/// areas where it would be less than <see cref="S129PlanSpec.AlmostMarginMeters"/>
/// above it, each traced by marching triangles within the plan area.
/// </para>
/// </remarks>
public static class S129PlanWriter
{
    /// <summary>The S-129 Edition 2.0.0 GML namespace.</summary>
    public const string Namespace = "http://www.iho.int/S129/2.0";

    private static readonly TimeSpan Margin = TimeSpan.FromHours(6);

    /// <summary>The route a plan follows.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The route.</returns>
    public static S421RouteSpec RouteOf(ScenarioDefinition definition, S129PlanSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        return definition.Routes.Single(r => r.DatasetName == spec.Route);
    }

    /// <summary>The plan's time range: six hours either side of the passage.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The start and end (UTC).</returns>
    public static (DateTime Start, DateTime End) TimeRange(ScenarioDefinition definition, S129PlanSpec spec)
    {
        var (departure, arrival) = Passage(definition, spec);
        return (departure - Margin, arrival + Margin);
    }

    /// <summary>The passage: departure and arrival on the plan's route.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The departure and arrival (UTC).</returns>
    public static (DateTime Departure, DateTime Arrival) Passage(ScenarioDefinition definition, S129PlanSpec spec)
    {
        var route = RouteOf(definition, spec);
        return (route.Departure, S421RouteBuilder.TimeAt(route, S421RouteBuilder.Distances(definition, route)[^1]));
    }

    /// <summary>The keel's height above the UKC limit at a point and time, in metres.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The height (negative below the limit).</returns>
    public static double AboveLimit(ScenarioDefinition definition, S129PlanSpec spec, double x, double y, DateTime utc)
    {
        var passage = RouteOf(definition, spec).Passage;
        var world = definition.World;
        return world.DepthAt(x, y) + world.Tide.HeightAt(x, y, utc) - passage.DraftMeters - passage.SquatMeters - spec.UkcLimitMeters;
    }

    /// <summary>The control points as the world model computes them.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The control points in route order.</returns>
    /// <exception cref="InvalidOperationException">A control point isn't on the route, or is below the limit at its passing time.</exception>
    public static IReadOnlyList<S129ControlPointTruth> ControlPoints(ScenarioDefinition definition, S129PlanSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        var route = RouteOf(definition, spec);
        var distances = S421RouteBuilder.Distances(definition, route);
        var (rangeStart, rangeEnd) = TimeRange(definition, spec);
        var points = new List<S129ControlPointTruth>();
        foreach (var name in spec.ControlPoints)
        {
            var (x, y) = definition.Places[name];
            var along = AlongRoute(definition, route, distances, x, y)
                ?? throw new InvalidOperationException($"Control point '{name}' isn't on route '{route.DatasetName}'.");
            var eta = S421RouteBuilder.TimeAt(route, along);
            var above = AboveLimit(definition, spec, x, y, eta);
            if (above < 0)
            {
                throw new InvalidOperationException($"Control point '{name}' is below the UKC limit at {eta:O}.");
            }

            var start = eta.AddSeconds(-eta.Second);
            while (start.AddMinutes(-1) >= rangeStart && AboveLimit(definition, spec, x, y, start.AddMinutes(-1)) >= 0)
            {
                start = start.AddMinutes(-1);
            }

            var end = eta.AddSeconds(-eta.Second);
            while (end.AddMinutes(1) <= rangeEnd && AboveLimit(definition, spec, x, y, end.AddMinutes(1)) >= 0)
            {
                end = end.AddMinutes(1);
            }

            points.Add(new S129ControlPointTruth(
                name,
                GmlWriting.Round(definition.World.Frame.ToGeo(x, y)),
                along,
                eta,
                Math.Round(above, 3),
                start,
                end));
        }

        return points;
    }

    /// <summary>The keel's height above the limit at the lowest tide during the passage, at a point (sampled every minute).</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns>The height in metres.</returns>
    public static double AboveLimitAtLowestTide(ScenarioDefinition definition, S129PlanSpec spec, double x, double y)
    {
        var (start, end) = Passage(definition, spec);
        var lowest = double.MaxValue;
        for (var t = start; t <= end; t = t.AddMinutes(1))
        {
            lowest = Math.Min(lowest, AboveLimit(definition, spec, x, y, t));
        }

        return lowest;
    }

    /// <summary>The (almost) non-navigable areas, with positions rounded to 10⁻⁷ degrees as written.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The non-navigable and almost non-navigable polygons.</returns>
    public static (IReadOnlyList<GeoPolygon> NonNavigable, IReadOnlyList<GeoPolygon> AlmostNonNavigable) Areas(ScenarioDefinition definition, S129PlanSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        var area = spec.PlanArea;
        var grid = new SampleGrid(
            definition.World.Frame,
            SampleGrid.Lattice(area.West, area.East, spec.AreaSpacing),
            SampleGrid.Lattice(area.South, area.North, spec.AreaSpacing),
            (x, y) => AboveLimitAtLowestTide(definition, spec, x, y));
        return (Rounded(Contouring.Regions(grid, double.NegativeInfinity, 0)), Rounded(Contouring.Regions(grid, 0, spec.AlmostMarginMeters)));

        // Positions as written: 10⁻⁷ degrees.
        static IReadOnlyList<GeoPolygon> Rounded(IReadOnlyList<GeoPolygon> polygons) =>
            polygons.Select(p => new GeoPolygon(
                p.Exterior.Select(GmlWriting.Round).ToList(),
                p.Holes.Select(h => (IReadOnlyList<GeoPosition>)h.Select(GmlWriting.Round).ToList()).ToList())).ToList();
    }

    /// <summary>Writes the plan.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The plan.</param>
    /// <returns>The UTF-8 encoded dataset.</returns>
    public static byte[] Build(ScenarioDefinition definition, S129PlanSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        var route = RouteOf(definition, spec);
        var frame = definition.World.Frame;
        var points = ControlPoints(definition, spec);
        var (nonNavigable, almost) = Areas(definition, spec);
        var (start, end) = TimeRange(definition, spec);
        var planArea = GmlWriting.Rectangle(frame, spec.PlanArea);
        const string ns = Namespace;

        return GmlWriting.Document(xml =>
        {
            xml.WriteStartElement("S129", "Dataset", ns);
            xml.WriteAttributeString("xmlns", "S100", null, GmlWriting.S100Gml5);
            xml.WriteAttributeString("xmlns", "gml", null, GmlWriting.Gml);
            xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.DatasetName);
            GmlWriting.BoundedBy(xml, planArea.Exterior);

            xml.WriteStartElement("S100", "DatasetIdentificationInformation", GmlWriting.S100Gml5);
            S100(xml, "encodingSpecification", "S-100 Part 10b");
            S100(xml, "encodingSpecificationEdition", "1.0");
            S100(xml, "productIdentifier", "S-129");
            S100(xml, "productEdition", "2.0.0");
            S100(xml, "applicationProfile", "1");
            S100(xml, "datasetFileIdentifier", spec.FileName);
            S100(xml, "datasetTitle", $"SYNTHETIC UKC plan in {definition.World.Name} (fictitious; not for navigation)");
            S100(xml, "datasetReferenceDate", definition.Issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            S100(xml, "datasetLanguage", "eng");
            S100(xml, "datasetTopicCategory", "oceans");
            S100(xml, "datasetPurpose", "base");
            S100(xml, "updateNumber", "0");
            xml.WriteEndElement();

            xml.WriteStartElement("S129", "members", ns);

            xml.WriteStartElement("S129", "UnderKeelClearancePlan", ns);
            xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.PlanId);
            TimeRangeElement(xml, start, end);
            xml.WriteElementString("S129", "generationTime", ns, GmlWriting.Time(definition.Issued));
            xml.WriteElementString("S129", "vesselID", ns, "ZZ00-SYNTHETIC-VESSEL");
            xml.WriteElementString("S129", "sourceRouteName", ns, route.RouteId);
            xml.WriteElementString("S129", "sourceRouteVersion", ns, S421RouteBuilder.Edition.ToString(CultureInfo.InvariantCulture));
            xml.WriteElementString("S129", "maximumDraught", ns, GmlWriting.Number(route.Passage.DraftMeters));
            Coded(xml, "underKeelClearancePurpose", 2, "Actual Plan");
            Coded(xml, "underKeelClearanceCalculationRequested", 1, "Time Window");
            xml.WriteEndElement();

            xml.WriteStartElement("S129", "UnderKeelClearancePlanArea", ns);
            xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.PlanId + ".AREA");
            Geometry(xml, spec.PlanId + ".AREA.G", planArea);
            xml.WriteEndElement();

            for (var i = 0; i < nonNavigable.Count; i++)
            {
                xml.WriteStartElement("S129", "UnderKeelClearanceNonNavigableArea", ns);
                xml.WriteAttributeString("gml", "id", GmlWriting.Gml, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.NNA.{i + 1}"));
                Geometry(xml, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.NNA.{i + 1}.G"), nonNavigable[i]);
                xml.WriteEndElement();
            }

            for (var i = 0; i < almost.Count; i++)
            {
                xml.WriteStartElement("S129", "UnderKeelClearanceAlmostNonNavigableArea", ns);
                xml.WriteAttributeString("gml", "id", GmlWriting.Gml, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.ANNA.{i + 1}"));
                xml.WriteElementString("S129", "distanceAboveUKCLimit", ns, GmlWriting.Number(spec.AlmostMarginMeters));
                Geometry(xml, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.ANNA.{i + 1}.G"), almost[i]);
                xml.WriteEndElement();
            }

            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                xml.WriteStartElement("S129", "UnderKeelClearanceControlPoint", ns);
                xml.WriteAttributeString("gml", "id", GmlWriting.Gml, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.CP.{i + 1}"));
                xml.WriteStartElement("S129", "featureName", ns);
                xml.WriteElementString("S129", "language", ns, "eng");
                xml.WriteElementString("S129", "name", ns, point.Name);
                Coded(xml, "nameUsage", 1, "Default Name Display");
                xml.WriteEndElement();
                xml.WriteElementString("S129", "expectedPassingTime", ns, GmlWriting.Time(point.ExpectedPassingTime));
                xml.WriteElementString("S129", "expectedPassingSpeed", ns, GmlWriting.Number(route.Passage.SpeedKnots));
                xml.WriteElementString("S129", "distanceAboveUKCLimit", ns, GmlWriting.Number(point.DistanceAboveUkcLimit));
                TimeRangeElement(xml, point.WindowStart, point.WindowEnd);
                xml.WriteStartElement("S129", "geometry", ns);
                GmlWriting.PointProperty(xml, GmlWriting.S100Gml5, string.Create(CultureInfo.InvariantCulture, $"{spec.PlanId}.CP.{i + 1}.G"), point.Position);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        });

        void TimeRangeElement(XmlWriter xml, DateTime from, DateTime to)
        {
            xml.WriteStartElement("S129", "fixedTimeRange", ns);
            xml.WriteElementString("S129", "timeStart", ns, GmlWriting.Time(from));
            xml.WriteElementString("S129", "timeEnd", ns, GmlWriting.Time(to));
            xml.WriteEndElement();
        }

        void Coded(XmlWriter xml, string name, int code, string label)
        {
            xml.WriteStartElement("S129", name, ns);
            xml.WriteAttributeString("code", code.ToString(CultureInfo.InvariantCulture));
            xml.WriteString(label);
            xml.WriteEndElement();
        }

        void Geometry(XmlWriter xml, string id, GeoPolygon polygon)
        {
            xml.WriteStartElement("S129", "geometry", ns);
            GmlWriting.SurfaceProperty(xml, GmlWriting.S100Gml5, id, polygon);
            xml.WriteEndElement();
        }
    }

    // The distance along the route to the point, when it lies on a leg (within a metre).
    private static double? AlongRoute(ScenarioDefinition definition, S421RouteSpec route, IReadOnlyList<double> distances, double x, double y)
    {
        for (var i = 1; i < route.Waypoints.Count; i++)
        {
            var (ax, ay) = definition.Places[route.Waypoints[i - 1]];
            var (bx, by) = definition.Places[route.Waypoints[i]];
            var dx = bx - ax;
            var dy = by - ay;
            var length2 = dx * dx + dy * dy;
            var f = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0, 1);
            var px = ax + f * dx;
            var py = ay + f * dy;
            if (Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) <= 1.0)
            {
                return distances[i - 1] + f * Math.Sqrt(length2);
            }
        }

        return null;
    }

    private static void S100(XmlWriter xml, string name, string value) =>
        xml.WriteElementString("S100", name, GmlWriting.S100Gml5, value);
}
