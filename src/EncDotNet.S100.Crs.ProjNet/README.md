# EncDotNet.S100.Crs.ProjNet

ProjNet-backed implementation of the `ICrsTransformFactory` /
`ICrsTransform` abstraction declared in **EncDotNet.S100.Core**
(`EncDotNet.S100.Pipelines` namespace).

## Overview

`ProjNetCrsTransformFactory` creates coordinate transforms using
[ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI). It supports:

- **WGS84 UTM zones** — EPSG:326xx (northern hemisphere) and EPSG:327xx
  (southern hemisphere), the CRS S-102 / S-104 / S-111 coverage products
  are typically georeferenced in.
- **EPSG:4326 ↔ EPSG:3857** — geographic ↔ Web Mercator, the projection
  used to lay coverage rasters onto a slippy-map viewport.

Identical source/target CRS short-circuit to `IdentityCrsTransform`.

## Why a separate package?

This implementation depends **only on ProjNet** — no map renderer. It
previously lived inside `EncDotNet.S100.Renderers.Mapsui`, which made
Mapsui a transitive dependency of otherwise-headless consumers (the
`EncDotNet.S100` facade and the `s100` CLI). Hosting it here lets those
consumers reproject coverage products without linking Mapsui.

## Usage

```csharp
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Pipelines;

ICrsTransformFactory factory = new ProjNetCrsTransformFactory();
ICrsTransform toWebMercator = factory.Create("EPSG:32608", "EPSG:3857");
var (x, y) = toWebMercator.Transform(easting, northing);
```
