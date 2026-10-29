using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Hdf5;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 info &lt;dataset&gt;</c> — reports the detected product specification,
/// edition, whether the dataset can be rendered headlessly, and (for
/// time-series datasets) the available time steps with their indices; for
/// weather files (GRIB2, NetCDF), the parameters and why any isn't used; for
/// CAP warnings, each alert area, when it's in force and the zone codes the
/// marine zone layer doesn't have.
/// </summary>
internal sealed class InfoCommand : Command<DatasetCommandSettings>
{
    public override int Execute(CommandContext context, DatasetCommandSettings settings, CancellationToken cancellationToken)
    {
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        try
        {
            var spec = DatasetPipelineFactory.DetectProductSpec(settings.DatasetPath);
            if (spec is null)
            {
                UnrecognizedDatasetMessage.Write(settings.DatasetPath);
                return 2;
            }

            var processor = DatasetProcessorLoader.Create(factory, spec, settings);
            // A lazily reading processor (GRIB2, NetCDF) keeps its file open.
            using var processorLifetime = processor as IDisposable;

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

            // S-104 defines no portrayal, so say whose symbology render draws (issue #937).
            if (ProductRegistry.Default.PortrayalOf(processor.Spec.Name).AsApplication is { } portrayal)
            {
                table.AddRow("Portrayal", Markup.Escape(portrayal.Note));
            }

            var axisOrder = processor.AxisOrder;
            if (axisOrder is not null)
            {
                var (label, colour) = axisOrder.Outcome switch
                {
                    AxisOrderOutcome.AsEncoded => ("latitude first (verified)", "green"),
                    AxisOrderOutcome.Swapped => axisOrder.Normalized
                        ? ("longitude first — swapped on read", "yellow")
                        : ("longitude first — not swapped", "red"),
                    AxisOrderOutcome.Inconsistent when axisOrder.ExtentMayBeSwapped =>
                        ("disagrees with the envelope — kept as encoded", "yellow"),
                    AxisOrderOutcome.Inconsistent => ("does not fit the declared extent", "red"),
                    _ => ("unverified — latitude first assumed", "grey"),
                };
                table.AddRow("Axis order", $"[{colour}]{Markup.Escape(label)}[/]");
            }

            table.AddRow("Headless render", processor is IHeadlessImageRenderer ? "[green]yes[/]" : "[yellow]no[/]");

            // Weather data (GRIB2, NetCDF, CAP) isn't S-100 and isn't for
            // navigation (design note weather-sources.md §3.1).
            if (!ProductRegistry.Default.IsS100(processor.Spec.Name))
            {
                var notice = ProductRegistry.Default.TryResolve(processor.Spec.Name, out var registration)
                    && registration.PickReportNotice is { Length: > 0 } own
                        ? own
                        : WeatherGridProcessorBase.NotForNavigationNotice;
                table.AddRow("Notice", $"[yellow]{Markup.Escape(notice)}[/]");
            }

            // Weather grids: the model and run, and the grid (issue #1076).
            if (processor is WeatherGridProcessorBase grid)
                AddWeatherGridRows(table, grid);
            if (processor is CapDatasetProcessor capAlerts)
            {
                table.AddRow(
                    "Alerts",
                    $"{capAlerts.Warnings.Document.Alerts.Count} ({capAlerts.Warnings.Areas.Count} areas, {capAlerts.Warnings.Areas.Count(a => a.IsDrawable)} drawn)");
            }

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

            // Likewise for swapped or inconsistent axis order (issue #650).
            if (axisOrder?.IsWarning == true)
            {
                Console.Error.WriteLine(axisOrder.BuildMessage());
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

            // Weather files (GRIB2, NetCDF): what the file holds, and why
            // anything isn't drawn or sampled (issue #1074).
            if (processor is WeatherGridProcessorBase weather && weather.Contents.Count > 0)
            {
                var contentsTable = new Table().Border(TableBorder.Rounded).Title("Weather parameters");
                contentsTable.AddColumn("Parameter");
                contentsTable.AddColumn("Level");
                contentsTable.AddColumn("Steps");
                contentsTable.AddColumn("Used");
                foreach (var entry in weather.Contents)
                {
                    contentsTable.AddRow(
                        Markup.Escape(entry.Name),
                        Markup.Escape(entry.Level),
                        entry.Steps.ToString(),
                        entry.IsUsed ? "[green]yes[/]" : $"[yellow]no[/]: {Markup.Escape(entry.Reason!)}");
                }

                AnsiConsole.Write(contentsTable);
            }

            // CAP warnings: each alert area, when it's in force, and the zone
            // codes the marine zone layer doesn't have (issue #1069).
            if (processor is CapDatasetProcessor cap)
                WriteCapWarnings(cap);

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

    private static void AddWeatherGridRows(Table table, WeatherGridProcessorBase weather)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        table.AddRow("Model", Markup.Escape(string.IsNullOrWhiteSpace(weather.ForecastRun.Model) ? "(not given)" : weather.ForecastRun.Model));
        table.AddRow(
            "Run (UTC)",
            weather.ReferenceTime is { } run
                ? run.ToString("yyyy-MM-dd HH:mm", invariant)
                : "none: the steps are analyses");
        var g = weather.GridSet.Grid;
        table.AddRow(
            "Grid",
            Markup.Escape(string.Create(
                invariant,
                $"regular latitude/longitude, {g.Columns} × {g.Rows} nodes, {g.SpacingLongitudinal:0.###}° × {g.SpacingLatitudinal:0.###}°")));
        if (weather.Metadata.Extent is { } e)
        {
            table.AddRow(
                "Extent",
                Markup.Escape(string.Create(
                    invariant,
                    $"{e.WestLongitude:0.###},{e.SouthLatitude:0.###},{e.EastLongitude:0.###},{e.NorthLatitude:0.###} (minLon,minLat,maxLon,maxLat)")));
        }

        table.AddRow("Layers", Markup.Escape(string.Join(", ", weather.LayerKeys)));
    }

    private static void WriteCapWarnings(CapDatasetProcessor cap)
    {
        static string Time(DateTimeOffset? time) =>
            time is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "—";

        var table = new Table().Border(TableBorder.Rounded).Title("Warnings");
        table.AddColumn("Area");
        table.AddColumn("Event");
        table.AddColumn("Severity");
        table.AddColumn("From (UTC)");
        table.AddColumn("To (UTC)");
        table.AddColumn("Shape");
        foreach (var area in cap.Warnings.Areas)
        {
            var shape = area.ShapeSource switch
            {
                Datasets.Cap.CapShapeSource.Zones => $"zones {string.Join(", ", area.Zones.Select(z => z.Code))}",
                Datasets.Cap.CapShapeSource.None => "none",
                var source => source.ToString().ToLowerInvariant(),
            };
            if (area.UnmatchedZoneCodes.Count > 0)
                shape += $"; not found: {string.Join(", ", area.UnmatchedZoneCodes)}";
            var to = Time(area.Validity.End);
            if (area.SupersededAt is { } superseded)
                to += $" (superseded {Time(superseded)})";
            if (!area.IsDrawable)
                to += $" ({area.Alert.MessageType}, not drawn)";

            table.AddRow(
                Markup.Escape(area.Id),
                Markup.Escape(area.Info.Event),
                Markup.Escape(area.Info.Severity.ToString()),
                Markup.Escape(Time(area.Validity.Start)),
                Markup.Escape(to),
                area.UnmatchedZoneCodes.Count > 0 ? $"[yellow]{Markup.Escape(shape)}[/]" : Markup.Escape(shape));
        }

        AnsiConsole.Write(table);
        if (cap.Warnings.UnmatchedZoneCodes.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]Zone codes not in the marine zone layer:[/] {string.Join(", ", cap.Warnings.UnmatchedZoneCodes)}");
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
    private static string DisplayModeCliToken(string displayModeId) =>
        S411DisplayModes.ToCliToken(displayModeId);
}
