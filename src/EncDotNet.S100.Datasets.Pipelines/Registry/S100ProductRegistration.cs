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
}
