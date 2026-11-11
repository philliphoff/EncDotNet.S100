namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// One feature an MCP <c>pick_features</c> call resolved and asked the
/// viewer to display, identified by the same keys the user-pick path uses:
/// the owning dataset's display name (equal to the catalog
/// <see cref="EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetId"/>) and the
/// feature reference (the feature's <c>gml:id</c> / RCID).
/// </summary>
/// <param name="DatasetDisplayName">Display name of the owning dataset.</param>
/// <param name="FeatureRef">Feature reference within that dataset.</param>
internal readonly record struct GeographicPickFeature(string DatasetDisplayName, string FeatureRef);

/// <summary>
/// UI-thread presenter that publishes an agent-driven (MCP) pick into the
/// viewer's <see cref="ViewModels.PickReportViewModel"/> — the same view
/// model a user click updates — so the on-screen Object Information panel
/// and the pick highlight reflect what an MCP agent picked.
/// </summary>
/// <remarks>
/// Kept as a narrow abstraction (rather than handing the MCP tool an
/// <see cref="IPickService"/> directly) so the tool stays free of any
/// UI-thread-marshalling concern and remains unit-testable without
/// Avalonia.
/// </remarks>
internal interface IGeographicPickPresenter
{
    /// <summary>
    /// Publishes the supplied geographic pick. Implementations marshal to
    /// the UI thread as needed. An empty <paramref name="features"/> list
    /// clears the current pick.
    /// </summary>
    /// <param name="latitude">Pick latitude in WGS-84 decimal degrees.</param>
    /// <param name="longitude">Pick longitude in WGS-84 decimal degrees.</param>
    /// <param name="features">Resolved features under the pick, most-specific first.</param>
    void Present(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features);
}
