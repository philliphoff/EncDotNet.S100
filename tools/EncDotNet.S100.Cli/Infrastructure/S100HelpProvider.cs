using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Adds <c>--skill</c> discovery to Spectre's standard human-oriented help.
/// </summary>
internal sealed class S100HelpProvider(ICommandAppSettings settings) : HelpProvider(settings)
{
    public override IEnumerable<IRenderable> GetOptions(
        ICommandModel model,
        ICommandInfo? command)
    {
        foreach (var renderable in base.GetOptions(model, command))
        {
            yield return renderable;
        }

        if (command is not null)
        {
            yield break;
        }

        var grid = new Grid();
        grid.AddColumn(new GridColumn { Padding = new Padding(4, 4), NoWrap = true });
        grid.AddColumn(new GridColumn { Padding = new Padding(0, 0) });
        grid.AddRow(
            new Text("--skill"),
            new Text("Show the complete agent-oriented CLI guide as Markdown."));
        yield return grid;
    }
}
