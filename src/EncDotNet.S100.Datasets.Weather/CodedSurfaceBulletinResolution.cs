namespace EncDotNet.S100.Datasets.Weather;

/// <summary>How precisely a coded surface bulletin gives positions.</summary>
public enum CodedSurfaceBulletinResolution
{
    /// <summary>
    /// Whole degrees: a 4- or 5-digit point, two digits of latitude north and
    /// two or three of longitude west (<c>41115</c> is 41°N 115°W).
    /// </summary>
    Standard,

    /// <summary>
    /// Tenths of a degree: a 7-digit point, three digits of latitude north and
    /// four of longitude west, in tenths (<c>4051145</c> is 40.5°N 114.5°W).
    /// The canonical input (design note <c>vector-weather.md</c> V35).
    /// </summary>
    HighResolution,
}
