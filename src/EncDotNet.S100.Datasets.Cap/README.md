# EncDotNet.S100.Datasets.Cap

`EncDotNet.S100.Datasets.Cap` reads marine weather warnings in the OASIS
Common Alerting Protocol (CAP 1.2), the format the US National Weather Service
and many other alerting authorities publish them in, and turns each alert's
areas into outlines a chart can draw. It reads:

- one CAP 1.2 `<alert>` document;
- an Atom feed of alerts, whether each entry holds a whole `<alert>` or, as
  `api.weather.gov/alerts.atom` writes them, the alert's fields as `cap:`
  elements of the entry;
- the NWS GeoJSON form from `api.weather.gov/alerts`, one alert feature or a
  feature collection.

It depends on [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) and
[`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md). The
pipeline processor that draws it, `CapDatasetProcessor`, is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

> **Not for navigation.** CAP warnings aren't S-100 products or official
> navigational warnings. Hosts label them so wherever they appear.

The design is in [Wind and wave data](../../docs/design/weather-sources.md), §6.1.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.Cap
```

## Read a file and see what's in force

```csharp
using EncDotNet.S100.Datasets.Cap;

var document = CapReader.Read("alerts.atom");    // CAP XML, Atom or NWS GeoJSON
var warnings = CapWarningSet.Build(document);     // zone codes resolved

foreach (var area in warnings.DrawnAt(DateTimeOffset.UtcNow))
{
    Console.WriteLine($"{area.Info.Event} ({area.Info.Severity}): {area.Area.Description}");
    Console.WriteLine($"  {area.ShapeSource}, {area.Surfaces.Count} surface(s)");
    if (area.UnmatchedZoneCodes.Count > 0)
        Console.WriteLine($"  zones not found: {string.Join(", ", area.UnmatchedZoneCodes)}");
}
```

Each `CapWarningArea` is one CAP area of one alert:

- **Outline.** The area's `<polygon>`s; its `<circle>`s as 64-vertex polygons
  (`CapGeometry.CircleToPolygon`); or, for an area with only zone codes (the
  `UGC` geocodes, for example `ANZ535`), the NWS marine zones it names, from
  `MarineZoneLayer.Default`. A code the layer doesn't have is kept in
  `UnmatchedZoneCodes`, not dropped; an area with no outline at all isn't
  drawn but is still listed.
- **Time.** In force from `onset` (else `effective`, else `sent`) to the later
  of `expires` and the hazard's end the NWS gives as `ends` (GeoJSON) or the
  `eventEndingTime` parameter (CAP XML). The NWS sets `expires` to when the
  message should be replaced, which can come before the hazard starts.
  The status uses `WarningValidity`, the rule S-124 navigational warnings use,
  so both start and end at the same instant. An `Update` or `Cancel` in the
  same file ends the alerts it references from when it was sent; a `Cancel`
  isn't drawn itself.

## The marine zone layer

`MarineZoneLayer.Default` is the NWS coastal and offshore marine zone
shapefiles (`mz16ap26`, `oz16ap26`; 699 zones), simplified once to about
500 m and bundled as GeoJSON (1.6 MB). It's a US Government work in the
public domain. `tools/cap` rebuilds it. A host with a newer release can load
its own with `MarineZoneLayer.Load(stream)` and pass it to
`CapWarningSet.Build`.

`CodesIn(south, west, north, east)` lists the zones whose outline reaches into
an area. The Library uses it to ask `api.weather.gov` for the alerts of the
map view's zones.

## Drawing

`CapStyle` builds the drawing instructions with the code-defined portrayal
toolkit: each area hatched and outlined by severity, and labelled with its
event:

| Severity | Outline | Hatching |
|---|---|---|
| Extreme | violet, 0.9 mm | crossed, every 2 mm |
| Severe | red, 0.7 mm | every 2.5 mm |
| Moderate | orange, 0.5 mm | every 4 mm |
| Minor | ochre, 0.4 mm | every 6 mm |
| Unknown | grey, 0.4 mm dashed | every 6 mm |

Labels are ordinary text instructions, so they follow the text scale and the
renderer's label declutter, and aren't drawn at scales smaller than
1:3,000,000. Day, Dusk and Night palettes are in `CapStyle.Palette`.

## Recognising CAP

`CapReader.LooksLikeCap(leadingBytes)` says whether a file's first kilobyte
is CAP: XML whose root is a CAP `alert` or an Atom `feed` declaring the CAP
namespace, or JSON from `api.weather.gov` naming an alert. The `CAP` product
registration uses it to recognise CAP files whatever their extension, without
claiming S-100 GML (such as S-124) or other GeoJSON.
