using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// S-124 warnings in the findings list (#957): the generated "Warnings on
/// route" scenario's warnings, listed by <see cref="NavigationalWarningsService"/>,
/// checked against its imported S-421 route without a draft by
/// <see cref="WarningFindingProducer"/>, and read back through <c>get_findings</c>.
/// </summary>
public sealed class WarningFindingProducerTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class ViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; } = current;

        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    /// <summary>One S-124 dataset's warning parts, as the real processor lists them.</summary>
    private sealed class WarningProcessor(IReadOnlyList<RouteWarning> parts) : IDatasetProcessor
    {
        public string ProductSpec => "S-124";
        public SpecRef Spec { get; } = new("S-124", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            parts.Select((p, i) => new NavigationalWarningFeature(p.Feature!.FeatureId, i, p.Info)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) =>
            parts[ordinal].Shapes.FirstOrDefault() ?? new S100FeatureGeometry { Primitive = S100GeometryType.None };
    }

    private sealed record Setup(
        RoutesService Routes,
        Route Route,
        PassageCheckService Check,
        FakeDatasetLoaderService Loader,
        NavigationalWarningsService Warnings,
        RouteFindingsService Findings) : IDisposable
    {
        public void Dispose()
        {
            Findings.Dispose();
            Check.Dispose();
        }
    }

    private Setup Create(bool withWarnings = true, DateTimeOffset? viewTime = null)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var routes = new RoutesService(viewTime is { } at ? new ViewTime(at) : null);
        var route = RouteS421Mapping.FromS421(fixture.S421Route());
        route.SetPassageValue(RoutePassageField.Draft, null);
        route.SetDeparture(Departure);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);

        var loader = new FakeDatasetLoaderService();
        if (withWarnings)
            loader.Processors = Processors();
        var warnings = new NavigationalWarningsService(loader);
        var catalog = new PassageCheckFixture.FixedCatalog(fixture.Datasets);
        var check = new PassageCheckService(routes, catalog, debounce: TimeSpan.Zero);
        var findings = new RouteFindingsService(check, routes, catalog, null, timeFormat: new Utc(),
            producers: [new WarningFindingProducer(warnings)]);
        return new Setup(routes, route, check, loader, warnings, findings);
    }

    private Dictionary<DatasetEntry, IDatasetProcessor> Processors() =>
        RouteWarningSources.FromDatasets(fixture.Datasets)
            .GroupBy(w => w.Feature!.DatasetId)
            .ToDictionary(
                g => new DatasetEntry($"/tmp/{g.Key}.gml", "S-124"),
                g => (IDatasetProcessor)new WarningProcessor(g.ToList()));

    private static RouteFinding Warning(RouteFindingsService findings, int number) =>
        Assert.Single(findings.Findings, f => f.Warning?.Info.Reference?.Number == number);

    [Fact]
    public async Task The_warnings_on_the_route_are_findings_by_relation_and_time()
    {
        using var s = Create();
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();

        Assert.False(s.Check.HasDraft);
        Assert.All(s.Findings.Findings, f => Assert.Equal(RouteFindingSource.S124, f.Source));

        // Survey area crossed in force; buoy off station 70 m off; moved diving area 550 m off (within 0,5 NM).
        Assert.Equal(RouteFindingGroup.MustReview, Warning(s.Findings, 1).Group);
        Assert.Equal(RouteFindingGroup.Check, Warning(s.Findings, 3).Group);
        Assert.Equal(RouteFindingGroup.Check, Warning(s.Findings, 6).Group);

        // Cable laying later in the day; diving cancelled by 006; tow cancelled at 05:30.
        Assert.Equal((RouteWarningCodes.NotInForce, RouteFindingGroup.Info), (Warning(s.Findings, 2).Code, Warning(s.Findings, 2).Group));
        Assert.Equal((RouteWarningCodes.Cancelled, RouteFindingGroup.Info), (Warning(s.Findings, 5).Code, Warning(s.Findings, 5).Group));
        Assert.Equal((RouteWarningCodes.Cancelled, RouteFindingGroup.Info), (Warning(s.Findings, 7).Code, Warning(s.Findings, 7).Group));
        Assert.DoesNotContain(s.Findings.Findings, f => f.Warning?.Info.Reference?.Number == 4);

        var survey = Warning(s.Findings, 1);
        Assert.Equal("Inside navigational warning 001/26", survey.Title);
        // Series and number · locality · validity in the time preference · status at the ETA.
        var meta = Normalise(survey.Meta).Split(" · ");
        Assert.Equal(["ZZ00 LOCAL 001/26", "Synthetic Sound (fictitious)"], meta[..2]);
        Assert.Contains(" – ", meta[2], StringComparison.Ordinal);
        Assert.Equal("In force at 06:08Z", meta[3]);
        Assert.Equal(survey.Feature!.DatasetId, s.Warnings.Warnings.Single(w => w.FeatureRef == survey.Feature.FeatureId).DatasetId);
        Assert.NotNull(s.Warnings.Find(survey.Feature.DatasetId, survey.Feature.FeatureId));
    }

    [Fact]
    public async Task An_off_chart_warning_is_still_a_finding_tagged_with_its_classification()
    {
        using var s = Create();
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var survey = Warning(s.Findings, 1);
        var entry = s.Warnings.Find(survey.Feature!.DatasetId, survey.Feature.FeatureId)!;

        s.Warnings.SetClassification(entry, NavigationalWarningClassification.OffChart);
        await s.Findings.WhenIdle();

        survey = Warning(s.Findings, 1);
        Assert.Equal(RouteFindingGroup.MustReview, survey.Group);
        Assert.EndsWith(" · Off chart", survey.Meta, StringComparison.Ordinal);
        Assert.DoesNotContain("Off chart", Warning(s.Findings, 3).Meta, StringComparison.Ordinal);

        s.Warnings.SetClassification(entry, NavigationalWarningClassification.OnChart);
        await s.Findings.WhenIdle();
        Assert.DoesNotContain("Off chart", Warning(s.Findings, 1).Meta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_departure_or_speed_moves_warnings_in_and_out_of_force()
    {
        using var s = Create();
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.Equal(RouteFindingGroup.MustReview, Warning(s.Findings, 1).Group);
        Assert.Equal(RouteFindingGroup.Info, Warning(s.Findings, 2).Group);

        // 13:00: the survey (05–08) has ended, the cable laying (12–18) has begun.
        s.Route.SetDeparture(Departure.AddHours(7));
        await s.Findings.WhenIdle();
        Assert.Equal((RouteWarningCodes.NotInForce, RouteFindingGroup.Info), (Warning(s.Findings, 1).Code, Warning(s.Findings, 1).Group));
        Assert.Equal(RouteFindingGroup.MustReview, Warning(s.Findings, 2).Group);
        Assert.Equal("Crosses navigational warning 002/26", Warning(s.Findings, 2).Title);

        // Back at 06:00 but at 0,5 kn: the survey area, 2,1 km in, is reached after 08:00.
        s.Route.SetDeparture(Departure);
        s.Route.SetPassageValue(RoutePassageField.Speed, 0.5);
        await s.Findings.WhenIdle();
        Assert.Equal(RouteFindingGroup.Info, Warning(s.Findings, 1).Group);
        Assert.True(Warning(s.Findings, 1).Warning!.From > new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Without_a_speed_or_departure_the_warnings_are_judged_at_the_view_time()
    {
        var viewTime = new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
        using var s = Create(viewTime: viewTime);
        s.Route.SetPassageValue(RoutePassageField.Speed, null);
        s.Route.SetDeparture(null);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();

        // At 13:00 the cable laying is in force and the survey has ended.
        var cable = Warning(s.Findings, 2);
        Assert.False(cable.Warning!.AtEta);
        Assert.Equal(viewTime, cable.Warning.From);
        Assert.Equal(RouteFindingGroup.MustReview, cable.Group);
        Assert.Equal(RouteFindingGroup.Info, Warning(s.Findings, 1).Group);
    }

    [Fact]
    public async Task Without_a_speed_the_warnings_are_judged_at_the_departure()
    {
        using var s = Create(viewTime: new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero));
        s.Route.SetPassageValue(RoutePassageField.Speed, null);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();

        // At the 06:00 departure the survey is in force and the cable laying hasn't begun.
        var cable = Warning(s.Findings, 2);
        Assert.False(cable.Warning!.AtEta);
        Assert.Equal(Departure, cable.Warning.From);
        Assert.Equal(RouteFindingGroup.Info, cable.Group);
        Assert.Equal(RouteFindingGroup.MustReview, Warning(s.Findings, 1).Group);
    }

    [Fact]
    public async Task Warnings_that_load_later_re_run_the_check()
    {
        using var s = Create(withWarnings: false);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.Empty(s.Findings.Findings);

        s.Loader.Processors = Processors();
        var entry = s.Loader.Processors.Keys.First();
        s.Loader.RaiseLoaded(entry);
        await s.Findings.WhenIdle();

        Assert.Equal(RouteFindingGroup.MustReview, Warning(s.Findings, 1).Group);
    }

    [Fact]
    public async Task Get_findings_returns_the_warning_details()
    {
        using var s = Create();
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var tool = new GetFindingsTool(s.Routes, new InlineInvoker(), s.Check, s.Findings);

        var result = await tool.InvokeAsync(null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var dto), result.TryGetError(out var e) ? e.Message : "");

        var survey = Assert.Single(dto!.Findings, f => f.Warning?.Number == "ZZ00 LOCAL 001/26");
        Assert.Equal(("must_review", "s124", "S-124"), (survey.Group, survey.Source, survey.Tag));
        var warning = survey.Warning!;
        Assert.Equal(("crosses", "in_force", true), (warning.Relation, warning.Status, warning.AtEta));
        Assert.InRange(warning.From, new DateTimeOffset(2026, 3, 1, 6, 8, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 6, 9, 0, TimeSpan.Zero));
        Assert.Equal([new RouteFindingValidityDto(new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero))], warning.Validity);
        Assert.Contains("Survey vessel working in the channel", Assert.Single(warning.Text).Text, StringComparison.Ordinal);
        Assert.Equal(survey.Feature!.DatasetId, Warning(s.Findings, 1).Feature!.DatasetId);

        var tow = Assert.Single(dto.Findings, f => f.Warning?.Number == "ZZ00 LOCAL 007/26");
        Assert.Equal(("info", "cancelled"), (tow.Group, tow.Warning!.Status));
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 5, 30, 0, TimeSpan.Zero), tow.Warning.CancellationDate);
        Assert.All(dto.Findings, f => Assert.NotNull(f.Warning));
    }

    // Narrow no-break spaces from some ICU versions' time formats.
    private static string Normalise(string text) => text.Replace(' ', ' ').Replace(' ', ' ');
}

/// <summary>Writes "Warnings on route" (#942, S-124 Edition 2.0.0) once for the test class.</summary>
public sealed class WarningsScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-viewer-warnings-" + Guid.NewGuid().ToString("N"));

    public WarningsScenarioFixture()
    {
        Scenario = ScenarioGenerator.Generate(SyntheticScenarios.WarningsOnRoute(), _root);
        var catalogue = ExchangeCatalogueReader.Read(Scenario.CataloguePath);
        Datasets = FileDatasetCatalog.Build(
            catalogue.DatasetDiscoveryMetadata
                .Select(d => new FileDatasetInput(
                    new DatasetId(Path.GetFileNameWithoutExtension(d.RelativePath)),
                    d.ProductSpecification!.ProductIdentifier!,
                    Path.Combine(Scenario.Root, d.RelativePath)))
                .ToList(),
            new ProjNetCrsTransformFactory()).Datasets;
    }

    public GeneratedScenario Scenario { get; }

    public IReadOnlyList<LoadedDataset> Datasets { get; }

    /// <summary>The route as the viewer imports it from its S-421 file.</summary>
    public S421Route S421Route()
    {
        var path = Scenario.Datasets.Single(d => d.ProductIdentifier == "S-421").FullPath;
        return S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
