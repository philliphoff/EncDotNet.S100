# Scenario: Compose S-101 ENC + S-102 bathymetry

## Why it matters

Compositing is where cross-product value appears: chart context plus bathymetric detail.

## Quick win

```bash
s100 render --layer enc.000 --layer bathy.h5 chart.png
```

Expected result: one image with interoperable layering.

## Deep dive

1. Render with explicit viewport:
   ```bash
   s100 render --layer enc.000 --layer bathy.h5 chart.png --bbox -1.5,50.0,-1.0,50.5
   ```
2. Use the library compositor:
   ```csharp
   byte[] png = await renderer.RenderAsync(
       new[]
       {
           new S100Layer { Dataset = enc },
           new S100Layer { Dataset = bathy }
       },
       new S100CompositeOptions { Width = 2048, Height = 1536 });
   ```

## Troubleshooting

> [!NOTE]
> Layer order is resolved by the S-98 interoperability authority; CLI `--layer` order is mainly a tiebreak within a display plane.

## Next step

- [S-98 interoperability design note](../design/s98-interoperability.md)
- [Embedding the renderer](../embedding-the-renderer.md)
