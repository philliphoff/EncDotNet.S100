using EncDotNet.S100.Datasets.Pipelines;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// What the CLI says when no product claims a dataset file: the fixed
/// "Could not detect" line, plus why when the factory can tell (for example
/// an HDF5 file with no <c>productSpecification</c>, #1092). The same reason
/// reaches the viewer and MCP through
/// <see cref="DatasetPipelineFactory.DescribeUnrecognizedDataset(string)"/>.
/// </summary>
internal static class UnrecognizedDatasetMessage
{
    /// <summary>The plain-text message, for JSON output and warnings.</summary>
    /// <param name="path">The dataset path as the user gave it.</param>
    public static string Describe(string path) =>
        DatasetPipelineFactory.DescribeUnrecognizedDataset(path) is { } reason
            ? $"Could not detect an S-100 product specification for: {path}. {reason}"
            : $"Could not detect an S-100 product specification for: {path}";

    /// <summary>Writes the message to the console: the red line, then the reason.</summary>
    /// <param name="path">The dataset path as the user gave it.</param>
    public static void Write(string path)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]Could not detect an S-100 product specification for:[/] {path}");
        if (DatasetPipelineFactory.DescribeUnrecognizedDataset(path) is { } reason)
            AnsiConsole.MarkupLineInterpolated($"{reason}");
    }

    /// <summary>The warning for a dataset skipped because no product claims it.</summary>
    /// <param name="path">The file to describe.</param>
    /// <param name="shown">The path as the warning names it, when not <paramref name="path"/>.</param>
    public static string Skipped(string path, string? shown = null) =>
        DatasetPipelineFactory.DescribeUnrecognizedDataset(path) is { } reason
            ? $"Skipped unsupported dataset (no known product specification): {shown ?? path}. {reason}"
            : $"Skipped unsupported dataset (no known product specification): {shown ?? path}";
}
