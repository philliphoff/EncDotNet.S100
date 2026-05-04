namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// An S-101 Electronic Navigational Chart dataset, parsed directly from
/// ISO 8211 encoded records via <see cref="S101DocumentReader"/>.
/// </summary>
public sealed class S101Dataset
{
    private S101Dataset(S101Document document)
    {
        Document = document;
    }

    /// <summary>The underlying parsed S-101 document.</summary>
    public S101Document Document { get; }

    /// <summary>Dataset name from the DSID record.</summary>
    public string DatasetName => Document.Identification.DatasetName;

    /// <summary>Coordinate multiplication factor for X (longitude).</summary>
    public uint CoordinateMultiplicationFactorX => Document.StructureInfo.CoordinateMultiplicationFactorX;

    /// <summary>Coordinate multiplication factor for Y (latitude).</summary>
    public uint CoordinateMultiplicationFactorY => Document.StructureInfo.CoordinateMultiplicationFactorY;

    /// <summary>Number of feature records in the dataset.</summary>
    public int FeatureCount => Document.Features.Length;

    /// <summary>Opens an S-101 dataset from a file path.</summary>
    public static S101Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var doc = S101DocumentReader.ReadFromFile(path);
        return new S101Dataset(doc);
    }

    /// <summary>Opens an S-101 dataset from a stream.</summary>
    public static S101Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = S101DocumentReader.ReadFromStream(stream);
        return new S101Dataset(doc);
    }

    /// <summary>
    /// Wraps an in-memory <see cref="S101Document"/> so it can be consumed by
    /// the rest of the S-101 pipeline.
    /// </summary>
    /// <remarks>
    /// Intended for adapters that translate from another product specification
    /// (for example, S-57) into the S-101 in-memory model in order to reuse the
    /// S-101 portrayal catalogue.
    /// </remarks>
    public static S101Dataset FromDocument(S101Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new S101Dataset(document);
    }
}
