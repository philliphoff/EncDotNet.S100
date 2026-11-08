using System.Globalization;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S412.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S412Dataset"/>: its weather
/// warnings and the data providers they refer to (S-412 Feature Catalogue
/// 1.1.0 draft).
/// </summary>
/// <remarks>
/// Projection issues (unparseable values, unknown enumeration codes,
/// unresolved references) surface as <see cref="ProjectionDiagnostic"/>
/// entries rather than exceptions.
/// </remarks>
public sealed class S412MarineWeatherWarnings
{
    /// <summary>The dataset identifier.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The product identifier (typically <c>"S-412"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The weather warnings, in document order.</summary>
    public required IReadOnlyList<S412WeatherWarning> Warnings { get; init; }

    /// <summary>The data providers, in document order.</summary>
    public required IReadOnlyList<S412DataProvider> DataProviders { get; init; }

    /// <summary>The originating feature-bag dataset.</summary>
    public required S412Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S412Dataset"/> into the typed model.
    /// </summary>
    /// <param name="dataset">The dataset to project.</param>
    /// <param name="diagnostics">The issues met while projecting.</param>
    /// <returns>The typed model.</returns>
    public static S412MarineWeatherWarnings From(S412Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var ctx = new ProjectionContext(XlinkResolver.Build(
            dataset.Features.Select(f => new KeyValuePair<string, object>(f.Id, f))
                .Concat(dataset.InformationTypes.Select(i => new KeyValuePair<string, object>(i.Id, i)))));

        var providers = new Dictionary<string, S412DataProvider>(StringComparer.Ordinal);
        var providerList = new List<S412DataProvider>();
        foreach (var info in dataset.InformationTypes)
        {
            if (!string.Equals(info.TypeCode, "DataProvider", StringComparison.OrdinalIgnoreCase))
                continue;
            var provider = ProjectDataProvider(info, ctx);
            providerList.Add(provider);
            providers.TryAdd(info.Id, provider);
        }

        var warnings = new List<S412WeatherWarning>();
        foreach (var feature in dataset.Features)
        {
            if (ProjectWarning(feature, providers, ctx) is { } warning)
                warnings.Add(warning);
        }

        diagnostics = ctx.ToDiagnosticsSnapshot();
        return new S412MarineWeatherWarnings
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Warnings = warnings,
            DataProviders = providerList,
            Source = dataset,
        };
    }

    private static S412DataProvider ProjectDataProvider(S412InformationType info, ProjectionContext ctx)
    {
        var issuing = Complex(info.ComplexAttributes, "issuingService");
        var contact = Complex(info.ComplexAttributes, "contactInformation");
        var preparation = Complex(info.ComplexAttributes, "preparationService");

        return new S412DataProvider
        {
            Id = info.Id,
            MetareaCode = issuing is null ? null : ParseInt(issuing.SubAttributes, "metareaName", info.Id, ctx),
            CountryName = Sub(issuing, "countryName"),
            IssuingServiceNames = Texts(issuing, "issuingServiceName"),
            AgencyName = Sub(contact, "agencyName"),
            MailingAddress = Sub(contact, "mailingAddress"),
            TelephoneNumber = Sub(contact, "telephoneNumber"),
            EmailAddress = Sub(contact, "emailAddress"),
            WebAddress = Sub(contact, "webAddress"),
            PreparationServiceNames = Texts(preparation, "issuingServiceName"),
            PreparationCountryName = Sub(preparation, "countryName"),
            Source = info,
        };
    }

    private static S412WeatherWarning? ProjectWarning(
        S412Feature feature,
        IReadOnlyDictionary<string, S412DataProvider> providers,
        ProjectionContext ctx)
    {
        var a = feature.Attributes;
        var common = new Common(
            DateRange(Complex(feature.ComplexAttributes, "fixedDateRange"), feature.Id, ctx),
            Texts(Complex(feature.ComplexAttributes, "weatherWarningMessage")),
            ParseDate(a.GetValueOrDefault("issueDate"), feature.Id, "issueDate", ctx),
            a.GetValueOrDefault("issueTime"),
            a.GetValueOrDefault("weatherWarningNumber"),
            ParseMoment(a.GetValueOrDefault("cancellationDate"), feature.Id, "cancellationDate", ctx),
            ParseMoment(a.GetValueOrDefault("nextUpdateDateTime"), feature.Id, "nextUpdateDateTime", ctx),
            a.GetValueOrDefault("interoperabilityIdentifier"),
            ResolveProvider(feature, providers, ctx));

        switch (feature.FeatureType)
        {
            case "WindWarning":
                return new S412WindWarning
                {
                    Source = feature,
                    Common = common,
                    Threshold = ParseEnum<S412WindWarningThreshold>(a, "windWarningThreshold", feature.Id, ctx),
                    BeaufortForce = ParseInt(a, "beaufortForce", feature.Id, ctx),
                    MaximumWindSpeed = Texts(Complex(feature.ComplexAttributes, "maximumWindSpeed")),
                };
            case "SignificantWaveHeightWarning":
                return new S412SignificantWaveHeightWarning
                {
                    Source = feature,
                    Common = common,
                    Threshold = ParseEnum<S412SignificantWaveHeightThreshold>(a, "significantWaveHeightWarningThreshold", feature.Id, ctx),
                };
            case "IceAccretionWarning":
                return new S412IceAccretionWarning
                {
                    Source = feature,
                    Common = common,
                    Rate = ParseEnum<S412IceAccretionRate>(a, "iceAccretionRate", feature.Id, ctx),
                    SourceOfIcing = ParseEnum<S412SourceOfIcing>(a, "sourceOfIcing", feature.Id, ctx),
                };
            case "RestrictedVisibilityWarning":
                return new S412RestrictedVisibilityWarning { Source = feature, Common = common };
            case "ThunderstormWarning":
                return new S412ThunderstormWarning { Source = feature, Common = common };
            default:
                ctx.Warn($"Unknown S-412 feature type '{feature.FeatureType}'.", "feature.unknown_type", feature.Id);
                return null;
        }
    }

    private static S412DataProvider? ResolveProvider(
        S412Feature feature,
        IReadOnlyDictionary<string, S412DataProvider> providers,
        ProjectionContext ctx)
    {
        var reference = feature.References.FirstOrDefault(r =>
            string.Equals(r.Role, "dataProviderInformation", StringComparison.OrdinalIgnoreCase));
        if (reference is null)
            return null;

        var id = reference.Href.TrimStart('#');
        if (providers.TryGetValue(id, out var provider))
            return provider;

        ctx.Warn(
            $"dataProviderInformation reference '{reference.Href}' doesn't resolve to a DataProvider.",
            "xlink.unresolved",
            feature.Id,
            "dataProviderInformation");
        return null;
    }

    private static S412FixedDateRange? DateRange(S412ComplexAttribute? range, string id, ProjectionContext ctx)
    {
        if (range is null)
            return null;

        var dateStart = range.SubAttributes.GetValueOrDefault("dateStart");
        var dateEnd = range.SubAttributes.GetValueOrDefault("dateEnd");
        var timeStart = range.SubAttributes.GetValueOrDefault("timeOfDayStart");
        var timeEnd = range.SubAttributes.GetValueOrDefault("timeOfDayEnd");

        return new S412FixedDateRange
        {
            DateStart = dateStart,
            DateEnd = dateEnd,
            TimeOfDayStart = timeStart,
            TimeOfDayEnd = timeEnd,
            Start = Moment(dateStart, timeStart, id, "fixedDateRange.dateStart", ctx),
            End = Moment(dateEnd, timeEnd, id, "fixedDateRange.dateEnd", ctx),
        };
    }

    private static DateTimeOffset? Moment(string? date, string? time, string id, string attribute, ProjectionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(date))
            return null;
        var moment = S100DateTimes.Combine(date, time);
        if (moment is null)
            ctx.Warn($"Can't read '{date}' with time of day '{time}' as a date and time.", "attribute.parse.date", id, attribute);
        return moment;
    }

    private static DateTimeOffset? ParseMoment(string? value, string id, string attribute, ProjectionContext ctx) =>
        Moment(value, null, id, attribute, ctx);

    private static DateOnly? ParseDate(string? value, string id, string attribute, ProjectionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateOnly.TryParseExact(value.Trim(), ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        ctx.Warn($"Can't read '{value}' as a date.", "attribute.parse.date", id, attribute);
        return null;
    }

    private static int? ParseInt(IReadOnlyDictionary<string, string> attributes, string code, string id, ProjectionContext ctx) =>
        AttributeParser.TryParseInt(attributes.GetValueOrDefault(code), ctx, id, code);

    private static TEnum? ParseEnum<TEnum>(IReadOnlyDictionary<string, string> attributes, string code, string id, ProjectionContext ctx)
        where TEnum : struct, Enum
    {
        if (ParseInt(attributes, code, id, ctx) is not { } value)
            return null;
        var result = (TEnum)Enum.ToObject(typeof(TEnum), value);
        if (Enum.IsDefined(result))
            return result;
        ctx.Warn($"{code} code {value} isn't a listed value.", "attribute.unknown_code", id, code);
        return null;
    }

    private static S412ComplexAttribute? Complex(IReadOnlyList<S412ComplexAttribute> attributes, string code) =>
        attributes.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    private static string? Sub(S412ComplexAttribute? attribute, string code) =>
        attribute?.SubAttributes.GetValueOrDefault(code);

    // The texts of every <information> under the attribute, or under its
    // nested group <groupCode> (issuingServiceName holds information).
    private static IReadOnlyList<S412Text> Texts(S412ComplexAttribute? attribute, string? groupCode = null)
    {
        if (attribute is null)
            return [];

        IEnumerable<S412ComplexAttribute> holders = groupCode is null
            ? [attribute]
            : attribute.Groups.Where(g => string.Equals(g.Code, groupCode, StringComparison.OrdinalIgnoreCase));

        return holders
            .SelectMany(h => h.Groups)
            .Where(g => string.Equals(g.Code, "information", StringComparison.OrdinalIgnoreCase))
            .Select(g => new S412Text(
                g.SubAttributes.GetValueOrDefault("language"),
                g.SubAttributes.GetValueOrDefault("text"),
                g.SubAttributes.GetValueOrDefault("fileReference")))
            .ToList();
    }

    /// <summary>The attributes every warning type inherits from <c>WeatherWarning</c>.</summary>
    internal sealed record Common(
        S412FixedDateRange? FixedDateRange,
        IReadOnlyList<S412Text> Messages,
        DateOnly? IssueDate,
        string? IssueTime,
        string? WeatherWarningNumber,
        DateTimeOffset? CancellationDate,
        DateTimeOffset? NextUpdateDateTime,
        string? InteroperabilityIdentifier,
        S412DataProvider? DataProvider);
}
