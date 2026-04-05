namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Provides deferred access to portrayal asset file contents (SVGs, Lua scripts, XML line styles, etc.).
/// </summary>
public interface IPortrayalAssetProvider
{
    /// <summary>
    /// Fetches the content of a portrayal asset by its file name relative to the catalogue root.
    /// </summary>
    Task<Stream> FetchAssetAsync(string fileName, CancellationToken cancellationToken = default);
}
