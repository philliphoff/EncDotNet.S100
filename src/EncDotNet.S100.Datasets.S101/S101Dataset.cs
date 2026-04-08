using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// An S-101 Electronic Navigational Chart dataset, built on the S-57
/// encoding (ISO 8211) through <see cref="S57Chart"/>.
/// </summary>
public sealed class S101Dataset
{
    private S101Dataset(S57Chart chart)
    {
        Chart = chart;
    }

    /// <summary>The underlying S-57 chart providing indexed features and spatial records.</summary>
    internal S57Chart Chart { get; }

    /// <summary>Dataset name from the DSID record.</summary>
    public string DatasetName => Chart.Identification.DataSetName ?? "";

    /// <summary>Compilation scale denominator (e.g. 22000 for 1:22,000).</summary>
    public int CompilationScale => Chart.CompilationScale;

    /// <summary>Coordinate multiplication factor (raw integers ÷ COMF → degrees).</summary>
    public int CoordinateMultiplicationFactor => Chart.CoordinateMultiplicationFactor;

    /// <summary>Number of feature records in the dataset.</summary>
    public int FeatureCount =>
        Chart.PointFeatures.Count + Chart.LineFeatures.Count + Chart.AreaFeatures.Count;

    /// <summary>Opens an S-101 dataset from a file path.</summary>
    public static S101Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var chart = S57Chart.FromFile(path);
        return new S101Dataset(chart);
    }

    /// <summary>Opens an S-101 dataset from a stream.</summary>
    public static S101Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var chart = S57Chart.FromStream(stream);
        return new S101Dataset(chart);
    }
}
