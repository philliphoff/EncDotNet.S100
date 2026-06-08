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

Open Sans is licensed under the Apache License, Version 2.0 (Copyright 2010-2011,
Google Inc.; designed by Steve Matteson). The full licence text and attribution
are in [`LICENSE-OpenSans.txt`](LICENSE-OpenSans.txt) in this folder, and the asset
is listed in the repository's root [`THIRD-PARTY-NOTICES.md`](../../../../THIRD-PARTY-NOTICES.md).

This is the same Open Sans face already redistributed under the official S-100
portrayal catalogues at
`EncDotNet.S100.Specifications/content/**/pc/Fonts/OpenSans-Regular.ttf`.
