using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 feed export</c> writes a folder, exchange set or dataset as a
/// static S-100 feed (issue #680) — <c>feed.json</c> plus one zip per
/// dataset — for hosting on any web server, object store or file share.
/// Re-running it updates the export incrementally.
/// </summary>
internal sealed class FeedExportCommand : AsyncCommand<FeedExportCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("A folder (scanned recursively), an exchange set (folder, CATALOG.XML, CATALOG.031 or .zip), or a single dataset.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--out <DIRECTORY>")]
        [Description("The folder to write the feed into (created if needed). Must not be inside <path>.")]
        public string Output { get; init; } = string.Empty;

        [CommandOption("--title <TITLE>")]
        [Description("The feed's title (default: the folder or file name).")]
        public string? Title { get; init; }

        public override ValidationResult Validate()
        {
            if (!Directory.Exists(Path) && !File.Exists(Path))
                return ValidationResult.Error($"'{Path}' does not exist.");
            if (string.IsNullOrWhiteSpace(Output))
                return ValidationResult.Error("--out is required.");
            if (Directory.Exists(Path) && FeedExporter.IsInside(Output, Path))
                return ValidationResult.Error("--out must not be inside <path>: the export would be published with the data next time.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var publisher = new FeedPublisher(settings.Path, settings.Title);
        AnsiConsole.MarkupLine($"Indexing [bold]{Markup.Escape(publisher.Path)}[/]…");
        var feed = await publisher.GetAsync(force: true).ConfigureAwait(false);

        var problems = publisher.Diagnostics.Where(d => d.Severity != Collections.IndexDiagnosticSeverity.Info).ToArray();
        foreach (var diagnostic in problems.Take(5))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(diagnostic.Message)}[/] [grey]{Markup.Escape(diagnostic.Path ?? string.Empty)}[/]");
        if (problems.Length > 5)
            AnsiConsole.MarkupLine(string.Create(CultureInfo.CurrentCulture, $"[yellow]…and {problems.Length - 5:N0} more problem(s).[/]"));

        var output = System.IO.Path.GetFullPath(settings.Output);
        var result = await FeedExporter.ExportAsync(
            feed, output,
            new Progress<string>(line => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(line)}[/]"))).ConfigureAwait(false);

        AnsiConsole.MarkupLine(string.Create(CultureInfo.CurrentCulture,
            $"Exported [bold]{result.Items:N0}[/] dataset(s) to [bold]{Markup.Escape(output)}[/]: {result.Written:N0} written, {result.Unchanged:N0} unchanged, {result.Removed:N0} removed ({FormatBytes(result.TotalBytes)})."));
        if (result.Failed > 0)
            AnsiConsole.MarkupLine(string.Create(CultureInfo.CurrentCulture, $"[yellow]{result.Failed:N0} dataset(s) could not be written and were left out.[/]"));
        AnsiConsole.MarkupLine("Upload the folder's contents to a web host, then add its feed.json URL in the viewer (Library → Online Catalogue → Add URL).");
        return result.Failed > 0 ? 1 : 0;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0.#} MB"),
        >= 1L << 10 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 10):0.#} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
    };
}
