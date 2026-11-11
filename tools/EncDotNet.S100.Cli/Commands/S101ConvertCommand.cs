using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;
using EncDotNet.S57.ExchangeSets;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 s101 convert --to s57 -o &lt;output&gt; &lt;source&gt;</c> — converts an
/// S-101 base dataset to an S-57 base cell by translating it with
/// <see cref="S101ToS57Translator"/> (S-65 Annex C) and encoding the result with
/// <see cref="S57DocumentWriter"/> (ISO/IEC 8211; S-57 Edition 3.1).
/// </summary>
/// <remarks>
/// <para>
/// The reverse of <see cref="S57ConvertCommand"/>, and like it a thin driver
/// over the translation and encoding libraries: conversion semantics are owned
/// by <see cref="S101ToS57Translator"/>. The output is for testing and for tools
/// that read only S-57; it is not a production ENC (no S-58 validation, and
/// Annex C expects producers to finish the cell by hand).
/// </para>
/// <para>
/// Sibling update datasets (<c>.001</c>, <c>.002</c>, …) next to the base
/// dataset are folded in before translation unless <c>--no-updates</c> is given.
/// With <c>--exchange-set</c> the cell is written under <c>ENC_ROOT</c> with a
/// <c>CATALOG.031</c> written by <see cref="S57CatalogWriter"/>.
/// </para>
/// </remarks>
internal sealed class S101ConvertCommand : Command<S101ConvertCommandSettings>
{
    private const string Volume = "V01X01";

    public override int Execute(CommandContext context, S101ConvertCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var updates = settings.NoUpdates
                ? Array.Empty<string>()
                : S101FilesystemUpdateDiscovery.FindSequentialUpdates(settings.SourcePath);

            var dataset = updates.Count == 0
                ? S101Dataset.Open(settings.SourcePath)
                : S101Dataset.OpenWithUpdates(settings.SourcePath, updates);

            if (updates.Count > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[grey]Applied {updates.Count} sibling update(s) ({string.Join(", ", updates.Select(Path.GetFileName))}).[/]");
            }

            WarnOnEdition(dataset.Document.Identification.ProductSpecificationEdition);

            var options = settings.ToTranslationOptions();
            var diagnostics = new S101ToS57TranslationDiagnostics();
            var document = new S101ToS57Translator(options).Translate(dataset.Document, diagnostics);
            var bytes = S57DocumentWriter.Write(document);

            string cellPath;
            string? catalogPath = null;
            if (settings.ExchangeSet)
            {
                var cellName = string.IsNullOrWhiteSpace(settings.CellName)
                    ? Path.GetFileNameWithoutExtension(settings.SourcePath)
                    : settings.CellName;
                (cellPath, catalogPath) = WriteExchangeSet(settings.OutputPath, cellName, bytes, document);
                WarnOnCellName(cellName);
            }
            else
            {
                cellPath = settings.OutputPath;
                File.WriteAllBytes(cellPath, bytes);
            }

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Converted[/] {settings.SourcePath} [green]→[/] {cellPath} as S-57 3.1 ({document.FeatureRecords.Count} feature records).");
            if (catalogPath is not null)
                AnsiConsole.MarkupLineInterpolated($"[grey]Exchange set catalogue written to[/] {catalogPath}.");

            PrintDiagnosticsSummary(diagnostics);

            if (!string.IsNullOrWhiteSpace(settings.ReportPath))
            {
                WriteReport(settings.ReportPath, settings.SourcePath, cellPath, catalogPath, updates, options, diagnostics);
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

    /// <summary>
    /// Writes <c>ENC_ROOT/&lt;cell&gt;/&lt;cell&gt;.000</c> and
    /// <c>ENC_ROOT/CATALOG.031</c> under <paramref name="root"/>. The catalogue
    /// lists itself and the cell, with the cell's extent and CRC (S-57 Part 3
    /// §7.8.1; the ENC product specification §5.6).
    /// </summary>
    private static (string CellPath, string CatalogPath) WriteExchangeSet(
        string root, string cellName, byte[] cell, S57Document document)
    {
        var encRoot = Path.Combine(root, "ENC_ROOT");
        var cellDir = Path.Combine(encRoot, cellName);
        Directory.CreateDirectory(cellDir);

        var cellFile = cellName + ".000";
        var cellPath = Path.Combine(cellDir, cellFile);
        File.WriteAllBytes(cellPath, cell);

        var extent = S57Dataset.ReadMetadata(document).Extent;
        var catalog = new S57Catalog
        {
            Entries =
            [
                new S57CatalogEntry
                {
                    RecordName = "CD",
                    RecordId = 1,
                    FileName = "CATALOG.031",
                    Volume = Volume,
                    Implementation = "ASC",
                },
                new S57CatalogEntry
                {
                    RecordName = "CD",
                    RecordId = 2,
                    // CATD file names use '\' whatever the host (S-57 Part 3 §7.8.1).
                    FileName = $"{cellName}\\{cellFile}",
                    Volume = Volume,
                    Implementation = "BIN",
                    SouthernmostLatitude = extent?.SouthLatitude,
                    WesternmostLongitude = extent?.WestLongitude,
                    NorthernmostLatitude = extent?.NorthLatitude,
                    EasternmostLongitude = extent?.EastLongitude,
                    CrcChecksum = S57CatalogWriter.ComputeCrc(cell),
                },
            ],
        };

        var catalogPath = Path.Combine(encRoot, "CATALOG.031");
        S57CatalogWriter.WriteToFile(catalogPath, catalog);
        return (cellPath, catalogPath);
    }

    // Annex C Ed 1.0.0 is written against S-101 Edition 2.0.0. Earlier
    // editions name some classes differently (BuoyLateral, not LateralBuoy),
    // and those have no rule.
    private static void WarnOnEdition(string edition)
    {
        if (edition.StartsWith("2.", StringComparison.Ordinal))
            return;

        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]Warning:[/] the dataset declares S-101 Edition {(edition.Length == 0 ? "(none)" : edition)}; S-65 Annex C converts Edition 2.0.0. Feature classes and attributes renamed since are reported as unmapped and not converted.");
    }

    private static void WarnOnCellName(string cellName)
    {
        if (cellName.Length == 8 && cellName.All(char.IsAsciiLetterOrDigit))
            return;

        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]Warning:[/] '{cellName}' is not an 8-character S-57 cell name (such as XX5CELL1); some S-57 tools may reject it. Pass --cell-name to choose one.");
    }

