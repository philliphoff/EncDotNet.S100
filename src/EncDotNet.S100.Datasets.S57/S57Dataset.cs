using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Thin wrapper over <see cref="EncDotNet.S57.S57Document"/> from the
/// upstream <c>EncDotNet.S57</c> package. Provides the <see cref="IsS57File"/>
/// discriminator used by <see cref="EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory"/>
/// to disambiguate <c>.000</c> files between S-57 and S-101 (which share the
/// extension and ISO 8211 envelope).
/// </summary>
public sealed class S57Dataset
{
    private S57Dataset(EncDotNet.S57.S57Document document)
    {
        Document = document;
    }

    /// <summary>The underlying parsed S-57 document (from the package).</summary>
    public EncDotNet.S57.S57Document Document { get; }

    /// <summary>Dataset name from the DSID record.</summary>
    public string DatasetName => Document.DataSetIdentification?.DataSetName ?? "";

    /// <summary>Number of feature records in the dataset.</summary>
    public int FeatureCount => Document.FeatureRecords.Count;

    /// <summary>Opens an S-57 base cell from a file path.</summary>
    public static S57Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var doc = EncDotNet.S57.S57DocumentReader.ReadFromFile(path);
        return new S57Dataset(doc);
    }

    /// <summary>Opens an S-57 base cell from a stream.</summary>
    public static S57Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = EncDotNet.S57.S57DocumentReader.Read(stream);
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
