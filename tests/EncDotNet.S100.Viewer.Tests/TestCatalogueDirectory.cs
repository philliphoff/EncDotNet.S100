using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds the Online Catalogue directory as the app does (#1399): over an
/// add-by-URL workflow and a SECOM service directory, with stubs for the URL
/// probe, the user's catalogues, the registry and the browser where a test
/// doesn't care about them.
/// </summary>
internal static class TestCatalogueDirectory
{
    /// <summary>A directory listing <paramref name="sources"/>.</summary>
    /// <param name="sources">The curated sources.</param>
    /// <param name="probe">Recognises a URL; by default nothing is recognised.</param>
    /// <param name="store">The user's catalogues; by default an empty one that isn't saved.</param>
    /// <param name="registry">The SECOM registry; by default one that lists nothing.</param>
    /// <param name="openUrl">Sees the URLs opened in the browser.</param>
    /// <param name="session">The app session; by default a new one.</param>
    public static CatalogueDirectoryDialogViewModel Create(
        IReadOnlyList<KnownCatalogueSource> sources,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null,
        UserCatalogueStore? store = null,
        ISecomRegistry? registry = null,
        Action<Uri>? openUrl = null,
        CatalogueDirectorySession? session = null) =>
        new(
            sources,
            new UrlOpener(openUrl ?? (_ => { })),
            AddByUrl(probe, store),
            new SecomServiceDirectory(registry ?? new FakeSecomRegistry()),
            session ?? new CatalogueDirectorySession());

    /// <summary>The add-by-URL workflow over <paramref name="probe"/> and <paramref name="store"/>.</summary>
    public static AddCatalogueByUrlWorkflow AddByUrl(
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null, UserCatalogueStore? store = null) =>
        new(
            new CatalogueUrlRecognizer(probe ?? ((_, _) => Task.FromResult(new CatalogueProbe(null, null, null)))),
            store ?? EmptyStore());

    /// <summary>A store of the user's catalogues that starts empty and is never saved.</summary>
    public static UserCatalogueStore EmptyStore() =>
        new(Path.Combine(Path.GetTempPath(), "s100-tests-" + Guid.NewGuid().ToString("N"), "catalogues.json"), readOnly: true);

    private sealed class UrlOpener(Action<Uri> open) : IUrlOpener
    {
        public void Open(string url) => open(new Uri(url));
    }
}

/// <summary>A SECOM registry that lists and probes as a test says.</summary>
/// <param name="listing">Lists services, given whether to read past the cache; by default nothing.</param>
/// <param name="probe">Probes a service; by default every one is open.</param>
internal sealed class FakeSecomRegistry(
    Func<bool, SecomRegistryListing>? listing = null,
    Func<Uri, SecomProbeResult>? probe = null) : ISecomRegistry
{
    public Task<SecomRegistryListing> GetServicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult((listing ?? (_ => new SecomRegistryListing([], 0, 0, DateTimeOffset.UtcNow)))(forceRefresh));

    public Task<SecomProbeResult> ProbeAsync(Uri serviceUri, string? identity = null, CancellationToken cancellationToken = default) =>
        Task.FromResult((probe ?? (_ => new SecomProbeResult(SecomReachability.Open)))(serviceUri));
}
