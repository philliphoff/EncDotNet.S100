using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary><see cref="SourceOptionsForm"/>: a source's settings page from its options type (design note D8).</summary>
public sealed class SourceOptionsFormTests
{
    public enum Mode
    {
        Fast,
        Careful,
    }

    public sealed record TestOptions
    {
        [SourceOption("API key", Required = true, Order = -1)]
        [Secret(EnvironmentVariable = "TEST_KEY")]
        public string? ApiKey { get; init; }

        [SourceOption("Zoom")]
        public double Span { get; init; } = 2.5;

        [SourceOption("Every")]
        public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(10);

        [SourceOption("Mode")]
        public Mode Kind { get; init; } = Mode.Careful;

        [SourceOption("On")]
        public bool Enabled { get; init; } = true;

        [SourceOption("Server")]
        public Uri? Server { get; init; }

        public string Hidden { get; init; } = "kept";
    }

    private static SourceRegistration Registration(object? defaults = null) => new()
    {
        Id = "test.feed",
        DisplayName = "Test",
        Spec = SpecName.Ndbc,
        OptionsType = typeof(TestOptions),
        DefaultOptions = defaults,
        Create = _ => throw new NotSupportedException(),
    };

    [Fact]
    public void Fields_are_the_marked_options_in_order()
    {
        var fields = SourceOptionsForm.Fields(Registration());

        Assert.Equal(["ApiKey", "Span", "Interval", "Kind", "Enabled", "Server"], fields.Select(f => f.Name));
        var key = fields[0];
        Assert.Equal(SourceOptionKind.Secret, key.Kind);
        Assert.True(key.Required);
        Assert.Equal("TEST_KEY", key.EnvironmentVariable);
        Assert.Equal(
            [SourceOptionKind.Number, SourceOptionKind.Duration, SourceOptionKind.Choice, SourceOptionKind.Boolean, SourceOptionKind.Url],
            fields.Skip(1).Select(f => f.Kind));
        Assert.Equal(["Fast", "Careful"], fields[3].Choices);
    }

    [Fact]
    public void A_source_without_options_has_no_fields()
    {
        Assert.Empty(SourceOptionsForm.Fields(Registration() with { OptionsType = null }));
        Assert.Null(SourceOptionsForm.Build(Registration() with { OptionsType = null }, new Dictionary<string, string> { ["x"] = "1" }));
    }

    [Fact]
    public void Values_round_trip_as_invariant_text()
    {
        var registration = Registration();
        var options = new TestOptions
        {
            ApiKey = "sc-secret:test.feed.apiKey",
            Span = 0.75,
            Interval = TimeSpan.FromSeconds(90),
            Kind = Mode.Fast,
            Enabled = false,
            Server = new Uri("https://example.test/feed"),
        };

        var values = SourceOptionsForm.Read(registration, options);
        Assert.Equal("0.75", values["Span"]);
        Assert.Equal("00:01:30", values["Interval"]);
        Assert.Equal("false", values["Enabled"]);
        Assert.False(values.ContainsKey("Hidden"));

        Assert.Equal(options, SourceOptionsForm.Build(registration, values));
    }

    [Fact]
    public void Building_starts_from_the_defaults()
    {
        var registration = Registration(new TestOptions { Hidden = "from defaults", Span = 4 });

        var options = Assert.IsType<TestOptions>(SourceOptionsForm.Build(registration, new Dictionary<string, string> { ["Kind"] = "fast", ["Nope"] = "x" }));

        Assert.Equal(Mode.Fast, options.Kind);
        Assert.Equal(4, options.Span);
        Assert.Equal("from defaults", options.Hidden);
        Assert.Same(registration.DefaultOptions, SourceOptionsForm.Build(registration, null));
    }

    [Fact]
    public void A_bad_value_names_its_option()
    {
        var ex = Assert.Throws<FormatException>(() =>
            SourceOptionsForm.Build(Registration(), new Dictionary<string, string> { ["Span"] = "wide" }));

        Assert.Contains("Zoom", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Span", "1.5", true)]
    [InlineData("Span", "abc", false)]
    [InlineData("Span", "", true)]
    [InlineData("ApiKey", " ", false)]
    [InlineData("Interval", "00:05:00", true)]
    [InlineData("Interval", "-00:05:00", false)]
    [InlineData("Kind", "careful", true)]
    [InlineData("Kind", "slow", false)]
    [InlineData("Server", "not a url", false)]
    [InlineData("Enabled", "true", true)]
    public void Validation_follows_the_kind(string name, string text, bool valid)
    {
        var field = SourceOptionsForm.Fields(Registration()).Single(f => f.Name == name);

        Assert.Equal(valid, SourceOptionsForm.IsValid(field, text));
    }

    [Fact]
    public void Activation_can_come_from_the_options()
    {
        var registration = Registration(new TestOptions()) with
        {
            ActivationFromOptions = o => o is TestOptions { Span: > 0 } t ? SourceActivation.BelowViewportSpan(t.Span) : SourceActivation.Always,
        };

        Assert.Equal(2.5, registration.ActivationFor(null).MaxViewportSpanDegrees);
        Assert.Equal(1, registration.ActivationFor(new TestOptions { Span = 1 }).MaxViewportSpanDegrees);
        Assert.Same(SourceActivation.Always, Registration().ActivationFor(null));
    }

    [Fact]
    public void NMEA_weather_asks_for_its_input()
    {
        var field = Assert.Single(SourceOptionsForm.Fields(S100Sources.NmeaWeather));

        Assert.Equal(nameof(NmeaWeatherSourceSettings.Input), field.Name);
        Assert.True(field.Required);
        Assert.Equal("udp:10110", Assert.IsType<NmeaWeatherSourceSettings>(
            SourceOptionsForm.Build(S100Sources.NmeaWeather, new Dictionary<string, string> { ["Input"] = "udp:10110" })).Input);
    }
}
