using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="ListTimeStepsTool"/>.</summary>
/// <param name="DatasetId">
/// Identifier of the time-varying coverage dataset to introspect. Must
/// be currently loaded in the host catalog.
/// </param>
public sealed record ListTimeStepsRequest(
    [property: Description("Identifier of a currently loaded time-varying coverage dataset (S-104 or S-111). The agent typically obtains this from list_datasets.")] DatasetId DatasetId);

/// <summary>Result of <see cref="ListTimeStepsTool"/>.</summary>
/// <param name="DatasetId">The dataset that was introspected, echoed back.</param>
/// <param name="Spec">Spec of the dataset that was introspected.</param>
/// <param name="Times">
/// All UTC time-step instants in ascending order. For gridded datasets
/// these are the per-step <c>TimePoint</c> values; for station-series
/// datasets these are derived from the first station's <c>StartTime</c>
/// + <c>k * TimeRecordInterval</c>.
/// </param>
/// <param name="Cadence">
/// Regular interval between consecutive samples when the series is
/// strictly uniform; otherwise <c>null</c>. Gridded coverages need not
/// be uniform, so callers must not rely on this being set.
/// </param>
/// <param name="FirstTime">First time step in <see cref="Times"/>, or <c>null</c> when empty.</param>
/// <param name="LastTime">Last time step in <see cref="Times"/>, or <c>null</c> when empty.</param>
public sealed record ListTimeStepsResult(
    [property: Description("The dataset that was introspected, echoed back.")] DatasetId DatasetId,
    [property: Description("Spec of the dataset that was introspected.")] SpecRef Spec,
    [property: Description("All UTC time-step instants in ascending order. ISO-8601 with explicit UTC offset.")] ImmutableArray<DateTimeOffset> Times,
    [property: Description("Regular interval between consecutive samples when the series is strictly uniform; otherwise null. Callers must not rely on this being set.")] TimeSpan? Cadence,
    [property: Description("First time step in Times, or null when empty.")] DateTimeOffset? FirstTime,
    [property: Description("Last time step in Times, or null when empty.")] DateTimeOffset? LastTime);

