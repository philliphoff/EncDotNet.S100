using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S412.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S412.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> for an S-412
/// <see cref="S412MarineWeatherWarnings"/> dataset.
/// </summary>
/// <remarks>
/// <para>
/// S-412 is a draft, and its validation annex (Annex B of the Product
/// Specification 1.3.0 draft) has no S-412 checks yet. These rules check what
/// the draft does make normative: the Feature Catalogue 1.1.0 (v009) bindings
/// and listed values, which the GML schema repeats; the encoding guide's time
/// rules (DCEG §2); and the S-100 Part 10b geometry rules.
/// </para>
/// <para>
/// Rule identifiers have the form <c>S412-R-{theme}.{n}</c>, numbered by the
/// data quality themes the draft's validation annex (archived Annex D §F.1.0)
/// lists: 1 completeness, 2 logical consistency, 3 spatial consistency, 5
/// temporal accuracy and 6 thematic accuracy. That annex asks for every check
/// to be a warning while S-412 is in development, so every rule here is a
/// <see cref="ValidationSeverity.Warning"/>.
/// </para>
/// </remarks>
public static class S412MarineWeatherWarningRules
{
    private const ValidationSeverity Severity = ValidationSeverity.Warning;

    // The listed values S412-R-6.1 checks (Feature Catalogue 1.1.0 v009).
    private static readonly (string FeatureType, string Code, Func<string, bool> IsListed)[] ListedValues =
    [
        ("WindWarning", "windWarningThreshold", v => InRange(v, 1, 4)),
        ("WindWarning", "beaufortForce", v => InRange(v, 1, 12) || v.Trim() == "99"),
        ("SignificantWaveHeightWarning", "significantWaveHeightWarningThreshold", v => InRange(v, 1, 5)),
        ("IceAccretionWarning", "iceAccretionRate", v => InRange(v, 1, 2)),
        ("IceAccretionWarning", "sourceOfIcing", v => InRange(v, 1, 4)),
    ];

    // ── 1 Completeness ──────────────────────────────────────────────

    /// <summary>
    /// <c>S412-R-1.1</c>: every warning has the attributes the Feature
    /// Catalogue binds <c>1..1</c> to <c>WeatherWarning</c>
    /// (<c>fixedDateRange</c>, <c>issueDate</c>, <c>weatherWarningNumber</c>,
    /// <c>cancellationDate</c>) and to its own type
    /// (<c>windWarningThreshold</c> and <c>maximumWindSpeed</c>,
    /// <c>significantWaveHeightWarningThreshold</c>, <c>iceAccretionRate</c>).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> WarningHasMandatoryAttributes { get; } =
        Rule("S412-R-1.1", "Warnings have the attributes the feature catalogue makes mandatory.", model =>
            model.Warnings.SelectMany(w => MandatoryOf(w)
                .Where(code => !Has(w.Source, code))
                .Select(code => Finding("S412-R-1.1", w, $"{Name(w)} has no {code}; the feature catalogue requires one."))));

    /// <summary>
    /// <c>S412-R-1.2</c>: every <c>fixedDateRange</c> has a <c>dateStart</c>,
    /// <c>dateEnd</c>, <c>timeOfDayStart</c> and <c>timeOfDayEnd</c>, each
    /// bound <c>1..1</c>.
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> FixedDateRangeIsComplete { get; } =
        Rule("S412-R-1.2", "Each fixedDateRange has its start and end dates and times of day.", model =>
            model.Warnings
                .Where(w => w.FixedDateRange is not null)
                .SelectMany(w => new (string Code, string? Value)[]
                    {
                        ("dateStart", w.FixedDateRange!.DateStart),
                        ("dateEnd", w.FixedDateRange.DateEnd),
                        ("timeOfDayStart", w.FixedDateRange.TimeOfDayStart),
                        ("timeOfDayEnd", w.FixedDateRange.TimeOfDayEnd),
                    }
                    .Where(s => string.IsNullOrWhiteSpace(s.Value))
                    .Select(s => Finding("S412-R-1.2", w, $"{Name(w)}: fixedDateRange has no {s.Code}."))));

