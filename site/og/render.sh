#!/usr/bin/env bash
# Renders og/card.html to public/og-card.png (the Open Graph card) with
# headless Chrome. Usage: og/render.sh [shot-id] [output]  (default H1).
# Run from site/ after `npm ci`, which provides the fonts.
set -euo pipefail
cd "$(dirname "$0")/.."

shot="${1:-H1}"
out="${2:-public/og-card.png}"
chrome="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"

"$chrome" --headless --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --allow-file-access-from-files --virtual-time-budget=5000 \
  --window-size=1200,630 --screenshot="$PWD/$out" \
  "file://$PWD/og/card.html?shot=$shot" 2>/dev/null
echo "Wrote $out ($shot)"
