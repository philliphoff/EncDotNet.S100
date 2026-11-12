# EncDotNet.S100.Tides

`EncDotNet.S100.Tides` predicts the astronomical tide at a station from its
harmonic constituents. It gives the predicted height at a time, a series of
heights at a fixed step, and the high and low waters in a period. It also
predicts tidal currents: the current at a time, and slack waters and
maximum floods and ebbs. Reference it when you have a station's
constituents, such as NOAA's, and need a water level or current where
there's no S-104 or S-111 dataset.

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

## Predict a subordinate station

Most NOAA tide stations are subordinate: they have no constituents of their
own, only time and height differences for high and low water against a
reference station. `SubordinateTidePredictor` predicts one from the
reference station's predictor, on the datum the differences apply on (MLLW
for NOAA):

```csharp
var tacomaNarrows = new SubordinateTidePredictor(
    new TidePredictor(onChartDatum),        // Seattle on MLLW
    new SubordinateOffsets(
        HighWaterTime: TimeSpan.FromMinutes(28),
        LowWaterTime: TimeSpan.FromMinutes(23),
        HighWaterHeight: 1.11,
        LowWaterHeight: 1.02,
        SubordinateHeightAdjustment.Ratio));

var highsAndLows = tacomaNarrows.FindExtremes(now, now.AddDays(2));
```

It follows NOAA's method. Each reference high and low is moved by its time
difference and its height multiplied by the ratio (or, for
`SubordinateHeightAdjustment.Offset`, shifted by the metres given). Between
them, the height is the reference curve with the time and height
differences interpolated linearly from one high or low to the next, so the
curve passes through every high and low but is less certain between them.
A reference turn of under 3 mm (`MinimumReferenceTurnMeters`) is a stand
and gives the subordinate station no high or low. Against NOAA's published
highs and lows for 2026 at Tacoma Narrows Bridge, Port Ludlow and
Marrowstone Point, every one matches, to under a minute and a millimetre.

Both predictors implement `ITidePredictor`. The
[`EncDotNet.S100.Tides.Stations`](../EncDotNet.S100.Tides.Stations/README.md)
package reads stations, offsets and datums from NOAA and creates the right
predictor for a station.

## Predict tidal currents

A current station is predicted the same way, along its major axis: the
direction the current mostly flows in, positive toward the flood. NOAA
publishes each current station's constituents per depth, with amplitudes
along the major and minor axes, the mean current along each, the major
axis's direction and the station's flood and ebb directions:

```csharp
var deceptionPass = new CurrentStation(
    "PUG1701_33",                          // NOAA station and depth bin
    "Deception Pass (Narrows)",
    [
        new CurrentConstituent("M2", 2.6802, 241.2, 0.0062, 31.3),
        // ... the station's other constituents, in m/s and Greenwich degrees
    ],
    majorAxisDirectionDegrees: 101.5,
    majorMeanVelocity: -0.3179,
    minorMeanVelocity: 0.1451,
    floodDirectionDegrees: 108,
    ebbDirectionDegrees: 277,
    isRotary: false);

var currents = new CurrentPredictor(deceptionPass);
TidalCurrent now = currents.CurrentAt(DateTime.UtcNow);   // speed, direction, flood or ebb
var events = currents.FindEvents(DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
```

`FindEvents` returns slack before flood and slack before ebb, where the
velocity along the major axis crosses zero, and between them every turn
of the flood or ebb: a maximum, or where a mixed or diurnal tide makes the
current weaken and strengthen again, two maxima with a minimum between. A
minimum within an hour of a maximum is a pause, not a turn, and is left
out with the weaker maximum (`MinimumTurnSeparation`). `Predict` fills a
span with velocities at a fixed step, as for heights.

A **rectilinear** current sets toward the flood direction while it floods
and the ebb direction while it ebbs. A **rotary** current (`isRotary: true`)
gets its speed and direction from both axes, the minor axis 90° clockwise
from the major. `M2EllipseRatio` says how wide the M2 current ellipse is.

`SubordinateCurrentPredictor` predicts a subordinate current station by
NOAA's method: each of the reference station's slacks and turns moved by
the time difference for its kind, and the flood and ebb speeds multiplied
by their ratios (`SubordinateCurrentOffsets`). Between them the reference
curve is used with the time differences interpolated, as for tides. Both
implement `ICurrentPredictor`.

Against NOAA's own published current predictions for 2026 at Deception
Pass, the Golden Gate, The Race and Mobile Bay Entrance, the 6-minute
velocities agree to about 1 mm/s RMS (worst 2.3 mm/s), and slacks and
maxima to a minute and 3 mm/s. At the two subordinate stations recorded,
Golden Gate Bridge 0.8 mile east of and Montauk Point 1.2 miles east of,
every event matches. Mobile Bay Entrance, a weak diurnal current, differs
in under 5% of events where the current nearly stops without turning: NOAA
lists such a near-stop as a slack and leaves out maxima under about 0.1 kn,
where this predictor lists a minimum and the maxima.

Velocities are metres per second; `TidalCurrent.SpeedKnots` converts. A
current prediction is astronomical only: wind and river flow change real
currents, and isn't for navigation.

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
