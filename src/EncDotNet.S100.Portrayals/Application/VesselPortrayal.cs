using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The code-defined portrayal of a vessel: own-ship or an AIS target. It
/// draws, as S-100 Part 9 drawing instructions:
/// <list type="bullet">
/// <item><description>a course and speed vector: six minutes ahead at the
/// speed over ground, at most 10 NM, with an arrowhead;</description></item>
/// <item><description>when the feature carries
/// <see cref="DynamicFeature.VesselGeometry"/>, the true-scale outline and a
/// consistent common reference point (CCRP) cross while the hull is at least
/// <see cref="MinVesselPixels"/> long on screen, and a disc further out;</description></item>
/// <item><description>a disc when it has no geometry.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The course vector, arrowhead and outline are augmented geometry (S-100
/// Part 9 §11.5), so they draw and pick as the vessel's own feature. The
/// outline is drawn as a line, as IHO S-52 draws a true-scale vessel outline.
/// </para>
/// <para>
/// The hull and the disc switch at a scale denominator set by the vessel's
/// length: the outline shows where a pixel (0.28 mm) covers at most
/// <c>length / 22</c> metres, so the switch is the same at every latitude.
/// </para>
/// <para>
/// Each vessel class has its own pair of palette tokens, <c>VSL{class}</c>
/// for the stroke and <c>VSL{class}F</c> for the fill, and its own symbols:
/// <c>VSLDISC_{class}</c>, <c>VSLARRW_{class}</c> and <c>VSLCCRP_{class}</c>.
/// </para>
/// </remarks>
public sealed class VesselPortrayal : IDynamicFeaturePortrayal
{
    /// <summary>The shortest on-screen hull length, in pixels, that draws the outline rather than the disc.</summary>
    public const double MinVesselPixels = 22.0;

    /// <summary>The fraction of the hull length from the stern to where the bow starts to taper.</summary>
    public const double BowTaperRatio = 0.7;

    /// <summary>How far ahead the course vector reaches, in seconds at the speed over ground.</summary>
    public const double CourseVectorSeconds = 360.0;

    /// <summary>The course vector's longest reach, in metres (10 NM).</summary>
    public const double CourseVectorMaxMetres = 18_520.0;

    /// <summary>The class of own-ship.</summary>
    public const string OwnShipClass = "OWN";

    /// <summary>The prefix of an AIS target's <see cref="DynamicFeature.Kind"/> (<c>vessel.ais.{class}</c>).</summary>
    public const string AisKindPrefix = "vessel.ais.";

    // S-100 display pixel and the nominal pixel the scale denominator is reckoned in.
    private const double DisplayPixelMillimetres = 0.32;
    private const double ScalePixelMetres = 0.00028;

