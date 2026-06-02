# EncDotNet.S100.Renderers.Mapsui

Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection.

## Overview

This library bridges the S-100 portrayal pipeline output to Mapsui map layers, including full CRS projection support (EPSG:3857 Web Mercator). Key types include:

- **`MapsuiCoverageRenderer`** — `ICoverageRenderer<ILayer>` implementation that renders coverage data as a georeferenced raster overlay (S-102 / S-104 / S-111).
- **`MapsuiCoverageArrowRenderer`** — renders current arrows (e.g. from S-111 data) as a georeferenced raster layer.
- **`MapsuiDisplayListRenderer`** — product-agnostic vector renderer that consumes a list of `DrawingInstruction`s plus an `IFeatureGeometryProvider` and produces a `MemoryLayer` of styled point/line/area/text features. Used by every S-100 vector product (S-101, S-124, S-129, S-421); no per-spec subclass is required.
- **`ProjNetCrsTransformFactory`** — `ICrsTransformFactory` implementation using [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) for coordinate transformations between UTM, WGS84, and Web Mercator.

`MapsuiDisplayListRenderer` honours the relevant S-100 Part 9 conventions:

- Pen widths and text/symbol offsets specified in millimetres on the nominal display surface are converted to screen pixels using the standard `1 px = 0.32 mm` ratio (S-100 Part 9 §3.10.4).
- `<foreground>` / `<background>` colours accept either a palette token or a literal `#RRGGBB` / `RRGGBBAA` hex value, with the optional `transparency` attribute applied as alpha attenuation.
- Text alignment, mm offsets, and `textLine` start/end offsets (Relative or Absolute) are honoured per S-100 Part 9 §11.4.
- `LineStyleProvider`, `SymbolProvider`, and `AreaFillProvider` callbacks let the host project plug in a portrayal catalogue without coupling the renderer to a specific dataset library.

### Sharing processed-SVG and pattern-tile work across renders

`MapsuiDisplayListRenderer` resolves SVG symbols and rasterises area-fill pattern tiles lazily on first reference. The processed-SVG output depends on the active `ColorPalette` (fill/stroke colours are recoloured against the palette), and pattern-tile rasterisation is comparatively expensive.

When a single dataset is re-rendered repeatedly — typical when toggling palettes, scrubbing time-steps, or changing mariner settings — assign a single `MapsuiRenderAssetCache` instance to the renderer's `AssetCache` property on every `Render()` call:

```csharp
private readonly MapsuiRenderAssetCache _renderAssetCache = new();

// per Render():
var renderer = new MapsuiDisplayListRenderer
{
    Palette = palette,
    AssetCache = _renderAssetCache,
    SymbolProvider = name => catalogue.GetSymbol(name).SvgContent,
    AreaFillProvider = name => catalogue.GetAreaFill(name),
};
```

The cache segments entries per palette (`Day` / `Dusk` / `Night`) so flipping back and forth keeps every palette warm. When `AssetCache` is unset, the renderer falls back to a per-instance cache, which preserves legacy behaviour for ad-hoc / one-shot callers.

## Dynamic feature sources

`EncDotNet.S100.Renderers.Mapsui.DynamicSources` hosts the Mapsui-bound side of the dynamic-feature-source abstraction defined in `EncDotNet.S100.Core` (see [`docs/design/dynamic-feature-source.md`](../../docs/design/dynamic-feature-source.md)). Renderers turn `DynamicFeature` snapshots into Mapsui `IFeature` + `IStyle` instances that the viewer's `DynamicSourceOverlayHost` attaches to a `MemoryLayer` on the overlay tier of `IMapHost`.

