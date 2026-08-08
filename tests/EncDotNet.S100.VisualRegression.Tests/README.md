# EncDotNet.S100.VisualRegression.Tests

Visual regression tests for the S-100 rendering pipelines. Each test runs a
representative dataset through its production pipeline + renderer, captures
the output as a PNG, and compares it against a checked-in baseline.

Built on the [`EncDotNet.S100.VisualRegression`](../EncDotNet.S100.VisualRegression/)
harness (which uses `Mapsui.Rendering.Skia` headlessly for vector products and
`SkiaCoverageRenderer` directly for coverage products) and
[Verify.Xunit](https://github.com/VerifyTests/Verify) for snapshot management.

## Layout

```
Snapshots/
  S101RenderingTests/
    S101RenderingTests.EncCell_DayPalette.verified.png   ← committed baseline
  S57RenderingTests/
    S57RenderingTests.EncCell_DayPalette.verified.png    ← S-57 → S-101 translation path
  S102RenderingTests/
    S102RenderingTests.BathymetricSurface_DepthShading_DefaultPalette.verified.png
  S104RenderingTests/
    ...
  ...
```

Each `*.verified.png` is the committed baseline. When a test fails, Verify
writes a sibling `*.received.png` (the new output) and the harness writes a
`*.diff.png` (red-highlighted pixel deltas) into the same directory.

## Running

```bash
dotnet test tests/EncDotNet.S100.VisualRegression.Tests
```

Tests use `[SkippableFact]` / `[SkippableTheory]` so they auto-skip when the
required dataset under `tests/datasets/` is missing — they will not fail CI
for missing fixtures.

## When a test fails

1. Open the failing test's `Snapshots/<TestClass>/` directory.
2. View the `*.received.png` (new render) and `*.diff.png` (red highlights).
3. Decide:
   - **Rendering regression** — fix the production code, re-run the test until
     it passes against the existing `*.verified.png`.
   - **Intentional change** — promote the received output:
     ```bash
     mv path/to/Foo.received.png path/to/Foo.verified.png
     ```
     and commit. (You can also use the `dotnet verify` global tool's
     `accept` command if installed.)

## Tolerances

`PerceptualImageComparer` defaults:

| Setting | Default | Meaning |
|---|---|---|
| `MaxChannelDelta` | 4 | Max absolute per-channel (R/G/B/A) diff before a pixel counts as different |
| `MaxDifferentPixelFraction` | 0.05 | Max fraction (5 %) of pixels that may differ |

These are tuned to absorb sub-pixel anti-aliasing and **cross-platform font
hinting drift** (the same dataset rendered on macOS vs Linux can disagree on
~1–2 % of pixels in label glyphs alone) while still catching real rendering
regressions in geometry, colour, or symbology.

## Adding a new test

1. Place the dataset under `tests/datasets/<spec>/...` (small fixtures only).
2. Add a method to the matching `S<NNN>RenderingTests` class:
   ```csharp
   [SkippableFact]
   public Task MyScenario()
   {
       var path = Path.Combine(TestHelpers.DatasetsRoot, "S101", "myCell.000");
       Skip.IfNot(File.Exists(path), $"Dataset not present: {path}");

       using var harness = new RenderHarness();
       var bitmap = harness.Render(path, new HarnessOptions { Width = 800, Height = 600 });

       return TestHelpers.VerifyBitmap(bitmap);
   }
   ```
3. Run the test once → inspect `*.received.png` → if correct, rename to
   `*.verified.png` and commit.

## A/B render-subsystem parity (`RenderParityTests`)

`RenderParityTests` is the headless half of the issue #347 "golden-image
parity set": it establishes that the tiled async **"B"** base-plane renderer
(`RenderSubsystemKind.TiledScene`) is at least as faithful as the per-feature
Mapsui **"A"** renderer. Three things run against the committed S-101 cell:

- **B-arm goldens** (`BMode_EncCell_Palette`, Day/Dusk/Night) — render the cell
  through "B" and compare to committed snapshots. This is the durable
  regression guard for the tiled renderer.
- **A/B close-match** (`AbParity_EncCell_Palette`, Day/Dusk/Night) — render the
  same cell through both arms and assert they match within the perceptual
  tolerance. Per the #347 decision, most datasets are expected to match
  closely; a divergence beyond tolerance **fails**, surfacing a real fidelity
  gap in one arm.
- **Dense labels+symbols** (`BMode_DenseCell_LabelsAndSymbols`) — local-only,
  golden-free: zooms "B" into a labelled harbour area and asserts the frame is
  non-blank and richly multi-coloured (area fills + point symbols + labels),
  proving the tiled overlay composites headlessly. Skipped in CI (real ENC data
  is never committed).

Both arms render with `EcdisDisplayCategory.Standard` to match the live
viewer's default display mode. (The legacy `S101RenderingTests` baselines use
the harness's historical `DisplayCategory = null`, i.e. no display-mode filter,
which draws supplementary `OtherInformation` content the live product hides at
`Standard` — so those baselines are **not** comparable pixel-for-pixel with the
parity goldens.)

