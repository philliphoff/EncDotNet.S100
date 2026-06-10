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

    [Fact]
    public void Validate_with_empty_suppress_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        int exit = CliApp.Build().Run(["validate", dataset, "--suppress", ","]);
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Validate_suppressing_all_rules_drops_findings_and_passes()
    {
        // The S-57 cell is translated to S-101 and flagged by the
        // "S101-as-S57/*" rule family; suppressing that family should leave
        // no effective findings and flip the dataset to valid (exit 0).
        var dataset = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (baselineExit, baselineOut) = RunCapturingStdout(["validate", dataset, "--format", "json"]);
        using var baseline = JsonDocument.Parse(baselineOut);
        int baselineFindings = baseline.RootElement.GetProperty("findings").GetArrayLength();
        Skip.If(baselineFindings == 0, "Fixture produced no findings to suppress.");
        Assert.Equal(6, baselineExit);

        var (exit, stdout) = RunCapturingStdout(
            ["validate", dataset, "--format", "json", "--suppress", "S101-as-S57/*"]);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(0, root.GetProperty("findings").GetArrayLength());
        Assert.Equal(baselineFindings, root.GetProperty("suppressedCount").GetInt32());
        Assert.Equal(0, exit);
    }

    [Fact]
    public void Validate_suppressing_one_rule_keeps_other_errors_failing()
    {
        var dataset = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (baselineExit, _) = RunCapturingStdout(["validate", dataset, "--format", "json"]);
        Skip.IfNot(baselineExit == 6, "Fixture did not produce failing findings.");

        // Suppress just one of several flagged rules; remaining errors keep it failing.
        int exit = CliApp.Build().Run(
            ["validate", dataset, "--suppress", "S101-as-S57/S101-R-1.2"]);
        Assert.Equal(6, exit);
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
