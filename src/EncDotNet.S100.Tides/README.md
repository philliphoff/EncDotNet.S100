# EncDotNet.S100.Tides

`EncDotNet.S100.Tides` predicts the astronomical tide at a station from its
harmonic constituents. It gives the predicted height at a time, a series of
heights at a fixed step, and the high and low waters in a period. Reference
it when you have a station's constituents, such as NOAA's, and need a water
level where there's no S-104 dataset.

The package is pure managed code with no dependencies, and is trimming and
NativeAOT compatible. Times are `DateTime` in UTC and heights are metres,
as in `EncDotNet.S100.Navigation`'s water-level types.

## Install

```bash
dotnet add package EncDotNet.S100.Tides
```

## Describe a station

A station is its identifier, its constituents and the datum its predictions
are given on. Each constituent has its NOAA name, its amplitude in metres and
its phase lag in degrees, referred to Greenwich (NOAA's `phase_GMT`):

```csharp
using EncDotNet.S100.Tides;

var seattle = new TideStation(
    "9447130",
    "Seattle",
    [
        new TideConstituent("M2", 1.063, 10.8),
        new TideConstituent("K1", 0.834, 276.8),
        new TideConstituent("O1", 0.459, 254.6),
        // ... the station's other constituents
    ],
    TideDatum.MeanSeaLevel);
```

`TideConstituent.FromLocalPhase` converts a phase referred to a time zone,
such as NOAA's `phase_local`, to a Greenwich phase.

The constituents describe the tide about mean sea level (MSL). To predict on
another datum, give the height of MSL above it. For a US station's chart
datum, mean lower low water (MLLW), that's MSL minus MLLW from the station's
datums:

```csharp
var onChartDatum = seattle.WithDatum(new TideDatum("MLLW", 4.443 - 2.419));
```

The predictor knows the 37 constituents NOAA publishes at most stations
(M2, S2, N2, K1, … , MS4) and the further 83 shallow-water and minor
constituents it lists at some, such as Anchorage. `HarmonicConstituents.Names`
lists them, and a station with any other name throws `ArgumentException`.

## Predict heights

```csharp
var predictor = new TidePredictor(onChartDatum);
var now = DateTime.UtcNow;

double height = predictor.HeightAt(now);   // metres above MLLW
double rate = predictor.RateAt(now);       // metres per hour, positive while rising
```

For a series, pass a span to fill. `Predict` doesn't allocate per sample:

```csharp
var week = new double[7 * 24 * 10];
predictor.Predict(now, TimeSpan.FromMinutes(6), week);
```

## Find high and low waters

```csharp
foreach (var extreme in predictor.FindExtremes(now, now.AddDays(2)))
{
    Console.WriteLine($"{extreme.Kind} {extreme.Time:u} {extreme.HeightMeters:F2} m");
}
```

Each high or low is a root of the rate of rise, refined to under a second.
A high and a low less than a millimetre apart in height
(`TidePredictor.MinimumTurnMeters`) are a stand, not a turn of the tide, and
are left out.

## How predictions are made

The height is the sum of the constituents' cosines about the datum's mean,
with each constituent's speed, equilibrium argument (V₀ + u) and node factor
(f) from Schureman, *Manual of Harmonic Analysis and Prediction of Tides*
(US Coast and Geodetic Survey Special Publication 98). As in NOAA's practice,
V₀ is taken at the start of each calendar year and f and u at its middle.

Against NOAA's own published predictions for 2026 at Seattle, Boston,
Pensacola, Anchorage and Astoria, the heights agree to under a millimetre
RMS at 6-minute steps, and the high and low waters to under a minute and
2 mm. NOAA rounds to the millimetre and the minute.

A prediction is astronomical only. It leaves out the weather, river flow and
other non-tidal effects on the water level, and isn't for navigation.
