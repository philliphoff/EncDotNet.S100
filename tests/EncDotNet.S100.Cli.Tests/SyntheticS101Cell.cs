using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Writes small S-101 base cells for tests that need overlapping cells of
/// different scales: one rectangular <c>DataCoverage</c> carrying a
/// <c>minimumDisplayScale</c>, and optionally a <c>LandArea</c> filling a
/// rectangle. Synthesizing the cells keeps these tests free of committed
/// sample data.
/// </summary>
internal static class SyntheticS101Cell
{
    private const ushort DataCoverageCode = 1;
    private const ushort LandAreaCode = 2;
    private const ushort MinimumDisplayScaleCode = 1;
    private const double Factor = 10_000_000;

    /// <summary>A WGS-84 rectangle, in degrees.</summary>
    public readonly record struct Box(double West, double South, double East, double North);

    /// <summary>
    /// Writes a cell named <paramref name="fileName"/> into
    /// <paramref name="directory"/> and returns its full path.
    /// </summary>
    /// <param name="directory">The folder to write into.</param>
    /// <param name="fileName">The cell's file name.</param>
    /// <param name="coverage">The cell's data coverage.</param>
    /// <param name="minimumDisplayScale">The coverage's minimum display scale denominator.</param>
    /// <param name="land">A land area to fill, or <see langword="null"/> for none.</param>
    public static string Write(string directory, string fileName, Box coverage, int minimumDisplayScale, Box? land)
    {
        var points = new Dictionary<uint, S101PointRecord>();
        var curves = new Dictionary<uint, S101CurveSegmentRecord>();
        var surfaces = new Dictionary<uint, S101SurfaceRecord>();
        var features = new List<S101FeatureRecord>();

        AddSurfaceFeature(1, DataCoverageCode, coverage, [new S101Attribute(MinimumDisplayScaleCode, 1, minimumDisplayScale.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        if (land is { } landBox)
            AddSurfaceFeature(2, LandAreaCode, landBox, []);

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                EncodingSpecification = "S-100 Part 10a",
                EncodingSpecificationEdition = "5.2.0",
                ProductSpecification = "INT.IHO.S-101.2.0",
                ProductSpecificationEdition = "2.0.0",
                ApplicationProfile = "1",
                DatasetName = fileName,
                DatasetTitle = "Synthetic overlap fixture",
                DatasetReferenceDate = "20260101",
                DatasetLanguage = "eng",
                DatasetAbstract = "",
                DatasetEdition = "1",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = (int)Factor,
                CoordinateMultiplicationFactorY = (int)Factor,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string>
            {
                [DataCoverageCode] = "DataCoverage",
                [LandAreaCode] = "LandArea",
            },
            AttributeTypeCatalogue = new Dictionary<ushort, string>
            {
                [MinimumDisplayScaleCode] = "minimumDisplayScale",
            },
            InformationTypeCatalogue = new Dictionary<ushort, string>(),
            InformationAssociationCatalogue = new Dictionary<ushort, string>(),
            FeatureAssociationCatalogue = new Dictionary<ushort, string>(),
            RoleCatalogue = new Dictionary<ushort, string>(),
            Points = points,
            CurveSegments = curves,
            CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(),
            Surfaces = surfaces,
            Features = features,
            InformationTypes = new Dictionary<uint, S101InformationRecord>(),
        };

        var path = Path.Combine(directory, fileName);
        S101DocumentWriter.WriteToFile(path, document);
        return path;

        // One closed curve per rectangle: it starts and ends at the south-west
        // corner and runs through the other three.
        void AddSurfaceFeature(uint id, ushort featureCode, Box box, S101Attribute[] attributes)
        {
            points[id] = new S101PointRecord
            {
                RecordId = id,
                X = Scaled(box.West),
                Y = Scaled(box.South),
                RecordVersion = 1,
                UpdateInstruction = S101UpdateInstruction.Insert,
            };
            curves[id] = new S101CurveSegmentRecord
            {
                RecordId = id,
                PointAssociations = [new S101PointAssociation(110, id, 1), new S101PointAssociation(110, id, 2)],
                IntermediateCoordinates =
                [
                    (Scaled(box.North), Scaled(box.West)),
                    (Scaled(box.North), Scaled(box.East)),
                    (Scaled(box.South), Scaled(box.East)),
                ],
                RecordVersion = 1,
                UpdateInstruction = S101UpdateInstruction.Insert,
            };
            surfaces[id] = new S101SurfaceRecord
            {
                RecordId = id,
                RingAssociations = [new S101RingAssociation(120, id, 1, 1)],
                RecordVersion = 1,
                UpdateInstruction = S101UpdateInstruction.Insert,
            };
            features.Add(new S101FeatureRecord
            {
                RecordId = id,
                FeatureTypeCode = featureCode,
                ProducingAgency = 550,
                FeatureIdentificationNumber = id,
                FeatureIdentificationSubdivision = 1,
                Attributes = attributes,
                SpatialAssociations = [new S101SpatialAssociation(130, id, 1)],
                RecordVersion = 1,
                UpdateInstruction = S101UpdateInstruction.Insert,
            });
        }
    }

    private static int Scaled(double degrees) => (int)Math.Round(degrees * Factor);
}
