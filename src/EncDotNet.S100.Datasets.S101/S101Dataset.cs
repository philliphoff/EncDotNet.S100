
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// An S-101 Electronic Navigational Chart dataset, parsed directly from
/// ISO 8211 encoded records via <see cref="S101DocumentReader"/>.
/// </summary>
public sealed class S101Dataset
{
    private S101Dataset(S101Document document, S101UpdateReport? updateReport = null)
    {
        Document = document;
        UpdateReport = updateReport;
    }

    /// <summary>The underlying parsed S-101 document.</summary>
    public S101Document Document { get; }

    /// <summary>
    /// Outcome of applying sequential updates when the dataset was opened via
    /// <see cref="OpenWithUpdates(string, IReadOnlyList{string})"/>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public S101UpdateReport? UpdateReport { get; }

    /// <summary>Dataset name from the DSID record.</summary>
    public string DatasetName => Document.Identification.DatasetName;

    /// <summary>Coordinate multiplication factor for X (longitude).</summary>
    public uint CoordinateMultiplicationFactorX => Document.StructureInfo.CoordinateMultiplicationFactorX;

    /// <summary>Coordinate multiplication factor for Y (latitude).</summary>
    public uint CoordinateMultiplicationFactorY => Document.StructureInfo.CoordinateMultiplicationFactorY;

