using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Validation;

/// <summary>
/// A single finding emitted by an <see cref="IValidationRule{TModel}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Findings are intended to be both machine-actionable (via <see cref="RuleId"/>)
/// and human-readable (via <see cref="Message"/>). Rule identifiers should
/// trace back to a clause of the relevant IHO product specification — for
/// example <c>"S421-R-3.1"</c> for clause 3.1 of S-421 — so that consumers
/// can cite the normative requirement that has been violated.
/// </para>
/// <para>
/// Either <see cref="Point"/> or <see cref="BoundingBox"/> (or neither) may
/// be populated to give the finding a spatial location. This enables
/// downstream consumers — such as the viewer's finding layer or an MCP
/// agent — to surface findings spatially.
/// </para>
/// </remarks>
public sealed record ValidationFinding
{
    /// <summary>
    /// Stable identifier for the rule that emitted this finding, traceable
    /// to a spec clause (e.g. <c>"S421-R-3.1"</c>).
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>The severity assigned to this finding.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>Human-readable description of the finding.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional point location associated with the finding (e.g. the
    /// position of a waypoint outside the legal lat/lon range).
    /// </summary>
    public GeoPosition? Point { get; init; }

    /// <summary>
    /// Optional bounding rectangle associated with the finding (e.g. the
    /// envelope of an out-of-range coverage extent). Set on findings whose
    /// natural spatial extent is an area rather than a single point.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }

    /// <summary>
    /// Identifier of the dataset the finding relates to, when the rule was
    /// evaluated in a context that has a dataset identity.
    /// </summary>
    public string? DatasetId { get; init; }

    /// <summary>
    /// Identifier of the feature, information type, or other addressable
    /// object within the dataset the finding relates to (typically a
    /// <c>gml:id</c>).
    /// </summary>
    public string? RelatedFeatureId { get; init; }
}
