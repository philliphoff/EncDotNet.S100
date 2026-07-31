namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Root data model for an S-102 Bathymetric Surface dataset.
/// </summary>
public sealed class S102Dataset
{
    /// <summary>EPSG code of the horizontal coordinate reference system.</summary>
    public int? HorizontalCRS { get; init; }

    /// <summary>
    /// The S-100 <c>verticalDatum</c> code declared on the dataset root, or
    /// <c>null</c> when the attribute is absent. This is the reference level
    /// for the coverage's depth values (S-102 Ed 3.0.0 §12.3; S-100 register
    /// codelist, source identifier 996). Resolve to a label with
    /// <c>EncDotNet.S100.DataModel.VerticalDatums.GetLabel</c>.
    /// </summary>
    public int? VerticalDatum { get; init; }

    /// <summary>Epoch of the coordinate reference system (e.g. "G1762").</summary>
    public string? Epoch { get; init; }

    /// <summary>A geographic description of the dataset coverage area.</summary>
    public string? GeographicIdentifier { get; init; }

    /// <summary>Issue date of the dataset (ISO 8601).</summary>
    public string? IssueDate { get; init; }

    /// <summary>Reference to an associated metadata file.</summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// The raw <c>productSpecification</c> string declared on the dataset
    /// root (e.g. <c>"INT.IHO.S-102.3.0.0"</c>), or <c>null</c> when the
    /// attribute is absent. S-100 Part 10c §10.2.1. Surfaced so the pipeline
    /// can report the dataset's declared edition and warn on a version
    /// mismatch (see <c>SpecVersionAssessment</c>).
    /// </summary>
    public string? DeclaredProductSpecification { get; init; }

    /// <summary>The bathymetric coverages contained in the dataset.</summary>
    public required IReadOnlyList<BathymetryCoverage> Coverages { get; init; }
}
