using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A fixed surface of a GRIB2 product (code table 4.5), for example the
/// ground (type 1), mean sea level (type 101) or 10 m above ground (type 103,
/// value 10).
/// </summary>
/// <param name="Type">The type of surface (code table 4.5); <c>255</c> means none.</param>
/// <param name="Value">
/// The surface's value in the type's unit (metres for type 103, pascals for an
/// isobaric surface), with the template's scale factor applied; or
/// <see langword="null"/> when the template marks it missing.
/// </param>
public readonly record struct GribFixedSurface(byte Type, double? Value)
{
    /// <summary>The ground or water surface.</summary>
    public const byte GroundOrWater = 1;

    /// <summary>An isobaric surface; the value is in pascals.</summary>
    public const byte Isobaric = 100;

    /// <summary>Mean sea level.</summary>
    public const byte MeanSeaLevel = 101;

    /// <summary>A height above ground; the value is in metres.</summary>
    public const byte HeightAboveGround = 103;

    /// <summary>No surface (missing).</summary>
    public const byte None = 255;

    /// <summary>Returns the surface as, for example, <c>"type 103 = 10"</c>.</summary>
    public override string ToString() => Value is { } value
        ? string.Create(CultureInfo.InvariantCulture, $"type {Type} = {value:0.###}")
        : string.Create(CultureInfo.InvariantCulture, $"type {Type}");
}
