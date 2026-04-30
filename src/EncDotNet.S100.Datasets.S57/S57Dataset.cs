namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// An S-57 Electronic Navigational Chart base cell, parsed directly from
/// ISO 8211 encoded records via <see cref="S57DocumentReader"/>.
/// </summary>
public sealed class S57Dataset
{
    private S57Dataset(S57Document document)
    {
        Document = document;
    }

    /// <summary>The underlying parsed S-57 document.</summary>
    public S57Document Document { get; }

    /// <summary>Dataset name from the DSID record.</summary>
    public string DatasetName => Document.Identification.DatasetName;

    /// <summary>Number of feature records in the dataset.</summary>
    public int FeatureCount => Document.Features.Length;

    /// <summary>Opens an S-57 base cell from a file path.</summary>
    /// <exception cref="NotSupportedException">Thrown when the file is an update (UPDN ≠ 0).</exception>
    public static S57Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var doc = S57DocumentReader.ReadFromFile(path);
        return new S57Dataset(doc);
    }

    /// <summary>Opens an S-57 base cell from a stream.</summary>
    /// <exception cref="NotSupportedException">Thrown when the stream contains an update (UPDN ≠ 0).</exception>
    public static S57Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = S57DocumentReader.ReadFromStream(stream);
        return new S57Dataset(doc);
    }

    /// <summary>
    /// Returns <c>true</c> when the file at <paramref name="path"/> appears to
    /// be an S-57 dataset (heuristic: the ISO 8211 DDR contains a <c>DSPM</c>
    /// field, which is unique to S-57 and not present in S-101). Returns
    /// <c>false</c> for non-ISO 8211 files or files lacking <c>DSPM</c>.
    /// </summary>
    public static bool IsS57File(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            var iso = EncDotNet.Iso8211.Iso8211DocumentReader.ReadFromFile(path);
            if (iso.DataDescriptiveRecord is null) return false;
            var ddr = EncDotNet.Iso8211.Iso8211DataDescriptiveRecordReader.Read(iso.DataDescriptiveRecord);
            return ddr.GetFieldDefinition("DSPM") is not null;
        }
        catch
        {
            return false;
        }
    }
}
