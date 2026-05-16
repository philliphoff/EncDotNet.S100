using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="SampleCoverageAlongTool"/>.
/// </summary>
/// <param name="Spec">The coverage spec to sample (<c>S-102</c>, <c>S-104</c>, or <c>S-111</c>).</param>
/// <param name="Polyline">
/// The polyline to sample. Each vertex is sampled at the supplied
/// coordinate; the polyline's <c>CorridorWidthMeters</c> is ignored
/// (corridor width applies to membership queries, not point sampling).
/// </param>
/// <param name="Time">
/// Optional time selector for time-varying products (S-104, S-111).
/// Applied identically to every vertex — useful for "depth/level at
/// each waypoint at the same instant" queries. Ignored for S-102.
/// </param>
public sealed record SampleCoverageAlongRequest(
    [property: Description("Coverage spec to sample (S-102, S-104, or S-111).")] SpecRef Spec,
    [property: Description("Polyline to sample. Each vertex is sampled at its coordinate; the polyline's CorridorWidthMeters is ignored (corridor width applies to membership queries, not point sampling).")] GeoPolyline Polyline,
    [property: Description("Optional UTC ISO-8601 time selector applied identically to every vertex; for time-varying products (S-104, S-111) only. Ignored for S-102.")] DateTimeOffset? Time = null);

/// <summary>
/// A single sample along the polyline.
/// </summary>
/// <param name="VertexIndex">Zero-based index of the vertex in the request polyline.</param>
/// <param name="Latitude">Latitude of the vertex.</param>
/// <param name="Longitude">Longitude of the vertex.</param>
/// <param name="Result">
/// The per-vertex sample result. <c>null</c> when no dataset of the
/// requested spec covers this vertex (or the spec returned
/// <c>OutOfBounds</c>/<c>NoDataAtPoint</c>); the agent should treat a
/// <c>null</c> entry as "no value available at this position" and
/// continue with the remaining vertices.
/// </param>
public sealed record CoverageSampleAlong(
    int VertexIndex,
    double Latitude,
    double Longitude,
    SampleCoverageResult? Result);

/// <summary>Result of <see cref="SampleCoverageAlongTool"/>.</summary>
/// <param name="Spec">The spec that was sampled.</param>
/// <param name="Samples">One entry per polyline vertex, in input order.</param>
public sealed record SampleCoverageAlongResult(
    [property: Description("The coverage spec that was sampled.")] SpecRef Spec,
    [property: Description("One entry per polyline vertex, in input order. Per-vertex misses (point outside coverage or no data) surface as a sample with a null Result.")] ImmutableArray<CoverageSampleAlong> Samples);

/// <summary>
/// Samples a coverage product (<see cref="SampleCoverageTool"/>) at
/// every vertex of a polyline, returning per-vertex results in input
/// order. Per-vertex failures are tolerated — only request-level
/// failures (invalid geometry, unsupported spec) bubble up as
/// <see cref="ToolError"/>.
/// </summary>
/// <remarks>
/// <para>
/// This tool composes <see cref="SampleCoverageTool"/>: each vertex is
/// sampled independently. It is the natural primitive behind questions
/// like "minimum depth along this route leg" and "max current speed
/// along this transit". Aggregation (min/max/mean) is intentionally
/// not performed here — the agent can compute it from
/// <see cref="SampleCoverageAlongResult.Samples"/>.
/// </para>
/// <para>
/// The polyline is sampled at its supplied vertices only. Callers that
/// want denser sampling should subdivide the polyline before calling
/// (a future <c>sample_coverage_along_dense</c> tool may automate
/// this).
/// </para>
/// </remarks>
public sealed class SampleCoverageAlongTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "sample_coverage_along";

    private readonly SampleCoverageTool _sampler;

    /// <summary>Creates a new <see cref="SampleCoverageAlongTool"/>.</summary>
    public SampleCoverageAlongTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _sampler = new SampleCoverageTool(catalog);
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<SampleCoverageAlongResult>> InvokeAsync(
        SampleCoverageAlongRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Polyline is null)
        {
            return ToolResult<SampleCoverageAlongResult>.Err(
                new InvalidArgument("polyline", "must be supplied"));
        }

        // Reuse the same validation that GeoQuery does for polylines.
        var asQuery = new GeoQuery.Polyline(request.Polyline);
        if (GeoQueryValidator.Validate(asQuery) is { } err)
        {
            return ToolResult<SampleCoverageAlongResult>.Err(err);
        }

        var vertices = request.Polyline.Vertices;
        var samples = ImmutableArray.CreateBuilder<CoverageSampleAlong>(vertices.Length);

        for (var i = 0; i < vertices.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var v = vertices[i];
            var inner = await _sampler.InvokeAsync(
                new SampleCoverageRequest(request.Spec, v.Latitude, v.Longitude, request.Time),
                cancellationToken).ConfigureAwait(false);

            // Request-level errors (unsupported spec) propagate; per-
            // vertex misses (OutOfBounds / NoDataAtPoint / NoDatasetCoversPoint)
            // surface as null entries so a partial route still returns
            // usable data.
            if (inner.TryGetError(out var innerErr))
            {
                if (innerErr is SpecNotSupportedForTool)
                {
                    return ToolResult<SampleCoverageAlongResult>.Err(innerErr);
                }

                samples.Add(new CoverageSampleAlong(i, v.Latitude, v.Longitude, null));
                continue;
            }

            inner.TryGetValue(out var value);
            samples.Add(new CoverageSampleAlong(i, v.Latitude, v.Longitude, value));
        }

        return ToolResult<SampleCoverageAlongResult>.Ok(
            new SampleCoverageAlongResult(request.Spec, samples.MoveToImmutable()));
    }
}
