namespace EncDotNet.S100.Datasets.S412.DataModel;

/// <summary>
/// The labels the S-412 Feature Catalogue (1.1.0 draft v009) gives its listed
/// values, for showing a code as a reader expects it.
/// </summary>
public static class S412Labels
{
    /// <summary>The label of a <c>windWarningThreshold</c>, such as <c>gale force</c>.</summary>
    /// <param name="value">The threshold.</param>
    /// <returns>The label.</returns>
    public static string Label(this S412WindWarningThreshold value) => value switch
    {
        S412WindWarningThreshold.NearGaleForce => "near gale force",
        S412WindWarningThreshold.GaleForce => "gale force",
        S412WindWarningThreshold.StormForce => "storm force",
        S412WindWarningThreshold.HurricaneForce => "hurricane force",
        _ => value.ToString(),
    };

    /// <summary>The label of a <c>significantWaveHeightWarningThreshold</c>, such as <c>four to six metres</c>.</summary>
    /// <param name="value">The threshold.</param>
    /// <returns>The label.</returns>
    public static string Label(this S412SignificantWaveHeightThreshold value) => value switch
    {
        S412SignificantWaveHeightThreshold.TwoAndAHalfToFourMetres => "two and a half to four metres",
        S412SignificantWaveHeightThreshold.FourToSixMetres => "four to six metres",
        S412SignificantWaveHeightThreshold.SixToNineMetres => "six to nine metres",
        S412SignificantWaveHeightThreshold.NineToFourteenMetres => "nine to fourteen metres",
        S412SignificantWaveHeightThreshold.GreaterThanFourteenMetres => "greater than fourteen metres",
        _ => value.ToString(),
    };

    /// <summary>The label of an <c>iceAccretionRate</c>, such as <c>severeIcing</c>.</summary>
    /// <param name="value">The rate.</param>
    /// <returns>The label.</returns>
    public static string Label(this S412IceAccretionRate value) => value switch
    {
        S412IceAccretionRate.ModerateIcing => "moderateIcing",
        S412IceAccretionRate.SevereIcing => "severeIcing",
        _ => value.ToString(),
    };

    /// <summary>The label of a <c>sourceOfIcing</c>, such as <c>freezingSpray</c>.</summary>
    /// <param name="value">The source.</param>
    /// <returns>The label.</returns>
    public static string Label(this S412SourceOfIcing value) => value switch
    {
        S412SourceOfIcing.FreezingSpray => "freezingSpray",
        S412SourceOfIcing.FreezingRain => "freezingRain",
        S412SourceOfIcing.FreezingFog => "freezingFog",
        S412SourceOfIcing.OtherIcingSource => "otherIcingSource",
        _ => value.ToString(),
    };

    /// <summary>
    /// The label of a <c>beaufortForce</c> code, such as <c>gale</c> for 8 or
    /// <c>calm</c> for 99, or <c>null</c> for a code the catalogue doesn't list.
    /// </summary>
    /// <param name="code">The code: 1 to 12, or 99 for calm.</param>
    /// <returns>The label, or <c>null</c>.</returns>
    public static string? BeaufortForceLabel(int code) => code switch
    {
        99 => "calm",
        1 => "light air",
        2 => "light breeze",
        3 => "gentle breeze",
        4 => "moderate breeze",
        5 => "fresh breeze",
        6 => "strong breeze",
        7 => "near gale",
        8 => "gale",
        9 => "strong gale",
        10 => "storm",
        11 => "violent storm",
        12 => "hurricane",
        _ => null,
    };

    /// <summary>The name of a METAREA code (1 to 14), such as <c>METAREA XI</c>, or <c>null</c>.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The name, or <c>null</c>.</returns>
    public static string? MetareaName(int code) => code is >= 1 and <= 14
        ? "METAREA " + Roman[code - 1]
        : null;

    private static readonly string[] Roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV"];
}
