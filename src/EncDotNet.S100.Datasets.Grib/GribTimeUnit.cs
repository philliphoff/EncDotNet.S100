namespace EncDotNet.S100.Datasets.Grib;

/// <summary>The unit of a GRIB2 time range (code table 4.4).</summary>
public enum GribTimeUnit : byte
{
    /// <summary>Minutes.</summary>
    Minute = 0,

    /// <summary>Hours.</summary>
    Hour = 1,

    /// <summary>Days.</summary>
    Day = 2,

    /// <summary>Months.</summary>
    Month = 3,

    /// <summary>Years.</summary>
    Year = 4,

    /// <summary>Decades (10 years).</summary>
    Decade = 5,

    /// <summary>Normals (30 years).</summary>
    Normal = 6,

    /// <summary>Centuries (100 years).</summary>
    Century = 7,

    /// <summary>3 hours.</summary>
    ThreeHours = 10,

    /// <summary>6 hours.</summary>
    SixHours = 11,

    /// <summary>12 hours.</summary>
    TwelveHours = 12,

    /// <summary>Seconds.</summary>
    Second = 13,

    /// <summary>Missing.</summary>
    Missing = 255,
}
