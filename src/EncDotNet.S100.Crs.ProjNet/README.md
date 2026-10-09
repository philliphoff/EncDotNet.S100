# EncDotNet.S100.Crs.ProjNet

`EncDotNet.S100.Crs.ProjNet` implements the `ICrsTransformFactory` and
`ICrsTransform` interfaces from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) (namespace
`EncDotNet.S100.Pipelines`) with
[ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI). It reprojects
coverage products, such as S-102, S-104 and S-111 grids in UTM, for display.
It depends only on ProjNet, not on a map renderer, so headless consumers such
as the [`EncDotNet.S100`](../EncDotNet.S100/README.md) facade and the `s100`
command-line tool use it without Mapsui. Reference it directly when you need
coordinate transforms outside the facade.

## Install

```bash
dotnet add package EncDotNet.S100.Crs.ProjNet
```

## Example: UTM to Web Mercator

```csharp
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Pipelines;

ICrsTransformFactory factory = new ProjNetCrsTransformFactory();
ICrsTransform toWebMercator = factory.Create("EPSG:32608", "EPSG:3857");
var (x, y) = toWebMercator.Transform(easting, northing);
```

## Supported coordinate reference systems

`ProjNetCrsTransformFactory.Create` transforms between any two of these:

| CRS | Codes | Use |
|---|---|---|
| WGS 84 / UTM | EPSG:32601–32660 (north), EPSG:32701–32760 (south) | The usual CRS of S-102, S-104 and S-111 coverage grids. |
| WGS 84 | EPSG:4326 | Geographic latitude and longitude. |
| WGS 84 / Pseudo-Mercator | EPSG:3857 | Web Mercator, used to draw coverage rasters on a tiled map. |

- Pass codes as `"EPSG:4326"` or `"4326"`. Any other code throws
  `NotSupportedException`.
- Transforms to or from EPSG:3857 go through EPSG:4326 and use the spherical
  Web Mercator formulas, as map renderers do. ProjNet's ellipsoidal Mercator
  would place northings about 20 km off at 50°N.
- When the source and target strings are the same, ignoring case, `Create`
  returns `IdentityCrsTransform`.
