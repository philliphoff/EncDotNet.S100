using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-104 Water Level Information for Surface
/// Navigation datasets. Dispatches on the catalog payload variant:
/// <list type="bullet">
/// <item><description><see cref="S104CoverageData"/> — data coding
/// format 2 (regularly-gridded coverage). Accepts
/// <c>WaterLevel[.NN]</c> for the instance and
/// <c>WaterLevel[.NN].Group_KKK</c> for the K-th time-step group
/// (S-104 Edition 2.0.0 §10.2.1).</description></item>
/// <item><description><see cref="S104StationSeriesData"/> — data
/// coding format 8 (time series at fixed stations; S-104
/// Edition 2.0.0 §10.2.3 / §10.2.7). Accepts <c>WaterLevel[.NN]</c>
/// for the instance, <c>WaterLevel[.NN].Group_KKK</c> for the K-th
/// station, or the bare station identifier string.</description></item>
/// </list>
/// </summary>
internal sealed class S104FeatureDescriber : ISpecFeatureDescriber
{
    private const string InstancePrefix = "WaterLevel";
    private const string InstanceFeatureType = "WaterLevel";
    private const string StationFeatureType = "WaterLevelStation";
    private const float HeightFillValue = S104CoverageSource.FillValue;

    public string SpecName => "S-104";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Dataset.Data switch
        {
            S104CoverageData gridded => DescribeGridded(context, gridded),
            S104StationSeriesData stations => DescribeStations(context, stations),
            _ => ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name)),
        };
    }

    private static ToolResult<DescribeFeatureResult> DescribeGridded(
        FeatureDescriberContext context,
        S104CoverageData payload)
    {
        var dataset = payload.Source.Dataset;

        if (!TryParseInstanceId(context.FeatureId, out var groupIndex))
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        if (groupIndex is null)
        {
            // Instance-level summary.
            var attributes = SerializeGriddedInstance(dataset);
            return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                context.Dataset.Spec,
                InstanceFeatureType,
                attributes,
                ImmutableArray<FeatureReference>.Empty));
        }

        var idx = groupIndex.Value - 1;
        if (idx < 0 || idx >= dataset.Coverages.Count)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var groupAttrs = SerializeGriddedGroup(dataset, groupIndex.Value, dataset.Coverages[idx]);
        return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            InstanceFeatureType,
            groupAttrs,
            ImmutableArray<FeatureReference>.Empty));
    }

    private static ToolResult<DescribeFeatureResult> DescribeStations(
        FeatureDescriberContext context,
        S104StationSeriesData payload)
    {
        var dataset = payload.Dataset;

        // Try parsing as instance/Group_NNN first; otherwise fall back to a station identifier match.
        if (TryParseInstanceId(context.FeatureId, out var groupIndex))
        {
            if (groupIndex is null)
            {
                var attributes = SerializeStationInstance(dataset);
                return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                    context.Dataset.Spec,
                    InstanceFeatureType,
                    attributes,
                    ImmutableArray<FeatureReference>.Empty));
            }

            var idx = groupIndex.Value - 1;
            if (idx < 0 || idx >= dataset.Stations.Count)
            {
                return ToolResult<DescribeFeatureResult>.Err(
                    new FeatureNotFound(context.Dataset.Id, context.FeatureId));
            }

            var stationAttrs = SerializeStation(dataset.Stations[idx]);
            return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                context.Dataset.Spec,
                StationFeatureType,
                stationAttrs,
                ImmutableArray<FeatureReference>.Empty));
        }

        // Try station identifier.
        foreach (var station in dataset.Stations)
        {
            if (string.Equals(station.Identifier, context.FeatureId, StringComparison.Ordinal))
            {
                var attrs = SerializeStation(station);
                return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
                    context.Dataset.Spec,
                    StationFeatureType,
                    attrs,
                    ImmutableArray<FeatureReference>.Empty));
            }
        }

        return ToolResult<DescribeFeatureResult>.Err(
            new FeatureNotFound(context.Dataset.Id, context.FeatureId));
    }

    /// <summary>
    /// Parses ids of the form <c>WaterLevel</c>, <c>WaterLevel.NN</c>, or
    /// <c>WaterLevel.NN.Group_KKK</c>. Returns <c>true</c> with
    /// <paramref name="groupIndex"/> = <c>null</c> for the instance form
    /// (instance number is always 1 in the current catalog projection);
    /// <c>true</c> with <paramref name="groupIndex"/> = K for the group
    /// form; <c>false</c> otherwise.
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

        if (!int.TryParse(instancePart, NumberStyles.None, CultureInfo.InvariantCulture, out var instance))
        {
            return false;
        }
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

    private static JsonElement SerializeGriddedInstance(S104Dataset dataset)
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
                    uniform = false;
                    break;
                }
            }
            if (uniform) intervalSeconds = step;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
            ["methodWaterLevelProduct"] = dataset.MethodWaterLevelProduct,
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
            ["noDataValue"] = HeightFillValue,
            ["geographicIdentifier"] = dataset.GeographicIdentifier,
            ["issueDate"] = dataset.IssueDate,
            ["metadata"] = dataset.Metadata,
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeGriddedGroup(S104Dataset dataset, int groupIndex, WaterLevelCoverage coverage)
    {
        var (minH, maxH, nodata) = ComputeHeightRange(coverage.Values);
        var trends = ComputeTrendCounts(coverage.Values);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["groupIndex"] = groupIndex,
            ["groupId"] = $"Group_{groupIndex:D3}",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
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
            ["heightRange"] = minH is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minH,
                ["max"] = maxH,
                ["nodataCount"] = nodata,
            },
            ["trendCounts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["unknown"] = trends.Unknown,
                ["decreasing"] = trends.Decreasing,
                ["increasing"] = trends.Increasing,
                ["steady"] = trends.Steady,
            },
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeStationInstance(S104StationSeriesDataset dataset)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{InstancePrefix}.01",
            ["featureType"] = InstanceFeatureType,
            ["dataCodingFormat"] = dataset.DataCodingFormat,
            ["methodWaterLevelProduct"] = dataset.MethodWaterLevelProduct,
            ["waterLevelTrendThreshold"] = dataset.WaterLevelTrendThreshold,
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

    private static JsonElement SerializeStation(WaterLevelStation station)
    {
        var (minH, maxH, nodata) = ComputeHeightRange(station.Heights);
        var trends = ComputeTrendCounts(station.Trends);

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
            ["heightRange"] = minH is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = minH,
                ["max"] = maxH,
                ["nodataCount"] = nodata,
            },
            ["trendCounts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["unknown"] = trends.Unknown,
                ["decreasing"] = trends.Decreasing,
                ["increasing"] = trends.Increasing,
                ["steady"] = trends.Steady,
            },
        };

        return ToJsonElement(payload);
    }

    private static (float? Min, float? Max, int NoDataCount) ComputeHeightRange(WaterLevelValue[] values)
    {
        float? min = null; float? max = null; int nodata = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var h = values[i].Height;
            if (h == HeightFillValue) { nodata++; continue; }
            if (min is null || h < min) min = h;
            if (max is null || h > max) max = h;
        }
        return (min, max, nodata);
    }

    private static (float? Min, float? Max, int NoDataCount) ComputeHeightRange(float[] heights)
    {
        float? min = null; float? max = null; int nodata = 0;
        for (int i = 0; i < heights.Length; i++)
        {
            var h = heights[i];
            if (h == HeightFillValue) { nodata++; continue; }
            if (min is null || h < min) min = h;
            if (max is null || h > max) max = h;
        }
        return (min, max, nodata);
    }

    private static (int Unknown, int Decreasing, int Increasing, int Steady) ComputeTrendCounts(WaterLevelValue[] values)
    {
        int u = 0, d = 0, inc = 0, s = 0;
        for (int i = 0; i < values.Length; i++)
        {
            switch (values[i].Trend)
            {
                case 1: d++; break;
                case 2: inc++; break;
                case 3: s++; break;
                default: u++; break;
            }
        }
        return (u, d, inc, s);
    }

    private static (int Unknown, int Decreasing, int Increasing, int Steady) ComputeTrendCounts(byte[] trends)
    {
        int u = 0, d = 0, inc = 0, st = 0;
        for (int i = 0; i < trends.Length; i++)
        {
            switch (trends[i])
            {
                case 1: d++; break;
                case 2: inc++; break;
                case 3: st++; break;
                default: u++; break;
            }
        }
        return (u, d, inc, st);
    }

    private static JsonElement ToJsonElement(object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }
}
