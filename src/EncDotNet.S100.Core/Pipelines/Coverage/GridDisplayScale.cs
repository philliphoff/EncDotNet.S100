namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Derives a gridded coverage's minimum display scale (the scale beyond which
/// it is hidden as you zoom out) from its grid spacing, for datasets that
/// declare none.
/// </summary>
/// <remarks>
/// A grid drawn with many nodes behind each screen pixel shows nothing the
/// mariner can read, yet every such dataset still costs a raster draw per
/// frame; hundreds of them made zoomed-out panning lag. The cutoff is where one
/// nominal screen pixel (0.28 mm, as for S-101 display scales) covers
/// <see cref="NodesPerPixel"/> grid nodes. A display scale the dataset or its
/// catalogue declares takes precedence.
/// </remarks>
public static class GridDisplayScale
{
    /// <summary>Grid nodes behind one screen pixel at the cutoff.</summary>
    public const double NodesPerPixel = 4;

    /// <summary>Nominal screen pixel pitch in metres (0.28 mm).</summary>
    public const double PixelSizeMetres = 0.00028;

    /// <summary>Metres per degree of latitude (mean).</summary>
    public const double MetresPerDegreeLatitude = 111_320;

    /// <summary>
    /// The minimum display scale denominator for a grid whose node spacing is
    /// <paramref name="spacingMetres"/>, or <see langword="null"/> when the
    /// spacing is not a positive finite number.
    /// </summary>
    /// <param name="spacingMetres">The grid's finest node spacing, in metres.</param>
    public static int? FromSpacing(double spacingMetres)
    {
        if (!double.IsFinite(spacingMetres) || spacingMetres <= 0)
            return null;

        var denominator = spacingMetres * NodesPerPixel / PixelSizeMetres;
        return denominator >= int.MaxValue ? int.MaxValue : (int)Math.Round(denominator);
    }

    /// <summary>
    /// The minimum display scale denominator for <paramref name="grid"/>,
    /// using its finer axis spacing. A geographic grid's spacing is in degrees
    /// (latitude spacing converts directly; longitude spacing shrinks with
    /// latitude, so the latitude axis alone decides); a projected grid's is in
    /// metres.
    /// </summary>
    /// <param name="grid">The full-resolution grid.</param>
    /// <param name="geographic">True when the grid's CRS is geographic (degrees).</param>
    public static int? FromGrid(GridMetadata grid, bool geographic)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var spacing = geographic
            ? Math.Abs(grid.SpacingLatitudinal) * MetresPerDegreeLatitude
            : Math.Min(Math.Abs(grid.SpacingLatitudinal), Math.Abs(grid.SpacingLongitudinal));
        return FromSpacing(spacing);
    }
}
