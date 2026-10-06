namespace EncDotNet.S100.Collections.Library;

/// <summary>Whether a library item is open in the host's session.</summary>
public enum LibraryLoadState
{
    /// <summary>Not opened from the library.</summary>
    None,

    /// <summary>Registered to load as it comes into view.</summary>
    Deferred,

    /// <summary>Loaded (on the map, or in the host's dataset catalog).</summary>
    Loaded,
}

/// <summary>
/// The downloaded copies of online library items, as far as an item's state
/// needs them: where a copy is, and whether it is behind the source.
/// </summary>
public interface ILibraryLocalCopies
{
    /// <summary>
    /// Returns <paramref name="item"/> with a local location when it is an
    /// online item that has been downloaded; otherwise the item itself.
    /// </summary>
    CollectionItem Localize(CollectionItem item);

    /// <summary>True when the downloaded copy of <paramref name="item"/> is an older edition or update.</summary>
    bool IsOutdated(CollectionItem item);

    /// <summary>The edition of <paramref name="item"/>'s downloaded copy, or <see langword="null"/> when not downloaded (or unknown).</summary>
    int? LocalEditionOf(CollectionItem item) => null;

    /// <summary>
    /// When <paramref name="item"/>'s downloaded copy was published (for a
    /// forecast, its run time), or <see langword="null"/> when not downloaded (or unknown).
    /// </summary>
    DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => null;
}
