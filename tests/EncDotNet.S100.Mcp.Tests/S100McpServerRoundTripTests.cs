using EncDotNet.S100.DataModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerRoundTripTests
{
    [Fact]
    public async Task ListTools_returns_seven_tools_with_schemas()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "count_features",
                "describe_feature",
                "describe_feature_type",
                "find_at",
                "identify_features",
                "list_datasets",
                "list_specs",
                "list_time_steps",
                "nearest_features",
                "query_features",
                "sample_coverage",
                "sample_coverage_along",
                "search_features",
            },
            names);
        foreach (var tool in tools)
        {
            // ProtocolTool exposes the JSON schema; ensure it is non-empty.
            var schema = tool.ProtocolTool.InputSchema;
            Assert.True(schema.ValueKind == JsonValueKind.Object && schema.GetRawText().Length > 2,
                $"Tool {tool.Name} has no input schema.");
        }
    }

    [Fact]
    public async Task ListDatasets_round_trip_returns_summaries()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1"),
            LoadedDatasetFactory.S124("synth-warn-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("list_datasets", new Dictionary<string, object?>
        {
            ["page"] = 0,
            ["pageSize"] = 50,
        });

        Assert.False(result.IsError ?? false, $"list_datasets returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Equal(2, datasets.Count);
        var ids = datasets
            .Select(d => d!["id"]!.GetValue<string>())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "synth-bathy-1", "synth-warn-1" }, ids);
    }

    [Fact]
    public async Task DescribeFeature_round_trip_returns_feature_payload()
    {
        var feature = S124Synth.Feature(
            id: "feat-1",
            featureType: "NavwarnPart",
            attributes: new Dictionary<string, string>
            {
                ["warningInformation"] = "Test warning text.",
            });
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-1", model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature", new Dictionary<string, object?>
        {
            ["datasetId"] = "synth-warn-1",
            ["featureId"] = "feat-1",
        });

        Assert.False(result.IsError ?? false, $"describe_feature returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal("S-124", payload["spec"]!["name"]!.GetValue<string>());
        Assert.Equal("NavwarnPart", payload["featureTypeName"]!.GetValue<string>());
        Assert.NotNull(payload["attributes"]);
    }

    [Fact]
    public async Task SampleCoverage_round_trip_returns_depth()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1",
                bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
                source: S102Synth.Source(S102Synth.Dataset(depth: 17.5f, uncertainty: 0.5f))));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-102/2.1.0",
            ["latitude"] = 0.01,
            ["longitude"] = 0.01,
        });

        Assert.False(result.IsError ?? false, $"sample_coverage returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var depth = payload["value"]!["depthMeters"]!.GetValue<double>();
        Assert.InRange(depth, 17.49, 17.51);
    }

    [Fact]
    public async Task DescribeFeature_unknown_dataset_returns_structured_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature", new Dictionary<string, object?>
        {
            ["datasetId"] = "missing",
            ["featureId"] = "x",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for unknown dataset.");
        var payload = ParseSingleJson(result);
        Assert.Equal("dataset_not_found", payload["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(payload["message"]?.GetValue<string>()));
    }

    [Fact]
    public async Task FindAt_round_trip_returns_matching_datasets()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("warn-here", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)),
            LoadedDatasetFactory.S124("warn-elsewhere", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            ["latitude"] = 5.0,
            ["longitude"] = 5.0,
        });

        Assert.False(result.IsError ?? false, $"find_at returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Single(datasets);
        Assert.Equal("warn-here", datasets[0]!["id"]!.GetValue<string>());
        Assert.Equal(1, payload["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task FindAt_invalid_latitude_returns_structured_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            ["latitude"] = 95.0,
            ["longitude"] = 0.0,
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for out-of-range latitude.");
        var payload = ParseSingleJson(result);
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
        Assert.Equal("latitude", payload["details"]!["parameter"]!.GetValue<string>());
    }

    [Fact]
    public async Task CountFeatures_round_trip_returns_type_tallies()
    {
        var feature = new S124Feature
        {
            Id = "feat-1",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-1", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("count_features", new Dictionary<string, object?>());

        Assert.False(result.IsError ?? false, $"count_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal(1, payload["totalFeatures"]!.GetValue<int>());
        Assert.Equal(1, payload["datasetCount"]!.GetValue<int>());
        var types = payload["types"]!.AsArray();
        var tally = Assert.Single(types);
        Assert.Equal("NavwarnPart", tally!["featureType"]!.GetValue<string>());
        Assert.Equal(1, tally["count"]!.GetValue<int>());
        Assert.Equal(1, tally["withGeometry"]!.GetValue<int>());
    }

    [Fact]
    public async Task IdentifyFeatures_round_trip_returns_ranked_matches()
    {
        var area = new S124Feature
        {
            Id = "area-1",
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = [
                new GeoPosition(4.0, 4.0), new GeoPosition(4.0, 6.0), new GeoPosition(6.0, 6.0), new GeoPosition(6.0, 4.0), new GeoPosition(4.0, 4.0)],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var point = new S124Feature
        {
            Id = "light-1",
            FeatureType = "Light",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(area, point);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-2", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("identify_features", new Dictionary<string, object?>
        {
            ["latitude"] = 5.0,
            ["longitude"] = 5.0,
        });

        Assert.False(result.IsError ?? false, $"identify_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal(2, payload["totalMatched"]!.GetValue<int>());
        var features = payload["features"]!.AsArray();
        Assert.Equal(2, features.Count);
        // The point ranks above the area.
        Assert.Equal("light-1", features[0]!["featureId"]!.GetValue<string>());
        Assert.Equal("point", features[0]!["geometry"]!.GetValue<string>());
        Assert.Equal("surface", features[1]!["geometry"]!.GetValue<string>());
        Assert.Equal("inside", features[1]!["containment"]!.GetValue<string>());
    }

    [Fact]
    public async Task NearestFeatures_round_trip_ranks_by_true_distance()
    {
        var near = new S124Feature
        {
            Id = "near-1",
            FeatureType = "Light",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.1)],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var far = new S124Feature
        {
            Id = "far-1",
            FeatureType = "Light",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 6.0)],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(far, near);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-3", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("nearest_features", new Dictionary<string, object?>
        {
            ["latitude"] = 5.0,
            ["longitude"] = 5.0,
        });

        Assert.False(result.IsError ?? false, $"nearest_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal(2, payload["totalMatched"]!.GetValue<int>());
        var features = payload["features"]!.AsArray();
        Assert.Equal("near-1", features[0]!["featureId"]!.GetValue<string>());
        Assert.Equal("far-1", features[1]!["featureId"]!.GetValue<string>());
        Assert.True(
            features[0]!["distanceMeters"]!.GetValue<double>() < features[1]!["distanceMeters"]!.GetValue<double>());
        Assert.NotNull(features[0]!["bearingDegrees"]);
    }

    [Fact]
    public async Task NearestFeatures_invalid_latitude_returns_structured_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("nearest_features", new Dictionary<string, object?>
        {
            ["latitude"] = 120.0,
            ["longitude"] = 0.0,
        });

        Assert.True(result.IsError ?? false);
        Assert.Contains("latitude", DumpText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryFeatures_precise_drops_bounding_box_false_positive()
    {
        // A triangle whose bounding box covers (1.5, 1.5) but whose body
        // does not — the coarse query matches, the precise query does not.
        var triangle = new S124Feature
        {
            Id = "tri-1",
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = [
                new GeoPosition(0, 0), new GeoPosition(2, 0), new GeoPosition(0, 2), new GeoPosition(0, 0)],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(triangle);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-4", bounds: LoadedDatasetFactory.Box(-1, -1, 3, 3), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var pointQuery = "{\"kind\":\"point\",\"latitude\":1.5,\"longitude\":1.5}";

        var coarse = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = pointQuery,
        });
        Assert.False(coarse.IsError ?? false, $"query_features returned an error: {DumpText(coarse)}");
        Assert.Equal(1, ParseSingleJson(coarse)["totalCount"]!.GetValue<int>());

        var precise = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = pointQuery,
            ["precise"] = true,
        });
        Assert.False(precise.IsError ?? false, $"query_features returned an error: {DumpText(precise)}");
        Assert.Equal(0, ParseSingleJson(precise)["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task DescribeFeatureType_round_trip_introspects_bundled_catalogue()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature_type", new Dictionary<string, object?>
        {
            ["spec"] = "S-124",
        });

        Assert.False(result.IsError ?? false, $"describe_feature_type returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.True(payload["totalFeatureTypeCount"]!.GetValue<int>() > 0);
        Assert.NotEmpty(payload["featureTypes"]!.AsArray());
    }

    [Fact]
    public async Task DescribeFeatureType_round_trip_normalises_edition_suffix_and_casing()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature_type", new Dictionary<string, object?>
        {
            ["spec"] = "s124/1.5.0",
        });

        Assert.False(result.IsError ?? false, $"describe_feature_type returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.True(payload["totalFeatureTypeCount"]!.GetValue<int>() > 0);
    }

    [Fact]
    public async Task DescribeFeatureType_round_trip_missing_catalogue_lists_accepted_specs()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        // S-100 is a valid spec-name pattern but is the framework, not a
        // product spec, so it has no bundled Feature Catalogue.
        var result = await client.CallToolAsync("describe_feature_type", new Dictionary<string, object?>
        {
            ["spec"] = "S-100",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for a spec without a bundled Feature Catalogue.");
        var payload = ParseSingleJson(result);
        Assert.Equal("feature_catalogue_not_available", payload["code"]!.GetValue<string>());
        var accepted = payload["details"]!["acceptedSpecs"]!.AsArray();
        Assert.Contains(accepted, n => n!.GetValue<string>() == "S-124");
    }

    [Fact]
    public async Task DescribeFeatureType_round_trip_unrecognised_spec_suggests_canonical_names()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature_type", new Dictionary<string, object?>
        {
            ["spec"] = "bathymetry",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for an unrecognised spec name.");
        var payload = ParseSingleJson(result);
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
        Assert.Contains("S-101", payload["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_returns_matching_features()
    {
        var feature = new S124Feature
        {
            Id = "feat-1",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["warningInformation"] = "Test warning text." },
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-1", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.NotEmpty(features);
        Assert.Equal("feat-1", features[0]!["featureId"]!.GetValue<string>());
        Assert.Equal("NavwarnPart", features[0]!["featureType"]!.GetValue<string>());

        var breakdown = payload["typeBreakdown"]!.AsArray();
        var tally = Assert.Single(breakdown);
        Assert.Equal("NavwarnPart", tally!["featureType"]!.GetValue<string>());
        Assert.Equal(1, tally["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_with_attributes_filters_on_attribute_value()
    {
        var matching = new S124Feature
        {
            Id = "match",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["navwarnTypeGeneral"] = "1" },
            ComplexAttributes = [],
            References = [],
        };
        var other = new S124Feature
        {
            Id = "other",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(6.0, 6.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["navwarnTypeGeneral"] = "2" },
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(matching, other);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-attr", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
            ["attributes"] = """{"navwarnTypeGeneral":"1"}""",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.Single(features);
        Assert.Equal("match", features[0]!["featureId"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_with_attribute_predicate_array()
    {
        var deep = new S124Feature
        {
            Id = "deep",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["valueOfDepth"] = "20" },
            ComplexAttributes = [],
            References = [],
        };
        var shallow = new S124Feature
        {
            Id = "shallow",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(6.0, 6.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["valueOfDepth"] = "5" },
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(deep, shallow);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-depth", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
            ["attributes"] = """[{"attribute":"valueOfDepth","op":"ge","value":"10"}]""",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.Single(features);
        Assert.Equal("deep", features[0]!["featureId"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchFeatures_round_trip_finds_features_by_name()
    {
        var named = new S124Feature
        {
            Id = "warn-named",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["objectName"] = "Nab Tower Light" },
            ComplexAttributes = [],
            References = [],
        };
        var other = new S124Feature
        {
            Id = "warn-other",
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(6.0, 6.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = new Dictionary<string, string> { ["objectName"] = "Spit Sand Fort" },
            ComplexAttributes = [],
            References = [],
        };
        var dataset = S124Synth.Dataset(named, other);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-named", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("search_features", new Dictionary<string, object?>
        {
            ["text"] = "nab tower",
        });

        Assert.False(result.IsError ?? false, $"search_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.Single(features);
        Assert.Equal("warn-named", features[0]!["featureId"]!.GetValue<string>());
        Assert.Equal("Nab Tower Light", features[0]!["matchedName"]!.GetValue<string>());
        Assert.Equal("objectName", features[0]!["matchedAttribute"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchFeatures_blank_text_returns_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("search_features", new Dictionary<string, object?>
        {
            ["text"] = "   ",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for blank search text.");
        var payload = ParseSingleJson(result);
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_invalid_query_json_returns_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"unknown"}""",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for unknown query kind.");
        var payload = ParseSingleJson(result);
        // Malformed query envelopes now map to a structured invalid_argument
        // error (naming the offending parameter) rather than an opaque
        // internal_error — see issue #312.
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_with_times_filters_out_disjoint_validity()
    {
        var inWindow = MakeS122WithFixedRange("in", "2024-01-01", "2024-12-31");
        var outOfWindow = MakeS122WithFixedRange("out", "2030-01-01", "2030-12-31");
        var s122 = new S122Dataset
        {
            Features = [inWindow, outOfWindow],
            InformationTypes = [],
        };
        var loaded = new LoadedDataset(
            new DatasetId("mpa-1"),
            new SpecRef("S-122", new SpecVersion(1, 0, 0)),
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new S122DatasetData(s122));
        var catalog = McpTestHelpers.NewCatalog(loaded);

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
            ["times"] = """{"kind":"instant","t":"2024-06-15T12:00:00Z"}""",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.Single(features);
        Assert.Equal("in", features[0]!["featureId"]!.GetValue<string>());
    }

    private static S122Feature MakeS122WithFixedRange(string id, string start, string end)
    {
        var sub = new Dictionary<string, string> { ["dateStart"] = start, ["dateEnd"] = end };
        return new S122Feature
        {
            Id = id,
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(5.0, 5.0)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [new S122ComplexAttribute
            {
                Code = "fixedDateRange",
                SubAttributes = sub,
            }],
        };
    }


    [Fact]
    public async Task SampleCoverageAlong_round_trip_returns_per_vertex_results()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1",
                bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
                source: S102Synth.Source(S102Synth.Dataset(depth: 17.5f, uncertainty: 0.5f))));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "S-102/2.1.0",
            ["polyline"] = """{"vertices":[[0.01,0.01],[0.02,0.02],[50.0,50.0]]}""",
        });

        Assert.False(result.IsError ?? false, $"sample_coverage_along returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var samples = payload["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);
        // First two vertices are inside the bathy bounds and should resolve.
        Assert.NotNull(samples[0]!["result"]);
        Assert.NotNull(samples[1]!["result"]);
        // Last vertex is far outside any coverage — per-vertex miss -> null.
        Assert.Null(samples[2]!["result"]);
    }

    [Fact]
    public async Task ListSpecs_round_trip_returns_capabilities()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1"),
            LoadedDatasetFactory.S124("synth-warn-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("list_specs", new Dictionary<string, object?>());

        Assert.False(result.IsError ?? false, $"list_specs returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var specs = payload["specs"]!.AsArray();
        Assert.NotEmpty(specs);
        // Every entry exposes its capability flags.
        foreach (var entry in specs)
        {
            Assert.NotNull(entry!["name"]);
            var caps = entry!["capabilities"]!.AsObject();
            Assert.NotNull(caps["canQueryFeatures"]);
            Assert.NotNull(caps["canDescribeFeature"]);
            Assert.NotNull(caps["canSampleCoverage"]);
            Assert.NotNull(caps["canListTimeSteps"]);
        }
    }

    [Fact]
    public async Task ListTimeSteps_round_trip_returns_cadence_for_S104()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S104("wl-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync(
            "list_time_steps",
            new Dictionary<string, object?> { ["datasetId"] = "wl-1" });

        Assert.False(result.IsError ?? false, $"list_time_steps returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var times = payload["times"]!.AsArray();
        Assert.NotEmpty(times);
        Assert.NotNull(payload["firstTime"]);
        Assert.NotNull(payload["lastTime"]);
        Assert.NotNull(payload["cadence"]);
        Assert.Equal("S-104", payload["spec"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SampleCoverage_round_trip_with_times_range_returns_series()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S104("wl-series"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-104/2.0.0",
            ["latitude"] = 0.01,
            ["longitude"] = 0.01,
            ["times"] = """{"kind":"range","from":"2024-01-01T00:00:00Z","to":"2024-01-01T02:00:00Z"}""",
        });

        Assert.False(result.IsError ?? false, $"sample_coverage returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var series = payload["series"]!.AsArray();
        Assert.Equal(3, series.Count);
        foreach (var step in series)
        {
            Assert.NotNull(step!["sampleTime"]);
            Assert.NotNull(step["requestedTime"]);
            Assert.NotNull(step["value"]);
        }
    }


    [Fact]
    public async Task FindAt_with_box_query_envelope_returns_intersecting_datasets()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("warn-here", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)),
            LoadedDatasetFactory.S124("warn-elsewhere", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            // latitude/longitude are ignored when 'query' is supplied.
            ["latitude"] = 0.0,
            ["longitude"] = 0.0,
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
        });

        Assert.False(result.IsError ?? false, $"find_at returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Single(datasets);
        Assert.Equal("warn-here", datasets[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_accepts_structured_object_query()
    {
        var feature = MakeNavwarn("feat-struct", 5.0, 5.0);
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-struct", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        // Pass the query envelope as a structured JSON object (the ergonomic
        // form an agent intuitively reaches for) rather than a stringified
        // JSON envelope — see issue #312.
        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = new JsonObject
            {
                ["kind"] = "box",
                ["south"] = -5,
                ["west"] = -5,
                ["north"] = 15,
                ["east"] = 15,
            },
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.Single(features);
        Assert.Equal("feat-struct", features[0]!["featureId"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_scopes_to_datasetId()
    {
        var a = S124Synth.Dataset(MakeNavwarn("in-a", 5.0, 5.0));
        var b = S124Synth.Dataset(MakeNavwarn("in-b", 6.0, 6.0));
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("ds-a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: a),
            LoadedDatasetFactory.S124("ds-b", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: b));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
            ["datasetId"] = "ds-b",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        var feature = Assert.Single(features);
        Assert.Equal("in-b", feature!["featureId"]!.GetValue<string>());
        Assert.Equal("ds-b", feature["datasetId"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_malformed_query_returns_invalid_argument()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("ds", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10),
                model: S124Synth.Dataset(MakeNavwarn("f", 5.0, 5.0))));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        // A query object missing the required "kind" discriminator should
        // surface a structured invalid_argument error, not an opaque
        // internal_error — see issue #312.
        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = new JsonObject
            {
                ["boundingBox"] = new JsonObject
                {
                    ["south"] = -5,
                    ["west"] = -5,
                    ["north"] = 15,
                    ["east"] = 15,
                },
            },
        });

        Assert.True(result.IsError ?? false, "expected an error for a malformed query.");
        var payload = ParseSingleJson(result);
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
    }

    private static S124Feature MakeNavwarn(string id, double lat, double lon) =>
        new()
        {
            Id = id,
            FeatureType = "NavwarnPart",
            GeometryType = S100GeometryType.Point,
            Points = [new GeoPosition(lat, lon)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
            References = [],
        };

    private static JsonObject ParseSingleJson(CallToolResult result)
    {
        var text = DumpText(result);
        var node = JsonNode.Parse(text)
            ?? throw new InvalidOperationException("Tool result text was not valid JSON.");
        return node.AsObject();
    }

    private static string DumpText(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock t) return t.Text;
        }
        return "";
    }
}
