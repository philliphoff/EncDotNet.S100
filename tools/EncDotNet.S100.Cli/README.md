# EncDotNet.S100.Cli (`s100`)

A small, cross-platform command-line tool for working with S-100 datasets. Its
primary command renders any supported dataset — or a composite of several,
via repeated `--layer` or by pointing at an entire exchange set / directory —
to a PNG, JPEG, or WebP image by
running the dataset's portrayal pipeline through the Mapsui-free Skia *headless*
it can also report a dataset's product specification (`info`), validate a
dataset against its specification's normative rule pack (`validate`), perform a
headless ECDIS-style "pick" of the features and coverage values at a point
across one or more layers (`identify`), and
convert an S-57 base cell to an S-101 dataset (`s57 convert`). It is
intended as the basis for batch scripts (for example, generating previews of
sea-ice or surface-current datasets, or gating a data pipeline on validation).

The command name is **`s100`**.

## Agent skill document

Run `s100 --skill` to print a complete, plain-Markdown description of the
command hierarchy, arguments, options, examples, workflows, output contracts,
exit codes, and operational limitations. Unlike progressive `--help` output,
the skill document covers every command in one invocation and is intended for
automated agents. It is written to standard output without ANSI styling and
does not perform the automatic release update check.

## Version and update notifications

Run `s100 --version` to print the CLI's assembly informational version:

```text
$ s100 --version
0.24.0+a1f9c20
```

Official builds also check the repository's latest GitHub Release. When a newer
version is known, every invocation writes a single notice to **standard error**:

```text
Update available: s100 0.25.0 (current 0.24.0): https://github.com/philliphoff/EncDotNet.S100/releases/tag/v0.25.0
```

Standard output and the command's exit code are unchanged, so JSON and other
machine-readable output can still be redirected or parsed normally. The latest
release and check timestamp are cached in the user's local application-data
directory; GitHub is contacted at most once every 24 hours. Development builds
(`0.0.0-dev`) do not check. Network, API, and cache failures are silent and
never prevent a command from running.

## Distribution

The `s100` CLI is distributed two ways, both produced by CI on each release:

### Standalone download (recommended)

Each GitHub Release attaches a **self-contained, per-platform archive** that
bundles the .NET runtime and SkiaSharp's native libraries, so **no .NET
installation is required**:

| Platform | Asset | Extract & run |
|---|---|---|
| Windows x64 | `s100-<version>-win-x64.zip` | `s100.exe render …` |
| Windows arm64 | `s100-<version>-win-arm64.zip` | `s100.exe render …` |
| macOS (Apple silicon) | `s100-<version>-osx-arm64.tar.gz` | `./s100 render …` |
| Linux x64 | `s100-<version>-linux-x64.tar.gz` | `./s100 render …` |
| Linux arm64 | `s100-<version>-linux-arm64.tar.gz` | `./s100 render …` |

The macOS archive is code-signed and notarized; Gatekeeper verifies the
notarization online on first run. If macOS still quarantines the extracted
binary (for example when copied between machines), clear the attribute with:

```bash
xattr -d com.apple.quarantine ./s100
```

#### Linux runtime dependencies

The self-contained archive bundles the .NET runtime and SkiaSharp's native
libraries, but it does **not** bundle the handful of system shared libraries
those components load from the OS. Minimal/server and container base images
(for example `ubuntu`, `debian`, `mcr.microsoft.com/dotnet/runtime-deps`) often
omit them, so install them before running `s100`:

