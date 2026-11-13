using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Nmea;

namespace EncDotNet.S100.DynamicSources.Nmea.Tests;

/// <summary><see cref="S100Sources.NmeaWeather"/> and <see cref="NmeaInput"/>.</summary>
public sealed class NmeaWeatherSourceRegistrationTests
{
    [Fact]
    public async Task The_registration_reads_the_input_in_its_options()
    {
        var path = NmeaWeatherSourceTests.FixturePath("ownship-weather.nmea");

        await using var processor = Assert.IsAssignableFrom<IHostedDatasetProcessor>(SourceRegistry.Default.Create(
            S100Sources.NmeaWeatherId,
            new SourceCreationContext { Options = new NmeaWeatherSourceSettings { Input = path } }));

        var source = Assert.IsType<NmeaWeatherSource>(Assert.IsType<DynamicSourceProcessor>(processor).Source);
        Assert.Equal(new SpecRef(SpecName.Nmea, default), processor.Spec);
        Assert.IsType<NmeaReplayFile>(source.Input);
    }

    [Theory]
    [InlineData("udp", typeof(NmeaUdpLineSource))]
    [InlineData("udp:10111", typeof(NmeaUdpLineSource))]
    [InlineData("udp://127.0.0.1:10110", typeof(NmeaUdpLineSource))]
    [InlineData("tcp://localhost:10110", typeof(NmeaTcpLineSource))]
    [InlineData("file:///tmp/log.nmea", typeof(NmeaReplayFile))]
    [InlineData("log.nmea", typeof(NmeaReplayFile))]
    public void Inputs_parse_by_scheme(string spec, Type expected)
    {
        Assert.IsType(expected, NmeaInput.Parse(spec));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("udp:0")]
    [InlineData("bogus://x")]
    public void Unusable_inputs_parse_to_null(string? spec)
    {
        Assert.Null(NmeaInput.Parse(spec));
    }
}
