# S-102 relief shading: design brief

Status: prototype engine in place, no UI. This brief is for a design pass on
how a mariner turns relief shading on, how much control they get, and how the
display makes clear that it is not the standard S-102 portrayal.

## Why

S-102 trial data is high resolution: the German BSH cells are 1 m grids, the
Dutch Rotterdam cells 5 m, the UK Clyde/Tees trials about 10 m. The S-102
Edition 3.0.0 portrayal (Annex B) paints each cell with the flat colour of
its depth band, so 2 or 4 colours in all. A whole 1 m cell can come out as one
solid blue (screenshot 03). The 3D-looking bathymetry that shows up in S-100
posts and vendor demos is hillshading: light the depth surface from the
north-west and darken or lighten each cell by its slope.

## What the prototype does

- Computes a hillshade from the sampled depths: light from 315° at 45°
  elevation, 3× vertical exaggeration, strength 0.6.
- Multiplies the shade into the band colour. Flat seabed keeps its exact band
  colour, slopes facing the light lighten towards white, and slopes facing
  away darken towards black. The band boundaries, safety contour and coverage
  limit are unchanged.
- No-data cells stay unshaded, and the edge of the data gets no dark rim.
- Works for geographic and projected (UTM) grids, in the viewer (Mapsui) and
  headless (`s100 render`) paths.
- Controlled by the map-wide display switch `s102.relief` in
  `EcdisDisplaySettings.LayerVisibility`. It is **off by default**, persisted
  in `settings.json` like the weather switches, and saved with display
  combinations. There is no UI yet. `s100 render <cell> --show-layer s102.relief`
  renders it.

## Screenshots

| | |
|---|---|
| `01-viewer-flat.png` | Rotterdam Waalhaven, 1:12 000, standard S-102 portrayal |
| `02-viewer-relief.png` | Same view, relief shading on (dredged pits, berth toes and slopes show) |
| `03-de-1m-flat.png` | BSH 1 m cell 102DE00KG45, standard portrayal: one band, flat |
| `04-de-1m-relief.png` | Same cell with relief: seabed morphology, scarps, scour |

## Decisions already made

- **Selectable, off by default.** It is a display aid, not portrayal. The
  default matches what the spec says an ECDIS shows.
- **Shading modulates the band colours, it does not replace them.** The safety
  meaning of the colours must survive.

## Questions for the design pass

1. **Where does the switch live?** Options:
   - a "Bathymetry" section in Display controls (next to the Weather section,
     shown only while S-102 is loaded);
   - with the depth settings (safety contour, four shades);
   - per dataset in the Datasets inspector.
   The engine is map-wide today. Per-dataset would need extra plumbing.
2. **How much control?** On/off only, or also a strength control
   (subtle/medium/strong) and vertical exaggeration? Light direction should
   probably stay fixed at the cartographic north-west.
3. **Marking non-standard display.** Should the map say that relief is on,
   for example a chip next to `DEPTHS Charted` in the status bar, a legend
   line, or a "Display not standard" stamp like the in-force warnings stamp?
4. **Dusk and Night palettes.** Lightening towards white brightens a night
   display. Options: darken only at Dusk/Night, cap the lightening, or switch
   relief off automatically in Night. Mocks of 02 in each palette would help
   decide.
5. **Interaction with S-101 under S-102.** S-102 draws at 0.8 opacity over
   the chart and basemap. Is shading readable enough there, or should relief
   raise the S-102 opacity?
6. **Name.** Candidates: "Relief shading", "Hillshade", "3D seabed",
   "Shaded relief". Mariners may not know "hillshade".

## Known engineering limits (not for design)

- Zoomed out, the viewer reads a shoal-biased overview grid. Shading there is
  coarser and can look noisy. Shading is computed per dataset, so seams can
  show where cells meet.
- The prototype re-renders the surface when the switch changes, like the
  weather switches do.
