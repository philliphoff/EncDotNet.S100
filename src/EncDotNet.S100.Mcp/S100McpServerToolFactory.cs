using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Specifications;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Builds <see cref="McpServerTool"/> wrappers around the MCP-1 tools
/// (<see cref="ListDatasetsTool"/>, <see cref="DescribeFeatureTool"/>,
/// <see cref="SampleTool"/>, <see cref="QueryFeaturesTool"/> and the rest),
/// translating <see cref="ToolResult{T}"/> outcomes into MCP
/// <see cref="CallToolResult"/> payloads.
/// </summary>
/// <remarks>
/// <para>
/// On success, the result <c>Value</c> is serialised to JSON and
/// attached as a single <see cref="TextContentBlock"/>.
/// </para>
/// <para>
/// On failure, the wrapper returns <c>isError = true</c> with a
/// structured JSON payload <c>{ "code", "message", "details" }</c>;
/// <c>details</c> serialises any non-base members of the concrete
/// <see cref="ToolError"/> subtype so callers can recover dataset
/// IDs, feature IDs, or sample coordinates without parsing the
/// human-readable message.
/// </para>
/// </remarks>
internal static class S100McpServerToolFactory
{
    private static JsonSerializerOptions JsonOptions => McpJson.Options;

    public static IEnumerable<McpServerTool> CreateTools(
        ListDatasetsTool listDatasets,
        DescribeFeatureTool describeFeature,
        DescribeFeatureTypeTool describeFeatureType,
        SampleTool sample,
        IdentifyFeaturesTool identifyFeatures,
        NearestFeaturesTool nearestFeatures,
        QueryFeaturesTool queryFeatures,
        SearchFeaturesTool searchFeatures,
        ListSpecsTool listSpecs,
        ListTimeStepsTool listTimeSteps,
        CheckRouteTool checkRoute,
        PlanRouteTool planRoute)
    {
        yield return McpToolsets.Assign(McpToolsets.Query, CreateListDatasetsTool(listDatasets));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateDescribeFeatureTool(describeFeature));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateDescribeFeatureTypeTool(describeFeatureType));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateSampleTool(sample));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateIdentifyFeaturesTool(identifyFeatures));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateNearestFeaturesTool(nearestFeatures));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateQueryFeaturesTool(queryFeatures));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateSearchFeaturesTool(searchFeatures));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateListSpecsTool(listSpecs));
        yield return McpToolsets.Assign(McpToolsets.Query, CreateListTimeStepsTool(listTimeSteps));
        yield return McpToolsets.Assign(McpToolsets.Routes, CreateCheckRouteTool(checkRoute));
        yield return McpToolsets.Assign(McpToolsets.Routes, CreatePlanRouteTool(planRoute));
    }

    private static McpServerTool CreateListDatasetsTool(ListDatasetsTool inner)
    {
        var description =
            "Lists the S-100 datasets loaded in the host (viewer or CLI): ids, spec, bounds (decimal degrees, WGS-84) and UTC time range, " +
            "paginated. Filter by spec, and by place: latitude/longitude keeps the datasets whose declared bounding box contains the point, " +
            "'query' (box, polygon or polyline) those whose bounding box intersects it. Bounding boxes only: a match doesn't mean the dataset " +
            "has data there (sample reads the value). Read-only and side-effect free.";

        var del = ([Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("A point, in decimal degrees WGS-84: keep the datasets whose bounding box contains it. Give with longitude, instead of 'query'.")] double? latitude = null,
                   [Description("The point's longitude, with latitude.")] double? longitude = null,
                   [Description(GeoQueryEnvelopeDescription + " Keeps the datasets whose bounding box intersects it (a point: contains it).")] JsonElement? query = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new ListDatasetsRequest(
                        ParseSpec(spec),
                        OptionalQueryOrPoint(query, latitude, longitude),
                        page,
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListDatasetsTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    // The spatial query envelope's shapes, as every tool that takes one describes it.
    private const string GeoQueryEnvelopeDescription =
        "Spatial query, as a JSON object (or a string holding one): {\"kind\":\"point\",\"latitude\":lat,\"longitude\":lon}, " +
        "{\"kind\":\"box\",\"south\":s,\"west\":w,\"north\":n,\"east\":e}, {\"kind\":\"polygon\",\"ring\":[[lat,lon],...]} or " +
        "{\"kind\":\"polyline\",\"vertices\":[[lat,lon],...],\"corridorWidthMeters\":w}.";

    private static McpServerTool CreateDescribeFeatureTool(DescribeFeatureTool inner)
    {
        var description =
            "Returns spec, feature-type code, attributes (as a JSON object), and xlink-resolved cross-references " +
            "for a single feature in a loaded vector dataset. Works across every vector spec — S-101 (ISO 8211 " +
            "ENC; featureId is the record's decimal RCID) plus the GML specs S-122, S-124, S-125, S-127, S-128, " +
            "S-129, S-131, S-201, S-411, and S-421. References for backfilled GML specs are currently returned " +
            "empty (spec-specific reference resolution is staged). An S-101 area, line or sounding group's coordinates can run to hundreds of " +
            "kilobytes: pass includeGeometry: false to keep only its primitive, bounding box and coordinate counts. For just a feature's " +
            "depths or attribute values, identify_features, query_features and nearest_features give 'depth' and, with includeAttributes, " +
            "the attributes. Read-only and side-effect free.";

        var del = ([Description("Stable dataset identifier returned by list_datasets.")] string datasetId,
                   [Description("Identifier of the feature within the dataset: the GML id for GML specs, or the decimal record id (RCID) for S-101.")] string featureId,
                   [Description("Include the feature's coordinates (default true). False keeps the geometry's primitive and boundingBox and gives coordinateCount and interiorRingCount instead.")] bool includeGeometry = true,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new DescribeFeatureRequest(new DatasetId(datasetId), featureId, includeGeometry),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = DescribeFeatureTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateDescribeFeatureTypeTool(DescribeFeatureTypeTool inner)
    {
        var description =
            "Introspects a spec's bundled Feature Catalogue (ISO 19110 / S-100 Part 5): the " +
            "schema-discovery counterpart to query_features. Call with just a spec " +
            "(e.g. \"S-101\" or \"S-124/1.5.0\") to list every feature type with its attribute count; " +
            "add a featureType (code, name, or alias) to get that type's attribute bindings — each " +
            "attribute's value type, whether it is mandatory and/or repeatable, and its enumerated " +
            "listed values. Use it to build valid attribute predicates without a loaded dataset. " +
            "The spec name is normalised, so casing and an optional edition suffix " +
            "(e.g. \"s101\" or \"S-101/1.2.0\") are accepted. Specs without a bundled Feature " +
            "Catalogue return feature_catalogue_not_available, whose details list the accepted " +
            "spec names. Read-only and side-effect free.";

        var del = ([Description("Product specification whose bundled Feature Catalogue to inspect (e.g. \"S-101\" or \"S-124/1.5.0\"). Edition is ignored. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement spec,
                   [Description("Optional feature-type code, name, or alias (case-insensitive). Null lists every feature type with attribute counts only; supplied returns full attribute detail.")] string? featureType = null,
                   [Description("When true (default), enumerated attributes carry their full listed values; set false to omit them.")] bool includeListedValues = true,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new DescribeFeatureTypeRequest(
                        ParseSpec(spec) ?? throw new ArgumentException("spec is required.", nameof(spec)),
                        featureType,
                        includeListedValues),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = DescribeFeatureTypeTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private const string OutOfRangeDescription =
        "What to do when a single requested time is outside the time range of every dataset covering the point: " +
        "\"error\" (default) returns time_out_of_range with the nearest step; \"nearest\" samples that step and sets " +
        "timeStatus to \"before_start\" or \"after_end\". Range/series queries are unaffected.";

    private static TimeOutOfRangePolicy ParseOutOfRange(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "error" => TimeOutOfRangePolicy.Error,
        "nearest" => TimeOutOfRangePolicy.Nearest,
        _ => throw new ArgumentException($"outOfRange must be \"error\" or \"nearest\" (got \"{value}\").", "outOfRange"),
    };

    private static McpServerTool CreateSampleTool(SampleTool inner)
    {
        var description =
            "Samples a quantity at a point (latitude/longitude) or along a polyline, from the loaded data. Quantities: " +
            "'depth': charted depth by the route check's rule (S-102 first, else the finest overlapping S-101, S-401 or translated S-57 cell's " +
            "dredged area, depth area or nearest sounding), with its kind, dataset, featureId (for describe_feature) or S-102 gridCell, the area's " +
            "depth range, and waterMask (water, land or uncharted; where there's no depth, depth is null); with a time, also the waterLevel there " +
            "(S-104, else a tide prediction where the host has them on; status applied, datum_mismatch, no_coverage, before_start, after_end or no_data) " +
            "and availableDepthMeters = depth + water level where it applies, as check_route computes clearance. " +
            "'water_level' (S-104: height in metres and trend), 'current' (S-111: speed in m/s and knots, direction toward, degrees true) and " +
            "'weather' (GRIB2 or NetCDF fields: waves, wind, gusts, pressure; directions 'from'; model data): the nearest grid cell or station " +
            "at the nearest time step, no interpolation. Of the datasets covering the point, one whose time range contains the time is used " +
            "(finest grid first, then newest run). A time outside every covering dataset's range returns time_out_of_range (details: " +
            "requestedTime, datasetId, validFrom, validTo, nearestStep, run, candidates) unless outOfRange=\"nearest\" (then timeStatus " +
            "\"before_start\" or \"after_end\"). Range/series 'times' return a 'series' (truncated=true with coveredFrom/coveredTo when the data " +
            "doesn't span it); depth takes one time. Where no S-104 or S-111 covers and the host has tide predictions on, the nearest tide or " +
            "current station within reach by water (10 km for tide stations, 2 km for current stations) is used: 'source' then names it (kind \"prediction\") with a caveat to quote; " +
            "beyond every station's reach the error is no_station_within_reach, naming the nearest station and its distance. " +
            "For depth at a point, minDepthMeters (with optional clearanceMeters and searchRadiusMeters, default 5000 m) also checks the point is navigable " +
            "(charted water at least that deep, no danger, and that much room from land, shoal, dangers and uncharted water): 'navigable', and when not, " +
            "'notNavigableReason' (land, uncharted, shoal, hazard, no_depth, too_close) and 'nearestNavigable' (latitude, longitude, depthMeters, depth source, " +
            "clearanceMeters, distanceMeters, bearingDegrees; or a 'reason' when nothing in the radius qualifies). " +
            "'spec' pins one product (S-102 depth means bathymetry only). A polyline returns 'samples' in order, each with legIndex, vertexIndex, " +
            "distanceMeters and its result, or an 'error' where that point has none; spacingMeters adds points between vertices along each rhumb-line " +
            "leg (at most " + SampleTool.MaxProfileSamples + " samples); for depth, 'shoalest' names the shallowest sample. CAP warnings are areas: use " +
            "identify_features. Not for navigation. Read-only and side-effect free.";

        var del = ([Description("What to sample: \"depth\", \"water_level\", \"current\" or \"weather\". May be omitted when 'spec' names the product.")] string? quantity = null,
                   [Description("Sample latitude in decimal degrees, WGS-84, -90..+90. With longitude, instead of 'polyline'.")] double? latitude = null,
                   [Description("Sample longitude in decimal degrees, WGS-84, -180..+180.")] double? longitude = null,
                   [Description("Polyline to sample instead of a point: {\"vertices\":[[lat,lon],...]} (a JSON object or a string holding one). Corridor width is ignored.")] JsonElement? polyline = null,
                   [Description("With a polyline, the longest distance in metres between samples along each leg; omit to sample the vertices only.")] double? spacingMeters = null,
                   [Description("Optional product to pin (S-102, S-104, S-111, GRIB2 or NetCDF; e.g. \"S-104\"). Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("Optional UTC ISO-8601 time: the time step for water level, current and weather (the first step when omitted), and the time of the water level for depth.")] DateTimeOffset? time = null,
                   [Description("Optional temporal query JSON envelope: {\"kind\":\"instant\",\"t\":\"…\"}, {\"kind\":\"range\",\"from\":\"…\",\"to\":\"…\"} or {\"kind\":\"series\",\"from\":\"…\",\"to\":\"…\",\"stepSeconds\":1800}. Takes precedence over 'time'; depth takes an instant only.")] string? times = null,
                   [Description(OutOfRangeDescription)] string? outOfRange = null,
                   [Description("For depth at a point: the least charted depth in metres the point needs (e.g. draft plus margin, or the safety contour). When the point fails, the result adds 'nearestNavigable'.")] double? minDepthMeters = null,
                   [Description("With minDepthMeters: the room wanted round the point, in metres, from land, shoal water, dangers and uncharted water. Default 0.")] double? clearanceMeters = null,
                   [Description("With minDepthMeters: how far to look for a navigable point, in metres. Default 5000, at most 20000; nearer water is searched on finer cells first.")] double? searchRadiusMeters = null,
                   CancellationToken ct = default) =>
            DispatchAsync(async () =>
            {
                var result = await inner.InvokeAsync(
                    new SampleRequest(
                        SampleTool.ParseQuantity(quantity),
                        ParseSpec(spec),
                        latitude,
                        longitude,
                        ParsePolyline(polyline),
                        spacingMeters,
                        time,
                        ParseTimeQuery(times),
                        ParseOutOfRange(outOfRange),
                        minDepthMeters,
                        clearanceMeters,
                        searchRadiusMeters),
                    ct).ConfigureAwait(false);
                return result.TryGetValue(out var output)
                    ? ToolResult<object>.Ok(output.Payload)
                    : ToolResult<object>.Err(result.TryGetError(out var error) ? error : new InvalidArgument(string.Empty, "no result"));
            });

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SampleTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateIdentifyFeaturesTool(IdentifyFeaturesTool inner)
    {
        var description =
            "Identifies the vector features at a geographic point — the ECDIS cursor-pick — ranked " +
            "by drawing priority where the dataset was portrayed (S-98 15.1), then most-specific " +
            "first (point features before curves before areas; within a primitive the " +
            "smaller/nearer feature wins). The feature-aware complement to list_datasets with a point " +
            "(which only answers which datasets' bounds cover it). Area features use exact point-in-" +
            "polygon containment (interior-ring holes honoured); point and curve features match " +
            "within radiusMeters. Works across every vector spec (S-101, S-122, S-124, S-125, " +
            "S-127, S-128, S-129, S-131, S-201, S-411, S-412, S-421, CAP and vector weather). " +
            "Time-dependent products (S-412 and CAP warnings, surface analyses, tropical cyclones) " +
            "return only the features drawn at 'time'; without it, a host with a map (SoundCharts) " +
            "returns what its map draws at the view time, hidden S-124 parts left out, and a headless " +
            "host returns every drawable feature. Each match reports the dataset ID, " +
            "spec, feature ID and type, geometry primitive, bounds, containment ('inside'/'near'), " +
            "and approximate distance, and 'depth' with its numeric depth values in metres where it has any (depthRangeMinimumMeters, " +
            "depthRangeMaximumMeters, valueOfSoundingMeters, waterLevelEffect); includeAttributes adds every attribute as encoded. " +
            "Use describe_feature for decoded attributes and geometry.";

        var del = ([Description("Pick latitude in decimal degrees, WGS-84. Must be in [-90, 90].")] double latitude,
                   [Description("Pick longitude in decimal degrees, WGS-84. Must be in [-180, 180].")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double radiusMeters = 50.0,
                   [Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int maxResults = 20,
                   [Description("Optional UTC ISO-8601 time to identify at: time-dependent products (S-412, CAP, surface analyses, tropical cyclones) return only the features drawn then. Omit to follow the host's map (SoundCharts: the view time and the warnings it shows), or, in a headless host, to identify every drawable feature.")] DateTimeOffset? time = null,
                   [Description("Add each feature's attributes as encoded (code and raw value; a complex attribute's parts as complex.sub) in 'attributes'. Default false. Numeric depths are in 'depth' either way.")] bool includeAttributes = false,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new IdentifyFeaturesRequest(
                        latitude,
                        longitude,
                        ParseSpec(spec),
                        radiusMeters,
                        maxResults,
                        time,
                        includeAttributes),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = IdentifyFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateNearestFeaturesTool(NearestFeaturesTool inner)
    {
        var description =
            "Ranks the vector features nearest to a geographic point by TRUE geometric distance — " +
            "the distance-ranking and containment query that list_datasets (dataset-bbox membership) and " +
            "query_features (feature-bbox intersection) can't answer. Answers \"nearest light/buoy/" +
            "berth to my position?\" and \"is this point inside any restricted area?\" in one call: " +
            "an area feature containing the point is returned at distanceMeters 0 with " +
            "containment 'inside'; every other feature reports the true distance to the nearest point " +
            "on its geometry (nearest point on a segment, not just the nearest vertex) plus the " +
            "bearing toward it. Works across every vector spec (S-101, S-122, S-124, S-125, S-127, " +
            "S-128, S-129, S-131, S-201, S-411, S-421). Optional spec / datasetId / featureType / " +
            "maxDistanceMeters filters; results are nearest-first. Each match has 'depth' with its numeric depth values in metres where it " +
            "has any (depthRangeMinimumMeters, depthRangeMaximumMeters, valueOfSoundingMeters, waterLevelEffect); includeAttributes adds " +
            "every attribute as encoded. Read-only.";

        var del = ([Description("Query latitude in decimal degrees, WGS-84. Must be in [-90, 90].")] double latitude,
                   [Description("Query longitude in decimal degrees, WGS-84. Must be in [-180, 180].")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("Optional dataset identifier (typically from list_datasets); null searches across every matching dataset.")] string? datasetId = null,
                   [Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"LightAllAround\"; for S-101 the feature-type acronym, e.g. \"LIGHTS\"); null matches every feature type.")] string? featureType = null,
                   [Description("Optional maximum distance in metres; features farther than this are excluded. null imposes no limit.")] double? maxDistanceMeters = null,
                   [Description("Maximum ranked features to return; clamped to [1, 200]. Default 10.")] int limit = 10,
                   [Description("Add each feature's attributes as encoded (code and raw value; a complex attribute's parts as complex.sub) in 'attributes'. Default false. Numeric depths are in 'depth' either way.")] bool includeAttributes = false,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new NearestFeaturesRequest(
                        latitude,
                        longitude,
                        ParseSpec(spec),
                        string.IsNullOrWhiteSpace(datasetId) ? null : new DatasetId(datasetId),
                        featureType,
                        maxDistanceMeters,
                        limit,
                        includeAttributes),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = NearestFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateQueryFeaturesTool(QueryFeaturesTool inner)
    {
        var description =
            "Returns features from loaded vector datasets whose geometry intersects a geographic " +
            "query (point / bounding box / polygon / polyline). Works across every vector spec — " +
            "S-101 (ISO 8211 ENC) plus the GML specs S-122, S-124, S-125, S-127, S-128, S-129, " +
            "S-131, S-201, S-411, and S-421 — adapted through the shared feature interface. For a point, latitude and longitude can stand in for 'query'. By " +
            "default intersection is bounding-box precision; set precise=true for true " +
            "full-geometry intersection (point-in-polygon containment for areas, holes honoured, " +
            "and genuine segment crossing). An optional attributes predicate set filters on " +
            "attribute values (equality map or explicit operator predicates, combined with AND). " +
            "For S-101 the featureType filter matches the feature-type acronym (e.g. LIGHTS, " +
            "BOYLAT) and each featureId is the record's decimal RCID. Each result includes the " +
            "dataset ID, spec, feature ID, feature type, bounding box, and 'depth' with its numeric depth values in metres where it has " +
            "any (depthRangeMinimumMeters, depthRangeMaximumMeters, valueOfSoundingMeters, waterLevelEffect); includeAttributes adds every " +
            "attribute as encoded. Use describe_feature for decoded attributes and geometry. The result also carries a typeBreakdown " +
            "(per-feature-type counts of the full match set). Pagination is server-side. countOnly answers \"what kinds of features, and how many?\": " +
            "no feature rows, and 'counts' per dataset and feature type with how many have resolvable geometry; it takes the same filters, and " +
            "'query' may then be left out to count every feature, those without geometry included.";

        var del = ([Description(GeoQueryEnvelopeDescription + " For a point, latitude and longitude can be given instead. Required unless countOnly.")] JsonElement? query = null,
                   [Description("Optional spec filter (e.g. \"S-124/1.5.0\"); null matches every spec. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"NavwarnPart\", \"BuoyLateral\"); null returns every feature type.")] string? featureType = null,
                   [Description("Optional temporal filter JSON envelope. Shapes: {\"kind\":\"instant\",\"t\":\"2024-01-01T12:00:00Z\"}, {\"kind\":\"range\",\"from\":\"...\",\"to\":\"...\"}, {\"kind\":\"series\",\"from\":\"...\",\"to\":\"...\",\"stepSeconds\":N}. Excludes features whose fixedDateRange/periodicDateRange is disjoint from the window; features without validity metadata are always included.")] string? times = null,
                   [Description("Optional attribute-value predicates (logical AND). Either a code→value map for equality, e.g. {\"categoryOfLateralMark\":\"1\"}, or an array of explicit predicates, e.g. [{\"attribute\":\"valueOfDepth\",\"op\":\"ge\",\"value\":\"10\"},{\"attribute\":\"objectName\",\"op\":\"exists\"}]. Operators: exists, notExists, eq, ne, contains, startsWith, gt, ge, lt, le.")] string? attributes = null,
                   [Description("When true, replaces the default bounding-box test with true full-geometry intersection: point-in-polygon containment for areas (holes honoured) and genuine segment crossing (e.g. which features a route leg actually crosses). Default false.")] bool precise = false,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   [Description("Optional dataset identifier (typically from list_datasets); null queries across every matching dataset.")] string? datasetId = null,
                   [Description("Add each feature's attributes as encoded (code and raw value; a complex attribute's parts as complex.sub) in 'attributes'. Default false. Numeric depths are in 'depth' either way.")] bool includeAttributes = false,
                   [Description("A point to query, in decimal degrees WGS-84, instead of 'query' (the same as {\"kind\":\"point\",...}). Give with longitude.")] double? latitude = null,
                   [Description("The point's longitude, with latitude.")] double? longitude = null,
                   [Description("Count instead of listing: no feature rows, and 'counts' per dataset and feature type. Default false.")] bool countOnly = false,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new QueryFeaturesRequest(
                        countOnly ? OptionalQueryOrPoint(query, latitude, longitude) : QueryOrPoint(query, latitude, longitude),
                        ParseSpec(spec),
                        featureType,
                        ParseTimeQuery(times),
                        ParseAttributePredicates(attributes),
                        precise,
                        page,
                        pageSize,
                        string.IsNullOrWhiteSpace(datasetId) ? null : new DatasetId(datasetId),
                        includeAttributes,
                        countOnly),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = QueryFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    // query_features' envelope, or a point given as latitude/longitude (#1628).
    private static GeoQuery QueryOrPoint(JsonElement? query, double? latitude, double? longitude) =>
        OptionalQueryOrPoint(query, latitude, longitude)
        ?? throw new ArgumentException("give 'query', or latitude and longitude for a point.", "query");

    // The envelope, a point given as latitude/longitude, or neither (null).
    private static GeoQuery? OptionalQueryOrPoint(JsonElement? query, double? latitude, double? longitude)
    {
        var envelope = query is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } ? ParseGeoQuery(query) : null;
        if (latitude is null && longitude is null)
        {
            return envelope;
        }

        if (envelope is not null)
        {
            throw new ArgumentException("give 'query' or latitude and longitude, not both.", "query");
        }

        if (latitude is not { } lat || longitude is not { } lon)
        {
            throw new ArgumentException("give both latitude and longitude.", latitude is null ? "latitude" : "longitude");
        }

        return new GeoQuery.Point(new GeoPoint(lat, lon));
    }

    private static McpServerTool CreateSearchFeaturesTool(SearchFeaturesTool inner)
    {
        var description =
            "Finds vector features by name across loaded S-100 datasets — the \"where is the " +
            "feature called X?\" question that query_features (geometry-first) and describe_feature " +
            "(needs an id you don't have yet) cannot answer. Searches every place a name can live: " +
            "the simple OBJNAM / NOBJNM / objectName attributes (incl. ISO 8211-encoded S-101) and " +
            "the repeatable complex featureName compound's name / displayName sub-attributes (GML " +
            "specs S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421). Matching " +
            "is case-insensitive substring containment by default; set exact for whole-name " +
            "equality or caseSensitive for an exact-case match. Optionally scope by spec, a single " +
            "dataset, and/or a spatial envelope. Overlapping chart cells of different usage bands each chart " +
            "the same object, so set finestOnly to keep only the finest cell's copy. Results are paginated and read-only.";

        var del = ([Description("The text to search for in feature names (OBJNAM / NOBJNM / objectName / featureName). Required.")] string text,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every spec. Accepts a string or the {\"name\",\"edition\"} spec object the tools return.")] JsonElement? spec = null,
                   [Description("Optional dataset identifier (typically from list_datasets); null searches across every matching dataset.")] string? datasetId = null,
                   [Description("Optional spatial query envelope, supplied as a JSON object or a JSON string holding one (the shapes query_features takes). When supplied, only features whose bounding box intersects are searched; geometry-less features are excluded.")] JsonElement? query = null,
                   [Description("When true the match is case-sensitive; default false.")] bool caseSensitive = false,
                   [Description("When true a name must equal the search text exactly; when false (default) any name containing the text matches.")] bool exact = false,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   [Description("When true, a hit in a chart cell (S-101, S-401 or S-57) is kept only where its cell is in the finest scale band of the loaded chart cells at the feature's position (the route check's ranking), so an object charted in several overlapping cells is returned once. Default false.")] bool finestOnly = false,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new SearchFeaturesRequest(
                        text,
                        ParseSpec(spec),
                        string.IsNullOrWhiteSpace(datasetId) ? null : new DatasetId(datasetId),
                        ParseGeoQuery(query),
                        caseSensitive,
                        exact,
                        page,
                        pageSize,
                        finestOnly),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SearchFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateCheckRouteTool(CheckRouteTool inner)
    {
        var description =
            "Checks a route against the loaded data in one run and returns the whole report: the verdict (must_review, " +
            "water_level_ends_before_arrival, check or clear, with the finding that names it), whether each check ran (chart, areas, " +
            "warnings, clearance, weather, with a reason when partial or skipped), every finding with its numbers in 'details', and the under-keel clearance. " +
            "The chart check reports where the track crosses land (LandArea, or a Coastline where no land area is charted) or drying ground " +
            "(must_review), where either reaches inside the corridor between the cross-track limits (check), and where the track leaves the " +
            "DataCoverage of every loaded cell; with vessel.safetyContourMeters and vessel.safetyDepthMeters, also charted dangers and areas, soundings and " +
            "S-102 cells shallower than the safety contour or depth (without them the chart check is partial, " +
            "reason safety_settings_missing, and no depth or danger is checked). Where cells overlap only the finest cell counts. The area check reports " +
            "S-101 restricted, military practice, caution, anchorage and traffic areas and S-122 protected areas the corridor meets; an area " +
            "whose dates or season don't cover the ETAs is info route_check.area_not_in_force. The warning check reports S-124 warnings on or " +
            "near the route at the ETAs. With a draft on every leg (vessel.draftMeters, or the route's own) the clearance check computes UKC = charted depth (S-102, else S-101, low " +
            "confidence) + S-104 water level at each sample's ETA − draft − squat, against the required UKC (0 m when unset); datums must match " +
            "(a mismatch is a gap, never mixed). samples.include adds per-leg results and samples; window adds a departure sweep " +
            "(window.from/to, or the S-104 range). With weather.maxWindKnots or weather.maxWaveHeightMeters (or the route's own limits) the " +
            "weather check tests loaded GRIB2/NetCDF forecasts along the route, and loaded NHC advisories are tested as weather.cyclones " +
            "says. window.constraints adds the timing check (result: timing): tidal gates, leg current limits and arrival constraints at each departure, " +
            "with applyCurrents for running times with currents, and the windows that meet them all. Pass a polyline, or (in the viewer) a routeId or nothing for the active route, which " +
            "uses each leg's XTDs, draft, speed and UKC. The speed is over the ground unless vessel.speedThroughWater (or applyCurrents) says it's through the water: " +
            "then every check's ETAs add the current along the track (S-111, else current predictions; result: currents). Not for navigation.";

        // The schema and the binding come from CheckRouteRequest itself, so
        // every parameter the request takes reaches the tool.
        return McpServerTool.Create(new CheckRouteFunction(inner), new McpServerToolCreateOptions
        {
            Name = CheckRouteTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>
    /// The wire name of <see cref="CheckRouteRequest.Polyline"/>: JSON text
    /// (<c>{"vertices":[[lat,lon],...]}</c>) on the wire, as the other tools
    /// take polylines, rather than the record's object shape.
    /// </summary>
    private const string CheckRoutePolylineArgument = "polyline";

    /// <summary>
    /// The <c>check_route</c> input schema: <see cref="CheckRouteRequest"/>'s
    /// properties and descriptions, with <c>polyline</c> as JSON text.
    /// </summary>
    internal static JsonElement CheckRouteInputSchema { get; } = CreateCheckRouteInputSchema();

    private static JsonElement CreateCheckRouteInputSchema()
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(CheckRouteRequest),
            serializerOptions: JsonOptions,
            inferenceOptions: new AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false });
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = node["properties"]!.AsObject();
        properties[CheckRoutePolylineArgument] = new JsonObject
        {
            ["description"] =
                "Polyline JSON: {\"vertices\":[[lat,lon],...],\"corridorWidthMeters\":w?}; each pair of consecutive vertices is a leg. " +
                "WGS-84 decimal degrees. Takes precedence over routeId. corridorWidthMeters, when set, is the half-width either side of the track.",
            ["type"] = "string",
        };
        node.Remove("required");
        RemoveNullDefaults(node);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    // "default": null says nothing an optional property doesn't; dropping
    // it keeps the schema (and every client's context) smaller.
    private static void RemoveNullDefaults(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("default", out var value) && value is null)
            {
                obj.Remove("default");
            }

            foreach (var (_, child) in obj.ToList())
            {
                RemoveNullDefaults(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                RemoveNullDefaults(child);
            }
        }
    }

    /// <summary>
    /// Reads a <see cref="CheckRouteRequest"/> from <c>check_route</c>'s
    /// arguments, by the request's own JSON names.
    /// </summary>
    /// <param name="arguments">The call's arguments, by name.</param>
    /// <returns>The request.</returns>
    /// <exception cref="ArgumentException">The polyline isn't a polyline.</exception>
    /// <exception cref="JsonException">An argument has the wrong type.</exception>
    internal static CheckRouteRequest ReadCheckRouteRequest(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var body = new JsonObject();
        GeoPolyline? polyline = null;
        foreach (var (name, value) in arguments)
        {
            var node = value switch
            {
                null => null,
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                JsonNode n => n.DeepClone(),
                _ => JsonSerializer.SerializeToNode(value, JsonOptions.GetTypeInfo(value.GetType())),
            };
            // A group (vessel, window, ...) sent as JSON text is read as its object.
            if (node is JsonValue groupText && groupText.TryGetValue<string>(out var json) && json.TrimStart().StartsWith('{')
                && !string.Equals(name, CheckRoutePolylineArgument, StringComparison.OrdinalIgnoreCase))
            {
                node = JsonNode.Parse(json);
            }

            if (string.Equals(name, CheckRoutePolylineArgument, StringComparison.OrdinalIgnoreCase))
            {
                // JSON text, as the schema says; an object is taken as is.
                var text = node switch
                {
                    null => null,
                    JsonValue v when v.TryGetValue<string>(out var str) => str,
                    _ => node.ToJsonString(),
                };
                polyline = string.IsNullOrWhiteSpace(text) ? null : ParsePolyline(text);
                continue;
            }

            body[name] = node;
        }

        var request = body.Deserialize(JsonOptions.GetTypeInfo(typeof(CheckRouteRequest))) as CheckRouteRequest
            ?? throw new JsonException("check_route takes an object of arguments.");
        return request with { Polyline = polyline };
    }

    /// <summary>
    /// <c>check_route</c> as an <see cref="AIFunction"/> whose schema and
    /// binding are <see cref="CheckRouteRequest"/>'s.
    /// </summary>
    private sealed class CheckRouteFunction(CheckRouteTool inner) : AIFunction
    {
        public override string Name => CheckRouteTool.Name;

        public override JsonElement JsonSchema => CheckRouteInputSchema;

        public override JsonSerializerOptions JsonSerializerOptions => JsonOptions;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            await DispatchAsync(() => inner.InvokeAsync(ReadCheckRouteRequest(arguments), cancellationToken)).ConfigureAwait(false);
    }

    private static McpServerTool CreatePlanRouteTool(PlanRouteTool inner)
    {
        var description =
            "Suggests a route between stops for a vessel (from, to, optional via for a whole itinerary in one call) and checks it. " +
            "Stops that are on land or in water too shallow move to the nearest navigable water (stops[].moved, reason, movedMeters); near a stop " +
            "(limits.berthRadiusMeters, default 300 m) water down to draft + squat is allowed so the route can reach a berth. Between the stops the " +
            "route keeps to charted water at least the larger of draft + squat + requiredUkc and the safety contour deep, with no danger or structure " +
            "(breakwater, pier, pontoon) in it, at least clearanceMeters from anything that isn't, and prefers open water and, with prefer, charted " +
            "fairways, dredged areas or recommended tracks. It searches the same grid as get_navigability_grid, then keeps few waypoints. Returns the " +
            "legs (distanceNm, runningMinutes at vessel.speedKnots, least depth and clearance), 'waypoints' in the shape append_waypoint takes, " +
            "'polyline' to pass to check_route, and with check (default true) a short check_route summary: verdict, counts, mustReviewAwayFromStops " +
            "and the worst findings. In SoundCharts, routeName creates the route and returns its routeId. No tide gates or currents. " +
            "Errors: no_route_planned (stop_not_navigable or no_navigable_way). Not for navigation.";

        return McpServerTool.Create(new PlanRouteFunction(inner), new McpServerToolCreateOptions
        {
            Name = PlanRouteTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>The <c>plan_route</c> input schema: <see cref="PlanRouteRequest"/>'s properties and descriptions.</summary>
    internal static JsonElement PlanRouteInputSchema { get; } = CreatePlanRouteInputSchema();

    private static JsonElement CreatePlanRouteInputSchema()
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(PlanRouteRequest),
            serializerOptions: JsonOptions,
            inferenceOptions: new AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false });
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["required"] = new JsonArray("from", "to", "vessel");
        RemoveNullDefaults(node);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    /// <summary>Reads a <see cref="PlanRouteRequest"/> from <c>plan_route</c>'s arguments.</summary>
    /// <param name="arguments">The call's arguments, by name.</param>
    /// <returns>The request.</returns>
    /// <exception cref="JsonException">An argument has the wrong type.</exception>
    internal static PlanRouteRequest ReadPlanRouteRequest(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var body = new JsonObject();
        foreach (var (name, value) in arguments)
        {
            var node = value switch
            {
                null => null,
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                JsonNode n => n.DeepClone(),
                _ => JsonSerializer.SerializeToNode(value, JsonOptions.GetTypeInfo(value.GetType())),
            };

            // An object or list sent as JSON text is read as itself.
            if (node is JsonValue text && text.TryGetValue<string>(out var json) && json.TrimStart() is ['{' or '[', ..]
                && !string.Equals(name, "routeName", StringComparison.OrdinalIgnoreCase))
            {
                node = JsonNode.Parse(json);
            }

            body[name] = Point(name, node);
        }

        if (body["from"] is null || body["to"] is null)
        {
            throw new ArgumentException("plan_route needs 'from' and 'to'.", body["from"] is null ? "from" : "to");
        }

        return body.Deserialize(JsonOptions.GetTypeInfo(typeof(PlanRouteRequest))) as PlanRouteRequest
            ?? throw new JsonException("plan_route takes an object of arguments.");

        // A point may be [lat, lon] as well as {latitude, longitude}; so may each via point.
        static JsonNode? Point(string name, JsonNode? node)
        {
            static JsonNode? One(JsonNode? n) => n is JsonArray { Count: 2 } pair && pair[0] is JsonValue && pair[1] is JsonValue
                ? new JsonObject { ["latitude"] = pair[0]!.DeepClone(), ["longitude"] = pair[1]!.DeepClone() }
                : n;

            if (string.Equals(name, "from", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "to", StringComparison.OrdinalIgnoreCase))
            {
                return One(node);
            }

            return string.Equals(name, "via", StringComparison.OrdinalIgnoreCase) && node is JsonArray list
                ? new JsonArray([.. list.Select(item => One(item?.DeepClone()))])
                : node;
        }
    }

    /// <summary><c>plan_route</c> as an <see cref="AIFunction"/> whose schema and binding are <see cref="PlanRouteRequest"/>'s.</summary>
    private sealed class PlanRouteFunction(PlanRouteTool inner) : AIFunction
    {
        public override string Name => PlanRouteTool.Name;

        public override JsonElement JsonSchema => PlanRouteInputSchema;

        public override JsonSerializerOptions JsonSerializerOptions => JsonOptions;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            await DispatchAsync(() => inner.InvokeAsync(ReadPlanRouteRequest(arguments), cancellationToken)).ConfigureAwait(false);
    }

    private static McpServerTool CreateListSpecsTool(ListSpecsTool inner)
    {
        var description =
            "Returns the S-100 specs the server is built against and, for each spec, the number of " +
            "loaded datasets and the tools applicable to it (query_features / describe_feature / " +
            "sample). Use this to introspect what the agent can ask in the current session " +
            "before issuing spatial or temporal queries.";

        var del = (CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(new ListSpecsRequest(), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListSpecsTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateListTimeStepsTool(ListTimeStepsTool inner)
    {
        var description =
            "Returns the available UTC time-step instants for a time-varying dataset " +
            "(S-104 water level, S-111 surface currents, GRIB2 and NetCDF weather grids' valid times, " +
            "and for CAP warnings the times warnings start and end). Use this to ground temporal questions " +
            "before calling sample — the agent can discover the " +
            "first/last instant, the cadence (when uniform), and the full list of valid times. " +
            "For S-102 (static bathymetry) the times array is empty. Read-only and side-effect free.";

        var del = ([Description("Identifier of a currently loaded time-varying dataset (typically obtained from list_datasets).")] string datasetId,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(new ListTimeStepsRequest(new DatasetId(datasetId)), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListTimeStepsTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static SpecRef? ParseSpec(JsonElement? spec)
        => ParseSpec(SpecArgumentReader.ReadText(spec));

    private static SpecRef? ParseSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        // Accept the editionless bare name (e.g. "S-124") that the tool
        // descriptions promise — SpecRef.Parse requires a name/edition
        // pair, so fall back to a default (edition-agnostic) SpecRef when
        // no separator is present.
        if (!SpecRef.TryParse(spec, out var parsed))
        {
            if (SpecName.TryNormalize(spec, out var name))
            {
                return new SpecRef(name, default);
            }

            throw new ArgumentException(
                $"'{spec}' is not a recognised S-100 product specification name. " +
                $"Use a canonical name such as {string.Join(", ", Specification.AvailableSpecs)} " +
                "(the edition suffix is optional and ignored).",
                nameof(spec));
        }

        return parsed;
    }

    private static GeoQuery? ParseGeoQuery(string? queryJson)
        => string.IsNullOrWhiteSpace(queryJson) ? null : GeoQueryJsonReader.Parse(queryJson);

    /// <summary>
    /// Parses a spatial query supplied either as a structured JSON object
    /// (e.g. <c>{"kind":"box",...}</c>) — the ergonomic form an agent
    /// intuitively reaches for — or, for backward compatibility, as a JSON
    /// string containing that same envelope. Returns <see langword="null"/>
    /// when the argument is absent/null, and throws
    /// <see cref="ArgumentException"/> (mapped to a structured
    /// <c>invalid_argument</c> error) for any other shape.
    /// </summary>
    private static GeoQuery? ParseGeoQuery(JsonElement? query)
    {
        if (query is not { } element)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => ParseGeoQuery(element.GetString()),
            JsonValueKind.Object => GeoQueryJsonReader.Parse(element.GetRawText()),
            _ => throw new ArgumentException(
                "query must be a JSON envelope object with a \"kind\" of point|box|polygon|polyline "
                + "(e.g. {\"kind\":\"box\",\"south\":s,\"west\":w,\"north\":n,\"east\":e}), "
                + "or a JSON string containing one.",
                nameof(query)),
        };
    }

    private static TimeQuery? ParseTimeQuery(string? timesJson)
        => string.IsNullOrWhiteSpace(timesJson) ? null : TimeQueryJsonReader.Parse(timesJson);

    private static IReadOnlyList<AttributePredicate> ParseAttributePredicates(string? attributesJson)
        => string.IsNullOrWhiteSpace(attributesJson)
            ? []
            : AttributePredicateJsonReader.Parse(attributesJson);

    // A polyline given as a JSON object or as a string holding one; null when absent.
    private static GeoPolyline? ParsePolyline(JsonElement? polyline) => polyline switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        { ValueKind: JsonValueKind.String } text => string.IsNullOrWhiteSpace(text.GetString()) ? null : ParsePolyline(text.GetString()!),
        { ValueKind: JsonValueKind.Object } obj => ParsePolyline(obj.GetRawText()),
        _ => throw new ArgumentException("polyline must be a JSON object {\"vertices\":[[lat,lon],...]} or a string holding one.", nameof(polyline)),
    };

    private static GeoPolyline ParsePolyline(string polylineJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(polylineJson);
        // Accept either a bare polyline object {"vertices":[…]} or the
        // full GeoQuery polyline envelope so callers can copy/paste the
        // same shape used by query_features.
        using var doc = JsonDocument.Parse(polylineJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("polyline must be a JSON object.", nameof(polylineJson));
        }

        // If the caller sent a {"kind":"polyline",…} envelope, reuse the GeoQuery reader.
        if (root.TryGetProperty("kind", out var _))
        {
            var query = GeoQueryJsonReader.Parse(polylineJson);
            if (query is GeoQuery.Polyline pl)
            {
                return pl.Value;
            }
            throw new ArgumentException("polyline query envelope must have kind='polyline'.", nameof(polylineJson));
        }

        // Otherwise expect {"vertices":[[lat,lon],…], "corridorWidthMeters":w?}
        // — synthesise an envelope and reuse the same parser.
        var synthesized = new JsonObject
        {
            ["kind"] = "polyline",
            ["vertices"] = JsonNode.Parse(root.GetProperty("vertices").GetRawText()),
        };
        if (root.TryGetProperty("corridorWidthMeters", out var widthEl)
            && widthEl.ValueKind != JsonValueKind.Null)
        {
            synthesized["corridorWidthMeters"] = widthEl.GetDouble();
        }
        var parsed = GeoQueryJsonReader.Parse(synthesized.ToJsonString(JsonOptions));
        return ((GeoQuery.Polyline)parsed).Value;
    }

    /// <summary>
    /// Translates a <see cref="ToolResult{T}"/> Task into the
    /// <see cref="CallToolResult"/> shape the MCP SDK expects.
    /// Catches any unexpected exception and surfaces it as a generic
    /// <c>internal_error</c> error envelope so a tool never throws
    /// into the SDK dispatcher.
    /// </summary>
    private static async Task<CallToolResult> DispatchAsync<T>(Func<Task<ToolResult<T>>> resultFactory)
    {
        try
        {
            var result = await resultFactory().ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                return Success(value);
            }

            result.TryGetError(out var err);
            return Failure(err!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException)
        {
            // Malformed caller input (bad query/time/attribute/spec JSON or
            // an out-of-range value) surfaces as a structured invalid_argument
            // error naming the offending parameter where known, rather than an
            // opaque internal_error — see issue #312.
            return ArgumentFailure(ex);
        }
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    /// <summary>
    /// Maps a caller-input exception (<see cref="ArgumentException"/>,
    /// <see cref="FormatException"/>, or <see cref="JsonException"/>) to a
    /// structured <c>invalid_argument</c> error envelope, carrying the
    /// offending parameter name when the exception is an
    /// <see cref="ArgumentException"/> that supplies one.
    /// </summary>
    private static CallToolResult ArgumentFailure(Exception ex)
    {
        var argument = ex is ArgumentException { ParamName: { Length: > 0 } name } ? name : null;
        return Failure(new InvalidArgument(argument ?? string.Empty, ex.Message));
    }

    private static CallToolResult Success<T>(T value)
    {
        var json = McpJson.SerializeByRuntimeType(value, JsonOptions);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = json },
            ],
            IsError = false,
        };
    }

    private static CallToolResult Failure(ToolError error)
    {
        var payload = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["details"] = SerializeDetails(error),
        };
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = payload.ToJsonString(JsonOptions) },
            ],
            IsError = true,
        };
    }

    private static CallToolResult InternalError(Exception ex)
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = ex.Message,
            ["details"] = new JsonObject
            {
                ["exceptionType"] = ex.GetType().FullName,
            },
        };
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = payload.ToJsonString(JsonOptions) },
            ],
            IsError = true,
        };
    }

    /// <summary>
    /// Serialises the concrete <see cref="ToolError"/> via System.Text.Json
    /// and strips the base <c>Code</c>/<c>Message</c> properties so the
    /// remaining members surface as <c>details</c> without duplication.
    /// </summary>
    private static JsonObject SerializeDetails(ToolError error)
    {
        var node = JsonSerializer.SerializeToNode(error, JsonOptions.GetTypeInfo(error.GetType())) as JsonObject
            ?? new JsonObject();
        node.Remove("code");
        node.Remove("message");
        node.Remove("Code");
        node.Remove("Message");
        return node;
    }
}
