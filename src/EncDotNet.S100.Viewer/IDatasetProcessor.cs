namespace EncDotNet.S100.Viewer;

/// <summary>
/// Processes a dataset file and renders it into Mapsui layers.
/// Constructed once per file; <see cref="Render"/> may be called
/// multiple times with different spec-specific contexts.
/// </summary>
internal interface IDatasetProcessor
{
    string ProductSpec { get; }

    DatasetResult Render(RenderContext? context = null);
}
