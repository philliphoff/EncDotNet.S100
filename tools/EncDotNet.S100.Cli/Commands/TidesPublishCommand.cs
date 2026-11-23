using System.ComponentModel;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Tides.Stations.Noaa;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides publish</c>: prepares a tide station catalogue for a mirror
/// (#1674). It checks a newly downloaded catalogue against the one the
/// mirror publishes now, keeps the published file when the data hasn't
/// changed, and writes the catalogue and its manifest into a folder to
/// upload.
/// </summary>
internal sealed class TidesPublishCommand : AsyncCommand<TidesPublishCommand.Settings>
{
    /// <summary>The exit code for a catalogue that fails the publish check.</summary>
    public const int RefusedExitCode = 9;

    /// <summary>The most station identifiers the summary lists for each kind of change.</summary>
    private const int MaxListed = 20;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<OUTPUT>")]
        [Description("The folder to write tide-stations.json.gz and tide-stations.manifest.json into, to upload to the mirror.")]
        public string Output { get; init; } = string.Empty;

        [CommandOption("--catalogue <FILE>")]
        [Description("The catalogue to publish: a tide-stations file, usually from 's100 tides update --from noaa'. Required.")]
        public string? Catalogue { get; init; }

        [CommandOption("--previous <FOLDER>")]
        [Description("A folder with the mirror's published tide-stations.json.gz and tide-stations.manifest.json, to check the catalogue against. Leave it out, or leave the folder empty, for a first publication.")]
        public string? Previous { get; init; }

        [CommandOption("--crawled")]
        [Description("The catalogue is a new full download from the provider: the manifest's crawled time becomes now. Leave it out when the catalogue is the published one, checked with 's100 tides update --check'.")]
        public bool Crawled { get; init; }

        [CommandOption("--provider <NAME>")]
        [Description("The provider the catalogue must name. Defaults to 'NOAA CO-OPS'.")]
        public string Provider { get; init; } = NoaaTideStationClient.ProviderName;

        [CommandOption("--max-drop <PERCENT>")]
        [Description("The largest share of stations, or of stations that can be predicted, the catalogue may lose against the published one, in percent. Defaults to 5.")]
        public double MaxDropPercent { get; init; } = TideCataloguePublishCheck.DefaultMaxDropFraction * 100;

