using System.ComponentModel;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Settings for <c>s100 s57 convert</c>: the S-57 source cell to read and the
/// S-101 dataset file to write.
/// </summary>
internal sealed class S57ConvertCommandSettings : CommandSettings
{
    [CommandArgument(0, "<source>")]
    [Description("Path to the source S-57 base cell (.000).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <output>")]
    [Description("Path of the S-101 dataset file to write (e.g. my-cell.000).")]
    public string OutputPath { get; init; } = string.Empty;

    [CommandOption("--report <report>")]
    [Description("Write a machine-readable (JSON) translation diagnostics report to this path.")]
    public string? ReportPath { get; init; }

    [CommandOption("--no-updates")]
    [Description("Do not auto-discover and fold sibling update files (.001, .002, …) before converting.")]
    public bool NoUpdates { get; init; }

    [CommandOption("--debug")]
    [Description("Show full stack traces on error.")]
    public bool Debug { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return Spectre.Console.ValidationResult.Error("A source S-57 dataset path is required.");

        if (!File.Exists(SourcePath))
            return Spectre.Console.ValidationResult.Error($"Source S-57 dataset not found: {SourcePath}");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return Spectre.Console.ValidationResult.Error("An output path is required (-o|--output).");

        var dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            return Spectre.Console.ValidationResult.Error($"Output directory does not exist: {dir}");

        if (!string.IsNullOrWhiteSpace(ReportPath))
        {
            var reportDir = Path.GetDirectoryName(Path.GetFullPath(ReportPath));
            if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                return Spectre.Console.ValidationResult.Error($"Report directory does not exist: {reportDir}");
        }

        return Spectre.Console.ValidationResult.Success();
    }
}
