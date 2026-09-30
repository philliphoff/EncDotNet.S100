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
internal sealed record LibraryOutlineStyle(Color Color, double Width, IReadOnlyList<float>? DashArray, float FillOpacity)
{
    /// <summary>
    /// True for round caps: a pattern starting with a zero-length dash draws
    /// true dots. Every other line uses butt caps, since round caps add the
    /// line width to each dash.
    /// </summary>
    public bool RoundCap => DashArray is [0f, ..];
}

/// <summary>
/// The coverage outline styles shared by the map overlay
/// (<see cref="Services.LibraryCoverageOverlayController"/>) and the Library
/// panel's row swatches, so the two cannot drift apart.
/// </summary>
internal static class LibraryOutlineStyles
{
    public static LibraryOutlineStyle Local { get; } = new(Color.FromRgb(0x3d, 0x8a, 0x5a), 1.0, null, 0);

    public static LibraryOutlineStyle Online { get; } = new(Color.FromRgb(0x3f, 0x6f, 0xb5), 1.0, [5f, 3f], 0);

    public static LibraryOutlineStyle Listed { get; } = new(Color.FromRgb(0x80, 0x86, 0x90), 1.2, [0f, 3f], 0);

    public static LibraryOutlineStyle Missing { get; } = new(Color.FromRgb(0xc0, 0x50, 0x4d), 1.2, [3f, 2f], 0);

    /// <summary>The selected dataset's line width (drawn in the accent colour).</summary>
    public const double SelectedWidth = 2.0;

    /// <summary>The selected dataset's fill opacity.</summary>
    public const float SelectedFillOpacity = 0.06f;

    /// <summary>How much wider than its line an outline's casing is.</summary>
    public const double CasingExtraWidth = 2.0;

    /// <summary>The casing's opacity (it is drawn in the chart's background colour).</summary>
    public const float CasingOpacity = 0.6f;

    /// <summary>An outline's line opacity.</summary>
    public const float LineOpacity = 0.85f;

    /// <summary>The other outlines' line opacity while a dataset is selected.</summary>
    public const float DimmedLineOpacity = 0.5f;

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
