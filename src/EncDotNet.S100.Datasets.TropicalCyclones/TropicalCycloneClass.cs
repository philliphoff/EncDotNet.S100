namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// A tropical cyclone's classification at one time, from the ATCF and NHC
/// storm type codes (<c>TD</c>, <c>TS</c>, <c>HU</c>, <c>MH</c> and so on).
/// The code itself is kept beside it (for example
/// <see cref="TropicalCycloneForecastPoint.ClassCode"/>), so nothing is lost
/// when a code isn't one of these.
/// </summary>
public enum TropicalCycloneClass
{
    /// <summary>A code this library doesn't know.</summary>
    Unknown,

    /// <summary>A disturbance (<c>DB</c>).</summary>
    Disturbance,

    /// <summary>A tropical wave (<c>WV</c>).</summary>
    TropicalWave,

    /// <summary>A low that isn't a tropical cyclone (<c>LO</c>), or a remnant low.</summary>
    Low,

    /// <summary>A potential tropical cyclone (<c>PC</c>): advisories start before the storm forms.</summary>
    PotentialTropicalCyclone,

    /// <summary>A tropical depression (<c>TD</c>), under 34 kt.</summary>
    TropicalDepression,

    /// <summary>A tropical storm (<c>TS</c>), 34 to 63 kt.</summary>
    TropicalStorm,

    /// <summary>A hurricane (<c>HU</c>), 64 kt or more.</summary>
    Hurricane,

    /// <summary>A major hurricane (<c>MH</c>), category 3 or more: 96 kt or more.</summary>
    MajorHurricane,

    /// <summary>A typhoon (<c>TY</c>), the West Pacific name for 64 kt or more.</summary>
    Typhoon,

    /// <summary>A super typhoon (<c>ST</c>), 130 kt or more.</summary>
    SuperTyphoon,

    /// <summary>A subtropical depression (<c>SD</c> or <c>STD</c>).</summary>
    SubtropicalDepression,

    /// <summary>A subtropical storm (<c>SS</c> or <c>STS</c>).</summary>
    SubtropicalStorm,

    /// <summary>An extratropical cyclone (<c>EX</c>).</summary>
    Extratropical,

    /// <summary>A post-tropical cyclone (<c>PT</c> or <c>PTC</c>).</summary>
    PostTropical,
}

/// <summary>Reads ATCF and NHC storm type codes.</summary>
public static class TropicalCycloneClasses
{
    /// <summary>
    /// The class a storm type code names, ignoring case and spaces; for
    /// example <c>"HU"</c> is <see cref="TropicalCycloneClass.Hurricane"/>.
    /// </summary>
    /// <param name="code">The code, as NHC writes it.</param>
    /// <returns>The class, or <see cref="TropicalCycloneClass.Unknown"/>.</returns>
    public static TropicalCycloneClass FromCode(string? code) =>
        code?.Trim().ToUpperInvariant() switch
        {
            "DB" => TropicalCycloneClass.Disturbance,
            "WV" => TropicalCycloneClass.TropicalWave,
            "LO" or "RL" => TropicalCycloneClass.Low,
            "PC" => TropicalCycloneClass.PotentialTropicalCyclone,
            "TD" => TropicalCycloneClass.TropicalDepression,
            "TS" => TropicalCycloneClass.TropicalStorm,
            "HU" => TropicalCycloneClass.Hurricane,
            "MH" => TropicalCycloneClass.MajorHurricane,
            "TY" => TropicalCycloneClass.Typhoon,
            "ST" or "STY" => TropicalCycloneClass.SuperTyphoon,
            "SD" or "STD" => TropicalCycloneClass.SubtropicalDepression,
            "SS" or "STS" => TropicalCycloneClass.SubtropicalStorm,
            "EX" => TropicalCycloneClass.Extratropical,
            "PT" or "PTC" => TropicalCycloneClass.PostTropical,
            _ => TropicalCycloneClass.Unknown,
        };

    /// <summary>
    /// Whether <paramref name="value"/> is a tropical or subtropical cyclone
    /// rather than a disturbance, wave, low or extratropical system.
    /// </summary>
    /// <param name="value">The class.</param>
    public static bool IsCyclone(this TropicalCycloneClass value) =>
        value is TropicalCycloneClass.TropicalDepression
            or TropicalCycloneClass.TropicalStorm
            or TropicalCycloneClass.Hurricane
            or TropicalCycloneClass.MajorHurricane
            or TropicalCycloneClass.Typhoon
            or TropicalCycloneClass.SuperTyphoon
            or TropicalCycloneClass.SubtropicalDepression
            or TropicalCycloneClass.SubtropicalStorm;
}
