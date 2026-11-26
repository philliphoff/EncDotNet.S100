namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// An <see cref="IMapCapabilityAccessor{TCapability}"/> for a capability every
/// map shares in step 1 of the split map (dual-map design §0), such as what the
/// maps draw (<see cref="EncDotNet.S100.Datasets.Pipelines.Query.IMapDrawState"/>):
/// every map's is the same value, so a tool's <c>map</c> argument only has to
/// name a map that exists.
/// </summary>
/// <typeparam name="TCapability">The capability type.</typeparam>
internal sealed class SharedMapCapabilityAccessor<TCapability> : IMapCapabilityAccessor<TCapability>
    where TCapability : class
{
    private readonly MapViews _views;
    private readonly TCapability _shared;

    /// <summary>Creates the accessor.</summary>
    /// <param name="views">The window's maps.</param>
    /// <param name="shared">The capability every map shares.</param>
    public SharedMapCapabilityAccessor(MapViews views, TCapability shared)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(shared);
        _views = views;
        _shared = shared;
    }

    /// <inheritdoc />
    public TCapability? Current => _shared;

    /// <inheritdoc />
    public IReadOnlyList<string> Maps => _views.MapIds();

    /// <inheritdoc />
    public TCapability? Get(string map) => _views.Find(map) is null ? null : _shared;
}
