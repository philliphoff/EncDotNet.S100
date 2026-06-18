using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S101Synth
{
    /// <summary>Builds a minimal in-memory S-101 dataset for tests.</summary>
    public static S101Dataset Dataset(string name = "test-enc")
    {
        return Dataset(name, features: ImmutableArray<S101FeatureRecord>.Empty);
    }

    /// <summary>
    /// Builds an S-101 dataset with the supplied feature records and
    /// optional code/acronym dictionaries.
    /// </summary>
    public static S101Dataset Dataset(
        string name,
        ImmutableArray<S101FeatureRecord> features,
        ImmutableDictionary<ushort, string>? featureTypes = null,
        ImmutableDictionary<ushort, string>? attributeTypes = null,
        ImmutableDictionary<uint, S101PointRecord>? points = null,
        ImmutableDictionary<uint, S101MultiPointRecord>? multiPoints = null,
        ImmutableDictionary<uint, S101InformationRecord>? informationTypes = null,
        ImmutableDictionary<ushort, string>? informationTypeCatalogue = null,
        ImmutableDictionary<ushort, string>? informationAssociationCatalogue = null,
        ImmutableDictionary<ushort, string>? roleCatalogue = null)
    {
        var document = new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = name,
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = featureTypes ?? ImmutableDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = attributeTypes ?? ImmutableDictionary<ushort, string>.Empty,
            Points = points ?? ImmutableDictionary<uint, S101PointRecord>.Empty,
            MultiPoints = multiPoints ?? ImmutableDictionary<uint, S101MultiPointRecord>.Empty,
            CurveSegments = ImmutableDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ImmutableDictionary<uint, S101SurfaceRecord>.Empty,
            Features = features,
            InformationTypes = informationTypes ?? ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = informationTypeCatalogue ?? ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = informationAssociationCatalogue ?? ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = roleCatalogue ?? ImmutableDictionary<ushort, string>.Empty,
        };
        return S101Dataset.FromDocument(document);
    }

    private const int CoordinateMultiplicationFactor = 10_000_000;

    /// <summary>
    /// Builds an S-101 dataset whose features carry resolvable point
    /// geometry. Each entry supplies a feature RCID, a feature-type code,
    /// and a lat/lon; a matching <see cref="S101PointRecord"/> (RCID =
    /// feature RCID, RCNM 110) is created so the vector source resolves the
    /// coordinate.
    /// </summary>
    public static S101Dataset DatasetWithPointFeatures(
        string name,
        IEnumerable<(uint Rcid, ushort FeatureTypeCode, double Lat, double Lon)> features,
        ImmutableDictionary<ushort, string>? featureTypes = null)
    {
        var featureRecords = ImmutableArray.CreateBuilder<S101FeatureRecord>();
        var pointRecords = ImmutableDictionary.CreateBuilder<uint, S101PointRecord>();
        foreach (var (rcid, code, lat, lon) in features)
        {
            featureRecords.Add(Feature(rcid, code, spatialRcnm: 110));
            pointRecords[rcid] = new S101PointRecord
            {
                RecordId = rcid,
                Y = (int)Math.Round(lat * CoordinateMultiplicationFactor),
                X = (int)Math.Round(lon * CoordinateMultiplicationFactor),
            };
        }

        return Dataset(name, featureRecords.ToImmutable(), featureTypes, points: pointRecords.ToImmutable());
    }

    /// <summary>
    /// Builds an S-101 dataset with a single MultiPoint <c>Sounding</c>
    /// feature whose points carry depth (Z) values. Each depth is supplied
    /// in metres and stored as <c>metres × CMFZ</c> (CMFZ = 10) so the
    /// describer recovers the original metres. The feature (RCID 808) and a
    /// matching MultiPoint spatial record (RCNM 115) are created, and the
    /// feature-type catalogue maps the feature-type code to "Sounding".
    /// </summary>
    public static S101Dataset DatasetWithSounding(
        string name,
        IEnumerable<(double Lat, double Lon, double Depth)> soundings,
        uint featureRcid = 808,
        ushort featureTypeCode = 5)
    {
        const int cmfz = 10;
        var points = soundings
            .Select(s => (
                Y: (int)Math.Round(s.Lat * CoordinateMultiplicationFactor),
                X: (int)Math.Round(s.Lon * CoordinateMultiplicationFactor),
                Z: (int)Math.Round(s.Depth * cmfz)))
            .ToImmutableArray();

        var multiPoint = new S101MultiPointRecord
        {
            RecordId = featureRcid,
            Points = points,
        };

        var feature = new S101FeatureRecord
        {
            RecordId = featureRcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = featureRcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S101Attribute>.Empty,
            SpatialAssociations = ImmutableArray.Create(
                new S101SpatialAssociation(115, featureRcid, 1)),
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
        };

        var featureTypes = ImmutableDictionary<ushort, string>.Empty
            .Add(featureTypeCode, "Sounding");
        var multiPoints = ImmutableDictionary<uint, S101MultiPointRecord>.Empty
            .Add(featureRcid, multiPoint);

        return Dataset(
            name,
            ImmutableArray.Create(feature),
            featureTypes,
            multiPoints: multiPoints);
    }

    /// <summary>
    /// Builds an S-101 dataset with a single point feature that carries an
    /// information association (INAS) to an information type record holding
    /// the supplied text. Exercises the describe_feature dereferencing of
    /// associated information records (issue #313). The feature is RCID
    /// <paramref name="featureRcid"/>; the information record is RCID
    /// <paramref name="infoRcid"/> and carries a single attribute
    /// (<paramref name="textAttributeCode"/> = <paramref name="text"/>).
    /// </summary>
    public static S101Dataset DatasetWithAssociatedInformation(
        string name,
        string text,
        uint featureRcid = 700,
        ushort featureTypeCode = 7,
        uint infoRcid = 9001,
        ushort infoTypeCode = 300,
        ushort infoAssociationCode = 401,
        ushort roleCode = 1,
        ushort textAttributeCode = 12)
    {
        var infoRecord = new S101InformationRecord
        {
            RecordId = infoRcid,
            InformationTypeCode = infoTypeCode,
            Attributes = ImmutableArray.Create(new S101Attribute(textAttributeCode, 1, text)),
        };

        var feature = new S101FeatureRecord
        {
            RecordId = featureRcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = featureRcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S101Attribute>.Empty,
            SpatialAssociations = ImmutableArray.Create(new S101SpatialAssociation(110, featureRcid, 1)),
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray.Create(
                new S101InformationAssociation(infoAssociationCode, infoRcid, roleCode)),
        };

        var points = ImmutableDictionary<uint, S101PointRecord>.Empty
            .Add(featureRcid, new S101PointRecord
            {
                RecordId = featureRcid,
                Y = (int)Math.Round(47.6 * CoordinateMultiplicationFactor),
                X = (int)Math.Round(-122.3 * CoordinateMultiplicationFactor),
            });

        var featureTypes = ImmutableDictionary<ushort, string>.Empty
            .Add(featureTypeCode, "CautionArea");
        var attributeTypes = ImmutableDictionary<ushort, string>.Empty
            .Add(textAttributeCode, "information");
        var informationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty
            .Add(infoRcid, infoRecord);
        var informationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty
            .Add(infoTypeCode, "NauticalInformation");
        var informationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty
            .Add(infoAssociationCode, "AdditionalInformation");
        var roleCatalogue = ImmutableDictionary<ushort, string>.Empty
            .Add(roleCode, "the additional information");

        return Dataset(
            name,
            ImmutableArray.Create(feature),
            featureTypes,
            attributeTypes,
            points: points,
            informationTypes: informationTypes,
            informationTypeCatalogue: informationTypeCatalogue,
            informationAssociationCatalogue: informationAssociationCatalogue,
            roleCatalogue: roleCatalogue);
    }

    /// <summary>
    /// Builds a feature record with the given RCID, feature type code,
    /// and optional flat attribute list and spatial associations.
    /// </summary>
    public static S101FeatureRecord Feature(
        uint rcid,
        ushort featureTypeCode,
        IEnumerable<(ushort Code, string Value)>? attributes = null,
        byte spatialRcnm = 110)
    {
        var attrs = attributes is null
            ? ImmutableArray<S101Attribute>.Empty
            : attributes.Select(a => new S101Attribute(a.Code, 1, a.Value)).ToImmutableArray();
        var spatial = ImmutableArray.Create(new S101SpatialAssociation(spatialRcnm, rcid, 1));
        return new S101FeatureRecord
        {
            RecordId = rcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = rcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = attrs,
            SpatialAssociations = spatial,
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
        };
    }
}
