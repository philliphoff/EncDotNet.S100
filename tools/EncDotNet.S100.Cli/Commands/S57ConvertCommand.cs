using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 s57 convert -o &lt;output&gt; &lt;source&gt;</c> — converts an S-57 base
/// cell to an S-101 dataset (or, for an inland ENC, an S-401 dataset) by
/// translating it with <see cref="S57ToS101Translator"/> and encoding the result
/// with <see cref="S101DocumentWriter"/> (ISO/IEC 8211; S-100 Part 10a).
/// </summary>
/// <remarks>
/// <para>
/// This command is a thin driver over the existing translation and encoding
/// libraries; it deliberately does not alter conversion semantics, which are
/// owned by <see cref="S57ToS101Translator"/>.
/// </para>
/// <para>
/// Sibling sequential update files (<c>.001</c>, <c>.002</c>, …) that sit next
/// to the base cell are auto-discovered and folded in via
/// <see cref="S57Dataset.Open(Stream, IReadOnlyList{Stream})"/> before
/// translation, so a converted cell reflects its up-to-date state (S-57 Part 3
/// dataset updating). Pass <c>--no-updates</c> to convert the bare base cell.
/// </para>
/// <para>
/// The product written follows <see cref="S57Dataset.TranslationTarget"/> of the
/// update-folded cell: S-401 for an inland ENC (DSID <c>PRSP</c> = 10), S-101
/// otherwise (issue #608). <c>--target s101|s401</c> overrides that choice.
/// </para>
/// </remarks>
internal sealed class S57ConvertCommand : Command<S57ConvertCommandSettings>
{
    public override int Execute(CommandContext context, S57ConvertCommandSettings settings)
    {
        try
        {
            var updates = settings.NoUpdates
                ? Array.Empty<string>()
                : S101FilesystemUpdateDiscovery.FindSequentialUpdates(settings.SourcePath);

            var dataset = OpenDataset(settings.SourcePath, updates);

            if (updates.Count > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[grey]Applied {updates.Count} sibling update(s) ({string.Join(", ", updates.Select(Path.GetFileName))}).[/]");
            }

            // Chosen from the update-folded document: an update could in principle
            // change the DSID product specification.
            var detected = dataset.TranslationTarget;
            S57ConvertCommandSettings.TryParseTarget(settings.Target, out var forced);
            var target = forced ?? detected;
            WarnOnTargetOverride(target, detected);

            var translator = S57ToS101Translator.ForTarget(target);
            var diagnostics = new S57TranslationDiagnostics();
            var document = translator.Translate(dataset, diagnostics);

            S101DocumentWriter.WriteToFile(settings.OutputPath, document);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Converted[/] {settings.SourcePath} [green]→[/] {settings.OutputPath} as {target.Spec} {target.Edition} ({document.Features.Count} features).");

            PrintDiagnosticsSummary(diagnostics);

            if (!string.IsNullOrWhiteSpace(settings.ReportPath))
            {
                WriteReport(settings.ReportPath, settings.SourcePath, settings.OutputPath, updates, target, detected, diagnostics);
                AnsiConsole.MarkupLineInterpolated($"[grey]Diagnostics report written to[/] {settings.ReportPath}.");
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
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static S57Dataset OpenDataset(string sourcePath, IReadOnlyList<string> updates)
    {
        if (updates.Count == 0)
            return S57Dataset.Open(sourcePath);

        var updateStreams = new List<Stream>(updates.Count);
        var baseStream = File.OpenRead(sourcePath);
        try
        {
            foreach (var updatePath in updates)
                updateStreams.Add(File.OpenRead(updatePath));

            return S57Dataset.Open(baseStream, updateStreams);
        }
        finally
        {
            baseStream.Dispose();
            foreach (var stream in updateStreams)
                stream.Dispose();
        }
    }

    private static void WarnOnTargetOverride(S57TranslationTarget target, S57TranslationTarget detected)
    {
        if (target == detected)
            return;

        if (target == S57TranslationTarget.S401)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]Warning:[/] the cell does not declare the inland ENC product specification (DSID PRSP = 10); writing {target.Spec} as requested by --target. Maritime content without an S-401 equivalent is dropped.");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]The cell is an inland ENC (DSID PRSP = 10); writing {target.Spec} as requested by --target. Inland object classes and attributes without an S-101 equivalent are dropped.[/]");
        }
    }

    private static void PrintDiagnosticsSummary(S57TranslationDiagnostics diagnostics)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Coverage");
        table.AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("Feature records read", diagnostics.FeatureRecordsRead.ToString());
        table.AddRow("Features emitted", diagnostics.FeaturesEmitted.ToString());
        table.AddRow("Soundings read / emitted",
            $"{diagnostics.SoundingFeaturesRead} / {diagnostics.SoundingFeaturesEmitted}");
        table.AddRow("Sounding points emitted", diagnostics.SoundingPointsEmitted.ToString());