### Why this is not a blanket `A == B` assertion

On dense real cells "B" sometimes *fixes* an "A" draw-order bug (e.g. a
supplementary depth area flooding the Isle of Wight land in the Solent trial
cell), where "B" > "A". A perpetual equality gate across every cell would emit
false failures there. The committed cell is a pure area-pattern fill with no
ordering hazard, so it is a stable apples-to-apples close-match fixture.

### What the headless path does *not* cover

The "B" base plane rasterises **north-up on a software surface**, so:

- viewport **rotation** uprightness (rotated "B" returns blank headlessly), and
- **GPU residency** (Metal/ANGLE-backed tile upload),

are out of scope for these tests and must be checked in the viewer.

## Multi-product A/B parity (`MultiProductParityTests`)

Where `RenderParityTests` covers the S-101 cell, `MultiProductParityTests`
extends the #347 "Multi-product / multi-dataset validation" item across the
non-S-101 products, turning the one-off A/B survey into committed CI gates. "B"
only swaps the **vector** base plane, so the guard has three tiers matched to
what can actually diverge:

- **Coverage exact-match** (`Coverage_AbPixelIdentical`, S-102/104/111) — the
  HDF5 coverage raster path is untouched by "B", so A and B must render
  effectively pixel-identical. A divergence means "B" leaked into a path it must
  not affect.
- **Per-product B-arm goldens** (`Vector_BArmGolden`) — one representative
  committed GML fixture per vector product (S-122/124/125/127/128/129/131/201/
  411/421) is rendered through "B" and compared to a committed snapshot, guarding
  the tiled renderer against self-drift across every product family.
- **Label preservation** (`Vector_PointSymbolsDoNotSuppressLabels`, S-421/S-124)
  — a structural, pixel-free assertion: it reads the real per-product overlay
  scene back from the tiled renderer (`S100VectorTileRenderer.TryGetPartitionedScene`)
  and decluttered both with and without the point symbols, asserting the symbols
  never change which labels survive. This catches the dropped-label class of bug
  (the S-421 route labels anchored on waypoint circles) that the coarse
  perceptual gate cannot see.

The structural tier runs with `DisplayCategory = null` ("All") so no label is
hidden by the Standard filter, and derives a viewport that encloses every
overlay anchor so the comparison is never vacuous. It is the strong signal placed
exactly where the labels+symbols declutter risk lives.

## In-viewer Metal A/B capture recipe

For the rotation / GPU / multi-product cases above, drive the Avalonia viewer
headlessly (per the `viewer-evaluation` skill / `docs/mcp-server.md`). The
`S100_RENDER_SUBSYSTEM` env var selects the arm (`mapsui` = "A",
`tiledscene` = "B")) and overrides the in-app flag.

Capture of the same cell + viewport through each arm. The CLI presets the
initial view (`--bbox` / `--palette` / `--display-category`) and enables the
MCP server; the capture itself is an MCP call — there is no one-shot
`--screenshot` CLI flag (it was removed in favour of the MCP tools):

```bash
VIEW=src/EncDotNet.S100.Viewer/bin/Release/net10.0/<rid>/EncDotNet.S100.Viewer
CELL=tests/datasets/S101/S-101/DATASET_FILES/101AA0000DS0009.000
mkdir -p /tmp/eval

for arm in mapsui tiledscene; do
  S100_RENDER_SUBSYSTEM=$arm "$VIEW" \
    --ephemeral --mcp --mcp-port-file /tmp/eval/mcp_$arm.url \
    --bbox -32.466667,61.5,-32.4417611,61.6145761 \
    --palette Day --display-category Standard \
    "$CELL" &
  PID=$!
  # Poll /tmp/eval/mcp_$arm.url for the endpoint, connect an MCP client, then:
  #   await_render_idle  →  render_to_image (save /tmp/eval/committed_$arm.png)
  kill -9 "$PID"   # once the capture is written
done
```

`--bbox` is `south,west,north,east`. For rotation, set a rotated viewport via
`set_viewport` before `render_to_image`; the headless harness cannot (rotated
"B" yields a blank base plane by design). The viewer ignores SIGTERM — stop it
with `kill -9 <pid>`. See the `viewer-evaluation` skill / `docs/mcp-server.md`
for the full capture loop. **Never commit** captured images, traces, or real
ENC datasets.
