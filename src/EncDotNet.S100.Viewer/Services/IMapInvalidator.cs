namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Requests a redraw of the live map graphics.
/// </summary>
internal interface IMapInvalidator
{
    /// <summary>
    /// Schedules a map redraw, marshalling to the UI thread when required.
    /// </summary>
    void RequestRedraw();
}
