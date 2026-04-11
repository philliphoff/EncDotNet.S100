# EncDotNet.S100.Renderers.Skia

Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps.

## Overview

This library renders S-100 coverage and vector data to SkiaSharp bitmaps. It handles pure rasterization without map projection. Key types include:

- **`SkiaCoverageRenderer`** — `ICoverageRenderer<SKBitmap>` implementation that maps coverage grid cells to pixel colors.
- **`SkiaSvgRasterizer`** — rasterizes SVG portrayal symbols to tiled pattern bitmaps.
- **`SkiaColorExtensions`** — helpers for converting between `RgbaColor` and `SKColor`.

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Skia
```
