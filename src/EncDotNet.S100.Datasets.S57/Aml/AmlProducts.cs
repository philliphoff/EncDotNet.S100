using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The AML products' codes and names, in SoundCharts' words.
/// </summary>
public static class AmlProducts
{
    /// <summary>The lowest AML product specification code (<c>PRSP</c>), 51 (MFF).</summary>
    public const int FirstProductSpecification = 51;

    /// <summary>The highest AML product specification code (<c>PRSP</c>), 57 (the AML 3.0 integrated product).</summary>
    public const int LastProductSpecification = 57;

    /// <summary>The name of the AML 3.0 integrated product.</summary>
    public const string IntegratedName = "AML 3.0 integrated product";

    /// <summary>Whether an S-57 <c>PRSP</c> code is an AML product (51–57).</summary>
    /// <param name="productSpecification">The <c>DSID</c> <c>PRSP</c> code.</param>
    public static bool IsAmlProductSpecification(int productSpecification) =>
        productSpecification is >= FirstProductSpecification and <= LastProductSpecification;

    /// <summary>
    /// The three-letter code of an AML product (<c>CLB</c>, …), or
    /// <see langword="null"/> for the integrated product, which has none.
    /// </summary>
    /// <param name="product">The product.</param>
    public static string? Code(AmlProduct product) => product switch
    {
        AmlProduct.MFF => "MFF",
        AmlProduct.RAL => "RAL",
        AmlProduct.LBO => "LBO",
        AmlProduct.CLB => "CLB",
        AmlProduct.ESB => "ESB",
        AmlProduct.SBO => "SBO",
        _ => null,
    };

    /// <summary>
    /// The full name of an AML product code, such as "Contour Line Bathymetry"
    /// for <c>CLB</c>, or <see langword="null"/> for a code that isn't one of
    /// the six AML 1.0/2.1 products.
    /// </summary>
    /// <param name="code">The three-letter product code.</param>
    public static string? Name(string? code) => code?.ToUpperInvariant() switch
    {
        "MFF" => "Maritime Foundation and Facilities",
        "RAL" => "Routes, Areas and Limits",
        "LBO" => "Large Bottom Objects",
        "CLB" => "Contour Line Bathymetry",
        "ESB" => "Environment, Seabed and Beach",
        "SBO" => "Small Bottom Objects",
        _ => null,
    };
}

/// <summary>
/// The AML scale bands (AML Implementation Guidance Document, Table 4): each
/// band's compilation scale and the display range it's meant for.
/// </summary>
public static class AmlScaleBands
{
    /// <summary>
    /// The band's display range as scale denominators: the coarsest
    /// (<see cref="AmlScaleBandRange.Minimum"/>) and finest
    /// (<see cref="AmlScaleBandRange.Maximum"/>) scale, either
    /// <see langword="null"/> when open.
    /// </summary>
    /// <param name="band">The scale band, 0–9.</param>
    /// <returns>The range, or <see langword="null"/> for a band outside 0–9.</returns>
    public static AmlScaleBandRange? DisplayRange(int band) => band switch
    {
        0 => new AmlScaleBandRange(null, null, null),
        1 => new AmlScaleBandRange(100_000_000, null, 40_000_000),
        2 => new AmlScaleBandRange(25_000_000, 62_500_000, 10_000_000),
        3 => new AmlScaleBandRange(5_000_000, 12_500_000, 2_000_000),
        4 => new AmlScaleBandRange(1_000_000, 2_500_000, 400_000),
        5 => new AmlScaleBandRange(250_000, 625_000, 100_000),
        6 => new AmlScaleBandRange(50_000, 125_000, 20_000),
        7 => new AmlScaleBandRange(10_000, 25_000, 4_000),
        8 => new AmlScaleBandRange(2_500, 6_250, 1_000),
        9 => new AmlScaleBandRange(1_600, 1_500, null),
        _ => null,
    };

    /// <summary>
    /// The scale band an S-57 intended usage (<c>INTU</c>) gives: AML cells
    /// encode band <c>n</c> as <c>100 + n</c>.
    /// </summary>
    /// <param name="intendedUsage">The <c>DSID</c> <c>INTU</c> value.</param>
    /// <returns>The band, or <see langword="null"/> when <paramref name="intendedUsage"/> isn't 100–109.</returns>
    public static int? FromIntendedUsage(int intendedUsage) =>
        intendedUsage is >= 100 and <= 109 ? intendedUsage - 100 : null;
}

/// <summary>An AML scale band's compilation scale and display range, as scale denominators.</summary>
/// <param name="CompilationScale">The band's compilation scale, or <see langword="null"/> for band 0 (unscaled).</param>
/// <param name="Minimum">The coarsest scale the band is shown at, or <see langword="null"/> for no limit.</param>
/// <param name="Maximum">The finest scale the band is shown at, or <see langword="null"/> for no limit.</param>
public sealed record AmlScaleBandRange(int? CompilationScale, int? Minimum, int? Maximum);
