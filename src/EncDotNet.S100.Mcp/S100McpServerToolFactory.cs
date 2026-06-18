using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Time;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Builds <see cref="McpServerTool"/> wrappers around the MCP-1 tools
/// (<see cref="ListDatasetsTool"/>, <see cref="DescribeFeatureTool"/>,
/// <see cref="SampleCoverageTool"/>, <see cref="FindAtTool"/>),
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Type == typeof(SampledValue))
                    {
                        typeInfo.PolymorphismOptions = new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions
                        {
                            TypeDiscriminatorPropertyName = "$kind",
                            IgnoreUnrecognizedTypeDiscriminators = true,
                            UnknownDerivedTypeHandling = System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling.FallBackToBaseType,
                            DerivedTypes =
                            {
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(DepthSample), "depth"),
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(WaterLevelSample), "water_level"),
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(WaterLevelStationSample), "water_level_station"),
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(SurfaceCurrentSample), "surface_current"),
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(SurfaceCurrentStationSample), "surface_current_station"),
                            },
                        };
                    }
                },
            },
        },
    };

    public static IEnumerable<McpServerTool> CreateTools(
        ListDatasetsTool listDatasets,
        DescribeFeatureTool describeFeature,
        DescribeFeatureTypeTool describeFeatureType,
        SampleCoverageTool sampleCoverage,
        FindAtTool findAt,
        IdentifyFeaturesTool identifyFeatures,
        NearestFeaturesTool nearestFeatures,
        QueryFeaturesTool queryFeatures,
        CountFeaturesTool countFeatures,
        SearchFeaturesTool searchFeatures,
        SampleCoverageAlongTool sampleCoverageAlong,
        ListSpecsTool listSpecs,
        ListTimeStepsTool listTimeSteps)
    {
        yield return CreateListDatasetsTool(listDatasets);
        yield return CreateDescribeFeatureTool(describeFeature);
        yield return CreateDescribeFeatureTypeTool(describeFeatureType);
        yield return CreateSampleCoverageTool(sampleCoverage);
        yield return CreateFindAtTool(findAt);
        yield return CreateIdentifyFeaturesTool(identifyFeatures);
        yield return CreateNearestFeaturesTool(nearestFeatures);
        yield return CreateQueryFeaturesTool(queryFeatures);
        yield return CreateCountFeaturesTool(countFeatures);
        yield return CreateSearchFeaturesTool(searchFeatures);
        yield return CreateSampleCoverageAlongTool(sampleCoverageAlong);
        yield return CreateListSpecsTool(listSpecs);
        yield return CreateListTimeStepsTool(listTimeSteps);
    }

    private static McpServerTool CreateListDatasetsTool(ListDatasetsTool inner)
    {
        var description =
            "Lists the S-100 datasets currently loaded in the host (viewer or CLI). " +
            "Supports optional spec and bounding-box filters and pagination. " +
            "Returns dataset IDs, spec, bounds (decimal degrees, WGS-84), and UTC time range. " +
            "Read-only and side-effect free.";

        var del = ([Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] string? spec = null,
                   [Description("Optional bounding-box south latitude (decimal degrees, WGS-84). Pass null to omit the bbox filter; if any one of south/west/north/east is supplied, all four must be.")] double? south = null,
                   [Description("Optional bounding-box west longitude (decimal degrees, WGS-84).")] double? west = null,
                   [Description("Optional bounding-box north latitude (decimal degrees, WGS-84).")] double? north = null,
                   [Description("Optional bounding-box east longitude (decimal degrees, WGS-84).")] double? east = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new ListDatasetsRequest(
                        ParseSpec(spec),
                        ParseBox(south, west, north, east),
                        page,
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = "list_datasets",
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateDescribeFeatureTool(DescribeFeatureTool inner)
    {
        var description =
            "Returns spec, feature-type code, attributes (as a JSON object), and xlink-resolved cross-references " +
            "for a single feature in a loaded vector dataset. Supports S-122, S-124, S-125, S-127, S-128, S-129, " +
            "S-131, S-201, S-411, and S-421. References for backfilled GML specs are currently returned empty " +
            "(spec-specific reference resolution is staged). Read-only and side-effect free.";

        var del = ([Description("Stable dataset identifier returned by list_datasets.")] string datasetId,
                   [Description("GML id of the feature within the dataset.")] string featureId,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new DescribeFeatureRequest(new DatasetId(datasetId), featureId),
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
            "schema-discovery counterpart to count_features / query_features. Call with just a spec " +
            "(e.g. \"S-101\" or \"S-124/1.5.0\") to list every feature type with its attribute count; " +
            "add a featureType (code, name, or alias) to get that type's attribute bindings — each " +
            "attribute's value type, whether it is mandatory and/or repeatable, and its enumerated " +
            "listed values. Use it to build valid attribute predicates without a loaded dataset. " +
            "Specs without a bundled Feature Catalogue return feature_catalogue_not_available. " +
            "Read-only and side-effect free.";

        var del = ([Description("Product specification whose bundled Feature Catalogue to inspect (e.g. \"S-101\" or \"S-124/1.5.0\"). Edition is ignored.")] string spec,
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

    private static McpServerTool CreateSampleCoverageTool(SampleCoverageTool inner)
    {
        var description =
            "Samples a coverage product at a single latitude/longitude (decimal degrees, WGS-84). " +
            "Returns the value of the nearest grid cell — no interpolation, no bbox aggregation. " +
            "Supports S-102 (depth and optional uncertainty in metres, positive down), " +
            "S-104 (water-level height in metres and decoded trend at the nearest time step), and " +
            "S-111 (current speed in m/s and knots, direction in degrees from true north 0..360, at the nearest time step). " +
            "Times outside a dataset's range clamp to its first or last step. Read-only and side-effect free.";

        var del = ([Description("Spec of the coverage to sample (S-102, S-104, or S-111; e.g. \"S-102/2.1.0\").")] string spec,
                   [Description("Sample latitude in decimal degrees, WGS-84, range -90..+90.")] double latitude,
                   [Description("Sample longitude in decimal degrees, WGS-84, range -180..+180.")] double longitude,
                   [Description("Optional UTC ISO-8601 time selector for time-varying products (S-104, S-111); ignored for S-102. Nearest time step is selected; times outside the dataset range clamp to the first or last step.")] DateTimeOffset? time = null,
                   [Description("Optional temporal query JSON envelope. Shapes: {\"kind\":\"instant\",\"t\":\"2024-01-01T14:00:00Z\"}, {\"kind\":\"range\",\"from\":\"…\",\"to\":\"…\"}, {\"kind\":\"series\",\"from\":\"…\",\"to\":\"…\",\"stepSeconds\":1800}. Range/Series populate the result's 'series' field. Takes precedence over 'time'.")] string? times = null,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new SampleCoverageRequest(
                        ParseSpec(spec) ?? throw new ArgumentException("spec is required.", nameof(spec)),
                        latitude,
                        longitude,
                        time,
                        ParseTimeQuery(times)),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SampleCoverageTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateFindAtTool(FindAtTool inner)
    {
        var description =
            "Returns every dataset currently loaded in the host (viewer or CLI) whose declared " +
            "bounding box contains or intersects the supplied geographic query. The simplest call " +
            "is point-based (latitude/longitude in WGS-84 decimal degrees); for richer spatial " +
            "selection (bbox / polygon / polyline), pass the optional 'query' JSON envelope and the " +
            "tool will use it in place of the lat/lon point. Containment is bbox-only — a positive " +
            "result means the point lies inside the dataset's declared rectangle, not that the " +
            "point has actual cell coverage (call sample_coverage to read a value). Optionally " +
            "filtered by spec. Returns dataset IDs, spec, bounds, and time range, with pagination.";

        var del = ([Description("Query latitude in decimal degrees, WGS-84. Must be in [-90, 90]. Ignored when 'query' is supplied.")] double latitude,
                   [Description("Query longitude in decimal degrees, WGS-84. Must be in [-180, 180]. Ignored when 'query' is supplied.")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] string? spec = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   [Description("Optional spatial query JSON envelope. Shapes: {\"kind\":\"point\",\"latitude\":lat,\"longitude\":lon}, {\"kind\":\"box\",\"south\":s,\"west\":w,\"north\":n,\"east\":e}, {\"kind\":\"polygon\",\"ring\":[[lat,lon],...]}, {\"kind\":\"polyline\",\"vertices\":[[lat,lon],...],\"corridorWidthMeters\":w}. When supplied, overrides latitude/longitude.")] string? query = null,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new FindAtRequest(
                        latitude,
                        longitude,
                        ParseSpec(spec),
                        page,
                        pageSize,
                        ParseGeoQuery(query)),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = "find_at",
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateIdentifyFeaturesTool(IdentifyFeaturesTool inner)
    {
        var description =
            "Identifies the vector features at a geographic point — the ECDIS cursor-pick — ranked " +
            "most-specific first (point features before curves before areas; within a primitive the " +
            "smaller/nearer feature wins). The feature-aware complement to find_at (which only " +
            "answers which datasets' bounds cover the point). Area features use exact point-in-" +
            "polygon containment (interior-ring holes honoured); point and curve features match " +
            "within radiusMeters. Works across every vector spec (S-101, S-122, S-124, S-125, " +
            "S-127, S-128, S-129, S-131, S-201, S-411, S-421). Each match reports the dataset ID, " +
            "spec, feature ID and type, geometry primitive, bounds, containment ('inside'/'near'), " +
            "and approximate distance — follow up with describe_feature for full attributes.";

        var del = ([Description("Pick latitude in decimal degrees, WGS-84. Must be in [-90, 90].")] double latitude,
                   [Description("Pick longitude in decimal degrees, WGS-84. Must be in [-180, 180].")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec.")] string? spec = null,
                   [Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double radiusMeters = 50.0,
                   [Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int maxResults = 20,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new IdentifyFeaturesRequest(
                        latitude,
                        longitude,
                        ParseSpec(spec),
                        radiusMeters,
                        maxResults),
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
            "the distance-ranking and containment query that find_at (dataset-bbox membership) and " +
            "query_features (feature-bbox intersection) can't answer. Answers \"nearest light/buoy/" +
            "berth to my position?\" and \"is this point inside any restricted area?\" in one call: " +
            "an area feature containing the point is returned at distanceMeters 0 with " +
            "containment 'inside'; every other feature reports the true distance to the nearest point " +
            "on its geometry (nearest point on a segment, not just the nearest vertex) plus the " +
            "bearing toward it. Works across every vector spec (S-101, S-122, S-124, S-125, S-127, " +
            "S-128, S-129, S-131, S-201, S-411, S-421). Optional spec / datasetId / featureType / " +
            "maxDistanceMeters filters; results are nearest-first. Read-only.";

        var del = ([Description("Query latitude in decimal degrees, WGS-84. Must be in [-90, 90].")] double latitude,
                   [Description("Query longitude in decimal degrees, WGS-84. Must be in [-180, 180].")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec.")] string? spec = null,
                   [Description("Optional dataset identifier (typically from list_datasets); null searches across every matching dataset.")] string? datasetId = null,
                   [Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"LightAllAround\"; for S-101 the feature-type acronym, e.g. \"LIGHTS\"); null matches every feature type.")] string? featureType = null,
                   [Description("Optional maximum distance in metres; features farther than this are excluded. null imposes no limit.")] double? maxDistanceMeters = null,
                   [Description("Maximum ranked features to return; clamped to [1, 200]. Default 10.")] int limit = 10,
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
                        limit),
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
            "Returns features from loaded GML-encoded vector datasets whose geometry intersects a " +
            "geographic query (point / bounding box / polygon / polyline). Supports S-122, S-124, " +
            "S-125, S-127, S-128, S-129, S-131, S-201, S-411, and S-421. Each result includes the " +
            "dataset ID, spec, feature ID, feature type, and bounding box — follow up with " +
            "describe_feature for full attributes. Pagination is server-side.";

        var del = ([Description("Spatial query JSON envelope. Shapes: {\"kind\":\"point\",\"latitude\":lat,\"longitude\":lon}, {\"kind\":\"box\",\"south\":s,\"west\":w,\"north\":n,\"east\":e}, {\"kind\":\"polygon\",\"ring\":[[lat,lon],...]}, {\"kind\":\"polyline\",\"vertices\":[[lat,lon],...],\"corridorWidthMeters\":w}.")] string query,
                   [Description("Optional spec filter (e.g. \"S-124/1.5.0\"); null matches every spec.")] string? spec = null,
                   [Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"NavwarnPart\", \"BuoyLateral\"); null returns every feature type.")] string? featureType = null,
                   [Description("Optional temporal filter JSON envelope. Shapes: {\"kind\":\"instant\",\"t\":\"2024-01-01T12:00:00Z\"}, {\"kind\":\"range\",\"from\":\"...\",\"to\":\"...\"}, {\"kind\":\"series\",\"from\":\"...\",\"to\":\"...\",\"stepSeconds\":N}. Excludes features whose fixedDateRange/periodicDateRange is disjoint from the window; features without validity metadata are always included.")] string? times = null,
                   [Description("Optional attribute-value predicates (logical AND). Either a code→value map for equality, e.g. {\"categoryOfLateralMark\":\"1\"}, or an array of explicit predicates, e.g. [{\"attribute\":\"valueOfDepth\",\"op\":\"ge\",\"value\":\"10\"},{\"attribute\":\"objectName\",\"op\":\"exists\"}]. Operators: exists, notExists, eq, ne, contains, startsWith, gt, ge, lt, le.")] string? attributes = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new QueryFeaturesRequest(
                        ParseGeoQuery(query) ?? throw new ArgumentException("query is required.", nameof(query)),
                        ParseSpec(spec),
                        featureType,
                        ParseTimeQuery(times),
                        ParseAttributePredicates(attributes),
                        page,
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = QueryFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateCountFeaturesTool(CountFeaturesTool inner)
    {
        var description =
            "Enumerates the feature types present in loaded vector datasets and counts how many " +
            "features of each type they contain — the \"what kinds of features, and how many, are " +
            "in this cell?\" discovery question. Works across every vector spec (S-101, S-122, " +
            "S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421). Optionally filter by " +
            "spec, by a single dataset, and/or by a spatial envelope. Each tally reports the total " +
            "count and how many of those features have resolvable geometry. Read-only and " +
            "side-effect free.";

        var del = ([Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every spec.")] string? spec = null,
                   [Description("Optional dataset identifier (typically from list_datasets); null counts across every matching dataset.")] string? datasetId = null,
                   [Description("Optional spatial query JSON envelope (same shapes as query_features: point / box / polygon / polyline). When supplied, only features whose bounding box intersects are counted; geometry-less features are excluded.")] string? query = null,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new CountFeaturesRequest(
                        ParseSpec(spec),
                        string.IsNullOrWhiteSpace(datasetId) ? null : new DatasetId(datasetId),
                        ParseGeoQuery(query)),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CountFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
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
            "dataset, and/or a spatial envelope. Results are paginated and read-only.";

        var del = ([Description("The text to search for in feature names (OBJNAM / NOBJNM / objectName / featureName). Required.")] string text,
                   [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every spec.")] string? spec = null,
                   [Description("Optional dataset identifier (typically from list_datasets); null searches across every matching dataset.")] string? datasetId = null,
                   [Description("Optional spatial query JSON envelope (same shapes as query_features: point / box / polygon / polyline). When supplied, only features whose bounding box intersects are searched; geometry-less features are excluded.")] string? query = null,
                   [Description("When true the match is case-sensitive; default false.")] bool caseSensitive = false,
                   [Description("When true a name must equal the search text exactly; when false (default) any name containing the text matches.")] bool exact = false,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
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
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SearchFeaturesTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateSampleCoverageAlongTool(SampleCoverageAlongTool inner)
    {
        var description =
            "Samples a coverage product (S-102 / S-104 / S-111) at every vertex of a polyline, " +
            "returning per-vertex results in input order. Vertices that fall outside coverage or " +
            "have no data return null entries so the agent can still use the rest of the route. " +
            "For time-varying products (S-104, S-111), the optional time applies identically to " +
            "every vertex — useful for \"depth/level/current at each waypoint at the same instant\". " +
            "The polyline's corridor width is ignored (corridors apply to membership queries, not " +
            "point sampling).";

        var del = ([Description("Spec of the coverage to sample (\"S-102/2.1.0\", \"S-104/1.1.0\", or \"S-111/1.1.1\").")] string spec,
                   [Description("Polyline JSON: {\"vertices\":[[lat,lon],...]} — corridor width is not used here. Coordinates are WGS-84 decimal degrees.")] string polyline,
                   [Description("Optional time selector (ISO-8601, time-varying products only).")] DateTimeOffset? time = null,
                   [Description("Optional temporal query JSON envelope applied to every vertex; same shape as sample_coverage. Takes precedence over 'time'.")] string? times = null,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new SampleCoverageAlongRequest(
                        ParseSpec(spec) ?? throw new ArgumentException("spec is required.", nameof(spec)),
                        ParsePolyline(polyline),
                        time,
                        ParseTimeQuery(times)),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SampleCoverageAlongTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateListSpecsTool(ListSpecsTool inner)
    {
        var description =
            "Returns the S-100 specs the server is built against and, for each spec, the number of " +
            "loaded datasets and the tools applicable to it (query_features / describe_feature / " +
            "sample_coverage). Use this to introspect what the agent can ask in the current session " +
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
            "Returns the available UTC time-step instants for a time-varying coverage dataset " +
            "(S-104 water level, S-111 surface currents). Use this to ground temporal questions " +
            "before issuing sample_coverage or sample_coverage_along — the agent can discover the " +
            "first/last instant, the cadence (when uniform), and the full list of valid times. " +
            "For S-102 (static bathymetry) the times array is empty. Read-only and side-effect free.";

        var del = ([Description("Identifier of a currently loaded time-varying coverage dataset (typically obtained from list_datasets).")] string datasetId,
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

            throw new FormatException($"'{spec}' is not a valid spec.");
        }

        return parsed;
    }

    private static GeoQuery? ParseGeoQuery(string? queryJson)
        => string.IsNullOrWhiteSpace(queryJson) ? null : GeoQueryJsonReader.Parse(queryJson);

    private static TimeQuery? ParseTimeQuery(string? timesJson)
        => string.IsNullOrWhiteSpace(timesJson) ? null : TimeQueryJsonReader.Parse(timesJson);

    private static ImmutableArray<AttributePredicate> ParseAttributePredicates(string? attributesJson)
        => string.IsNullOrWhiteSpace(attributesJson)
            ? ImmutableArray<AttributePredicate>.Empty
            : AttributePredicateJsonReader.Parse(attributesJson);

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

    private static BoundingBox? ParseBox(double? south, double? west, double? north, double? east)
    {
        if (south is null && west is null && north is null && east is null) return null;
        if (south is null || west is null || north is null || east is null)
        {
            throw new ArgumentException(
                "When supplying a bbox filter, all of south, west, north and east must be provided.");
        }
        return new BoundingBox(south.Value, west.Value, north.Value, east.Value);
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
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    private static CallToolResult Success<T>(T value)
    {
        var runtimeType = value?.GetType() ?? typeof(T);
        var json = JsonSerializer.Serialize(value, runtimeType, JsonOptions);
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
        var node = JsonSerializer.SerializeToNode(error, error.GetType(), JsonOptions) as JsonObject
            ?? new JsonObject();
        node.Remove("code");
        node.Remove("message");
        node.Remove("Code");
        node.Remove("Message");
        return node;
    }
}
