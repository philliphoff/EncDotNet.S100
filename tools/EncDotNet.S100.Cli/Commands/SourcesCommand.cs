using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 sources</c>: the live data sources <c>--source</c> and MCP's
/// <c>add_data_source</c> take (data source design note D9), with their options
/// and what each still needs.
/// </summary>
internal sealed class SourcesCommand : Command<SourcesCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Id");
        table.AddColumn("Shows");
        table.AddColumn("Runs in views narrower than");
        table.AddColumn("Options");
        table.AddColumn("Needs");
        foreach (var registration in SourceRegistry.CreateDefault().Registrations)
        {
            var fields = SourceOptionsForm.Fields(registration);
            var options = fields.Count == 0
                ? "[grey]none[/]"
                : string.Join("\n", fields.Select(f => Markup.Escape(
                    f.Kind == SourceOptionKind.Secret && f.EnvironmentVariable is { } variable
                        ? $"{f.Label}: ${variable}"
                        : $"{f.Name}{(f.Required ? "*" : string.Empty)}: {f.Label}")));
            var needs = SourceOptionsForm.MissingRequired(registration, null) is { } missing
                ? $"[yellow]{Markup.Escape(Infrastructure.LiveSourceFactory.Needs(registration, missing))}[/]"
                : "[green]ready[/]";
            table.AddRow(
                Markup.Escape(registration.Id),
                Markup.Escape(registration.DisplayName),
                registration.Activation.MaxViewportSpanDegrees is { } span
                    ? string.Create(CultureInfo.InvariantCulture, $"{span:0.##}°")
                    : "[grey]any view[/]",
                options,
                needs);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Add one with --source id[[:name=value,...]] on mcp serve or identify, or add_data_source over MCP. Keys ($VARIABLE) come from environment variables only.[/]");
        return 0;
    }
}
