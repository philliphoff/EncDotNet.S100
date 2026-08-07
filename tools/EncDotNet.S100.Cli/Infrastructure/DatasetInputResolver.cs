using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Resolves the CLI's shared dataset-input grammar — a single positional
/// dataset, repeated <c>--layer</c> options, or an exchange set (positional
/// directory / <c>CATALOG.XML</c> / <c>.zip</c>, or <c>--from</c>) — to the
/// list of <see cref="FileDatasetInput"/> a <see cref="FileDatasetCatalog"/>
/// is built from.
/// </summary>
/// <remarks>
/// Shared by <c>s100 identify</c> and <c>s100 mcp serve</c> so both build a
/// byte-identical catalog. Datasets whose product specification is
/// unsupported, whose file is missing, or that fail to parse are skipped with
/// a warning rather than failing the whole resolution.
/// </remarks>
internal static class DatasetInputResolver
{
    /// <summary>
    /// Resolves the input grammar to dataset files, each detected to its
    /// product specification and paired with an S-101 external text resolver
    /// where applicable.
    /// </summary>
    /// <param name="input">The positional input (single dataset or exchange set), or <c>null</c>.</param>
    /// <param name="layers">Repeated <c>--layer</c> paths; empty when unused.</param>
    /// <param name="exchangeSet">Explicit <c>--from</c> exchange-set source, or <c>null</c>.</param>
    /// <param name="only">Optional comma-separated spec filter for the exchange-set form.</param>
    /// <param name="warnings">Accumulates human-readable skip warnings, in input order.</param>
    /// <param name="exchangeSetResolution">
    /// Set to the disposable that owns any extracted exchange-set resources
    /// (e.g. a temp directory for a <c>.zip</c>); the caller must dispose it
    /// once the catalog is no longer needed. <c>null</c> for the non-exchange
    /// forms.
    /// </param>
    public static List<FileDatasetInput> Resolve(
        string? input,
        string[] layers,
        string? exchangeSet,
        string? only,
        List<string> warnings,
        out IDisposable? exchangeSetResolution)
    {
        exchangeSetResolution = null;
        var inputs = new List<FileDatasetInput>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        var exchangeSetSource = !string.IsNullOrWhiteSpace(exchangeSet)
            ? exchangeSet
            : (layers.Length == 0 && !string.IsNullOrWhiteSpace(input)
                && ExchangeSetInput.LooksLikeExchangeSet(input)
                ? input
                : null);

        if (exchangeSetSource is not null)
        {
            IReadOnlySet<string>? onlySpecs = null;
            if (!string.IsNullOrWhiteSpace(only))
                onlySpecs = ParseOnlySpecs(only);

            var resolution = ExchangeSetLayerResolution.Resolve(exchangeSetSource, onlySpecs);
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

        var paths = layers.Length > 0
            ? layers
            : (string.IsNullOrWhiteSpace(input) ? [] : new[] { input! });

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
    /// Builds a file-name → text resolver for an S-101 / S-57 cell's
    /// <c>fileReference</c> attributes rooted at the cell's own directory, so
    /// referenced text is surfaced. Returns <c>null</c> for other specs.
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

    private static IReadOnlySet<string> ParseOnlySpecs(string only) =>
        only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOnlyToken)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Normalises a spec token to the CATALOG.XML-comparable form the exchange
    /// set resolver expects: hyphens removed and upper-cased
    /// (e.g. <c>s-101</c> → <c>S101</c>).
    /// </summary>
    private static string NormalizeOnlyToken(string token) =>
        token.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
