using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>
/// Assembles an <see cref="S101Document"/> feature by feature: interns the
/// feature and attribute catalogues, numbers records, and encodes
/// geometry as S-100 Part 10a spatial records.
/// </summary>
/// <remarks>
/// Each ring and line becomes one curve record whose begin and end point
/// records carry its first and last vertex (a ring's begin and end are the
/// same point). Surfaces reference their rings as exterior (usage 1) and
/// interior (usage 2). Records are numbered in the order they're added, so
/// the same calls give the same document.
/// </remarks>
public sealed class S101DocumentBuilder
{
    /// <summary>Coordinate multiplication factor for latitude and longitude (1e-7 degrees).</summary>
    public const uint CoordinateFactor = 10_000_000;

    /// <summary>Coordinate multiplication factor for sounding depths (decimetres).</summary>
    public const uint DepthFactor = 10;

    private readonly Dictionary<ushort, string> _featureTypes = new();
    private readonly Dictionary<ushort, string> _attributeTypes = new();
    private readonly Dictionary<uint, S101PointRecord> _points = new();
    private readonly Dictionary<uint, S101MultiPointRecord> _multiPoints = new();
    private readonly Dictionary<uint, S101CurveSegmentRecord> _curves = new();
    private readonly Dictionary<uint, S101SurfaceRecord> _surfaces = new();
    private readonly List<S101FeatureRecord> _features = new();
    private readonly ushort _agency;
    private readonly ushort _subdivision;
    private uint _nextRecord = 1;
    private uint _nextFeatureId = 1;

    /// <summary>Creates a builder whose features carry FOIDs <c>agency:n.subdivision</c>.</summary>
    /// <param name="agency">The producing agency code in every FOID.</param>
    /// <param name="subdivision">The FOID subdivision, unique per cell in an exchange set.</param>
    public S101DocumentBuilder(ushort agency, ushort subdivision)
    {
        _agency = agency;
        _subdivision = subdivision;
    }

    /// <summary>An attribute to add: a simple value, or a complex one with sub-attributes.</summary>
    /// <param name="Code">The attribute's FC code, such as <c>"depthRangeMinimumValue"</c>.</param>
    /// <param name="Value">The value (empty for a complex attribute).</param>
    /// <param name="SubAttributes">The sub-attributes of a complex attribute, or <c>null</c>.</param>
    public sealed record FeatureAttribute(string Code, string Value, IReadOnlyList<FeatureAttribute>? SubAttributes = null)
    {
        /// <summary>A simple numeric attribute, formatted with the invariant culture.</summary>
        /// <param name="code">The FC code.</param>
        /// <param name="value">The value.</param>
        /// <returns>The attribute.</returns>
        public static FeatureAttribute Number(string code, double value) => new(code, value.ToString("0.###", CultureInfo.InvariantCulture));

        /// <summary>A <c>featureName</c> complex attribute with one English name.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The attribute.</returns>
        public static FeatureAttribute FeatureName(string name) =>
            new("featureName", "", [new FeatureAttribute("name", name), new FeatureAttribute("language", "eng")]);
    }

    /// <summary>Adds a surface feature.</summary>
    /// <param name="featureType">The FC feature type, such as <c>"DepthArea"</c>.</param>
    /// <param name="exterior">The closed exterior ring, clockwise.</param>
    /// <param name="holes">The closed interior rings, anticlockwise.</param>
    /// <param name="attributes">The attributes.</param>
    public void AddSurface(string featureType, IReadOnlyList<GeoPosition> exterior, IReadOnlyList<IReadOnlyList<GeoPosition>> holes, params FeatureAttribute[] attributes)
    {
        var rings = new List<S101RingAssociation> { new(120, AddCurve(exterior), 1, 1) };
        foreach (var hole in holes)
        {
            rings.Add(new S101RingAssociation(120, AddCurve(hole), 1, 2));
        }

        var surfaceId = _nextRecord++;
        _surfaces[surfaceId] = new S101SurfaceRecord { RecordId = surfaceId, RingAssociations = rings, RecordVersion = 1 };
        AddFeature(featureType, new S101SpatialAssociation(130, surfaceId, 1), attributes);
    }

    /// <summary>Adds a curve feature.</summary>
    /// <param name="featureType">The FC feature type, such as <c>"DepthContour"</c>.</param>
    /// <param name="line">The line's vertices (at least two).</param>
    /// <param name="attributes">The attributes.</param>
    public void AddCurve(string featureType, IReadOnlyList<GeoPosition> line, params FeatureAttribute[] attributes) =>
        AddFeature(featureType, new S101SpatialAssociation(120, AddCurve(line), 1), attributes);

    /// <summary>Adds a point feature, such as a <c>Wreck</c>, on one point record.</summary>
    /// <param name="featureType">The FC feature type.</param>
    /// <param name="position">The position.</param>
    /// <param name="attributes">The attributes.</param>
    public void AddPoint(string featureType, GeoPosition position, params FeatureAttribute[] attributes) =>
        AddFeature(featureType, new S101SpatialAssociation(110, AddPoint((Scale(position.Latitude), Scale(position.Longitude))), 1), attributes);

