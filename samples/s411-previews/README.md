# S-411 sea-ice preview generator (sample)

A proof-of-concept Bash script that generates **S-411 sea-ice "quicklook"
previews** using the [`s100` CLI](../../tools/EncDotNet.S100.Cli/README.md).

It mirrors the idea behind the
[BSIS Ice Portal](https://www.bsis-ice.de/IcePortal/ILP_S411.shtml) previews:
download the published S-411 exchange-set ZIPs, extract the GML dataset, and
rasterise a PNG. Here the rendering is done entirely by `s100 render` — so the
same machinery the desktop viewer uses also drives an unattended batch job.

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
```

Available region keys: `north-atlantic`, `canada-east`, `hudson-bay`,
`alaska`, `nw-greenland`. Add more by extending the `region_zip` lookup with
file names from the BSIS portal.

### Environment overrides

| Variable | Default | Purpose |
|---|---|---|
| `S100` | runs the Release DLL via `dotnet` | Command used to invoke the CLI (e.g. a published `s100` binary). |
| `WIDTH` / `HEIGHT` | `1600` | Output size in pixels. |
| `PALETTE` | `day` | `day` \| `dusk` \| `night`. |
| `EXTRA_OPTS` | _empty_ | Extra flags forwarded verbatim to `s100 render` (e.g. `--no-text`, `--basemap offline`). |

```bash
# Use an installed global tool and a larger night-palette canvas
S100="s100" WIDTH=2048 HEIGHT=2048 PALETTE=night \
  samples/s411-previews/s411_previews.sh
```

## Output

```
<out-dir>/
  data/<region>.zip          downloaded exchange set
  data/<region>/...          extracted contents
  previews/<region>.png      rendered preview
```

## Notes & caveats

- **The ZIP file names on the BSIS portal are dated and rotate** (often daily).
  The keys here point at a snapshot; if a download 404s, refresh the file name
  from <https://www.bsis-ice.de/IcePortal/ILP_S411.shtml>.
- **Clean-fill previews:** to mirror the BSIS portal's text-free look, pass
  `--no-text` to the CLI (or set `EXTRA_OPTS=--no-text` and have the script
  forward it). For example:

  ```bash
  EXTRA_OPTS="--no-text" samples/s411-previews/s411_previews.sh
  ```

  This suppresses the S-411 egg-code labels at the renderer level while
  leaving the fills, ice-edge lines, and symbols untouched. Use
  `--hide text,points` to also drop point symbols.
- **Offline land basemap:** pass `--basemap offline` (headless; no online
  tiles) to composite the bundled Natural Earth 1:10m land layer beneath the
  ice, so floes and ice edges read against the coastline instead of a bare
  background. Combine with `--no-text` for a clean, geo-referenced quicklook:

  ```bash
  EXTRA_OPTS="--no-text --basemap offline" samples/s411-previews/s411_previews.sh
  ```
- This is sample/PoC code, not a supported product. Generated output under the
  chosen directory is intentionally not committed.
- Data © the respective ice services (CIS, DMI, Met.no, US NWS/NIC, AARI, SHN,
  …) via the BSIS Ice Portal; respect their terms of use.
