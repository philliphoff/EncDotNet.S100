using System.Collections.Immutable;

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
                Messages = readMessages.Concat(report.Messages).ToImmutableArray(),
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
}
