using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Where a library dataset's data is — exactly one per dataset. This is what
/// the map's coverage outline shows, and the row swatch mirrors it, so the
/// list doubles as the map's legend. What is happening to a dataset (loaded,
/// on pan, update available, downloading…) is shown separately, as tags.
/// </summary>
internal enum LibraryPrimaryAvailability
{
    /// <summary>On disk (whether or not it is loaded, loading on pan, or outdated).</summary>
    Local,

    /// <summary>Downloadable.</summary>
    Online,

    /// <summary>Catalogue only: nothing to download.</summary>
    Listed,

    /// <summary>The referenced file has moved or been deleted.</summary>
    Missing,
}

/// <summary>A coverage outline's stroke: colour, width in pixels, dash pattern in pixels, and fill opacity.</summary>
internal sealed record LibraryOutlineStyle(Color Color, double Width, IReadOnlyList<float>? DashArray, float FillOpacity);

/// <summary>
/// The coverage outline styles shared by the map overlay
/// (<see cref="Services.LibraryCoverageOverlayController"/>) and the Library
/// panel's row swatches, so the two cannot drift apart.
/// </summary>
internal static class LibraryOutlineStyles
{
    public static LibraryOutlineStyle Local { get; } = new(Color.FromRgb(0x3d, 0x8a, 0x5a), 1.6, null, 0);

    public static LibraryOutlineStyle Online { get; } = new(Color.FromRgb(0x3f, 0x6f, 0xb5), 1.6, [6f, 4f], 0);

    public static LibraryOutlineStyle Listed { get; } = new(Color.FromRgb(0x80, 0x86, 0x90), 1.4, [1.5f, 3f], 0);

    public static LibraryOutlineStyle Missing { get; } = new(Color.FromRgb(0xc0, 0x50, 0x4d), 1.6, [4f, 3f], 0);

    /// <summary>The style for a primary availability.</summary>
    public static LibraryOutlineStyle For(LibraryPrimaryAvailability availability) => availability switch
    {
        LibraryPrimaryAvailability.Local => Local,
        LibraryPrimaryAvailability.Online => Online,
        LibraryPrimaryAvailability.Missing => Missing,
        _ => Listed,
    };

    /// <summary>The primary availability behind a detailed one.</summary>
    public static LibraryPrimaryAvailability Primary(LibraryAvailability availability) => availability switch
    {
        LibraryAvailability.Local or LibraryAvailability.Loaded
            or LibraryAvailability.Deferred or LibraryAvailability.Outdated => LibraryPrimaryAvailability.Local,
        LibraryAvailability.Online => LibraryPrimaryAvailability.Online,
        LibraryAvailability.Missing => LibraryPrimaryAvailability.Missing,
        _ => LibraryPrimaryAvailability.Listed,
    };
}
