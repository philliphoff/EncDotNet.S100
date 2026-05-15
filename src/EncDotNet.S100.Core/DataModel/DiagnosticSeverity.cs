namespace EncDotNet.S100.DataModel;

/// <summary>
/// Severity of a <see cref="ProjectionDiagnostic"/> emitted while projecting
/// a feature-bag dataset into a strongly-typed data model.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>An informational note (e.g. an unrecognised attribute was preserved verbatim).</summary>
    Info,

    /// <summary>A non-fatal anomaly (e.g. an <c>xlink:href</c> could not be resolved, an attribute would not parse).</summary>
    Warning,

    /// <summary>A fatal projection failure that the projection author chose to surface as a diagnostic rather than an exception.</summary>
    Error,
}
