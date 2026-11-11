using System.Globalization;
using System.Text;
using System.Xml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>Options for <see cref="S421DatasetWriter"/>.</summary>
public sealed record S421WriteOptions
{
    /// <summary>
    /// The dataset's <c>gml:id</c>. When <c>null</c>, the writer uses
    /// <c>S421.</c> followed by the route's <see cref="S421Route.Id"/>.
    /// </summary>
    public string? DatasetIdentifier { get; init; }
}

/// <summary>
/// Writes a typed <see cref="S421Route"/> as an S-421 Route Plan dataset:
/// S-100 Part 10b GML in the S-421 Edition 1.0.0 application schema
/// (<c>http://www.iho.int/S421/gml/cs0/1.0</c>), which
/// <see cref="S421Dataset.Open(Stream)"/> reads back.
/// </summary>
/// <remarks>
/// <para>
/// The dataset holds one <c>Route</c> feature with its <c>RouteInfo</c>,
/// a <c>RouteWaypoints</c> collection, one <c>RouteWaypoint</c> per
/// waypoint, one <c>RouteWaypointLeg</c> per leg, and, when the route has
/// them, schedules and action points. Objects refer to each other with
/// <c>xlink:href</c>, in both directions, as the feature catalogue's
/// compositions do.
/// </para>
/// <para>
/// A leg is referenced from the waypoint it ends at (S-421 Edition 1.0.0,
/// <c>RouteWaypoint.routeWaypointLeg</c>), so the first waypoint has none.
/// Each leg's geometry is its <see cref="S421Leg.Coordinates"/>, or the
/// straight line between its waypoints when it has fewer than two.
/// </para>
/// <para>
/// Positions are written latitude first, as EPSG:4326 requires (S-100
/// Part 10b). Numbers use the invariant culture and round-trip exactly;
/// times are written in UTC. Attributes the typed model doesn't know
/// (<c>ExtraAttributes</c>) are written after the known ones, in ordinal
/// order. The output is UTF-8 without a byte-order mark, with <c>\n</c>
/// line endings, so the same route gives the same bytes.
/// </para>
/// </remarks>
public static class S421DatasetWriter
{
    /// <summary>The S-421 Edition 1.0.0 GML application-schema namespace.</summary>
    public const string Namespace = "http://www.iho.int/S421/gml/cs0/1.0";

    private const string S100Namespace = "http://www.iho.int/s100gml/1.0";
    private const string GmlNamespace = "http://www.opengis.net/gml/3.2";
    private const string XlinkNamespace = "http://www.w3.org/1999/xlink";
    private const string RolePrefix = "http://www.iho.int/S-421/gml/1.0/roles/";
    private const string Srs = "EPSG:4326";

    /// <summary>Writes <paramref name="route"/> to <paramref name="stream"/>.</summary>
    /// <param name="route">The route.</param>
    /// <param name="stream">The stream to write to; it is left open.</param>
    /// <param name="options">Options, or <c>null</c> for the defaults.</param>
    /// <exception cref="ArgumentException">The route has fewer than two waypoints.</exception>
    public static void Write(S421Route route, Stream stream, S421WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(stream);
        if (route.Waypoints.Count < 2)
        {
            throw new ArgumentException("An S-421 route needs at least two waypoints.", nameof(route));
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            CloseOutput = false,
        };

        using var xml = XmlWriter.Create(stream, settings);
        new Writer(xml, route).WriteDataset(options?.DatasetIdentifier ?? "S421." + route.Id);
    }

    /// <summary>Writes <paramref name="route"/> to a file, replacing it if it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="route">The route.</param>
    /// <param name="options">Options, or <c>null</c> for the defaults.</param>
    public static void WriteToFile(string path, S421Route route, S421WriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.Create(path);
        Write(route, stream, options);
    }

    /// <summary>Writes <paramref name="route"/> to a byte array.</summary>
    /// <param name="route">The route.</param>
    /// <param name="options">Options, or <c>null</c> for the defaults.</param>
    /// <returns>The UTF-8 encoded dataset.</returns>
    public static byte[] ToBytes(S421Route route, S421WriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(route, stream, options);
        return stream.ToArray();
    }

