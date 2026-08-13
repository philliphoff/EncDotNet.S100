using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Builds an <see cref="IDatasetProcessor"/> for a dataset on the local file
/// system, given the shared <paramref name="services"/> and the dataset
/// <paramref name="path"/>.
/// </summary>
public delegate IDatasetProcessor DatasetProcessorFromPath(
    DatasetProcessorServices services,
    string path);

/// <summary>
/// Builds an <see cref="IDatasetProcessor"/> for a dataset stored inside an
/// <see cref="IAssetSource"/> (e.g. an exchange-set folder or ZIP), given the
/// shared <paramref name="services"/> and the <paramref name="request"/>.
/// </summary>
public delegate IDatasetProcessor DatasetProcessorFromSource(
    DatasetProcessorServices services,
    DatasetProcessorSourceRequest request);

/// <summary>
/// Decides, from a dataset file's content, whether it belongs to a particular
/// product. Used to disambiguate a file extension shared by more than one
/// product: the ISO 8211 <c>.000</c> extension is used by both legacy S-57 and
/// S-101, so the S-57 registration sniffs the ISO 8211 DDR for the S-57-only
/// <c>DSPM</c> field. Returns <see langword="true"/> when the file belongs to
/// the registering product.
/// </summary>
/// <param name="path">Path to the dataset file to inspect.</param>
public delegate bool DatasetContentDiscriminator(string path);

/// <summary>
/// A dataset stored inside an <see cref="IAssetSource"/>, addressed for
/// processor construction by a <see cref="DatasetProcessorFromSource"/>.
/// </summary>
public sealed record DatasetProcessorSourceRequest
{
    /// <summary>The asset source (folder or ZIP) hosting the dataset.</summary>
    public required IAssetSource Source { get; init; }

    /// <summary>Path to the dataset, relative to <see cref="Source"/>.</summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Optional map of support-file name (case-insensitive) to source-relative
    /// path, from the exchange-set catalogue (S-100 Edition 5.2.1 Part 17).
    /// </summary>
    public IReadOnlyDictionary<string, string>? SupportFiles { get; init; }
}

/// <summary>
/// Registers one S-100 product with a <see cref="S100ProductRegistry"/>: the
/// canonical spec string it handles (e.g. <c>"S-101"</c>) and the two ways to
/// construct its processor (from a file path, and from an
/// <see cref="EncDotNet.S100.Core.IAssetSource"/>). This is what inverts the
/// former hard <c>switch</c> in <see cref="DatasetPipelineFactory"/> into
/// data, so a host can register only the products it needs.
/// </summary>
public sealed record S100ProductRegistration
{
    /// <summary>The canonical product-specification string (e.g. <c>"S-101"</c>).</summary>
    public required string Spec { get; init; }

    /// <summary>Constructs the product's processor from a local file path.</summary>
    public required DatasetProcessorFromPath CreateFromPath { get; init; }

    /// <summary>Constructs the product's processor from an asset source.</summary>
    public required DatasetProcessorFromSource CreateFromSource { get; init; }

    /// <summary>
    /// Optional content sniffer that claims a file whose extension is ambiguous
    /// across products. Only meaningful for the S-57 registration today (S-57 and
    /// S-101 both use the ISO 8211 <c>.000</c> extension). It is
    /// <see langword="null"/> for products whose extension already identifies
    /// them. <see cref="DatasetPipelineFactory"/> consults this only for the
    /// products a registry actually contains, so a host that omits S-57 never
    /// runs the S-57 sniff and treats every <c>.000</c> file as S-101.
    /// </summary>
    public DatasetContentDiscriminator? Discriminate { get; init; }

    /// <summary>
    /// Optional recognizer for a GML-encoded product: given the parsed root of a
    /// <c>.gml</c> document, decides whether the document belongs to this
    /// product. This is what inverts the former central GML namespace
    /// <c>switch</c> in <see cref="DatasetPipelineFactory"/> into data, so a new
    /// GML product is recognized in the same place it is constructed. It is
    /// <see langword="null"/> for non-GML products (S-101/S-57 ISO 8211,
    /// S-102/S-104/S-111 HDF5), whose extension or header identifies them
    /// instead. <see cref="DatasetPipelineFactory"/> reads the document root once
    /// and returns the spec of the first registered product whose matcher claims
    /// it.
    /// </summary>
    public DatasetGmlMatcher? MatchGml { get; init; }
}