    /// <summary>Adds a <c>Sounding</c> feature on one multipoint record.</summary>
    /// <param name="soundings">Positions with their depths in metres.</param>
    /// <param name="attributes">The attributes.</param>
    public void AddSoundings(IReadOnlyList<(GeoPosition Position, double Depth)> soundings, params FeatureAttribute[] attributes)
    {
        var id = _nextRecord++;
        _multiPoints[id] = new S101MultiPointRecord
        {
            RecordId = id,
            RecordVersion = 1,
            Points = soundings.Select(s => (Scale(s.Position.Latitude), Scale(s.Position.Longitude), (int)Math.Round(s.Depth * DepthFactor))).ToList(),
        };
        AddFeature("Sounding", new S101SpatialAssociation(115, id, 1), attributes);
    }

    /// <summary>Builds the document.</summary>
    /// <param name="identification">The dataset identification (DSID).</param>
    /// <returns>The document.</returns>
    public S101Document Build(S101DatasetIdentification identification) => new()
    {
        Identification = identification,
        StructureInfo = new S101DatasetStructureInfo
        {
            CoordinateMultiplicationFactorX = CoordinateFactor,
            CoordinateMultiplicationFactorY = CoordinateFactor,
            CoordinateMultiplicationFactorZ = DepthFactor,
        },
        FeatureTypeCatalogue = _featureTypes,
        AttributeTypeCatalogue = _attributeTypes,
        Points = _points,
        MultiPoints = _multiPoints,
        CurveSegments = _curves,
        CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
        Surfaces = _surfaces,
        Features = _features,
        InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
        InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
    };

    private void AddFeature(string featureType, S101SpatialAssociation spatial, FeatureAttribute[] attributes)
    {
        var rows = new List<S101Attribute>();
        foreach (var attribute in attributes)
        {
            AddAttribute(rows, attribute, parent: 0);
        }

        _features.Add(new S101FeatureRecord
        {
            RecordId = _nextRecord++,
            FeatureTypeCode = Intern(_featureTypes, featureType),
            ProducingAgency = _agency,
            FeatureIdentificationNumber = _nextFeatureId++,
            FeatureIdentificationSubdivision = _subdivision,
            RecordVersion = 1,
            Attributes = rows,
            SpatialAssociations = [spatial],
        });
    }

    // Complex attributes are written in pre-order with an empty value; each
    // sub-attribute's PAIX is its parent's 1-based row position (S-100 Part
    // 10a, ATTR field).
    private void AddAttribute(List<S101Attribute> rows, FeatureAttribute attribute, ushort parent)
    {
        rows.Add(new S101Attribute(Intern(_attributeTypes, attribute.Code), 1, attribute.Value, parent));
        var position = (ushort)rows.Count;
        foreach (var sub in attribute.SubAttributes ?? [])
        {
            AddAttribute(rows, sub, position);
        }
    }

    private uint AddCurve(IReadOnlyList<GeoPosition> vertices)
    {
        var scaled = Quantise(vertices);
        if (scaled.Count < 2)
        {
            throw new ArgumentException("A curve needs at least two distinct vertices.", nameof(vertices));
        }

        var closed = scaled[0] == scaled[^1];
        var begin = AddPoint(scaled[0]);
        var end = closed ? begin : AddPoint(scaled[^1]);
        var id = _nextRecord++;
        _curves[id] = new S101CurveSegmentRecord
        {
            RecordId = id,
            RecordVersion = 1,
            PointAssociations = [new S101PointAssociation(110, begin, 1), new S101PointAssociation(110, end, 2)],
            IntermediateCoordinates = scaled.Skip(1).Take(scaled.Count - 2).ToList(),
        };
        return id;
    }

    private uint AddPoint((int Y, int X) position)
    {
        var id = _nextRecord++;
        _points[id] = new S101PointRecord { RecordId = id, RecordVersion = 1, Y = position.Y, X = position.X };
        return id;
    }

    /// <summary>
    /// The vertices as encoded coordinates, without the repeats that rounding
    /// to 1e-7 degrees can create.
    /// </summary>
    /// <param name="vertices">The vertices.</param>
    /// <returns>The encoded (Y, X) pairs.</returns>
    public static List<(int Y, int X)> Quantise(IReadOnlyList<GeoPosition> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var result = new List<(int Y, int X)>(vertices.Count);
        foreach (var v in vertices)
        {
            var p = (Scale(v.Latitude), Scale(v.Longitude));
            if (result.Count == 0 || result[^1] != p)
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static ushort Intern(Dictionary<ushort, string> catalogue, string code)
    {
        foreach (var (key, value) in catalogue)
        {
            if (value == code)
            {
                return key;
            }
        }

        var next = (ushort)(catalogue.Count + 1);
        catalogue[next] = code;
        return next;
    }

    private static int Scale(double degrees) => (int)Math.Round(degrees * CoordinateFactor, MidpointRounding.AwayFromZero);
}