| Package | Required on | Why |
|---|---|---|
| `libicu` (`libicu74` on Ubuntu 24.04, `libicu72` on Debian 12) | **all** Linux | .NET globalization. Without it **every** command aborts at startup with a `Couldn't find a valid ICU package` failure. |
| `fontconfig` + a font package (e.g. `fonts-dejavu-core`) | optional | The bundled `libSkiaSharp.so` is the self-contained `SkiaSharp.NativeAssets.Linux.NoDependencies` build (issue #23), so `render` loads and draws text **without** fontconfig: labels fall back to a font embedded in the renderer. Installing fontconfig and a system font package only changes *which* font labels use (the discovered system font instead of the embedded fallback) and silences the `Fontconfig error: Cannot load default config file` warning. |

> The shipped native library is now the same self-contained
> `NoDependencies` build on both `linux-x64` and `linux-arm64`. Earlier
> releases bundled the regular native, which hard-linked `libfontconfig.so.1`
> on x64 (so `render` failed to load without it) and aborted on arm64 with
> `undefined symbol: uuid_parse` once fontconfig was present (issue #221).

Debian/Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y libicu74          # required
sudo apt-get install -y fontconfig fonts-dejavu-core   # optional: system label fonts
# Debian 12: use libicu72 instead of libicu74
```

`s100 list-specs`, `s100 info`, and `s100 validate` need only `libicu`;
`s100 render` (the Skia path) also needs only `libicu` — text renders via an
embedded fallback font even with no fontconfig or system fonts installed.

### Inside the Viewer application bundle

The `s100` executable also ships **inside the S-100 Viewer application bundle**
so a single viewer download provides both the GUI and the command-line tool. CI
publishes it self-contained (per platform RID) into a `cli/` subfolder of the
viewer's publish output:

| Platform | Location of `s100` |
|---|---|
| macOS | `EncDotNet.S100.Viewer.app/Contents/MacOS/cli/s100` (code-signed, notarized, and hardened-runtime alongside the viewer) |
| Windows | `cli/s100.exe` next to the published viewer |
| Linux | `cli/s100` next to the published viewer |

On macOS the bundled `s100` binary and its native libraries are signed by the
same step that signs the viewer, so it runs without Gatekeeper prompts.

## Commands

### `s100 render <dataset> <output>` (single dataset)
### `s100 render --layer <dataset> … <output>` (composite)
### `s100 render <exchange-set> <output>` (exchange-set composite)

Detects the product specification of each input, runs its portrayal pipeline,
and writes an image. Three grammars are supported:

- **Single dataset** — `s100 render <dataset> <output>` renders one dataset.
- **Composite** — `s100 render --layer A --layer B … <output>` stacks several
  products into one image via the renderer-neutral S-98 interoperability
  engine. `--layer` is repeatable; the output path is either the trailing
  positional argument or `-o|--output`.
- **Exchange set** — `s100 render <exchange-set> <output>` (or
  `--exchange-set`/`--from`) discovers and composites **every** renderable
  dataset in a directory / `CATALOG.XML` / exchange-set `.zip`, so you don't have
  to enumerate each `--layer`. Auto-detected when the positional input is a
  directory, a `CATALOG.XML`, or an exchange-set `.zip`. Mutually exclusive with
  `--layer`.

The output format (PNG, JPEG, or WebP) is inferred from the file extension
unless `--format` is given.

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | _none_ | Add a dataset as a composite layer (repeatable). When any `--layer` is given, the composite grammar is used. |
| `--exchange-set`, `--from <path>` | _none_ | Composite an entire exchange set (directory / `CATALOG.XML` / `.zip`). A directory / `CATALOG.XML` / `.zip` passed positionally is also auto-detected. Mutually exclusive with `--layer`. |
| `--only <specs>` | _all_ | **Exchange-set only.** Restrict compositing to a comma-separated list of product specifications (e.g. `--only S101,S128`; hyphenation and case are ignored). |
| `-o`, `--output <path>` | _positional_ | Output image path. Required (or given positionally) for the composite forms; an alternative to the positional `<output>` for the single form. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | auto-fit | Explicit viewport as a WGS-84 bounding box (e.g. `--bbox -1.5,50.0,-1.0,50.5`). Mutually exclusive with `--center`/`--scale`. Applies to single-dataset and composite image renders; on a single vector dataset it also enables S-100 Part 9 scale-visibility culling, while S-102/S-104/S-111 sample only the intersecting grid region. Not supported with `--format json`. |
| `--center <lon,lat>` | auto-fit | Explicit viewport centre. Must be used with `--scale`. Same applicability as `--bbox`. |
| `--scale <denominator>` | auto-fit | Explicit viewport scale denominator (e.g. `--scale 50000` for 1:50 000). Must be used with `--center`. Same applicability as `--bbox`. |
| `-w`, `--width` | `1024` | Output image width in pixels. |
| `-h`, `--height` | `768` | Output image height in pixels. |
| `--palette` | `day` | Colour palette: `day`, `dusk`, or `night`. |
| `--symbol-scale` | `1.0` | Symbol scale factor. |
| `--text-scale` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time-step index for time-series datasets (S-104 / S-111). |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--format <fmt>` | inferred from extension, else `png` | Output format: `png`, `jpeg` (`jpg`), `webp`, or `json`. When omitted, the format is inferred from the output file extension; an unrecognised extension falls back to `png`. `json` emits the S-100 Part 9 **display list** (see [Display-list output](#display-list-output-format-json)) instead of an image — **single-dataset form only**. An explicit `--format` that conflicts with a recognised output extension is rejected. |
| `--quality <1-100>` | `90` | Encoder quality for lossy formats (`jpeg`, `webp`). Ignored for `png`. |
| `--no-text` | off | Suppress text/label drawing instructions. Shorthand for `--hide text`. In the composite form the suppression is global (applies to every layer). |
| `--hide <list>` | _none_ | Comma-separated list of drawing-instruction categories to suppress: `text`, `points`, `lines`, `areas` (e.g. `--hide text,points`). Combines additively with `--no-text`. In the composite form the suppression is global. Useful for label-dense products such as S-411 sea-ice, where the egg-code text overlaps fills at preview scales — `--no-text` yields a BSIS-style "clean fill" preview. |
| `--no-updates` | off | **Single form only.** Do not apply S-101 sequential updates. By default, when the dataset is an S-101 base cell (`….000`), any sibling update files (`….001`, `….002`, …) in the same directory are applied best-effort before rendering so the cell is drawn at its up-to-date state (S-100 Part 10a). |
| `--basemap <mode>` | `none` | Draw a basemap **beneath** the chart data: `none` (default) or `offline`. `offline` composites the bundled Natural Earth 1:10m land layer (public domain) under all chart layers in the muted parchment tone `238,232,220`, projected with the chart's own Web-Mercator viewport so it registers exactly. Works in both the single-dataset and `--layer` composite forms. Online tile basemaps (e.g. OSM) are **not** available in the headless renderer — only the offline land layer. |
| `--display-mode <mode>` | `ice-concentration` | **S-411 only.** Sea-ice portrayal display mode: `ice-concentration` (total concentration, default), `ice-sod` (stage of development) or `ice-navigational` (**provisional** preview derived from total concentration — **not** a POLARIS/RIO navigational-risk computation). One dataset carries the full WMO egg code, so the same data renders in any mode; the concentration and stage-of-development colours are held inline in the adapter, mirrored from the bundled upstream WMO tables and guarded against drift by an xunit parity test. Applies to both forms (any S-411 layer in a composite reacts). Supplying it for a non-S-411 dataset is an error. Run `s100 info <dataset>` to list the supported modes. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 render currents.h5 currents.png --time-step 6 --palette night
s100 render warnings.gml warnings.png --width 2048 --height 1536
s100 render seaice.gml seaice.png --no-text                # clean fill preview
s100 render seaice.gml seaice.png --basemap offline        # land under the chart
s100 render chart.gml chart.png --hide text,points         # hide text + symbols
s100 render NL4NZ110.000 cell.png                          # applies .001/.002/… updates
s100 render NL4NZ110.000 base.png --no-updates             # render the base cell only
s100 render warnings.gml warnings.jpg --quality 85         # JPEG (format inferred from .jpg)
s100 render warnings.gml preview.webp                      # WebP preview
s100 render warnings.gml warnings.json                     # display list (not an image)
s100 render warnings.gml list.txt --format json           # display list, explicit format
s100 render enc.000 window.png --bbox -1.5,50.0,-1.0,50.5  # exact vector viewport
s100 render enc.000 window.png --center -1.25,50.25 --scale 50000  # centre + scale
s100 render bathy.h5 window.png --bbox -1.5,50.0,-1.0,50.5  # crop/sample coverage

# Composite several products into one chart:
s100 render --layer enc.000 --layer bathy.h5 --layer warnings.gml chart.png
s100 render --layer enc.000 --layer bathy.h5 -o chart.png --bbox -1.5,50.0,-1.0,50.5
s100 render --layer enc.000 --layer bathy.h5 chart.png --center -1.25,50.25 --scale 50000
s100 render --layer enc.000 --layer warnings.gml chart.png --basemap offline

# Composite an entire exchange set / directory / .zip:
s100 render exchange-set/ chart.png                         # auto-detected directory
s100 render exchange-set/CATALOG.XML chart.png              # auto-detected catalogue
s100 render --exchange-set exchange-set.zip -o chart.png    # explicit, ZIP archive
s100 render --from exchange-set/ chart.png --only S101,S102 # restrict to some specs
```

#### Display-list output (`--format json`)

Instead of rasterising, the single-dataset form can emit the dataset's
**S-100 Part 9 display list** — the ordered set of drawing instructions the
portrayal pipeline produced — as JSON. This captures *what* was drawn (symbol,
line-style and area-fill references, colours, display planes, drawing
priorities, viewing groups, text) rather than pixels, so a portrayal change can
be reviewed, diffed, and snapshot-tested in text without a viewer or an image
comparison. It is available for vector products (S-101, S-57, and the GML
families S-12x / S-129 / S-131 / S-201 / S-411 / S-421); coverage products
(S-102 / S-104 / S-111) do not emit a Part 9 display list and report a clear
error.

The document is **pure portrayal output**: it contains no timing and no encoder
settings, and geometry is summarised (type, vertex count, and a representative
latitude/longitude anchor) rather than dumped in full, so two runs over the same
dataset and render context produce byte-identical JSON. The palette and the
portrayal options (symbol/text scale, display mode, and time step) still
influence which instructions the pipeline emits. The raster-only options
`--width` / `--height` / `--quality` / `--background` are ignored (the JSON path
builds the display list without rasterizing), and the viewport options `--bbox`
/ `--center` / `--scale` are rejected for `--format json` (a display list has no
viewport).

```bash
s100 render warnings.gml warnings.json           # infer json from the .json extension
s100 render warnings.gml out.txt --format json   # explicit; any non-image extension
```

```jsonc
{
  "dataset": "navwarn_surface.gml",
  "product": "S-124",
  "spec": "S-124/1.0.0",
  "palette": "day",
  "instructionCount": 3,
  "categoryCounts": { "areas": 0, "lines": 0, "points": 3, "text": 0 },
  "instructions": [
    {
      "kind": "point",
      "feature": "f1",
      "subLayer": 0,
      "plane": "UnderRadar",
      "viewingGroup": 31020,
      "drawingPriority": 15,
      "symbol": "NavigationalWarningFeaturePart",
      "geometry": { "type": "Surface", "vertexCount": 5, "anchor": [51.05, 1.2] }
    }
  ]
}
```

> **Composite ordering.** The S-98 authority orders layers by display plane,
> so the order in which you pass `--layer` is only a **within-plane tiebreak** —
> hand-ordering layers generally has no visible effect. Explicitly ordering an
> S-102 bathymetry surface above an S-101 chart, for example, is unnecessary:
> the plane assignment already places it correctly.
>
> **Composite viewport.** When no `--bbox` / `--center`+`--scale` is given the
> compositor auto-fits the union extent of all layers to the requested
> `--width` × `--height`.
>
> **Composite and S-101 updates.** Neither composite form applies S-101
> sequential/sibling updates — `--no-updates` applies to the single-dataset
> form only. Render an S-101 cell singly if you need its updates folded in.
>
> **Exchange-set discovery.** The exchange-set form reuses the same
> exchange-set reader the viewer and `validate` use. Only base and single cells
> are composited (S-101 update files and orphan updates are skipped). Datasets
> whose product specification is unsupported, whose file is missing, or that
> declare data protection (encryption — this CLI has no decryption keys) are
> **skipped with a warning on stderr** rather than failing the whole render; if
> nothing renderable remains, `render` exits non-zero. A `.zip` exchange set is
> extracted to a uniquely-named temporary directory (cleaned up after rendering,
> even on failure), so a large set needs transient temporary disk space of
> roughly its uncompressed size.

> **S-101 sequential updates.** When pointed at an S-101 base cell
> (`….000`), the single-dataset `render` and `info` discover sibling update
> files (`….001`, `….002`, …) in the same directory and apply them in order
> before processing the cell, mirroring how an exchange set is loaded
> in the viewer. Application is best-effort: a missing, out-of-order, or
> unreadable update is reported but never blocks the command. Pass
> `--no-updates` to operate on the base cell exactly as named.

### `s100 info <dataset>`

Prints the detected specification, edition, whether the dataset supports
headless rendering, and — for time-series datasets — the available time steps
with their indices (use the index with `render --time-step`). For datasets
whose portrayal catalogue declares display modes (e.g. S-411 sea-ice) it also
lists the available `render --display-mode` tokens. For an S-101
base cell, sibling sequential updates are applied first (see `--no-updates`)
so the reported model reflects the up-to-date cell.

### `s100 identify --lat <lat> --lon <lon>`

Performs a headless ECDIS-style **pick**: at a geographic point it identifies
the vector features and samples the coverage products across one or more
dataset layers — the same interaction the viewer offers on a cursor click, but
without an open viewer or MCP server. It drives the same shared pick services
(`IdentifyFeaturesService` / `SampleCoverageService`) the MCP
`identify_features` / `sample_coverage` tools use, so the results match
exactly. Features are ranked in ECDIS draw order (point before curve before
area; nearer before farther).

Three input grammars mirror `s100 render`: a single positional dataset,
repeated `--layer` options, or an exchange set (positional directory /
`CATALOG.XML` / `.zip`, or `--from`). Datasets whose product specification is
unsupported, whose file is missing, or that fail to parse are skipped with a
warning on stderr rather than failing the whole pick.

| Option | Default | Description |
|---|---|---|
| `--lat <lat>` | _required_ | Pick latitude in decimal degrees, WGS-84 (EPSG:4326), range −90..90. |
| `--lon <lon>` | _required_ | Pick longitude in decimal degrees, WGS-84 (EPSG:4326), range −180..180. |
| `--layer <path>` | _none_ | Add a dataset as a pick layer (repeatable). Mutually exclusive with the exchange-set form. |
| `--from`, `--exchange-set <path>` | _none_ | Pick across every dataset in an exchange set (directory, `CATALOG.XML`, or `.zip`). |
| `--only <specs>` | _all_ | Comma-separated spec filter for the exchange-set form (e.g. `--only S101,S102`). |
| `--radius <metres>` | `50` | Search radius for point/near-miss feature matching (clamped 0..100000). |
| `--spec <spec>` | _all_ | Restrict features and samples to one specification (e.g. `S-124`). |
| `--time <iso8601>` | _first step_ | Time step for coverage sampling of time-series products (S-104 / S-111). When omitted the first available step is used; when supplied the nearest step is selected (clamped to the dataset range). |
| `--max-results <n>` | `20` | Maximum number of features to report (clamped 1..200). |
| `--attributes` | off | Include each feature's full attribute set (via `DescribeFeatureService`). |
| `--format <fmt>` | `table` | Output format: `table` (human-readable) or `json` (machine-readable). |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 identify warnings.gml --lat 51.085 --lon 1.30            # single dataset
s100 identify --layer enc.000 --layer bathy.h5 --lat 50.1 --lon -1.4   # several layers
s100 identify --from exchange-set.zip --lat 50.1 --lon -1.4 --format json
s100 identify chart.000 --lat 50.1 --lon -1.4 --attributes    # include feature attributes
```

The JSON output is an object with `point`, `totalMatched`, `truncated`,
`features` (ranked), `samples` (one per coverage layer that covers the point),
and, when any input was skipped, a `warnings` array.

### `s100 validate <dataset>`

Detects the product specification of `<dataset>` and runs that spec's normative
validation rule pack against the parsed dataset, reporting the findings. Each
finding carries a spec-traceable rule id (e.g. `S127-R-2.1`), a severity, a
message, and — where the rule can locate the problem — a feature id or
geographic position.

Validation is a pure function of the parsed dataset: it does not depend on the
palette, opacity, or selected time step. Specs without a rule pack today —
S-101, S-201, and S-57 — report *no rules available* and exit successfully;
this is distinct from a dataset that was evaluated and found conformant.

| Option | Default | Description |
|---|---|---|
| `--format <fmt>` | `text` | Output format: `text` (a findings table) or `json` (machine-readable, for CI). |
| `--suppress <list>` | _none_ | Comma-separated list of rule ids (or `*` glob patterns) whose findings are dropped from the report **and** ignored by the exit code — e.g. `--suppress S101-R-1.2,S101-R-3.2` or `--suppress "S101-*"`. Compiler-style "no-warn": mute a known rule class (such as a feature-catalogue-version mismatch) to surface the more-likely-real findings. The count of suppressed findings is still reported. |
| `--strict` | off | Treat warnings as failures: exit `6` when any warning (not just an error) is present. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 validate warnings.gml                 # human-readable findings table
s100 validate route.gml --strict           # fail the build on warnings too
s100 validate currents.h5 --format json    # machine-readable report for CI
s100 validate chart.000 --suppress S101-R-1.2,S101-R-3.2   # mute a rule class
s100 validate chart.000 --suppress "S101-*"                # glob: mute a whole spec's rules
```

The `--suppress` patterns are matched case-insensitively against each finding's
rule id; `*` is the only wildcard and matches any run of characters, so a
pattern with no `*` is an exact rule-id match. Suppressed findings are removed
from the table/JSON and do not contribute to the exit code, but the trailing
summary still reports how many were suppressed.

By default the command exits `0` when no **error**-severity findings remain
after suppression (warnings and info are reported but do not fail); pass
`--strict` to also fail on warnings. Exit code `6` signals failing findings.

### `s100 list-specs`

Lists the supported product specifications and whether each supports the
headless render path.

### `s100 s57 convert -o <output> <source>`

Converts an S-57 base cell (`.000`) to an S-101 dataset, writing an ISO/IEC 8211
encoded `.000` file (S-100 Part 10a). The source is translated to an
`S101Document` in memory with the same `S57ToS101Translator` the render/validate
paths use, then encoded with `S101DocumentWriter`.

Sibling sequential update files (`.001`, `.002`, …) sitting next to the base
cell are auto-discovered and folded in (S-57 Part 3 dataset updating) before
translation, so a converted cell reflects its up-to-date state. Pass
`--no-updates` to convert only the bare base cell. After a successful convert a
concise translation-coverage summary is printed; add `--report <path>` to also
write the full diagnostics as JSON.

```
s100 s57 convert -o my-s101-dataset.000 my-s57-dataset.000
```

| Option | Default | Description |
|---|---|---|
| `-o`, `--output <path>` | _required_ | Path of the S-101 dataset file to write. |
| `--report <path>` | off | Write a machine-readable (JSON) translation diagnostics report. |
| `--no-updates` | off | Do not auto-discover and fold sibling update files before converting. |
| `--debug` | off | Show a full stack trace on error. |

The written dataset can be inspected with `s100 info`, validated with
`s100 validate`, and rendered with `s100 render`. Conversion semantics (feature
and attribute mapping, allowed-value enforcement) are owned by
`S57ToS101Translator`; this command only drives it and encodes the result.

### `s100 mcp serve <dataset-or-exchange-set>`

Hosts the read-only S-100 [Model Context Protocol](https://modelcontextprotocol.io)
tools over the **stdio** transport, so an MCP client that spawns this process can
query the served datasets — features, attributes, spatial queries, and coverage
samples — without a GUI viewer. The tools are the same read-only set the desktop
viewer exposes (`list_datasets`, `describe_feature`, `query_features`, `find_at`,
`identify_features`, `nearest_features`, `count_features`, `search_features`,
`sample_coverage`, `sample_coverage_along`, `list_specs`, `list_time_steps`);
none mutate data, load/unload datasets, or write files.

The datasets to serve are specified up front, using the same input grammar as
`s100 identify`: a single positional dataset, repeated `--layer` options, or an
exchange set (positional directory / `CATALOG.XML` / `.zip`, or `--from`). They
are loaded once into an immutable catalog and served read-only; the process is
the session boundary — spawn another `serve` for a different set.

```
s100 mcp serve dataset.h5
s100 mcp serve --layer enc.000 --layer bathy.h5
s100 mcp serve --from exchange-set.zip --only S101,S102
```

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | — | Add a dataset to serve (repeatable). Mutually exclusive with the exchange-set form. |
| `--from`, `--exchange-set <path>` | — | Serve every discoverable dataset in an exchange set. Mutually exclusive with `--layer`. |
| `--only <specs>` | all | Exchange-set form only: restrict loading to a comma-separated spec list (e.g. `S101,S102`). |
| `--debug` | off | Show a full stack trace on error. |

Configure it as an MCP server with command `s100` and args
`["mcp", "serve", "<dataset-or-exchange-set>"]`. **Standard output carries the
MCP protocol** — startup notices, load warnings, and errors are written to
standard error so they never corrupt the stream. The server runs until the
client disconnects (stdin end-of-file) or it is interrupted.

## Supported specifications

| Family | Specs | Path |
|---|---|---|
| Vector (ISO 8211) | S-101, S-57 (translated to S-101) | `HeadlessVectorRenderer` |
| Vector (GML) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `HeadlessVectorRenderer` |
| Coverage (HDF5) | S-102; S-104 DCF1/2/8; S-111 DCF1/2/3/8 | Coverage and point-glyph headless renderers |

## Limitations

- **Explicit coverage viewports.** `--bbox` / `--center`+`--scale` crop
  S-102/S-104/S-111 gridded sampling to the requested WGS-84 window and
  reproject projected grids from their native CRS. Positioned station/node
  datasets render their point glyphs against the requested viewport.
- **S-57 renders through the S-101 pipeline.** Datasets are translated to
  `S101Document` in-memory and rasterised with S-101 symbology (not S-52).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unhandled error (use `--debug` for a stack trace). |
| `2` | Product specification could not be detected (single dataset), or no renderable datasets were discovered in an exchange set. |
| `3` | The detected spec does not support headless rendering. |
| `4` | The dataset is recognised but its shape or encoding is unsupported by the selected operation. |
| `5` | The dataset is recognised but non-conforming (a required attribute, dataset, or group is missing or malformed). |
| `6` | `validate` only: the dataset was evaluated and produced failing findings (any error-severity finding, or — with `--strict` — any warning). |
| non-zero | Argument validation failure (missing file, bad palette, etc.). |
