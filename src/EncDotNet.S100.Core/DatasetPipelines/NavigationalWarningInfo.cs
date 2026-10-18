namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Whether a navigational warning applies at a given time.
/// </summary>
public enum NavigationalWarningStatus
{
    /// <summary>The warning applies at that time.</summary>
    InForce,

    /// <summary>The warning is outside its validity ranges at that time, and a later one is still to start.</summary>
    NotYetInForce,

    /// <summary>The warning was cancelled, or every validity range ended, before that time.</summary>
    Ended,
}

/// <summary>
/// One language of a navigational warning's message text.
/// </summary>
/// <param name="Language">
/// The language code as the dataset writes it (S-124 uses ISO 639-2, e.g.
/// <c>"eng"</c>), or <c>null</c> when it gives none.
/// </param>
/// <param name="Text">The message text, with its line breaks and indentation kept.</param>
public sealed record NavigationalWarningText(string? Language, string Text);

/// <summary>
/// A period when a navigational warning applies. Either end may be open.
/// </summary>
/// <param name="Start">The start (UTC), or <c>null</c> when open.</param>
/// <param name="End">The end (UTC), or <c>null</c> when open.</param>
public sealed record NavigationalWarningValidity(DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>
/// A navigational warning's identifier in its series: the series name, the
/// warning number and the year, as a cancellation refers to it.
/// </summary>
/// <param name="Series">The series name (e.g. <c>"NAVAREA X"</c>), or <c>null</c> when not given.</param>
/// <param name="Number">The warning number within the series and year.</param>
/// <param name="Year">The year (four digits, or two when the source gives two), or <c>null</c> when not given.</param>
public sealed record NavigationalWarningReference(string? Series, int Number, int? Year)
{
    /// <summary>
    /// Whether this reference names <paramref name="other"/>: the same number,
    /// the same year (compared by its last two digits) when both give one,
    /// and the same series, ignoring case and spacing, when both give one.
    /// </summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns><c>true</c> when they name the same warning.</returns>
    public bool Matches(NavigationalWarningReference other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Number != other.Number)
            return false;
        if (Year is { } year && other.Year is { } otherYear && year % 100 != otherYear % 100)
            return false;
        return string.IsNullOrWhiteSpace(Series) || string.IsNullOrWhiteSpace(other.Series)
            || string.Equals(Normalise(Series), Normalise(other.Series), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string series) =>
        string.Concat(series.Where(c => !char.IsWhiteSpace(c)));
}

/// <summary>
/// The message of a navigational warning part, projected for object
/// information UIs: identification, type, validity, area and the full text in
/// every language the dataset gives (S-124 <c>NavwarnPart</c> read through its
/// <c>NavwarnPreamble</c> header).
/// </summary>
/// <remarks>
/// A UI shows this as one card, not as attribute rows. The text is the point
/// of picking a warning, and a SECURITE message runs to 10–20 lines.
/// </remarks>
public sealed record NavigationalWarningInfo
{
    /// <summary>The series name (e.g. <c>"NAVAREA X"</c>, <c>"AUSCOAST"</c>), when given.</summary>
    public string? Series { get; init; }

    /// <summary>
    /// The warning number and two-digit year as mariners write it
    /// (e.g. <c>"106/26"</c>), or <c>null</c> when the preamble gives no number.
    /// </summary>
    public string? Number { get; init; }

    /// <summary>The detailed warning type (S-124 <c>navwarnTypeDetails</c>, e.g. "Cable Operations"), when given.</summary>
    public string? WarningType { get; init; }

    /// <summary>The general warning type (S-124 <c>navwarnTypeGeneral</c>), when given.</summary>
    public string? GeneralType { get; init; }

    /// <summary>The warning's title, when given.</summary>
    public string? Title { get; init; }

    /// <summary>The general area (e.g. "Coral Sea"), when given.</summary>
    public string? GeneralArea { get; init; }

    /// <summary>The locality within the general area, when given.</summary>
    public string? Locality { get; init; }

    /// <summary>The authority or agency that issued the warning, when given.</summary>
    public string? Issuer { get; init; }

    /// <summary>When the warning was published, when given.</summary>
    public DateTimeOffset? PublicationTime { get; init; }

    /// <summary>When the warning cancels itself, or <c>null</c> when it stays in force until cancelled.</summary>
    public DateTimeOffset? CancellationDate { get; init; }

    /// <summary>When the part applies, in document order. Empty when the part gives no range.</summary>
    public IReadOnlyList<NavigationalWarningValidity> Validity { get; init; } = [];

    /// <summary>The message text, one entry per language in document order. Empty when the part has none.</summary>
    public IReadOnlyList<NavigationalWarningText> Texts { get; init; } = [];

    /// <summary>Whether the part has geometry, so the map draws it.</summary>
    public bool HasLocation { get; init; }

    /// <summary>
    /// The warning's identifier in its series, or <c>null</c> when the
    /// preamble gives no number; what another warning's cancellation names.
    /// </summary>
    public NavigationalWarningReference? Reference { get; init; }

    /// <summary>
    /// The warnings this one cancels (S-124 <c>References</c> with
    /// <c>referenceCategory</c> 1), in document order. Empty when it cancels none.
    /// </summary>
    public IReadOnlyList<NavigationalWarningReference> Cancels { get; init; } = [];

    /// <summary>
    /// The areas the part affects beyond its own geometry (S-124
    /// <c>NavwarnAreaAffected</c>, through <c>areaAffected</c>), in WGS-84.
    /// Empty when it names none.
    /// </summary>
    public IReadOnlyList<S100FeatureGeometry> AffectedAreas { get; init; } = [];

    /// <summary>
    /// The series and number (e.g. <c>"NAVAREA X 106/26"</c>), or
    /// <c>null</c> when the preamble gives no number.
    /// </summary>
    public string? Label => Number is null
        ? null
        : string.IsNullOrWhiteSpace(Series) ? Number : $"{Series} {Number}";

    /// <summary>
    /// Whether the warning applies at <paramref name="time"/>. A warning is
    /// ended once its cancellation date has passed or every validity range has
    /// ended, and not yet in force while it waits for a range to start. With
    /// no validity range it is in force until cancelled.
    /// </summary>
    /// <param name="time">The time to test, usually the view time.</param>
    /// <returns>The warning's status at that time.</returns>
    public NavigationalWarningStatus StatusAt(DateTimeOffset time)
    {
        if (CancellationDate is { } cancelled && time >= cancelled)
            return NavigationalWarningStatus.Ended;
        if (Validity.Count == 0)
            return NavigationalWarningStatus.InForce;

        var anyLater = false;
        foreach (var range in Validity)
        {
            var started = range.Start is null || time >= range.Start;
            var ended = range.End is { } end && time > end;
            if (started && !ended)
                return NavigationalWarningStatus.InForce;
            anyLater |= !started;
        }

        // Outside every range: waiting for a later one, or past them all.
        return anyLater ? NavigationalWarningStatus.NotYetInForce : NavigationalWarningStatus.Ended;
    }
}

/// <summary>
/// A navigational warning part as a dataset lists it: the feature that carries
/// it and its message. Returned by
/// <see cref="IDatasetProcessor.GetNavigationalWarnings"/>.
/// </summary>
/// <param name="FeatureRef">The part's feature id within the dataset.</param>
/// <param name="Ordinal">
/// The part's position in <see cref="IDatasetProcessor.EnumerateFeatures"/>;
/// pass it to <see cref="IDatasetProcessor.GetFeatureInfoAt"/> or
/// <see cref="IDatasetProcessor.GetFeatureGeometryAt"/>.
/// </param>
/// <param name="Warning">The part's message.</param>
public sealed record NavigationalWarningFeature(string FeatureRef, int Ordinal, NavigationalWarningInfo Warning);
