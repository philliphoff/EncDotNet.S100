using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end smoke tests that run the <c>s100 validate</c> command in-process
/// against a committed synthetic dataset fixture.
/// </summary>
public sealed class ValidateCommandTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public void Validate_conformant_dataset_returns_success()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["validate", dataset]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Validate_conformant_dataset_with_strict_returns_success()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["validate", dataset, "--strict"]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Validate_emits_well_formed_json()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (exit, stdout) = RunCapturingStdout(["validate", dataset, "--format", "json"]);

        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("S-127", root.GetProperty("specification").GetString());
        Assert.True(root.GetProperty("rulesAvailable").GetBoolean());
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("findings").ValueKind);
    }

    [Fact]
    public void Validate_with_missing_dataset_returns_nonzero()
    {
        int exit = CliApp.Build().Run(["validate", "does-not-exist.gml"]);
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Validate_with_bad_format_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["validate", dataset, "--format", "bogus"]);
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
