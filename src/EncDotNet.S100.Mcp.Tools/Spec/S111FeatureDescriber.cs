using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-111 Surface Currents datasets. Dispatches
/// on the catalog payload variant:
/// <list type="bullet">
/// <item><description><see cref="S111CoverageData"/> — data coding
/// format 2 (regularly-gridded coverage). Accepts
/// <c>SurfaceCurrent[.NN]</c> for the instance and
/// <c>SurfaceCurrent[.NN].Group_KKK</c> for the K-th time-step group
/// (S-111 Edition 2.0.0 §10.2).</description></item>
/// <item><description><see cref="S111StationSeriesData"/> — data
/// coding format 8 (time series at fixed stations; S-111
/// Edition 2.0.0 §10.2.3 / §10.2.7). Accepts <c>SurfaceCurrent[.NN]</c>,
/// <c>SurfaceCurrent[.NN].Group_KKK</c>, or the bare station
/// identifier.</description></item>
/// </list>
/// </summary>
internal sealed class S111FeatureDescriber : ISpecFeatureDescriber
{
    private const string InstancePrefix = "SurfaceCurrent";
    private const string InstanceFeatureType = "SurfaceCurrent";
    private const string StationFeatureType = "SurfaceCurrentStation";

    public string SpecName => "S-111";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Dataset.Data switch
        {
            S111CoverageData gridded => DescribeGridded(context, gridded),
            S111StationSeriesData stations => DescribeStations(context, stations),
            _ => ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name)),
        };
    }

    private static ToolResult<DescribeFeatureResult> DescribeGridded(
        FeatureDescriberContext context,
        S111CoverageData payload)
    {
        var dataset = payload.Source.Dataset;

        if (!TryParseInstanceId(context.FeatureId, out var groupIndex))
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        if (groupIndex is null)
        {
            return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                context.Dataset.Spec,
                InstanceFeatureType,
                SerializeGriddedInstance(dataset),
                ImmutableArray<FeatureReference>.Empty));
        }

        var idx = groupIndex.Value - 1;
        if (idx < 0 || idx >= dataset.Coverages.Count)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            InstanceFeatureType,
            SerializeGriddedGroup(dataset, groupIndex.Value, dataset.Coverages[idx]),
            ImmutableArray<FeatureReference>.Empty));
    }

    private static ToolResult<DescribeFeatureResult> DescribeStations(
        FeatureDescriberContext context,
        S111StationSeriesData payload)
    {
        var dataset = payload.Dataset;

        if (TryParseInstanceId(context.FeatureId, out var groupIndex))
        {
            if (groupIndex is null)
            {
                return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                    context.Dataset.Spec,
                    InstanceFeatureType,
                    SerializeStationInstance(dataset),
                    ImmutableArray<FeatureReference>.Empty));
            }

            var idx = groupIndex.Value - 1;
            if (idx < 0 || idx >= dataset.Stations.Count)
            {
                return ToolResult<DescribeFeatureResult>.Err(
                    new FeatureNotFound(context.Dataset.Id, context.FeatureId));
            }

            return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                context.Dataset.Spec,
                StationFeatureType,
                SerializeStation(dataset.Stations[idx]),
                ImmutableArray<FeatureReference>.Empty));
        }

        foreach (var station in dataset.Stations)
        {
            if (string.Equals(station.Identifier, context.FeatureId, StringComparison.Ordinal))
            {
                return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                    context.Dataset.Spec,
                    StationFeatureType,
                    SerializeStation(station),
                    ImmutableArray<FeatureReference>.Empty));
            }
        }

        return ToolResult<DescribeFeatureResult>.Err(
            new FeatureNotFound(context.Dataset.Id, context.FeatureId));
    }

    /// <summary>
    /// Parses ids of the form <c>SurfaceCurrent</c>,
    /// <c>SurfaceCurrent.NN</c>, or
    /// <c>SurfaceCurrent.NN.Group_KKK</c>.
    /// </summary>
    internal static bool TryParseInstanceId(string id, out int? groupIndex)
    {
        groupIndex = null;
        if (string.IsNullOrEmpty(id)) return false;

        if (string.Equals(id, InstancePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        if (!id.StartsWith(InstancePrefix + ".", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = id[(InstancePrefix.Length + 1)..];
        var dot = rest.IndexOf('.');
        var instancePart = dot < 0 ? rest : rest[..dot];
        if (!int.TryParse(instancePart, NumberStyles.None, CultureInfo.InvariantCulture, out var instance)) return false;
        if (instance != 1) return false;

        if (dot < 0) return true;

        var groupPart = rest[(dot + 1)..];
        if (!groupPart.StartsWith("Group_", StringComparison.Ordinal)) return false;
        var nPart = groupPart["Group_".Length..];
        if (!int.TryParse(nPart, NumberStyles.None, CultureInfo.InvariantCulture, out var k)) return false;
        if (k < 1) return false;

        groupIndex = k;
        return true;
    }

    private static JsonElement SerializeGriddedInstance(S111Dataset dataset)
    {
        var first = dataset.Coverages.Count > 0 ? dataset.Coverages[0] : null;

        DateTime? firstTime = dataset.Coverages.Count > 0 ? dataset.Coverages[0].TimePoint : null;
        DateTime? lastTime = dataset.Coverages.Count > 0 ? dataset.Coverages[^1].TimePoint : null;
        double? intervalSeconds = null;
        if (dataset.Coverages.Count >= 2)
        {
            var t0 = dataset.Coverages[0].TimePoint;
            var t1 = dataset.Coverages[1].TimePoint;
            var step = (t1 - t0).TotalSeconds;
            var uniform = true;
            for (int i = 2; i < dataset.Coverages.Count; i++)
            {
                if (Math.Abs((dataset.Coverages[i].TimePoint - dataset.Coverages[i - 1].TimePoint).TotalSeconds - step) > 1e-3)
                {
                    uniform = false; break;
                }
            }
            if (uniform) intervalSeconds = step;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
            ["typeOfCurrentData"] = dataset.TypeOfCurrentData,
            ["surfaceCurrentDepth"] = dataset.SurfaceCurrentDepth,
            ["gridMetadata"] = first is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["originLatitude"] = first.OriginLatitude,
                ["originLongitude"] = first.OriginLongitude,
                ["spacingLatitudinal"] = first.SpacingLatitudinal,
                ["spacingLongitudinal"] = first.SpacingLongitudinal,
                ["numPointsLatitudinal"] = first.NumPointsLatitudinal,
                ["numPointsLongitudinal"] = first.NumPointsLongitudinal,
            },
            ["boundingBox"] = first is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["south"] = first.OriginLatitude,
                ["west"] = first.OriginLongitude,
                ["north"] = first.OriginLatitude + first.SpacingLatitudinal * first.NumPointsLatitudinal,
                ["east"] = first.OriginLongitude + first.SpacingLongitudinal * first.NumPointsLongitudinal,
            },
            ["horizontalCRS"] = dataset.HorizontalCRS,
            ["epoch"] = dataset.Epoch,
            ["timeSteps"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = dataset.Coverages.Count,
                ["first"] = firstTime,
                ["last"] = lastTime,
                ["intervalSeconds"] = intervalSeconds,
            },
            ["geographicIdentifier"] = dataset.GeographicIdentifier,
            ["issueDate"] = dataset.IssueDate,
            ["metadata"] = dataset.Metadata,
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeGriddedGroup(S111Dataset dataset, int groupIndex, SurfaceCurrentCoverage coverage)
    {
        var (minS, maxS, minD, maxD) = ComputeSpeedDirectionRange(coverage.Values);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["groupIndex"] = groupIndex,
            ["groupId"] = $"Group_{groupIndex:D3}",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
            ["typeOfCurrentData"] = dataset.TypeOfCurrentData,
            ["surfaceCurrentDepth"] = dataset.SurfaceCurrentDepth,
            ["timePoint"] = coverage.TimePoint,
            ["gridMetadata"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["originLatitude"] = coverage.OriginLatitude,
                ["originLongitude"] = coverage.OriginLongitude,
                ["spacingLatitudinal"] = coverage.SpacingLatitudinal,
                ["spacingLongitudinal"] = coverage.SpacingLongitudinal,
                ["numPointsLatitudinal"] = coverage.NumPointsLatitudinal,
                ["numPointsLongitudinal"] = coverage.NumPointsLongitudinal,
            },
            ["speedRange"] = minS is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minS,
                ["max"] = maxS,
                ["units"] = "knots",
            },
            ["directionRange"] = minD is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minD,
                ["max"] = maxD,
                ["units"] = "degrees true",
            },
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeStationInstance(S111StationSeriesDataset dataset)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
            ["typeOfCurrentData"] = dataset.TypeOfCurrentData,
            ["surfaceCurrentDepth"] = dataset.SurfaceCurrentDepth,
            ["horizontalCRS"] = dataset.HorizontalCRS,
            ["epoch"] = dataset.Epoch,
            ["stations"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = dataset.Stations.Count,
            },
            ["timeRange"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = dataset.MinTime,
                ["max"] = dataset.MaxTime,
            },
            ["geographicIdentifier"] = dataset.GeographicIdentifier,
            ["issueDate"] = dataset.IssueDate,
            ["metadata"] = dataset.Metadata,
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeStation(SurfaceCurrentStation station)
    {
        var (minS, maxS) = ComputeRange(station.SpeedsMetresPerSecond);
        var (minD, maxD) = ComputeRange(station.DirectionsDegreesTrue);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["stationId"] = station.Identifier,
            ["featureType"] = StationFeatureType,
            ["position"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["latitude"] = station.Latitude,
                ["longitude"] = station.Longitude,
            },
            ["timeRange"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["start"] = station.StartTime,
                ["end"] = station.EndTime,
                ["intervalSeconds"] = station.TimeRecordInterval.TotalSeconds,
                ["count"] = station.NumberOfTimes,
            },
            ["speedRange"] = minS is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minS,
                ["max"] = maxS,
                ["units"] = "metres/second",
            },
            ["directionRange"] = minD is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minD,
                ["max"] = maxD,
                ["units"] = "degrees true",
            },
        };

        return ToJsonElement(payload);
    }

    private static (float? MinSpeed, float? MaxSpeed, float? MinDir, float? MaxDir) ComputeSpeedDirectionRange(SurfaceCurrentValue[] values)
    {
        float? minS = null, maxS = null, minD = null, maxD = null;
        for (int i = 0; i < values.Length; i++)
        {
            var s = values[i].Speed;
            var d = values[i].Direction;
            if (minS is null || s < minS) minS = s;
            if (maxS is null || s > maxS) maxS = s;
            if (minD is null || d < minD) minD = d;
            if (maxD is null || d > maxD) maxD = d;
        }
        return (minS, maxS, minD, maxD);
    }

    private static (float? Min, float? Max) ComputeRange(float[] xs)
    {
        float? min = null, max = null;
        for (int i = 0; i < xs.Length; i++)
        {
            var v = xs[i];
            if (min is null || v < min) min = v;
            if (max is null || v > max) max = v;
        }
        return (min, max);
    }

    private static JsonElement ToJsonElement(object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }
}
