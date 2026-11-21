namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Whether AML Contour Line Bathymetry (CLB) is drawn on the map: loaded,
/// shown and drawn by the AML portrayal.
/// </summary>
/// <remarks>
/// <para>
/// While it is, the ENC safety contour gains a halo so it stays the
/// strongest line under CLB's many contours (AML design handoff decision 22):
/// <see cref="MapPresentationStateProjection"/> puts it in every presentation
/// snapshot, and <see cref="ViewerPresentationCoordinator"/> re-draws the
/// depth layers when it changes. The map also shows the corner chip
/// "AML · CLB not used for depth checks" (decision 23). The viewer's
/// presence is <see cref="AmlDisplayService"/>, so a CLB cell the AML product
/// switches or scale bands hide doesn't count.
/// </para>
/// <para>
/// It says nothing about depths: AML is never a source of depth answers or
/// route checks, drawn or not.
/// </para>
/// </remarks>
internal interface IAmlClbPresence
{
    /// <summary>Whether any CLB cell is loaded, shown and drawn.</summary>
    bool IsClbDrawn { get; }

    /// <summary>Raised on the UI thread when <see cref="IsClbDrawn"/> changes.</summary>
    event Action? Changed;
}
