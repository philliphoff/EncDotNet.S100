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
    /// Specs whose processors expose the headless Skia render path. Coverage
    /// specs (S-102/104/111) support gridded datasets only; fixed-station
    /// (data coding format 3 / 8) datasets are rejected at render time.
    /// </summary>
    private static readonly HashSet<string> HeadlessSpecs = new(StringComparer.Ordinal)
    {
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

        foreach (var spec in Specification.AvailableSpecs)
        {
            table.AddRow(
                spec,
                Specification.HasPortrayalCatalogue(spec) ? "[green]yes[/]" : "[grey]no[/]",
                HeadlessSpecs.Contains(spec) ? "[green]yes[/]" : "[yellow]no[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            "[grey]Coverage specs (S-102/104/111) render gridded datasets only; " +
            "fixed-station datasets are not supported headlessly.[/]");
        return 0;
    }
}
