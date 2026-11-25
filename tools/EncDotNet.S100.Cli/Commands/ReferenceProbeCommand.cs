using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 reference probe</c>: runs each feature type's portrayal rule over
/// the values it reads and writes what each combination draws next to the
/// bundled portrayal catalogue (<c>content/S101/pc/rule-probes.json</c>),
/// keyed by the feature and portrayal catalogue hashes (symbol dictionary
/// design note §7, R13a). Run it when a bundled catalogue changes.
/// </summary>
internal sealed class ReferenceProbeCommand : AsyncCommand<ReferenceProbeCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--spec <SPEC>")]
        [Description("The product specification whose bundled catalogues are probed. Only S-101 is supported.")]
        public string Spec { get; init; } = "S-101";

        [CommandOption("-o|--output <FILE>")]
        [Description("Where to write the results (.json, or .json.gz to compress). Defaults to rule-probes.json next to the bundled portrayal catalogue in this repository's src/EncDotNet.S100.Specifications/content.")]
        public string? Output { get; init; }

        [CommandOption("--cap <COUNT>")]
        [Description("The most combinations to probe per feature type. Defaults to 512.")]
        public int Cap { get; init; } = RuleProbeSet.DefaultCap;

        [CommandOption("--parallel <COUNT>")]
        [Description("How many Lua sessions to run at once. Defaults to the number of processors.")]
        public int? Parallel { get; init; }

        [CommandOption("--feature-type <CODE>")]
        [Description("Probe only this feature type; repeat for more. For trying the prober; the committed file has every feature type.")]
        public string[] FeatureTypes { get; init; } = [];

        public override ValidationResult Validate()
        {
            if (!RuleProber.Supports(Spec))
                return ValidationResult.Error($"--spec {Spec} can't be probed; only S-101's Lua rules are.");
            if (Cap < 1)
                return ValidationResult.Error("--cap must be at least 1.");
            return Parallel is < 1 ? ValidationResult.Error("--parallel must be at least 1.") : ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = settings.Output ?? DefaultOutput(settings.Spec);
        if (output is null)
        {
            Console.Error.WriteLine("Error: Couldn't find src/EncDotNet.S100.Specifications/content from here; run in the repository or pass --output.");
            return 1;
        }

        var options = new RuleProberOptions
        {
            Cap = settings.Cap,
            MaxDegreeOfParallelism = settings.Parallel ?? Environment.ProcessorCount,
            FeatureTypes = settings.FeatureTypes.Length > 0 ? settings.FeatureTypes : null,
        };

        var stopwatch = Stopwatch.StartNew();
        RuleProbeSet set = null!;
        await AnsiConsole.Progress()
            .AutoClear(true)
            .StartAsync(async progress =>
            {
                var task = progress.AddTask($"Probing {settings.Spec} rules");
                set = await RuleProber.ProbeBundledAsync(
                    settings.Spec,
                    LuaEngineFactory.CreateForTracing(),
                    options,
                    new Progress<RuleProbeProgress>(p =>
                    {
                        task.MaxValue = Math.Max(1, p.Total);
                        task.Value = p.Done;
                    }),
                    cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        stopwatch.Stop();

        RuleProbeStore.WriteFile(set, output);
        var size = new FileInfo(output).Length;
        var combinations = set.FeatureTypes.Sum(f => f.Variants.Count);
        var partial = set.FeatureTypes.Count(f => f.IsPartial);
        AnsiConsole.MarkupLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Probed [bold]{set.FeatureTypes.Count}[/] feature types, {combinations:N0} combinations ({partial} partial), in {stopwatch.Elapsed.TotalSeconds:0.0} s."));
        AnsiConsole.MarkupLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Wrote {Markup.Escape(output)} ({size / 1024.0:N0} KB)."));
        return 0;
    }

    /// <summary>
    /// <c>src/EncDotNet.S100.Specifications/content/&lt;spec&gt;/pc/rule-probes.json</c>
    /// in the repository the command runs in, or that it was built from.
    /// </summary>
    internal static string? DefaultOutput(string spec)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var pc = Path.Combine(dir.FullName, "src", "EncDotNet.S100.Specifications", "content", spec.Replace("-", string.Empty, StringComparison.Ordinal), "pc");
                if (Directory.Exists(pc)) return Path.Combine(pc, RuleProbeStore.FileName);
            }
        }

        return null;
    }
}
