using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
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
    public void Sample_ReadsItsIncompleteTimesAndSaysSo()
    {
        // The upstream sample writes 20250101 for date-times (read as the
        // start of that day) and 0600 for a time of day (hhmm, not the
        // complete hhmmss the encoding guide asks for).
        var model = S412MarineWeatherWarnings.From(S412Dataset.Open(S412DatasetReaderTests.SampleFile), out var diagnostics);

        var wind = Assert.IsType<S412WindWarning>(Assert.Single(model.Warnings));
        Assert.Equal(S412WindWarningThreshold.StormForce, wind.Threshold);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 6, 0, 0, TimeSpan.Zero), wind.FixedDateRange!.Start);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 18, 0, 0, TimeSpan.Zero), wind.FixedDateRange.End);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), wind.CancellationDate);
        Assert.All(diagnostics, d => Assert.Equal("attribute.time.incomplete", d.Code));
        Assert.Equal(
            ["fixedDateRange.dateStart", "fixedDateRange.dateEnd"],
            diagnostics.Select(d => d.RelatedAttribute));
        Assert.Equal("UK Met Office", model.DataProviders[0].IssuingServiceNames[0].Text);
        Assert.Same(model.DataProviders[0], wind.DataProvider);
    }

    [Fact]
    public void Sample_IsCancelledBeforeItStarts()
    {
        // Its cancellationDate (2025-01-01, midnight) is before its range
        // starts (06:00), so it is never in force.
        var model = S412MarineWeatherWarnings.From(S412Dataset.Open(S412DatasetReaderTests.SampleFile), out _);
        var wind = model.Warnings[0];

        Assert.Equal(NavigationalWarningStatus.Ended, wind.StatusAt(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        Assert.Empty(model.DrawnAt(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        Assert.Single(model.DrawnAt(null));
    }

    [Theory]
    [InlineData("2026-11-07T05:59:59Z", NavigationalWarningStatus.NotYetInForce)]
    [InlineData("2026-11-07T06:00:00Z", NavigationalWarningStatus.InForce)]
    [InlineData("2026-11-08T17:59:59Z", NavigationalWarningStatus.InForce)]
    [InlineData("2026-11-08T18:00:00Z", NavigationalWarningStatus.Ended)]
    public void WindWarning_StatusAt_FollowsRangeAndCancellation(string time, NavigationalWarningStatus expected)
    {
        // Range 2026-11-07 06:00 to 2026-11-08 18:00, cancelled 2026-11-08
        // 18:00: the cancellation ends it at 18:00, where the range alone
        // would include it.
        var wind = Synthetic(out _).Warnings[0];

        Assert.Equal(expected, wind.StatusAt(DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Changes_AreTheInstantsTheWarningsInForceChange()
    {
        var model = Synthetic(out _);

        // Each warning's cancellation falls on the end of its range, so the
        // tick after each range end changes nothing and isn't listed.
        Assert.Equal(
            [
                new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc),   // VIS1, WAVE1, TS1, ICE1 start
                new DateTime(2026, 11, 7, 6, 0, 0, DateTimeKind.Utc),   // WIND1 starts
                new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),  // VIS1 cancelled
                new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),   // TS1 cancelled
                new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc),  // WIND1 cancelled
                new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),   // WAVE1 cancelled
                new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),  // ICE1 cancelled
            ],
            model.Changes);
    }

    [Fact]
    public void DrawnAt_KeepsOnlyWarningsInForce()
    {
        var model = Synthetic(out _);

        Assert.Equal(
            ["WAVE1", "VIS1", "TS1", "ICE1"],
            model.DrawnAt(new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero)).Select(w => w.Id));
        Assert.Equal(
            ["WIND1", "WAVE1", "ICE1"],
            model.DrawnAt(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero)).Select(w => w.Id));
        Assert.Empty(model.DrawnAt(new DateTimeOffset(2026, 11, 10, 0, 0, 1, TimeSpan.Zero)));
    }

    [Fact]
    public void TimeCoverage_IssueAndNextUpdate()
    {
        var model = Synthetic(out _);

        Assert.Equal(
            new TimeCoverage(new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)),
            model.TimeCoverage);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 5, 30, 0, TimeSpan.Zero), model.IssuedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), model.NextUpdateDateTime);
        Assert.Equal(model.TimeCoverage, S412Dataset.ReadMetadata(S412DatasetReaderTests.SyntheticFile).TimeCoverage);
    }
}