    private static void PrintDiagnosticsSummary(S101ToS57TranslationDiagnostics diagnostics)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Coverage");
        table.AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("S-101 features read", diagnostics.FeaturesRead.ToString());
        table.AddRow("S-57 feature records written", diagnostics.FeaturesWritten.ToString());

        AddCounts("Objects synthesised", diagnostics.FeaturesSynthesised.Values);
        AddCounts("Features restructured", diagnostics.Restructured.Values);
        AddCounts("Primitives changed (Table 2-1)", diagnostics.PrimitivesChanged.Values);
        AddCounts("Skin-of-the-earth cuts", diagnostics.SkinOfTheEarthCuts.Values);
        AddCounts("Text folded into INFORM", diagnostics.FoldedIntoInform.Values);
        AddCounts("Moved to national attributes", diagnostics.MovedToNational.Values);
        AddCounts("Lists cut to their first value", diagnostics.TruncatedToFirst.Values);
        AddCounts("Mandatory attributes left empty", diagnostics.MandatoryAttributesNulled.Values);
        if (diagnostics.InformOverLength > 0)
            table.AddRow("INFORM over the length limit", diagnostics.InformOverLength.ToString());
        if (diagnostics.NonBooleanValues > 0)
            table.AddRow("Non-Boolean values read as false", diagnostics.NonBooleanValues.ToString());

        table.AddRow(
            "[yellow]Features dropped by Annex C[/]",
            Distinct(diagnostics.DroppedFeatures.Count, Total(diagnostics.DroppedFeatures.Values)));
        table.AddRow(
            "[yellow]Unmapped feature classes[/]",
            Distinct(diagnostics.UnmappedFeatureClasses.Count, Total(diagnostics.UnmappedFeatureClasses.Values)));
        table.AddRow(
            "[yellow]Attributes dropped by Annex C[/]",
            Distinct(diagnostics.DroppedAttributes.Count, Total(diagnostics.DroppedAttributes.Values)));
        table.AddRow(
            "[yellow]Unmapped attributes[/]",
            Distinct(diagnostics.UnmappedAttributes.Count, Total(diagnostics.UnmappedAttributes.Values)));
        table.AddRow(
            "[yellow]Information types dropped[/]",
            Distinct(diagnostics.DroppedInformationTypes.Count, Total(diagnostics.DroppedInformationTypes.Values)));
        table.AddRow(
            "[yellow]Unmapped information types[/]",
            Distinct(diagnostics.UnmappedInformationTypes.Count, Total(diagnostics.UnmappedInformationTypes.Values)));
        table.AddRow(
            "[yellow]Unencodable text dropped[/]",
            Distinct(diagnostics.UnencodableText.Count, Total(diagnostics.UnencodableText.Values)));

        AnsiConsole.Write(table);

        void AddCounts(string label, IEnumerable<int> values)
        {
            var total = Total(values);
            if (total > 0)
                table.AddRow(label, total.ToString());
        }

        static int Total(IEnumerable<int> values) => values.Sum();

