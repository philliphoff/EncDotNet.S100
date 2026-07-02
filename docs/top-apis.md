# Top APIs by package

## Why it matters

Generated API trees are complete but can be hard to navigate on first contact.
This page highlights the highest-value entry points per package.

## Quick win

If you only pick three APIs to start, use:

- `S100Dataset.Open(...)`
- `PngS100DatasetRenderer.RenderAsync(...)`
- `S100FeatureCatalogue.Bundled(...)`

## Deep dive

## EncDotNet.S100

- `S100Dataset` — open and inspect datasets
- `PngS100DatasetRenderer` — one-call image rendering
- `S100FeatureCatalogue` — read decoded feature metadata
- `S100Layer` / `S100CompositeOptions` — multi-layer composition

## EncDotNet.S100.Core

- `CoveragePipeline` / `VectorPipeline`
- `DrawingInstruction` and instruction subclasses
- `MarinerSettings`, `Viewport`, `BoundingBox`
- Validation primitives (`ValidationRuleSet<TModel>`, `ValidationFinding`)

## EncDotNet.S100.Datasets.S101

- `S101DatasetReader`
- `S101LuaPortrayalCatalogue`
- `S101DocumentReader`

## EncDotNet.S100.Datasets.S102 / S104 / S111

- `S102DatasetReader` / `S104DatasetReader` / `S111DatasetReader`
- Coverage sources for grid/time-step access

## EncDotNet.S100.Datasets (GML products)

- Reader classes for S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421
- Typed data models where available (for example S-125, S-128, S-201)

## Renderers

- `SkiaDisplayListRenderer`
- `HeadlessVectorRenderer`
- `HeadlessCompositeRenderer`

## Troubleshooting

> [!TIP]
> If you need the shortest path, prefer `EncDotNet.S100` facade APIs. Drop down to per-spec readers when you need custom catalogue wiring or low-level control.

## Next step

- [Getting started](getting-started.md)
- [Embedding the renderer](embedding-the-renderer.md)
- [API reference index](../api/index.md)
