using EncDotNet.S100.Collections;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>Where a collection item's data can be had right now.</summary>
internal enum LibraryAvailability
{
    /// <summary>Catalogue-only: the source describes the product but has no data for it.</summary>
    Listed,

    /// <summary>Available for download.</summary>
    Online,

    /// <summary>On disk and ready to load.</summary>
    Local,

    /// <summary>The referenced file has moved or been deleted.</summary>
    Missing,

    /// <summary>Opened from the library, loading as it comes into view.</summary>
    Deferred,

    /// <summary>Loaded on the map.</summary>
    Loaded,
}

/// <summary>Computes a <see cref="LibraryAvailability"/> from an item's location.</summary>
internal static class LibraryAvailabilityResolver
{
    /// <summary>
    /// Resolves <paramref name="item"/>'s availability, checking the file
    /// system for local items.
    /// </summary>
    public static LibraryAvailability Resolve(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Location switch
        {
            RemoteItemLocation => LibraryAvailability.Online,
            LocalItemLocation local => Exists(local) ? LibraryAvailability.Local : LibraryAvailability.Missing,
            _ => LibraryAvailability.Listed,
        };
    }

    /// <summary>The absolute path of a local item's base file (or its ZIP).</summary>
    public static string ResolvePath(LocalItemLocation location) =>
        location.IsZip
            ? location.RootPath
            : Path.GetFullPath(Path.Combine(location.RootPath, location.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static bool Exists(LocalItemLocation location)
    {
        try
        {
            return File.Exists(ResolvePath(location));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
