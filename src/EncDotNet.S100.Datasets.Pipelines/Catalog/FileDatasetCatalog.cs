using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// One dataset file to load into a <see cref="FileDatasetCatalog"/>.
/// </summary>
/// <param name="Id">Stable identifier for the dataset within the catalog session.</param>
/// <param name="Spec">
/// The detected product specification name (e.g. <c>"S-101"</c>,
/// <c>"S-102"</c>), typically from <c>DatasetPipelineFactory.DetectProductSpec</c>.
/// </param>
/// <param name="Path">Filesystem path to the dataset file (absolute, or relative to the current working directory).</param>
/// <param name="ExternalTextResolver">
/// Optional file-name → text delegate for S-101 <c>fileReference</c>
/// attributes (S-101 Feature Catalogue); <c>null</c> for non-S-101 specs or
/// loose cells with no sibling text files.
/// </param>
public sealed record FileDatasetInput(
    DatasetId Id,
    string Spec,
    string Path,
    Func<string, string?>? ExternalTextResolver = null);

/// <summary>
/// A read-only, file-backed <see cref="IDatasetCatalog"/> built once from a
/// fixed set of dataset files — the headless counterpart to the viewer's
/// live catalog, used by the CLI <c>identify</c> command to run the same
/// pick services (<c>IdentifyFeaturesService</c> /
/// <c>SampleCoverageService</c>) the MCP surface exposes, without an open
/// viewer or MCP server.
/// </summary>
/// <remarks>
/// <para>
/// Each input is opened, sniffed to its product specification by the caller,
/// and projected via <see cref="LoadedDatasetProjector"/> so the catalog
/// entries are byte-identical to those a viewer would produce. Vector
/// products (S-101 and the GML specs) are fully materialised into managed
/// model objects; coverage products (S-102 / S-104 / S-111) are read
/// eagerly into managed value arrays, so the snapshot is self-contained and
/// no file handle is held open past <see cref="Build"/>.
/// </para>
/// <para>
/// The catalog is immutable: <see cref="Changed"/> is declared to satisfy
/// <see cref="IDatasetCatalog"/> but never raised. A file that cannot be
/// opened, parsed, or whose spec is unsupported is skipped and reported via
/// <see cref="Warnings"/> rather than failing the whole build.
/// </para>
/// </remarks>
public sealed class FileDatasetCatalog : IDatasetCatalog
{
    private FileDatasetCatalog(IReadOnlyList<LoadedDataset> datasets, IReadOnlyList<string> warnings)
    {
        Datasets = datasets;
        Warnings = warnings;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets { get; }

    /// <summary>
    /// Human-readable warnings for inputs that were skipped (unsupported
    /// spec, missing file, or a parse failure), in input order.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <inheritdoc />
    /// <remarks>Never raised — the catalog is a fixed, one-shot snapshot.</remarks>
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Builds a catalog by projecting each input file into a
    /// <see cref="LoadedDataset"/>.
    /// </summary>
    /// <param name="inputs">The dataset files to load, in the desired order.</param>
    /// <param name="transforms">
    /// Optional CRS transform factory used when projecting coverage bounds. For
    /// projected S-102 tiles (e.g. UTM) this reprojects the native grid extent
    /// into WGS-84 so <see cref="LoadedDataset.Bounds"/> matches the WGS-84
    /// point-in-bounds test used when sampling; when null a naive fallback is
    /// used, which leaves projected-tile bounds in native metres.
    /// </param>
    /// <returns>An immutable catalog whose <see cref="Datasets"/> holds every
    /// successfully projected input and whose <see cref="Warnings"/> reports
    /// the rest.</returns>
    public static FileDatasetCatalog Build(
        IEnumerable<FileDatasetInput> inputs,
        ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var datasets = new List<LoadedDataset>();
        var warnings = new List<string>();

        foreach (var input in inputs)
        {
            if (input is null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(input.Path) || !File.Exists(input.Path))
            {
                warnings.Add($"Skipped missing dataset file: {input.Path}");
                continue;
            }

            LoadedDataset? projected;
            try
            {
                using var stream = File.OpenRead(input.Path);
                projected = LoadedDatasetProjector.Project(
                    input.Id, input.Spec, stream, input.ExternalTextResolver, transforms);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or NotSupportedException)
            {
                warnings.Add($"Skipped unreadable dataset '{input.Path}': {ex.Message}");
                continue;
            }

            if (projected is null)
            {
                warnings.Add(
                    $"Skipped unsupported dataset (no known product specification '{input.Spec}'): {input.Path}");
                continue;
            }

            datasets.Add(projected);
        }

        return new FileDatasetCatalog(datasets, warnings);
    }
}
