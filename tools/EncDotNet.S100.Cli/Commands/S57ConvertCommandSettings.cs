using System.ComponentModel;
using EncDotNet.S100.Datasets.S57;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Settings for <c>s100 s57 convert</c>: the S-57 source cell to read, the
/// S-100 product to translate it into, and the dataset file to write.
/// </summary>
internal sealed class S57ConvertCommandSettings : CommandSettings
{
    [CommandArgument(0, "<source>")]
    [Description("Path to the source S-57 base cell (.000).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <output>")]
    [Description("Path of the S-101 or S-401 dataset file to write (e.g. my-cell.000).")]
    public string OutputPath { get; init; } = string.Empty;

    [CommandOption("--target <TARGET>")]
    [Description("The S-100 product to write: auto (default) writes S-401 for an inland ENC (DSID PRSP = 10) and S-101 otherwise; s101 or s401 forces that product. Forcing s101 on an inland cell drops its inland features; forcing s401 on a maritime cell is allowed but warns.")]
    [DefaultValue("auto")]
    public string Target { get; init; } = "auto";

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

        if (!TryParseTarget(Target, out _))
            return Spectre.Console.ValidationResult.Error(
                $"Invalid --target value '{Target}'. Use auto, s101 or s401.");

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

    /// <summary>
    /// Parses a <c>--target</c> token (case-insensitive; <c>s101</c> and
    /// <c>S-101</c> are equivalent) into the forced translation target, or
    /// <c>null</c> for <c>auto</c> (choose from the cell's DSID <c>PRSP</c>).
    /// </summary>
    internal static bool TryParseTarget(string? value, out S57TranslationTarget? target)
    {
        target = null;
        switch (value?.Trim().Replace("-", string.Empty).ToLowerInvariant())
        {
            case null or "" or "auto":
                return true;
            case "s101":
                target = S57TranslationTarget.S101;
                return true;
            case "s401":
                target = S57TranslationTarget.S401;
                return true;
            default:
                return false;
        }
    }
}
