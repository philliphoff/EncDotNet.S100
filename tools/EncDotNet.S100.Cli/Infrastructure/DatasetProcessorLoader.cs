using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds an <see cref="IDatasetProcessor"/> for a command's dataset path,
/// applying sibling S-101 sequential updates (<c>….001</c>, <c>….002</c>, …)
/// when the path is an <c>….000</c> base cell and <c>--no-updates</c> was not
/// supplied. The apply outcome is summarised to the console so the operator
/// can see how many updates were folded in and whether any failed. Updates are
/// applied best-effort and never block rendering. S-101 / S-100 Part 10a.
/// </summary>
internal static class DatasetProcessorLoader
{
    /// <summary>
    /// Creates a processor for <paramref name="settings"/>, applying discovered
    /// S-101 updates unless suppressed.
    /// </summary>
    /// <param name="factory">The configured pipeline factory.</param>
    /// <param name="spec">The detected product specification (e.g. <c>"S-101"</c>).</param>
    /// <param name="settings">The dataset command settings.</param>
    public static IDatasetProcessor Create(
        DatasetPipelineFactory factory,
        string spec,
        DatasetCommandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settings);

        if (spec == "S-101" && !settings.NoUpdates)
        {
            var updates = S101FilesystemUpdateDiscovery.FindSequentialUpdates(settings.DatasetPath);
            if (updates.Count > 0)
            {
                var processor = factory.CreateS101ProcessorWithUpdates(settings.DatasetPath, updates);
                if (processor is S101DatasetProcessor { UpdateReport: { } report })
                    ReportUpdateOutcome(report);
                return processor;
            }
        }

        return factory.CreateProcessor(settings.DatasetPath);
    }

    private static void ReportUpdateOutcome(S101UpdateReport report)
    {
        var applied = report.AppliedThroughUpdateNumber - report.BaseUpdateNumber;
        if (applied > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Applied {applied} S-101 update(s) through update {report.AppliedThroughUpdateNumber} (+{report.Inserted}/~{report.Modified}/-{report.Deleted} records).[/]");
        }

        foreach (var message in report.Messages)
        {
            var colour = message.Severity switch
            {
                S101UpdateSeverity.Error => "red",
                S101UpdateSeverity.Warning => "yellow",
                _ => "grey",
            };
            AnsiConsole.MarkupLine($"[{colour}]{Markup.Escape(message.Text)}[/]");
        }
    }
}
