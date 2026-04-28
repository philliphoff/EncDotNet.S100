namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// A latitude/longitude position in the WGS-84 (EPSG:4326) coordinate
/// reference system used throughout S-421.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
public readonly record struct GeoPosition(double Latitude, double Longitude);

/// <summary>
/// Severity of a diagnostic produced while projecting an
/// <see cref="S421Dataset"/> into the strongly-typed
/// <see cref="S421RoutePlan"/> model.
/// </summary>
public enum S421DiagnosticSeverity
{
    /// <summary>An informational note (e.g. an unrecognised attribute was preserved verbatim).</summary>
    Info,
    /// <summary>A non-fatal anomaly (e.g. an <c>xlink:href</c> could not be resolved).</summary>
    Warning,
    /// <summary>A fatal projection failure (e.g. the dataset has no <c>Route</c> feature).</summary>
    Error,
}

/// <summary>
/// A diagnostic message emitted while projecting an <see cref="S421Dataset"/>
/// feature bag into the strongly-typed <see cref="S421RoutePlan"/> model.
/// </summary>
public sealed class S421ProjectionDiagnostic
{
    /// <summary>The diagnostic severity.</summary>
    public required S421DiagnosticSeverity Severity { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// The <c>gml:id</c> of the feature or information type the issue
    /// relates to, when applicable.
    /// </summary>
    public string? RelatedId { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        RelatedId is null
            ? $"[{Severity}] {Message}"
            : $"[{Severity}] {Message} (id: {RelatedId})";
}
