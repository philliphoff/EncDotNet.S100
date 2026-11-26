using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Nis;
using EncDotNet.S100.Datasets.Nis.FileGdb;
using EncDotNet.S57;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 nis convert --to s57 -o &lt;output&gt; &lt;source.gdb&gt;</c> — converts
/// one cell of an Esri ArcGIS Maritime S-57 Nautical Information System (NIS),
/// stored in a file geodatabase, to an S-57 base cell.
/// </summary>
/// <remarks>
/// A thin driver: <see cref="FileGdbNisSource"/> reads the geodatabase into the
/// source-neutral NIS model, <see cref="NisToS57Translator"/> turns the model
/// into an S-57 document, and <see cref="S57DocumentWriter"/> encodes it. The
/// output is for testing, previewing and tools that read S-57; it is not a
/// production ENC.
/// </remarks>
internal sealed class NisConvertCommand : Command<NisConvertCommandSettings>
{
    public override int Execute(CommandContext context, NisConvertCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var source = FileGdbNisSource.Open(settings.SourcePath);
            var names = source.GetDatasetNames();

            if (settings.List)
            {
                PrintCells(source);
                return 0;
            }

            if (!TryChooseCell(settings.Cell, names, out var datasetName, out var error))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {error}");
                return 1;
            }

            var options = settings.ToOptions(datasetName);
            var diagnostics = new NisToS57Diagnostics();
            var document = new NisToS57Translator(options).Translate(source, diagnostics);
            var bytes = S57DocumentWriter.Write(document);
            var cellName = Path.GetFileNameWithoutExtension(document.DataSetIdentification!.DataSetName);

            string cellPath;
            string? catalogPath = null;
            if (settings.ExchangeSet)
            {
                (cellPath, catalogPath) = S57ExchangeSetOutput.Write(settings.OutputPath, cellName, bytes, document);
            }
            else
            {
                cellPath = settings.OutputPath;
                File.WriteAllBytes(cellPath, bytes);
            }
            S57ExchangeSetOutput.WarnOnCellName(cellName);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Converted[/] {datasetName ?? "the NIS"} in {settings.SourcePath} [green]→[/] {cellPath} as S-57 3.1 ({document.FeatureRecords.Count} feature records).");
            if (catalogPath is not null)
                AnsiConsole.MarkupLineInterpolated($"[grey]Exchange set catalogue written to[/] {catalogPath}.");

            PrintDiagnosticsSummary(diagnostics);

