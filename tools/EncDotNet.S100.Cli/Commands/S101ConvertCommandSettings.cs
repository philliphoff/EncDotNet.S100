using System.ComponentModel;
using EncDotNet.S100.Datasets.S57;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Settings for <c>s100 s101 convert</c>: the S-101 source dataset to read, the
/// S-57 cell (or exchange set) to write, and the choices S-65 Annex C leaves to
/// the converter (<see cref="S101ToS57TranslationOptions"/>).
/// </summary>
internal sealed class S101ConvertCommandSettings : CommandSettings
{
    [CommandArgument(0, "<source>")]
    [Description("Path to the source S-101 base dataset (.000).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <output>")]
    [Description("Path of the S-57 base cell to write (e.g. XX5CELL1.000), or with --exchange-set the directory to write the exchange set into.")]
    public string OutputPath { get; init; } = string.Empty;

    [CommandOption("--to <FORMAT>")]
    [Description("The format to write. Only s57 (S-57 Edition 3.1 ENC, following S-65 Annex C) is supported.")]
    [DefaultValue("s57")]
    public string To { get; init; } = "s57";

    [CommandOption("--exchange-set")]
    [Description("Write an S-57 exchange set instead of a bare cell: <output>/ENC_ROOT/CATALOG.031 and <output>/ENC_ROOT/<cell>/<cell>.000.")]
    public bool ExchangeSet { get; init; }

    [CommandOption("--cell-name <NAME>")]
    [Description("With --exchange-set, the cell's file name without extension. Defaults to the source file name; S-57 expects an 8-character name such as XX5CELL1.")]
    public string? CellName { get; init; }

    [CommandOption("--report <report>")]
    [Description("Write a machine-readable (JSON) translation diagnostics report to this path.")]
    public string? ReportPath { get; init; }

    [CommandOption("--no-updates")]
    [Description("Do not auto-discover and fold sibling update files (.001, .002, …) before converting.")]
    public bool NoUpdates { get; init; }

    [CommandOption("--no-optional-inform")]
    [Description("Skip the conversions to INFORM that S-65 Annex C marks optional (such as multiplicityOfFeatures).")]
    public bool NoOptionalInform { get; init; }

    [CommandOption("--inform-separator <SEPARATOR>")]
    [Description("The separator between text strings folded into one INFORM or NINFOM (Annex C §2.4.6).")]
    [DefaultValue(";")]
    public string InformSeparator { get; init; } = ";";

    [CommandOption("--max-inform-length <LENGTH>")]
    [Description("The recommended maximum length of INFORM and NINFOM (Annex C §2.4.6).")]
    [DefaultValue(300)]
    public int MaxInformLength { get; init; } = 300;

    [CommandOption("--inform-length <POLICY>")]
    [Description("What to do with a longer INFORM or NINFOM: keep (default) keeps the whole text and reports it; truncate cuts it at --max-inform-length.")]
    [DefaultValue("keep")]
    public string InformLength { get; init; } = "keep";

    [CommandOption("--national-language <CODE>")]
    [Description("The ISO 639-2 language (e.g. fra) whose name, text and file reference to prefer for NOBJNM, NINFOM and NTXTDS when there are several. Defaults to the first.")]
    public string? NationalLanguage { get; init; }

    [CommandOption("--name-without-usage <POLICY>")]
    [Description("What to do with a featureName that has no nameUsage: by-language (default) writes an English or unlabelled name to OBJNAM and any other to NOBJNM; drop doesn't convert it.")]
    [DefaultValue("by-language")]
    public string NameWithoutUsage { get; init; } = "by-language";

    [CommandOption("--debug")]
    [Description("Show full stack traces on error.")]
    public bool Debug { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return Spectre.Console.ValidationResult.Error("A source S-101 dataset path is required.");

        if (!File.Exists(SourcePath))
            return Spectre.Console.ValidationResult.Error($"Source S-101 dataset not found: {SourcePath}");

        if (!IsS57(To))
            return Spectre.Console.ValidationResult.Error($"Invalid --to value '{To}'. Only s57 is supported.");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return Spectre.Console.ValidationResult.Error("An output path is required (-o|--output).");

        var dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            return Spectre.Console.ValidationResult.Error($"Output directory does not exist: {dir}");

        if (ExchangeSet && File.Exists(OutputPath))
            return Spectre.Console.ValidationResult.Error($"With --exchange-set the output must be a directory, but a file exists at: {OutputPath}");

        if (!string.IsNullOrEmpty(CellName))
        {
            if (!ExchangeSet)
                return Spectre.Console.ValidationResult.Error("--cell-name applies only with --exchange-set; name the cell with -o|--output instead.");

            if (CellName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || CellName.Contains('.'))
                return Spectre.Console.ValidationResult.Error($"Invalid --cell-name '{CellName}'. Give a file name without a directory or extension.");
        }

        if (MaxInformLength <= 0)
            return Spectre.Console.ValidationResult.Error("--max-inform-length must be positive.");

        if (!TryParseInformLength(InformLength, out _))
            return Spectre.Console.ValidationResult.Error($"Invalid --inform-length value '{InformLength}'. Use keep or truncate.");

        if (!TryParseNameWithoutUsage(NameWithoutUsage, out _))
            return Spectre.Console.ValidationResult.Error($"Invalid --name-without-usage value '{NameWithoutUsage}'. Use by-language or drop.");

        if (!string.IsNullOrWhiteSpace(ReportPath))
        {
            var reportDir = Path.GetDirectoryName(Path.GetFullPath(ReportPath));
            if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                return Spectre.Console.ValidationResult.Error($"Report directory does not exist: {reportDir}");
        }

        return Spectre.Console.ValidationResult.Success();
    }

    /// <summary>The translation options these settings select.</summary>
    internal S101ToS57TranslationOptions ToTranslationOptions()
    {
        TryParseInformLength(InformLength, out var informLength);
        TryParseNameWithoutUsage(NameWithoutUsage, out var nameWithoutUsage);

        return new S101ToS57TranslationOptions
        {
            InformSeparator = InformSeparator,
            MaxInformLength = MaxInformLength,
            InformLengthPolicy = informLength,
            NationalLanguage = string.IsNullOrWhiteSpace(NationalLanguage) ? null : NationalLanguage.Trim(),
            NameWithoutUsage = nameWithoutUsage,
            IncludeOptionalInform = !NoOptionalInform,
        };
    }

    /// <summary>
    /// Whether a <c>--to</c> token names S-57 (case-insensitive; <c>s57</c> and
    /// <c>S-57</c> are equivalent).
    /// </summary>
    internal static bool IsS57(string? value) =>
        value?.Trim().Replace("-", string.Empty).ToLowerInvariant() == "s57";

    internal static bool TryParseInformLength(string? value, out S101InformLengthPolicy policy)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "keep":
                policy = S101InformLengthPolicy.Keep;
                return true;
            case "truncate":
                policy = S101InformLengthPolicy.Truncate;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    internal static bool TryParseNameWithoutUsage(string? value, out S101NameWithoutUsage policy)
    {
        switch (value?.Trim().Replace("-", string.Empty).ToLowerInvariant())
        {
            case null or "" or "bylanguage":
                policy = S101NameWithoutUsage.ByLanguage;
                return true;
            case "drop":
                policy = S101NameWithoutUsage.Drop;
                return true;
            default:
                policy = default;
                return false;
        }
    }
}