    /// <summary>
    /// <c>S412-R-1.3</c>: every <c>DataProvider</c> has its mandatory parts:
    /// <c>issuingService</c> (with <c>metareaName</c> and
    /// <c>issuingServiceName</c>), <c>contactInformation</c> (with
    /// <c>agencyName</c>) and <c>preparationService</c> (with
    /// <c>issuingServiceName</c>).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> DataProviderIsComplete { get; } =
        Rule("S412-R-1.3", "Each DataProvider has its issuing service, contact information and preparation service.", model =>
            model.DataProviders.SelectMany(p => MissingProviderParts(p)
                .Select(part => new ValidationFinding
                {
                    RuleId = "S412-R-1.3",
                    Severity = Severity,
                    Message = $"DataProvider '{p.Id}' has no {part}; the feature catalogue requires one.",
                    RelatedFeatureId = p.Id,
                })));

    /// <summary>
    /// <c>S412-R-1.4</c>: every <c>information</c> in a warning message or
    /// maximum wind speed has a <c>text</c> or a <c>fileReference</c>.
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> InformationHasContent { get; } =
        Rule("S412-R-1.4", "Each information has a text or a file reference.", model =>
            model.Warnings.SelectMany(w => w.Messages
                .Concat(w is S412WindWarning wind ? wind.MaximumWindSpeed : [])
                .Where(t => string.IsNullOrWhiteSpace(t.Text) && string.IsNullOrWhiteSpace(t.FileReference))
                .Take(1)
                .Select(_ => Finding("S412-R-1.4", w, $"{Name(w)} has an information with neither text nor fileReference."))));

    // ── 2 Logical consistency ───────────────────────────────────────

    /// <summary>
    /// <c>S412-R-2.1</c>: a warning's <c>dataProviderInformation</c> refers to
    /// a <c>DataProvider</c> in the dataset.
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> DataProviderReferenceResolves { get; } =
        Rule("S412-R-2.1", "dataProviderInformation refers to a DataProvider in the dataset.", model =>
            model.Warnings
                .Where(w => w.DataProvider is null)
                .SelectMany(w => w.Source.References
                    .Where(r => string.Equals(r.Role, "dataProviderInformation", StringComparison.OrdinalIgnoreCase))
                    .Take(1)
                    .Select(r => Finding("S412-R-2.1", w, $"{Name(w)}: dataProviderInformation '{r.Href}' isn't a DataProvider in the dataset."))));

