using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An <see cref="IActiveMap"/> the test switches: <see cref="FakeViewportSet.First"/>
/// is active until <see cref="Activate"/> picks another map.
/// </summary>
internal sealed class FakeActiveMap : IActiveMap
{
    /// <inheritdoc />
    public MapViewId? ActiveId { get; private set; } = FakeViewportSet.First;

    /// <inheritdoc />
    public event EventHandler? ActiveChanged;

    /// <summary>Makes <paramref name="map"/> the active map and raises <see cref="ActiveChanged"/>.</summary>
    /// <param name="map">The map, or <see langword="null"/> for none.</param>
    public void Activate(MapViewId? map)
    {
        ActiveId = map;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
