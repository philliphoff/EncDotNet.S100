namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Late-bound accessor for the viewer's <see cref="IMapHost"/>.
/// </summary>
/// <remarks>
/// <para>
/// The map host is constructed by <c>MainWindow</c> after the Avalonia
/// <see cref="Mapsui.UI.Avalonia.MapControl"/> exists, which happens
/// after DI is built and after singletons like
/// <see cref="McpServerHost"/> have already been resolved. This
/// accessor bridges that ordering: services that need the host (for
/// snapshotting the current map, etc.) hold the accessor and read
/// <see cref="Current"/> at invocation time.
/// </para>
/// <para>
/// Implementations are expected to be thread-safe — readers may run
/// off the UI thread (e.g. MCP request handlers); the host itself is
/// responsible for marshalling.
/// </para>
/// </remarks>
internal interface IMapHostAccessor
{
    /// <summary>The current map host, or <see langword="null"/> when not yet attached.</summary>
    IMapHost? Current { get; set; }
}

/// <summary>Default in-memory implementation of <see cref="IMapHostAccessor"/>.</summary>
internal sealed class MapHostAccessor : IMapHostAccessor
{
    public IMapHost? Current { get; set; }
}
