# S-102 relief shading: design pass

Status: decided 2026-11-11, all six as recommended. Answers the six questions
in the [brief](s102-relief-brief.md). Today SoundCharts has a Relief shading switch
in a Bathymetry section of Display controls (#1389), shown while S-102 is
loaded, map-wide and off by default. Questions 2 and 4 are built with this
note; question 3 follows.

The sheets are drawn from the real pipeline: the S-102 processor's band
colours for each palette, shaded with `ReliefShading` and drawn one pixel per
grid cell.

| Sheet | Shows |
|---|---|
| `07-strength-5m.png` | Rotterdam Waalhaven (5 m), off and strengths 0.35 / 0.6 / 0.85, Day |
| `08-strength-1m.png` | BSH 102DE00KG45 (1 m), the same four |
| `09-palettes.png` | Rotterdam in Day, Dusk and Night: off, today's shading, darken only, lightening capped to 30 % |

## Summary

| # | Question | Recommendation |
|---|---|---|
| 1 | Where the switch lives | Keep it in Display controls > Bathymetry. Add no per-dataset switch. |
| 2 | How much control | On/off plus a three-step strength: Subtle, Medium (default), Strong. No exaggeration or light direction controls. |
| 3 | Marking non-standard display | Mark it on the `DEPTHS` status chip and in the S-102 legend. No map stamp. |
| 4 | Dusk and Night | Keep the darkening, scale the lightening down: 50 % at Dusk, 30 % at Night. Don't switch relief off. |
| 5 | S-101 under S-102 | Nothing to do: the S-102 surface is now opaque. |
| 6 | Name | Keep "Relief shading". |

## 1. Where the switch lives

Keep the Bathymetry section in Display controls. It sits with the other
display choices that aren't part of a product's portrayal (the Weather
section), it only shows while S-102 is loaded, and it's saved with display
combinations, which is how a mariner keeps a "harbour approach" setup.

- **Not with the depth settings.** Safety contour and depth shades are
  mariner settings with safety meaning (S-164); relief changes none of the
  values they use. Putting it there would suggest it does.
- **Not per dataset.** Neighbouring cells are joined into one surface
  (#1438); shading one cell and not the next would bring the junction back as
  an edge in the shading. A per-dataset switch would also need new plumbing.
- The `DEPTHS` chip popup (question 3) gets a shortcut to the same switch, so
  it can be turned off from where the display says it is on.

## 2. How much control

Add a three-step **Strength** control under the switch, shown only while
relief is on: **Subtle** (0.35), **Medium** (0.6, today's, the default) and
**Strong** (0.85). A segmented control, saved with
display combinations.

- The sheets show why one setting doesn't suit every cell. On the 1 m BSH
  cell Subtle already shows the sand waves and scour (08). In the Rotterdam
  basins Medium is needed to read the berth toes and dredged pits, and Strong
  starts drawing hard black rims along the quay walls (07). The light vs dark
  of a screen and the mariner's eyes vary too.
- Three steps rather than a slider: each step is a named, repeatable look that
  a display combination can carry and a colleague can match, and none of them
  can be set so strong that the band colours stop reading.
- No vertical exaggeration control. Strength already covers "more" or "less";
  a second knob that does nearly the same thing adds a choice without a
  reason. Exaggeration stays at 3×, and the cell size is already accounted
  for (larger cells flatten the same depth step).
- No light direction control. North-west is the cartographic convention every
  shaded map uses; another direction can make features read inverted (pits as
  mounds).

## 3. Marking non-standard display

Relief is a display aid, not the S-102 portrayal, so the display should say
when it's on, without competing with statutory indications.

- **`DEPTHS` status chip.** While relief is on, the chip reads
  `DEPTHS Charted · Relief` (compact: `Charted · R`). Its popup, "Depths on the
  chart", gets a row: *Relief shading on. A display aid that shades the depth
  surface by its slope. Depth colours and values are unchanged.* with an
  **Off** button. The chip is already where the display says what the depth
  colours mean, so this is where a mariner looks.
- **Legend tab.** The S-102 section gets a last row, *Relief shading (display
  aid)*, with a small shaded swatch, while it's on.
- **No map stamp.** The map stamp is kept for "Display Not Real Time"
  (S-98 §12.7.2), and the time overlays design keeps one stamp at a time. Relief
  doesn't change what is drawn where, or when, so a stamp would overstate it.

## 4. Dusk and Night

Keep darkening as it is and scale lightening by palette: all of it in Day,
50 % at Dusk, 30 % at Night (`09-palettes.png`, the last column is 30 %).

- With today's shading the Night display gets near-white highlights on the
  slopes facing the light. That is glare on a display set up to protect night
  vision, and it draws the eye more than the depth colours do.
- "Darken only" removes the glare but at Night the dark band colours leave the
  shadows almost nothing to darken, so the relief all but disappears.
- Capped lightening keeps the forms readable at Night with highlights no
  brighter than the band colours around them.
- Switching relief off in Night would make the mariner's choice silently not
  apply after a palette change; a capped version keeps the choice visible and
  honest.

Engineering: `ReliefShading` gains a lightening factor (1.0, 0.5, 0.3) picked
by palette; the S-102 processor already re-portrays on a palette switch.

## 5. S-101 under S-102

The brief assumed S-102 drew at 0.8 opacity. It is now opaque (the S-102
surface sub-layer sets no opacity, so 1.0), so the S-101 depth areas and
basemap don't show through the shading. Nothing to do.

## 6. Name

Keep **Relief shading**. It says what it does in plain words: it shades the
relief. "Hillshade" is GIS jargon that mariners may not know, "3D seabed"
promises something the display doesn't do (no perspective, no height), and
"Shaded relief" is the same name in a stiffer order. The tooltip stays as it
is: it already explains the light direction and that the band colours keep
their meaning.

## What a build would add

1. Strength control in the Bathymetry section; `s102.relief.strength` saved
   with display settings and combinations; MCP `set_bathymetry_display`
   gains `strength`.
2. Palette-scaled lightening in `ReliefShading`.
3. `DEPTHS` chip text and popup row with Off; S-102 legend row.

Each is small; 1 and 2 could be one PR, 3 another.
