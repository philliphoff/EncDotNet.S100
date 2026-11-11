using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Routes.Rtz;

/// <summary>Writes an <see cref="RtzRoute"/> as RTZ 1.0, 1.1 or 1.2.</summary>
/// <remarks>
/// <para>
/// The writer follows the target version's schema: the root namespace and
/// <c>version</c>, the schedule element's spelling (<c>sheduleElement</c> in
/// 1.0), windows and stay as <c>xsd:time</c> (1.0) or <c>xsd:duration</c>,
/// and a <c>revision</c> on every waypoint in 1.2 (0 when unset). An empty
/// route name is written as "Route", since <c>routeName</c> is mandatory.
/// </para>
/// <para>
/// Extra attributes and extensions are written back. Extension elements in
/// another RTZ version's namespace move to the target's. RTZ 1.1 and 1.2 hold
/// extensions in <c>extension</c> elements with a manufacturer and name, so
/// a bare extension element read from a 1.0 file is wrapped in one with
/// manufacturer "unknown" and its own local name; a 1.0 file takes extension
/// elements as they are. Only RTZ 1.2 lets a leg carry extensions, so they
/// are left out of 1.0 and 1.1.
/// </para>
/// </remarks>
public static class RtzWriter
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Writes <paramref name="route"/> to <paramref name="stream"/> as UTF-8.</summary>
    /// <param name="route">The route.</param>
    /// <param name="stream">Where to write; left open.</param>
    /// <param name="version">The RTZ version to write; the route's own when <c>null</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> isn't supported.</exception>
    public static void Write(RtzRoute route, Stream stream, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = ToDocument(route, version);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            CloseOutput = false,
        });
        document.Save(writer);
    }

    /// <summary>Writes <paramref name="route"/> to the file at <paramref name="path"/>.</summary>
    /// <param name="route">The route.</param>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="version">The RTZ version to write; the route's own when <c>null</c>.</param>
    public static void Write(RtzRoute route, string path, string? version = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.Create(path);
        Write(route, stream, version);
    }

    /// <summary>The RTZ text of <paramref name="route"/>.</summary>
    /// <param name="route">The route.</param>
    /// <param name="version">The RTZ version to write; the route's own when <c>null</c>.</param>
    /// <returns>The XML.</returns>
    public static string ToXml(RtzRoute route, string? version = null)
    {
        using var stream = new MemoryStream();
        Write(route, stream, version);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The RTZ document of <paramref name="route"/>.</summary>
    /// <param name="route">The route.</param>
    /// <param name="version">The RTZ version to write; the route's own when <c>null</c>.</param>
    /// <returns>The document.</returns>
    public static XDocument ToDocument(RtzRoute route, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        version ??= RtzVersions.IsSupported(route.Version) ? route.Version : RtzVersions.Latest;
        if (!RtzVersions.IsSupported(version))
            throw new ArgumentException($"RTZ {version} isn't supported; use {string.Join(", ", RtzVersions.Supported)}.", nameof(version));
        return new Writer(version).Document(route);
    }

    private sealed class Writer(string version)
    {
        private readonly XNamespace _ns = RtzVersions.NamespaceOf(version);
        private readonly bool _v10 = version == RtzVersions.V1_0;

        public XDocument Document(RtzRoute route)
        {
            var root = new XElement(_ns + "route",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XAttribute("version", version));
            foreach (var (prefix, uri) in route.NamespaceDeclarations)
            {
                if (prefix != "xsi" && !RtzVersions.IsRtzNamespace(uri))
                    root.Add(new XAttribute(XNamespace.Xmlns + prefix, uri));
            }
            AddExtras(root, route.ExtraAttributes);

            root.Add(Info(route.Info));
            var waypoints = new XElement(_ns + "waypoints");
            if (route.DefaultWaypoint is { } defaults)
                waypoints.Add(Default(defaults));
            foreach (var waypoint in route.Waypoints)
                waypoints.Add(Waypoint(waypoint));
            AddExtensions(waypoints, route.WaypointsExtensions);
            root.Add(waypoints);

            if (route.Schedules.Count > 0 || route.SchedulesExtensions.Count > 0)
            {
                var schedules = new XElement(_ns + "schedules", route.Schedules.Select(Schedule));
                AddExtensions(schedules, route.SchedulesExtensions);
                root.Add(schedules);
            }
            AddExtensions(root, route.Extensions);
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        }

        private XElement Info(RtzRouteInfo info)
        {
            var e = new XElement(_ns + "routeInfo",
                Attr("routeName", string.IsNullOrWhiteSpace(info.RouteName) ? "Route" : info.RouteName),
                Attr("routeAuthor", info.RouteAuthor),
                Attr("routeStatus", info.RouteStatus),
                Attr("validityPeriodStart", info.ValidityPeriodStart),
                Attr("validityPeriodStop", info.ValidityPeriodStop),
                Attr("vesselName", info.VesselName),
                Attr("vesselMMSI", info.VesselMmsi),
                Attr("vesselIMO", info.VesselImo),
                Attr("vesselVoyage", info.VesselVoyage),
                Attr("vesselDisplacement", info.VesselDisplacement),
                Attr("vesselCargo", info.VesselCargo),
                Attr("vesselGM", info.VesselGm),
                Attr("optimizationMethod", info.OptimizationMethod),
                Attr("vesselMaxRoll", info.VesselMaxRoll),
                Attr("vesselMaxWave", info.VesselMaxWave),
                Attr("vesselMaxWind", info.VesselMaxWind),
                Attr("vesselSpeedMax", info.VesselSpeedMax),
                Attr("vesselServiceMin", info.VesselServiceMin),
                Attr("vesselServiceMax", info.VesselServiceMax),
                Attr("routeChangesHistory", info.RouteChangesHistory));
            AddExtras(e, info.ExtraAttributes);
            AddExtensions(e, info.Extensions);
            return e;
        }

        private XElement Default(RtzDefaultWaypoint defaults)
        {
            var e = new XElement(_ns + "defaultWaypoint", Attr("radius", defaults.Radius));
            AddExtras(e, defaults.ExtraAttributes);
            if (defaults.Leg is { } leg)
                e.Add(Leg(leg));
            AddExtensions(e, defaults.Extensions);
            return e;
        }

        private XElement Waypoint(RtzWaypoint waypoint)
        {
            var e = new XElement(_ns + "waypoint",
                Attr("id", (long)waypoint.Id),
                Attr("revision", waypoint.Revision is { } r ? (long?)r : version == RtzVersions.V1_2 ? 0L : null),
                Attr("name", waypoint.Name),
                Attr("radius", waypoint.Radius));
            AddExtras(e, waypoint.ExtraAttributes);
            e.Add(new XElement(_ns + "position",
                Attr("lat", waypoint.Position.Latitude),
                Attr("lon", waypoint.Position.Longitude)));
            if (waypoint.Leg is { } leg)
                e.Add(Leg(leg));
            AddExtensions(e, waypoint.Extensions);
            return e;
        }

        private XElement Leg(RtzLeg leg)
        {
            var e = new XElement(_ns + "leg",
                Attr("starboardXTD", leg.StarboardXtd),
                Attr("portsideXTD", leg.PortsideXtd),
                Attr("safetyContour", leg.SafetyContour),
                Attr("safetyDepth", leg.SafetyDepth),
                Attr("geometryType", leg.GeometryType?.ToString()),
                Attr("speedMin", leg.SpeedMin),
                Attr("speedMax", leg.SpeedMax),
                Attr("draughtForward", leg.DraughtForward),
                Attr("draughtAft", leg.DraughtAft),
                Attr("staticUKC", leg.StaticUkc),
                Attr("dynamicUKC", leg.DynamicUkc),
                Attr("masthead", leg.Masthead),
                Attr("legReport", leg.LegReport),
                Attr("legInfo", leg.LegInfo),
                Attr("legNote1", leg.LegNote1),
                Attr("legNote2", leg.LegNote2));
            AddExtras(e, leg.ExtraAttributes);
            if (version == RtzVersions.V1_2)
                AddExtensions(e, leg.Extensions);
            return e;
        }

        private XElement Schedule(RtzSchedule schedule)
        {
            var e = new XElement(_ns + "schedule", Attr("id", (long)schedule.Id), Attr("name", schedule.Name));
            AddExtras(e, schedule.ExtraAttributes);
            if (schedule.Manual is { } manual)
                e.Add(Table("manual", manual));
            if (schedule.Calculated is { } calculated)
                e.Add(Table("calculated", calculated));
            AddExtensions(e, schedule.Extensions);
            return e;
        }

        private XElement Table(string name, RtzScheduleTable table)
        {
            var e = new XElement(_ns + name, table.Elements.Select(Element));
            AddExtensions(e, table.Extensions);
            return e;
        }

        private XElement Element(RtzScheduleElement element)
        {
            var e = new XElement(_ns + (_v10 ? "sheduleElement" : "scheduleElement"),
                Attr("waypointId", (long)element.WaypointId),
                Attr("etd", element.Etd),
                Attr("etdWindowBefore", Duration(element.EtdWindowBefore)),
                Attr("etdWindowAfter", Duration(element.EtdWindowAfter)),
                Attr("eta", element.Eta),
                Attr("etaWindowBefore", Duration(element.EtaWindowBefore)),
                Attr("etaWindowAfter", Duration(element.EtaWindowAfter)),
                Attr("stay", Duration(element.Stay)),
                Attr("speed", element.Speed),
                Attr("speedWindow", element.SpeedWindow),
                Attr("windSpeed", element.WindSpeed),
                Attr("windDirection", element.WindDirection),
                Attr("currentSpeed", element.CurrentSpeed),
                Attr("currentDirection", element.CurrentDirection),
                Attr("windLoss", element.WindLoss),
                Attr("waveLoss", element.WaveLoss),
                Attr("totalLoss", element.TotalLoss),
                Attr("rpm", element.Rpm),
                Attr("pitch", element.Pitch),
                Attr("fuel", element.Fuel),
                Attr("relFuelSave", element.RelFuelSave),
                // The 1.0 schema spells it absFuelSace.
                Attr(_v10 ? "absFuelSace" : "absFuelSave", element.AbsFuelSave),
                Attr("Note", element.Note));
            AddExtras(e, element.ExtraAttributes);
            AddExtensions(e, element.Extensions);
            return e;
        }

        // xsd:time in 1.0 (which can't hold a day or more, so longer spans
        // fall back to a duration), xsd:duration from 1.1.
        private string? Duration(TimeSpan? span)
        {
            if (span is not { } s)
                return null;
            if (_v10 && s >= TimeSpan.Zero && s < TimeSpan.FromDays(1))
                return s.ToString(s.Ticks % TimeSpan.TicksPerSecond == 0 ? "hh\\:mm\\:ss" : "hh\\:mm\\:ss\\.FFFFFFF", CultureInfo.InvariantCulture);
            return XmlConvert.ToString(s);
        }

        private static void AddExtras(XElement e, IReadOnlyDictionary<XName, string> extras)
        {
            foreach (var (name, value) in extras)
            {
                if (e.Attribute(name) is null)
                    e.Add(new XAttribute(name, value));
            }
        }

        private void AddExtensions(XElement e, IReadOnlyList<XElement> extensions)
        {
            if (extensions.Count == 0)
                return;
            e.Add(new XElement(_ns + "extensions", extensions.Select(Extension)));
        }

        private XElement Extension(XElement source)
        {
            var copy = Retarget(new XElement(source));
            if (_v10 || copy.Name == _ns + "extension")
                return copy;
            return new XElement(_ns + "extension",
                new XAttribute("manufacturer", "unknown"),
                new XAttribute("name", copy.Name.LocalName),
                copy);
        }

        // Elements in any RTZ namespace move to the target version's.
        private XElement Retarget(XElement e)
        {
            foreach (var element in e.DescendantsAndSelf())
            {
                if (RtzVersions.IsRtzNamespace(element.Name.Namespace))
                    element.Name = _ns + element.Name.LocalName;
                foreach (var declaration in element.Attributes().Where(a => a.IsNamespaceDeclaration && RtzVersions.IsRtzNamespace(a.Value)).ToList())
                    declaration.Remove();
            }
            return e;
        }

        private static XAttribute? Attr(string name, string? value) => value is null ? null : new XAttribute(name, value);

        private static XAttribute? Attr(string name, double? value)
            => value is { } v ? new XAttribute(name, v.ToString("R", CultureInfo.InvariantCulture)) : null;

        private static XAttribute? Attr(string name, long? value)
            => value is { } v ? new XAttribute(name, v.ToString(CultureInfo.InvariantCulture)) : null;

        private static XAttribute? Attr(string name, DateTimeOffset? value)
            => value is { } v ? new XAttribute(name, v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)) : null;
    }
}
