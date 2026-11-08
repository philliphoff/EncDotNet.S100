using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S412.DataModel;

/// <summary>
/// A weather warning: the attributes every S-412 warning type inherits from
/// the abstract <c>WeatherWarning</c> feature type, and its area.
/// </summary>
public abstract class S412WeatherWarning
{
    /// <summary>The feature this warning was projected from.</summary>
    public required S412Feature Source { get; init; }

    internal S412MarineWeatherWarnings.Common Common { get; init; } = null!;

    /// <summary>The warning's kind.</summary>
    public abstract S412WarningKind Kind { get; }

    /// <summary>The feature's <c>gml:id</c>.</summary>
    public string Id => Source.Id;

    /// <summary>The geometry primitive. S-412 warnings are surfaces.</summary>
    public S100GeometryType GeometryType => Source.GeometryType;

    /// <summary>The warning area's exterior ring.</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing => Source.ExteriorRing;

    /// <summary>The warning area's interior rings (holes).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings => Source.InteriorRings;

    /// <summary>The period the warning applies to (<c>fixedDateRange</c>), or <c>null</c>.</summary>
    public S412FixedDateRange? FixedDateRange => Common.FixedDateRange;

    /// <summary>The warning's message, one per language (<c>weatherWarningMessage</c>).</summary>
    public IReadOnlyList<S412Text> Messages => Common.Messages;

    /// <summary>The date the warning was issued (<c>issueDate</c>), or <c>null</c>.</summary>
    public DateOnly? IssueDate => Common.IssueDate;

    /// <summary>The UTC time of day the warning was issued (<c>issueTime</c>) as encoded, or <c>null</c>.</summary>
    public string? IssueTime => Common.IssueTime;

    /// <summary>The warning's number (<c>weatherWarningNumber</c>), or <c>null</c>.</summary>
    public string? WeatherWarningNumber => Common.WeatherWarningNumber;

    /// <summary>When the warning is cancelled (<c>cancellationDate</c>), or <c>null</c>.</summary>
    public DateTimeOffset? CancellationDate => Common.CancellationDate;

    /// <summary>When the warning is next updated (<c>nextUpdateDateTime</c>), or <c>null</c>.</summary>
    public DateTimeOffset? NextUpdateDateTime => Common.NextUpdateDateTime;

    /// <summary>The warning's interoperability identifier (<c>interoperabilityIdentifier</c>), or <c>null</c>.</summary>
    public string? InteroperabilityIdentifier => Common.InteroperabilityIdentifier;

    /// <summary>The data provider the warning refers to (<c>dataProviderInformation</c>), or <c>null</c>.</summary>
    public S412DataProvider? DataProvider => Common.DataProvider;
}

/// <summary>A wind warning (<c>WindWarning</c>).</summary>
public sealed class S412WindWarning : S412WeatherWarning
{
    /// <inheritdoc/>
    public override S412WarningKind Kind => S412WarningKind.Wind;

    /// <summary>The threshold exceeded (<c>windWarningThreshold</c>), or <c>null</c>.</summary>
    public S412WindWarningThreshold? Threshold { get; init; }

    /// <summary>
    /// The Beaufort force code (<c>beaufortForce</c>): 1 to 12, or 99 for
    /// calm, or <c>null</c>.
    /// </summary>
    public int? BeaufortForce { get; init; }

    /// <summary>The maximum wind speed as text, one per language (<c>maximumWindSpeed</c>).</summary>
    public IReadOnlyList<S412Text> MaximumWindSpeed { get; init; } = [];
}

/// <summary>A significant wave height warning (<c>SignificantWaveHeightWarning</c>).</summary>
public sealed class S412SignificantWaveHeightWarning : S412WeatherWarning
{
    /// <inheritdoc/>
    public override S412WarningKind Kind => S412WarningKind.SignificantWaveHeight;

    /// <summary>The wave height band exceeded (<c>significantWaveHeightWarningThreshold</c>), or <c>null</c>.</summary>
    public S412SignificantWaveHeightThreshold? Threshold { get; init; }
}

/// <summary>An ice accretion warning (<c>IceAccretionWarning</c>).</summary>
public sealed class S412IceAccretionWarning : S412WeatherWarning
{
    /// <inheritdoc/>
    public override S412WarningKind Kind => S412WarningKind.IceAccretion;

    /// <summary>The rate of icing (<c>iceAccretionRate</c>), or <c>null</c>.</summary>
    public S412IceAccretionRate? Rate { get; init; }

    /// <summary>The source of icing (<c>sourceOfIcing</c>), or <c>null</c>.</summary>
    public S412SourceOfIcing? SourceOfIcing { get; init; }
}

/// <summary>A restricted visibility warning (<c>RestrictedVisibilityWarning</c>).</summary>
public sealed class S412RestrictedVisibilityWarning : S412WeatherWarning
{
    /// <inheritdoc/>
    public override S412WarningKind Kind => S412WarningKind.RestrictedVisibility;
}

/// <summary>A thunderstorm warning (<c>ThunderstormWarning</c>).</summary>
public sealed class S412ThunderstormWarning : S412WeatherWarning
{
    /// <inheritdoc/>
    public override S412WarningKind Kind => S412WarningKind.Thunderstorm;
}

/// <summary>
/// The service that issued and prepared warnings (the <c>DataProvider</c>
/// information type).
/// </summary>
public sealed class S412DataProvider
{
    /// <summary>The information type's <c>gml:id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The METAREA code (1 to 14, <c>issuingService/metareaName</c>), or <c>null</c>.</summary>
    public int? MetareaCode { get; init; }

    /// <summary>The issuing service's country (<c>issuingService/countryName</c>), or <c>null</c>.</summary>
    public string? CountryName { get; init; }

    /// <summary>The issuing service's name, one per language (<c>issuingService/issuingServiceName</c>).</summary>
    public IReadOnlyList<S412Text> IssuingServiceNames { get; init; } = [];

    /// <summary>The agency name (<c>contactInformation/agencyName</c>), or <c>null</c>.</summary>
    public string? AgencyName { get; init; }

    /// <summary>The mailing address (<c>contactInformation/mailingAddress</c>), or <c>null</c>.</summary>
    public string? MailingAddress { get; init; }

    /// <summary>The telephone number (<c>contactInformation/telephoneNumber</c>), or <c>null</c>.</summary>
    public string? TelephoneNumber { get; init; }

    /// <summary>The email address (<c>contactInformation/emailAddress</c>), or <c>null</c>.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>The web address (<c>contactInformation/webAddress</c>), or <c>null</c>.</summary>
    public string? WebAddress { get; init; }

    /// <summary>The preparing service's name, one per language (<c>preparationService/issuingServiceName</c>).</summary>
    public IReadOnlyList<S412Text> PreparationServiceNames { get; init; } = [];

    /// <summary>The preparing service's country (<c>preparationService/countryName</c>), or <c>null</c>.</summary>
    public string? PreparationCountryName { get; init; }

    /// <summary>The information type this provider was projected from.</summary>
    public required S412InformationType Source { get; init; }
}
