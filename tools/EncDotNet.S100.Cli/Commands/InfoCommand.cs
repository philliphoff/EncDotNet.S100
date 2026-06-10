using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Hdf5;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 info &lt;dataset&gt;</c> — reports the detected product specification,
/// edition, whether the dataset can be rendered headlessly, and (for
/// time-series datasets) the available time steps with their indices.
/// </summary>
internal sealed class InfoCommand : Command<DatasetCommandSettings>
{
    public override int Execute(CommandContext context, DatasetCommandSettings settings)
    {
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        try
        {
            var spec = DatasetPipelineFactory.DetectProductSpec(settings.DatasetPath);
            if (spec is null)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Could not detect an S-100 product specification for:[/] {settings.DatasetPath}");
                return 2;
            }

            var processor = factory.CreateProcessor(settings.DatasetPath);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Dataset", Markup.Escape(settings.DatasetPath));
            table.AddRow("Specification", Markup.Escape(processor.Spec.Name));
            table.AddRow("Edition", Markup.Escape(processor.Spec.Edition.ToString()));
            table.AddRow("Headless render", processor is IHeadlessImageRenderer ? "[green]yes[/]" : "[yellow]no[/]");

            if (processor is ITimeAwareDatasetProcessor timeAware && timeAware.AvailableTimes.Count > 0)
            {
                table.AddRow("Time steps", timeAware.AvailableTimes.Count.ToString());
            }

            AnsiConsole.Write(table);

            if (processor is ITimeAwareDatasetProcessor ta && ta.AvailableTimes.Count > 0)
            {
                var timeTable = new Table().Border(TableBorder.Rounded).Title("Available time steps");
                timeTable.AddColumn("Index");
                timeTable.AddColumn("Time (UTC)");
                for (int i = 0; i < ta.AvailableTimes.Count; i++)
                {
                    timeTable.AddRow(
                        i.ToString(),
                        ta.AvailableTimes[i].ToString("yyyy-MM-dd HH:mm:ss"));
                }
                AnsiConsole.Write(timeTable);
            }

            return 0;
        }
        catch (S100DatasetSchemaException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Non-conforming dataset:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 5;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            catalogueManager.Dispose();
        }
    }
}
