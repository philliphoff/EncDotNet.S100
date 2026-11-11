namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Classifies the operation that triggered a <see cref="MapsuiDatasetLayerSession"/>
/// dataset render, carried by its lifecycle events.
/// </summary>
public enum MapSessionRenderKind
{
    /// <summary>A single-dataset render (<see cref="MapsuiDatasetLayerSession.RenderAsync"/>).</summary>
    Render,

    /// <summary>A coalesced, time-gated refresh (<see cref="MapsuiDatasetLayerSession.RefreshTimeAsync"/>).</summary>
    TimeRefresh,

    /// <summary>A full presentation refresh (<see cref="MapsuiDatasetLayerSession.RefreshAsync"/>).</summary>
    PresentationRefresh,
}
