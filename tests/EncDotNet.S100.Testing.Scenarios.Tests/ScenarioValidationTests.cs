namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// Every generated dataset opens through the exchange set and passes its
/// product's rule pack with no findings.
/// </summary>
public abstract class ScenarioValidationTests(GeneratedScenarioFixture fixture)
{
    [Fact]
    public async Task The_exchange_set_lists_every_dataset()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(fixture.Scenario.Root, TestContext.Current.CancellationToken);

        Assert.StartsWith($"SYNTHETIC {fixture.Definition.Name} ", exchangeSet.Catalogue.Identifier.Identifier, StringComparison.Ordinal);
        Assert.Equal(
            fixture.Scenario.Datasets.Select(d => d.RelativePath),
            exchangeSet.Datasets.Select(d => d.Metadata.RelativePath));
        Assert.All(exchangeSet.Datasets, d => Assert.NotNull(d.Metadata.BoundingBox));
    }

    [Fact]
    public async Task Every_dataset_validates_clean()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(fixture.Scenario.Root, TestContext.Current.CancellationToken);

        var reports = new List<string>();
        foreach (var entry in exchangeSet.Datasets)
        {
            using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
            var report = dataset.Validate();

            Assert.NotNull(report);
            Assert.True(report.RulesEvaluated > 0, entry.Metadata.RelativePath);
            reports.AddRange(report.Findings.Select(f => $"{entry.Metadata.RelativePath}: {f.RuleId} {f.Severity} {f.Message}"));
        }

        Assert.True(reports.Count == 0, string.Join(Environment.NewLine, reports.Take(20)));
    }

    [Fact]
    public void Every_dataset_loads_into_the_catalog()
    {
        var catalog = ScenarioLoading.Catalog(fixture.Scenario);

        Assert.Equal(fixture.Scenario.Datasets.Count, catalog.Datasets.Count);
    }
}

public sealed class SpitAcrossScalesValidationTests(SpitAcrossScalesFixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<SpitAcrossScalesFixture>;

public sealed class SpitCrossingValidationTests(SpitCrossingFixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<SpitCrossingFixture>;

public sealed class ApproachWithTideWindowValidationTests(ApproachWithTideWindowFixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<ApproachWithTideWindowFixture>;

public sealed class WarningsOnRouteValidationTests(WarningsOnRouteFixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<WarningsOnRouteFixture>;

public sealed class WarningsOnRouteEdition1ValidationTests(WarningsOnRouteEdition1Fixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<WarningsOnRouteEdition1Fixture>;

public sealed class PortPlanVsOwnRouteValidationTests(PortPlanVsOwnRouteFixture fixture)
    : ScenarioValidationTests(fixture), IClassFixture<PortPlanVsOwnRouteFixture>;