        static string Distinct(int distinct, int total) =>
            distinct == 0 ? "0" : $"{total} ({distinct} distinct)";
    }

    private static void WriteReport(
        string reportPath,
        string sourcePath,
        string outputPath,
        string? catalogPath,
        IReadOnlyList<string> updates,
        S101ToS57TranslationOptions options,
        S101ToS57TranslationDiagnostics diagnostics)
    {
        var report = new JsonObject
        {
            ["source"] = sourcePath,
            ["output"] = outputPath,
            ["catalog"] = catalogPath,
            ["product"] = "S-57",
            ["productEdition"] = "3.1",
            ["updatesApplied"] = new JsonArray(updates.Select(u => (JsonNode?)Path.GetFileName(u)).ToArray()),
            ["options"] = new JsonObject
            {
                ["informSeparator"] = options.InformSeparator,
                ["maxInformLength"] = options.MaxInformLength,
                ["informLengthPolicy"] = options.InformLengthPolicy.ToString(),
                ["nationalLanguage"] = options.NationalLanguage,
                ["nameWithoutUsage"] = options.NameWithoutUsage.ToString(),
                ["includeOptionalInform"] = options.IncludeOptionalInform,
            },
            ["featuresRead"] = diagnostics.FeaturesRead,
            ["featuresWritten"] = diagnostics.FeaturesWritten,
            ["informOverLength"] = diagnostics.InformOverLength,
            ["nonBooleanValues"] = diagnostics.NonBooleanValues,
            ["droppedFeatures"] = Clauses(diagnostics.DroppedFeatures.Select(p => (p.Key.S101Code, p.Key.Clause, p.Value))),
            ["unmappedFeatureClasses"] = Counts(diagnostics.UnmappedFeatureClasses.Select(p => (p.Key, p.Value))),
            ["droppedAttributes"] = Clauses(diagnostics.DroppedAttributes.Select(p => (p.Key.S101Code, p.Key.Clause, p.Value))),
            ["unmappedAttributes"] = new JsonArray(diagnostics.UnmappedAttributes
                .OrderByDescending(p => p.Value)
                .Select(p => (JsonNode?)new JsonObject
                {
                    ["featureClass"] = p.Key.S101FeatureCode,
                    ["attribute"] = p.Key.S101AttributeCode,
                    ["count"] = p.Value,
                })
                .ToArray()),
            ["droppedInformationTypes"] = Clauses(diagnostics.DroppedInformationTypes.Select(p => (p.Key.S101Code, p.Key.Clause, p.Value))),
            ["unmappedInformationTypes"] = Counts(diagnostics.UnmappedInformationTypes.Select(p => (p.Key, p.Value))),
            ["restructured"] = Clauses(diagnostics.Restructured.Select(p => (p.Key.S101Code, p.Key.Clause, p.Value))),
            ["foldedIntoInform"] = Counts(diagnostics.FoldedIntoInform.Select(p => (p.Key, p.Value))),
            ["truncatedToFirst"] = Counts(diagnostics.TruncatedToFirst.Select(p => (p.Key, p.Value))),
            ["mandatoryAttributesNulled"] = new JsonArray(diagnostics.MandatoryAttributesNulled
                .OrderByDescending(p => p.Value)
                .Select(p => (JsonNode?)new JsonObject
                {
                    ["objectClass"] = p.Key.ObjectClass,
                    ["attribute"] = p.Key.Attribute,
                    ["count"] = p.Value,
                })
                .ToArray()),
            ["featuresSynthesised"] = Counts(diagnostics.FeaturesSynthesised.Select(p => (p.Key, p.Value))),
            ["primitivesChanged"] = Counts(diagnostics.PrimitivesChanged.Select(p => (p.Key, p.Value))),
            ["skinOfTheEarthCuts"] = Counts(diagnostics.SkinOfTheEarthCuts.Select(p => (p.Key, p.Value))),
            ["movedToNational"] = Counts(diagnostics.MovedToNational.Select(p => (p.Key, p.Value))),
            ["unencodableText"] = Counts(diagnostics.UnencodableText.Select(p => (p.Key, p.Value))),
        };

        var json = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
    }

    /// <summary>Counts keyed by name, most frequent first.</summary>
    private static JsonObject Counts(IEnumerable<(string Key, int Count)> counts)
    {
        var json = new JsonObject();
        foreach (var (key, count) in counts.OrderByDescending(c => c.Count))
            json[key] = count;
        return json;
    }

    /// <summary>Counts by S-101 code with the Annex C clause, most frequent first.</summary>
    private static JsonArray Clauses(IEnumerable<(string Code, string Clause, int Count)> counts) =>
        new(counts
            .OrderByDescending(c => c.Count)
            .Select(c => (JsonNode?)new JsonObject
            {
                ["code"] = c.Code,
                ["clause"] = c.Clause,
                ["count"] = c.Count,
            })
            .ToArray());
}
