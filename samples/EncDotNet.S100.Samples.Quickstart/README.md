# EncDotNet.S100.Samples.Quickstart

A minimal console sample showing the **`EncDotNet.S100`** convenience facade
end-to-end: open a dataset, read its features, and render it to a PNG — without
hand-wiring feature or portrayal catalogues.

It ships with a small **synthetic** S-124 (Navigational Warnings) GML fixture
(`sample-navwarn.gml`, reused from the test suite at
`tests/datasets/S124/navwarn_point.gml`) so it runs with **zero setup and no
downloads**. The fixture is hand-authored test data, not a real navigational
warning.

## Run

```bash
# Uses the bundled synthetic S-124 fixture, writes out.png
dotnet run --project samples/EncDotNet.S100.Samples.Quickstart
```

Point it at your own dataset (ISO 8211 `.000`, HDF5 `.h5`, or GML) instead:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.Quickstart -- path/to/dataset out.png
```

## Expected output

```
Opened sample-navwarn.gml — S-124/0.0.0
Features: 2
  f1: NAVWARN Part
  f2: NAVWARN Part
Wrote 7,242 bytes to out.png
```

## What it demonstrates

1. `S100Dataset.Open(path)` — detects the product specification from the file.
2. `S100FeatureCatalogue.Bundled(spec).EnumerateFeatures(dataset)` — reads
   feature summaries through the bundled feature catalogue.
3. `new PngS100DatasetRenderer().RenderAsync(dataset)` — renders to PNG bytes
   through the bundled portrayal catalogue.

A real consumer references the published package rather than the in-repo
project:

```sh
dotnet add package EncDotNet.S100
```

See [docs/getting-started.md](../../docs/getting-started.md) for the full
quickstart, including the CLI path and where to find sample data.
