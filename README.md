# EncDotNet.S100

[![CI](https://github.com/philliphoff/EncDotNet.S100/actions/workflows/ci.yml/badge.svg)](https://github.com/philliphoff/EncDotNet.S100/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/philliphoff/EncDotNet.S100)](LICENSE)
[![Release](https://img.shields.io/github/v/tag/philliphoff/EncDotNet.S100?label=release&sort=semver)](https://github.com/philliphoff/EncDotNet.S100/releases)
[![.NET](https://img.shields.io/badge/.NET-8-512bd4)](https://dotnet.microsoft.com/)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/EncDotNet.S100.Core)](https://www.nuget.org/packages?q=EncDotNet.S100)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://philliphoff.github.io/EncDotNet.S100/)

A set of .NET libraries for reading, portraying, and rendering [S-100](https://iho.int/en/s-100-edition-5-2-0) based nautical chart data, including S-101 Electronic Navigational Charts (ENCs), S-102 Bathymetric Surfaces, S-104 Water Level Information, S-111 Surface Currents, S-122 Marine Protected Areas, S-124 Navigational Warnings, S-127 Marine Resources and Services, S-129 Under Keel Clearance Management, S-411 Sea Ice, and S-421 Route Plans.

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
| **EncDotNet.S100.Datasets.S104** | Reader and coverage portrayal pipeline for S-104 Water Level Information for Surface Navigation datasets. |
| **EncDotNet.S100.Datasets.S111** | Reader and coverage portrayal pipeline for S-111 Surface Current datasets. |
| **EncDotNet.S100.Datasets.S122** | Reader and XSLT portrayal pipeline for S-122 Marine Protected Areas datasets. |
| **EncDotNet.S100.Datasets.S124** | Reader and XSLT portrayal pipeline for S-124 Navigational Warnings datasets. |
| **EncDotNet.S100.Datasets.S125** | Reader and XSLT portrayal pipeline for S-125 Marine Aids to Navigation datasets. |
| **EncDotNet.S100.Datasets.S127** | Reader and XSLT portrayal pipeline for S-127 Marine Resources and Services datasets. |
| **EncDotNet.S100.Datasets.S129** | Reader and XSLT portrayal pipeline for S-129 Under Keel Clearance Management datasets. |
| **EncDotNet.S100.Datasets.S411** | Reader and XSLT portrayal pipeline for S-411 Sea Ice datasets. |
| **EncDotNet.S100.Datasets.S421** | Reader and XSLT portrayal pipeline for S-421 Route Plan datasets. |
| **EncDotNet.S100.Scripting.MoonSharp** | Lua scripting engine implementation using [MoonSharp](https://github.com/moonsharp-devs/moonsharp). |
| **EncDotNet.S100.Renderers.Skia** | Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps. |
| **EncDotNet.S100.Renderers.Mapsui** | Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection. |

## Applications

| Application | Description |
|---|---|
| **EncDotNet.S100.Viewer** | Cross-platform desktop nautical chart viewer built on [Avalonia](https://avaloniaui.net/) and Mapsui. Loads S-101, S-102, S-104, S-111, S-122, S-124, S-125, S-127, S-129, S-411, and S-421 datasets and renders them on an interactive map. |

### Pick / Object Information

Picking is performed in an ECDIS-style **Pick Mode**: toggle the cross-hair
button on the map toolbar (or **View → Appearance → Pick Mode**, or press
**`I`**) and then click any vector feature on the map to open the **Object
Information** panel on the right side of the window — a "pick report"
showing the feature's class, identifier, source dataset, and full attribute
list. Press **`Esc`** to leave Pick Mode. While Pick Mode is active the
double-tap-to-zoom gesture is suppressed so that successive taps each
register as a pick.

On macOS, **Cmd-click** (and **Ctrl-click** on Windows / Linux) acts as a
one-shot pick regardless of whether Pick Mode is active. A
**press-and-hold** of about half a second on any map location is also
treated as a one-shot pick — useful for an occasional identify without
leaving navigation mode.

Click empty water (or the close button on the panel) to dismiss the
report. Use **View → Appearance → Pick Report** to disable the panel
entirely; the status bar will continue to show a one-line summary of each
pick.

The pick report supports vector products (S-101, S-122, S-124, S-125,
S-127, S-129, S-411, S-421). Coverage products (S-102, S-104, S-111)
report layer-level information in the status bar but do not currently
produce per-cell pick reports.

## Screenshots

The cross-platform viewer rendering various S-100 dataset types:

### S-101 — Electronic Navigational Charts

![S-101 ENC](readme/S101Screenshot.png)

Vector chart data with IHO symbology including depth contours, navigation aids, land areas, and other chart features.

### S-102 — Bathymetric Surfaces

![S-102 Bathymetry](readme/S102Screenshot.png)

Color-shaded bathymetric depth grids providing high-resolution seafloor elevation data.

### S-104 — Water Level Information

![S-104 Water Level](readme/S104Screenshot.png)

Gridded water level data for surface navigation, showing tidal and non-tidal water level variations.

### S-111 — Surface Currents

![S-111 Surface Currents](readme/S111Screenshot.png)

Gridded surface current data showing current speed and direction.

### S-122 — Marine Protected Areas

![S-122 Marine Protected Areas](readme/S122Screenshot.png)

GML-encoded marine protected areas, restricted areas, and vessel traffic service areas rendered via XSLT portrayal. Shown above: UK MPAs around the Solent and Isle of Wight from the UKHO trial dataset.

### S-124 — Navigational Warnings

![S-124 Navigational Warnings](readme/S124Screenshot.png)

GML-encoded navigational warnings rendered via XSLT portrayal, highlighting hazards and notices to mariners.

### S-125 — Marine Aids to Navigation

![S-125 Marine Aids to Navigation](readme/S125Screenshot.png)

GML-encoded marine aids to navigation (lights, buoys, beacons, daymarks, AIS aids) rendered via XSLT portrayal, including AtoN status indication symbology.

### S-129 — Under Keel Clearance Management

![S-129 Under Keel Clearance](readme/S129Screenshot.png)

Under keel clearance data for safe navigation in shallow or restricted waterways.

### S-411 — Sea Ice

GML-encoded sea-ice and lake-ice information (concentration, stage of development, ice edges, icebergs) rendered via XSLT portrayal.

![S-411 Sea Ice](readme/S411Screenshot.png)

### S-421 — Route Plans

![S-421 Route Plan](readme/S421Screenshot.png)

GML-encoded route plans rendered via XSLT portrayal, showing waypoints, route legs, and action points along a planned voyage.

## Building

```sh
dotnet build
```

## License

This project is licensed under the [MIT License](LICENSE).