    private sealed class Writer(XmlWriter xml, S421Route route)
    {
        private static readonly IFormatProvider Invariant = CultureInfo.InvariantCulture;

        private readonly string _waypointsId = route.Id + ".WPTS";
        private readonly string _schedulesId = route.Id + ".SCHEDS";
        private readonly string _actionPointsId = route.Id + ".APTS";

        public void WriteDataset(string datasetId)
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("S421", "Dataset", Namespace);
            xml.WriteAttributeString("xmlns", "S100", null, S100Namespace);
            xml.WriteAttributeString("xmlns", "gml", null, GmlNamespace);
            xml.WriteAttributeString("xmlns", "xlink", null, XlinkNamespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, datasetId);

            WriteBoundedBy();
            WriteRoute();
            if (route.Info is { } info)
            {
                WriteInfo(info);
            }

            WriteWaypoints();
            WriteSchedules();
            WriteActionPoints();

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        private void WriteBoundedBy()
        {
            var positions = route.Waypoints.Select(w => w.Position)
                .Concat(route.Legs.SelectMany(l => l.Coordinates))
                .Concat(route.ActionPoints.SelectMany(a => a.Coordinates))
                .ToList();
            xml.WriteStartElement("gml", "boundedBy", GmlNamespace);
            xml.WriteStartElement("gml", "Envelope", GmlNamespace);
            xml.WriteAttributeString("srsName", Srs);
            xml.WriteElementString("gml", "lowerCorner", GmlNamespace, Pos(positions.Min(p => p.Latitude), positions.Min(p => p.Longitude)));
            xml.WriteElementString("gml", "upperCorner", GmlNamespace, Pos(positions.Max(p => p.Latitude), positions.Max(p => p.Longitude)));
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private void WriteRoute()
        {
            xml.WriteStartElement("member");
            xml.WriteStartElement("S421", "Route", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, route.Id);
            Simple("routeFormatVersion", route.FormatVersion ?? "1.0");
            Simple("routeID", route.RouteId);
            Simple("routeEditionNo", route.EditionNumber);
            Extras(route.ExtraAttributes);
            if (route.Info is { } info)
            {
                Reference("routeInfo", info.Id);
            }

            Reference("routeWaypoints", _waypointsId);
            if (route.Schedules.Count > 0)
            {
                Reference("routeSchedules", _schedulesId);
            }

            if (route.ActionPoints.Count > 0)
            {
                Reference("routeActionPoints", _actionPointsId);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private void WriteInfo(S421RouteInfo info)
        {
            xml.WriteStartElement("imember");
            xml.WriteStartElement("S421", "RouteInfo", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, info.Id);
            Simple("routeInfoName", info.Name);
            Simple("routeInfoAuthor", info.Author);
            Simple("routeInfoEditionTime", info.EditionTime);
            Simple("routeInfoDescription", info.Description);
            Simple("routeInfoStatus", info.Status);
            Simple("routeInfoValidityStart", info.ValidityStart);
            Simple("routeInfoValidityEnd", info.ValidityEnd);
            Simple("routeInfoDeparturePortID1", info.DeparturePortId1);
            Simple("routeInfoDeparturePortID2", info.DeparturePortId2);
            Simple("routeInfoDeparturePortCall", info.DeparturePortCall);
            Simple("routeInfoArrivalPortID1", info.ArrivalPortId1);
            Simple("routeInfoArrivalPortID2", info.ArrivalPortId2);
            Simple("routeInfoArrivalPortCall", info.ArrivalPortCall);
            Simple("routeInfoReferencePrevRoute", info.PreviousRouteReference);
            Simple("routeInfoReferenceNextRoute", info.NextRouteReference);
            if (info.Vessel is { } vessel)
            {
                Simple("routeInfoVesselType", vessel.VesselType);
                Simple("routeInfoVesselName", vessel.Name);
                Simple("routeInfoVesselMMSI", vessel.Mmsi);
                Simple("routeInfoVesselCallsign", vessel.Callsign);
                Simple("routeInfoVesselIMO", vessel.Imo);
                Simple("routeInfoVesselVoyage", vessel.VoyageId);
                Simple("routeInfoVesselHeight", vessel.HeightMeters);
                Simple("routeInfoVesselLength", vessel.LengthMeters);
                Simple("routeInfoVesselBeam", vessel.BeamMeters);
            }

            Extras(info.ExtraAttributes);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private void WriteWaypoints()
        {
            xml.WriteStartElement("member");
            xml.WriteStartElement("S421", "RouteWaypoints", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, _waypointsId);
            Reference("routeWaypointsCollection", route.Id);
            foreach (var waypoint in route.Waypoints)
            {
                Reference("routeWaypoint", waypoint.Id);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();

            var arriving = ArrivingLegs();
            for (var i = 0; i < route.Waypoints.Count; i++)
            {
                WriteWaypoint(route.Waypoints[i], arriving[i]);
            }

            for (var i = 1; i < route.Waypoints.Count; i++)
            {
                if (arriving[i] is { } leg)
                {
                    WriteLeg(leg, route.Waypoints[i - 1], route.Waypoints[i]);
                }
            }
        }

        // The leg that ends at each waypoint: the previous waypoint's outgoing
        // leg, or this waypoint's incoming one.
        private S421Leg?[] ArrivingLegs()
        {
            var legs = new S421Leg?[route.Waypoints.Count];
            for (var i = 1; i < legs.Length; i++)
            {
                legs[i] = route.Waypoints[i - 1].OutgoingLeg ?? route.Waypoints[i].IncomingLeg;
            }

            return legs;
        }

        private void WriteWaypoint(S421Waypoint waypoint, S421Leg? arriving)
        {
            xml.WriteStartElement("member");
            xml.WriteStartElement("S421", "RouteWaypoint", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, waypoint.Id);
            xml.WriteStartElement("geometry");
            xml.WriteStartElement("S100", "pointProperty", S100Namespace);
            xml.WriteStartElement("S100", "Point", S100Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, waypoint.Id + ".PT");
            xml.WriteAttributeString("srsName", Srs);
            xml.WriteElementString("gml", "pos", GmlNamespace, Pos(waypoint.Position));
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            Simple("routeWaypointID", waypoint.WaypointNumber);
            Simple("routeWaypointName", waypoint.Name);
            Simple("routeWaypointExternalReferenceID", waypoint.ExternalReferenceId);
            Simple("routeWaypointFixed", waypoint.Fixed is { } isFixed ? (isFixed ? "true" : "false") : null);
            Simple("routeWaypointTurnRadius", waypoint.TurnRadius);
            Extras(waypoint.ExtraAttributes);
            Reference("routeWaypointCollection", _waypointsId);
            if (arriving is not null)
            {
                Reference("routeWaypointLeg", arriving.Id);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private void WriteLeg(S421Leg leg, S421Waypoint start, S421Waypoint end)
        {
            xml.WriteStartElement("member");
            xml.WriteStartElement("S421", "RouteWaypointLeg", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, leg.Id);
            xml.WriteStartElement("geometry");
            xml.WriteStartElement("S100", "curveProperty", S100Namespace);
            xml.WriteStartElement("S100", "Curve", S100Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, leg.Id + ".CV");
            xml.WriteAttributeString("srsName", Srs);
            xml.WriteStartElement("gml", "segments", GmlNamespace);
            xml.WriteStartElement("gml", "LineStringSegment", GmlNamespace);
            var coordinates = leg.Coordinates.Count >= 2 ? leg.Coordinates : [start.Position, end.Position];
            xml.WriteElementString("gml", "posList", GmlNamespace, string.Join(' ', coordinates.Select(Pos)));
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            Simple("routeWaypointLegStarboardXTDL", leg.StarboardCrossTrackDistanceLimit);
            Simple("routeWaypointLegPortXTDL", leg.PortCrossTrackDistanceLimit);
            Simple("routeWaypointLegStarboardCL", leg.StarboardChannelLimit);
            Simple("routeWaypointLegPortCL", leg.PortChannelLimit);
            Simple("routeWaypointLegSafetyContour", leg.SafetyContour);
            Simple("routeWaypointLegSafetyDepth", leg.SafetyDepth);
            Simple("routeWaypointLegGeometryType", leg.GeometryTypeCode);
            Simple("routeWaypointLegSOGMin", leg.SpeedOverGroundMin);
            Simple("routeWaypointLegSOGMax", leg.SpeedOverGroundMax);
            Simple("routeWaypointLegSTWMin", leg.SpeedThroughWaterMin);
            Simple("routeWaypointLegSTWMax", leg.SpeedThroughWaterMax);
            Simple("routeWaypointLegDraft", leg.Draft);
            Simple("routeWaypointLegStaticUKC", leg.StaticUnderKeelClearance);
            Simple("routeWaypointLegDynamicUKC", leg.DynamicUnderKeelClearance);
            Simple("routeWaypointLegSafetyMargin", leg.SafetyMargin);
            Simple("routeWaypointLegNote", leg.Note);
            Extras(leg.ExtraAttributes);
            Reference("routeWaypointLegCollection", end.Id);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private void WriteSchedules()
        {
            if (route.Schedules.Count == 0)
            {
                return;
            }

            xml.WriteStartElement("imember");
            xml.WriteStartElement("S421", "RouteSchedules", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, _schedulesId);
            foreach (var schedule in route.Schedules)
            {
                Reference("routeSchedule", schedule.Id);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();

            foreach (var schedule in route.Schedules)
            {
                xml.WriteStartElement("imember");
                xml.WriteStartElement("S421", "RouteSchedule", Namespace);
                xml.WriteAttributeString("gml", "id", GmlNamespace, schedule.Id);
                Simple("routeScheduleID", schedule.ScheduleNumber);
                Simple("routeScheduleName", schedule.Name);
                Extras(schedule.ExtraAttributes);
                Reference("routeScheduleCollection", _schedulesId);
                foreach (var variant in schedule.Variants)
                {
                    Reference("routeSchedule" + VariantName(variant.Kind), variant.Id);
                }

                xml.WriteEndElement();
                xml.WriteEndElement();

                foreach (var variant in schedule.Variants)
                {
                    WriteVariant(schedule, variant);
                }
            }
        }

        private void WriteVariant(S421Schedule schedule, S421ScheduleVariant variant)
        {
            var name = VariantName(variant.Kind);
            xml.WriteStartElement("imember");
            xml.WriteStartElement("S421", "RouteSchedule" + name, Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, variant.Id);
            Reference("routeScheduleCollection", schedule.Id);
            foreach (var element in variant.Elements)
            {
                Reference("routeScheduleElement", element.Id);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();

            foreach (var element in variant.Elements)
            {
                xml.WriteStartElement("imember");
                xml.WriteStartElement("S421", "RouteScheduleElement", Namespace);
                xml.WriteAttributeString("gml", "id", GmlNamespace, element.Id);
                Simple("routeWaypointID", element.WaypointNumber);
                Simple("routeScheduleElementPlanSOG", element.PlannedSpeedOverGround);
                Simple("routeScheduleElementETD", element.Etd);
                Simple("routeScheduleElementETA", element.Eta);
                Simple("routeScheduleElementETDWindowBefore", element.EtdWindowBeforeMinutes);
                Simple("routeScheduleElementETDWindowAfter", element.EtdWindowAfterMinutes);
                Simple("routeScheduleElementETAWindowBefore", element.EtaWindowBeforeMinutes);
                Simple("routeScheduleElementETAWindowAfter", element.EtaWindowAfterMinutes);
                Simple("routeScheduleElementNote", element.Note);
                Extras(element.ExtraAttributes);
                Reference($"routeScheduleElement{name}Collection", variant.Id);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
        }

        private void WriteActionPoints()
        {
            if (route.ActionPoints.Count == 0)
            {
                return;
            }

            xml.WriteStartElement("member");
            xml.WriteStartElement("S421", "RouteActionPoints", Namespace);
            xml.WriteAttributeString("gml", "id", GmlNamespace, _actionPointsId);
            Reference("routeActionPointsCollection", route.Id);
            foreach (var point in route.ActionPoints)
            {
                Reference("routeActionPoint", point.Id);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();

            foreach (var point in route.ActionPoints)
            {
                xml.WriteStartElement("member");
                xml.WriteStartElement("S421", "RouteActionPoint", Namespace);
                xml.WriteAttributeString("gml", "id", GmlNamespace, point.Id);
                WriteActionPointGeometry(point);
                Simple("routeActionPointID", point.ActionPointNumber);
                Simple("routeActionPointName", point.Name);
                Simple("routeActionPointRadius", point.RadiusNauticalMiles);
                Simple("routeActionPointTimeToAct", point.TimeToActMinutes);
                Simple("routeActionPointRequiredAction", point.RequiredAction);
                Simple("routeActionPointRequiredActionDescription", point.RequiredActionDescription);
                Extras(point.ExtraAttributes);
                Reference("routeActionPointCollection", _actionPointsId);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
        }

        private void WriteActionPointGeometry(S421ActionPoint point)
        {
            if (point.Coordinates.Count == 0)
            {
                return;
            }

            xml.WriteStartElement("geometry");
            switch (point.GeometryKind)
            {
                case S421ActionPointGeometryKind.Curve when point.Coordinates.Count >= 2:
                    xml.WriteStartElement("S100", "curveProperty", S100Namespace);
                    xml.WriteStartElement("S100", "Curve", S100Namespace);
                    xml.WriteAttributeString("gml", "id", GmlNamespace, point.Id + ".CV");
                    xml.WriteAttributeString("srsName", Srs);
                    xml.WriteStartElement("gml", "segments", GmlNamespace);
                    xml.WriteStartElement("gml", "LineStringSegment", GmlNamespace);
                    xml.WriteElementString("gml", "posList", GmlNamespace, string.Join(' ', point.Coordinates.Select(Pos)));
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    break;
                case S421ActionPointGeometryKind.Surface when point.Coordinates.Count >= 4:
                    xml.WriteStartElement("S100", "surfaceProperty", S100Namespace);
                    xml.WriteStartElement("S100", "Surface", S100Namespace);
                    xml.WriteAttributeString("gml", "id", GmlNamespace, point.Id + ".SF");
                    xml.WriteAttributeString("srsName", Srs);
                    xml.WriteStartElement("gml", "patches", GmlNamespace);
                    xml.WriteStartElement("gml", "PolygonPatch", GmlNamespace);
                    xml.WriteStartElement("gml", "exterior", GmlNamespace);
                    xml.WriteStartElement("gml", "LinearRing", GmlNamespace);
                    xml.WriteElementString("gml", "posList", GmlNamespace, string.Join(' ', point.Coordinates.Select(Pos)));
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    break;
                default:
                    xml.WriteStartElement("S100", "pointProperty", S100Namespace);
                    xml.WriteStartElement("S100", "Point", S100Namespace);
                    xml.WriteAttributeString("gml", "id", GmlNamespace, point.Id + ".PT");
                    xml.WriteAttributeString("srsName", Srs);
                    xml.WriteElementString("gml", "pos", GmlNamespace, Pos(point.Coordinates[0]));
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    break;
            }

            xml.WriteEndElement();
        }

        private static string VariantName(S421ScheduleVariantKind kind) => kind switch
        {
            S421ScheduleVariantKind.Manual => "Manual",
            S421ScheduleVariantKind.Calculated => "Calculated",
            S421ScheduleVariantKind.Recommended => "Recommended",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown schedule kind."),
        };

        private void Reference(string role, string targetId)
        {
            xml.WriteStartElement(role);
            xml.WriteAttributeString("xlink", "href", XlinkNamespace, "#" + targetId);
            xml.WriteAttributeString("xlink", "arcrole", XlinkNamespace, RolePrefix + role);
            xml.WriteEndElement();
        }

        private void Simple(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                xml.WriteElementString(name, value);
            }
        }

        private void Simple(string name, int? value) =>
            Simple(name, value?.ToString(Invariant));

        private void Simple(string name, double? value) =>
            Simple(name, value is { } v ? Number(v) : null);

        private void Simple(string name, DateTimeOffset? value) =>
            Simple(name, value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", Invariant));

        private void Extras(IReadOnlyDictionary<string, string> extras)
        {
            foreach (var (name, value) in extras.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                Simple(name, value);
            }
        }

        private static string Number(double value) => value.ToString("R", Invariant);

        private static string Pos(GeoPosition position) => Pos(position.Latitude, position.Longitude);

        private static string Pos(double latitude, double longitude) => Number(latitude) + " " + Number(longitude);
    }
}
