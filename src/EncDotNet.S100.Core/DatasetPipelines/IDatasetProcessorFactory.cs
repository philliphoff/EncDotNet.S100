namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Creates an <see cref="IDatasetProcessor"/> for a dataset on the local file
/// system, without the caller having to know which S-100 product the file
/// belongs to or how that product's processor is constructed. This is the
/// renderer-neutral, product-agnostic seam a reusable host (e.g. the Mapsui
/// <c>Map.AddS100</c> extension) depends on so it can turn a dropped file into a
/// processor while referencing neither the concrete
/// <see cref="DatasetPipelineFactory"/> nor any per-product assembly.
/// <see cref="DatasetPipelineFactory"/> is the built-in implementation.
/// </summary>
public interface IDatasetProcessorFactory
{
    /// <summary>
    /// Creates a processor for the dataset file at <paramref name="path"/>,
    /// detecting the product specification from the file. Throws
    /// <see cref="System.NotSupportedException"/> when the file is unrecognized
    /// or no processor is registered for its product.
    /// </summary>
    IDatasetProcessor CreateProcessor(string path);

    /// <summary>
    /// Creates a processor for the base cell file at <paramref name="path"/>,
    /// discovering and applying any sibling sequential update files that live in
    /// the same directory (S-57 / S-101 Part 10a). Products that never carry
    /// <c>.000</c> sequential updates, or a base cell with no updates on disk,
    /// behave identically to <see cref="CreateProcessor(string)"/>.
    /// </summary>
    IDatasetProcessor CreateProcessorWithFilesystemUpdates(string path);
}
