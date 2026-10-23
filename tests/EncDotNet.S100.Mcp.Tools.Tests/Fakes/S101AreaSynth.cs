using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

/// <summary>
/// Builds small S-101 cells with area features (one closed ring each, with
/// optional holes) carrying simple attributes, plus optional soundings, for
/// the passage-check depth fallback.
/// </summary>
internal static class S101AreaSynth
{
    private const int Cmf = 10_000_000;
    private const int CmfZ = 10;

    /// <summary>One area feature.</summary>
    public sealed record AreaFeature(
        string FeatureType,
        (double Lat, double Lon)[] Ring,
        IReadOnlyDictionary<string, string> Attributes,
        (double Lat, double Lon)[]? Hole = null);

    public static AreaFeature DepthArea(double drval1, (double Lat, double Lon)[] ring, (double Lat, double Lon)[]? hole = null) =>
        new("DepthArea", ring, new Dictionary<string, string> { ["depthRangeMinimumValue"] = Format(drval1) }, hole);

    public static AreaFeature DredgedArea(double drval1, (double Lat, double Lon)[] ring) =>
        new("DredgedArea", ring, new Dictionary<string, string> { ["depthRangeMinimumValue"] = Format(drval1) });

    public static AreaFeature SoundingDatum(int code, (double Lat, double Lon)[] ring) =>
        new("SoundingDatum", ring, new Dictionary<string, string> { ["verticalDatum"] = code.ToString(CultureInfo.InvariantCulture) });

    public static AreaFeature DataCoverage(int minimumDisplayScale, (double Lat, double Lon)[] ring) =>
        new("DataCoverage", ring, new Dictionary<string, string> { ["minimumDisplayScale"] = minimumDisplayScale.ToString(CultureInfo.InvariantCulture) });

    /// <summary>A coverage with no display scale, as a translated S-57 <c>M_COVR</c> has.</summary>
    public static AreaFeature DataCoverage((double Lat, double Lon)[] ring) =>
        new("DataCoverage", ring, new Dictionary<string, string>());

    /// <summary>A closed rectangle ring.</summary>
    public static (double Lat, double Lon)[] Box(double south, double west, double north, double east) =>
        [(south, west), (south, east), (north, east), (north, west), (south, west)];

    public static S101Dataset Cell(
        IReadOnlyList<AreaFeature> areas,
        IReadOnlyList<(double Lat, double Lon, double Depth)>? soundings = null)
    {
        var featureTypes = new Dictionary<ushort, string>();
        var attributeTypes = new Dictionary<ushort, string>();
        var points = new Dictionary<uint, S101PointRecord>();
        var curves = new Dictionary<uint, S101CurveSegmentRecord>();
        var surfaces = new Dictionary<uint, S101SurfaceRecord>();
        var multiPoints = new Dictionary<uint, S101MultiPointRecord>();
        var features = new List<S101FeatureRecord>();
        uint id = 1;

        ushort Code(Dictionary<ushort, string> catalogue, string name)
        {
            foreach (var (k, v) in catalogue)
            {
                if (v == name) return k;
            }

            var code = (ushort)(catalogue.Count + 1);
            catalogue[code] = name;
            return code;
        }

        uint Ring((double Lat, double Lon)[] ring)
        {
            var pointId = id++;
            var curveId = id++;
            points[pointId] = new S101PointRecord { RecordId = pointId, Y = Scale(ring[0].Lat), X = Scale(ring[0].Lon) };
            curves[curveId] = new S101CurveSegmentRecord
            {
                RecordId = curveId,
                PointAssociations = [new S101PointAssociation(110, pointId, 1), new S101PointAssociation(110, pointId, 2)],
                IntermediateCoordinates = ring.Skip(1).Take(ring.Length - 2).Select(v => (Scale(v.Lat), Scale(v.Lon))).ToList(),
            };
            return curveId;
        }

        foreach (var area in areas)
        {
            var exterior = Ring(area.Ring);
            var rings = new List<S101RingAssociation> { new(120, exterior, 1, 1) };
            if (area.Hole is { } hole)
            {
                rings.Add(new S101RingAssociation(120, Ring(hole), 1, 2));
            }

            var surfaceId = id++;
            surfaces[surfaceId] = new S101SurfaceRecord { RecordId = surfaceId, RingAssociations = rings };
            features.Add(new S101FeatureRecord
            {
                RecordId = id++,
                FeatureTypeCode = Code(featureTypes, area.FeatureType),
                Attributes = area.Attributes.Select(kv => new S101Attribute(Code(attributeTypes, kv.Key), 1, kv.Value)).ToList(),
                SpatialAssociations = [new S101SpatialAssociation(130, surfaceId, 1)],
                FeatureAssociations = [],
                InformationAssociations = [],
            });
        }

        if (soundings is { Count: > 0 })
        {
            var multiId = id++;
            multiPoints[multiId] = new S101MultiPointRecord
            {
                RecordId = multiId,
                Points = soundings.Select(s => (Scale(s.Lat), Scale(s.Lon), (int)Math.Round(s.Depth * CmfZ))).ToList(),
            };
            features.Add(new S101FeatureRecord
            {
                RecordId = id++,
                FeatureTypeCode = Code(featureTypes, "Sounding"),
                Attributes = [],
                SpatialAssociations = [new S101SpatialAssociation(115, multiId, 1)],
                FeatureAssociations = [],
                InformationAssociations = [],
            });
        }

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "101AREAS" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = Cmf,
                CoordinateMultiplicationFactorY = Cmf,
                CoordinateMultiplicationFactorZ = CmfZ,
            },
            FeatureTypeCatalogue = featureTypes,
            AttributeTypeCatalogue = attributeTypes,
            Points = points,
            MultiPoints = multiPoints,
            CurveSegments = curves,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = surfaces,
            Features = features,
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
        return S101Dataset.FromDocument(document);
    }

    private static int Scale(double degrees) => (int)Math.Round(degrees * Cmf);

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
