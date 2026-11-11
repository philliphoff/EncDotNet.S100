# EncDotNet.S100.Nmea

`EncDotNet.S100.Nmea` is a minimal NMEA 0183 reader: it checks sentences,
decodes the wind, temperature, pressure, heading and motion sentences, and
reads lines from a stream, a recording, TCP or UDP. It has no dependency
beyond [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) (for
`Angle`, `Speed` and `GeoPosition`), so the
[NMEA weather source](../EncDotNet.S100.DynamicSources.Nmea/README.md) and,
later, an own-ship NMEA driver can share it.

Not for navigation.

## Install

This package isn't published to NuGet. Reference the project directly:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.Nmea/EncDotNet.S100.Nmea.csproj" />
```

## Example: read wind from a multiplexer

```csharp
using EncDotNet.S100.Nmea;

var input = new NmeaUdpLineSource(port: 10110);
await foreach (var line in input.ReadLinesAsync(cancellationToken))
{
    if (!NmeaSentence.TryParse(line, out var sentence, out var error))
        continue; // error says why: ChecksumMismatch, MissingChecksum, ...

    if (NmeaDecoder.TryDecode(sentence, out var message) && message is MwvSentence { IsValid: true } mwv)
        Console.WriteLine($"{mwv.Reference} wind {mwv.Angle?.TotalDegrees}° {mwv.Speed?.TotalKnots:0.0} kn");
}
```

## Main entry points

| Type | Purpose |
| --- | --- |
| `NmeaSentence` | One sentence: talker, formatter and fields. `TryParse` checks the `*hh` checksum, and rejects a line without one by default, since a cut-off sentence has none. It skips an NMEA 4 tag block, keeps the last sentence when a cut-off one is glued to it, and reports why a line was rejected (`NmeaParseError`). `Format` builds a sentence with its checksum. |
| `NmeaDecoder` | Decodes by formatter, whatever the talker: `MWV` (wind angle and speed, relative or true), `MWD` (true and magnetic wind direction and speed), `MTW` (water temperature), `XDR` (transducers), `HDT`, `HDG` (with deviation and variation), `VHW` (heading and speed through the water), `RMC` (position, time, speed and course over the ground, variation) and `VTG`. Speeds are converted from knots, m/s, km/h or statute mph. |
| `XdrSentence` | All of an `XDR`'s measurements, plus `AirTemperatureCelsius` and `PressureHectopascals`: the outside air temperature and barometric pressure, picked by type, unit, range and a name that doesn't mark them as an engine's, a tank's or a cabin's. |
| `TrueWind` | True wind from apparent wind, over the ground (heading, SOG and COG) or through the water (heading and speed through water). |
| `INmeaLineSource` | Where lines come from. `NmeaStreamLineSource` (any stream, such as a serial port's), `NmeaReplayFile` (a recording, optionally paced and looped), `NmeaUdpLineSource` and `NmeaTcpLineSource` (reconnects). Each enumeration opens the input and closes it when it ends. |

## True wind: over the ground or through the water

`TrueWind.OverGround` uses the speed and course over the ground: it gives the
wind a buoy or a forecast reports. `TrueWind.ThroughWater` uses the speed
through the water along the heading: the sailor's true wind, which differs
from the ground wind by the current. Both need the heading, since the
apparent angle is measured from the bow. Neither corrects for mast motion,
upwash or leeway.

## See also

- [Wind and wave data](../../docs/design/weather-sources.md), §6.2: the
  design note.
- [EncDotNet.S100.DynamicSources.Nmea](../EncDotNet.S100.DynamicSources.Nmea/README.md):
  own-ship wind on the chart.