- **`IDynamicFeatureRenderer`** — `CanRender` + `Render` contract. Implementations are stateless functions of one feature; the overlay host owns the layer-level state and UI-thread marshalling.
- **`DefaultDynamicFeatureRenderer`** — geometry-kind-dispatching fallback: coloured disc + optional speed-scaled heading line (six-minute predictor capped at 10 nm) for `Point`, stroked polyline for `Curve`, translucent fill + outline for `Surface`. Also the safety-net renderer when a source's `RendererKey` is `null` or unregistered.
- **`OwnShipRenderer`** — own-ship symbology under key `"ownship"`. Draws a true-scale 5-vertex hull polygon when the on-screen vessel length exceeds `MinVesselPixels` (22 px ≈ 6 mm @ 96 dpi), a coloured disc otherwise, plus a heading vector with filled-triangle arrowhead in both modes and a CCRP cross at the GPS antenna in outline mode. Uses `DynamicFeature.VesselGeometry` (CCRP offsets) to place the hull around the antenna and gates the outline / pictogram via mutually-exclusive `MinVisible` / `MaxVisible` styles so the renderer stays viewport-agnostic. Falls back to pictogram-only when no `VesselGeometry` is supplied (e.g. AIS targets with unknown dimensions). See [`docs/design/own-ship-symbology.md`](../../docs/design/own-ship-symbology.md).
- **`KindMatchingRenderer`** — dispatches by `DynamicFeature.Kind` via exact match or dot-namespaced prefix match (e.g. `"vessel"` matches `"vessel.cargo"`). Longest-key-first ordering keeps prefix matching deterministic.
- **`CompositeDynamicFeatureRenderer`** — first-`CanRender`-wins fallthrough over an ordered list. Conventional ordering: per-kind specialists first, `DefaultDynamicFeatureRenderer` last.
- **`DynamicFeatureRendererServiceCollectionExtensions`** — DI helpers that register renderers under the same string key a source advertises via `DynamicSourceMetadata.RendererKey`:

  ```csharp
  // Register a source and its renderer in one call:
  services.AddDynamicFeatureSource<MyAisFeed, MyVesselRenderer>("vessel");

  // Or just a renderer, for cross-source sharing:
  services.AddDynamicFeatureRenderer<MyVesselRenderer>("vessel");
  ```

  The viewer's overlay host resolves the renderer at registration time via `IServiceProvider.GetKeyedService<IDynamicFeatureRenderer>(source.Metadata.RendererKey)`.

## Performance instrumentation

The renderer ships with optional OpenTelemetry instrumentation that
attributes paint cost down to the style-renderer, layer, and geometry
vertex count. All instruments are sub-millisecond per paint when no
OTel listener is attached, so they are safe to leave in production
builds.

| Instrument | Unit | Tags | Purpose |
|---|---|---|---|
| `s100.map.paint.duration` | ms | — | Compositor-thread paint wall-time per frame |
| `s100.map.paint.interval` | ms | — | Time between paints (idle gaps > 500 ms dropped) |
| `s100.map.paint.style.calls` | count | `style`, `layer`, `points` | Style-renderer `Draw` calls per paint |
| `s100.map.paint.style.duration` | ms | `style`, `layer`, `points` | Cumulative `Draw` duration per paint |
| `s100.layer.get_features.duration` | ms | `layer` | Layer-level filter cost per `GetFeatures` call |
| `s100.layer.get_features.visible` / `total` | count | `layer` | Visible / total feature counts per call |
| `s100.layer.get_features.fps` | gauge | `layer` | Effective `GetFeatures` rate per layer |
| `s100.pattern_fill.draw.duration` | ms | — | `AnchoredPatternFillRenderer` per-call cost |

The `points` tag is bucketed (`1-9`, `10-99`, `100-999`, `1k-10k`,
`10k-100k`, `100k+`) to keep histogram cardinality bounded while
still revealing whether a layer's cost is driven by many cheap draws
or a few expensive ones.

To capture a measurement session, run the viewer with the OTel console
exporter enabled:

```sh
ENC_DOTNET_OTEL_CONSOLE=1 OTEL_METRIC_EXPORT_INTERVAL=2000 \
  dotnet run -c Release --project src/EncDotNet.S100.Viewer
```

Histograms are emitted every 2 s with cumulative counts and per-bucket
distributions. Aggregate by `(layer, points)` to identify which
geometries are dominating paint time — empirically, ~93% of paint cost
on real-world S-101 datasets is spent on geometries with ≥100 vertices,
with per-vertex cost ~1 µs. See
[`docs/design/mapsui-performance.md`](../../docs/design/mapsui-performance.md)
for the full investigation and optimization plan.

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Mapsui
```
