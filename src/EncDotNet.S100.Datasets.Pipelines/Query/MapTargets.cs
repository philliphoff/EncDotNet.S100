using EncDotNet.S100.Hosting;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// Resolves a tool's optional <c>map</c> argument against a capability
/// accessor (dual-map design D28): no map is the active map; a map id needs a
/// host with several maps (<see cref="IMapCapabilityAccessor{TCapability}"/>)
/// that knows it.
/// </summary>
public static class MapTargets
{
    /// <summary>
    /// Resolves <paramref name="map"/> to a capability from <paramref name="accessor"/>.
    /// </summary>
    /// <typeparam name="TCapability">The capability type.</typeparam>
    /// <param name="accessor">The host's accessor.</param>
    /// <param name="map">The map id, or <see langword="null"/> (or blank) for the active map.</param>
    /// <param name="capability">The map's capability, or <see langword="null"/> when it isn't available yet.</param>
    /// <returns><see cref="MapNotFound"/> when the id names no map; otherwise <see langword="null"/>.</returns>
    public static ToolError? TryResolve<TCapability>(
        ICapabilityAccessor<TCapability> accessor,
        string? map,
        out TCapability? capability)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (string.IsNullOrWhiteSpace(map))
        {
            capability = accessor.Current;
            return null;
        }

        if (accessor is not IMapCapabilityAccessor<TCapability> maps)
        {
            capability = null;
            return new MapNotFound(map, []);
        }

        var ids = maps.Maps;
        if (!ids.Contains(map, StringComparer.Ordinal))
        {
            capability = null;
            return new MapNotFound(map, ids);
        }

        capability = maps.Get(map);
        return null;
    }
}