        if (diagnostics.SectorLightsMerged > 0)
            table.AddRow("Sector lights merged", diagnostics.SectorLightsMerged.ToString());
        if (diagnostics.TopmarksAbsorbed > 0)
            table.AddRow("Topmarks absorbed", diagnostics.TopmarksAbsorbed.ToString());
        if (diagnostics.NauticalInformationTypesEmitted > 0)
            table.AddRow("NauticalInformation records", diagnostics.NauticalInformationTypesEmitted.ToString());
        if (diagnostics.RangeSystemsEmitted > 0)
            table.AddRow("RangeSystem collections", diagnostics.RangeSystemsEmitted.ToString());
        if (diagnostics.BridgeSpansEmitted > 0)
            table.AddRow("Bridge spans", diagnostics.BridgeSpansEmitted.ToString());
        if (diagnostics.BridgeAggregationsEmitted > 0)
            table.AddRow("Aggregated bridges", diagnostics.BridgeAggregationsEmitted.ToString());

        table.AddRow(
            "[yellow]Unmapped object classes[/]",
            Distinct(diagnostics.UnmappedObjectClasses.Count, Total(diagnostics.UnmappedObjectClasses.Values)));
        table.AddRow(
            "[yellow]Rule-dropped object classes[/]",
            Distinct(diagnostics.RuleDroppedObjectClasses.Count, Total(diagnostics.RuleDroppedObjectClasses.Values)));
        table.AddRow(
            "[yellow]Unmapped attributes[/]",
            Distinct(diagnostics.UnmappedAttributes.Count, Total(diagnostics.UnmappedAttributes.Values)));
        table.AddRow(
            "[yellow]Rule-dropped attributes[/]",
            Distinct(diagnostics.RuleDroppedAttributes.Count, Total(diagnostics.RuleDroppedAttributes.Values)));
        table.AddRow(
            "[yellow]FC-rejected enum values[/]",
            Distinct(diagnostics.DroppedEnumValues.Count, Total(diagnostics.DroppedEnumValues.Values)));
        table.AddRow(
            "[yellow]Features dropped (no geometry)[/]",
            Distinct(diagnostics.FeaturesDroppedForNoGeometry.Count, Total(diagnostics.FeaturesDroppedForNoGeometry.Values)));

        AnsiConsole.Write(table);

        static int Total(IEnumerable<int> values) => values.Sum();

        static string Distinct(int distinct, int total) =>
            distinct == 0 ? "0" : $"{total} ({distinct} distinct)";
    }

    private static void WriteReport(
        string reportPath,
        string sourcePath,
        string outputPath,
        IReadOnlyList<string> updates,
        S57TranslationTarget target,
        S57TranslationTarget detected,
        S57TranslationDiagnostics diagnostics)
    {
        var report = new
        {
            source = sourcePath,
            output = outputPath,
            product = target.Spec,
            productEdition = target.Edition,
            detectedProduct = detected.Spec,
            updatesApplied = updates.Select(Path.GetFileName).ToArray(),
            featureRecordsRead = diagnostics.FeatureRecordsRead,
            featuresEmitted = diagnostics.FeaturesEmitted,
            soundingFeaturesRead = diagnostics.SoundingFeaturesRead,
            soundingFeaturesEmitted = diagnostics.SoundingFeaturesEmitted,
            soundingPointsEmitted = diagnostics.SoundingPointsEmitted,
            soundingFeaturesWithoutPoints = diagnostics.SoundingFeaturesWithoutPoints,
            sectorLightsMerged = diagnostics.SectorLightsMerged,
            topmarksAbsorbed = diagnostics.TopmarksAbsorbed,
            nauticalInformationTypesEmitted = diagnostics.NauticalInformationTypesEmitted,
            rangeSystemsEmitted = diagnostics.RangeSystemsEmitted,
            bridgeSpansEmitted = diagnostics.BridgeSpansEmitted,
            bridgeAggregationsEmitted = diagnostics.BridgeAggregationsEmitted,
            unmappedObjectClasses = diagnostics.UnmappedObjectClasses
                .OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key.ToString(), p => p.Value),
            ruleDroppedObjectClasses = diagnostics.RuleDroppedObjectClasses
                .OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key.ToString(), p => p.Value),
            unmappedAttributes = diagnostics.UnmappedAttributes
                .OrderByDescending(p => p.Value)
                .Select(p => new { objectClass = p.Key.ObjectClass, attribute = p.Key.AttributeCode, count = p.Value })
                .ToArray(),
            ruleDroppedAttributes = diagnostics.RuleDroppedAttributes
                .OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key.ToString(), p => p.Value),
            droppedEnumValues = diagnostics.DroppedEnumValues
                .OrderByDescending(p => p.Value)
                .Select(p => new { attribute = p.Key.S101Attribute, value = p.Key.Value, count = p.Value })
                .ToArray(),
            featuresDroppedForNoGeometry = diagnostics.FeaturesDroppedForNoGeometry
                .OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key, p => p.Value),
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
    }
}
