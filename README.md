# EncDotNet.S100

A set of .NET libraries for reading, portraying, and rendering [S-100](https://iho.int/en/s-100-edition-5-2-0) based nautical chart data, including S-101 Electronic Navigational Charts (ENCs), S-102 Bathymetric Surfaces, and S-111 Surface Currents.

## Libraries

| Package | Description |
|---|---|
| **EncDotNet.S100.Core** | Core abstractions and pipeline framework for S-100 data (asset sources, HDF5, Lua scripting, coverage and vector pipelines). |
| **EncDotNet.S100.Features** | Parser for S-100 Feature Catalogue XML files (ISO 19110/S-100 Part 5). |
| **EncDotNet.S100.ExchangeSets** | Reader for S-100 Exchange Set catalogues and dataset/support file discovery. |
| **EncDotNet.S100.Portrayals** | Parser for S-100 Portrayal Catalogues (symbols, line styles, area fills, color profiles, viewing groups). |
| **EncDotNet.S100.Hdf5.PureHdf** | HDF5 file reader implementation using [PureHDF](https://github.com/Apollo3zehn/PureHDF) (no native dependencies). |
| **EncDotNet.S100.Datasets.S101** | Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart datasets. |
| **EncDotNet.S100.Datasets.S102** | Reader and coverage portrayal pipeline for S-102 Bathymetric Surface datasets. |
| **EncDotNet.S100.Datasets.S111** | Reader and coverage portrayal pipeline for S-111 Surface Current datasets. |
| **EncDotNet.S100.Scripting.MoonSharp** | Lua scripting engine implementation using [MoonSharp](https://github.com/moonsharp-devs/moonsharp). |
| **EncDotNet.S100.Renderers.Skia** | Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps. |
| **EncDotNet.S100.Renderers.Mapsui** | Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection. |

## Applications

| Application | Description |
|---|---|
| **EncDotNet.S100.Viewer** | Cross-platform desktop nautical chart viewer built on [Avalonia](https://avaloniaui.net/) and Mapsui. Loads S-101, S-102, and S-111 datasets and renders them on an interactive map. |

## Tools

| Tool | Description |
|---|---|
| **RenderS102** | CLI tool that renders an S-102 HDF5 bathymetric file to a PNG image. |
| **TestS101Lua** | CLI tool that runs the S-101 Lua portrayal pipeline and dumps drawing instructions. |

## Building

```sh
dotnet build
```

## License

This project is licensed under the [MIT License](LICENSE).
