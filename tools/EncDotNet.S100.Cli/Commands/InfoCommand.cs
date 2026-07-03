using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
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

            var processor = DatasetProcessorLoader.Create(factory, spec, settings);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Dataset", Markup.Escape(settings.DatasetPath));
            table.AddRow("Specification", Markup.Escape(processor.Spec.Name));
            table.AddRow("Edition", Markup.Escape(DescribeDeclaredEdition(processor.Spec)));

            var assessment = processor.VersionAssessment;
            if (assessment is not null)
            {
                table.AddRow(
                    "Supported edition",
                    Markup.Escape(string.Join(", ", assessment.Supported)));

                if (assessment.Catalogue is { } catalogue)
                {
                    table.AddRow(
                        "Catalogue version",
                        Markup.Escape($"{catalogue.Name} {catalogue.Version}"));
                }

                var (label, colour) = assessment.Kind switch
                {
                    SpecMatchKind.Exact => ("matches", "green"),
                    SpecMatchKind.CatalogueNewerCompatible => ("application supports a newer, compatible edition", "green"),
                    SpecMatchKind.CatalogueOlder => ("application supports an older edition — may be incomplete", "yellow"),
                    SpecMatchKind.MajorDivergence => ("incompatible edition — may be incomplete or incorrect", "red"),
                    _ => ("not declared", "grey"),
                };
                table.AddRow("Version match", $"[{colour}]{Markup.Escape(label)}[/]");
            }

            table.AddRow("Headless render", processor is IHeadlessImageRenderer ? "[green]yes[/]" : "[yellow]no[/]");

            if (processor is IDisplayModeAwareDatasetProcessor displayModeAware &&
                displayModeAware.DeclaredDisplayModeIds.Count > 0)
            {
                var tokens = displayModeAware.DeclaredDisplayModeIds.Select(DisplayModeCliToken).ToList();
                table.AddRow("Display modes", Markup.Escape(string.Join(", ", tokens)));
                if (tokens.Contains("ice-navigational"))
                {
                    table.AddRow(string.Empty, "[yellow]ice-navigational is a provisional preview, not a POLARIS/RIO risk computation[/]");
                }
            }

            if (processor is ITimeAwareDatasetProcessor timeAware && timeAware.AvailableTimes.Count > 0)
            {
                table.AddRow("Time steps", timeAware.AvailableTimes.Count.ToString());
            }

            AnsiConsole.Write(table);

            // Non-blocking: surface a one-line warning on stderr when the
            // declared edition diverges from what this build implements in a
            // way that may degrade rendering (issue #248).
            if (assessment?.IsWarning == true)
            {
                Console.Error.WriteLine(assessment.BuildMessage());
            }

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
        catch (NotSupportedException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 4;
        }
        catch (S100DatasetNotSupportedException ex)
        {
            // Recognised-but-not-yet-implemented spec feature (e.g. data coding
            // format 1). Does not derive from NotSupportedException, so it needs
            // its own catch to map to exit 4 rather than the generic exit-1 path.
            // See issue #253.
            AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 4;
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

    /// <summary>
    /// Formats the declared edition for display, substituting a placeholder
    /// when the dataset declares no edition (<see cref="SpecVersion"/> default).
    /// </summary>
    private static string DescribeDeclaredEdition(SpecRef spec) =>
        spec.Edition == default ? "(not declared)" : spec.Edition.ToString();

    /// <summary>
    /// Maps a spec-native display-mode id to the friendly <c>render
    /// --display-mode</c> token, falling back to the raw id for modes with no
    /// CLI alias. Kept in step with
    /// <c>RenderCommand.TryParseDisplayMode</c> (issue #416).
    /// </summary>
    private static string DisplayModeCliToken(string displayModeId) => displayModeId switch
    {
        "IceScientificIceactDisplayMode" => "ice-concentration",
        "IceScientificIcesodDisplayMode" => "ice-sod",
        "IceNavigationalDisplayMode" => "ice-navigational",
        _ => displayModeId,
    };
}
