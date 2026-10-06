namespace EncDotNet.S100.Collections.Library;

/// <summary>Where a collection item's data can be had right now.</summary>
public enum LibraryAvailability
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

    /// <summary>Downloaded, but the feed now lists a newer edition or update.</summary>
    Outdated,

    /// <summary>
    /// A downloaded forecast run whose valid window has ended, with no newer
    /// run known (#685); it still loads, for looking back.
    /// </summary>
    Expired,
}

/// <summary>Computes a <see cref="LibraryAvailability"/> from an item's location.</summary>
public static class LibraryAvailabilityResolver
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

/// <summary>
/// The wire names of <see cref="LibraryAvailability"/> states, as the MCP
/// Library tools report and filter them (<c>online</c>, <c>local</c>,
/// <c>on_pan</c>, <c>update</c>, …).
/// </summary>
public static class LibraryAvailabilityNames
{
    /// <summary>Every state's wire name.</summary>
    public static IReadOnlySet<string> All { get; } =
        Enum.GetValues<LibraryAvailability>().Select(Of).ToHashSet(StringComparer.Ordinal);

    /// <summary>The wire name of <paramref name="availability"/>.</summary>
    public static string Of(LibraryAvailability availability) => availability switch
    {
        LibraryAvailability.Deferred => "on_pan",
        LibraryAvailability.Outdated => "update",
        _ => availability.ToString().ToLowerInvariant(),
    };

    /// <summary>Parses a wire name (<see cref="Of"/>) back to its state.</summary>
    public static bool TryParse(string? name, out LibraryAvailability availability)
    {
        foreach (var value in Enum.GetValues<LibraryAvailability>())
        {
            if (string.Equals(Of(value), name, StringComparison.Ordinal))
            {
                availability = value;
                return true;
            }
        }

        availability = default;
        return false;
    }
}
