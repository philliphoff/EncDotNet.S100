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
files under `S-101/DATASET_FILES/`, `S-102/DATASET_FILES/` and so on, for
every product the scenario writes: S-101, S-102, S-104, S-111, S-122, S-124,
S-125, S-129 and S-421. Open it
like any other exchange set:

```csharp
using EncDotNet.S100;

await using var exchangeSet = await S100ExchangeSet.OpenAsync(scenario.Root);
```

Tests generate the files they need into a temporary folder. The only
generated data committed to the repository is the five Part 15 sets in
`tests/datasets/ExchangeSets/Protected-*`, for the viewer's protected-data
work (see [Protected exchange sets](#protected-exchange-sets)).

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
- **Currents.** The tidal stream follows the tide: the Sound fills from the
  west, so the stream floods east while the water rises and ebbs west while
  it falls, at a speed proportional to the rate of rise or fall, and is slack
  at high and low water. `SyntheticWorld.CurrentAt` gives the current
  anywhere, at any time.
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
| S-111 | `CoverageWriters.WriteS111Grid` | A regular grid of surface currents (data coding format 2), speed in knots and direction towards, one step per record time. |
| S-111 | `CoverageWriters.WriteS111Stations` | Current station series (data coding format 8) at the world's tide stations. |
| S-421 | `S421RouteBuilder` and the public `S421DatasetWriter` | A route through named places, with each leg's draft and required UKC (as the static UKC), 100 m cross-track limits, and a manual schedule with the planned speed, the departure and each waypoint's ETA. |
| S-122 | `S122ProtectedAreaWriter` | Edition 2.0.0: a `DataCoverage`, one `MarineProtectedArea` (name, category, restriction, status, and a `periodicDateRange` for each season it applies) and the `Authority` that manages it. |
| S-124 | `S124WarningWriter` | One navigational warning per dataset: a point, line or area, its validity, publication time, an optional cancellation date, and the warnings it cancels. Edition 2.0.0 by default; `S124Edition = 1` writes Edition 1.0.0. |
| S-125 | `S125AidsWriter` | Edition 1.0.0 buoys (lateral, safe water and cardinal, IALA A) with their number, shape, colours and name, and an all-round light on each lit buoy, linked to it as parent and child. |
| S-129 | `S129PlanWriter` | An Actual Plan for one of the scenario's routes: control points with passing times, distance above the UKC limit and time windows, the plan area, and the non-navigable and almost non-navigable areas, all computed from the model. |
| Exchange set | `ExchangeCatalogueBuilder` | `CATALOG.XML` in the S-100 Edition 5.2 schema, with a bounding box for each dataset. Plain scenarios aren't signed or encrypted; `ProtectedScenarioGenerator` adds the Part 15 certificates, signatures and permits. |

Each S-101 cell samples the seabed on its own grid, with its own depth
contours. A coarser cell can generalise towards the shoal side: each grid
node takes the shallowest depth within a radius, so shoals widen and nothing
gets deeper. Depth areas are the bands between the cell's contours, and their
minimum depth (DRVAL1) is the band's lower contour. Within a cell, land, depth
areas and dredged areas cover the cell with no gaps or overlaps.

Generated data says it's synthetic: the producer code is `ZZ00`, names end in
"(fictitious)", and titles, abstracts and catalogue comments say "SYNTHETIC"
and "not for navigation".

S-421 has no attribute for squat, so a route's squat stays in the scenario's
`PassageValues`; tests that check a passage read it from there.

## Built-in scenarios

### Spit across scales

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

### Approach with tide window

`SyntheticScenarios.ApproachWithTideWindow` is an S-421 route from a sea buoy
over the crest of a shoal (4 m) and into the channel, for a 6 m draft that can
cross the crest only for an hour or so either side of high water:

- An S-102 surface covers the whole route, and S-104 stations give the tide,
  so every sample's depth and water level comes from one known node and one
  known station.
- S-111 grids and stations give the tidal streams over the Sound.

The tests sweep two days of departures and check that the departures that
meet the settings are exactly those for which every sample has water. In
SoundCharts the same files give the departure window, Follow passage and the
findings list.

### Warnings on route

`SyntheticScenarios.WarningsOnRoute` is an S-421 route along the channel and
seven S-124 warnings: areas, lines and points that cross the route or lie
near it, in force at different times, one cancelled by a later warning and one
that cancels itself before the vessel arrives. `RouteWarnings.For` gives the
expected answer for each warning: whether it crosses the route, comes within
100 m of it, or neither; where along the route; and whether it's in force while
the vessel is there. Pass `s124Edition: 1` to write the warnings in Edition
1.0.0.

The same route crosses an S-122 seal sanctuary whose speed restriction
applies from 1 March to 31 May each year, so it's in force for the passage
(`S122ProtectedAreaSpec.InForce` gives the answer for any time), and S-125
buoys mark the channel: lateral buoys on its edges, a fairway buoy at the
west entrance and a north cardinal off the spit.

### Port plan vs own route

`SyntheticScenarios.PortPlanVsOwnRoute` is the approach route with an S-129
plan that the port made for it, and the vessel's own route, which rounds the
shoal instead of crossing it. `S129PlanWriter.ControlPoints` and
`S129PlanWriter.Areas` give the plan's values from the model, so the tests can
check what `EncDotNet.S100.Datasets.S129.Fusion` reads from the S-102 and
S-104 files at each control point, and which control points lie on each
route.

## Producer quirks

Real producers' data departs from the specifications in ways we've had to
handle: swapped axes, missing datums, odd CRS declarations and so on. A
scenario can reproduce these per dataset. `ScenarioDefinition.Quirks` maps a
dataset name (the file name without extension) to a list of `ProducerQuirk`
records:

```csharp
using EncDotNet.S100.Testing.Scenarios.Quirks;

var definition = SyntheticScenarios.SpitAcrossScales() with
{
    Name = "spit-swapped-s102",
    Quirks = new Dictionary<string, IReadOnlyList<ProducerQuirk>>
    {
        ["102ZZ00SPIT"] = [new SwappedAxisOrder(), new DeclaredVerticalDatum(null)],
    },
};
```

| Quirk | What the writer does | Products |
|---|---|---|
| `SwappedAxisOrder` | Exchanges latitude and longitude: grid origin, spacing and node counts, or station positions (#650). | S-102, S-104 |
| `DeclaredVerticalDatum` | Leaves `verticalDatum` out (`null`), or declares another datum (#939). | S-102, S-104 |
| `DeclaredHorizontalCrs` | Declares another `horizontalCRS` while the coordinates stay WGS 84 degrees. | S-102, S-104 |
| `LongitudeFrame0To360` | Writes longitudes in the 0…360° frame, in the file and the catalogue (#773). | S-102, S-104 |
| `DeclaredDisplayScales` | Writes the `DataCoverage` display scales inverted, or leaves them out. | S-101 |
| `NoDataHoles` | Writes the fill value at every grid node inside the given rectangles. | S-102 |
| `RecordGap` | Leaves grid steps out, or writes the fill value at stations, for a span of time. | S-104 |
| `ForecastRunEnds` | Stops the records at a time, so later times are past the run. | S-104 |
| `ShoalBias` | Charts every depth shoaler by a fixed amount, as a cell from an older survey (#947). | S-101 |
| `SpikedArea` | Adds a pattern-filled area whose ring has a zero-width spike, which overlays turn into mixed-dimension geometry (#891). | S-101 |

Signatures without `dataStatus` (#754) are a Part 15 option: see
`DatasetProtection.OmitSignatureDataStatus` below. S-101 can't carry 0…360°
longitudes: at the writer's coordinate factor of 10⁷, 225° overflows a 32-bit
coordinate.

`DatasetQuirks` turns a dataset's quirks into hooks the writers call, such as
`ApplyToHdf5Root`, `Position`, `Grid`, `IsNoData` and `IsInGap`. With no
quirks every hook returns its input, so the output is unchanged. To support
quirks in another writer, take a `DatasetQuirks` and pass what it writes
through the hooks that apply to its product.

`QuirkScenarios` has one scenario for each class of quirk, all variants of
"Spit across scales". `QuirkScenarioTests` pins what the library does with
each one today, including the validation findings it reports. Some of that
behaviour is a gap rather than a decision: for example, a swapped S-102 grid
is reported but not used, so passage depths fall back to S-101. When you
change that behaviour, update the test in the same change.

## Protected exchange sets

`ProtectedScenarioGenerator.Generate` writes a scenario as an S-100 Part 15
protected exchange set, playing the data server. A `ProtectionSpec` lists the
datasets to sign, and for each one, whether to encrypt it and what its permit
says. Datasets not in the list stay plain and unsigned.

```csharp
using EncDotNet.S100.Testing.Scenarios.Protection;

using ProtectedScenario protectedSet = ProtectedScenarioGenerator.Generate(
    ProtectedScenarios.Small(),
    new ProtectionSpec("my-protected-set",
    [
        new DatasetProtection("101ZZ00SPIT3"),
        new DatasetProtection("102ZZ00SPIT") { Permit = PermitKind.WrongEdition },
    ]),
    "path/to/folder");
```

The generator signs each dataset's plain bytes with `Part15Signer`, encrypts
it with `S100Cipher`, issues permits with `DataPermit.Create` and writes them
with `PermitFileWriter`. It also writes `TEST-ONLY/` beside the data: the
Scheme Administrator certificate, the hardware ID, the user permit, the test
manufacturer's ID and key, and the expected outcome for each dataset. Read
the set as [Reading protected exchange sets](protected-exchange-sets.md)
describes.

| Option | Result when read |
|---|---|
| `PermitKind.Valid` | `Allowed` |
| `PermitKind.Omitted` | `PermitNotFound` |
| `PermitKind.WrongEdition` | `EditionMismatch` |
| `PermitKind.WrongIssueDate` | `IssueDateMismatch` |
| `PermitKind.ExpiredBeforeIssue` | `IssuedAfterExpiry` |
| `PermitKind.ExpiringSoon` | `Allowed`, with an expiry 30 days after the issue date |
| `PermitKind.WrongHardwareId` | `Allowed`, then `DatasetDecryptionException` when the dataset is read |
| `DatasetProtection.AsUpdateWithoutBase` | `BaseDatasetMissing` |
| `DatasetProtection.Encrypt = false` | `NotProtected`, but signed |
| `DatasetProtection.OmitSignatureDataStatus` | The signature verifies on plain data and is malformed on protected data (#754) |
| `PermitSigning.UntrustedSchemeAdministrator` | `CertificateUntrusted` |
| `PermitSigning.ExpiredCertificate` | `CertificateExpired` |
| `PermitSigning.Unsigned` | `NotSigned` |
| `PermitSigning.Tampered` | `SignatureInvalid` |
| `ProtectionSpec.PermitOnly` | The permit is written to `TEST-ONLY/separate-permit/` |

`ProtectedScenarios` has the sets #768 asks for. The five checked-in sets
under `tests/datasets/ExchangeSets/Protected-*` come from
`ProtectedScenarios.CheckedIn()`; their
[README](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets/ExchangeSets/README.md)
says what each holds and how to regenerate them.

> [!CAUTION]
> The keys and certificates are TEST ONLY. `SyntheticScheme` derives every
> private key from the seed, so anyone can recompute it. The certificates
> say "TEST ONLY" and "not real" in their names.

Protected output is deterministic too. Keys are derived from the seed, and
`DeterministicEcdsa` signs with RFC 6979 nonces, so certificates, permit
signatures and catalogue signatures repeat byte for byte. Certificates have
fixed dates and serial numbers. Each dataset's cell key, and the prefix block
and IV that `S100Cipher.EncryptDataset` normally draws at random, are derived
from the seed and the dataset's name. Part 15 requires those to be random:
fixing them is only for test data.

## Define a scenario

A scenario is a `ScenarioDefinition` record: a world, the products to write
and the times to write them for, and named places. Each product is a small
specification record, such as `S101CellSpec`, `S102SurfaceSpec`,
`S104StationsSpec`, `S104GridSpec`, `S111GridSpec`, `S421RouteSpec`,
`S122ProtectedAreaSpec`, `S124WarningSpec`, `S125AidsSpec` or
`S129PlanSpec`. The coverages are constructor parameters; the S-111 currents,
routes, protected areas, warnings, aids and plans are properties
(`CurrentGrids`, `CurrentStations`, `Routes`, `ProtectedAreas`, `Warnings`,
`Aids`, `UkcPlans`) that default to none.

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

`DeterminismTests` generates each scenario twice and compares the SHA-256
hash of every file, product by product. `QuirkScenarioTests` and
`ProtectedScenarioTests` do the same for the quirk and protected scenarios.

The model uses floating-point functions such as `Math.Tanh` and `Math.Exp`,
whose last bit may differ between platforms. Coordinates are rounded to
10⁻⁷ degrees and depths to `float`, so this rarely changes a file, but don't
rely on hashes matching across operating systems.
