# SoundCharts branding assets

This folder holds the master art for the SoundCharts icon and macOS installer,
and the icon and installer files generated from it. The build, the CI release
workflow and the documentation site use these files.

## Source files

Edit these files, then [regenerate the assets](#regenerate-the-assets).

| File | Purpose |
|---|---|
| `icon.svg` | Master application icon, 1024×1024. |
| `dmg-background.svg` | Master background for the macOS `.dmg` installer window, 540×380. |
| `Generate.sh` | Script that regenerates every generated file below from the two SVGs. |

## Generated files

These files are committed. Don't edit them by hand.

| File | Used by |
|---|---|
| `AppIcon.png` (1024×1024) | The Avalonia window icon. Also the 1024 px source for Linux. |
| `AppIcon.icns` | The macOS `.app` bundle icon and the DMG volume icon. |
| `AppIcon.ico` | The Windows executable icon. Contains 16, 32, 48, 64, 128 and 256 px images. |
| `dmg-background.png` | The macOS installer DMG background, 540×380. |
| `dmg-background@2x.png` | A 1080×760 Retina version of the DMG background. The CI workflow doesn't use it yet. |
| `png/AppIcon-<size>.png` | Single-size PNGs at 16, 32, 48, 64, 128, 256, 512 and 1024 px, for Linux icon themes and other reuse. |

## How the build uses the assets

- **Window icon**: `EncDotNet.S100.Viewer.csproj` includes
  `Branding/AppIcon.png` as an `AvaloniaResource`, and `MainWindow.axaml`
  sets `Icon="avares://SoundCharts/Branding/AppIcon.png"`.
- **Windows executable icon**: the same project file sets
  `<ApplicationIcon>Branding\AppIcon.ico</ApplicationIcon>`, so
  `dotnet publish` for a `win-*` runtime embeds the icon in the executable.
- **macOS app icon**: `Info.plist` sets `CFBundleIconFile` to `AppIcon`, and
  the CI workflow copies `AppIcon.icns` into
  `SoundCharts.app/Contents/Resources/`.
- **macOS DMG**: the CI workflow builds the DMG with
  [create-dmg](https://github.com/create-dmg/create-dmg), using
  `dmg-background.png` as the window background and `AppIcon.icns` as the
  volume icon. The window size and icon positions in
  [`ci.yml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/.github/workflows/ci.yml)
  must match the layout drawn in `dmg-background.svg`.
- **Documentation site**: `docfx.json` uses `icon.svg` as the site logo and
  favicon.

## Regenerate the assets

Run the script after you edit either SVG. It needs macOS, because it uses
`sips` and `iconutil`, and Python 3 with Pillow to write the `.ico` file.

```bash
python3 -m pip install --user Pillow
./Generate.sh
```

The script overwrites `AppIcon.png`, `AppIcon.icns`, `AppIcon.ico`, both DMG
background PNGs and the `png/` folder. Commit the regenerated files with the
SVG change.

## Icon design

The icon is a geometric, faceted treatment of the S-100 chart palette:

- Buff land (`#e7cf8e`) with a desaturated gold coastline.
- Stepped depth bands, from shallow (`#c9deec`) to deepest (`#5d9bd3`).
- Two layers of simplified contour lines.
- A navy (`#0b3a67`) frame that is also the icon's rounded-rectangle mask.
  The bezel is part of the artwork; the operating system doesn't apply it.

Land fills the upper left, and open water extends toward the lower right, the
way a chart of a coastline usually appears in SoundCharts.
