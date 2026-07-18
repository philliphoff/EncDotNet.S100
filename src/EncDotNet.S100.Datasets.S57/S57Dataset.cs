using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

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

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-57
    /// base cell at <paramref name="path"/> — its (canonical) specification,
    /// geographic extent, and intended display-scale window — for phased /
    /// deferred loading (issue #460). See <see cref="ReadMetadata()"/> for the
    /// rationale and the cheap-scan strategy.
    /// </summary>
    /// <param name="path">Absolute path to the S-57 base cell (<c>.000</c>).</param>
    /// <returns>The cell's lightweight metadata.</returns>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-57
    /// base cell from <paramref name="stream"/>. See
    /// <see cref="ReadMetadata()"/> for the phased-loading rationale.
    /// </summary>
    /// <param name="stream">The S-57 base cell (<c>.000</c>) stream.</param>
    /// <returns>The cell's lightweight metadata.</returns>
    public static DatasetMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(stream).ReadMetadata();
    }

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for this already
    /// parsed dataset: the canonical <c>S-57</c> specification, the geographic
    /// extent, and the compilation-scale display window (issue #460).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The phased win over a full load is that the S-57 → S-101 translation and
    /// the portrayal catalogue are never touched: the extent is folded directly
    /// from the raw spatial records' coordinates (2-D vertices and sounding
    /// nodes), divided by the coordinate multiplication factor (COMF) from the
    /// <c>DSPM</c> field to yield WGS-84 decimal degrees. This mirrors the
    /// extent the full render ultimately fits, but skips feature/attribute
    /// materialisation.
    /// </para>
    /// <para>
    /// The display-scale window carries only a coarsest bound, sourced from the
    /// compilation scale (CSCL, S-57 Appendix B.1 §7.3.1.1) — the same value the
    /// full processor applies as the whole-cell minimum display scale. The
    /// canonical spec name is <c>S-57</c> with a <c>default</c> edition, matching
    /// <c>S57DatasetProcessor.Spec</c>.
    /// </para>
    /// </remarks>
    /// <returns>The cell's lightweight metadata.</returns>
    public DatasetMetadata ReadMetadata() => ReadMetadata(Document);

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for an
    /// already-parsed (and, where applicable, update-folded)
    /// <see cref="EncDotNet.S57.S57Document"/>. Exposed so the render pipeline's
    /// <c>S57DatasetProcessor</c> can surface the same metadata from the merged
    /// document it already holds, without re-opening the cell.
    /// </summary>
    /// <param name="document">The parsed S-57 document.</param>
    /// <returns>The document's lightweight metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static DatasetMetadata ReadMetadata(EncDotNet.S57.S57Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DatasetMetadata
        {
            Spec = new SpecRef("S-57", default),
            Extent = ComputeExtent(document),
            HorizontalCrsEpsg = null,
            DisplayScale = ResolveDisplayScale(document),
        };
    }

    /// <summary>
    /// Folds the WGS-84 extent from every 2-D vertex and sounding node in the
    /// document's spatial records, converting raw integer coordinates to decimal
    /// degrees via the coordinate multiplication factor (COMF, S-57 Appendix
    /// B.1 §7.3.1.1). Returns <see langword="null"/> when the document declares
    /// no usable COMF or carries no coordinate-bearing spatial record.
    /// </summary>
    private static BoundingBox? ComputeExtent(EncDotNet.S57.S57Document document)
    {
        var comf = document.CoordinateMultiplicationFactor;
        if (comf <= 0)
            return null;

        long minX = long.MaxValue, maxX = long.MinValue;
        long minY = long.MaxValue, maxY = long.MinValue;
        var any = false;

        foreach (var vector in document.VectorRecords)
        {
            foreach (var c in vector.Coordinates2D)
            {
                if (c.X < minX) minX = c.X;
                if (c.X > maxX) maxX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.Y > maxY) maxY = c.Y;
                any = true;
            }

            foreach (var s in vector.Soundings)
            {
                if (s.X < minX) minX = s.X;
                if (s.X > maxX) maxX = s.X;
                if (s.Y < minY) minY = s.Y;
                if (s.Y > maxY) maxY = s.Y;
                any = true;
            }
        }

        if (!any)
            return null;

        double divisor = comf;
        return new BoundingBox(
            southLatitude: minY / divisor,
            westLongitude: minX / divisor,
            northLatitude: maxY / divisor,
            eastLongitude: maxX / divisor);
    }

    /// <summary>
    /// Resolves the display-scale window from the compilation scale (CSCL) in
    /// the <c>DSPM</c> field. Only the coarsest bound is modelled — S-57 has no
    /// finest-scale analogue — matching the whole-cell minimum display scale the
    /// full processor applies. Returns <see langword="null"/> when the cell
    /// declares no usable compilation scale.
    /// </summary>
    private static DisplayScaleRange? ResolveDisplayScale(EncDotNet.S57.S57Document document)
    {
        var compilationScale = document.DataSetParameters?.CompilationScale ?? 0;
        return compilationScale > 0 ? new DisplayScaleRange(compilationScale, null) : null;
    }

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
    /// Opens an S-57 base cell from <paramref name="baseStream"/> and applies the
    /// sequential update datasets in <paramref name="updateStreams"/>, returning
    /// the fully-updated dataset.
    /// </summary>
    /// <remarks>
    /// The base (<c>.000</c>) and each update (<c>.001</c>, <c>.002</c>, …) are
    /// read into <see cref="EncDotNet.S57.S57Document"/>s and folded with
    /// <see cref="EncDotNet.S57.S57Document.ApplyChanges"/> in the order supplied
    /// — which must be ascending update-number order (S-57 Part 3, dataset
    /// updating). The merged document is what callers translate to S-101; the
    /// base and intermediate records are never surfaced.
    /// </remarks>
    /// <param name="baseStream">The base cell (<c>.000</c>) stream.</param>
    /// <param name="updateStreams">The update streams, in ascending update-number order.</param>
    public static S57Dataset Open(Stream baseStream, IReadOnlyList<Stream> updateStreams)
    {
        ArgumentNullException.ThrowIfNull(baseStream);
        ArgumentNullException.ThrowIfNull(updateStreams);

        var doc = EncDotNet.S57.S57DocumentReader.Read(baseStream);
        foreach (var updateStream in updateStreams)
        {
            ArgumentNullException.ThrowIfNull(updateStream);
            var update = EncDotNet.S57.S57DocumentReader.Read(updateStream);
            doc = doc.ApplyChanges(update);
        }

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
