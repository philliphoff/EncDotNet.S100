using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// The IHO S-100 <c>verticalDatum</c> enumeration (the reference level used
/// for vertical measurements such as sounding reduction and heights). The
/// codes are the shared S-100 register values (source identifier 996) carried
/// by the <c>verticalDatum</c> attribute of HDF5-encoded coverage products
/// (S-102, S-104, S-111) and by GML-encoded feature products.
/// </summary>
/// <remarks>
/// Only the codes defined by the S-100 register are listed; the enumeration is
/// intentionally sparse (there is no code 42 in the register). Use
/// <see cref="VerticalDatums.GetLabel(int?)"/> to obtain a human-readable
/// label without pattern-matching on individual members.
/// </remarks>
public enum VerticalDatum
{
    /// <summary>Mean Low Water Springs (S-100 code 1).</summary>
    MeanLowWaterSprings = 1,

    /// <summary>Mean Lower Low Water Springs (S-100 code 2).</summary>
    MeanLowerLowWaterSprings = 2,

    /// <summary>Mean Sea Level (S-100 code 3).</summary>
    MeanSeaLevel = 3,

    /// <summary>Lowest Low Water (S-100 code 4).</summary>
    LowestLowWater = 4,

    /// <summary>Mean Low Water (S-100 code 5).</summary>
    MeanLowWater = 5,

    /// <summary>Lowest Low Water Springs (S-100 code 6).</summary>
    LowestLowWaterSprings = 6,

    /// <summary>Approximate Mean Low Water Springs (S-100 code 7).</summary>
    ApproximateMeanLowWaterSprings = 7,

    /// <summary>Indian Spring Low Water (S-100 code 8).</summary>
    IndianSpringLowWater = 8,

    /// <summary>Low Water Springs (S-100 code 9).</summary>
    LowWaterSprings = 9,

    /// <summary>Approximate Lowest Astronomical Tide (S-100 code 10).</summary>
    ApproximateLowestAstronomicalTide = 10,

    /// <summary>Nearly Lowest Low Water (S-100 code 11).</summary>
    NearlyLowestLowWater = 11,

    /// <summary>Mean Lower Low Water (S-100 code 12).</summary>
    MeanLowerLowWater = 12,

    /// <summary>Low Water (S-100 code 13).</summary>
    LowWater = 13,

    /// <summary>Approximate Mean Low Water (S-100 code 14).</summary>
    ApproximateMeanLowWater = 14,

    /// <summary>Approximate Mean Lower Low Water (S-100 code 15).</summary>
    ApproximateMeanLowerLowWater = 15,

    /// <summary>Mean High Water (S-100 code 16).</summary>
    MeanHighWater = 16,

    /// <summary>Mean High Water Springs (S-100 code 17).</summary>
    MeanHighWaterSprings = 17,

    /// <summary>High Water (S-100 code 18).</summary>
    HighWater = 18,

    /// <summary>Approximate Mean Sea Level (S-100 code 19).</summary>
    ApproximateMeanSeaLevel = 19,

    /// <summary>High Water Springs (S-100 code 20).</summary>
    HighWaterSprings = 20,

    /// <summary>Mean Higher High Water (S-100 code 21).</summary>
    MeanHigherHighWater = 21,

    /// <summary>Equinoctial Spring Low Water (S-100 code 22).</summary>
    EquinoctialSpringLowWater = 22,

    /// <summary>Lowest Astronomical Tide (S-100 code 23).</summary>
    LowestAstronomicalTide = 23,

    /// <summary>Local Datum (S-100 code 24).</summary>
    LocalDatum = 24,

    /// <summary>International Great Lakes Datum 1985 (S-100 code 25).</summary>
    InternationalGreatLakesDatum1985 = 25,

    /// <summary>Mean Water Level (S-100 code 26).</summary>
    MeanWaterLevel = 26,

    /// <summary>Lower Low Water Large Tide (S-100 code 27).</summary>
    LowerLowWaterLargeTide = 27,

    /// <summary>Higher High Water Large Tide (S-100 code 28).</summary>
    HigherHighWaterLargeTide = 28,

    /// <summary>Nearly Highest High Water (S-100 code 29).</summary>
    NearlyHighestHighWater = 29,

    /// <summary>Highest Astronomical Tide (S-100 code 30).</summary>
    HighestAstronomicalTide = 30,

    /// <summary>Local Low Water Reference Level (S-100 code 31).</summary>
    LocalLowWaterReferenceLevel = 31,

    /// <summary>Local High Water Reference Level (S-100 code 32).</summary>
    LocalHighWaterReferenceLevel = 32,

    /// <summary>Local Mean Water Reference Level (S-100 code 33).</summary>
    LocalMeanWaterReferenceLevel = 33,

    /// <summary>Equivalent Height of Water (German GlW) (S-100 code 34).</summary>
    EquivalentHeightOfWaterGermanGlW = 34,

    /// <summary>Highest Shipping Height of Water (German HSW) (S-100 code 35).</summary>
    HighestShippingHeightOfWaterGermanHSW = 35,

    /// <summary>Reference Low Water Level According to Danube Commission (S-100 code 36).</summary>
    ReferenceLowWaterLevelDanubeCommission = 36,