    /// <summary>Number of feature records in the dataset.</summary>
    public int FeatureCount => Document.Features.Count;

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
    /// Opens an S-101 base cell and applies a set of sequential update files
    /// (best-effort) to produce an up-to-date dataset.
    /// </summary>
    /// <param name="basePath">Path to the base cell (<c>….000</c>).</param>
    /// <param name="updatePaths">
    /// Paths to the update files (<c>….001</c>, <c>….002</c>, …). They are read,
    /// ordered by update number, and applied in sequence. A file that fails to
    /// read, or a non-contiguous / invalid update, is recorded in
    /// <see cref="UpdateReport"/> and never prevents the dataset from opening.
    /// </param>
    /// <returns>
    /// The dataset reflecting the base plus every successfully applied update,
    /// with <see cref="UpdateReport"/> populated.
    /// </returns>
    public static S101Dataset OpenWithUpdates(string basePath, IReadOnlyList<string> updatePaths)
    {
        ArgumentException.ThrowIfNullOrEmpty(basePath);
        ArgumentNullException.ThrowIfNull(updatePaths);

        var baseDoc = S101DocumentReader.ReadFromFile(basePath);

        var readMessages = new List<S101UpdateMessage>();
        var updates = new List<S101Document>(updatePaths.Count);
        foreach (var path in updatePaths)
        {
            try
            {
                updates.Add(S101DocumentReader.ReadFromFile(path));
            }
            catch (Exception ex)
            {
                readMessages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Error,
                    $"Failed to read update '{System.IO.Path.GetFileName(path)}': {ex.Message}."));
            }
        }

        updates.Sort((a, b) => a.Identification.UpdateNumber.CompareTo(b.Identification.UpdateNumber));

        var merged = S101UpdateApplicator.Apply(baseDoc, updates, out var report);

        if (readMessages.Count > 0)
        {
            report = new S101UpdateReport
            {
                BaseUpdateNumber = report.BaseUpdateNumber,
                AppliedThroughUpdateNumber = report.AppliedThroughUpdateNumber,
                Inserted = report.Inserted,
                Deleted = report.Deleted,
                Modified = report.Modified,
                Messages = readMessages.Concat(report.Messages).ToArray(),
            };
        }

        return new S101Dataset(merged, report);
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

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-101
    /// cell at <paramref name="path"/> — its declared specification, geographic
    /// extent, and intended display-scale window — for phased / deferred
    /// loading (issue #460).
    /// </summary>
    /// <remarks>
    /// Unlike the HDF5 products, the S-101 ISO 8211 encoding carries no
    /// header-level minimum bounding rectangle, so the extent is still derived
    /// by scanning the spatial records; the phased win over a full load is that
    /// the feature graph is not resolved and — crucially — the portrayal
    /// catalogue is never loaded or executed. The display-scale window is read
    /// cheaply from the <c>DataCoverage</c> feature records
    /// (S-101 FC §3.1.1 <c>minimumDisplayScale</c> / <c>maximumDisplayScale</c>).
    /// </remarks>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-101
    /// cell from <paramref name="stream"/>. See
    /// <see cref="ReadMetadata(string)"/> for the phased-loading rationale.
    /// </summary>
    public static DatasetMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(stream).ReadMetadata();
    }

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for this already
    /// parsed dataset: declared specification, geographic extent, and the
    /// <c>DataCoverage</c> display-scale window (issue #460).
    /// </summary>
    public DatasetMetadata ReadMetadata()
    {
        var vector = new S101VectorSource(this).Metadata;

        // S101VectorSource.ComputeExtent yields BoundingBox(0,0,0,0) when the
        // cell carries no coordinate-bearing spatial records. Detect that
        // "no coordinates" case from the document directly rather than
        // special-casing the numeric bounds, so a dataset that legitimately
        // sits at 0°N 0°E is not mistaken for an empty one.
        bool hasCoordinates = DatasetHasCoordinates(Document);

        return new DatasetMetadata
        {
            Spec = vector.Spec,
            Extent = hasCoordinates ? vector.Extent : null,
            HorizontalCrsEpsg = null,
            DisplayScale = ResolveDisplayScale(Document),
        };
    }

    /// <summary>
    /// Reports whether the dataset carries any coordinate-bearing spatial
    /// record — the same Point / MultiPoint / curve-segment sources that
    /// <see cref="S101VectorSource"/> folds into the extent. Used to
    /// distinguish a genuinely empty cell (extent unavailable) from one that
    /// legitimately resolves to a bounding box at the origin.
    /// </summary>
    private static bool DatasetHasCoordinates(S101Document document)
    {
        if (document.Points.Count > 0)
            return true;

        foreach (var mp in document.MultiPoints.Values)
        {
            if (mp.Points.Count > 0)
                return true;
        }

        foreach (var seg in document.CurveSegments.Values)
        {
            if (seg.IntermediateCoordinates.Count > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the dataset-level display-scale window from the <c>DataCoverage</c>
    /// feature records (S-101 FC §3.1.1). The coarsest bound (largest
    /// denominator) is the maximum <c>minimumDisplayScale</c> and the finest
    /// bound (smallest denominator) is the minimum <c>maximumDisplayScale</c>
    /// across all coverages, matching the exchange-set catalogue's
    /// <c>DatasetDiscoveryMetadata</c> resolution.
    /// </summary>
    private static DisplayScaleRange? ResolveDisplayScale(S101Document document)
    {
        ushort? dataCoverageCode = ResolveCode(document.FeatureTypeCatalogue, "DataCoverage");
        if (dataCoverageCode is null)
            return null;

        ushort? minCode = ResolveCode(document.AttributeTypeCatalogue, "minimumDisplayScale");
        ushort? maxCode = ResolveCode(document.AttributeTypeCatalogue, "maximumDisplayScale");
        if (minCode is null && maxCode is null)
            return null;

        int? coarsest = null; // largest minimumDisplayScale denominator
        int? finest = null;   // smallest maximumDisplayScale denominator

        foreach (var feature in document.Features)
        {
            if (feature.FeatureTypeCode != dataCoverageCode.Value)
                continue;

            foreach (var attr in feature.Attributes)
            {
                if (minCode is not null && attr.NumericCode == minCode.Value
                    && TryParseDenominator(attr.Value, out int minDenom))
                {
                    coarsest = coarsest is null ? minDenom : Math.Max(coarsest.Value, minDenom);
                }
                else if (maxCode is not null && attr.NumericCode == maxCode.Value
                    && TryParseDenominator(attr.Value, out int maxDenom))
                {
                    finest = finest is null ? maxDenom : Math.Min(finest.Value, maxDenom);
                }
            }
        }

        if (coarsest is null && finest is null)
            return null;

        return new DisplayScaleRange(coarsest, finest);
    }

    private static ushort? ResolveCode(IReadOnlyDictionary<ushort, string> catalogue, string name)
    {
        foreach (var pair in catalogue)
        {
            if (string.Equals(pair.Value, name, StringComparison.Ordinal))
                return pair.Key;
        }

        return null;
    }

    private static bool TryParseDenominator(string? value, out int denominator)
    {
        denominator = 0;
        return !string.IsNullOrWhiteSpace(value)
            && int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out denominator)
            && denominator > 0;
    }
}
