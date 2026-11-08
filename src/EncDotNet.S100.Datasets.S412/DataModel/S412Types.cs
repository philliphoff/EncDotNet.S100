namespace EncDotNet.S100.Datasets.S412.DataModel;

/// <summary>The kind of an S-412 weather warning, one per concrete feature type.</summary>
public enum S412WarningKind
{
    /// <summary><c>WindWarning</c>.</summary>
    Wind,

    /// <summary><c>SignificantWaveHeightWarning</c>.</summary>
    SignificantWaveHeight,

    /// <summary><c>RestrictedVisibilityWarning</c>.</summary>
    RestrictedVisibility,

    /// <summary><c>ThunderstormWarning</c>.</summary>
    Thunderstorm,

    /// <summary><c>IceAccretionWarning</c>.</summary>
    IceAccretion,
}

/// <summary>The wind warning threshold exceeded or forecast to be exceeded (<c>windWarningThreshold</c>).</summary>
public enum S412WindWarningThreshold
{
    /// <summary>1: near gale force, 28 to 33 knots (Beaufort force 7).</summary>
    NearGaleForce = 1,

    /// <summary>2: gale force, 34 to 47 knots (Beaufort force 8 and 9).</summary>
    GaleForce = 2,

    /// <summary>3: storm force, 48 to 63 knots (Beaufort force 10 and 11).</summary>
    StormForce = 3,

    /// <summary>4: hurricane force, 64 knots and above (Beaufort force 12).</summary>
    HurricaneForce = 4,
}

/// <summary>The significant wave height band exceeded (<c>significantWaveHeightWarningThreshold</c>).</summary>
public enum S412SignificantWaveHeightThreshold
{
    /// <summary>1: two and a half to four metres.</summary>
    TwoAndAHalfToFourMetres = 1,

    /// <summary>2: four to six metres.</summary>
    FourToSixMetres = 2,

    /// <summary>3: six to nine metres.</summary>
    SixToNineMetres = 3,

    /// <summary>4: nine to fourteen metres.</summary>
    NineToFourteenMetres = 4,

    /// <summary>5: greater than fourteen metres.</summary>
    GreaterThanFourteenMetres = 5,
}

/// <summary>The rate of ice accretion (<c>iceAccretionRate</c>).</summary>
public enum S412IceAccretionRate
{
    /// <summary>1: moderate icing.</summary>
    ModerateIcing = 1,

    /// <summary>2: severe icing.</summary>
    SevereIcing = 2,
}

/// <summary>The source of icing (<c>sourceOfIcing</c>).</summary>
public enum S412SourceOfIcing
{
    /// <summary>1: freezing spray.</summary>
    FreezingSpray = 1,

    /// <summary>2: freezing rain.</summary>
    FreezingRain = 2,

    /// <summary>3: freezing fog.</summary>
    FreezingFog = 3,

    /// <summary>4: another source of icing.</summary>
    OtherIcingSource = 4,
}

/// <summary>A text in one language (the <c>information</c> complex attribute).</summary>
/// <param name="Language">The ISO 639-2/T language code (for example <c>eng</c>), or <c>null</c>.</param>
/// <param name="Text">The text, or <c>null</c> when only a file reference is given.</param>
/// <param name="FileReference">A reference to a file holding the text, or <c>null</c>.</param>
public sealed record S412Text(string? Language, string? Text, string? FileReference);

/// <summary>
/// The period a warning applies to (the <c>fixedDateRange</c> complex
/// attribute): a start and end date, each with an optional time of day.
/// </summary>
public sealed record S412FixedDateRange
{
    /// <summary>The start date as encoded (<c>dateStart</c>), or <c>null</c>.</summary>
    public string? DateStart { get; init; }

    /// <summary>The end date as encoded (<c>dateEnd</c>), or <c>null</c>.</summary>
    public string? DateEnd { get; init; }

    /// <summary>The start time of day as encoded (<c>timeOfDayStart</c>), or <c>null</c>.</summary>
    public string? TimeOfDayStart { get; init; }

    /// <summary>The end time of day as encoded (<c>timeOfDayEnd</c>), or <c>null</c>.</summary>
    public string? TimeOfDayEnd { get; init; }

    /// <summary>
    /// The UTC moment the period starts, or <c>null</c> when the start date
    /// or time doesn't parse.
    /// </summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>
    /// The UTC moment the period ends, or <c>null</c> when the end date or
    /// time doesn't parse. A date with no time of day ends at the start of
    /// that day, as <see cref="EncDotNet.S100.DataModel.S100DateTimes"/> reads it.
    /// </summary>
    public DateTimeOffset? End { get; init; }
}
