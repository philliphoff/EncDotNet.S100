using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

/// <summary>
/// <see cref="S100Sources.NdbcObservations"/>: the processor built from the
/// registration, run as an <see cref="IHostedDatasetProcessor"/>.
/// </summary>
public sealed class NdbcSourceRegistrationTests
{
    private static readonly GeographicBounds NewEngland = new(-71.5, 40.5, -69.5, 43.0);

    private readonly FakeTimeProvider _time = new(NdbcFixtures.NewestTime(NdbcFixtures.Buoy) + TimeSpan.FromMinutes(10));

    private IHostedDatasetProcessor Create(object? options = null) =>
        Assert.IsAssignableFrom<IHostedDatasetProcessor>(SourceRegistry.Default.Create(
            S100Sources.NdbcObservationsId,
            new SourceCreationContext
            {
                Options = options,
                TimeProvider = _time,
                CreateHttpClient = () => new HttpClient(FakeNdbcHandler.WithFixtures()),
            }));

    [Fact]
    public async Task The_registration_builds_a_hosted_processor_that_fetches_the_area_in_view()
    {
        await using var processor = Create();
        var source = Assert.IsType<NdbcObservationSource>(Assert.IsType<DynamicSourceProcessor>(processor).Source);

        processor.UpdateArea(NewEngland);
        await source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new SpecRef(SpecName.Ndbc, default), processor.Spec);
        Assert.Equal(40.5, source.Area!.SouthLatitude);
        Assert.Equal(-69.5, source.Area.EastLongitude);
        Assert.Equal(["ndbc:44013", "ndbc:BUZM3"], processor.EnumerateFeatures().Select(f => f.FeatureRef));
    }

    [Fact]
    public async Task IsActive_pauses_the_source()
    {
        await using var processor = Create();
        var source = (NdbcObservationSource)((DynamicSourceProcessor)processor).Source;

        processor.IsActive = false;

        Assert.False(source.IsActive);
        Assert.False(processor.IsActive);
    }

    [Fact]
    public async Task The_saved_options_reach_the_source()
    {
        await using var processor = Create(new NdbcObservationSourceOptions { Id = "ndbc-2", DisplayName = "Buoys" });

        var source = (NdbcObservationSource)((DynamicSourceProcessor)processor).Source;
        Assert.Equal("ndbc-2", source.Id);
    }

    [Fact]
    public void Options_of_another_type_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Create(new object()));
    }

    [Fact]
    public async Task Disposing_the_processor_disposes_the_source()
    {
        var processor = Create();
        var source = (NdbcObservationSource)((DynamicSourceProcessor)processor).Source;

        await processor.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PollAsync(TestContext.Current.CancellationToken));
    }
}
