# Scenario: Render S-102 bathymetry to PNG

## Why it matters

This is the fastest path to a visual output from an HDF5 coverage product.

## Quick win

```bash
s100 render bathy.h5 bathy.png --palette day -w 1600 -h 1200
```

Expected result: `bathy.png` with depth colouring.

## Deep dive

1. Inspect the file first:
   ```bash
   s100 info bathy.h5
   ```
2. Render with alternate palette:
   ```bash
   s100 render bathy.h5 bathy-night.png --palette night
   ```
3. Render through the .NET facade for integration:
   ```csharp
   using var dataset = S100Dataset.Open("bathy.h5");
   using var renderer = new PngS100DatasetRenderer();
   byte[] png = await renderer.RenderAsync(dataset);
   File.WriteAllBytes("bathy.png", png);
   ```

## Troubleshooting

> [!WARNING]
> Some coverage formats are not headless-renderable. Check `s100 info` and `CanRenderHeadless` before automation.

## Next step

- [Compose S-101 + S-102](compose-s101-s102.md)
- [Command-line rendering](../cli.md)
