using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Routes.Rtz;

/// <summary>Reads RTZ route plans (versions 1.0, 1.1 and 1.2) into an <see cref="RtzRoute"/>.</summary>
/// <remarks>
/// <para>
/// The reader is lenient where the data allows: elements are matched in the
/// root's RTZ namespace, both schedule element spellings
/// (<c>sheduleElement</c>, <c>scheduleElement</c>) and both window forms
/// (<c>xsd:time</c>, <c>xsd:duration</c>) are accepted in every version, and
/// an attribute whose value can't be parsed is kept as an extra attribute
/// rather than failing the file. Only a file that isn't an RTZ route, or a
/// waypoint without a position or id, is an error.
/// </para>
/// <para>
/// The <c>defaultWaypoint</c> is read as it is; it isn't applied to the
/// waypoints (see <see cref="RtzRoute.GetEffectiveLeg"/>).
/// </para>
/// </remarks>
public static class RtzReader
{
    // The writer always declares xsi itself.
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Reads the RTZ file at <paramref name="path"/>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The route.</returns>
    /// <exception cref="RtzFormatException">The file isn't an RTZ route.</exception>
    public static RtzRoute Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads an RTZ route from <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream; left open.</param>
    /// <returns>The route.</returns>
    /// <exception cref="RtzFormatException">The content isn't an RTZ route.</exception>
    public static RtzRoute Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, CloseInput = false });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new RtzFormatException("The file isn't well-formed XML.", ex);
        }
        return Read(document);
    }

    /// <summary>Reads an RTZ route from a loaded document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The route.</returns>
    /// <exception cref="RtzFormatException">The document isn't an RTZ route.</exception>
    public static RtzRoute Read(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "route" || RtzVersions.VersionOf(root.Name.Namespace) is not { } nsVersion)
            throw new RtzFormatException("The document's root isn't an RTZ route element.");
        var ns = root.Name.Namespace;

        var attrs = new Attrs(root);
        var version = attrs.String("version") ?? nsVersion;
        var info = root.Element(ns + "routeInfo") is { } infoElement ? ReadInfo(infoElement, ns) : new RtzRouteInfo();
        var waypointsElement = root.Element(ns + "waypoints")
            ?? throw new RtzFormatException("The route has no waypoints element.");
        var schedulesElement = root.Element(ns + "schedules");

        return new RtzRoute
        {
            Version = version,
            Info = info,
            DefaultWaypoint = waypointsElement.Element(ns + "defaultWaypoint") is { } d ? ReadDefault(d, ns) : null,
            Waypoints = waypointsElement.Elements(ns + "waypoint").Select(w => ReadWaypoint(w, ns)).ToList(),
            WaypointsExtensions = Extensions(waypointsElement, ns),
            Schedules = schedulesElement?.Elements(ns + "schedule").Select(s => ReadSchedule(s, ns)).ToList() ?? [],
            SchedulesExtensions = schedulesElement is null ? [] : Extensions(schedulesElement, ns),
            Extensions = Extensions(root, ns),
            ExtraAttributes = attrs.Rest,
            NamespaceDeclarations = root.Attributes()
                .Where(a => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns && !RtzVersions.IsRtzNamespace(a.Value) && a.Value != XsiNamespace)
                .ToDictionary(a => a.Name.LocalName, a => a.Value),
        };
    }

    private static RtzRouteInfo ReadInfo(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        return new RtzRouteInfo
        {
            RouteName = a.String("routeName"),
            RouteAuthor = a.String("routeAuthor"),
            RouteStatus = a.String("routeStatus"),
            ValidityPeriodStart = a.DateTime("validityPeriodStart"),
            ValidityPeriodStop = a.DateTime("validityPeriodStop"),
            VesselName = a.String("vesselName"),
            VesselMmsi = a.String("vesselMMSI"),
            VesselImo = a.String("vesselIMO"),
            VesselVoyage = a.String("vesselVoyage"),
            VesselDisplacement = a.Long("vesselDisplacement"),
            VesselCargo = a.Long("vesselCargo"),
            VesselGm = a.Double("vesselGM"),
            OptimizationMethod = a.String("optimizationMethod"),
            VesselMaxRoll = a.Long("vesselMaxRoll"),
            VesselMaxWave = a.Double("vesselMaxWave"),
            VesselMaxWind = a.Double("vesselMaxWind"),
            VesselSpeedMax = a.Double("vesselSpeedMax"),
            VesselServiceMin = a.Double("vesselServiceMin"),
            VesselServiceMax = a.Double("vesselServiceMax"),
            RouteChangesHistory = a.String("routeChangesHistory"),
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    private static RtzDefaultWaypoint ReadDefault(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        return new RtzDefaultWaypoint
        {
            Radius = a.Double("radius"),
            Leg = e.Element(ns + "leg") is { } leg ? ReadLeg(leg, ns) : null,
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    private static RtzWaypoint ReadWaypoint(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        var id = a.Int("id") ?? throw new RtzFormatException("A waypoint has no valid id.");
        var position = e.Element(ns + "position") ?? throw new RtzFormatException($"Waypoint {id} has no position.");
        var p = new Attrs(position);
        var lat = p.Double("lat") ?? throw new RtzFormatException($"Waypoint {id} has no valid latitude.");
        var lon = p.Double("lon") ?? throw new RtzFormatException($"Waypoint {id} has no valid longitude.");
        return new RtzWaypoint
        {
            Id = id,
            Revision = a.Int("revision"),
            Name = a.String("name"),
            Radius = a.Double("radius"),
            Position = new GeoPosition(lat, lon),
            Leg = e.Element(ns + "leg") is { } leg ? ReadLeg(leg, ns) : null,
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    private static RtzLeg ReadLeg(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        return new RtzLeg
        {
            StarboardXtd = a.Double("starboardXTD"),
            PortsideXtd = a.Double("portsideXTD"),
            SafetyContour = a.Double("safetyContour"),
            SafetyDepth = a.Double("safetyDepth"),
            GeometryType = a.Geometry("geometryType"),
            SpeedMin = a.Double("speedMin"),
            SpeedMax = a.Double("speedMax"),
            DraughtForward = a.Double("draughtForward"),
            DraughtAft = a.Double("draughtAft"),
            StaticUkc = a.Double("staticUKC"),
            DynamicUkc = a.Double("dynamicUKC"),
            Masthead = a.Double("masthead"),
            LegReport = a.String("legReport"),
            LegInfo = a.String("legInfo"),
            LegNote1 = a.String("legNote1"),
            LegNote2 = a.String("legNote2"),
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    private static RtzSchedule ReadSchedule(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        return new RtzSchedule
        {
            Id = a.Int("id") ?? 0,
            Name = a.String("name"),
            Manual = e.Element(ns + "manual") is { } manual ? ReadTable(manual, ns) : null,
            Calculated = e.Element(ns + "calculated") is { } calculated ? ReadTable(calculated, ns) : null,
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    private static RtzScheduleTable ReadTable(XElement e, XNamespace ns) => new()
    {
        Elements = e.Elements()
            .Where(c => c.Name.Namespace == ns && c.Name.LocalName is "scheduleElement" or "sheduleElement")
            .Select(c => ReadElement(c, ns))
            .ToList(),
        Extensions = Extensions(e, ns),
    };

    private static RtzScheduleElement ReadElement(XElement e, XNamespace ns)
    {
        var a = new Attrs(e);
        return new RtzScheduleElement
        {
            WaypointId = a.Int("waypointId") ?? throw new RtzFormatException("A schedule element has no valid waypointId."),
            Etd = a.DateTime("etd"),
            EtdWindowBefore = a.Duration("etdWindowBefore"),
            EtdWindowAfter = a.Duration("etdWindowAfter"),
            Eta = a.DateTime("eta"),
            EtaWindowBefore = a.Duration("etaWindowBefore"),
            EtaWindowAfter = a.Duration("etaWindowAfter"),
            Stay = a.Duration("stay"),
            Speed = a.Double("speed"),
            SpeedWindow = a.Double("speedWindow"),
            WindSpeed = a.Double("windSpeed"),
            WindDirection = a.Double("windDirection"),
            CurrentSpeed = a.Double("currentSpeed"),
            CurrentDirection = a.Double("currentDirection"),
            WindLoss = a.Double("windLoss"),
            WaveLoss = a.Double("waveLoss"),
            TotalLoss = a.Double("totalLoss"),
            Rpm = a.Long("rpm"),
            Pitch = a.Long("pitch"),
            Fuel = a.Double("fuel"),
            RelFuelSave = a.Double("relFuelSave"),
            AbsFuelSave = a.Double("absFuelSave") ?? a.Double("absFuelSace"),
            Note = a.String("Note"),
            Extensions = Extensions(e, ns),
            ExtraAttributes = a.Rest,
        };
    }

    // The children of the element's own extensions element, detached copies.
    private static List<XElement> Extensions(XElement e, XNamespace ns)
        => e.Element(ns + "extensions")?.Elements().Select(x => new XElement(x)).ToList() ?? [];

    /// <summary>
    /// The attributes of one element: each known one is taken out as it's
    /// read, and what's left (unknown, or unparsable) is kept verbatim.
    /// </summary>
    private sealed class Attrs
    {
        private readonly Dictionary<XName, string> _rest;

        public Attrs(XElement e)
            => _rest = e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name, a => a.Value);

        public IReadOnlyDictionary<XName, string> Rest => _rest.Count == 0 ? RtzAttributes.None : _rest;

        public string? String(string name) => _rest.Remove(name, out var value) ? value : null;

        public double? Double(string name) => Take<double>(name, v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : (double?)null);

        public long? Long(string name) => Take<long>(name, v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : (long?)null);

        public int? Int(string name) => Take<int>(name, v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : (int?)null);

        public DateTimeOffset? DateTime(string name) => Take<DateTimeOffset>(name, v =>
        {
            try
            {
                return XmlConvert.ToDateTimeOffset(v);
            }
            catch (FormatException)
            {
                return null;
            }
        });

        // xsd:duration (1.1, 1.2) or xsd:time "hh:mm:ss" (1.0).
        public TimeSpan? Duration(string name) => Take<TimeSpan>(name, v =>
        {
            var text = v.Trim();
            if (text.StartsWith('P') || text.StartsWith("-P", StringComparison.Ordinal))
            {
                try
                {
                    return XmlConvert.ToTimeSpan(text);
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            return TimeSpan.TryParseExact(text, ["hh\\:mm\\:ss", "hh\\:mm\\:ss\\.FFFFFFF", "hh\\:mm"], CultureInfo.InvariantCulture, out var t) ? t : null;
        });

        public RtzGeometryType? Geometry(string name) => Take<RtzGeometryType>(name, v => v.Trim() switch
        {
            "Loxodrome" => RtzGeometryType.Loxodrome,
            "Orthodrome" => RtzGeometryType.Orthodrome,
            _ => null,
        });

        private T? Take<T>(string name, Func<string, T?> parse)
            where T : struct
        {
            if (!_rest.TryGetValue(name, out var value))
                return null;
            var parsed = parse(value);
            if (parsed is not null)
                _rest.Remove(name);
            return parsed;
        }
    }
}

/// <summary>The content isn't an RTZ route this library can read.</summary>
public sealed class RtzFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What's wrong.</param>
    /// <param name="inner">The underlying failure, if any.</param>
    public RtzFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
