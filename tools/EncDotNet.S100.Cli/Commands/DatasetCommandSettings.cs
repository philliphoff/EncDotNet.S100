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
    [Description("Path to the S-100 dataset file (.h5, .000, or .gml), or a cell of an NIS file geodatabase (<path>.gdb/<DSNM>).")]
    public string DatasetPath { get => _datasetPath; init => _datasetPath = value; }

    private string _datasetPath = string.Empty;

    [CommandOption("--debug")]
    [Description("Show full stack traces on error, and surface host/Lua portrayal diagnostics on stderr.")]
    public bool Debug { get; init; }

    [CommandOption("--no-updates")]
    [Description("Do not apply sibling S-101 sequential updates (.001, .002, …) found alongside an .000 base cell. By default they are applied best-effort.")]
    [DefaultValue(false)]
    public bool NoUpdates { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetPath))
            return Spectre.Console.ValidationResult.Error("A dataset path is required.");

        var original = ResolveNisCellPath();
        if (!File.Exists(DatasetPath))
            return Spectre.Console.ValidationResult.Error(Infrastructure.NisCells.ErrorFor(original) ?? $"Dataset file not found: {DatasetPath}");

        return Spectre.Console.ValidationResult.Success();
    }

    /// <summary>
    /// Replaces a cell path of an NIS file geodatabase (<c>&lt;path&gt;.gdb/&lt;DSNM&gt;</c>)
    /// in <see cref="DatasetPath"/> with its translated S-57 cell (#1777).
    /// </summary>
    /// <returns>The path as given, for <see cref="Infrastructure.NisCells.ErrorFor"/>.</returns>
    protected string ResolveNisCellPath()
    {
        var original = _datasetPath;
        _datasetPath = Infrastructure.NisCells.ResolveArgument(_datasetPath) ?? _datasetPath;
        return original;
    }
}
