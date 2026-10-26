namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The scanning mode flags of a GRIB2 grid (flag table 3.4): the order the
/// grid points are stored in. Bit 1 of the table is the most significant bit
/// of the octet.
/// </summary>
/// <remarks>
/// <see cref="None"/> (all bits clear) is the common case: rows run from
/// north to south, and the points in each row run from west to east. The
/// decoder returns values row by row in the grid's own row and column
/// directions (see <see cref="GribLatLonGrid.ToWeatherNativeGrid"/>), so it
/// undoes <see cref="JConsecutive"/> and <see cref="Boustrophedon"/> itself.
/// </remarks>
[Flags]
public enum GribScanningMode : byte
{
    /// <summary>Points run in the +i (east) direction, rows in the −j (south) direction, row by row.</summary>
    None = 0,

    /// <summary>Bit 1: points of a row run in the −i (west) direction.</summary>
    INegative = 0x80,

    /// <summary>Bit 2: rows run in the +j (north) direction.</summary>
    JPositive = 0x40,

    /// <summary>Bit 3: adjacent points in the j direction are consecutive (the values are stored column by column).</summary>
    JConsecutive = 0x20,

    /// <summary>Bit 4: every other row (or column, with <see cref="JConsecutive"/>) runs in the opposite direction.</summary>
    Boustrophedon = 0x10,

    /// <summary>
    /// Bits 5 to 8: offsets for staggered grids (odd rows or columns offset,
    /// or rows and columns of different lengths). Not supported.
    /// </summary>
    StaggeredMask = 0x0F,
}
