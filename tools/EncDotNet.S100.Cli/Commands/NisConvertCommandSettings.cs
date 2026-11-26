using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Datasets.Nis;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Settings for <c>s100 nis convert</c>: the NIS file geodatabase to read, the
/// cell in it to convert, the S-57 cell (or exchange set) to write, and the
/// dataset records the NIS doesn't hold (<see cref="NisToS57Options"/>).
/// </summary>
internal sealed class NisConvertCommandSettings : CommandSettings
{
    [CommandArgument(0, "<source>")]
    [Description("Path to the NIS file geodatabase (a .gdb directory).")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <output>")]
    [Description("Path of the S-57 base cell to write (e.g. US5CA98M.000), or with --exchange-set the directory to write the exchange set into. Not needed with --list.")]
    public string OutputPath { get; init; } = string.Empty;

    [CommandOption("--to <FORMAT>")]
    [Description("The format to write. Only s57 (an S-57 Edition 3.1 base cell) is supported.")]
    [DefaultValue("s57")]
    public string To { get; init; } = "s57";

    [CommandOption("--cell <NAME>")]
    [Description("The cell to convert: a source cell name (DSNM) as --list prints it, such as US5CA98M.005, or the name without its extension. Needed when the NIS holds more than one cell.")]
    public string? Cell { get; init; }

    [CommandOption("--list")]
    [Description("List the cells the NIS holds, with their feature counts, and exit.")]
    public bool List { get; init; }

    [CommandOption("--exchange-set")]
    [Description("Write an S-57 exchange set instead of a bare cell: <output>/ENC_ROOT/CATALOG.031 and <output>/ENC_ROOT/<cell>/<cell>.000.")]
    public bool ExchangeSet { get; init; }

    [CommandOption("--cell-name <NAME>")]
    [Description("The name of the cell to write, without extension (DSID DSNM and, with --exchange-set, its file name). Defaults to the --cell name without its extension, such as US5CA98M.")]
    public string? CellName { get; init; }

    [CommandOption("--bbox <W,S,E,N>")]
    [Description("Only features whose extent overlaps this box, in decimal degrees.")]
    public string? Bbox { get; init; }

    [CommandOption("--edition <N>")]
    [Description("The edition number to write in DSID (EDTN).")]
    [DefaultValue(1)]
    public int Edition { get; init; } = 1;

    [CommandOption("--issue-date <YYYYMMDD>")]
    [Description("The issue date to write in DSID (ISDT, UADT). Defaults to today (UTC).")]
    public string? IssueDate { get; init; }

    [CommandOption("--agency <CODE>")]
    [Description("The numeric S-62 producing agency to write in DSID (AGEN), also given to features whose long-name agency isn't known. Defaults to the commonest agency of the features.")]
    public int? Agency { get; init; }

    [CommandOption("--agency-code <TOKEN=CODE>")]
    [Description("The numeric S-62 code of a two-character agency token in the NIS long names, such as US=550 (built in). Repeat for each token.")]
    public string[] AgencyCodes { get; init; } = [];

    [CommandOption("--no-coverage")]
    [Description("Don't add M_COVR objects when the cell has none.")]
    public bool NoCoverage { get; init; }

    [CommandOption("--report <report>")]
    [Description("Write a machine-readable (JSON) translation diagnostics report to this path.")]
    public string? ReportPath { get; init; }

    [CommandOption("--debug")]
    [Description("Show full stack traces on error.")]
    public bool Debug { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return Spectre.Console.ValidationResult.Error("A source NIS file geodatabase path is required.");

        if (!Directory.Exists(SourcePath))
            return Spectre.Console.ValidationResult.Error($"Source file geodatabase not found (a .gdb directory): {SourcePath}");

        if (List)
            return Spectre.Console.ValidationResult.Success();

        if (!string.Equals(To, "s57", StringComparison.OrdinalIgnoreCase))
            return Spectre.Console.ValidationResult.Error($"Invalid --to value '{To}'. Only s57 is supported.");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return Spectre.Console.ValidationResult.Error("An output path is required (-o|--output).");

        var dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            return Spectre.Console.ValidationResult.Error($"Output directory does not exist: {dir}");

        if (ExchangeSet && File.Exists(OutputPath))
            return Spectre.Console.ValidationResult.Error($"With --exchange-set the output must be a directory, but a file exists at: {OutputPath}");

        if (!string.IsNullOrEmpty(CellName) && (CellName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || CellName.Contains('.')))
            return Spectre.Console.ValidationResult.Error($"Invalid --cell-name '{CellName}'. Give a file name without a directory or extension.");

        if (Bbox is not null && !TryParseBbox(Bbox, out _))
            return Spectre.Console.ValidationResult.Error($"Invalid --bbox '{Bbox}'. Use west,south,east,north in decimal degrees.");

        if (Edition < 0)
            return Spectre.Console.ValidationResult.Error("--edition must not be negative.");

        if (IssueDate is not null && !TryParseDate(IssueDate, out _))
            return Spectre.Console.ValidationResult.Error($"Invalid --issue-date '{IssueDate}'. Use YYYYMMDD.");

        if (!TryParseAgencyCodes(AgencyCodes, out _, out var bad))
            return Spectre.Console.ValidationResult.Error($"Invalid --agency-code '{bad}'. Use TOKEN=CODE, such as US=550.");

        if (!string.IsNullOrWhiteSpace(ReportPath))
        {
            var reportDir = Path.GetDirectoryName(Path.GetFullPath(ReportPath));
            if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                return Spectre.Console.ValidationResult.Error($"Report directory does not exist: {reportDir}");
        }

        return Spectre.Console.ValidationResult.Success();
    }

    /// <summary>The translator options these settings ask for, for the chosen source cell.</summary>
    internal NisToS57Options ToOptions(string? datasetName)
    {
        TryParseAgencyCodes(AgencyCodes, out var codes, out _);
        return new NisToS57Options
        {
            DatasetName = datasetName,
            CellName = string.IsNullOrWhiteSpace(CellName) ? null : CellName,
            Bounds = Bbox is not null && TryParseBbox(Bbox, out var bounds) ? bounds : null,
            EditionNumber = Edition,
            IssueDate = IssueDate is not null && TryParseDate(IssueDate, out var date) ? date : null,
            ProducingAgency = Agency,
            AgencyCodes = codes,
            SynthesiseCoverage = !NoCoverage,
        };
    }

    internal static bool TryParseBbox(string text, out NisBounds bounds)
    {
        bounds = default;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
        }
        if (values[0] >= values[2] || values[1] >= values[3]) return false;
        bounds = new NisBounds(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryParseDate(string text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseAgencyCodes(string[] values, out Dictionary<string, int> codes, out string? bad)
    {
        codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bad = null;
        foreach (var value in values)
        {
            var parts = value.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length != 2
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var code) || code > ushort.MaxValue)
            {
                bad = value;
                return false;
            }
            codes[parts[0]] = code;
        }
        return true;
    }
}