    /// <summary>Highest Shipping Height of Water According to Danube Commission (S-100 code 37).</summary>
    HighestShippingHeightOfWaterDanubeCommission = 37,

    /// <summary>Dutch River Low Water Reference Level (OLR) (S-100 code 38).</summary>
    DutchRiverLowWaterReferenceLevel = 38,

    /// <summary>Russian Project Water Level (S-100 code 39).</summary>
    RussianProjectWaterLevel = 39,

    /// <summary>Russian Normal Backwater Level (S-100 code 40).</summary>
    RussianNormalBackwaterLevel = 40,

    /// <summary>Ohio River Datum (S-100 code 41).</summary>
    OhioRiverDatum = 41,

    /// <summary>Dutch High Water Reference Level (S-100 code 43).</summary>
    DutchHighWaterReferenceLevel = 43,

    /// <summary>Baltic Sea Chart Datum 2000 (S-100 code 44).</summary>
    BalticSeaChartDatum2000 = 44,

    /// <summary>Dutch Estuary Low Water Reference Level (OLW) (S-100 code 45).</summary>
    DutchEstuaryLowWaterReferenceLevel = 45,

    /// <summary>International Great Lakes Datum 2020 (S-100 code 46).</summary>
    InternationalGreatLakesDatum2020 = 46,

    /// <summary>Sea Floor (S-100 code 47).</summary>
    SeaFloor = 47,

    /// <summary>Sea Surface (S-100 code 48).</summary>
    SeaSurface = 48,

    /// <summary>Hydrographic Zero (S-100 code 49).</summary>
    HydrographicZero = 49,
}

/// <summary>
/// Helpers for resolving IHO S-100 <see cref="VerticalDatum"/> codes to
/// human-readable labels.
/// </summary>
public static class VerticalDatums
{
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [1] = "Mean Low Water Springs",
        [2] = "Mean Lower Low Water Springs",
        [3] = "Mean Sea Level",
        [4] = "Lowest Low Water",
        [5] = "Mean Low Water",
        [6] = "Lowest Low Water Springs",
        [7] = "Approximate Mean Low Water Springs",
        [8] = "Indian Spring Low Water",
        [9] = "Low Water Springs",
        [10] = "Approximate Lowest Astronomical Tide",
        [11] = "Nearly Lowest Low Water",
        [12] = "Mean Lower Low Water",
        [13] = "Low Water",
        [14] = "Approximate Mean Low Water",
        [15] = "Approximate Mean Lower Low Water",
        [16] = "Mean High Water",
        [17] = "Mean High Water Springs",
        [18] = "High Water",
        [19] = "Approximate Mean Sea Level",
        [20] = "High Water Springs",
        [21] = "Mean Higher High Water",
        [22] = "Equinoctial Spring Low Water",
        [23] = "Lowest Astronomical Tide",
        [24] = "Local Datum",
        [25] = "International Great Lakes Datum 1985",
        [26] = "Mean Water Level",
        [27] = "Lower Low Water Large Tide",
        [28] = "Higher High Water Large Tide",
        [29] = "Nearly Highest High Water",
        [30] = "Highest Astronomical Tide",
        [31] = "Local Low Water Reference Level",
        [32] = "Local High Water Reference Level",
        [33] = "Local Mean Water Reference Level",
        [34] = "Equivalent Height of Water (German GlW)",
        [35] = "Highest Shipping Height of Water (German HSW)",
        [36] = "Reference Low Water Level According to Danube Commission",
        [37] = "Highest Shipping Height of Water According to Danube Commission",
        [38] = "Dutch River Low Water Reference Level (OLR)",
        [39] = "Russian Project Water Level",
        [40] = "Russian Normal Backwater Level",
        [41] = "Ohio River Datum",
        [43] = "Dutch High Water Reference Level",
        [44] = "Baltic Sea Chart Datum 2000",
        [45] = "Dutch Estuary Low Water Reference Level (OLW)",
        [46] = "International Great Lakes Datum 2020",
        [47] = "Sea Floor",
        [48] = "Sea Surface",
        [49] = "Hydrographic Zero",
    };

    /// <summary>
    /// Attempts to resolve the S-100 register label for a vertical datum code.
    /// </summary>
    /// <param name="code">The S-100 <c>verticalDatum</c> code.</param>
    /// <param name="label">The resolved label when the code is recognised.</param>
    /// <returns><c>true</c> when the code is a defined register value.</returns>
    public static bool TryGetLabel(int code, [MaybeNullWhen(false)] out string label) =>
        Labels.TryGetValue(code, out label);

    /// <summary>
    /// Resolves a vertical datum code to a human-readable label. Unknown
    /// codes are rendered as <c>"Unknown (code N)"</c>, and a <c>null</c>
    /// code (attribute absent) as <c>"Unknown"</c>.
    /// </summary>
    /// <param name="code">The S-100 <c>verticalDatum</c> code, or <c>null</c>.</param>
    /// <returns>A human-readable datum label.</returns>
    public static string GetLabel(int? code)
    {
        if (code is null)
        {
            return "Unknown";
        }

        return Labels.TryGetValue(code.Value, out var label)
            ? label
            : $"Unknown (code {code.Value})";
    }
}
