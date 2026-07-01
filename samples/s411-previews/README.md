# S-411 sea-ice preview generator (sample)

A proof-of-concept Bash script that generates **S-411 sea-ice "quicklook"
previews** using the [`s100` CLI](../../tools/EncDotNet.S100.Cli/README.md).

It mirrors the idea behind the
[BSIS Ice Portal](https://www.bsis-ice.de/IcePortal/ILP_S411.shtml) previews:
download the published S-411 exchange-set ZIPs, extract the GML dataset, and
rasterise a PNG. Here the rendering is done entirely by `s100 render` — so the
same machinery the desktop viewer uses also drives an unattended batch job.

The published ZIP names are dated and rotate (often daily). Instead of
hard-coding a snapshot, the script **discovers the current ZIP** for each region
by scanning the live portal index for a stable per-region pattern, so it keeps
working day-to-day. A `COMPARE=1` mode additionally downloads the matching BSIS
quicklook(s) next to our render for side-by-side inspection.

## Prerequisites

- .NET 10 SDK
- `bash`, `curl`, `unzip`
- A built CLI:

  ```bash
  dotnet build tools/EncDotNet.S100.Cli -c Release
  ```

## Usage

From anywhere in the repo:

```bash
# Render the default region set into ./s411-previews
samples/s411-previews/s411_previews.sh

# Choose an output directory and explicit regions
samples/s411-previews/s411_previews.sh /tmp/out north-atlantic hudson-bay

# Render every known region
samples/s411-previews/s411_previews.sh /tmp/out all

# Render + fetch the BSIS quicklooks for side-by-side comparison
COMPARE=1 samples/s411-previews/s411_previews.sh /tmp/out cw-greenland
```

Available region keys: `cw-greenland`, `nw-greenland`, `ne-greenland`,
`se-greenland`, `sw-greenland`, `ce-greenland`, `cape-farewell`, `qaanaaq`
(DMI Greenland); `canada-east`, `hudson-bay`, `eastern-arctic`,
`western-arctic` (CIS); `alaska` (US NWS); `north-atlantic` (Met.no). Use `all`
to render them all. Add more by extending the `region_zip_pattern` /
`region_conc_image` / `region_sod_image` lookups with entries from the BSIS
portal.

### Environment overrides

| Variable | Default | Purpose |
|---|---|---|
| `S100` | runs the Release DLL via `dotnet` | Command used to invoke the CLI (e.g. a published `s100` binary). |
| `WIDTH` / `HEIGHT` | `1600` | Output size in pixels. |
| `PALETTE` | `day` | `day` \| `dusk` \| `night`. |
| `EXTRA_OPTS` | _empty_ | Extra flags forwarded verbatim to `s100 render` (e.g. `--no-text`). |
| `COMPARE` | `0` | When `1`, also download the BSIS quicklook(s) for each region to `previews/<key>.bsis-conc.png` / `.bsis-sod.png`. If ImageMagick's `montage` is on `PATH`, a combined `previews/<key>.compare.png` contact sheet is produced. |

```bash
# Use an installed global tool and a larger night-palette canvas
S100="s100" WIDTH=2048 HEIGHT=2048 PALETTE=night \
  samples/s411-previews/s411_previews.sh
```

## Output

```
<out-dir>/
  data/ILP_S411.shtml        cached portal index (used for ZIP discovery)
  data/<region>.zip          downloaded exchange set
  data/<region>/...          extracted contents
  previews/<region>.png      rendered preview
  previews/<region>.bsis-conc.png   BSIS concentration quicklook   (COMPARE=1)
  previews/<region>.bsis-sod.png    BSIS stage-of-development quicklook (COMPARE=1)
  previews/<region>.compare.png     contact sheet, if `montage` present (COMPARE=1)
```

## Notes & caveats

- **Current ZIPs are discovered automatically.** The script scans the live
  portal index for a stable per-region pattern (the rotating date is wildcarded)
  and picks the most recent match, so it keeps working as the portal updates. If
  a region stops resolving, its file-name scheme likely changed — update the
  `region_zip_pattern` entry from <https://www.bsis-ice.de/IcePortal/ILP_S411.shtml>.
- **Clean-fill previews:** to mirror the BSIS portal's text-free look, pass
  `--no-text` to the CLI (or set `EXTRA_OPTS=--no-text` and have the script
  forward it). For example:

  ```bash
  EXTRA_OPTS="--no-text" samples/s411-previews/s411_previews.sh
  ```

  This suppresses the S-411 egg-code labels at the renderer level while
  leaving the fills, ice-edge lines, and symbols untouched. Use
  `--hide text,points` to also drop point symbols.
- This is sample/PoC code, not a supported product. Generated output under the
  chosen directory is intentionally not committed.
- Data © the respective ice services (CIS, DMI, Met.no, US NWS/NIC, AARI, SHN,
  …) via the BSIS Ice Portal; respect their terms of use.

## Parity with the BSIS quicklooks

The BSIS quicklooks are produced by a short bespoke Python script. Running this
sample with `COMPARE=1` against today's data (e.g. DMI CentralWest-Greenland)
shows how close `s100 render` gets to them, and where the honest gaps are.

**What matches.** The underlying S-411 data is reproduced faithfully — ice-edge
polygons, fjord tongues, and offshore patches line up with the BSIS
concentration quicklook essentially polygon-for-polygon. Reading, parsing, and
coverage of the live GML are not in question.

**What differs.** `s100 render` rasterises the *chart layer* using the bundled
S-411 portrayal catalogue; the BSIS script composes a full *figure*. Concretely:

| Aspect | BSIS quicklook | `s100 render` today |
|---|---|---|
| **Basemap** | Blue ocean + white land + lat/lon graticule + title + axes | Ice polygons only, on a transparent/white canvas (no land/ocean layer) |
| **Concentration palette** | Vivid WMO ramp (green→yellow→orange→red for low→high) | Muted tan/peach ramp from the bundled S-411 PC |
| **Stage of development (SOD)** | Separate brown/green "thickness" map | Not produced (the bundled PC portrays concentration) |
| **Projection** | Equirectangular (level rectangle) | Web-Mercator (slightly rotated) |
| **Extent** | Fixed regional frame incl. surrounding coast | Auto-fit to the ice-polygon bounds |

**Out of scope entirely.** Most BSIS region pages also publish ~12 **POLARIS**
navigational-risk maps — one per ice class (PC1–PC7, 1As/1A/1B/1C, none),
coloured by Risk Index Outcome. Those are a *computed decision-support product*,
not a portrayal of the source data, and there is no POLARIS/RIO engine in this
codebase. `COMPARE` mode therefore only fetches the CONC/SOD quicklooks.

**Bottom line.** For the *data/geometry* this is effectively a drop-in; for a
*standalone quicklook figure* it is not, chiefly because there is no basemap
compositing and the concentration palette differs. Closing those would be
feature work (a land/ocean context layer + a WMO concentration palette + an SOD
portrayal), tracked separately from this sample.
