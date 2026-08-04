using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Describes a <see cref="MapsuiMapSession"/> dataset render lifecycle event
/// (started or completed).
/// </summary>
public class MapSessionDatasetRenderEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="datasetId">The dataset identity being rendered.</param>
    /// <param name="kind">The operation that triggered the render.</param>
    public MapSessionDatasetRenderEventArgs(
        MapDatasetId datasetId,
        MapSessionRenderKind kind)
    {
        DatasetId = datasetId;
        Kind = kind;
    }

    /// <summary>The dataset identity being rendered.</summary>
    public MapDatasetId DatasetId { get; }

    /// <summary>The operation that triggered the render.</summary>
    public MapSessionRenderKind Kind { get; }
}
