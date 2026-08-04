using EncDotNet.S100.Specifications;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 list-specs</c> — lists the S-100 product specifications the CLI knows
/// about and whether each supports the headless image-render path.
/// </summary>
internal sealed class ListSpecsCommand : Command<ListSpecsCommand.Settings>
{
    /// <summary>
    /// Specs whose processors expose the headless Skia render path. S-104 and
    /// S-111 support both gridded and positioned station/node glyph datasets.
    /// S-57 datasets are translated in-memory to S-101 and rendered through
    /// the S-101 portrayal pipeline.
    /// </summary>
    private static readonly HashSet<string> HeadlessSpecs = new(StringComparer.Ordinal)
    {
        "S-57",
        "S-101", "S-102", "S-104", "S-111",
        "S-122", "S-124", "S-125", "S-127", "S-128", "S-129", "S-131", "S-201", "S-411", "S-421",
    };

    internal sealed class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Spec");
        table.AddColumn("Portrayal");
        table.AddColumn("Headless render");

        // S-57 is not in Specification.AvailableSpecs (it has no bundled
        // FC/PC of its own — it borrows S-101's catalogues at render time)
        // but the CLI does support it, so report it explicitly.
        table.AddRow(
            "S-57",
            "[grey]via S-101[/]",
            HeadlessSpecs.Contains("S-57") ? "[green]yes[/]" : "[yellow]no[/]");

        foreach (var spec in Specification.AvailableSpecs)
        {
            table.AddRow(
                spec,
                Specification.HasPortrayalCatalogue(spec) ? "[green]yes[/]" : "[grey]no[/]",
                HeadlessSpecs.Contains(spec) ? "[green]yes[/]" : "[yellow]no[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
