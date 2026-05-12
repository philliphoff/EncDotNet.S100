# Viewer branding assets

This folder contains the master art for the S-100 Viewer application and
all generated icon / installer assets that the build and CI pipeline
consume.

## Source files (edit these)

| File                  | Purpose                                                                 |
| --------------------- | ----------------------------------------------------------------------- |
| `icon.svg`            | Master 1024×1024 application icon. Geometric ENC-inspired chart fragment. |
| `dmg-background.svg`  | Master 540×380 background image for the macOS `.dmg` installer window. |
| `Generate.sh`         | Regenerates every binary artifact below from the two source SVGs.       |

## Generated files (committed; do not hand-edit)

| File                       | Used by                                                                 |
| -------------------------- | ----------------------------------------------------------------------- |
| `AppIcon.png` (1024)       | Avalonia `Window.Icon` via `avares://`. Also serves as a 1× source for Linux. |
| `AppIcon.icns`             | macOS `.app` bundle (`Contents/Resources/AppIcon.icns`, referenced by `Info.plist`'s `CFBundleIconFile`). |
| `AppIcon.ico`              | Windows executable icon (`<ApplicationIcon>` in the viewer csproj). Multi-resolution: 16/32/48/64/128/256. |
| `dmg-background.png`       | 540×380 background for the macOS installer DMG.                         |
| `dmg-background@2x.png`    | 1080×760 retina background (kept alongside; ready if/when we move to UDIF retina mode). |
| `png/AppIcon-NN.png`       | Per-resolution PNGs at 16/32/48/64/128/256/512/1024 for Linux icon themes, About dialog reuse, README previews. |

## How it is wired in

- **Avalonia window icon** — `src/EncDotNet.S100.Viewer/EncDotNet.S100.Viewer.csproj` declares `Branding/AppIcon.png` as an `AvaloniaResource`, and `MainWindow.axaml` sets `Icon="avares://EncDotNet.S100.Viewer/Branding/AppIcon.png"`.
- **Windows .exe icon** — the same csproj sets `<ApplicationIcon>Branding\AppIcon.ico</ApplicationIcon>`, so `dotnet publish --runtime win-…` embeds the icon into the executable resource.
- **macOS .app icon** — `Info.plist` sets `CFBundleIconFile=AppIcon`. The CI build copies `Branding/AppIcon.icns` into `EncDotNet.S100.Viewer.app/Contents/Resources/`.
- **macOS DMG** — the CI workflow builds the DMG read-write, stages `Branding/dmg-background.png` into a hidden `.background/` folder, then uses AppleScript to set the window bounds, icon size, icon positions, and background image before converting to compressed UDZO.

## Regenerating

Re-run after editing either SVG. macOS only (uses `sips` + `iconutil`).
Pillow is required for the `.ico` packer:

```bash
python3 -m pip install --user Pillow
./Generate.sh
```

The script overwrites `AppIcon.png`, `AppIcon.icns`, `AppIcon.ico`, the
DMG background PNGs, and the `png/` directory. Commit the changes
alongside any source SVG edits.

## Design notes

The icon uses a geometric, faceted treatment of the standard S-100
chart palette:

- buff land (`#e7cf8e`) with a desaturated gold coastline,
- stepped depth bands from shallow (`#c9deec`) to deepest (`#5d9bd3`),
- two layers of simplified contour lines, and
- a `0b3a67` navy "frame" that doubles as the icon's rounded-rect mask
  (the bezel is part of the artwork, not an OS-applied mask).

Land occupies the upper-left and the open chart extends toward the
lower-right, mirroring how the viewer naturally renders ENC tiles.
