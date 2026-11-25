using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.Cli.Reference;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 reference export</c>: writes a product's chart reference as a
/// static HTML site, from the bundled catalogues or a portrayal catalogue
/// on disk (symbol dictionary design note §7, R14, #1723). When the rules
/// have been probed for those catalogues (R13a), the pages carry the
/// variant tables, value columns and before/after pairs.
/// </summary>
internal sealed class ReferenceExportCommand : AsyncCommand<ReferenceExportCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--spec <SPEC>")]
        [Description("The product specification, such as S-101. Defaults to S-101.")]
        public string Spec { get; init; } = "S-101";

        [CommandOption("--palette <PALETTE>")]
        [Description("The palette the glyphs are drawn in and the pages follow: day, dusk or night. Symbol pages show all three. Defaults to day.")]
        public string Palette { get; init; } = "day";

        [CommandOption("-o|--out <DIR>")]
        [Description("The folder to write the site to. It must be empty, or an earlier export, which is replaced.")]
        public string? Out { get; init; }

        [CommandOption("--catalogue <PATH>")]
        [Description("A portrayal catalogue to describe instead of the bundled one: a folder or .zip holding portrayal_catalogue.xml.")]
        public string? Catalogue { get; init; }

        [CommandOption("--feature-catalogue <FILE>")]
        [Description("A feature catalogue XML file to use instead of the bundled one.")]
        public string? FeatureCatalogue { get; init; }

        [CommandOption("--probes <FILE>")]
        [Description("Rule probe results to use (from s100 reference probe) instead of the ones committed with the bundled catalogue. They must be for the same catalogues.")]
        public string? Probes { get; init; }

        [CommandOption("--no-probes")]
        [Description("Leave out the variant tables, value columns and before/after pairs, even when probe results are available.")]
        public bool NoProbes { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Out))
                return ValidationResult.Error("--out is required: the folder to write the site to.");
            if (ParsePalette(Palette) is null)
                return ValidationResult.Error($"--palette {Palette} isn't a palette; use day, dusk or night.");
            if (Catalogue is null && !Specification.HasPortrayalCatalogue(Spec))
                return ValidationResult.Error($"{Spec} has no bundled portrayal catalogue; pass --catalogue.");
            if (Catalogue is not null && !Directory.Exists(Catalogue) && !File.Exists(Catalogue))
                return ValidationResult.Error($"--catalogue {Catalogue} doesn't exist.");
            if (FeatureCatalogue is not null && !File.Exists(FeatureCatalogue))
                return ValidationResult.Error($"--feature-catalogue {FeatureCatalogue} doesn't exist.");
            if (Probes is not null && !File.Exists(Probes))
                return ValidationResult.Error($"--probes {Probes} doesn't exist.");
            return NoProbes && Probes is not null
                ? ValidationResult.Error("--probes and --no-probes can't be used together.")
                : ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = Path.GetFullPath(settings.Out!);
        if (PrepareOutput(output) is { } problem)
        {
            Console.Error.WriteLine($"Error: {problem}");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();
        var spec = new SpecRef(settings.Spec, default);

        IAssetSource portrayalSource = settings.Catalogue is null
            ? Specification.CreatePortrayalCatalogueSource(settings.Spec)
            : File.Exists(settings.Catalogue)
                ? ZipAssetSource.Create(Path.GetFullPath(settings.Catalogue))
                : FileSystemAssetSource.Create(Path.GetFullPath(settings.Catalogue));
        using var portrayals = new PortrayalCatalogueManager();
        portrayals.SetSource(spec, portrayalSource);
        using var features = settings.FeatureCatalogue is null
            ? new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue)
            : new FeatureCatalogueManager((string _) => File.OpenRead(settings.FeatureCatalogue));

        PortrayalCatalogueProvider provider;
        try
        {
            provider = portrayals.GetProvider(spec);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"Error: Couldn't read the portrayal catalogue: {ex.Message}");
            return 1;
        }

        var featureCatalogue = features.GetCatalogue(spec);
        var index = await ChartReferenceIndex.BuildAsync(spec, featureCatalogue, provider, cancellationToken).ConfigureAwait(false);

        string probeNote;
        if (settings.NoProbes)
        {
            probeNote = "left out (--no-probes)";
        }
        else
        {
            var fcHash = await features.GetCatalogueHashAsync(spec, cancellationToken).ConfigureAwait(false);
            var pcHash = await portrayals.GetCatalogueHashAsync(spec, cancellationToken).ConfigureAwait(false);
            RuleProbeSet? probes;
            if (settings.Probes is not null)
            {
                probes = RuleProbeStore.ReadFile(settings.Probes);
                if (!string.Equals(probes.Spec, settings.Spec, StringComparison.Ordinal) || !probes.Matches(fcHash, pcHash))
                {
                    Console.Error.WriteLine($"Error: {settings.Probes} is for other catalogues ({probes.Spec}); run s100 reference probe again.");
                    return 1;
                }
            }
            else
            {
                probes = await RuleProbeStore.TryLoadBundledAsync(settings.Spec, fcHash, pcHash, cancellationToken).ConfigureAwait(false);
            }

            index = index.WithProbes(probes);
            probeNote = probes is null
                ? "none for these catalogues, so no variant tables"
                : string.Create(CultureInfo.InvariantCulture, $"{probes.FeatureTypes.Count} feature types ({probes.FeatureTypes.Count(f => f.IsPartial)} partial)");
        }

        var artwork = new ChartReferenceArtwork(provider);
        var palette = ParsePalette(settings.Palette)!.Value;
        var result = await new ReferenceHtmlExporter(index, artwork, palette, output).ExportAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        AnsiConsole.MarkupLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Exported the [bold]{Markup.Escape(settings.Spec)}[/] chart reference: {result.Pages:N0} pages and {result.Glyphs:N0} glyphs, {result.Bytes / (1024.0 * 1024.0):0.0} MB in {result.Files:N0} files, in {stopwatch.Elapsed.TotalSeconds:0.0} s."));
        AnsiConsole.MarkupLine($"Probe results: {Markup.Escape(probeNote)}.");
        AnsiConsole.MarkupLine($"Open {Markup.Escape(Path.Combine(output, ReferenceSite.IndexPage))}.");
        return 0;
    }

    internal static PaletteType? ParsePalette(string? palette) => palette?.Trim().ToLowerInvariant() switch
    {
        "day" => PaletteType.Day,
        "dusk" => PaletteType.Dusk,
        "night" => PaletteType.Night,
        _ => null,
    };

    /// <summary>
    /// Makes <paramref name="output"/> an empty folder: creates it, or empties
    /// an earlier export (marked by <see cref="ReferenceSite.Marker"/>). A
    /// folder holding anything else is left alone.
    /// </summary>
    /// <returns>Why the folder can't be used, or <see langword="null"/>.</returns>
    internal static string? PrepareOutput(string output)
    {
        if (File.Exists(output))
            return $"{output} is a file; --out takes a folder.";
        if (!Directory.Exists(output))
        {
            Directory.CreateDirectory(output);
            return null;
        }

        if (!Directory.EnumerateFileSystemEntries(output).Any())
            return null;
        if (!File.Exists(Path.Combine(output, ReferenceSite.Marker)))
            return $"{output} isn't empty and isn't an earlier export; pass an empty folder.";

        foreach (var dir in Directory.EnumerateDirectories(output))
            Directory.Delete(dir, recursive: true);
        foreach (var file in Directory.EnumerateFiles(output))
            File.Delete(file);
        return null;
    }
}
