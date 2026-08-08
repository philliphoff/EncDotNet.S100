using System.ComponentModel;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.MutableTools;
using EncDotNet.S100.Mcp.Tools.Mutable;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 mcp serve</c> hosts the S-100 MCP tool set over the <b>stdio</b>
/// transport for a fixed set of datasets, so an agent that spawns this process
/// can query features, sample coverages, and drive a stateful session (palette,
/// time step, headless render) without a GUI viewer or an out-of-band HTTP
/// endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The datasets to serve are specified up front using the same input grammar
/// as <c>s100 identify</c> / <c>s100 render</c>: a single positional dataset,
/// repeated <c>--layer</c> options, or an exchange set (positional directory /
/// <c>CATALOG.XML</c> / <c>.zip</c>, or <c>--from</c>). They are loaded into a
/// <see cref="FileDatasetCatalog"/> for the read-only query tools, and opened as
/// resident render handles for the mutating tools; the process is the session
/// boundary — spawn another to serve a different set.
/// </para>
/// <para>
/// The server is <b>mutable by default</b>: alongside the read-only query tools
/// it exposes the presentation / time / render tools (<c>set_palette</c>,
/// <c>set_display_category</c>, <c>set_display_mode</c>, <c>set_time_step</c>,
/// <c>render_to_image</c>), backed by an in-process headless Skia session.
/// </para>
/// <para>
/// Standard output carries the MCP protocol, so every human-readable message
/// (startup banner, load warnings, errors) is written to standard error.
/// The server runs until the client disconnects (stdin EOF) or Ctrl-C.
/// </para>
/// </remarks>
internal sealed class McpServeCommand : AsyncCommand<McpServeCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Single-dataset form: the dataset to serve. Exchange-set form: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one.")]
        public string? Input { get; init; }

        [CommandOption("--layer <PATH>")]
        [Description("Add a dataset to serve (repeatable). Mutually exclusive with the exchange-set form.")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--from|--exchange-set <PATH>")]
        [Description("Serve every discoverable dataset in an exchange set: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one. Mutually exclusive with --layer.")]
        public string? ExchangeSet { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange-set form only: restrict loading to a comma-separated list of product specifications (e.g. --only S101,S102; hyphenation and case are ignored).")]
        public string? Only { get; init; }

        [CommandOption("--debug")]
        [Description("Show full stack traces on error.")]
        public bool Debug { get; init; }

        public override ValidationResult Validate()
        {
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

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // stdout is the MCP protocol channel — never write human-readable text
        // to it here. All diagnostics go to stderr.
        IDisposable? exchangeSetResolution = null;
        var warnings = new List<string>();
        try
        {
            var inputs = DatasetInputResolver.Resolve(
                settings.Input, settings.Layers, settings.ExchangeSet, settings.Only,
                warnings, out exchangeSetResolution);

            foreach (var warning in warnings)
                Console.Error.WriteLine(warning);

            if (inputs.Count == 0)
            {
                Console.Error.WriteLine("No datasets could be resolved to serve.");
                return 2;
            }

            // The mutable catalog is the single source of truth for the session:
            // it holds each dataset both as a projected LoadedDataset (read tools)
            // and an open render handle (headless renderer), and is what
            // open_dataset / close_dataset mutate.
            using var catalog = new HeadlessMutableCatalog(new ProjNetCrsTransformFactory());

            // Ownership of any exchange-set extraction transfers to the catalog,
            // which keeps it alive for the whole session (the composite renderer
            // re-reads dataset paths on each render).
            var toSeed = exchangeSetResolution;
            exchangeSetResolution = null;
            catalog.Seed(inputs, toSeed);

            if (catalog.Datasets.Count == 0)
            {
                Console.Error.WriteLine("No datasets loaded successfully to serve.");
                return 2;
            }

            // Mutable-by-default: the served tool set includes the mutating
            // catalog / presentation / time / render tools, backed by an
            // in-process headless session over the catalog above.
            using var session = new HeadlessS100Session(catalog);
            var additionalTools = S100MutableTools.Create(
                presentation: new StaticCapabilityAccessor<IPresentationController>(session),
                time: new StaticCapabilityAccessor<ITimeController>(session),
                renderer: new StaticCapabilityAccessor<IImageRenderer>(session),
                catalog: catalog);

            Console.Error.WriteLine(
                $"s100 mcp serve: serving {catalog.Datasets.Count} dataset(s) over stdio (mutable). Ctrl-C to stop.");

            await S100McpStdioHost.RunAsync(catalog, additionalTools);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (settings.Debug)
                Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            // Keep any extracted exchange-set resources alive for the whole
            // serving session: S-101/S-57 fileReference text is resolved lazily
            // from the dataset directory on describe_feature calls.
            exchangeSetResolution?.Dispose();
        }
    }
}
