namespace EncDotNet.S100.DataModel;

/// <summary>
/// A diagnostic message emitted while projecting an S-100 feature-bag
/// dataset (e.g. <c>S421Dataset</c>, <c>S124Dataset</c>) into a strongly-typed
/// data model.
/// </summary>
/// <remarks>
/// <para>
/// Typed-model projections are deliberately permissive: malformed input
/// surfaces here rather than throwing. The only fatal condition is a missing
/// root entity (e.g. an S-421 dataset with no <c>Route</c> feature), which
/// remains an <see cref="System.InvalidOperationException"/>.
/// </para>
/// <para>
/// Use <see cref="Code"/> for programmatic dispatch — stable identifiers
/// (e.g. <c>"xlink.unresolved"</c>, <c>"attribute.parse.double"</c>,
/// <c>"feature.missing"</c>) are preferred over message-text matching.
/// </para>
/// </remarks>
public sealed class ProjectionDiagnostic
{
    /// <summary>The diagnostic severity.</summary>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>Human-readable description of the issue. Not intended for machine parsing — use <see cref="Code"/> instead.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Stable, programmatic identifier for the issue (e.g.
    /// <c>"xlink.unresolved"</c>, <c>"attribute.parse.int"</c>,
    /// <c>"attribute.parse.double"</c>, <c>"attribute.parse.bool"</c>,
    /// <c>"attribute.parse.datetime"</c>, <c>"feature.missing"</c>,
    /// <c>"feature.geometry.missing"</c>, <c>"feature.duplicate"</c>).
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// The <c>gml:id</c> of the feature or information type the issue
    /// relates to, when applicable.
    /// </summary>
    public string? RelatedId { get; init; }

    /// <summary>
    /// The name of the attribute (or association role) the issue relates to,
    /// when applicable (e.g. <c>"routeInfoEditionTime"</c>, <c>"routeWaypoint"</c>).
    /// </summary>
    public string? RelatedAttribute { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var head = $"[{Severity}] {Message}";
        if (RelatedId is null && RelatedAttribute is null) return head;
        if (RelatedAttribute is null) return $"{head} (id: {RelatedId})";
        if (RelatedId is null) return $"{head} (attribute: {RelatedAttribute})";
        return $"{head} (id: {RelatedId}, attribute: {RelatedAttribute})";
    }
}