    private static readonly IReadOnlyDictionary<string, (string Stroke, string Fill)> ClassColours =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [OwnShipClass] = ("#007ACC", "#66B3E6"),
            ["DEF"] = ("#00A040", "#66CC88"),
            ["TNK"] = ("#CC3333", "#E69999"),
            ["PAS"] = ("#008080", "#66C2C2"),
            ["HSC"] = ("#8033CC", "#C299E6"),
            ["UNK"] = ("#666666", "#B3B3B3"),
        };

    private readonly Func<DynamicFeature, string?> _classOf;
    private readonly InstructionBuilder _vector = new() { DrawingPriority = 1 };
    private readonly InstructionBuilder _vessel = new() { DrawingPriority = 2 };

    private VesselPortrayal(Func<DynamicFeature, string?> classOf)
    {
        _classOf = classOf;
        SymbolProvider = ResolveSymbol;
    }

    /// <summary>
    /// The own-ship portrayal: any point feature, drawn in the own-ship blue.
    /// </summary>
    public static VesselPortrayal OwnShip { get; } = new(_ => OwnShipClass);

    /// <summary>
    /// The AIS target portrayal: point features whose kind starts with
    /// <see cref="AisKindPrefix"/>, coloured by ship type class (tankers red,
    /// passenger teal, high-speed craft purple, unknown grey, every other
    /// class green).
    /// </summary>
    public static VesselPortrayal Ais { get; } = new(AisClassOf);

    /// <summary>The colours of every vessel class's stroke and fill tokens.</summary>
    public static ApplicationPalette VesselPalette { get; } = BuildPalette();

    /// <inheritdoc />
    public ApplicationPalette Palette => VesselPalette;

    /// <inheritdoc />
    public Func<string, string?> SymbolProvider { get; }

    /// <inheritdoc />
    public bool CanPortray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.GeometryType == GeometryType.Point
            && feature.Coordinates.Count > 0
            && _classOf(feature) is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!CanPortray(feature))
            return [];

        var cls = _classOf(feature)!;
        var stroke = "VSL" + cls;
        var position = feature.Coordinates[0];
        var heading = (feature.Motion?.Heading ?? feature.Motion?.CourseOverGround)?.TotalDegrees;
        var knots = feature.Motion?.SpeedOverGround?.TotalKnots ?? 0.0;
        var instructions = new List<DrawingInstruction>(5);

        if (heading is { } bearing && knots > 0.0)
        {
            var reach = Math.Min(knots * 1852.0 / 3600.0 * CourseVectorSeconds, CourseVectorMaxMetres);
            var end = Destination(position, bearing, reach);
            instructions.Add(_vector.Path(feature.Id, [position, end], stroke, 1.5 * DisplayPixelMillimetres));
            instructions.Add(_vector.PointAt(feature.Id, "VSLARRW_" + cls, end, bearing, SymbolRotationCrs.Geographic));
        }

        if (feature.VesselGeometry is { LengthMetres: > 0, BeamMetres: > 0 } geometry)
        {
            var hullScale = geometry.LengthMetres / MinVesselPixels / ScalePixelMetres;
            var hull = _vessel with { ScaleMinimum = hullScale };
            instructions.Add(hull.Path(feature.Id, Outline(position, geometry, heading ?? 0.0), stroke, 1.5 * DisplayPixelMillimetres));
            instructions.Add(hull.Point(feature.Id, "VSLCCRP_" + cls, heading ?? 0.0, SymbolRotationCrs.Geographic));
            instructions.Add((_vessel with { ScaleMaximum = hullScale }).Point(feature.Id, "VSLDISC_" + cls));
        }
        else
        {
            instructions.Add(_vessel.Point(feature.Id, "VSLDISC_" + cls));
        }

        return instructions;
    }

    /// <summary>
    /// The class token of an AIS target's kind: <c>TNK</c>, <c>PAS</c>,
    /// <c>HSC</c>, <c>DEF</c> for the other known classes, <c>UNK</c>
    /// otherwise; <see langword="null"/> when it isn't an AIS target.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public static string? AisClassOf(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Kind is not { } kind || !kind.StartsWith(AisKindPrefix, StringComparison.Ordinal))
            return null;
        return kind[AisKindPrefix.Length..] switch
        {
            "tanker" => "TNK",
            "passenger" => "PAS",
            "highspeedcraft" => "HSC",
            "cargo" or "fishing" or "tug" or "pleasure" or "sailing"
                or "pilot" or "sar" or "lawenforcement" or "military"
                or "other" => "DEF",
            _ => "UNK",
        };
    }

    // The hull outline around the antenna (the CCRP), heading up: square
    // stern, straight sides to BowTaperRatio of the length, a point at the bow.
    private static IReadOnlyList<GeoPosition> Outline(GeoPosition antenna, DynamicVesselGeometry geometry, double headingDegrees)
    {
        var antennaX = -geometry.BeamMetres / 2.0 + geometry.PortOffsetMetres;
        var antennaY = geometry.LengthMetres - geometry.BowOffsetMetres;
        var halfBeam = geometry.BeamMetres / 2.0;
        var taper = geometry.LengthMetres * BowTaperRatio;
        (double X, double Y)[] local =
        [
            (0, geometry.LengthMetres),
            (halfBeam, taper),
            (halfBeam, 0),
            (-halfBeam, 0),
            (-halfBeam, taper),
        ];

        var theta = headingDegrees * Math.PI / 180.0;
        var (sin, cos) = Math.SinCos(theta);
        var ring = new GeoPosition[local.Length + 1];
        for (var i = 0; i < local.Length; i++)
        {
            var dx = local[i].X - antennaX;
            var dy = local[i].Y - antennaY;
            var east = dx * cos + dy * sin;
            var north = -dx * sin + dy * cos;
            ring[i] = Offset(antenna, east, north);
        }
        ring[^1] = ring[0];
        return ring;
    }

    private static GeoPosition Offset(GeoPosition origin, double eastMetres, double northMetres)
    {
        const double metresPerDegree = 111_320.0;
        var latitude = origin.Latitude + northMetres / metresPerDegree;
        var longitude = origin.Longitude + eastMetres / (metresPerDegree * Math.Cos(origin.Latitude * Math.PI / 180.0));
        return new GeoPosition(latitude, longitude);
    }

    private static GeoPosition Destination(GeoPosition start, double bearingDegrees, double distanceMetres)
    {
        const double earthRadiusMetres = 6_371_008.8;
        var delta = distanceMetres / earthRadiusMetres;
        var theta = bearingDegrees * Math.PI / 180.0;
        var phi1 = start.Latitude * Math.PI / 180.0;
        var lambda1 = start.Longitude * Math.PI / 180.0;
        var sinPhi2 = Math.Sin(phi1) * Math.Cos(delta) + Math.Cos(phi1) * Math.Sin(delta) * Math.Cos(theta);
        var phi2 = Math.Asin(sinPhi2);
        var lambda2 = lambda1 + Math.Atan2(
            Math.Sin(theta) * Math.Sin(delta) * Math.Cos(phi1),
            Math.Cos(delta) - Math.Sin(phi1) * sinPhi2);
        return new GeoPosition(phi2 * 180.0 / Math.PI, ((lambda2 * 180.0 / Math.PI) + 540.0) % 360.0 - 180.0);
    }

    private static ApplicationPalette BuildPalette()
    {
        var tokens = new Dictionary<string, PaletteColors>(StringComparer.Ordinal);
        foreach (var (cls, (stroke, fill)) in ClassColours)
        {
            tokens["VSL" + cls] = PaletteColors.FromHex(day: stroke, dusk: stroke, night: Dim(stroke));
            tokens["VSL" + cls + "F"] = PaletteColors.FromHex(day: fill, dusk: fill, night: Dim(fill));
        }
        return new ApplicationPalette("VESSEL", tokens);
    }

    // Night colours at 55 % brightness, so vessels don't glare on a dark chart.
    private static string Dim(string hex)
    {
        var r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"#{(int)(r * 0.55):X2}{(int)(g * 0.55):X2}{(int)(b * 0.55):X2}");
    }

    // VSLDISC_{class}, VSLARRW_{class} and VSLCCRP_{class}, in millimetres,
    // north up, pivoting on the origin.
    private static string? ResolveSymbol(string name)
    {
        var separator = name.IndexOf('_', StringComparison.Ordinal);
        if (separator < 0 || !ClassColours.ContainsKey(name[(separator + 1)..]))
            return null;
        var stroke = "VSL" + name[(separator + 1)..];
        var fill = stroke + "F";
        var body = name[..separator] switch
        {
            "VSLDISC" => $"""<circle class="f{fill} s{stroke}" stroke-width="0.48" cx="0" cy="0" r="2.2"/>""",
            "VSLARRW" => $"""<path class="f{stroke} s{stroke}" stroke-width="0.32" d="M 0 -1.6 L 1.1 0.8 L -1.1 0.8 Z"/>""",
            "VSLCCRP" => $"""<path class="s{stroke}" fill="none" stroke-width="0.48" d="M -1.9 0 L 1.9 0 M 0 -1.9 L 0 1.9"/>""",
            _ => null,
        };
        return body is null
            ? null
            : $"""<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="6mm" height="6mm" viewBox="-3 -3 6 6"><title>{name}</title><rect class="symbolBox layout" fill="none" x="-3" y="-3" width="6" height="6"/>{body}<circle class="pivotPoint layout" fill="none" cx="0" cy="0" r="1"/></svg>""";
    }
}
