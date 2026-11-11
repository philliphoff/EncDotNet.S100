using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Creates an <see cref="IS100MapSession"/> for a runtime <see cref="Map"/>,
/// resolving its dependencies (CRS transform factory and options) from a DI
/// container. Register it with
/// <see cref="S100MapsuiServiceCollectionExtensions.AddS100Mapsui"/>.
/// </summary>
/// <remarks>
/// The returned session is owned by the caller, not the container: dispose it
/// when the map/window goes away.
/// </remarks>
public interface IS100MapSessionFactory
{
    /// <summary>
    /// Attaches a new S-100 session to <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The Mapsui map to attach to.</param>
    /// <returns>The owned, disposable S-100 session.</returns>
    IS100MapSession Create(Map map);
}
