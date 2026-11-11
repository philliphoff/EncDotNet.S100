# Embedded render fonts

This folder holds `OpenSans-Regular.ttf`, which `EncDotNet.S100.Renderers.Skia`
embeds as a resource. `SkiaDisplayListRenderer` uses it to draw text labels when
the host has no usable system font. You don't need to reference or configure it.

## When the fallback font is used

The renderer draws labels with `SKTypeface.Default`. If that typeface has no
glyphs, the renderer uses the embedded Open Sans face instead.

This happens on Linux when you ship the self-contained
`SkiaSharp.NativeAssets.Linux.NoDependencies` native library on a machine
without `fontconfig` and a font package. That native library doesn't load
`fontconfig`, so `SKTypeface.Default` resolves to an empty typeface and labels
would otherwise render blank. For when to use that native library, see
[Linux arm64 native dependency](../../README.md#linux-arm64-native-dependency).

When the host does expose system fonts, as desktops and CI runners with
`fontconfig` do, the renderer uses `SKTypeface.Default` unchanged. Visual
regression baselines rendered on those hosts don't change.

## Licence

Open Sans is licensed under the Apache License, Version 2.0 (Copyright
2010-2011, Google Inc.; designed by Steve Matteson). The full licence text and
attribution are in
[`LICENSE-OpenSans.txt`](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Renderers.Skia/Assets/Fonts/LICENSE-OpenSans.txt),
and the font is listed in the repository's
[third-party notices](../../../../THIRD-PARTY-NOTICES.md).

It's the same Open Sans face that the official S-100 portrayal catalogues
already redistribute, at
`EncDotNet.S100.Specifications/content/**/pc/Fonts/OpenSans-Regular.ttf`.
