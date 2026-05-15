using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// Mutable scratchpad threaded through a typed-model projection. Bundles the
/// diagnostic collector, the <see cref="XlinkResolver"/>, and convenience
/// accessors so projection authors can keep their <c>From(...)</c> factories
/// concise.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ProjectionContext"/> is created at the top of a projection
/// factory and passed (by reference, since it is a reference type) into the
/// per-feature / per-information-type projection helpers. It is not intended
/// to outlive a single <c>From(...)</c> call.
/// </para>
/// <para>
/// The class is public so that third-party authors can build their own
/// strongly-typed projections over S-100 GML datasets using the same plumbing
/// the built-in S-421 / S-124 projections use.
/// </para>
/// </remarks>
public sealed class ProjectionContext
{
    private readonly List<ProjectionDiagnostic> _diagnostics = new();

    /// <summary>
    /// Creates a new context backed by the supplied xlink index.
    /// </summary>
    /// <param name="xlinkIndex">
    /// The xlink resolver to use for <c>xlink:href</c> resolution. Typically
    /// built by <see cref="XlinkResolver.Build"/> from the source dataset.
    /// </param>
    public ProjectionContext(XlinkResolver xlinkIndex)
    {
        ArgumentNullException.ThrowIfNull(xlinkIndex);
        Xlinks = xlinkIndex;
    }

    /// <summary>The xlink resolver used for cross-reference lookup.</summary>
    public XlinkResolver Xlinks { get; }

    /// <summary>The diagnostics accumulated so far during projection.</summary>
    public IReadOnlyList<ProjectionDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Returns the accumulated diagnostics as an immutable snapshot suitable
    /// for the <c>out IReadOnlyList&lt;ProjectionDiagnostic&gt;</c> parameter
    /// of a projection's <c>From(...)</c> factory.
    /// </summary>
    public IReadOnlyList<ProjectionDiagnostic> ToImmutableDiagnostics() =>
        _diagnostics.ToImmutableArray();

    /// <summary>Adds a diagnostic to the context.</summary>
    /// <param name="diagnostic">The diagnostic to record.</param>
    public void Report(ProjectionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Convenience for recording a warning-level diagnostic.
    /// </summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="code">Stable diagnostic code (e.g. <c>"xlink.unresolved"</c>).</param>
    /// <param name="relatedId">The <c>gml:id</c> of the related object, if any.</param>
    /// <param name="relatedAttribute">The attribute name or association role, if any.</param>
    public void Warn(string message, string? code = null, string? relatedId = null, string? relatedAttribute = null) =>
        Report(new ProjectionDiagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = message,
            Code = code,
            RelatedId = relatedId,
            RelatedAttribute = relatedAttribute,
        });
}
