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
| `MaxDifferentPixelFraction` | 0.001 | Max fraction (0.1 %) of pixels that may differ |

These are tuned to absorb minor anti-aliasing / font-hinting drift between
platforms while still catching real rendering regressions. Adjust per-test
via `PerceptualImageComparer` constructor parameters or by setting
`HarnessOptions` to a smaller, less-text-heavy viewport.

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