    /// <summary><c>S412-R-2.2</c>: every <c>gml:id</c> is unique in the dataset (S-100 Part 10b).</summary>
    public static IValidationRule<S412MarineWeatherWarnings> IdentifiersAreUnique { get; } =
        Rule("S412-R-2.2", "Feature and information type identifiers are unique.", model =>
            model.Source.Features.Select(f => f.Id)
                .Concat(model.Source.InformationTypes.Select(i => i.Id))
                .Where(id => !string.IsNullOrEmpty(id))
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => new ValidationFinding
                {
                    RuleId = "S412-R-2.2",
                    Severity = Severity,
                    Message = $"The identifier '{g.Key}' is used {g.Count()} times.",
                    RelatedFeatureId = g.Key,
                }));

    // ── 3 Spatial consistency ───────────────────────────────────────

    /// <summary>
    /// <c>S412-R-3.1</c>: every warning is a surface with a closed exterior
    /// ring of at least four positions (the Feature Catalogue permits only
    /// <c>surface</c>; S-100 Part 10b <c>gml:LinearRing</c>).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> WarningIsClosedSurface { get; } =
        Rule("S412-R-3.1", "Each warning is a surface with a closed exterior ring.", model =>
            model.Warnings.Select(w => SurfaceProblem(w) is { } problem
                ? Finding("S412-R-3.1", w, $"{Name(w)} {problem}.")
                : null).OfType<ValidationFinding>());

    /// <summary>
    /// <c>S412-R-3.2</c>: every position lies within the WGS 84 latitude and
    /// longitude ranges (S-100 Part 10b, EPSG:4326).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> PositionsInWgs84Range { get; } =
        Rule("S412-R-3.2", "Positions lie within the WGS 84 latitude and longitude ranges.", model =>
            model.Warnings.SelectMany(w => w.ExteriorRing.Concat(w.InteriorRings.SelectMany(r => r))
                .Where(p => p.Latitude is < -90 or > 90 || p.Longitude is < -180 or > 180)
                .Take(1)
                .Select(p => new ValidationFinding
                {
                    RuleId = "S412-R-3.2",
                    Severity = Severity,
                    Message = $"{Name(w)} has a position ({p.Latitude}, {p.Longitude}) outside the WGS 84 ranges.",
                    Point = p,
                    RelatedFeatureId = w.Id,
                })));

    // ── 5 Temporal accuracy ─────────────────────────────────────────

    /// <summary>
    /// <c>S412-R-5.1</c>: dates and times can be read as the schema types
    /// say (<c>xs:date</c>, <c>xs:time</c>, <c>xs:dateTime</c>), and a time
    /// of day is a complete time with seconds (DCEG §2: complete
    /// representation, <c>hhmmss</c>).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> TimesAreReadable { get; } =
        Rule("S412-R-5.1", "Dates and times are complete and readable.", model =>
            model.Warnings.SelectMany(w => UnreadableTimes(w)
                .Select(t => Finding("S412-R-5.1", w, $"{Name(w)}: {t.Code} '{t.Value}' {t.Problem}."))));

    /// <summary><c>S412-R-5.2</c>: a <c>fixedDateRange</c> doesn't end before it starts.</summary>
    public static IValidationRule<S412MarineWeatherWarnings> RangeEndsAfterStart { get; } =
        Rule("S412-R-5.2", "fixedDateRange doesn't end before it starts.", model =>
            model.Warnings
                .Where(w => w.FixedDateRange is { Start: { } start, End: { } end } && end < start)
                .Select(w => Finding("S412-R-5.2", w,
                    $"{Name(w)}: fixedDateRange ends ({w.FixedDateRange!.End:u}) before it starts ({w.FixedDateRange.Start:u}).")));

    /// <summary>
    /// <c>S412-R-5.3</c>: a warning is in force at some time: its
    /// <c>cancellationDate</c> isn't at or before the start of its
    /// <c>fixedDateRange</c>, so it is never shown.
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> NotCancelledBeforeItStarts { get; } =
        Rule("S412-R-5.3", "A warning isn't cancelled before its date range starts.", model =>
            model.Warnings
                .Where(w => w.CancellationDate is { } cancelled && w.FixedDateRange?.Start is { } start && cancelled <= start)
                .Select(w => Finding("S412-R-5.3", w,
                    $"{Name(w)} is cancelled ({w.CancellationDate:u}) at or before its date range starts ({w.FixedDateRange!.Start:u}), so it is never in force.")));

    /// <summary>
    /// <c>S412-R-5.4</c>: the next update (<c>nextUpdateDateTime</c>) isn't
    /// before the warning was issued (<c>issueDate</c>, <c>issueTime</c>).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> NextUpdateAfterIssue { get; } =
        Rule("S412-R-5.4", "nextUpdateDateTime isn't before the issue time.", model =>
            model.Warnings
                .Where(w => w.NextUpdateDateTime is { } next && w.IssuedAt is { } issued && next < issued)
                .Select(w => Finding("S412-R-5.4", w,
                    $"{Name(w)}: nextUpdateDateTime ({w.NextUpdateDateTime:u}) is before it was issued ({w.IssuedAt:u}).")));

    // ── 6 Thematic accuracy ─────────────────────────────────────────

    /// <summary>
    /// <c>S412-R-6.1</c>: enumeration values are codes the Feature Catalogue
    /// lists (<c>windWarningThreshold</c> 1–4, <c>beaufortForce</c> 1–12 or
    /// 99, <c>significantWaveHeightWarningThreshold</c> 1–5,
    /// <c>iceAccretionRate</c> 1–2, <c>sourceOfIcing</c> 1–4, and the
    /// <c>DataProvider</c>'s <c>metareaName</c> 1–14).
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> ValuesAreListed { get; } =
        Rule("S412-R-6.1", "Enumeration values are listed in the feature catalogue.", model =>
            model.Warnings.SelectMany(w => ListedValues
                    .Where(l => l.FeatureType == w.Source.FeatureType && w.Source.Attributes.TryGetValue(l.Code, out var v) && !l.IsListed(v))
                    .Select(l => Finding("S412-R-6.1", w, $"{Name(w)}: {l.Code} '{w.Source.Attributes[l.Code]}' isn't a listed value.")))
                .Concat(model.DataProviders
                    .Select(p => (Provider: p, Value: p.Source.ComplexAttributes
                        .FirstOrDefault(c => c.Code == "issuingService")?.SubAttributes.GetValueOrDefault("metareaName")))
                    .Where(x => x.Value is not null && !InRange(x.Value, 1, 14))
                    .Select(x => new ValidationFinding
                    {
                        RuleId = "S412-R-6.1",
                        Severity = Severity,
                        Message = $"DataProvider '{x.Provider.Id}': metareaName '{x.Value}' isn't a listed value.",
                        RelatedFeatureId = x.Provider.Id,
                    })));

    /// <summary>
    /// <c>S412-R-6.2</c>: a wind warning's <c>beaufortForce</c> matches its
    /// <c>windWarningThreshold</c>, as the schema defines the thresholds:
    /// near gale force is force 7, gale force 8 or 9, storm force 10 or 11,
    /// and hurricane force 12.
    /// </summary>
    public static IValidationRule<S412MarineWeatherWarnings> BeaufortForceMatchesThreshold { get; } =
        Rule("S412-R-6.2", "A wind warning's Beaufort force matches its threshold.", model =>
            model.Warnings.OfType<S412WindWarning>()
                .Where(w => w.Threshold is { } t && w.BeaufortForce is { } force && !ForcesOf(t).Contains(force))
                .Select(w => Finding("S412-R-6.2", w,
                    $"{Name(w)}: Beaufort force {w.BeaufortForce} doesn't match windWarningThreshold {(int)w.Threshold!.Value} ({w.Threshold.Value.Label()}, force {string.Join(" or ", ForcesOf(w.Threshold.Value))}).")));

    /// <summary>The canonical default rule set for S-412 datasets.</summary>
    public static ValidationRuleSet<S412MarineWeatherWarnings> Default { get; } = new(
        WarningHasMandatoryAttributes,
        FixedDateRangeIsComplete,
        DataProviderIsComplete,
        InformationHasContent,
        DataProviderReferenceResolves,
        IdentifiersAreUnique,
        WarningIsClosedSurface,
        PositionsInWgs84Range,
        TimesAreReadable,
        RangeEndsAfterStart,
        NotCancelledBeforeItStarts,
        NextUpdateAfterIssue,
        ValuesAreListed,
        BeaufortForceMatchesThreshold);

    /// <summary>Runs the <see cref="Default"/> rule set against <paramref name="model"/>.</summary>
    /// <param name="model">The typed dataset.</param>
    /// <param name="context">The validation context, or <c>null</c>.</param>
    /// <returns>The report.</returns>
    public static ValidationReport Validate(S412MarineWeatherWarnings model, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Default.Run(model, context);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static IValidationRule<S412MarineWeatherWarnings> Rule(
        string id, string description, Func<S412MarineWeatherWarnings, IEnumerable<ValidationFinding>> findings) =>
        ValidationRuleBuilder.RuleFor<S412MarineWeatherWarnings>(id)
            .WithDescription(description)
            .WithSeverity(Severity)
            .Yield((model, _) => findings(model).ToList())
            .Build();

    private static ValidationFinding Finding(string ruleId, S412WeatherWarning warning, string message) => new()
    {
        RuleId = ruleId,
        Severity = Severity,
        Message = message,
        Point = warning.ExteriorRing.Count > 0 ? warning.ExteriorRing[0] : null,
        BoundingBox = Bounds(warning.ExteriorRing),
        RelatedFeatureId = warning.Id,
    };

    private static BoundingBox? Bounds(IReadOnlyList<GeoPosition> ring) =>
        ring.Count == 0
            ? null
            : new BoundingBox(ring.Min(p => p.Latitude), ring.Min(p => p.Longitude), ring.Max(p => p.Latitude), ring.Max(p => p.Longitude));

    private static string Name(S412WeatherWarning warning) => $"{warning.Source.FeatureType} '{warning.Id}'";

    private static IEnumerable<string> MandatoryOf(S412WeatherWarning warning)
    {
        string[] common = ["fixedDateRange", "issueDate", "weatherWarningNumber", "cancellationDate"];
        string[] own = warning switch
        {
            S412WindWarning => ["windWarningThreshold", "maximumWindSpeed"],
            S412SignificantWaveHeightWarning => ["significantWaveHeightWarningThreshold"],
            S412IceAccretionWarning => ["iceAccretionRate"],
            _ => [],
        };
        return common.Concat(own);
    }

    private static bool Has(S412Feature feature, string code) =>
        feature.Attributes.ContainsKey(code)
        || feature.ComplexAttributes.Any(c => string.Equals(c.Code, code, StringComparison.Ordinal));

    private static IEnumerable<string> MissingProviderParts(S412DataProvider provider)
    {
        var attributes = provider.Source.ComplexAttributes;
        S412ComplexAttribute? Part(string code) => attributes.FirstOrDefault(c => c.Code == code);
        bool HasNames(S412ComplexAttribute? holder) =>
            holder?.Groups.Any(g => g.Code == "issuingServiceName" && g.Groups.Any(i => i.Code == "information")) == true;

        var issuing = Part("issuingService");
        if (issuing is null)
            yield return "issuingService";
        else
        {
            if (!issuing.SubAttributes.ContainsKey("metareaName"))
                yield return "issuingService.metareaName";
            if (!HasNames(issuing))
                yield return "issuingService.issuingServiceName";
        }

        var contact = Part("contactInformation");
        if (contact is null)
            yield return "contactInformation";
        else if (!contact.SubAttributes.ContainsKey("agencyName"))
            yield return "contactInformation.agencyName";

        var preparation = Part("preparationService");
        if (preparation is null)
            yield return "preparationService";
        else if (!HasNames(preparation))
            yield return "preparationService.issuingServiceName";
    }

    private static string? SurfaceProblem(S412WeatherWarning warning)
    {
        if (warning.GeometryType != S100GeometryType.Surface)
            return warning.GeometryType == S100GeometryType.None
                ? "has no geometry; S-412 warnings are surfaces"
                : $"is a {warning.GeometryType.ToString().ToLowerInvariant()}; S-412 warnings are surfaces";

        var ring = warning.ExteriorRing;
        if (ring.Count < 4)
            return $"has an exterior ring of {ring.Count} position(s); a closed ring needs at least four";

        const double tolerance = 1e-9;
        return Math.Abs(ring[0].Latitude - ring[^1].Latitude) > tolerance || Math.Abs(ring[0].Longitude - ring[^1].Longitude) > tolerance
            ? "has an exterior ring whose first and last positions differ"
            : null;
    }

    private static IEnumerable<(string Code, string Value, string Problem)> UnreadableTimes(S412WeatherWarning warning)
    {
        if (warning.FixedDateRange is { } range)
        {
            foreach (var (code, value) in new[] { ("timeOfDayStart", range.TimeOfDayStart), ("timeOfDayEnd", range.TimeOfDayEnd) })
            {
                if (value is null)
                    continue;
                if (S100DateTimes.Combine("2000-01-01", value) is null)
                    yield return ($"fixedDateRange.{code}", value, IsHoursAndMinutes(value)
                        ? "has no seconds; a time of day is a complete time (hhmmss)"
                        : "isn't a time");
            }

            foreach (var (code, value) in new[] { ("dateStart", range.DateStart), ("dateEnd", range.DateEnd) })
            {
                if (value is not null && S100DateTimes.Combine(value, null) is null)
                    yield return ($"fixedDateRange.{code}", value, "isn't a date");
            }
        }

        var attributes = warning.Source.Attributes;
        if (attributes.TryGetValue("issueDate", out var issueDate) && warning.IssueDate is null)
            yield return ("issueDate", issueDate, "isn't a date");
        if (attributes.TryGetValue("issueTime", out var issueTime) && S100DateTimes.Combine("2000-01-01", issueTime) is null)
            yield return ("issueTime", issueTime, "isn't a time");

        foreach (var code in new[] { "cancellationDate", "nextUpdateDateTime" })
        {
            if (!attributes.TryGetValue(code, out var value))
                continue;
            if (S100DateTimes.Combine(value, null) is null)
                yield return (code, value, "isn't a date and time");
            else if (!value.Contains('T', StringComparison.Ordinal))
                yield return (code, value, "is a date without a time; the schema type is a date and time");
        }
    }

    private static bool IsHoursAndMinutes(string value)
    {
        var digits = value.Trim().TrimEnd('Z');
        return digits.Length == 4 && digits.All(char.IsAsciiDigit);
    }

    private static bool InRange(string value, int low, int high) =>
        int.TryParse(value.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
        && n >= low && n <= high;

    private static int[] ForcesOf(S412WindWarningThreshold threshold) => threshold switch
    {
        S412WindWarningThreshold.NearGaleForce => [7],
        S412WindWarningThreshold.GaleForce => [8, 9],
        S412WindWarningThreshold.StormForce => [10, 11],
        S412WindWarningThreshold.HurricaneForce => [12],
        _ => [],
    };
}