/// <summary>
/// Returns the available time-step instants for a time-varying coverage
/// dataset (S-104 water level, S-111 surface currents). Helps the agent
/// ground temporal questions before issuing <c>sample_coverage</c> /
/// <c>sample_coverage_along</c> calls.
/// </summary>
/// <remarks>
/// <para>
/// Gridded datasets (S-104/S-111 data coding format 2) carry an
/// explicit <c>TimePoint</c> per coverage instance; the result lists
/// these in ascending order. Station-series datasets (data coding
/// format 8) carry <c>StartTime</c> + <c>TimeRecordInterval</c> per
/// station; the result derives the series from the first station and
/// sets <see cref="ListTimeStepsResult.Cadence"/>.
/// </para>
/// <para>
/// S-102 bathymetry is static (no time axis) and returns an empty
/// <see cref="ListTimeStepsResult.Times"/> series. Non-coverage specs
/// raise <see cref="NotSupportedYet"/>.
/// </para>
/// </remarks>
public sealed class ListTimeStepsTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "list_time_steps";

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="ListTimeStepsTool"/>.</summary>
    public ListTimeStepsTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<ListTimeStepsResult>> InvokeAsync(
        ListTimeStepsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        LoadedDataset? match = null;
        foreach (var dataset in _catalog.Datasets)
        {
            if (dataset.Id == request.DatasetId)
            {
                match = dataset;
                break;
            }
        }

        if (match is null)
        {
            return Task.FromResult(ToolResult<ListTimeStepsResult>.Err(
                new DatasetNotFound(request.DatasetId)));
        }

        return Task.FromResult(match.Data switch
        {
            S102CoverageData => Empty(match),
            S104CoverageData s104g => FromGridded(match, ExtractWaterLevelTimes(s104g)),
            S104StationSeriesData s104s => FromStations(match, ExtractStationTimes(s104s)),
            S111CoverageData s111g => FromGridded(match, ExtractSurfaceCurrentTimes(s111g)),
            S111StationSeriesData s111s => FromStations(match, ExtractStationTimes(s111s)),
            _ => ToolResult<ListTimeStepsResult>.Err(new NotSupportedYet(
                match.Spec,
                Name,
                $"spec '{match.Spec.Name}' is not a coverage product with a time axis")),
        });
    }

    private static ToolResult<ListTimeStepsResult> Empty(LoadedDataset dataset) =>
        ToolResult<ListTimeStepsResult>.Ok(new ListTimeStepsResult(
            dataset.Id, dataset.Spec,
            ImmutableArray<DateTimeOffset>.Empty,
            Cadence: null, FirstTime: null, LastTime: null));

    private static ToolResult<ListTimeStepsResult> FromGridded(
        LoadedDataset dataset,
        IEnumerable<DateTimeOffset> times)
    {
        var ordered = times.OrderBy(t => t).ToImmutableArray();
        var cadence = DetectCadence(ordered);
        return ToolResult<ListTimeStepsResult>.Ok(new ListTimeStepsResult(
            dataset.Id, dataset.Spec, ordered, cadence,
            ordered.Length == 0 ? null : ordered[0],
            ordered.Length == 0 ? null : ordered[^1]));
    }

    private static ToolResult<ListTimeStepsResult> FromStations(
        LoadedDataset dataset,
        (DateTimeOffset Start, TimeSpan Interval, int Count)? series)
    {
        if (series is null)
        {
            return Empty(dataset);
        }

        var (start, interval, count) = series.Value;
        var builder = ImmutableArray.CreateBuilder<DateTimeOffset>(count);
        for (int i = 0; i < count; i++)
        {
            builder.Add(start + TimeSpan.FromTicks(interval.Ticks * i));
        }
        var times = builder.MoveToImmutable();
        return ToolResult<ListTimeStepsResult>.Ok(new ListTimeStepsResult(
            dataset.Id, dataset.Spec, times,
            Cadence: interval > TimeSpan.Zero ? interval : null,
            FirstTime: times.Length == 0 ? null : times[0],
            LastTime: times.Length == 0 ? null : times[^1]));
    }

    private static IEnumerable<DateTimeOffset> ExtractWaterLevelTimes(S104CoverageData payload)
    {
        foreach (var step in payload.Source.Dataset.Coverages)
        {
            yield return new DateTimeOffset(DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc));
        }
    }

    private static IEnumerable<DateTimeOffset> ExtractSurfaceCurrentTimes(S111CoverageData payload)
    {
        foreach (var step in payload.Source.Dataset.Coverages)
        {
            yield return new DateTimeOffset(DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc));
        }
    }

    private static (DateTimeOffset Start, TimeSpan Interval, int Count)? ExtractStationTimes(S104StationSeriesData payload)
    {
        var stations = payload.Dataset.Stations;
        if (stations.Count == 0) return null;
        var first = stations[0];
        if (first.NumberOfTimes <= 0) return null;
        return (
            new DateTimeOffset(DateTime.SpecifyKind(first.StartTime, DateTimeKind.Utc)),
            first.TimeRecordInterval,
            first.NumberOfTimes);
    }

    private static (DateTimeOffset Start, TimeSpan Interval, int Count)? ExtractStationTimes(S111StationSeriesData payload)
    {
        var stations = payload.Dataset.Stations;
        if (stations.Count == 0) return null;
        var first = stations[0];
        if (first.NumberOfTimes <= 0) return null;
        return (
            new DateTimeOffset(DateTime.SpecifyKind(first.StartTime, DateTimeKind.Utc)),
            first.TimeRecordInterval,
            first.NumberOfTimes);
    }

    // Returns the common cadence if every adjacent pair is equally
    // spaced; null otherwise. Single-element series returns null.
    private static TimeSpan? DetectCadence(ImmutableArray<DateTimeOffset> times)
    {
        if (times.Length < 2) return null;
        var first = times[1] - times[0];
        if (first <= TimeSpan.Zero) return null;
        for (int i = 2; i < times.Length; i++)
        {
            if (times[i] - times[i - 1] != first) return null;
        }
        return first;
    }
}
