using System.Collections.ObjectModel;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S101Synth
{
    /// <summary>Builds a minimal in-memory S-101 dataset for tests.</summary>
    public static S101Dataset Dataset(string name = "test-enc")
    {
        return Dataset(name, features: []);
    }

    /// <summary>
    /// Builds an S-101 dataset with the supplied feature records and
    /// optional code/acronym dictionaries.
    /// </summary>
    public static S101Dataset Dataset(
        string name,
        IReadOnlyList<S101FeatureRecord> features,
        IReadOnlyDictionary<ushort, string>? featureTypes = null,
        IReadOnlyDictionary<ushort, string>? attributeTypes = null,
        IReadOnlyDictionary<uint, S101PointRecord>? points = null,
        IReadOnlyDictionary<uint, S101MultiPointRecord>? multiPoints = null,
        IReadOnlyDictionary<uint, S101InformationRecord>? informationTypes = null,
        IReadOnlyDictionary<ushort, string>? informationTypeCatalogue = null,
        IReadOnlyDictionary<ushort, string>? informationAssociationCatalogue = null,
        IReadOnlyDictionary<ushort, string>? roleCatalogue = null)
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
            FeatureTypeCatalogue = featureTypes ?? ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = attributeTypes ?? ReadOnlyDictionary<ushort, string>.Empty,
            Points = points ?? ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            MultiPoints = multiPoints ?? ReadOnlyDictionary<uint, S101MultiPointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = features,
            InformationTypes = informationTypes ?? ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = informationTypeCatalogue ?? ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = informationAssociationCatalogue ?? ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = roleCatalogue ?? ReadOnlyDictionary<ushort, string>.Empty,
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
        IReadOnlyDictionary<ushort, string>? featureTypes = null)
    {
        var featureRecords = new List<S101FeatureRecord>();
        var pointRecords = new Dictionary<uint, S101PointRecord>();
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

        return Dataset(name, featureRecords.ToArray(), featureTypes, points: pointRecords.ToDictionary());
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
            .ToArray();

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
            Attributes = [],
            SpatialAssociations = [
                new S101SpatialAssociation(115, featureRcid, 1)],
            FeatureAssociations = [],
            InformationAssociations = [],
        };

        var featureTypes = new Dictionary<ushort, string> { [featureTypeCode] = "Sounding" };
        var multiPoints = new Dictionary<uint, S101MultiPointRecord> { [featureRcid] = multiPoint };

        return Dataset(
            name,
            [feature],
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
            Attributes = [new S101Attribute(textAttributeCode, 1, text)],
        };

        var feature = new S101FeatureRecord
        {
            RecordId = featureRcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = featureRcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = [],
            SpatialAssociations = [new S101SpatialAssociation(110, featureRcid, 1)],
            FeatureAssociations = [],
            InformationAssociations = [
                new S101InformationAssociation(infoAssociationCode, infoRcid, roleCode)],
        };

        var points = new Dictionary<uint, S101PointRecord>
        {
            [featureRcid] = new S101PointRecord
            {
                RecordId = featureRcid,
                Y = (int)Math.Round(47.6 * CoordinateMultiplicationFactor),
                X = (int)Math.Round(-122.3 * CoordinateMultiplicationFactor),
            }
        };

        var featureTypes = new Dictionary<ushort, string> { [featureTypeCode] = "CautionArea" };
        var attributeTypes = new Dictionary<ushort, string> { [textAttributeCode] = "information" };
        var informationTypes = new Dictionary<uint, S101InformationRecord> { [infoRcid] = infoRecord };
        var informationTypeCatalogue = new Dictionary<ushort, string> { [infoTypeCode] = "NauticalInformation" };
        var informationAssociationCatalogue = new Dictionary<ushort, string> { [infoAssociationCode] = "AdditionalInformation" };
        var roleCatalogue = new Dictionary<ushort, string> { [roleCode] = "the additional information" };

        return Dataset(
            name,
            [feature],
            featureTypes,
            attributeTypes,
            points: points,
            informationTypes: informationTypes,
            informationTypeCatalogue: informationTypeCatalogue,
            informationAssociationCatalogue: informationAssociationCatalogue,
            roleCatalogue: roleCatalogue);
    }

    /// <summary>
    /// Builds an S-101 dataset with a single point feature carrying flat
    /// attributes, mapping each attribute's numeric code to an acronym via
    /// the dataset's <c>AttributeTypeCatalogue</c> so the describer (and the
    /// unit resolver) can resolve attribute names. Used to exercise unit
    /// annotation of depth-valued attributes (issue #334).
    /// </summary>
    public static S101Dataset DatasetWithAttributedFeature(
        string name,
        uint featureRcid,
        ushort featureTypeCode,
        string featureTypeName,
        IEnumerable<(ushort Code, string Acronym, string Value)> attributes,
        double lat = 50.0,
        double lon = -1.0)
    {
        var attrList = attributes.ToArray();

        var feature = new S101FeatureRecord
        {
            RecordId = featureRcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = featureRcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = attrList.Select(a => new S101Attribute(a.Code, 1, a.Value)).ToArray(),
            SpatialAssociations = [new S101SpatialAssociation(110, featureRcid, 1)],
            FeatureAssociations = [],
            InformationAssociations = [],
        };

        var points = new Dictionary<uint, S101PointRecord>
        {
            [featureRcid] = new S101PointRecord
            {
                RecordId = featureRcid,
                Y = (int)Math.Round(lat * CoordinateMultiplicationFactor),
                X = (int)Math.Round(lon * CoordinateMultiplicationFactor),
            }
        };

        var featureTypes = new Dictionary<ushort, string> { [featureTypeCode] = featureTypeName };
        var attributeTypes = new Dictionary<ushort, string>();
        foreach (var a in attrList)
        {
            attributeTypes[a.Code] = a.Acronym;
        }

        return Dataset(
            name,
            [feature],
            featureTypes,
            attributeTypes,
            points: points);
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
        IReadOnlyList<S101Attribute> attrs = attributes is null
            ? []
            : attributes.Select(a => new S101Attribute(a.Code, 1, a.Value)).ToArray();
        IReadOnlyList<S101SpatialAssociation> spatial = [new S101SpatialAssociation(spatialRcnm, rcid, 1)];
        return new S101FeatureRecord
        {
            RecordId = rcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = rcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = attrs,
            SpatialAssociations = spatial,
            FeatureAssociations = [],
            InformationAssociations = [],
        };
    }
}
