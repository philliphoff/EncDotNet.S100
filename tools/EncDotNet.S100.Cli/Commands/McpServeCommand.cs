using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.Library;
using EncDotNet.S100.Mcp.MutableTools;
using EncDotNet.S100.Mcp.Tools.Library;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 mcp serve</c> hosts the S-100 MCP tool set over the <b>stdio</b>
/// transport, so an agent that spawns this process can query features, sample
/// coverages, drive a stateful session (palette, time step, headless render)
/// and work with a dataset-collection Library without a GUI viewer or an
/// out-of-band HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Datasets to open up front use the input grammar of <c>s100 identify</c>,
/// widened: a positional dataset, exchange set (directory / <c>CATALOG.XML</c>
/// / <c>.zip</c>) or plain folder of datasets, repeated <c>--layer</c> options,
/// and an exchange set given with <c>--from</c>, in any combination. They are
/// loaded into a <see cref="HeadlessMutableCatalog"/>, which parses each
/// dataset once into a resident processor that feeds both the query tools (via
/// a projected read model) and the headless renderer. Further datasets can be
/// added at runtime via <c>open_dataset</c>; the process is the session
/// boundary — spawn another to serve a different set.
/// </para>
/// <para>
/// The Library tools (#792) run over a headless Library kept in a data
/// directory (<c>--data-dir</c>; a temporary one, removed on exit, by
/// default), optionally over an existing <c>collections.json</c>
/// (<c>--collections</c>). <c>--collection</c> adds sources at startup — a
/// local path, a known-source id or a catalogue URL, with <c>#choice,…</c> to
/// include only some of it — skipping any the Library already has. Library
/// items are opened into the same catalog.
/// </para>
/// <para>
/// The server is <b>mutable by default</b>: alongside the read-only query tools
/// it exposes the catalog / presentation / time / render tools
/// (<c>open_dataset</c>, <c>close_dataset</c>, <c>close_all_datasets</c>,
/// <c>set_palette</c>, <c>set_display_category</c>, <c>set_display_mode</c>,
/// <c>set_time_step</c>, <c>set_viewport</c>, <c>render_to_image</c>), backed by
/// an in-process headless Skia session.
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
        [Description("A dataset to serve; an exchange set (a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one); or a plain folder of datasets, searched recursively.")]
        public string? Input { get; init; }

        [CommandOption("--layer <PATH>")]
        [Description("Add a dataset to serve (repeatable).")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--from|--exchange-set <PATH>")]
        [Description("Serve every discoverable dataset in an exchange set: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one.")]
        public string? ExchangeSet { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange sets and folders only: restrict loading to a comma-separated list of product specifications (e.g. --only S101,S102; hyphenation and case are ignored).")]
        public string? Only { get; init; }

        [CommandOption("--collection <SOURCE>")]
        [Description("Add a source to the Library at startup (repeatable): a folder, exchange set, collection manifest or S-128 catalogue path; a known-source id (list_known_sources); or a catalogue URL. Append #choice,choice to include only those choices, e.g. noaa-s111#sfbofs. A source the Library already has is skipped.")]
        public string[] Collections { get; init; } = [];

        [CommandOption("--collections <FILE>")]
        [Description("Use this collections.json as the Library (for example the viewer's); changes are saved to it. Defaults to collections.json in the data directory.")]
        public string? CollectionsFile { get; init; }

        [CommandOption("--data-dir <DIR>")]
        [Description("Keep the Library's index cache, catalogue cache, downloads (and collections.json) here between runs. Defaults to a temporary directory that is removed on exit.")]
        public string? DataDir { get; init; }

        [CommandOption("--debug")]
        [Description("Show full stack traces on error.")]
        public bool Debug { get; init; }

        public override ValidationResult Validate()
        {
            var hasExchangeSet = !string.IsNullOrWhiteSpace(ExchangeSet);
            var hasInput = !string.IsNullOrWhiteSpace(Input);

            if (!string.IsNullOrWhiteSpace(Only)
                && !hasExchangeSet
                && !(hasInput && (ExchangeSetInput.LooksLikeExchangeSet(Input!) || Directory.Exists(Input))))
            {
                return ValidationResult.Error("--only applies only to exchange sets (--from or positional) and positional folders.");
            }

            if (Collections.Any(string.IsNullOrWhiteSpace))
                return ValidationResult.Error("--collection needs a path, known-source id or URL.");

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // stdout is the MCP protocol channel — never write human-readable text
        // to it here. All diagnostics go to stderr.
        IDisposable? exchangeSetResolution = null;
        var warnings = new List<string>();
        var temporaryDataDir = settings.DataDir is null
            ? Path.Combine(Path.GetTempPath(), "s100-mcp-" + Guid.NewGuid().ToString("N"))
            : null;
        try
        {
            var inputs = DatasetInputResolver.Resolve(
                settings.Input, settings.Layers, settings.ExchangeSet, settings.Only,
                warnings, out exchangeSetResolution);

            foreach (var warning in warnings)
                Console.Error.WriteLine(warning);

            var askedForDatasets = !string.IsNullOrWhiteSpace(settings.Input) || settings.Layers.Length > 0
                || !string.IsNullOrWhiteSpace(settings.ExchangeSet);
            if (askedForDatasets && inputs.Count == 0)
            {
                Console.Error.WriteLine("No datasets could be resolved to serve.");
                return 2;
            }

            // The mutable catalog is the single source of truth for the session:
            // it holds each dataset both as a projected LoadedDataset (read tools)
            // and an open render handle (headless renderer), and is what
            // open_dataset / close_dataset and the Library's loads mutate.
            using var catalog = new HeadlessMutableCatalog(new ProjNetCrsTransformFactory());

            // Ownership of any exchange-set extraction transfers to the catalog,
            // which keeps it alive for the whole session (the composite renderer
            // re-reads dataset paths on each render).
            var toSeed = exchangeSetResolution;
            exchangeSetResolution = null;
            catalog.Seed(inputs, toSeed);

            if (askedForDatasets && catalog.Datasets.Count == 0)
            {
                Console.Error.WriteLine("No datasets loaded successfully to serve.");
                return 2;
            }

            // The Library (#792): sources indexed into the data directory, their
            // items downloaded there and opened into the catalog above.
            using var opener = new CatalogLibraryOpener(catalog);
            var root = Path.GetFullPath(settings.DataDir ?? temporaryDataDir!);
            var paths = settings.CollectionsFile is { } collectionsFile
                ? new LibraryDataPaths(root) { CollectionsFile = Path.GetFullPath(collectionsFile) }
                : new LibraryDataPaths(root);
            using var services = LibraryServices.Create(
                paths, FeedPublisher.TryReadMetadata, new LibraryServicesOptions { IsInUse = opener.IsInUse });
            services.Library.Initialize();
            var operations = new LibraryOperations(services.Library, services.Downloads, new LibraryLoader(opener));
            var adder = new LibrarySourceAdder(services.Readers, probe: services.ProbeCatalogue);
            var library = new HeadlessLibrary(
                operations, services.Sync, readers: services.Readers, probe: services.ProbeCatalogue);

            foreach (var collection in settings.Collections)
            {
                if (!await AddCollectionAsync(collection, adder, services.Library).ConfigureAwait(false))
                    return 2;
            }

            // Mutable-by-default: the served tool set includes the mutating
            // catalog / presentation / time / render tools, backed by an
            // in-process headless session over the catalog above, and the
            // Library tools.
            using var session = new HeadlessS100Session(catalog);
            var additionalTools = S100MutableTools.Create(
                presentation: new StaticCapabilityAccessor<IPresentationController>(session),
                time: new StaticCapabilityAccessor<ITimeController>(session),
                renderer: new StaticCapabilityAccessor<IImageRenderer>(session),
                catalog: catalog,
                viewport: new StaticCapabilityAccessor<IViewportController>(session))
                .Concat(LibraryMcpTools.Create(library, library, services.SecomRegistry, services.SecomTrust))
                .ToList();

            var collections = services.Library.Collections.Count(c => !c.IsSession);
            Console.Error.WriteLine(
                $"s100 mcp serve: serving {catalog.Datasets.Count} dataset(s) and a Library of {collections} collection(s) over stdio (mutable). Ctrl-C to stop.");
            Console.Error.WriteLine(temporaryDataDir is null
                ? $"Library data: {root}"
                : $"Library data: {root} (temporary; pass --data-dir to keep it)");

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
            if (temporaryDataDir is not null)
                TryDelete(temporaryDataDir);
        }
    }

    /// <summary>
    /// Adds the <c>--collection</c> source <paramref name="value"/> to the
    /// Library in a new collection, unless the Library already has it.
    /// </summary>
    /// <returns>False (after saying why on standard error) when it cannot be added.</returns>
    internal static async Task<bool> AddCollectionAsync(string value, LibrarySourceAdder adder, CollectionLibrary library)
    {
        var (draft, error) = await adder.DraftAsync(CollectionRequest(value)).ConfigureAwait(false);
        if (error is not null)
        {
            Console.Error.WriteLine($"--collection {value}: {error.Message}");
            return false;
        }

        if (!draft!.CanBuild)
        {
            Console.Error.WriteLine($"--collection {value}: nothing to add; check the choices after '#'.");
            return false;
        }

        var name = draft.SuggestedName;
        var candidate = Canonical(draft.Build(name));
        if (library.Collections.SelectMany(c => c.Sources).Any(s => Canonical(s.Definition) == candidate))
        {
            Console.Error.WriteLine($"--collection {value}: already in the Library.");
            return true;
        }

        draft.AddTo(library, collectionName: name);
        Console.Error.WriteLine($"--collection {value}: added as '{name}'.");
        return true;
    }

    /// <summary>
    /// The add request for a <c>--collection</c> value: an http(s) URL, an
    /// existing (or path-like) local path, or else a known-source id, with
    /// <c>#choice,…</c> after it.
    /// </summary>
    internal static AddSourceRequest CollectionRequest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var source = value.Trim();
        IReadOnlyList<string>? choices = null;
        var hash = source.LastIndexOf('#');
        if (hash > 0 && !File.Exists(source) && !Directory.Exists(source))
        {
            choices = source[(hash + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            source = source[..hash];
        }

        string? known = null, path = null, url = null;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            url = source;
        else if (File.Exists(source) || Directory.Exists(source) || LooksLikePath(source))
            path = source;
        else
            known = source;

        return new AddSourceRequest(known, path, url, null, choices is { Count: > 0 } ? choices : null, null, null, null, null, null, Preview: false);
    }

    private static bool LooksLikePath(string value) =>
        value.StartsWith('.') || value.StartsWith('~') || Path.IsPathRooted(value)
        || value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>A source as stored, without its id: two sources that read the same are the same source.</summary>
    private static string Canonical(CollectionSource source) =>
        JsonSerializer.Serialize(source with { Id = Guid.Empty },
            (JsonTypeInfo<CollectionSource>)CollectionJson.StoreOptions.GetTypeInfo(typeof(CollectionSource)));

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not remove the temporary Library data in {directory}: {ex.Message}");
        }
    }
}
