# EncDotNet.S100.Samples.RouteCheck

This console sample checks a passage against a set of datasets with
`RouteChecker` from the
[`EncDotNet.S100.Navigation`](../../src/EncDotNet.S100.Navigation/README.md)
package, and prints the verdict, what each check did and the findings.

It's the runnable version of
[How a route check works](../../docs/how-a-route-check-works.md#run-a-check).
The results are an aid for exploring data, not for navigation.

## Prerequisites

- The .NET 10 SDK, to build the sample from this repository.

The sample needs no download. With no arguments, it writes a synthetic
exchange set into a temporary folder with the repository's test scenario
generator, and deletes it when it's done. The data covers a fictitious sound
with S-101 charts, S-102 bathymetry, S-104 water levels, S-122 protected
areas and S-124 navigational warnings. None of it is real.

## Run the sample

From the repository root:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.RouteCheck
```

The sample builds a two-leg passage by hand, past charted dangers and along
a channel, at 8 knots with a 7 m draft. It prints:

```text
Loaded 36 datasets from <temporary folder>
Passage: 2 legs, departing 2026-03-01 06:00:00Z

Verdict: MustReview (Enters Firing Range Alpha (fictitious))
Findings: 5 must review, 7 check, 5 info

Checks:
  chart      Ran        4 findings
  areas      Ran        7 findings
  warnings   Ran        6 findings
  clearance  Ran        0 findings

Findings:
  MustReview route_check.enters_prohibited_area       leg 1     100 m  Enters Firing Range Alpha (fictitious)
  MustReview route_check.danger_in_corridor           leg 1     999 m  Wreck in the corridor
  ...
  Info       route_check.area_not_in_force            leg 2    4506 m  Summer Speed Zone (fictitious) not in force during the passage

Clearance: MeetsSettings, smallest UKC 6.33 m at 2026-03-01 06:14:20Z
```

To check your own route, pass a folder of datasets (an exchange set or loose
dataset files) and an RTZ file:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.RouteCheck -- path/to/data path/to/route.rtz
```

The sample uses a safety contour and safety depth of 10 m. Change them in
`Program.cs` to suit the vessel.

## How it works

[`Program.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.RouteCheck/Program.cs)
takes five steps:

1. `FileDatasetCatalog.Build(inputs, transforms)` loads every dataset in the
   folder. `DatasetPipelineFactory.DetectProductSpec(path)` detects each
   file's product, and files it doesn't recognise are skipped.
2. `CatalogRouteCheckSources.FromDatasets(catalog.Datasets, transforms)`
   gathers what the checks read, and `new RouteChecker(sources)` creates the
   checker. Create both once per set of loaded datasets.
3. A `PassagePlan` describes the passage: legs with points, cross-track
   limits, speed, draft, squat and required UKC, and the departure. With an
   RTZ file, `RtzReader.Read(path).ToPassagePlan()` builds it.
4. `checker.Check(plan, settings)` runs the chart, area, warning and
   clearance checks. `RouteCheckSettings` holds the safety contour and
   safety depth, and `At`, the time to judge at when the plan has no
   departure.
5. The `RouteCheckReport` gives the `Verdict`, each check's status in
   `Checks`, the sorted `Findings` and the `Clearance` result.

The sample references the projects so that it builds in this repository. It
also references the test scenario generator, which isn't a published
package, for its synthetic data. In your own project, add the packages
instead:

```bash
dotnet add package EncDotNet.S100.Navigation
dotnet add package EncDotNet.S100.Datasets.Pipelines
dotnet add package EncDotNet.S100.Crs.ProjNet
dotnet add package EncDotNet.S100.Routes.Rtz
```

## Next steps

- [How a route check works](../../docs/how-a-route-check-works.md): which
  dataset feeds each check, the verdict and each check's status.
- [Route checks and under-keel clearance](../../docs/route-checks.md): every
  finding code, what it means and the thresholds behind it.
- [route check](../../docs/cli.md#route-check): the same check from the
  command line, for CI.
