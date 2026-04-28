# EncDotNet.S100.Renderers.Mapsui

Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection.

## Overview

This library bridges the S-100 portrayal pipeline output to Mapsui map layers, including full CRS projection support (EPSG:3857 Web Mercator). Key types include:

- **`MapsuiCoverageRenderer`** — `ICoverageRenderer<ILayer>` implementation that renders coverage data as a georeferenced raster overlay.
- **`MapsuiCoverageArrowRenderer`** — renders current arrows (e.g., from S-111 data) as a georeferenced raster layer.
- **`MapsuiS101VectorRenderer`** — renders S-101 drawing instructions as Mapsui vector geometries.
- **`MapsuiS421VectorRenderer`** — renders an S-421 Route Plan dataset and its XSLT-produced Part 9 display list as Mapsui vector geometries (route lines, waypoint symbols, leg labels, action points).
- **`ProjNetCrsTransformFactory`** — `ICrsTransformFactory` implementation using [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) for coordinate transformations between UTM, WGS84, and Web Mercator.

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Mapsui
```
