using System.IO.Compression;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100;

/// <summary>
/// An opened S-100 exchange set: its parsed <c>CATALOG.XML</c> and the
/// datasets it lists, each of which opens as an <see cref="S100Dataset"/>
/// parsed against the bundled catalogues. S-100 Part 17.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Datasets"/> has one entry per dataset to load: an S-101 base cell
/// and the sequential updates the set ships for it are grouped into a single
/// entry, which opens with the updates applied. Every other dataset is its own
/// entry.
/// </para>
/// <para>
/// Datasets opened from an exchange set read from its source lazily, so dispose
/// them before (or together with) the exchange set.
/// </para>
/// </remarks>
public sealed class S100ExchangeSet : IDisposable, IAsyncDisposable
{
    private const string CatalogueFileName = "CATALOG.XML";

    private readonly IAssetSource _source;
    private readonly bool _ownsSource;
    private bool _disposed;

    private S100ExchangeSet(IAssetSource source, bool ownsSource, ExchangeCatalogue catalogue)
    {
        _source = source;
        _ownsSource = ownsSource;
        Catalogue = catalogue;
        SupportFiles = BuildSupportFileMap(catalogue);
        Datasets = S101ExchangeSetUpdatePlan.Build(catalogue.DatasetDiscoveryMetadata)
            .Select(item => new S100ExchangeSetDataset(this, item))
            .ToArray();
    }

    /// <summary>
    /// Opens the exchange set at <paramref name="path"/>: a folder whose top
    /// level holds <c>CATALOG.XML</c>, the <c>CATALOG.XML</c> file itself, or a
    /// <c>.zip</c> archive whose root holds <c>CATALOG.XML</c>. The file name is
    /// matched case-insensitively.
    /// </summary>
    /// <param name="path">The folder, catalogue file, or ZIP archive.</param>
    /// <param name="cancellationToken">Cancels reading the catalogue.</param>
    /// <returns>
    /// The opened exchange set. It owns the folder or archive source it creates,
    /// and releases it when disposed.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="path"/> does not exist, or has no <c>CATALOG.XML</c> where expected.
    /// </exception>
    /// <exception cref="System.Xml.XmlException">The catalogue is not well-formed.</exception>
    public static async Task<S100ExchangeSet> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (source, cataloguePath) = OpenSource(path);
        try
        {
            var catalogue = await ReadCatalogueAsync(source, cataloguePath, cancellationToken).ConfigureAwait(false);
            return new S100ExchangeSet(source, ownsSource: true, catalogue);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the exchange set whose catalogue is at
    /// <paramref name="cataloguePath"/> inside <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// The source holding the exchange set, e.g. a
    /// <see cref="FileSystemAssetSource"/>, a <see cref="ZipAssetSource"/>, or a
    /// <see cref="CachingAssetSource"/> over either. The exchange set borrows
    /// the source: the caller keeps ownership and must keep it alive for as long
    /// as the exchange set and any dataset opened from it are in use.
    /// </param>
    /// <param name="cataloguePath">
    /// The catalogue's path relative to <paramref name="source"/>. The dataset
    /// paths the catalogue lists are resolved relative to the source root.
    /// </param>
    /// <param name="cancellationToken">Cancels reading the catalogue.</param>
    /// <returns>The opened exchange set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="cataloguePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="source"/> has no file at <paramref name="cataloguePath"/>
    /// (the exact exception type is the source's).
    /// </exception>
    /// <exception cref="System.Xml.XmlException">The catalogue is not well-formed.</exception>
    public static async Task<S100ExchangeSet> OpenAsync(
        IAssetSource source,
        string cataloguePath = CatalogueFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        var catalogue = await ReadCatalogueAsync(source, cataloguePath, cancellationToken).ConfigureAwait(false);
        return new S100ExchangeSet(source, ownsSource: false, catalogue);
    }

    /// <summary>
    /// Creates an exchange set over <paramref name="source"/> with an
    /// already-parsed <paramref name="catalogue"/>, for decorators such as
    /// <see cref="S100ExchangeSetProtectionExtensions.WithDecryption"/>.
    /// </summary>
    internal static S100ExchangeSet Create(IAssetSource source, bool ownsSource, ExchangeCatalogue catalogue) =>
        new(source, ownsSource, catalogue);

    /// <summary>The parsed exchange catalogue (<c>CATALOG.XML</c>).</summary>
    public ExchangeCatalogue Catalogue { get; }

    /// <summary>
    /// The datasets to load, in catalogue order: one entry per dataset, except
    /// that an S-101 base cell and its in-set sequential updates share a single
    /// entry (see <see cref="S100ExchangeSetDataset.Updates"/>).
    /// </summary>
    public IReadOnlyList<S100ExchangeSetDataset> Datasets { get; }

    /// <summary>The source the catalogue and datasets are read from.</summary>
    internal IAssetSource Source
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _source;
        }
    }

    /// <summary>
    /// Support-file name to source-relative path, so an S-101 cell can resolve
    /// its <c>fileReference</c> text files through the catalogue; <see langword="null"/>
    /// when the catalogue lists none.
    /// </summary>
    internal IReadOnlyDictionary<string, string>? SupportFiles { get; }

    /// <summary>
    /// Releases the source when this exchange set owns it (it was opened from a
    /// path); a borrowed source is left to its owner.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsSource)
            _source.Dispose();
    }

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        return _ownsSource ? _source.DisposeAsync() : ValueTask.CompletedTask;
    }

    private static async Task<ExchangeCatalogue> ReadCatalogueAsync(
        IAssetSource source, string cataloguePath, CancellationToken cancellationToken)
    {
        await using var stream = await source.OpenAsync(cataloguePath, cancellationToken).ConfigureAwait(false);
        return ExchangeCatalogueReader.Read(stream);
    }

    private static (IAssetSource Source, string CataloguePath) OpenSource(string path)
    {
        if (Directory.Exists(path))
        {
            var catalogue = FindCatalogueInDirectory(path)
                ?? throw new FileNotFoundException($"No {CatalogueFileName} found in folder: {path}", path);
            return (FileSystemAssetSource.Create(path), Path.GetFileName(catalogue));
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"S-100 exchange set not found: {path}", path);

        if (string.Equals(Path.GetFileName(path), CatalogueFileName, StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return (FileSystemAssetSource.Create(directory), Path.GetFileName(path));
        }

        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var source = ZipAssetSource.Create(path);
            var entry = FindRootCatalogueEntry(path);
            if (entry is null)
            {
                source.Dispose();
                throw new FileNotFoundException($"No root {CatalogueFileName} found in archive: {path}", path);
            }

            return (source, entry);
        }

        throw new FileNotFoundException(
            $"Not an S-100 exchange set (expected a folder, {CatalogueFileName}, or .zip): {path}", path);
    }

    private static string? FindCatalogueInDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), CatalogueFileName, StringComparison.OrdinalIgnoreCase));

    private static string? FindRootCatalogueEntry(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries
            .FirstOrDefault(e => !e.FullName.Contains('/') && !e.FullName.Contains('\\')
                && string.Equals(e.FullName, CatalogueFileName, StringComparison.OrdinalIgnoreCase))
            ?.FullName;
    }

    private static IReadOnlyDictionary<string, string>? BuildSupportFileMap(ExchangeCatalogue catalogue)
    {
        var entries = catalogue.SupportFileDiscoveryMetadata;
        if (entries.Count == 0)
            return null;

        var map = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.FileName))
                map[entry.FileName] = entry.RelativePath;
        }

        return map.Count > 0 ? map : null;
    }
}
