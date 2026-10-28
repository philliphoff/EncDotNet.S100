using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// The catalog payload of a weather grid dataset (NetCDF-CF or GRIB2): a
/// <see cref="ProductDatasetData"/> that describes itself and samples its
/// grid, so <c>sample_coverage</c> and
/// <c>sample_coverage_along</c> answer with wave heights and winds and their
/// units with no MCP change (design note weather-sources.md §8.8).
/// </summary>
/// <remarks>
/// The payload holds the open grid set, whose time steps are read from the
/// file when sampled. A payload opened from a stream
/// (<see cref="ProductRegistration.OpenData"/>) keeps an in-memory copy of the
/// file's bytes for that; one projected from a processor
/// (<see cref="ILoadedDatasetProjection"/>) shares the processor's file.
/// </remarks>
public sealed record WeatherGridDatasetData : ProductDatasetData, ICoverageSampler, ITimeSteppedDatasetData
{
    private readonly Sampling.WeatherGridSampler _sampler;

    /// <summary>Creates the payload.</summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="metadata">The dataset's spec, extent and time coverage.</param>
    /// <param name="referenceTime">The model run's reference time, or <see langword="null"/>.</param>
    public WeatherGridDatasetData(WeatherGridSet gridSet, DatasetMetadata metadata, DateTime? referenceTime)
        : this(gridSet, metadata, new Sampling.WeatherGridSampler(gridSet, referenceTime))
    {
    }

    internal WeatherGridDatasetData(WeatherGridSet gridSet, DatasetMetadata metadata, Sampling.WeatherGridSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(gridSet);
        ArgumentNullException.ThrowIfNull(metadata);
        GridSet = gridSet;
        Metadata = metadata;
        _sampler = sampler;
    }

    /// <summary>The grid set.</summary>
    public WeatherGridSet GridSet { get; }

    /// <inheritdoc />
    public override DatasetMetadata Metadata { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Every known parameter at the grid node nearest the point, at the step
    /// nearest the time, as a <see cref="CoverageFieldsSample"/>: wave height
    /// in <c>m</c>, wind speed in <c>m/s</c> and <c>kn</c>, directions in
    /// <c>deg</c> ("from"), periods in <c>s</c>.
    /// </remarks>
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => _sampler.FindSites(query);

    /// <inheritdoc />
    /// <remarks>The valid times of the grid set's steps, the steps the processor draws.</remarks>
    public IReadOnlyList<DateTime> TimeSteps => _sampler.Times;
}