            if (!string.IsNullOrWhiteSpace(settings.ReportPath))
            {
                WriteReport(settings.ReportPath, settings.SourcePath, datasetName, cellPath, catalogPath, document, diagnostics);
                AnsiConsole.MarkupLineInterpolated($"[grey]Diagnostics report written to[/] {settings.ReportPath}.");
            }
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    /// <summary>
    /// The source cell a <c>--cell</c> value names, by its full name or its
    /// name without extension; with no value, the only cell there is.
    /// </summary>
    internal static bool TryChooseCell(string? cell, IReadOnlyList<string> names, out string? datasetName, out string? error)
    {
        datasetName = null;
        error = null;
        if (string.IsNullOrWhiteSpace(cell))
        {
            if (names.Count <= 1)
            {
                datasetName = names.Count == 1 ? names[0] : null;
                return true;
            }
            error = $"The NIS holds {names.Count} cells ({string.Join(", ", names)}). Choose one with --cell.";
            return false;
        }

        var matches = names
            .Where(n => string.Equals(n, cell, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NisToS57Translator.CellNameOf(n), cell, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            datasetName = matches[0];
            return true;
        }
        error = matches.Count == 0
            ? $"The NIS holds no cell named {cell}. It holds: {string.Join(", ", names)}."
            : $"{cell} names more than one cell ({string.Join(", ", matches)}). Give the full name.";
        return false;
    }

    private static void PrintCells(FileGdbNisSource source)
    {
        var counts = source.GetFeatures(NisQuery.All)
            .GroupBy(f => f.DatasetName ?? "(none)", StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Cell (DSNM)");
        table.AddColumn(new TableColumn("Features").RightAligned());
        foreach (var group in counts)
            table.AddRow(Markup.Escape(group.Key), group.Count().ToString());
        AnsiConsole.Write(table);
    }

    private static void PrintDiagnosticsSummary(NisToS57Diagnostics diagnostics)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Conversion");
        table.AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("NIS features read", diagnostics.FeaturesRead.ToString());
        table.AddRow("S-57 feature records written", diagnostics.FeaturesWritten.ToString());
        table.AddRow("Soundings (SOUNDG objects)", $"{diagnostics.SoundingsWritten} ({diagnostics.SoundingObjects})");
        table.AddRow("Vector records written", diagnostics.VectorRecordsWritten.ToString());
        table.AddRow("Feature pointers written", diagnostics.RelationshipsWritten.ToString());
        table.AddRow("Unknown values (-32767) written empty", diagnostics.UnknownValues.ToString());
        if (diagnostics.CoverageSynthesised)
            table.AddRow("M_COVR added", "yes");
        if (diagnostics.IdentitiesAssigned > 0)
            table.AddRow("[yellow]Identities assigned[/]", diagnostics.IdentitiesAssigned.ToString());
        if (diagnostics.RelationshipsUnresolved > 0)
            table.AddRow("[yellow]Relationships unresolved[/]", diagnostics.RelationshipsUnresolved.ToString());
        if (diagnostics.EmptyGeometries > 0)
            table.AddRow("[yellow]Empty geometries dropped[/]", diagnostics.EmptyGeometries.ToString());
        table.AddRow("[yellow]Object classes dropped[/]", Distinct(diagnostics.DroppedObjectClasses));
        table.AddRow("[yellow]Unknown attributes[/]", Distinct(diagnostics.UnknownAttributes));
        table.AddRow("[yellow]Unknown agencies[/]", Distinct(diagnostics.UnknownAgencies));
        if (diagnostics.UnencodableText > 0)
            table.AddRow("[yellow]Unencodable text dropped[/]", diagnostics.UnencodableText.ToString());
        AnsiConsole.Write(table);

        static string Distinct(IReadOnlyDictionary<string, int> counts)
            => counts.Count == 0 ? "0" : $"{counts.Values.Sum()} ({counts.Count} distinct)";
    }

    private static void WriteReport(
        string reportPath,
        string sourcePath,
        string? datasetName,
        string outputPath,
        string? catalogPath,
        S57Document document,
        NisToS57Diagnostics diagnostics)
    {
        var report = new JsonObject
        {
            ["source"] = sourcePath,
            ["sourceCell"] = datasetName,
            ["output"] = outputPath,
            ["catalog"] = catalogPath,
            ["cellName"] = document.DataSetIdentification?.DataSetName,
            ["product"] = "S-57",
            ["productEdition"] = "3.1",
            ["featuresRead"] = diagnostics.FeaturesRead,
            ["featuresWritten"] = diagnostics.FeaturesWritten,
            ["soundingsWritten"] = diagnostics.SoundingsWritten,
            ["soundingObjects"] = diagnostics.SoundingObjects,
            ["vectorRecordsWritten"] = diagnostics.VectorRecordsWritten,
            ["relationshipsWritten"] = diagnostics.RelationshipsWritten,
            ["relationshipsUnresolved"] = diagnostics.RelationshipsUnresolved,
            ["unknownValues"] = diagnostics.UnknownValues,
            ["identitiesAssigned"] = diagnostics.IdentitiesAssigned,
            ["emptyGeometries"] = diagnostics.EmptyGeometries,
            ["coverageSynthesised"] = diagnostics.CoverageSynthesised,
            ["unencodableText"] = diagnostics.UnencodableText,
            ["droppedObjectClasses"] = Counts(diagnostics.DroppedObjectClasses),
            ["unknownAttributes"] = Counts(diagnostics.UnknownAttributes),
            ["unknownAgencies"] = Counts(diagnostics.UnknownAgencies),
        };
        File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        static JsonObject Counts(IReadOnlyDictionary<string, int> counts)
        {
            var json = new JsonObject();
            foreach (var (key, value) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                json[key] = value;
            return json;
        }
    }
}
