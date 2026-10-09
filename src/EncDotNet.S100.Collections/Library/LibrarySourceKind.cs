using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Library;

/// <summary>What kind of source is being added to the Library (#792).</summary>
public enum LibrarySourceKind
{
    /// <summary>A local folder (scanned recursively).</summary>
    Folder,

    /// <summary>A single exchange set: folder, ZIP or catalogue file.</summary>
    ExchangeSet,

    /// <summary>An S-128 catalogue file.</summary>
    S128Catalogue,

    /// <summary>A scope of the NOAA ENC product catalogue feed.</summary>
    NoaaFeed,

    /// <summary>A scope of the USACE Inland ENC product catalogue feed (issue #670).</summary>
    UsaceFeed,

    /// <summary>Some or all entries of a community chart list (<c>chartcatalogs</c> format; issue #670).</summary>
    CommunityFeed,

    /// <summary>Some or all products of an S-100 feed, e.g. one served by <c>s100 feed serve</c> (issue #680).</summary>
    S100Feed,

    /// <summary>Some or all groups of a local collection manifest (<c>*.s100collection.json</c>).</summary>
    LocalManifest,

    /// <summary>Some regions and areas of a remote S-100 exchange catalogue, e.g. NOAA's S-102 on AWS (issue #685).</summary>
    S100Catalogue,

    /// <summary>Some models of an S-100 forecast feed, e.g. NOAA's S-111 on AWS (issue #685).</summary>
    S100Forecast,

    /// <summary>Some or all products of a SECOM service, read anonymously (issue #804).</summary>
    Secom,
}

/// <summary>Recognises what kind of Library source a known catalogue or a local path is (#792).</summary>
public static class LibrarySourceKinds
{
    /// <summary>The kind a known online catalogue adds as.</summary>
    /// <param name="format">The catalogue's format.</param>
    public static LibrarySourceKind Of(KnownCatalogueFormat format) => format switch
    {
        KnownCatalogueFormat.UsaceIenc => LibrarySourceKind.UsaceFeed,
        KnownCatalogueFormat.ChartCatalogs => LibrarySourceKind.CommunityFeed,
        KnownCatalogueFormat.S100Feed => LibrarySourceKind.S100Feed,
        KnownCatalogueFormat.S100ExchangeCatalogue => LibrarySourceKind.S100Catalogue,
        KnownCatalogueFormat.S100ForecastModels => LibrarySourceKind.S100Forecast,
        KnownCatalogueFormat.Secom => LibrarySourceKind.Secom,
        _ => LibrarySourceKind.NoaaFeed,
    };

    /// <summary>
    /// What adding <paramref name="path"/> means: a folder with a catalogue is
    /// an exchange set, any other folder is scanned; a collection manifest is
    /// one (by its <c>.s100collection.json</c> name or its <c>format</c>); any
    /// other file is an exchange set (a ZIP or catalogue).
    /// </summary>
    /// <param name="path">A local folder or file.</param>
    public static LibrarySourceKind Classify(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Directory.Exists(path)
            ? (ExchangeSetLayout.PickS100Catalogue(SafeFileNames(path)) is not null
               || SafeFileNames(path).Any(ExchangeSetLayout.IsS57CatalogueName)
                ? LibrarySourceKind.ExchangeSet
                : LibrarySourceKind.Folder)
            : CollectionManifest.IsManifestPath(path)
                ? LibrarySourceKind.LocalManifest
                : LibrarySourceKind.ExchangeSet;
    }

    /// <summary>True for the kinds read from an online catalogue or service.</summary>
    /// <param name="kind">The kind.</param>
    public static bool IsOnline(LibrarySourceKind kind) => kind is LibrarySourceKind.NoaaFeed or LibrarySourceKind.UsaceFeed
        or LibrarySourceKind.CommunityFeed or LibrarySourceKind.S100Feed or LibrarySourceKind.S100Catalogue
        or LibrarySourceKind.S100Forecast or LibrarySourceKind.Secom;

    private static IEnumerable<string?> SafeFileNames(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Select(Path.GetFileName).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
