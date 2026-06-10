using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 validate &lt;dataset&gt;</c> — detects the dataset's product
/// specification and runs its normative validation rule pack (if one is
/// available for the spec) via <see cref="IDatasetProcessor.Validate"/>,
/// reporting the findings as a table or as JSON.
/// </summary>
/// <remarks>
/// Validation is a pure function of the parsed dataset (independent of
/// palette, opacity, or time step). Specs without a rule pack — coverage
/// products and S-101 / S-57 today — report "no rules available" and exit
/// successfully; this is distinct from a dataset that was evaluated and
/// found conformant.
/// </remarks>
internal sealed class ValidateCommand : Command<ValidateCommand.Settings>
{
    /// <summary>Exit code returned when validation produced failing findings.</summary>
    private const int FindingsExitCode = 6;

    internal sealed class Settings : DatasetCommandSettings
    {
        [CommandOption("--format")]
        [Description("Output format: text or json (default text).")]
        [DefaultValue("text")]
        public string Format { get; init; } = "text";

        [CommandOption("--strict")]
        [Description("Treat warnings as failures: exit non-zero when any warning (not just error) is present.")]
        [DefaultValue(false)]
        public bool Strict { get; init; }

        public override ValidationResult Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.Successful)
                return baseResult;

            if (!TryParseFormat(Format, out _))
                return ValidationResult.Error($"Unknown format '{Format}'. Use text or json.");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        TryParseFormat(settings.Format, out var format);
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        try
        {
            var spec = DatasetPipelineFactory.DetectProductSpec(settings.DatasetPath);
            if (spec is null)
            {
                if (format == OutputFormat.Json)
                    EmitJsonError("spec-not-detected", $"Could not detect an S-100 product specification for: {settings.DatasetPath}");
                else
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]Could not detect an S-100 product specification for:[/] {settings.DatasetPath}");
                return 2;
            }

            var processor = factory.CreateProcessor(settings.DatasetPath);
            var report = processor.Validate();

            return format == OutputFormat.Json
                ? EmitJson(processor.Spec, report, settings.Strict)
                : EmitText(processor.Spec, report, settings.Strict);
        }
        catch (NotSupportedException ex)
        {
            ReportException("Not supported", ex, format, settings.Debug);
            return 4;
        }
        catch (S100DatasetNotSupportedException ex)
        {
            // Recognised-but-not-yet-implemented spec feature (e.g. data coding
            // format 1). Does not derive from NotSupportedException, so it needs
            // its own catch to map to exit 4 rather than the generic exit-1 path.
            // See issue #253.
            ReportException("Not supported", ex, format, settings.Debug);
            return 4;
        }
        catch (S100DatasetSchemaException ex)
        {
            ReportException("Non-conforming dataset", ex, format, settings.Debug, warning: true);
            return 5;
        }
        catch (Exception ex)
        {
            ReportException("Error", ex, format, settings.Debug);
            return 1;
        }
        finally
        {
            catalogueManager.Dispose();
        }
    }

    private static int EmitText(SpecRef spec, ValidationReport? report, bool strict)
    {
        if (report is null)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]No validation rules are available for[/] {Markup.Escape(spec.Name)}.");
            return 0;
        }

        if (report.Findings.IsDefaultOrEmpty)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[green]Valid[/] — {Markup.Escape(spec.Name)} ([grey]{report.RulesEvaluated} rule(s) evaluated, no findings[/])");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Severity");
        table.AddColumn("Rule");
        table.AddColumn("Message");
        table.AddColumn("Location");

        foreach (var finding in report.Findings)
        {
            table.AddRow(
                SeverityMarkup(finding.Severity),
                Markup.Escape(finding.RuleId),
                Markup.Escape(finding.Message),
                Markup.Escape(FormatLocation(finding)));
        }

        AnsiConsole.Write(table);

        int errors = report.FindingsOfSeverity(ValidationSeverity.Error).Count();
        int warnings = report.FindingsOfSeverity(ValidationSeverity.Warning).Count();
        int infos = report.FindingsOfSeverity(ValidationSeverity.Info).Count();

        AnsiConsole.MarkupLineInterpolated(
            $"[grey]{report.RulesEvaluated} rule(s) evaluated, {report.RulesWithFindings} with findings:[/] [red]{errors} error(s)[/], [yellow]{warnings} warning(s)[/], [blue]{infos} info[/].");

        return Failed(report, strict) ? FindingsExitCode : 0;
    }

    private static int EmitJson(SpecRef spec, ValidationReport? report, bool strict)
    {
        var payload = new
        {
            specification = spec.Name,
            edition = spec.Edition.ToString(),
            rulesAvailable = report is not null,
            rulesEvaluated = report?.RulesEvaluated ?? 0,
            rulesWithFindings = report?.RulesWithFindings ?? 0,
            valid = report is null || report.Findings.IsDefaultOrEmpty,
            findings = (report?.Findings ?? System.Collections.Immutable.ImmutableArray<ValidationFinding>.Empty)
                .Select(f => new
                {
                    ruleId = f.RuleId,
                    severity = f.Severity.ToString(),
                    message = f.Message,
                    datasetId = f.DatasetId,
                    relatedFeatureId = f.RelatedFeatureId,
                    point = f.Point is { } p ? new { latitude = p.Latitude, longitude = p.Longitude } : null,
                    boundingBox = f.BoundingBox is { } b
                        ? new
                        {
                            southLatitude = b.SouthLatitude,
                            westLongitude = b.WestLongitude,
                            northLatitude = b.NorthLatitude,
                            eastLongitude = b.EastLongitude,
                        }
                        : null,
                })
                .ToArray(),
        };

        Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        return report is not null && Failed(report, strict) ? FindingsExitCode : 0;
    }

    private static bool Failed(ValidationReport report, bool strict) =>
        report.HasErrors || (strict && report.HasWarnings);

    private static string FormatLocation(ValidationFinding finding)
    {
        if (finding.RelatedFeatureId is { Length: > 0 } id)
            return id;
        if (finding.Point is { } p)
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#####}, {1:0.#####}", p.Latitude, p.Longitude);
        if (finding.BoundingBox is { } b)
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:0.###}, {1:0.###} – {2:0.###}, {3:0.###}]",
                b.SouthLatitude, b.WestLongitude, b.NorthLatitude, b.EastLongitude);
        return string.Empty;
    }

    private static string SeverityMarkup(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => "[red]error[/]",
        ValidationSeverity.Warning => "[yellow]warning[/]",
        _ => "[blue]info[/]",
    };

    private static void ReportException(
        string label, Exception ex, OutputFormat format, bool debug, bool warning = false)
    {
        if (format == OutputFormat.Json)
        {
            EmitJsonError(label.ToLowerInvariant().Replace(' ', '-'), ex.Message);
            return;
        }

        var colour = warning ? "yellow" : "red";
        AnsiConsole.MarkupLineInterpolated($"[{colour}]{label}:[/] {ex.Message}");
        if (debug)
            AnsiConsole.WriteException(ex);
    }

    private static void EmitJsonError(string error, string message) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(new { error, message }, JsonOptions));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private enum OutputFormat
    {
        Text,
        Json,
    }

    private static bool TryParseFormat(string value, out OutputFormat format)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "text": format = OutputFormat.Text; return true;
            case "json": format = OutputFormat.Json; return true;
            default: format = OutputFormat.Text; return false;
        }
    }
}
