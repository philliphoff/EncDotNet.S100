namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Classifies the operation that triggered a <see cref="MapsuiMapSession"/>
/// dataset render, carried by its lifecycle events.
/// </summary>
public enum MapSessionRenderKind
{
    /// <summary>A single-dataset render (<see cref="MapsuiMapSession.RenderAsync"/>).</summary>
    Render,

    /// <summary>A coalesced, time-gated refresh (<see cref="MapsuiMapSession.RefreshTimeAsync"/>).</summary>
    TimeRefresh,

    /// <summary>A full presentation refresh (<see cref="MapsuiMapSession.RefreshAsync"/>).</summary>
    PresentationRefresh,
}
