using System.IO.Compression;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// One dataset discovered in an exchange set and resolved to a local filesystem
/// path, ready to be opened as a composite layer.
/// </summary>
/// <param name="Path">Absolute filesystem path to the dataset file.</param>
/// <param name="Spec">The detected product specification (e.g. <c>"S-101"</c>).</param>
/// <param name="RelativePath">The source-relative path, for diagnostics.</param>
internal readonly record struct ResolvedExchangeSetLayer(string Path, string Spec, string RelativePath);

/// <summary>
/// Discovers the renderable datasets in an S-100 exchange set (a directory
/// containing a top-level <c>CATALOG.XML</c>, a <c>CATALOG.XML</c> file, or a
/// <c>.zip</c> archive whose root holds one) and resolves each to a local
/// filesystem path, so <c>s100 render</c> can composite the whole set without
/// the user enumerating every <c>--layer</c>.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 17. This is discovery + layer-list assembly on top
/// of the composite engine exposed by #402: it parses the catalogue with
/// <see cref="ExchangeCatalogueReader"/>, groups S-101 base cells and their
/// sequential updates with <see cref="S101ExchangeSetUpdatePlan"/>, and returns
/// the base/single datasets in catalogue order.
/// </para>
/// <para>
/// Carrying over the #402 composite limitations: the composite path applies
/// <b>no</b> S-101 sequential/sibling updates, so update files (and orphan
/// updates) are intentionally skipped here — only base and single cells are
/// composited. Datasets whose product specification is unsupported, whose file
/// is missing, or that declare data protection (encryption, which this CLI
/// cannot decrypt) are skipped with a warning rather than failing the whole
/// render.
/// </para>
/// <para>
/// A <c>.zip</c> exchange set is extracted to a uniquely-named temporary
/// directory; the resolution owns that directory and deletes it on
/// <see cref="Dispose"/>. Callers must therefore keep the resolution alive until
/// the datasets have been rendered, then dispose it.
/// </para>
/// </remarks>
internal sealed class ExchangeSetLayerResolution : IDisposable
{
    private readonly string? _tempDirectory;
    private bool _disposed;

    private ExchangeSetLayerResolution(
        IReadOnlyList<ResolvedExchangeSetLayer> layers,
        IReadOnlyList<string> warnings,
        string? tempDirectory)
    {
        Layers = layers;
        Warnings = warnings;
        _tempDirectory = tempDirectory;
    }

    /// <summary>The resolved renderable datasets, in catalogue order.</summary>
    public IReadOnlyList<ResolvedExchangeSetLayer> Layers { get; }

    /// <summary>
    /// Human-readable warnings for datasets that were discovered but skipped
    /// (unsupported spec, missing file, orphan update, or data-protected).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Resolves the renderable datasets in the exchange set at
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="path">
    /// A directory containing a top-level <c>CATALOG.XML</c>, a <c>CATALOG.XML</c>
    /// file, or a <c>.zip</c> archive whose root holds one.
    /// </param>
    /// <param name="only">
    /// When non-empty, restricts the result to datasets whose detected product
    /// specification (case-insensitive, e.g. <c>S101</c> or <c>S-101</c>) is in
    /// the set; other supported datasets are silently omitted.
    /// </param>
    /// <returns>The resolution (which the caller owns and must dispose).</returns>
    /// <exception cref="FileNotFoundException">No catalogue was found.</exception>
    public static ExchangeSetLayerResolution Resolve(string path, IReadOnlySet<string>? only = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? tempDirectory = null;
        try
        {
            var (root, cataloguePath) = LocateCatalogue(path, ref tempDirectory);
            var catalogue = ExchangeCatalogueReader.Read(cataloguePath);

            var layers = new List<ResolvedExchangeSetLayer>();
            var warnings = new List<string>();
            var plan = S101ExchangeSetUpdatePlan.Build(catalogue.DatasetDiscoveryMetadata);

            foreach (var item in plan)
            {
                var metadata = item.Base;
                var relativePath = metadata.RelativePath;

                // The composite path applies no S-101 updates, so an update with
                // no base in this set has nothing to attach to and is dropped.
                if (item.Kind == S101LoadItemKind.OrphanUpdate)
                {
                    warnings.Add($"Skipped orphan S-101 update (no base cell in set): {relativePath}");
                    continue;
                }

                // This CLI has no decryption keys, so a data-protected dataset
                // cannot be opened; skip it visibly rather than failing the set.
                if (metadata.DataProtection)
                {
                    warnings.Add($"Skipped data-protected (encrypted) dataset: {relativePath}");
                    continue;
                }

                var absolutePath = Path.GetFullPath(Path.Combine(root, relativePath));
                if (!File.Exists(absolutePath))
                {
                    warnings.Add($"Skipped missing dataset file: {relativePath}");
                    continue;
                }

                // Prefer the catalogue's declared product specification; fall
                // back to content sniffing when the catalogue omits it (common
                // for ISO 8211 / HDF5 sets), so such datasets are not spuriously
                // reported as unsupported.
                var spec = DatasetPipelineFactory.MapProductSpecificationToSpec(metadata.ProductSpecification)
                    ?? DatasetPipelineFactory.DetectProductSpec(absolutePath);
                if (spec is null)
                {
                    warnings.Add($"Skipped unsupported dataset (no known product specification): {relativePath}");
                    continue;
                }

                if (only is { Count: > 0 } && !only.Contains(NormalizeSpec(spec)))
                    continue;

                layers.Add(new ResolvedExchangeSetLayer(absolutePath, spec, relativePath));
            }

            var resolution = new ExchangeSetLayerResolution(layers, warnings, tempDirectory);
            tempDirectory = null; // ownership transferred to the resolution
            return resolution;
        }
        finally
        {
            // If we extracted a ZIP but never handed ownership to a resolution
            // (an exception above), clean the temp directory up now.
            if (tempDirectory is not null)
                TryDeleteDirectory(tempDirectory);
        }
    }

    /// <summary>
    /// Normalizes a spec token to the comparison form used for <c>--only</c>:
    /// upper-cased with hyphens removed (so <c>S-101</c> and <c>s101</c> match).
    /// </summary>
    public static string NormalizeSpec(string spec) =>
        spec.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static (string Root, string CataloguePath) LocateCatalogue(string path, ref string? tempDirectory)
    {
        if (Directory.Exists(path))
        {
            var catalogue = FindCatalogueInDirectory(path)
                ?? throw new FileNotFoundException($"No CATALOG.XML found in folder: {path}");
            return (Path.GetFullPath(path), catalogue);
        }

        if (IsCatalogueFileName(Path.GetFileName(path)) && File.Exists(path))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return (directory, Path.GetFullPath(path));
        }

        if (IsZipPath(path) && File.Exists(path))
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(), "s100-render-es-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            ZipFile.ExtractToDirectory(path, tempDirectory);

            var catalogue = FindCatalogueInDirectory(tempDirectory)
                ?? throw new FileNotFoundException($"No root CATALOG.XML found in archive: {path}");
            return (tempDirectory, catalogue);
        }

        throw new FileNotFoundException($"Not an S-100 exchange set: {path}");
    }

    private static string? FindCatalogueInDirectory(string folderPath)
    {
        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => IsCatalogueFileName(Path.GetFileName(f)));
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool IsCatalogueFileName(string? name) =>
        string.Equals(name, "CATALOG.XML", StringComparison.OrdinalIgnoreCase);

    private static bool IsZipPath(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_tempDirectory is not null)
            TryDeleteDirectory(_tempDirectory);
    }
}
