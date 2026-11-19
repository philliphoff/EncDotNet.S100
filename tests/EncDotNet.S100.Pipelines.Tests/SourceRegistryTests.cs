using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="SourceRegistry"/>, <see cref="SourceActivation"/> and
/// <see cref="SourceCreationContext"/>.
/// </summary>
public sealed class SourceRegistryTests
{
    private static SourceRegistration Registration(string id, Func<SourceCreationContext, IDatasetProcessor>? create = null) => new()
    {
        Id = id,
        DisplayName = "Test",
        Spec = SpecName.Ndbc,
        Create = create ?? (_ => throw new NotSupportedException()),
    };

    [Fact]
    public void A_host_can_add_a_source_and_replace_a_built_in_one_in_place()
    {
        var registry = SourceRegistry.CreateDefault()
            .AddDataSource(Registration("acme.ice-feed"));
        var wrapped = S100Sources.AisStream with { DisplayName = "Vessels (wrapped)" };

        registry.AddDataSource(wrapped, replace: true);

        Assert.Equal(
            [S100Sources.NdbcObservationsId, S100Sources.NmeaWeatherId, S100Sources.AisStreamId, "acme.ice-feed"],
            registry.Registrations.Select(r => r.Id));
        Assert.Same(wrapped, registry.Resolve(S100Sources.AisStreamId));
        Assert.Throws<ArgumentException>(() => registry.AddDataSource(Registration("acme.ice-feed")));
        Assert.Same(registry, registry.AddDataSource(Registration("acme.new"), replace: true));
        Assert.Equal("acme.new", registry.Registrations[^1].Id);
    }

    [Fact]
    public void AIS_needs_a_key_and_runs_where_its_options_say()
    {
        var ais = S100Sources.AisStream;

        Assert.Equal(SpecName.Ais, ais.Spec);
        Assert.Equal(SourceGroup.Vessels, ais.Group);
        Assert.Equal(50, ais.ActivationFor(null).MaxViewportSpanDegrees);
        Assert.Same(SourceActivation.Always, ais.ActivationFor(new AisFeedOptions { ActivationSpanDegrees = 0 }));
        var key = Assert.Single(SourceOptionsForm.Fields(ais), f => f.Kind == SourceOptionKind.Secret);
        Assert.Equal(AisFeedOptions.KeyEnvironmentVariable, key.EnvironmentVariable);

        Assert.Equal(key.Name, SourceOptionsForm.MissingRequired(ais, null, _ => null)?.Name);
        Assert.Null(SourceOptionsForm.MissingRequired(ais, null, _ => "from-env"));
        Assert.Null(SourceOptionsForm.MissingRequired(ais, new Dictionary<string, string> { ["ApiKey"] = "k" }, _ => null));

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AisFeedOptions.KeyEnvironmentVariable)))
            return; // A real key on this machine would start a subscription.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ais.Create(new SourceCreationContext { Options = new AisFeedOptions() }));
        Assert.Contains(AisFeedOptions.KeyEnvironmentVariable, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_registry_has_the_built_in_sources()
    {
        var ids = SourceRegistry.Default.Registrations.Select(r => r.Id).ToList();

        Assert.Equal([S100Sources.NdbcObservationsId, S100Sources.NmeaWeatherId, S100Sources.AisStreamId], ids);
        Assert.All(SourceRegistry.Default.Registrations, r => Assert.False(r.IsS100));
    }

    [Fact]
    public void Ids_resolve_ignoring_case_and_duplicates_are_refused()
    {
        var registry = new SourceRegistry();
        var registration = Registration("acme.feed");
        registry.Register(registration);

        Assert.True(registry.TryResolve("ACME.Feed", out var found));
        Assert.Same(registration, found);
        Assert.False(registry.TryResolve("acme.other", out _));
        Assert.Throws<NotSupportedException>(() => registry.Resolve("acme.other"));
        Assert.Throws<ArgumentException>(() => registry.Register(Registration("Acme.Feed")));
    }

    [Fact]
    public void Create_passes_the_default_options_when_the_host_saved_none()
    {
        var registry = new SourceRegistry();
        object? seen = null;
        var defaults = new object();
        var registration = Registration("acme.feed", c =>
        {
            seen = c.Options;
            throw new InvalidOperationException("stop");
        });
        registry.Register(registration with { DefaultOptions = defaults });

        Assert.Throws<InvalidOperationException>(() => registry.Create("acme.feed", new SourceCreationContext()));
        Assert.Same(defaults, seen);
    }

    [Theory]
    [InlineData(-71.0, 42.0, -70.0, 43.0, true)]
    [InlineData(-75.0, 40.0, -70.0, 43.0, false)]
    [InlineData(179.5, 10.0, -179.5, 11.0, true)]
    public void BelowViewportSpan_runs_only_in_a_narrow_view(double west, double south, double east, double north, bool active)
    {
        var activation = SourceActivation.BelowViewportSpan(2);

        Assert.Equal(active, activation.IsActiveFor(new GeographicBounds(west, south, east, north)));
        Assert.False(activation.IsActiveFor(null));
        Assert.True(SourceActivation.Always.IsActiveFor(null));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void BelowViewportSpan_needs_a_positive_span(double degrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceActivation.BelowViewportSpan(degrees));
    }

    [Fact]
    public void Own_ship_weather_needs_an_input()
    {
        Assert.Throws<ArgumentException>(() => SourceRegistry.Default.Create(S100Sources.NmeaWeatherId, new SourceCreationContext()));
        Assert.Throws<ArgumentException>(() => SourceRegistry.Default.Create(
            S100Sources.NmeaWeatherId,
            new SourceCreationContext { Options = new NmeaWeatherSourceSettings { Input = "bogus://x" } }));
    }
}
