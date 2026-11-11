using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by coverage dataset processors (and the catalog
/// payloads that carry their data) whose values can be read at a point: S-102
/// depths, S-104 water levels and S-111 surface currents today, weather grids
/// later. Lets the MCP <c>sample_coverage</c> and <c>sample_coverage_along</c>
/// tools sample a product without knowing its specification.
/// </summary>
/// <remarks>
/// <para>
/// A sampler only says <em>where</em> in its dataset a point can be answered
/// (<see cref="FindSites"/>) and reads a value there
/// (<see cref="CoverageSampleSite.Read"/> and
/// <see cref="CoverageSampleSite.ReadSteps"/>). Choosing between datasets and
/// between the sites in them, and the time handling — the nearest time step,
/// the one-step tolerance at either end of a dataset's range, the
/// out-of-range policy and range or series windows — are shared, in
/// <see cref="CoverageSampling"/>, so every product treats time the same way.
/// </para>
/// <para>
/// Values come back as <see cref="SampledValue"/>s. A product without a typed
/// shape of its own returns a <see cref="CoverageFieldsSample"/>, whose
/// <see cref="SampledField"/>s carry each value's quantity and unit, so its
/// fields reach MCP callers with no tool changes. Every typed shape describes
/// its values the same way through <see cref="SampledValue.GetFields"/>.
/// </para>
/// <para>
/// Point sampling at a time, sampling along a polyline and windowed sampling
/// of a single dataset are available as extension methods in
/// <see cref="CoverageSamplerExtensions"/>.
/// </para>
/// </remarks>
public interface ICoverageSampler
{
    /// <summary>
    /// Finds the places in this dataset that can answer a sample at the
    /// query's point: the grid node nearest to it, or every station of a
    /// station-series dataset (the nearest across datasets is chosen later).
    /// </summary>
    /// <param name="query">The point to sample, with the transforms that reproject it into the dataset's CRS.</param>
    /// <returns>
    /// The sites, or an empty search when the dataset does not cover the
    /// point. Implementations may throw <see cref="ObjectDisposedException"/>
    /// when the dataset was closed during the call.
    /// </returns>
    CoverageSiteSearch FindSites(CoverageSampleQuery query);
}
