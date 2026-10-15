# Test scenario generator

The scenario generator writes S-100 exchange sets in which several products
cover the same fictitious piece of sea, sampled from one analytic model of
its seabed and tide. Tests load the files through the real readers and compare
what the library reports with values computed from the model.

The generator lives in
[`tests/EncDotNet.S100.Testing.Scenarios`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Testing.Scenarios)
and isn't a published package. Its own tests, in
[`tests/EncDotNet.S100.Testing.Scenarios.Tests`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Testing.Scenarios.Tests),
show each use on this page.

## Generate a scenario

Reference the generator project from a test project, then write a scenario
into a folder:

```csharp
using EncDotNet.S100.Testing.Scenarios;

ScenarioDefinition definition = SyntheticScenarios.SpitAcrossScales(seed: 941);
GeneratedScenario scenario = ScenarioGenerator.Generate(definition, "path/to/folder");
```

The folder is an exchange set: `CATALOG.XML` at the root, and each product's
files under `S-101/DATASET_FILES/`, `S-102/DATASET_FILES/` and so on. Open it
like any other exchange set:

```csharp
using EncDotNet.S100;

await using var exchangeSet = await S100ExchangeSet.OpenAsync(scenario.Root);
```

Nothing is committed to the repository: tests generate the files they need
into a temporary folder.

## Look at a scenario

To open the generated data in SoundCharts or the `s100` tool, set
`S100_SCENARIO_OUT` to a folder and run the export test. It writes each
built-in scenario to a subfolder named after it.

```bash
S100_SCENARIO_OUT=path/to/folder dotnet test tests/EncDotNet.S100.Testing.Scenarios.Tests --filter "FullyQualifiedName~ExportScenarioTests"
```

On Windows:

```powershell
$env:S100_SCENARIO_OUT = "path\to\folder"
dotnet test tests/EncDotNet.S100.Testing.Scenarios.Tests --filter "FullyQualifiedName~ExportScenarioTests"
```

Then render the exchange set, for example:

```bash
s100 render path/to/folder/spit-across-scales -o spit.png
```

## The world model

A scenario samples a `SyntheticWorld`. The world is fictitious and placed in
the open South Pacific, at 41° S 135° W, so it can't be mistaken for a real
chart or for the IHO test area at 32° S 63° E.

- **Frame.** Positions are metres east (`x`) and north (`y`) of the origin,
  mapped to degrees by a fixed scale. The mapping is the world's definition,
  so expected values are exact.
- **Seabed.** Depth below chart datum is a closed-form function of `x` and
  `y`: a shore profile with land to the south, plus channels, spits and
  mounds (banks when they dry, shoals when they don't), plus seeded
  undulations of a few centimetres. `SyntheticWorld.DepthAt` gives the depth
  anywhere.
- **Coastline.** The coastline is the contour where the seabed meets high
  water (`SyntheticWorld.HighWaterHeight`). Land is shallower than that;
  drying areas lie between it and chart datum.
- **Tide.** Water level is a mean level plus harmonic constituents (M2 and S2
  in "Synthetic Sound"), with amplitudes and phase lags that vary smoothly from
  west to east. Stations and grid nodes are samples of the same field.
  `TideModel.HeightAt` gives the height anywhere, at any time.
- **Datums.** `WorldDatums` holds the chart datum and the water-level datum as
  S-100 vertical datum codes. Both are Lowest Astronomical Tide in "Synthetic
  Sound".

## What the writers produce

| Product | Writer | Contents |
|---|---|---|
| S-101 | `S101CellWriter` | `DataCoverage` (with display scales), `SoundingDatum`, `LandArea`, `DepthArea`, `DredgedArea`, `Coastline`, `DepthContour` and `Sounding`. |
| S-102 | `CoverageWriters.WriteS102` | One depth and uncertainty grid. The depth at each node is the model's depth there. |
| S-104 | `CoverageWriters.WriteS104Stations` | Station series (data coding format 8) at the world's tide stations. |
| S-104 | `CoverageWriters.WriteS104Grid` | A regular grid (data coding format 2) with one step per record time. |
| Exchange set | `ExchangeCatalogueBuilder` | `CATALOG.XML` in the S-100 Edition 5.2 schema, with a bounding box for each dataset. Datasets aren't signed or encrypted. |

Each S-101 cell samples the seabed on its own grid, with its own depth
contours. A coarser cell can generalise towards the shoal side: each grid
node takes the shallowest depth within a radius, so shoals widen and nothing
gets deeper. Depth areas are the bands between the cell's contours, and their
minimum depth (DRVAL1) is the band's lower contour. Within a cell, land, depth
areas and dredged areas cover the cell with no gaps or overlaps.

Generated data says it's synthetic: the producer code is `ZZ00`, names end in
"(fictitious)", and titles, abstracts and catalogue comments say "SYNTHETIC"
and "not for navigation".

## Built-in scenarios

`SyntheticScenarios.SpitAcrossScales` is a dredged channel running east–west
past a drying spit:

- A harbour-band cell and a coastal-band cell cover the spit. Off the spit's
  tip they disagree: the harbour cell charts a 2–5 m depth area where the
  coastal cell's generalised spit dries.
- An S-102 surface covers the channel but not the spit, so a route across the
  spit falls back to S-101 depths.
- S-104 stations stand west and east of the spit, and an S-104 grid covers the
  east of the harbour.
- `declareS102VerticalDatum: false` leaves the vertical datum out of the
  S-102 file, as some producers do.

The scenario also names places (`Places`) and a route (`Route`) for tests to
use. The end-to-end tests check the passage under-keel clearance at those
places against the model.

## Define a scenario

A scenario is a `ScenarioDefinition` record: a world, the products to write
and the times to write them for, and named places. Each product is a small
specification record, such as `S101CellSpec`, `S102SurfaceSpec`,
`S104StationsSpec` or `S104GridSpec`.

To vary an existing scenario, change the parts you need with `with`. This
variant drops the S-102 surface, so every depth comes from S-101:

```csharp
var definition = SyntheticScenarios.SpitAcrossScales() with
{
    Name = "spit-without-s102",
    Surfaces = [],
};
```

To add a scenario, add a method to `SyntheticScenarios` that returns a
definition, and give it a distinct `Name`. Keep the large-scale geography
independent of the seed, so the places your tests name stay where they are.
Then add tests that:

- load the exchange set and validate every dataset with no findings, as
  `ScenarioValidationTests` does;
- compare the values your feature reports with the world model at named
  places and times.

## How determinism works

The same seed and definition give byte-identical files. The generator:

- takes every time it writes (issue time, record times) from the definition,
  never from the clock;
- writes collections, attributes and records in definition order;
- draws random values only from `ScenarioRandom`, a SplitMix64 sequence that
  this code defines, so the same seed gives the same values on every runtime.
  `ScenarioRandom.For(seed, name)` gives an independent sequence for each use,
  so adding a draw in one place doesn't shift the others;
- writes XML with fixed encoding, indentation and line endings, and numbers
  with the invariant culture.

`DeterminismTests` generates the same scenario twice and compares the SHA-256
hash of every file.

The model uses floating-point functions such as `Math.Tanh` and `Math.Exp`,
whose last bit may differ between platforms. Coordinates are rounded to
10⁻⁷ degrees and depths to `float`, so this rarely changes a file, but don't
rely on hashes matching across operating systems.
