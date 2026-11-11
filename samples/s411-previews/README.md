# S-411 sea-ice preview generator (sample)

This sample is a Bash script that makes PNG previews of S-411 sea-ice data with
[`s100 render`](../../docs/cli.md#render). It works like the previews on the
[BSIS Ice Portal](https://www.bsis-ice.de/IcePortal/ILP_S411.shtml): it
downloads each region's published S-411 exchange set, extracts the GML dataset
and renders it. Use it as an example of running `s100` in an unattended batch
job.

The portal's file names include a date and change, often daily. The script
finds the current file for each region by matching a fixed pattern against the
live portal index, so you don't need to update it as new files are published.

## Prerequisites

- The .NET 10 SDK.
- `bash`, `curl` and `unzip`.
- A Release build of `s100`. From the repository root:

  ```bash
  dotnet build tools/EncDotNet.S100.Cli -c Release
  ```

## Run the script

You can run the script from any folder in the repository. With no arguments,
it renders the `cw-greenland`, `hudson-bay` and `alaska` regions into
`./s411-previews`:

```bash
samples/s411-previews/s411_previews.sh
```

Pass an output folder, then one or more region keys, or `all`:

```bash
samples/s411-previews/s411_previews.sh /tmp/out north-atlantic hudson-bay
samples/s411-previews/s411_previews.sh /tmp/out all
```

For each region, the script prints the file it found and the preview it wrote.
It skips a region whose file it can't find, download or render, and goes on to
the next.

### Regions

| Source | Region keys |
|---|---|
| DMI (Greenland) | `cw-greenland`, `nw-greenland`, `ne-greenland`, `se-greenland`, `sw-greenland`, `ce-greenland`, `cape-farewell`, `qaanaaq` |
| CIS (Canada) | `canada-east`, `hudson-bay`, `eastern-arctic`, `western-arctic` |
| US NWS | `alaska` |
| Met.no | `north-atlantic` |

To add a region, add entries to the `region_zip_pattern`, `region_conc_image`
and `region_sod_image` functions in the script, using the file names on the
BSIS portal. If a region stops resolving, its file-name pattern has probably
changed; update its `region_zip_pattern` entry from the
[portal index](https://www.bsis-ice.de/IcePortal/ILP_S411.shtml).

### Settings

Set these environment variables to change how the script renders:

| Variable | Default | Description |
|---|---|---|
| `S100` | `dotnet` with the Release `s100.dll` | The command that runs `s100`, such as the path to a downloaded `s100` binary. |
| `WIDTH`, `HEIGHT` | `1600` | The image size in pixels. |
| `PALETTE` | `day` | `day`, `dusk` or `night`. |
| `EXTRA_OPTS` | empty | More options for `s100 render`, added after `--width`, `--height` and `--palette`. |
| `COMPARE` | `0` | `1` also downloads the BSIS previews for comparison. See [Compare with the BSIS previews](#compare-with-the-bsis-previews). |

For example, to use a downloaded `s100` and a larger image in the night
palette:

```bash
S100="path/to/s100" WIDTH=2048 HEIGHT=2048 PALETTE=night \
  samples/s411-previews/s411_previews.sh
```

## Output

```text
<out-dir>/
  data/ILP_S411.shtml               the cached portal index
  data/<region>.zip                 the downloaded exchange set
  data/<region>/...                 the extracted exchange set
  previews/<region>.png             the rendered preview
  previews/<region>.bsis-conc.png   the BSIS concentration preview (COMPARE=1)
  previews/<region>.bsis-sod.png    the BSIS stage-of-development preview (COMPARE=1)
  previews/<region>.compare.png     a side-by-side sheet (COMPARE=1, with montage)
```

The output isn't meant to be committed.

## Change the preview

Pass `s100 render` options through `EXTRA_OPTS`:

- **Hide the labels.** `--no-text` removes the S-411 egg-code labels and leaves
  the fills, ice-edge lines and symbols, like the BSIS previews.
  `--hide text,points` also removes point symbols.

  ```bash
  EXTRA_OPTS="--no-text" samples/s411-previews/s411_previews.sh
  ```

- **Add land.** `--basemap offline` draws the bundled Natural Earth 1:10m land
  layer under the ice, so you can see the coastline. It doesn't use online
  tiles.

  ```bash
  EXTRA_OPTS="--no-text --basemap offline" samples/s411-previews/s411_previews.sh
  ```

- **Show the stage of development.** The default display mode shows total
  concentration. `--display-mode ice-sod` shows the stage of development
  instead.

  ```bash
  EXTRA_OPTS="--display-mode ice-sod" samples/s411-previews/s411_previews.sh
  ```

For every `render` option, see [Command-line rendering](../../docs/cli.md#render).

## Compare with the BSIS previews

With `COMPARE=1`, the script also downloads the BSIS concentration preview and,
where the portal publishes one, the stage-of-development preview, for each
region. If ImageMagick's `montage` is on your `PATH`, it also puts your render
and the BSIS previews side by side in `previews/<region>.compare.png`.

```bash
COMPARE=1 samples/s411-previews/s411_previews.sh /tmp/out cw-greenland
```

The comparison shows that `s100 render` reproduces the S-411 data: the
ice-edge polygons, fjord tongues and offshore patches line up with the BSIS
concentration preview. The differences come from how the image is composed.
`s100 render` draws the chart layer with the S-411 portrayal; the BSIS previews
are complete figures made by a separate script.

| Aspect | BSIS preview | `s100 render` |
|---|---|---|
| Background | Blue ocean, white land, a latitude and longitude grid, a title and axes | No background map by default. `--basemap offline` adds land; there's no ocean colour, grid, title or axes. |
| Concentration colours | A WMO colour ramp, green to red from low to high | WMO total-concentration colours, in the default `ice-concentration` mode. |
| Stage of development | A separate map | A separate render with `--display-mode ice-sod`. |
| Projection | Equirectangular | Web Mercator. |
| Extent | A fixed frame for each region, including the nearby coast | Fitted to the data. Use `--bbox` for a fixed frame. |

Most BSIS region pages also publish POLARIS navigational-risk maps, one for each
ice class, coloured by risk index outcome. These are calculated
decision-support products, not a portrayal of the data, and EncDotNet.S100 has
no POLARIS calculation. The `ice-navigational` display mode is a provisional
preview derived from total concentration, not a POLARIS result. `COMPARE` mode
downloads only the concentration and stage-of-development previews.

## Data and terms of use

The S-411 data comes from the ice services (CIS, DMI, Met.no, US NWS/NIC, AARI,
SHN and others) through the BSIS Ice Portal. Follow their terms of use.

This is sample code, not a supported product.
