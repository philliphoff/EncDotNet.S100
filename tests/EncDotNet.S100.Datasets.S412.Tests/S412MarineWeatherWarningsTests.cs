using EncDotNet.S100.Datasets.S412.DataModel;

namespace EncDotNet.S100.Datasets.S412.Tests;

public class S412MarineWeatherWarningsTests
{
    private static S412MarineWeatherWarnings Synthetic(out IReadOnlyList<EncDotNet.S100.DataModel.ProjectionDiagnostic> diagnostics) =>
        S412MarineWeatherWarnings.From(S412Dataset.Open(S412DatasetReaderTests.SyntheticFile), out diagnostics);

    [Fact]
    public void Synthetic_ProjectsEveryKindWithoutDiagnostics()
    {
        var model = Synthetic(out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            [S412WarningKind.Wind, S412WarningKind.SignificantWaveHeight, S412WarningKind.RestrictedVisibility,
             S412WarningKind.Thunderstorm, S412WarningKind.IceAccretion],
            model.Warnings.Select(w => w.Kind));
    }

    [Fact]
    public void WindWarning_TypedAttributes()
    {
        var wind = Assert.IsType<S412WindWarning>(Synthetic(out _).Warnings[0]);

        Assert.Equal(S412WindWarningThreshold.GaleForce, wind.Threshold);
        Assert.Equal(8, wind.BeaufortForce);
        Assert.Equal("45 knots", Assert.Single(wind.MaximumWindSpeed).Text);
        Assert.Equal("W-001", wind.WeatherWarningNumber);
        Assert.Equal(new DateOnly(2026, 11, 7), wind.IssueDate);
        Assert.Equal("05:30:00Z", wind.IssueTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero), wind.CancellationDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), wind.NextUpdateDateTime);
    }

    [Fact]
    public void WindWarning_MessagesInEachLanguage()
    {
        var wind = Synthetic(out _).Warnings[0];

        Assert.Collection(
            wind.Messages,
            m => Assert.Equal(("eng", "Gale warning: southwest 8, increasing 9 later."), (m.Language, m.Text)),
            m => Assert.Equal("fra", m.Language));
    }

    [Fact]
    public void WindWarning_FixedDateRangeMoments()
    {
        var range = Synthetic(out _).Warnings[0].FixedDateRange!;

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero), range.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero), range.End);
    }

    [Fact]
    public void Warnings_ResolveTheirDataProvider()
    {
        var model = Synthetic(out _);

        var provider = Assert.Single(model.DataProviders);
        Assert.Equal(1, provider.MetareaCode);
        Assert.Equal("Example", provider.CountryName);
        Assert.Equal(["Example Met Service", "Service météo exemple"], provider.IssuingServiceNames.Select(n => n.Text));
        Assert.Equal("Example Marine Forecast Desk", provider.AgencyName);
        Assert.Equal("forecast@example.org", provider.EmailAddress);
        Assert.Same(provider, model.Warnings[0].DataProvider);
        Assert.Same(provider, model.Warnings[1].DataProvider);
        Assert.Null(model.Warnings[2].DataProvider);
    }

    [Fact]
    public void OtherKinds_TypedAttributes()
    {
        var warnings = Synthetic(out _).Warnings;

        Assert.Equal(S412SignificantWaveHeightThreshold.SixToNineMetres, Assert.IsType<S412SignificantWaveHeightWarning>(warnings[1]).Threshold);
        var ice = Assert.IsType<S412IceAccretionWarning>(warnings[4]);
        Assert.Equal(S412IceAccretionRate.SevereIcing, ice.Rate);
        Assert.Equal(S412SourceOfIcing.FreezingSpray, ice.SourceOfIcing);
        Assert.Equal(5, ice.ExteriorRing.Count);
    }

    [Fact]
    public void Sample_ReportsTheValuesItCannotRead()
    {
        // The upstream sample writes 20250101 for date-times (a date is read)
        // and 0600 for a time of day (not an S-100 time).
        var model = S412MarineWeatherWarnings.From(S412Dataset.Open(S412DatasetReaderTests.SampleFile), out var diagnostics);

        var wind = Assert.IsType<S412WindWarning>(Assert.Single(model.Warnings));
        Assert.Equal(S412WindWarningThreshold.StormForce, wind.Threshold);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), wind.CancellationDate);
        Assert.Null(wind.FixedDateRange!.Start);
        Assert.Equal(
            ["fixedDateRange.dateStart", "fixedDateRange.dateEnd"],
            diagnostics.Select(d => d.RelatedAttribute));
        Assert.Equal("UK Met Office", model.DataProviders[0].IssuingServiceNames[0].Text);
        Assert.Same(model.DataProviders[0], wind.DataProvider);
    }
}
