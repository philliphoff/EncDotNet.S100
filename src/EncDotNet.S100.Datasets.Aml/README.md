# EncDotNet.S100.Datasets.Aml

`EncDotNet.S100.Datasets.Aml` draws NATO Additional Military Layers (AML)
cells with SoundCharts' own portrayal. It reads a cell with
[`EncDotNet.S57`](https://www.nuget.org/packages/EncDotNet.S57) (`AmlDataSet`)
and turns it into renderer-neutral drawing instructions on the *Additional
military layers* display plane.

It depends on [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md),
[`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md) and
`EncDotNet.S57`.

> **Not the official AML portrayal, and not for navigation.** The official
> portrayal is an S-52 `.dai` file restricted to S-52 Presentation Library
> holders, and nothing here comes from it. The symbols are SoundCharts' own or
> follow MIL-STD-2525C shapes. AML is never used for depth answers or route
> checks.

The design is in the AML design handoff (`docs/design/aml/HANDOFF.md`,
decisions 15–19, 23 and 25) and epic #1369.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.Aml
```

## Draw a cell

```csharp
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S57.Aml;

var cell = AmlDataSet.FromFile("UKR0UT00.000");
AmlDrawing drawing = new AmlPortrayal().Draw(cell, "UKR0UT00.000");

Console.WriteLine(drawing.Info);
// AML RAL UKR0UT00.000: 37 of 68 features drawn (not drawn: AIRARE, …). Not for navigation.
```

`Draw` reads the cell once. The instructions name colour tokens, so the same
drawing serves Day, Dusk and Night. To hand it to a renderer, make a
`VectorPortrayalResult` in the palette you want:

```csharp
var result = drawing.ToVectorPortrayalResult(
    spec, "UKR0UT00.000", new RenderContext { Palette = PaletteType.Night });
```

`drawing.Features` holds every feature of the cell except meta and collection
objects, drawn or not, so a host can pick classes that have no style.

## What it draws

Every AML feature shares one overlay signature, so AML never reads as chart
data:

| Primitive | Drawn as |
|---|---|
| Line | Dash-dot (14·3·2·3 px) on a 5 px halo in the palette's background. |
| Area | No fill. The dash-dot boundary with ticks on the inside, and the name inside. Airspace adds its vertical limits under the name, upper over lower (`FL245` / `SFC`). |
| Point | The class's glyph in a rounded-square frame with a notch at the top right, and the name to the right. |

Labels are italic, in the family's colour, with a halo. Each feature's
`SCAMIN` limits the scales it draws at.

The colours come in four families, one sub-layer each:

| Family | Layer key | Day | Dusk | Night |
|---|---|---|---|---|
| Air | `aml.air` | `#2350B5` | `#3E66C2` | `#33508F` |
| Operational | `aml.operational` | `#B2381B` | `#B44A2C` | `#8A3B27` |
| Environmental | `aml.environmental` | `#2B7547` | `#3A7E52` | `#2E5C40` |
| Seabed | `aml.seabed` | `#7A4B1F` | `#8A5A2E` | `#6B4A2B` |
| Chart background | `aml.background` | `#4F5B66` | `#66717C` | `#48525C` |
| Bathymetry (CLB) | `aml.bathymetry` | `#7A4B1F` at 55 % | `#8A5A2E` at 55 % | `#6B4A2B` at 55 % |

All sub-layers are on `S98DisplayPlane.AdditionalMilitaryLayers` (45): above
the chart and the weather overlays, below cautions, routes and alerts. Air
draws on top, then operational, environmental, seabed, chart background and
bathymetry.

### Classes

The built-in styles cover the Routes, Areas and Limits (RAL) air and
operational classes, Contour Line Bathymetry (CLB), Maritime Foundation &
Facilities (MFF), and the AML 3.0 chart and land background (NCD, LND) and
airfields. The AML 3.0 trials use the same classes.

| Family | Class | Drawn as |
|---|---|---|
| Air | `atsctl` ATS route centreline | Line, name at its middle |
| Air | `awaypt` air waypoint | Framed open triangle |
| Air | `navaid` NAVAID | Framed hexagon with a centre dot |
| Air | `airres` airspace restriction | Area with limits |
| Air | `ctlasp` controlled airspace | Area with limits; line features as lines |
| Air | `mexasp` military exercise airspace | Area with limits; line features as lines |
| Air | `AIRARE`, `RUNWAY` airfield, runway | Framed airfield or runway glyph; areas ticked |
| Operational | `MIPARE` military practice area | Area |
| Operational | `qroute` Q-route leg | Heavier dash-dot with chevrons along the leg |
| Operational | `turnpt` turning point | Framed ringed dot |
| Operational | `patare` patrol area | Area |
| Operational | `intwtr` internal waters | Area |
| Operational | MFF platforms, production and fishing areas, traffic separation, routes, anchorages | Areas and lines; framed platform or fish glyph at a point |
| Environmental | MFF tide and tidal-stream stations, magnetics, weed, ice, currents | Framed tide, needle or weed glyph; areas and lines |
| Seabed | MFF cables and pipelines (`CBLSUB`, `PIPSOL`, `CBLARE`, `PIPARE`) | Lines and areas |
| Chart background | Lights, buoys, beacons, light vessels | A chart-like glyph (flare, buoy, beacon) in the AML frame, no name |
| Chart background | Landmarks, stations, harbour facilities, buildings | A chart-like glyph with the name |
| Chart background | Coastline, roads, rivers, the AML 3.0 LND lines | Line |
| Chart background | `LNDARE`, `SEAARE`, `txtbua` | The name only; an islet is a dot |
| Chart background | Built-up, administration and harbour areas, LND sites | Area; points as a framed square |
| Bathymetry | `DEPCNT` depth contour | Solid 0.7 px seabed brown at 55 %, its depth in small italic brown |
| Bathymetry | `DEPARE` depth area | Nothing: no fill, its edges are the contours |
| Bathymetry | `SOUNDG` sounding | Each sounding a small italic brown figure |

Other classes aren't drawn yet, but are still features.

**CLB (decision 22).** CLB is drawn in its own style, never the chart's:
with S-101 portrayal its depth areas would look like the chart and bring a
second safety contour. While CLB is drawn over an ENC, a host keeps the
ENC's safety contour dominant by setting `RenderContext.SafetyContourHalo`
(the S-101 and S-57 processors then add a halo under it) and shows the
corner chip `ClbStyles.NotForDepthChecksChip`, "AML · CLB not used for depth
checks". CLB depths never feed depth answers or route checks.

**Standard classes.** A class that is also an IHO S-57 chart class, such as
`MIPARE`, is marked `IsChartClass`. AML draws only its own signature, the
ticked boundary and the label, never the chart symbol. The chart symbol comes
from the ENC under it when the ENC has the feature. AML cells are never drawn
with S-101 portrayal.

## Add a family or a class

Styles are data, grouped in modules, one per file
(`Families/RalAirStyles.cs` is an example). To add classes, write a module and
build a catalogue with it:

```csharp
public static class SeabedStyles
{
    public static AmlGlyph Contact { get; } = new(
        "CONTACT", "An open circle: a seabed contact.",
        """<circle class="s{ink} f0" stroke-width="0.5" cx="0" cy="0" r="1.8"/>""");

    public static IAmlStyleModule Module { get; } = new AmlStyleModule(
        "SBO",
        [new AmlClassStyle("contct", AmlFamily.Seabed, AmlClassKind.Point, "Contact") { Glyph = Contact }]);
}

var catalog = new AmlStyleCatalog([.. AmlStyleCatalog.BuiltInModules, SeabedStyles.Module]);
var drawing = new AmlPortrayal(catalog).Draw(cell);
```

The catalogue makes the framed symbols, the line styles, the palette and the
legend from the styles, so no other code changes. A built-in module is also
added to `AmlStyleCatalog.BuiltInModules`. A new colour family is a new
`AmlFamily` value that its styles use.

A class that needs more than the shared drawing, such as a pattern fill, sets
`AmlClassStyle.Build`. It gets an `AmlDrawingContext`, whose builders keep the
family's viewing group and the feature's `SCAMIN`:

```csharp
new AmlClassStyle("seddep", AmlFamily.Environmental, AmlClassKind.Area, "Sediment")
{
    Build = (feature, context) => [context.Boundary(), context.At(5).Area(feature.Id, "AMLENV", 0.93)],
};
```

## Legend

The legend is built from the styles, so it always matches the drawing:

```csharp
foreach (var group in AmlStyleCatalog.Default.BuildLegend(PaletteType.Day))
{
    Console.WriteLine(group.Family.Name);
    foreach (var entry in group.Entries)
        Console.WriteLine($"  {entry.Label}");
}
```

Points are `SymbolLegendEntry` values, lines and routes `LineLegendEntry`
values, and areas a clear `SwatchLegendEntry` with the ticked boundary as its
outline. Each sub-layer also carries its family's legend.
`AmlStyleCatalog.NoChecksNote` is the caption to show with it: "AML depths
aren't used for depth answers or route checks."
