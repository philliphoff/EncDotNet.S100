namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default in-memory, settable implementation of
/// <see cref="ICapabilityAccessor{TCapability}"/> for a map capability that is
/// attached after container composition (the map surface is created by the UI).
/// </summary>
/// <remarks>
/// <see cref="Current"/> is the active map's capability. Once
/// <see cref="MapViews"/> binds the accessor (<see cref="BindMaps"/>), it's also
/// an <see cref="IMapCapabilityAccessor{TCapability}"/> over every map, so an
/// MCP tool's <c>map</c> argument reaches any pane's (split map D28, #1735).
/// </remarks>
/// <typeparam name="TCapability">The focused map capability type.</typeparam>
internal sealed class MapCapabilityAccessor<TCapability> : IMapCapabilityAccessor<TCapability>
    where TCapability : class
{
    private Func<IReadOnlyList<string>>? _maps;
    private Func<string, TCapability?>? _get;

    /// <inheritdoc />
    public TCapability? Current { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string> Maps => _maps?.Invoke() ?? [];

    /// <inheritdoc />
    public TCapability? Get(string map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _get?.Invoke(map);
    }

    /// <summary>Reaches every map through <paramref name="maps"/> and <paramref name="get"/>.</summary>
    /// <param name="maps">The map ids, in reading order.</param>
    /// <param name="get">A map's capability by id, or <see langword="null"/>.</param>
    public void BindMaps(Func<IReadOnlyList<string>> maps, Func<string, TCapability?> get)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(get);
        _maps = maps;
        _get = get;
    }
}
