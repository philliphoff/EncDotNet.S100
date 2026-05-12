#!/usr/bin/env bash
# Regenerate all branding asset binaries from icon.svg and dmg-background.svg.
#
# Requires macOS (uses sips + iconutil) and Python 3 with Pillow for the
# Windows .ico packer. Re-run this script any time the source SVGs change
# and commit the produced PNG / ICNS / ICO / PNG-set files alongside.
#
# Usage:
#   ./Generate.sh
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v iconutil >/dev/null 2>&1; then
  echo "error: iconutil not found (this script must run on macOS)" >&2
  exit 1
fi
if ! command -v sips >/dev/null 2>&1; then
  echo "error: sips not found (this script must run on macOS)" >&2
  exit 1
fi

PYTHON="${PYTHON:-python3}"
if ! "$PYTHON" -c "import PIL" >/dev/null 2>&1; then
  echo "error: Python 3 with Pillow is required for .ico generation" >&2
  echo "       install with: $PYTHON -m pip install --user Pillow" >&2
  exit 1
fi

# -----------------------------------------------------------------------------
# 1x master PNG (used as Avalonia window icon and as a Linux 1024 source)
# -----------------------------------------------------------------------------
echo "==> Rendering AppIcon.png (1024)"
sips -s format png -z 1024 1024 icon.svg --out AppIcon.png >/dev/null

# -----------------------------------------------------------------------------
# macOS .icns bundle
#
# Apple's iconset layout — each pair below is one logical size at 1x and 2x:
#   icon_16x16     icon_16x16@2x      (16, 32)
#   icon_32x32     icon_32x32@2x      (32, 64)
#   icon_128x128   icon_128x128@2x    (128, 256)
#   icon_256x256   icon_256x256@2x    (256, 512)
#   icon_512x512   icon_512x512@2x    (512, 1024)
# -----------------------------------------------------------------------------
echo "==> Building AppIcon.iconset"
rm -rf AppIcon.iconset
mkdir AppIcon.iconset
render_iconset_entry() {
  local size="$1" name="$2"
  sips -s format png -z "$size" "$size" icon.svg \
    --out "AppIcon.iconset/${name}.png" >/dev/null
}
render_iconset_entry  16  icon_16x16
render_iconset_entry  32  icon_16x16@2x
render_iconset_entry  32  icon_32x32
render_iconset_entry  64  icon_32x32@2x
render_iconset_entry 128  icon_128x128
render_iconset_entry 256  icon_128x128@2x
render_iconset_entry 256  icon_256x256
render_iconset_entry 512  icon_256x256@2x
render_iconset_entry 512  icon_512x512
render_iconset_entry 1024 icon_512x512@2x

echo "==> Compiling AppIcon.icns"
iconutil -c icns AppIcon.iconset -o AppIcon.icns

# -----------------------------------------------------------------------------
# Per-resolution PNGs (Linux icon themes, About dialog, README previews)
# -----------------------------------------------------------------------------
echo "==> Rendering Linux PNG set into png/"
mkdir -p png
for size in 16 32 48 64 128 256 512 1024; do
  sips -s format png -z "$size" "$size" icon.svg \
    --out "png/AppIcon-${size}.png" >/dev/null
done

# -----------------------------------------------------------------------------
# Windows .ico (multi-resolution, packs 16/32/48/64/128/256)
# -----------------------------------------------------------------------------
echo "==> Building AppIcon.ico"
"$PYTHON" - <<'PY'
from PIL import Image
sizes = [(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)]
master = Image.open("png/AppIcon-256.png").convert("RGBA")
master.save("AppIcon.ico", format="ICO", sizes=sizes)
PY

# -----------------------------------------------------------------------------
# DMG background (1x and 2x)
# -----------------------------------------------------------------------------
echo "==> Rendering DMG background"
sips -s format png -z 380 540 dmg-background.svg \
  --out dmg-background.png >/dev/null
sips -s format png -z 760 1080 dmg-background.svg \
  --out dmg-background@2x.png >/dev/null

echo "==> Done. Generated artifacts:"
ls -la AppIcon.png AppIcon.icns AppIcon.ico dmg-background.png dmg-background@2x.png
echo "Linux PNGs:"
ls png/
