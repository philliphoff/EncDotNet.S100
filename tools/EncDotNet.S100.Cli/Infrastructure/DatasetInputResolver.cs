using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Resolves the CLI's shared dataset-input grammar — a positional dataset,
/// exchange set (directory / <c>CATALOG.XML</c> / <c>.zip</c>) or plain
/// folder of datasets, repeated <c>--layer</c> options, and an exchange set
/// given with <c>--from</c> — to the list of <see cref="FileDatasetInput"/> a
/// <see cref="FileDatasetCatalog"/> is built from.
/// </summary>
/// <remarks>
/// Shared by <c>s100 identify</c> and <c>s100 mcp serve</c> so both build a
/// byte-identical catalog. The forms combine (each command decides which it
/// accepts together): an exchange set's datasets come first, then a folder's,
/// then single files. Datasets whose product specification is unsupported,
/// whose file is missing, or that fail to parse are skipped with a warning
/// rather than failing the whole resolution.
/// </remarks>
internal static class DatasetInputResolver
{
    /// <summary>
    /// Resolves the input grammar to dataset files, each detected to its
    /// product specification and paired with an S-101 external text resolver
    /// where applicable.
    /// </summary>
    /// <param name="input">The positional input (a single dataset, an exchange set or a plain folder of datasets), or <c>null</c>.</param>
    /// <param name="layers">Repeated <c>--layer</c> paths; empty when unused.</param>
    /// <param name="exchangeSet">Explicit <c>--from</c> exchange-set source, or <c>null</c>.</param>
    /// <param name="only">Optional comma-separated spec filter for the exchange-set form.</param>
    /// <param name="warnings">Accumulates human-readable skip warnings, in input order.</param>
    /// <param name="exchangeSetResolution">
    /// Set to the disposable that owns any extracted exchange-set resources
    /// (e.g. a temp directory for a <c>.zip</c>); the caller must dispose it
    /// once the catalog is no longer needed. <c>null</c> when no exchange set
    /// was opened.
    /// </param>
    /// <param name="specHint">
    /// Optional product-spec hint (e.g. <c>"S-102"</c>, <c>"s102"</c>) that
    /// forces the spec for the <b>single-file</b> forms instead of
    /// auto-detecting it. Ignored for exchange sets and folders. When provided, the
    /// file is loaded as that spec even if auto-detection would fail.
    /// </param>
    public static List<FileDatasetInput> Resolve(
        string? input,
        string[] layers,
        string? exchangeSet,
        string? only,
        List<string> warnings,
        out IDisposable? exchangeSetResolution,
        string? specHint = null)
    {
        exchangeSetResolution = null;
        var inputs = new List<FileDatasetInput>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var resolutions = new List<IDisposable>();

        var positionalIsExchangeSet = !string.IsNullOrWhiteSpace(input) && ExchangeSetInput.LooksLikeExchangeSet(input);
        var exchangeSets = new[] { exchangeSet, positionalIsExchangeSet ? input : null }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToArray();

        IReadOnlySet<string>? onlySpecs = string.IsNullOrWhiteSpace(only) ? null : ParseOnlySpecs(only);
        foreach (var source in exchangeSets)
            AddExchangeSet(source, onlySpecs, inputs, usedIds, warnings, resolutions);

        if (!positionalIsExchangeSet && !string.IsNullOrWhiteSpace(input) && Directory.Exists(input))
            AddFolder(input, onlySpecs, inputs, usedIds, warnings, resolutions);

        var paths = layers.Concat(!positionalIsExchangeSet && !string.IsNullOrWhiteSpace(input) && !Directory.Exists(input) ? [input!] : []);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                warnings.Add($"Skipped missing dataset file: {path}");
                continue;
            }

