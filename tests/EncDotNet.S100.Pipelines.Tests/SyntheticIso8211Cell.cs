using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Writes minimal but well-formed S-100 Part 10a (ISO 8211) cells for tests that
/// need a dataset declaring a particular product in its <c>DSID</c>/<c>PRSP</c>
/// subfield — the field <see cref="EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory"/>
/// reads to tell S-101 and S-401 apart. Synthesizing the cell keeps these tests
/// free of committed sample data.
/// </summary>
internal static class SyntheticIso8211Cell
{
    /// <summary>
    /// Writes a cell named <paramref name="fileName"/> into
    /// <paramref name="directory"/> whose DSID declares
    /// <paramref name="productSpecification"/> (e.g. <c>INT.IHO.S-401.1.2</c>),
    /// and returns its full path.
    /// </summary>
    public static string Write(string directory, string fileName, string productSpecification)
    {
        var document = new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                EncodingSpecification = "S-100 Part 10a",
                EncodingSpecificationEdition = "5.2.0",
                ProductSpecification = productSpecification,
                ProductSpecificationEdition = "1.2.0",
                ApplicationProfile = "1",
                DatasetName = fileName,
                DatasetTitle = "Synthetic detection fixture",
                DatasetReferenceDate = "20260101",
                DatasetLanguage = "eng",
                DatasetAbstract = "",
                DatasetEdition = "1",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string>(),
            AttributeTypeCatalogue = new Dictionary<ushort, string>(),
            InformationTypeCatalogue = new Dictionary<ushort, string>(),
            InformationAssociationCatalogue = new Dictionary<ushort, string>(),
            FeatureAssociationCatalogue = new Dictionary<ushort, string>(),
            RoleCatalogue = new Dictionary<ushort, string>(),
            Points = new Dictionary<uint, S101PointRecord>
            {
                [1] = new S101PointRecord
                {
                    RecordId = 1,
                    X = -710_000_000,
                    Y = 420_000_000,
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
            CurveSegments = new Dictionary<uint, S101CurveSegmentRecord>(),
            CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(),
            Surfaces = new Dictionary<uint, S101SurfaceRecord>(),
            Features = [],
            InformationTypes = new Dictionary<uint, S101InformationRecord>(),
        };

        var path = Path.Combine(directory, fileName);
        S101DocumentWriter.WriteToFile(path, document);
        return path;
    }
}
