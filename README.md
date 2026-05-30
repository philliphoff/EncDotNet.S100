# EncDotNet.S100

[![CI](https://github.com/philliphoff/EncDotNet.S100/actions/workflows/ci.yml/badge.svg)](https://github.com/philliphoff/EncDotNet.S100/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/philliphoff/EncDotNet.S100)](LICENSE)
[![Release](https://img.shields.io/github/v/tag/philliphoff/EncDotNet.S100?label=release&sort=semver)](https://github.com/philliphoff/EncDotNet.S100/releases)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/EncDotNet.S100.Core)](https://www.nuget.org/packages?q=EncDotNet.S100)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://philliphoff.github.io/EncDotNet.S100/)

## Overview

**EncDotNet.S100** is a managed, cross-platform implementation of the
IHO [S-100](https://iho.int/en/s-100-edition-5-2-0) Universal
Hydrographic Data Model for .NET. It provides:

- A set of **reusable libraries** for reading, portraying, rendering,
  and **validating** S-100 product data — from ISO 8211 ENC cells to
  HDF5 coverage grids to GML feature collections — behind a common
  pipeline abstraction.
- A **cross-platform desktop viewer** (Avalonia + Mapsui) that loads
  any combination of supported products from an exchange set or as
  loose files and renders them, time-aligned, on an interactive map.
- An **optional MCP server** that exposes loaded datasets to AI
  agents (`list_datasets`, `describe_feature`, `sample_coverage`,
  `render_to_image`).

The goal is to make S-100 data approachable for .NET developers and
end users without requiring native dependencies, commercial S-52
assets, or platform-specific tooling. Everything runs on macOS,
Windows, and Linux out of the box.

## Supported standards

Every supported product ships a reader, a portrayal pipeline, and a
normative validation rule pack.

| Standard | Subject | Encoding | Portrayal | Validation pack |
|---|---|---|---|---|
| **S-101** | Electronic Navigational Charts | ISO 8211 | Lua (Part 9A) | ✅ |
| **S-102** | Bathymetric Surfaces | HDF5 | Coverage (Lua) | ✅ |
| **S-104** | Water Level Information | HDF5 | Coverage | ✅ |
| **S-111** | Surface Currents | HDF5 | Arrow symbology | ✅ |
| **S-122** | Marine Protected Areas | GML | XSLT | ✅ |
| **S-124** | Navigational Warnings | GML | XSLT | ✅ |
| **S-125** | Marine Aids to Navigation | GML | XSLT | ✅ |
| **S-127** | Marine Resources & Services | GML | XSLT | ✅ |
| **S-128** | Catalogue of Nautical Products | GML | XSLT | ✅ |
| **S-129** | Under Keel Clearance Management | GML | XSLT | ✅ |
| **S-131** | Marine Harbour Infrastructure | GML | Lua (Part 9A) | ✅ |
| **S-201** | Aids to Navigation Information (IALA) | GML | XSLT | ✅ |
| **S-411** | Sea Ice Information | GML | XSLT | ✅ |
| **S-421** | Route Plans | GML | XSLT | ✅ |
| **S-57** *(legacy)* | Electronic Navigational Charts (Ed 3.1) | ISO 8211 | via S-101 pipeline | ✅ (delegated) |

S-57 cells are translated to the in-memory S-101 model and rendered
through the S-101 portrayal pipeline; validation runs as two passes
with S-101 findings rebadged `S101-as-S57/*` so the user can tell
which layer of the pipeline a finding came from. This is a
best-effort path for viewing legacy chart collections; it is **not**
an S-52 implementation.

## The Viewer

`EncDotNet.S100.Viewer` is a cross-platform desktop nautical chart
viewer built on [Avalonia](https://avaloniaui.net/) and
[Mapsui](https://mapsui.com/). It loads any combination of the
supported products and renders them time-aligned over an
OpenStreetMap basemap. Headline features:

- **Multi-product paint stack** driven by the S-98 interoperability
  authority (display planes, within-plane priority, inter-product
  rules) rather than load order.
- **Activity-bar panels** for Datasets, Layer Stack, ECDIS Display
  Controls, Object Information (pick reports), Validation, and
  Settings — every panel can be docked, hidden, or rearranged, and
  the layout persists between sessions.
- **Pick / Object Information** for both vector and coverage
  products, with FC-decoded attribute names, follow-the-`xlink`
  reference navigation, and an embedded time-series chart for
  fixed-station S-104 / S-111 observations.
- **ECDIS-style controls** — display category, display planes, text
  groups, per-spec viewing groups, Day / Dusk / Night palettes, and
  mariner settings (safety contour, depth contours, four shades,
  simplified symbols, radar overlay).
- **Global timeline** for time-varying datasets (S-104, S-111,
  S-411).
- **Validation panel** with click-to-zoom, severity colouring, and
  an overlay marker layer.
- **Live overlays** through the dynamic-feature-source abstraction —
  an own-ship glyph with true-scale hull + arrowhead + CCRP cross at
  zoom, plus an **AIS-target overlay** (PR-D3) backed by the
  [aisstream.io](https://aisstream.io) WebSocket service and rendered
  with a per-class palette using the same hull/arrowhead vocabulary.
- **Optional MCP server** (off by default) exposing the loaded
  datasets to AI agents.

See [the viewer README](src/EncDotNet.S100.Viewer/README.md) for the
full feature guide, and the per-product gallery below for what each
product looks like in the viewer.

### Gallery

| Product | Screenshot |
|---|---|
| S-101 ENC | ![S-101 ENC](readme/S101Screenshot.png) |
| S-102 Bathymetry | ![S-102 Bathymetry](readme/S102Screenshot.png) |
| S-104 Water Level | ![S-104 Water Level](readme/S104Screenshot.png) |
| S-111 Surface Currents | ![S-111 Surface Currents](readme/S111Screenshot.png) |
| S-122 Marine Protected Areas | ![S-122 MPAs](readme/S122Screenshot.png) |
| S-124 Navigational Warnings | ![S-124 Navigational Warnings](readme/S124Screenshot.png) |
| S-125 Marine AtoN | ![S-125 Marine Aids to Navigation](readme/S125Screenshot.png) |
| S-127 Marine Services | ![S-127 Marine Resources & Services](readme/S127Screenshot.png) |
| S-128 Catalogue | ![S-128 Catalogue of Nautical Products](readme/S128Screenshot.png) |
| S-129 UKC | ![S-129 Under Keel Clearance](readme/S129Screenshot.png) |
| S-131 Marine Harbour | ![S-131 Marine Harbour Infrastructure](readme/s131-viewer.png) |
| S-411 Sea Ice | ![S-411 Sea Ice](readme/S411Screenshot.png) |
| S-421 Route Plan | ![S-421 Route Plan](readme/S421Screenshot.png) |
| S-57 (via S-101) | ![S-57 rendered via S-101 pipeline](docs/images/s57-viewer-us4fl1lt.png) |

## Libraries

For developers consuming EncDotNet.S100 directly, the solution is
split into focused packages:

### Core framework

| Package | Description |
|---|---|
| **EncDotNet.S100.Core** | Pipeline abstractions (asset sources, HDF5, Lua, coverage + vector pipelines), the validation framework, and the dynamic-feature-source abstraction. |
| **EncDotNet.S100.Features** | Parser for S-100 Feature Catalogue XML files (ISO 19110 / S-100 Part 5). |
| **EncDotNet.S100.ExchangeSets** | Reader for S-100 Exchange Set catalogues and dataset/support file discovery. |
| **EncDotNet.S100.Portrayals** | Parser for S-100 Portrayal Catalogues (symbols, line styles, area fills, colour profiles, viewing groups). |
| **EncDotNet.S100.Specifications** | Bundles official feature and portrayal catalogues as embedded resources. |

### Encoding and scripting backends

| Package | Description |
|---|---|
| **EncDotNet.S100.Hdf5.PureHdf** | HDF5 reader implementation using [PureHDF](https://github.com/Apollo3zehn/PureHDF) (fully managed, no native dependencies). |
| **EncDotNet.S100.Scripting.MoonSharp** | Lua 5.2 scripting engine using [MoonSharp](https://github.com/moonsharp-devs/moonsharp). |

### Product datasets

| Package | Description |
|---|---|
| **EncDotNet.S100.Datasets.S101** | S-101 ENC reader, Lua portrayal pipeline, validation pack (`S101DatasetView` façade + 10 rules). |
| **EncDotNet.S100.Datasets.S102** | S-102 bathymetry reader, coverage pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S104** | S-104 water level reader, coverage pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S111** | S-111 surface currents reader, coverage pipeline (per-feature arrow symbology), validation pack. |
| **EncDotNet.S100.Datasets.S122** | S-122 marine protected areas reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S124** | S-124 navigational warnings reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S125** | S-125 marine aids to navigation reader (with typed AtoN projection and xlink-resolved status), XSLT portrayal, validation pack. |
| **EncDotNet.S100.Datasets.S127** | S-127 marine resources and services reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S128** | S-128 catalogue of nautical products reader (with typed `DataModel` projection), XSLT portrayal, validation pack. |
| **EncDotNet.S100.Datasets.S129** | S-129 under keel clearance reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S131** | S-131 marine harbour infrastructure reader, Lua portrayal pipeline (GML+Lua hybrid), validation pack. |
| **EncDotNet.S100.Datasets.S201** | S-201 aids to navigation information (IALA) reader, XSLT portrayal pipeline, typed AtoN inventory data model, validation pack. |
| **EncDotNet.S100.Datasets.S411** | S-411 sea ice reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S421** | S-421 route plan reader, XSLT portrayal pipeline, validation pack. |
| **EncDotNet.S100.Datasets.S57** | Legacy S-57 ENC reader that translates to the in-memory S-101 model; pre-translation rules + delegation to the S-101 validation pack. |
| **EncDotNet.S100.Datasets.Pipelines** | Per-spec `IDatasetProcessor` implementations, the S-98 interoperability authority, the validation runner, and the `ConcatReports` rebadge helper used by S-57. |

### Renderers

| Package | Description |
|---|---|
| **EncDotNet.S100.Renderers.Skia** | Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps. |
| **EncDotNet.S100.Renderers.Mapsui** | Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection, plus dynamic-feature-source renderers (own-ship hull + arrowhead, AIS-target hull + per-class palette, default disc/line/polygon fallback). |

### Dynamic feature sources

| Package | Description |
|---|---|
| **EncDotNet.S100.DynamicSources.Ais** | Decoder-agnostic AIS dynamic feature source: per-MMSI cache, ITU-R M.1371-aligned aging, projection to `DynamicFeature` with sentinel-collapsed motion. See [its README](src/EncDotNet.S100.DynamicSources.Ais/README.md). |
| **EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo** | Production driver implementing `IAisMessageSource` over [aisstream.io](https://aisstream.io)'s WebSocket service. BCL-only (no third-party AIS or WebSocket deps). See [its README](src/EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo/README.md). |

### MCP server

| Package | Description |
|---|---|
| **EncDotNet.S100.Mcp.Tools** | Transport-agnostic Model Context Protocol tool surface (`list_datasets`, `describe_feature`, `sample_coverage`). See [its README](src/EncDotNet.S100.Mcp.Tools/README.md). |
| **EncDotNet.S100.Mcp** | Streamable HTTP host that exposes the `Mcp.Tools` surface plus the viewer-injected `render_to_image` tool; bound to `127.0.0.1` by default, off by default, no authentication. See [its README](src/EncDotNet.S100.Mcp/README.md) and the [agent walkthrough](docs/mcp-server.md). |

## Building

```sh
dotnet build
```

Running the viewer:

```sh
dotnet run --project src/EncDotNet.S100.Viewer
```

Pre-built per-platform binaries are attached to every
[release](https://github.com/philliphoff/EncDotNet.S100/releases).
The macOS DMG is Developer-ID signed and Apple-notarized; Windows
and Linux ship as architecture-tagged `tar.gz` archives.

## Observability

The libraries are instrumented with `Microsoft.Extensions.Logging`,
`System.Diagnostics.ActivitySource`, and
`System.Diagnostics.Metrics.Meter`, and the viewer ships an
OpenTelemetry OTLP exporter configured by the standard `OTEL_*`
environment variables. The fastest way to see logs, traces, and
metrics is the bundled .NET Aspire host:

```sh
dotnet run --project src/EncDotNet.S100.AppHost
```

See [`docs/observability.md`](docs/observability.md) for the span
tree, metrics catalogue, and alternative recipes (standalone Aspire
dashboard, Jaeger).

## License

This project is licensed under the [MIT License](LICENSE).
