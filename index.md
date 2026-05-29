# EncDotNet.S100

A managed, cross-platform implementation of the IHO
[S-100](https://iho.int/en/s-100-edition-5-2-0) Universal Hydrographic
Data Model for .NET, with a cross-platform desktop viewer
(Avalonia + Mapsui).

## Libraries

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
| **EncDotNet.S100.Datasets.S101** | S-101 ENC reader and Lua portrayal pipeline. |
| **EncDotNet.S100.Datasets.S102** | S-102 bathymetric surface reader and coverage pipeline. |
| **EncDotNet.S100.Datasets.S104** | S-104 water level reader and coverage pipeline. |
| **EncDotNet.S100.Datasets.S111** | S-111 surface current reader and coverage pipeline. |
| **EncDotNet.S100.Datasets.S122** | S-122 marine protected areas reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S124** | S-124 navigational warnings reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S125** | S-125 marine aids to navigation reader and XSLT portrayal pipeline (with typed AtoN projection). |
| **EncDotNet.S100.Datasets.S127** | S-127 marine resources and services reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S128** | S-128 catalogue of nautical products reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S129** | S-129 under keel clearance reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S131** | S-131 marine harbour infrastructure reader and Lua portrayal pipeline (GML+Lua hybrid). |
| **EncDotNet.S100.Datasets.S201** | S-201 aids to navigation information (IALA) reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S411** | S-411 sea ice reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S421** | S-421 route plan reader and XSLT portrayal pipeline. |
| **EncDotNet.S100.Datasets.S57** | Legacy S-57 ENC reader that translates to the in-memory S-101 model. |
| **EncDotNet.S100.Datasets.Pipelines** | Per-spec `IDatasetProcessor` implementations, the S-98 interoperability authority, and the validation runner consumed by the viewer and the MCP server. |

### Renderers

| Package | Description |
|---|---|
| **EncDotNet.S100.Renderers.Skia** | Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps. |
| **EncDotNet.S100.Renderers.Mapsui** | Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection, plus dynamic-feature-source renderers (own-ship hull + arrowhead, default disc/line/polygon fallback). |

### MCP server

| Package | Description |
|---|---|
| **EncDotNet.S100.Mcp.Tools** | Transport-agnostic Model Context Protocol tool surface (`list_datasets`, `describe_feature`, `sample_coverage`, `render_to_image`). |
| **EncDotNet.S100.Mcp** | Streamable HTTP host that exposes the `Mcp.Tools` surface; bound to `127.0.0.1` by default, off by default, no authentication. |

## Getting Started

Browse the [API Reference](api/index.md) for detailed type and member
documentation, or see the [Documentation](docs/index.md) section for
conceptual guides, the per-product matrix, and design notes.
