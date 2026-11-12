# S-102 relief shading: options for the zoomed-out view

Status: closed, option 1 (leave it). Option 2 was built and dropped; see
"Outcome" at the end. Follows the relief shading brief
([s102-relief-brief.md](s102-relief-brief.md)) and its "known engineering
limits".

## The problem

The viewer shades each S-102 cell once, at full resolution, and draws the
shaded image with mipmaps (#1419, #1421). Close up, that works well. Zoomed
out on a 1 m cell, each screen pixel averages many lit and shadowed cells.
The light and dark sides of each small feature cancel out, so the shading
fades to near flat. Broad shapes such as channel edges, dredged basins and
scarps should still read at that scale, but they barely show.

## What was tried

A throwaway harness read BSH cell 102DE00KG45 (2330 × 3806, 1 m, UTM 32N).
It shaded the cell several ways and drew each result at 1, 4 and 13–16 cells
per screen pixel with the viewer's trilinear sampling. Band colours are the
day palette's four shades with a 10 m safety contour, and the shading is the
current default: 315° light at 45° elevation, 3× vertical exaggeration,
strength 0.6.

| Variant | What it does |
|---|---|
| A, today | Shade the native surface; the mipmaps average it. |
| B, fixed smoothing | Gaussian blur of 3 cells, then shade. |
| C, two scales | Average the native shade and the shade of a 12-cell blur. |
| D, per zoom | At each zoom, blur to about the screen pixel size (σ = cells per pixel ÷ 2), then shade. |
| E, per zoom + boost √ | As D, with exaggeration × √(cells per pixel): ×2 at 4, ×4 at 16. |
| F, per zoom + boost ⁴√ | As D, with exaggeration × ⁴√(cells per pixel): ×1.4 at 4, ×2 at 16. |

Sheets: `05-smoothing-variants.png` (A–E) and `06-zoom-boost.png` (A, D, E, F).

## Findings

1. **Smoothing alone doesn't help (B, C, D).** Zoomed out, D looks almost
   the same as A. The mipmaps already average the surface, and smoothing
   flattens the slopes as much as it removes noise, so contrast doesn't come
   back.
2. **Fixed smoothing hurts close up (B, and C to a lesser degree).** At 1
   cell per pixel, B is visibly blurred. Close-up detail is the reason the
   1 m data is worth shading, so any fixed smoothing is out.
3. **What brings broad shapes back is more exaggeration as you zoom out
   (E, F).** The scarp and basin edges read clearly at 13–16 cells per pixel.
   - E is strong: it looks like vendor demo images, but it also brings back
     the survey-line pattern in the north-west corner and gives the shading
     more weight than the band colours.
   - F is a moderate middle: shapes are readable, noise stays in the
     background, and the band colours still dominate.
4. **Cost is small either way.** On the 1 m cell, shading takes 43 ms. A
   blur plus shade at a reduced level takes 100–230 ms with the naive
   prototype blur, and a 2×2 averaging pyramid would be cheaper. A shade
   pyramid at 1/4 and 1/16 resolution adds about 7 % memory to the shaded
   image.

## Options

1. **Leave it (A).** Relief is a close-up aid; zoomed out it fades out
   gracefully. No work.
2. **Zoom-aware shading with a moderate boost (F).**
   - Build a small pyramid of shaded images per cell: full resolution,
     1/4 and 1/16. Each level is shaded from the depth surface averaged to
     that level, with exaggeration × ⁴√(cells per pixel).
   - `SmoothRasterRenderer` picks the level that matches the screen
     resolution and keeps trilinear sampling within that level.
   - The full-resolution image is unchanged, so close up nothing changes.
   - Risk: a visible jump when the level changes during a zoom. Cross-fading
     the two nearest levels in the renderer would avoid it at the cost of a
     second draw.
3. **The same pyramid with a stronger boost (E).** More striking, but it
   competes with the band colours and amplifies survey artefacts. Only worth
   it if the design pass gives users a strength control.
4. **A user strength control instead of zoom awareness.** Covered by the
   brief's question 2. It doesn't fix the zoomed-out fade on its own, because
   a strength that suits the zoomed-out view is too strong close up.

## Not covered

- The 5 m Rotterdam cell (102NL005_518N044E) fits on screen at 4 cells per
  pixel, so the differences there are small. The fade mainly affects 1 m and
  2 m cells.
- The headless renderer (`s100 render`) draws through
  `CoverageHeadlessRenderer`, not the Mapsui raster path, so it needs its own
  decision if option 2 goes ahead.

## Outcome (2026-11-11)

Option 2 was built and checked in the viewer, then dropped: it has no
visible effect there.

- The harness ignored the viewer's grid display limit (`GridDisplayScale`).
  The viewer hides a grid once one nominal 0.28 mm pixel covers 4 grid
  nodes, about 1:14 300 for a 1 m cell.
- On a Retina display that is at most about 2 cells per device pixel. The
  13–16 cells per pixel drawn in the sheets above never happen.
- At 1:14 000 on 102DE00KG45 the cell spans about 1 100 device pixels for
  2 330 cells, so the 1/4 image (about 580 px) is never chosen, and the
  relief already reads well with the full-resolution image and mipmaps.

The zoomed-out fade only matters if relief should stay visible beyond the
grid display limit. That limit keeps panning fast with many cells loaded,
so changing it is a separate decision.
