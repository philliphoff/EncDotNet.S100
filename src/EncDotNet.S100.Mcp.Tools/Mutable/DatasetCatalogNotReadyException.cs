namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Thrown by an <see cref="IMutableDatasetCatalog.LoadAsync"/> implementation
/// whose host load path is not yet initialised (e.g. the desktop viewer's
/// dataset loader before the main window has wired it up).
/// </summary>
/// <remarks>
/// The shared <c>open_dataset</c> tool catches this and surfaces a
/// <see cref="EncDotNet.S100.Datasets.Pipelines.Query.HostNotReady"/> error, so
/// a caller sees a clean, retryable <c>host_not_ready</c> instead of an internal
/// error. A catalog whose load path is always available (the headless CLI
/// session) never throws it.
/// </remarks>
public sealed class DatasetCatalogNotReadyException : Exception
{
    /// <summary>Creates the exception with a human-readable reason.</summary>
    /// <param name="message">
    /// Names the subsystem that is not ready; surfaced verbatim as the
    /// <c>host_not_ready</c> reason.
    /// </param>
    public DatasetCatalogNotReadyException(string message)
        : base(message)
    {
    }
}