            // A caller-supplied spec hint forces the product for the single-file
            // forms; otherwise auto-detect from the file.
            var spec = !string.IsNullOrWhiteSpace(specHint)
                ? NormalizeSpecHint(specHint)
                : DatasetPipelineFactory.DetectProductSpec(path);
            if (spec is null)
            {
                warnings.Add($"Skipped unsupported dataset (no known product specification): {path}");
                continue;
            }

            var id = UniqueId(Path.GetFileName(path), usedIds);
            inputs.Add(new FileDatasetInput(
                new DatasetId(id), spec, path, BuildExternalTextResolver(spec, path)));
        }

        exchangeSetResolution = resolutions.Count switch
        {
            0 => null,
            1 => resolutions[0],
            _ => new Disposables(resolutions),
        };
        return inputs;
    }

    private static void AddExchangeSet(
        string source, IReadOnlySet<string>? onlySpecs, List<FileDatasetInput> inputs, HashSet<string> usedIds,
        List<string> warnings, List<IDisposable> resolutions, string? idPrefix = null)
    {
        var resolution = ExchangeSetLayerResolution.Resolve(source, onlySpecs);
        resolutions.Add(resolution);
        warnings.AddRange(resolution.Warnings);

        foreach (var layer in resolution.Layers)
        {
            var id = UniqueId(idPrefix is null ? layer.RelativePath : $"{idPrefix}/{layer.RelativePath}", usedIds);
            inputs.Add(new FileDatasetInput(
                new DatasetId(id), layer.Spec, layer.Path,
                BuildExternalTextResolver(layer.Spec, layer.Path)));
        }
    }

    /// <summary>The extensions of loose dataset files in a plain folder, as the Library indexes them.</summary>
    private static readonly string[] FolderDatasetExtensions = [".000", ".h5", ".hdf5", ".gml"];

    /// <summary>
    /// Adds a plain folder: each exchange set in it (a folder holding a
    /// catalogue, not searched further) and every loose dataset file, searched
    /// recursively; archives are left for the caller to open directly.
    /// </summary>
    private static void AddFolder(
        string folder, IReadOnlySet<string>? onlySpecs, List<FileDatasetInput> inputs, HashSet<string> usedIds,
        List<string> warnings, List<IDisposable> resolutions)
    {
        var root = Path.GetFullPath(folder);
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var relative = Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/');
            if (directory != root && ExchangeSetInput.LooksLikeExchangeSet(directory))
            {
                AddExchangeSet(directory, onlySpecs, inputs, usedIds, warnings, resolutions, relative);
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", options).Order(StringComparer.Ordinal))
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Skipped archive in a folder (open it directly): {file}");
                    continue;
                }
                if (!FolderDatasetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;

                var spec = DatasetPipelineFactory.DetectProductSpec(file);
                if (spec is null || (onlySpecs is not null && !onlySpecs.Contains(NormalizeOnlyToken(spec))))
                    continue;

                var id = UniqueId(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), usedIds);
                inputs.Add(new FileDatasetInput(new DatasetId(id), spec, file, BuildExternalTextResolver(spec, file)));
            }

            foreach (var child in Directory.EnumerateDirectories(directory, "*", options).Order(StringComparer.Ordinal).Reverse())
                pending.Push(child);
        }
    }

    /// <summary>Disposes several exchange-set resolutions as one.</summary>
    private sealed class Disposables(IReadOnlyList<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
                item.Dispose();
        }
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

    /// <summary>
    /// Normalises a product-spec hint to the canonical <c>S-NNN</c> form the
    /// projector switches on (e.g. <c>"s102"</c>, <c>"S102"</c>, <c>"s-102"</c>
    /// → <c>"S-102"</c>). An unrecognised shape is passed through trimmed so the
    /// projector rejects it as unsupported.
    /// </summary>
    private static string NormalizeSpecHint(string hint)
    {
        var compact = hint.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (compact.Length > 1 && compact[0] == 'S' && compact[1..].All(char.IsDigit))
        {
            return $"S-{compact[1..]}";
        }
        return hint.Trim();
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
