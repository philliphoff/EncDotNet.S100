using System.ComponentModel;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Shared settings for commands that take a dataset path as their first
/// argument. Validates that the dataset file exists before the command runs.
/// </summary>
internal class DatasetCommandSettings : CommandSettings
{
    [CommandArgument(0, "<dataset>")]
    [Description("Path to the S-100 dataset file (.h5, .000, or .gml).")]
    public string DatasetPath { get; init; } = string.Empty;

    [CommandOption("--debug")]
    [Description("Show full stack traces on error.")]
    public bool Debug { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetPath))
            return Spectre.Console.ValidationResult.Error("A dataset path is required.");

        if (!File.Exists(DatasetPath))
            return Spectre.Console.ValidationResult.Error($"Dataset file not found: {DatasetPath}");

        return Spectre.Console.ValidationResult.Success();
    }
}
