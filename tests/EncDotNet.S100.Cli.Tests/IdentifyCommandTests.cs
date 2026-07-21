using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end smoke tests that run the <c>s100 identify</c> command in-process
/// against committed synthetic dataset fixtures. The command performs a
/// headless ECDIS-style "pick" — identifying vector features and sampling
/// coverage values at a lat/lon — so these exercise the shared catalog
/// projection and pick services without an open viewer or MCP server.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class IdentifyCommandTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public void Identify_single_gml_dataset_returns_success()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["identify", dataset, "--lat", "51.085", "--lon", "1.30"]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Identify_emits_well_formed_json_with_ranked_features()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (exit, stdout) = RunCapturingStdout(
            ["identify", dataset, "--lat", "51.085", "--lon", "1.30", "--format", "json"]);

        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        Assert.Equal(51.085, root.GetProperty("point").GetProperty("latitude").GetDouble(), 3);
        Assert.Equal(1.30, root.GetProperty("point").GetProperty("longitude").GetDouble(), 3);

        var features = root.GetProperty("features");
        Assert.Equal(JsonValueKind.Array, features.ValueKind);
        Assert.True(features.GetArrayLength() >= 2);

        // ECDIS draw order: the point feature ranks ahead of the surface feature.
        Assert.Equal("point", features[0].GetProperty("geometry").GetString());
        Assert.Equal("S-124", features[0].GetProperty("spec").GetString());

        Assert.Equal(JsonValueKind.Array, root.GetProperty("samples").ValueKind);
    }

    [Fact]
    public void Identify_multiple_layers_merges_results()
    {
        var s124 = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        var s125 = FixturePath(Path.Combine("S125", "aton_point.gml"));
        Skip.IfNot(File.Exists(s124), $"Fixture not found: {s124}");
        Skip.IfNot(File.Exists(s125), $"Fixture not found: {s125}");

        // Pick inside the S-124 polygon; the S-125 layer simply contributes no
        // features here, proving multi-layer resolution succeeds.
        int exit = CliApp.Build().Run(
            ["identify", "--layer", s124, "--layer", s125, "--lat", "51.085", "--lon", "1.30"]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Identify_spec_filter_restricts_features()
    {
        var s124 = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        var s125 = FixturePath(Path.Combine("S125", "aton_point.gml"));
        Skip.IfNot(File.Exists(s124), $"Fixture not found: {s124}");
        Skip.IfNot(File.Exists(s125), $"Fixture not found: {s125}");

        var (exit, stdout) = RunCapturingStdout(
            ["identify", "--layer", s124, "--layer", s125,
             "--lat", "51.085", "--lon", "1.30", "--spec", "S-125", "--format", "json"]);

        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(stdout);
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            Assert.Equal("S-125", f.GetProperty("spec").GetString());
    }

    [Fact]
    public void Identify_without_coordinates_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["identify", dataset]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Identify_with_bad_format_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(
            ["identify", dataset, "--lat", "51.085", "--lon", "1.30", "--format", "bogus"]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Identify_with_nan_coordinate_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["identify", dataset, "--lat", "NaN", "--lon", "1.30"]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Identify_positional_combined_with_layer_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(
            ["identify", dataset, "--layer", dataset, "--lat", "51.085", "--lon", "1.30"]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Identify_only_without_exchange_set_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(
            ["identify", "--layer", dataset, "--lat", "51.085", "--lon", "1.30", "--only", "S124"]);

        Assert.NotEqual(0, exit);
    }

    private static (int Exit, string Stdout) RunCapturingStdout(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            int exit = CliApp.Build().Run(args);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
