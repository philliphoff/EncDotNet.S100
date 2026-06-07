# Embedded render fonts

`OpenSans-Regular.ttf` is bundled as an embedded resource and used by
`SkiaDisplayListRenderer` as a deterministic text fallback when the host provides
no usable system font (issue #23).

This matters on Linux when the self-contained `SkiaSharp.NativeAssets.Linux.NoDependencies`
native library is used: that build does not pull in `fontconfig`, so on a system
without `fontconfig` (and a font package) installed, `SKTypeface.Default` resolves
to an empty typeface and labels would otherwise render blank. The embedded font
guarantees the headless render path produces real text without any system font
infrastructure.

When the host *does* expose system fonts (every desktop, and CI runners with
`fontconfig`), `SKTypeface.Default` is used unchanged, so existing visual-regression
baselines are unaffected.

## Licence

Open Sans is licensed under the Apache License, Version 2.0. The font is the same
asset already redistributed under `EncDotNet.S100.Specifications/content/**/pc/Fonts/`
as part of the official S-100 portrayal catalogues.
