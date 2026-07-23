using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 identify</c> performs a headless ECDIS-style "pick": it identifies
/// the vector features at a geographic point and samples the coverage
/// products at that point, across one or more dataset layers — the same
/// interaction the viewer offers on a cursor click, but without an open
/// viewer or MCP server.
/// </summary>
/// <remarks>
/// <para>
/// The command builds a headless <see cref="FileDatasetCatalog"/> from the
/// resolved layers and drives the shared pick services
/// (<see cref="IdentifyFeaturesService"/> for vector features and
/// <see cref="SampleCoverageService"/> for S-102 / S-104 / S-111 coverages),
/// so its output matches the MCP <c>identify_features</c> / <c>sample_coverage</c>
/// tools exactly. Feature ranking follows ECDIS draw order (point before curve
/// before area); see S-100 Edition 5.2.1 Part 9.
/// </para>
/// <para>
/// Three input grammars mirror <c>s100 render</c>: a single positional dataset,
/// repeated <c>--layer</c> options, or an exchange set (positional directory /
/// <c>CATALOG.XML</c> / <c>.zip</c>, or <c>--from</c>). Datasets whose product
/// specification is unsupported, whose file is missing, or that fail to parse
/// are skipped with a warning on stderr rather than failing the whole pick.
/// </para>
/// </remarks>
internal sealed class IdentifyCommand : Command<IdentifyCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Single-dataset form: the dataset to pick. Exchange-set form: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one.")]
        public string? Input { get; init; }

        [CommandOption("--lat <LATITUDE>")]
        [Description("Pick latitude in decimal degrees, WGS-84 (EPSG:4326). Range -90..90. Required.")]
        public string? Latitude { get; init; }

        [CommandOption("--lon <LONGITUDE>")]
        [Description("Pick longitude in decimal degrees, WGS-84 (EPSG:4326). Range -180..180. Required.")]
        public string? Longitude { get; init; }

        [CommandOption("--layer <PATH>")]
        [Description("Add a dataset as a pick layer (repeatable). Mutually exclusive with the exchange-set form.")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--from|--exchange-set <PATH>")]
        [Description("Pick across every discoverable dataset in an exchange set: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one. Mutually exclusive with --layer.")]
        public string? ExchangeSet { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange-set form only: restrict loading to a comma-separated list of product specifications (e.g. --only S101,S102; hyphenation and case are ignored).")]
        public string? Only { get; init; }

        [CommandOption("--radius <METERS>")]
        [Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")]
        [DefaultValue(50.0)]
        public double RadiusMeters { get; init; } = 50.0;

        [CommandOption("--spec <SPEC>")]
        [Description("Restrict the pick to a single product specification (e.g. --spec S-124; hyphenation and case are ignored).")]
        public string? Spec { get; init; }

        [CommandOption("--time <ISO8601>")]
        [Description("UTC ISO-8601 instant selecting a time step for time-varying coverages (S-104, S-111); ignored for S-102. When omitted the first available step is used.")]
        public string? Time { get; init; }

        [CommandOption("--max-results <N>")]
        [Description("Maximum ranked feature matches to return; clamped to [1, 200]. Default 20.")]
        [DefaultValue(20)]
        public int MaxResults { get; init; } = 20;

        [CommandOption("--attributes")]
        [Description("Include each identified feature's full attribute payload (via describe_feature). Vector specs without a describer are reported without attributes.")]
        [DefaultValue(false)]
        public bool Attributes { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: 'table' (default, human-readable) or 'json'.")]
        [DefaultValue("table")]
        public string Format { get; init; } = "table";

        [CommandOption("--debug")]
        [Description("Show full stack traces on error.")]
        public bool Debug { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Latitude) || string.IsNullOrWhiteSpace(Longitude))
                return ValidationResult.Error("Both --lat and --lon are required.");

            if (!TryParseCoordinate(Latitude, out var lat) || lat < -90.0 || lat > 90.0)
                return ValidationResult.Error("--lat must be a number in the range -90..90.");

            if (!TryParseCoordinate(Longitude, out var lon) || lon < -180.0 || lon > 180.0)
                return ValidationResult.Error("--lon must be a number in the range -180..180.");

            var hasLayers = Layers.Length > 0;
            var hasExchangeSet = !string.IsNullOrWhiteSpace(ExchangeSet);
            var hasInput = !string.IsNullOrWhiteSpace(Input);

            if (hasLayers && hasExchangeSet)
                return ValidationResult.Error("--layer and --from are mutually exclusive.");

            if (hasInput && (hasLayers || hasExchangeSet))
                return ValidationResult.Error("A positional dataset cannot be combined with --layer or --from.");

            if (!hasLayers && !hasExchangeSet && !hasInput)
                return ValidationResult.Error("Provide a dataset, one or more --layer options, or an exchange set (--from or positional).");

            if (!string.IsNullOrWhiteSpace(Only)
                && !hasExchangeSet
                && !(hasInput && ExchangeSetInput.LooksLikeExchangeSet(Input!)))
            {
                return ValidationResult.Error("--only applies only to the exchange-set form (--from or a positional exchange set).");
            }

            if (!double.IsFinite(RadiusMeters))
                return ValidationResult.Error("--radius must be a finite number.");

            if (!string.Equals(Format, "table", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Format, "json", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Error("--format must be 'table' or 'json'.");
            }

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        TryParseCoordinate(settings.Latitude, out var latitude);
        TryParseCoordinate(settings.Longitude, out var longitude);

        DateTimeOffset? time = null;
        if (!string.IsNullOrWhiteSpace(settings.Time))
        {
            if (!DateTimeOffset.TryParse(
                    settings.Time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Invalid --time value:[/] {settings.Time}");
                return 2;
            }
            time = parsed;
        }

        SpecRef? specFilter = null;
        if (!string.IsNullOrWhiteSpace(settings.Spec))
        {
            specFilter = new SpecRef(NormalizeSpec(settings.Spec), default);
        }

        IDisposable? exchangeSetResolution = null;
        var warnings = new List<string>();
        try
        {
            var inputs = ResolveInputs(settings, warnings, out exchangeSetResolution);
            if (inputs.Count == 0)
            {
                foreach (var warning in warnings)
                    Console.Error.WriteLine(warning);
                AnsiConsole.MarkupLine("[red]No datasets could be resolved for the pick.[/]");
                return 2;
            }

            var catalog = FileDatasetCatalog.Build(inputs, new ProjNetCrsTransformFactory());
            warnings.AddRange(catalog.Warnings);
            foreach (var warning in warnings)
                Console.Error.WriteLine(warning);

            if (catalog.Datasets.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No datasets loaded successfully for the pick.[/]");
                return 2;
            }

            var identify = new IdentifyFeaturesService(catalog);
            var identifyResult = identify.InvokeAsync(new IdentifyFeaturesRequest(
                latitude, longitude, specFilter,
                Math.Clamp(settings.RadiusMeters, 0.0, 100_000.0),
                Math.Clamp(settings.MaxResults, 1, 200)))
                .GetAwaiter().GetResult();

            if (!identifyResult.TryGetValue(out var features))
            {
                identifyResult.TryGetError(out var error);
                AnsiConsole.MarkupLineInterpolated($"[red]identify failed:[/] {error?.Message}");
                return 1;
            }

            var samples = SampleCoverages(catalog, latitude, longitude, time, specFilter);
            var describer = settings.Attributes ? new DescribeFeatureService(catalog) : null;

            if (string.Equals(settings.Format, "json", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(features, samples, warnings, describer);
            }
            else
            {
                WriteTable(features, samples, describer);
            }

            return 0;
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
            exchangeSetResolution?.Dispose();
        }
    }

    /// <summary>
    /// Resolves the command's input grammar to a list of dataset files, each
    /// detected to its product specification and paired with an S-101 external
    /// text resolver where applicable.
    /// </summary>
    private static List<FileDatasetInput> ResolveInputs(
        Settings settings,
        List<string> warnings,
        out IDisposable? exchangeSetResolution)
    {
        exchangeSetResolution = null;
        var inputs = new List<FileDatasetInput>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        var exchangeSetSource = !string.IsNullOrWhiteSpace(settings.ExchangeSet)
            ? settings.ExchangeSet
            : (settings.Layers.Length == 0 && !string.IsNullOrWhiteSpace(settings.Input)
                && ExchangeSetInput.LooksLikeExchangeSet(settings.Input)
                ? settings.Input
                : null);

        if (exchangeSetSource is not null)
        {
            IReadOnlySet<string>? only = null;
            if (!string.IsNullOrWhiteSpace(settings.Only))
                only = ParseOnlySpecs(settings.Only);

            var resolution = ExchangeSetLayerResolution.Resolve(exchangeSetSource, only);
            exchangeSetResolution = resolution;
            warnings.AddRange(resolution.Warnings);

            foreach (var layer in resolution.Layers)
            {
                var id = UniqueId(layer.RelativePath, usedIds);
                inputs.Add(new FileDatasetInput(
                    new DatasetId(id), layer.Spec, layer.Path,
                    BuildExternalTextResolver(layer.Spec, layer.Path)));
            }

            return inputs;
        }

        var paths = settings.Layers.Length > 0
            ? settings.Layers
            : (string.IsNullOrWhiteSpace(settings.Input) ? [] : new[] { settings.Input! });

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                warnings.Add($"Skipped missing dataset file: {path}");
                continue;
            }

            var spec = DatasetPipelineFactory.DetectProductSpec(path);
            if (spec is null)
            {
                warnings.Add($"Skipped unsupported dataset (no known product specification): {path}");
                continue;
            }

            var id = UniqueId(Path.GetFileName(path), usedIds);
            inputs.Add(new FileDatasetInput(
                new DatasetId(id), spec, path, BuildExternalTextResolver(spec, path)));
        }

        return inputs;
    }

    /// <summary>
    /// Builds a file-name → text resolver for an S-101 cell's
    /// <c>fileReference</c> attributes (S-101 Feature Catalogue) rooted at the
    /// cell's own directory, so referenced text is surfaced in the pick.
    /// Returns <c>null</c> for non-S-101 specs.
    /// </summary>
    private static Func<string, string?>? BuildExternalTextResolver(string spec, string path)
    {
        if (spec is not ("S-101" or "S-57"))
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory))
            return null;

        var source = FileSystemAssetSource.Create(directory);
        return new ExternalTextFileResolver(source, Path.GetFileName(path)).AsDelegate();
    }

    /// <summary>
    /// Samples the coverage products (S-102 / S-104 / S-111) present in the
    /// catalog at the pick point, honouring an optional spec filter. Coverage
    /// datasets are grouped by specification and <see cref="SampleCoverageService"/>
    /// is invoked once per spec, so the result holds at most one sample per
    /// specification (the service selects the covering dataset for that spec).
    /// </summary>
    private static List<SampleCoverageResult> SampleCoverages(
        IDatasetCatalog catalog,
        double latitude,
        double longitude,
        DateTimeOffset? time,
        SpecRef? specFilter)
    {
        var results = new List<SampleCoverageResult>();
        var service = new SampleCoverageService(catalog, new ProjNetCrsTransformFactory());

        var coverageSpecs = catalog.Datasets
            .Where(d => d.Data is S102CoverageData or S104CoverageData or S104StationSeriesData
                or S111CoverageData or S111StationSeriesData)
            .Select(d => d.Spec.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => specFilter is null || string.Equals(name, specFilter.Value.Name, StringComparison.Ordinal))
            .ToList();

        foreach (var specName in coverageSpecs)
        {
            var result = service.InvokeAsync(new SampleCoverageRequest(
                new SpecRef(specName, default), latitude, longitude, time))
                .GetAwaiter().GetResult();

            if (result.TryGetValue(out var sample))
                results.Add(sample);
        }

        return results;
    }

    private static void WriteTable(
        IdentifyFeaturesResult features,
        IReadOnlyList<SampleCoverageResult> samples,
        DescribeFeatureService? describer)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[bold]Pick[/] lat {features.Point.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}, lon {features.Point.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}");

        if (features.Features.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No vector features at this point.[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded).Title("Features");
            table.AddColumn("#");
            table.AddColumn("Dataset");
            table.AddColumn("Spec");
            table.AddColumn("Type");
            table.AddColumn("Geometry");
            table.AddColumn("Containment");
            table.AddColumn("Distance (m)");
            table.AddColumn("Feature ID");

            var rank = 1;
            foreach (var f in features.Features)
            {
                table.AddRow(
                    rank.ToString(CultureInfo.InvariantCulture),
                    Markup.Escape(f.DatasetId.Value),
                    Markup.Escape(f.Spec.Name),
                    Markup.Escape(f.FeatureType),
                    Markup.Escape(f.Geometry),
                    Markup.Escape(f.Containment),
                    f.DistanceMeters is { } d ? d.ToString("0.#", CultureInfo.InvariantCulture) : "-",
                    Markup.Escape(f.FeatureId));
                rank++;
            }

            AnsiConsole.Write(table);

            foreach (var f in features.Features)
            {
                if (f.ReferencedTexts is { Count: > 0 })
                {
                    foreach (var text in f.ReferencedTexts)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[grey]  {f.FeatureId} → {text.FileName}:[/] {Truncate(text.Text, 200)}");
                    }
                }

                if (describer is not null)
                {
                    var attrs = TryDescribeAttributes(describer, f.DatasetId, f.FeatureId);
                    if (attrs is not null)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[grey]  {f.FeatureId} attributes:[/] {Truncate(attrs.Value.GetRawText(), 400)}");
                    }
                }
            }

            if (features.Truncated)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[grey]Showing {features.Features.Count} of {features.TotalMatched} matched features.[/]");
            }
        }

        if (samples.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No coverage samples at this point.[/]");
            return;
        }

        var sampleTable = new Table().Border(TableBorder.Rounded).Title("Coverage samples");
        sampleTable.AddColumn("Spec");
        sampleTable.AddColumn("Dataset");
        sampleTable.AddColumn("Value");
        foreach (var s in samples)
        {
            sampleTable.AddRow(
                Markup.Escape(SpecOfSample(s)),
                Markup.Escape(s.DatasetId.Value),
                Markup.Escape(DescribeSample(s.Value)));
        }

        AnsiConsole.Write(sampleTable);
    }

    private static void WriteJson(
        IdentifyFeaturesResult features,
        IReadOnlyList<SampleCoverageResult> samples,
        IReadOnlyList<string> warnings,
        DescribeFeatureService? describer)
    {
        var root = new JsonObject
        {
            ["point"] = new JsonObject
            {
                ["latitude"] = features.Point.Latitude,
                ["longitude"] = features.Point.Longitude,
            },
            ["totalMatched"] = features.TotalMatched,
            ["truncated"] = features.Truncated,
        };

        var featureArray = new JsonArray();
        foreach (var f in features.Features)
        {
            var node = new JsonObject
            {
                ["datasetId"] = f.DatasetId.Value,
                ["spec"] = f.Spec.Name,
                ["featureId"] = f.FeatureId,
                ["featureType"] = f.FeatureType,
                ["geometry"] = f.Geometry,
                ["containment"] = f.Containment,
                ["distanceMeters"] = f.DistanceMeters,
            };

            if (f.ReferencedTexts is { Count: > 0 })
            {
                var texts = new JsonArray();
                foreach (var text in f.ReferencedTexts)
                {
                    texts.Add(new JsonObject
                    {
                        ["fileName"] = text.FileName,
                        ["text"] = text.Text,
                    });
                }
                node["referencedTexts"] = texts;
            }

            if (describer is not null)
            {
                var attrs = TryDescribeAttributes(describer, f.DatasetId, f.FeatureId);
                if (attrs is not null)
                    node["attributes"] = JsonNode.Parse(attrs.Value.GetRawText());
            }

            featureArray.Add(node);
        }

        root["features"] = featureArray;

        var sampleArray = new JsonArray();
        foreach (var s in samples)
        {
            sampleArray.Add(new JsonObject
            {
                ["spec"] = SpecOfSample(s),
                ["datasetId"] = s.DatasetId.Value,
                ["latitude"] = s.Latitude,
                ["longitude"] = s.Longitude,
                ["value"] = SampleToJson(s.Value),
            });
        }

        root["samples"] = sampleArray;

        if (warnings.Count > 0)
        {
            var warningArray = new JsonArray();
            foreach (var warning in warnings)
                warningArray.Add(warning);
            root["warnings"] = warningArray;
        }

        Console.WriteLine(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonElement? TryDescribeAttributes(
        DescribeFeatureService describer,
        DatasetId datasetId,
        string featureId)
    {
        var result = describer.InvokeAsync(new DescribeFeatureRequest(datasetId, featureId))
            .GetAwaiter().GetResult();
        return result.TryGetValue(out var described) ? described.Attributes : null;
    }

    private static JsonObject SampleToJson(SampledValue value) => value switch
    {
        DepthSample d => new JsonObject
        {
            ["kind"] = "depth",
            ["depthMeters"] = d.DepthMeters,
            ["uncertaintyMeters"] = d.UncertaintyMeters,
        },
        WaterLevelSample w => new JsonObject
        {
            ["kind"] = "waterLevel",
            ["waterLevelHeight"] = w.WaterLevelHeight,
            ["trend"] = w.Trend,
            ["sampleTime"] = w.SampleTime,
            ["cellCentreLatitude"] = w.CellCentreLatitude,
            ["cellCentreLongitude"] = w.CellCentreLongitude,
        },
        WaterLevelStationSample w => new JsonObject
        {
            ["kind"] = "waterLevelStation",
            ["stationId"] = w.StationId,
            ["stationDistanceMetres"] = w.StationDistanceMetres,
            ["waterLevelHeight"] = w.WaterLevelHeight,
            ["trend"] = w.Trend,
            ["sampleTime"] = w.SampleTime,
        },
        SurfaceCurrentSample c => new JsonObject
        {
            ["kind"] = "surfaceCurrent",
            ["speedMetresPerSecond"] = c.SpeedMetresPerSecond,
            ["speedKnots"] = c.SpeedKnots,
            ["directionDegreesTrue"] = c.DirectionDegreesTrue,
            ["sampleTime"] = c.SampleTime,
            ["cellCentreLatitude"] = c.CellCentreLatitude,
            ["cellCentreLongitude"] = c.CellCentreLongitude,
        },
        SurfaceCurrentStationSample c => new JsonObject
        {
            ["kind"] = "surfaceCurrentStation",
            ["stationId"] = c.StationId,
            ["stationDistanceMetres"] = c.StationDistanceMetres,
            ["speedMetresPerSecond"] = c.SpeedMetresPerSecond,
            ["speedKnots"] = c.SpeedKnots,
            ["directionDegreesTrue"] = c.DirectionDegreesTrue,
            ["sampleTime"] = c.SampleTime,
        },
        _ => new JsonObject { ["kind"] = "unknown" },
    };

    private static string DescribeSample(SampledValue value) => value switch
    {
        DepthSample d => $"depth {d.DepthMeters.ToString("0.##", CultureInfo.InvariantCulture)} m"
            + (d.UncertaintyMeters is { } u ? $" ±{u.ToString("0.##", CultureInfo.InvariantCulture)} m" : string.Empty),
        WaterLevelSample w => $"water level {w.WaterLevelHeight.ToString("0.##", CultureInfo.InvariantCulture)} m ({w.Trend}) @ {w.SampleTime:yyyy-MM-dd HH:mm}Z",
        WaterLevelStationSample w => $"water level {w.WaterLevelHeight.ToString("0.##", CultureInfo.InvariantCulture)} m ({w.Trend}) station {w.StationId} @ {w.SampleTime:yyyy-MM-dd HH:mm}Z",
        SurfaceCurrentSample c => $"current {c.SpeedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn @ {c.DirectionDegreesTrue.ToString("0.#", CultureInfo.InvariantCulture)}° @ {c.SampleTime:yyyy-MM-dd HH:mm}Z",
        SurfaceCurrentStationSample c => $"current {c.SpeedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn @ {c.DirectionDegreesTrue.ToString("0.#", CultureInfo.InvariantCulture)}° station {c.StationId} @ {c.SampleTime:yyyy-MM-dd HH:mm}Z",
        _ => "unknown",
    };

    private static string SpecOfSample(SampleCoverageResult result) => result.Value switch
    {
        DepthSample => "S-102",
        WaterLevelSample or WaterLevelStationSample => "S-104",
        SurfaceCurrentSample or SurfaceCurrentStationSample => "S-111",
        _ => "?",
    };

    private static string UniqueId(string candidate, HashSet<string> used)
    {
        var id = string.IsNullOrEmpty(candidate) ? "dataset" : candidate;
        if (used.Add(id))
            return id;

        for (var i = 2; ; i++)
        {
            var next = $"{id}#{i}";
            if (used.Add(next))
                return next;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static bool TryParseCoordinate(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && double.IsFinite(result);

    private static IReadOnlySet<string> ParseOnlySpecs(string only) =>
        only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOnlyToken)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Normalises a spec token to the CATALOG.XML-comparable form the exchange
    /// set resolver expects (digits only, e.g. <c>S101</c> → <c>S101</c>).
    /// </summary>
    private static string NormalizeOnlyToken(string token) =>
        token.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// Normalises a spec token to the canonical <c>S-NNN</c> form used by
    /// <see cref="SpecRef.Name"/> (e.g. <c>s124</c> / <c>S-124</c> → <c>S-124</c>).
    /// </summary>
    private static string NormalizeSpec(string spec)
    {
        var trimmed = spec.Trim().ToUpperInvariant();
        if (trimmed.StartsWith("S-", StringComparison.Ordinal))
            return trimmed;
        if (trimmed.StartsWith("S", StringComparison.Ordinal) && trimmed.Length > 1)
            return "S-" + trimmed[1..];
        return trimmed;
    }
}