        [CommandOption("--summary <FILE>")]
        [Description("A Markdown file to append a summary to, such as $GITHUB_STEP_SUMMARY. The summary is also written to standard output.")]
        public string? Summary { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Output))
                return ValidationResult.Error("Give the folder to write the catalogue and manifest into.");
            if (string.IsNullOrWhiteSpace(Catalogue))
                return ValidationResult.Error("--catalogue is required.");
            if (!(MaxDropPercent >= 0 && MaxDropPercent <= 100))
                return ValidationResult.Error("--max-drop must be a percentage from 0 to 100.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var cataloguePath = Path.GetFullPath(settings.Catalogue ?? string.Empty);
        if (!File.Exists(cataloguePath))
        {
            Console.Error.WriteLine($"Error: There's no catalogue at {cataloguePath}.");
            return 2;
        }

        var output = Path.GetFullPath(settings.Output);
        var previousFile = settings.Previous is { } p ? Path.Combine(Path.GetFullPath(p), TideCatalogueManifest.DefaultCatalogueFileName) : null;
        var previousManifestFile = settings.Previous is { } m ? Path.Combine(Path.GetFullPath(m), TideCatalogueManifest.DefaultFileName) : null;

        TideStationCatalogue candidate;
        TideStationCatalogue? previous = null;
        TideCatalogueManifest? previousManifest = null;
        try
        {
            candidate = await TideStationCatalogueFile.LoadAsync(cataloguePath, cancellationToken).ConfigureAwait(false);
            if (previousFile is not null && File.Exists(previousFile))
                previous = await TideStationCatalogueFile.LoadAsync(previousFile, cancellationToken).ConfigureAwait(false);
            if (previous is not null && previousManifestFile is not null && File.Exists(previousManifestFile))
                previousManifest = await TideCatalogueManifest.LoadAsync(previousManifestFile, cancellationToken).ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        var problems = TideCataloguePublishCheck.FindProblems(previous, candidate, settings.Provider, settings.MaxDropPercent / 100);
        if (problems.Count > 0)
        {
            var refused = new StringBuilder()
                .AppendLine("### Tide station catalogue not published")
                .AppendLine()
                .AppendLine("The new catalogue failed the publish check, so the published one stays:")
                .AppendLine();
            foreach (var problem in problems)
                refused.AppendLine(CultureInfo.InvariantCulture, $"- {problem}");
            await WriteSummaryAsync(settings.Summary, refused.ToString(), cancellationToken).ConfigureAwait(false);
            foreach (var problem in problems)
                Console.Error.WriteLine($"Error: {problem}");
            return RefusedExitCode;
        }

        var comparison = previous is null ? null : TideCatalogueComparison.Compare(previous, candidate);
        var unchanged = comparison is { HasChanges: false };
        var outputFile = Path.Combine(output, TideCatalogueManifest.DefaultCatalogueFileName);
        Directory.CreateDirectory(output);

        // Unchanged data keeps the published file, and with it the date its
        // data was retrieved, so clients don't download the same stations again.
        var published = unchanged && previous is not null ? previous : candidate;
        if (unchanged && previousFile is not null)
        {
            if (!string.Equals(previousFile, outputFile, StringComparison.Ordinal))
                File.Copy(previousFile, outputFile, overwrite: true);
        }
        else
        {
            await TideStationCatalogueFile.SaveAsync(outputFile, candidate, cancellationToken).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var content = await File.ReadAllBytesAsync(outputFile, cancellationToken).ConfigureAwait(false);
        var crawled = settings.Crawled
            ? now
            : previousManifest?.Crawled ?? published.Retrieved ?? now;
        var manifest = TideCatalogueManifest.Create(content, published, crawled, now);
        await manifest.SaveAsync(Path.Combine(output, TideCatalogueManifest.DefaultFileName), cancellationToken).ConfigureAwait(false);

        await WriteSummaryAsync(settings.Summary, Summarize(manifest, comparison, settings.Crawled), cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine($"Wrote {outputFile} and {TideCatalogueManifest.DefaultFileName}.");
        return 0;
    }

    private static async Task WriteSummaryAsync(string? path, string summary, CancellationToken cancellationToken)
    {
        Console.Out.Write(summary);
        if (!string.IsNullOrWhiteSpace(path))
            await File.AppendAllTextAsync(path, summary + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What was published, as Markdown.</summary>
    internal static string Summarize(TideCatalogueManifest manifest, TideCatalogueComparison? comparison, bool crawled)
    {
        var result = comparison switch
        {
            null => "First publication.",
            { HasChanges: true } => "New data: the catalogue changed.",
            _ when crawled => "No change: downloaded again and kept the published catalogue.",
            _ => "No change: the provider has no newer data.",
        };
        var summary = new StringBuilder()
            .AppendLine("### Tide station catalogue")
            .AppendLine()
            .AppendLine(result)
            .AppendLine()
            .AppendLine("| | |")
            .AppendLine("|---|---|")
            .AppendLine(CultureInfo.InvariantCulture, $"| Provider | {manifest.Provider ?? "unknown"} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Retrieved | {TideCli.FormatDate(manifest.Retrieved)} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Crawled | {TideCli.FormatDate(manifest.Crawled)} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Checked | {TideCli.FormatDate(manifest.Checked)} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Datum epoch | {manifest.DatumEpoch ?? "none"} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Tide stations | {manifest.StationCount} ({manifest.HarmonicStationCount} harmonic, {manifest.SubordinateStationCount} subordinate) |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Current stations | {manifest.CurrentStationCount} |")
            .AppendLine(CultureInfo.InvariantCulture, $"| File | `{manifest.File}`, {manifest.Size.ToString("N0", CultureInfo.InvariantCulture)} bytes, SHA-256 `{manifest.Sha256}` |");

        if (comparison is { HasChanges: true })
        {
            summary.AppendLine();
            if (!string.Equals(comparison.PreviousDatumEpoch, comparison.DatumEpoch, StringComparison.Ordinal))
                summary.AppendLine(CultureInfo.InvariantCulture, $"- Datum epoch: {comparison.PreviousDatumEpoch ?? "none"} → {comparison.DatumEpoch ?? "none"}");
            List("Tide stations added", comparison.AddedStationIds);
            List("Tide stations removed", comparison.RemovedStationIds);
            List("Tide stations changed", comparison.ChangedStationIds);
            List("Current stations added", comparison.AddedCurrentStationIds);
            List("Current stations removed", comparison.RemovedCurrentStationIds);
            List("Current stations changed", comparison.ChangedCurrentStationIds);
        }

        return summary.ToString();

        void List(string label, IReadOnlyList<string> ids)
        {
            if (ids.Count == 0)
                return;
            var shown = string.Join(", ", ids.Take(MaxListed));
            summary.AppendLine(ids.Count > MaxListed
                ? $"- {label} ({ids.Count}): {shown} and {ids.Count - MaxListed} more"
                : $"- {label} ({ids.Count}): {shown}");
        }
    }
}
