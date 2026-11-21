using EncDotNet.S100.Collections;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Adds sources to the library interactively: picks a path (or online feed)
/// and confirms the target collection. Implemented by
/// <see cref="Services.LibraryImportCoordinator"/>.
/// </summary>
internal interface ILibraryImporter
{
    /// <summary>Picks a folder to scan for exchange sets and datasets.</summary>
    Task AddFolderAsync(Guid? targetCollectionId);

    /// <summary>Picks a zipped exchange set.</summary>
    Task AddExchangeSetZipAsync(Guid? targetCollectionId);

    /// <summary>
    /// Opens the directory of known online catalogues (issue #670), then the
    /// chosen catalogue's scope picker.
    /// </summary>
    Task AddOnlineCatalogueAsync(Guid? targetCollectionId);

    /// <summary>
    /// Asks for the URL of a feed served by <c>s100 feed serve</c> on another
    /// computer, checks it, then opens its product picker (issue #680).
    /// </summary>
    Task AddSharedFeedAsync(Guid? targetCollectionId);

    /// <summary>Opens the scope picker for a known online catalogue directly.</summary>
    Task AddKnownCatalogueAsync(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSource source, Guid? targetCollectionId);

    /// <summary>Picks an S-128 catalogue file.</summary>
    Task AddS128CatalogueAsync(Guid? targetCollectionId);

    /// <summary>Picks a collection manifest (<c>*.s100collection.json</c>) and opens its group picker.</summary>
    Task AddCollectionManifestAsync(Guid? targetCollectionId);

    /// <summary>Reopens a collection manifest's group picker to change an existing source's selection.</summary>
    Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source);

    /// <summary>
    /// Opens the S-111 surface-currents catalogue at its models step with the
    /// models covering <paramref name="area"/> ticked (#685, handoff B8).
    /// </summary>
    Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId) => Task.CompletedTask;

    /// <summary>Adds a known path (for example a dropped folder), confirming the target collection.</summary>
    Task AddPathAsync(string path, Guid? targetCollectionId);

    /// <summary>
    /// True when <paramref name="path"/> is already a library source or lies
    /// inside a folder source.
    /// </summary>
    bool IsInLibrary(string path);
}
