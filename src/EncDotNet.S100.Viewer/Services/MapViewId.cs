namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The stable, opaque id of a <see cref="MapView"/> (dual-map design note §2.1),
/// such as <c>map-1</c>. It names a map for MCP and settings; it isn't a
/// position ("left", "secondary") and isn't the number shown to the user.
/// </summary>
/// <param name="Value">The id text.</param>
internal readonly record struct MapViewId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
