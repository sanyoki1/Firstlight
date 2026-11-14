#!/usr/bin/env bash
# Runs the -uishots screenshot pass on the built player (batch mode still renders offscreen at ~125 fps).
# Copies the data JSON into the build first. Usage (Git Bash): tools/uishots.sh [outdir] [seed] [extra args...]
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/runs/uishots}"; SEED="${2:-2026}"; shift 2 2>/dev/null
mkdir -p "$OUT"; rm -f "$OUT"/*.png
cp "$ROOT"/Assets/StreamingAssets/Data/*.json "$ROOT"/Build/Firstlight_Data/StreamingAssets/Data/
OUTW="$(cd "$OUT" && pwd -W)"
"$ROOT"/Build/Firstlight.exe -batchmode -screen-width 1920 -screen-height 1080 -uishots -seed "$SEED" -outdir "$OUTW" -logFile "$OUTW/player.log" "$@"
echo "exit $?"
grep -E "\[UiShots\]|\[Perf\]" "$OUT/player.log"
echo "exceptions/errors: $(grep -cE 'Exception|Error' "$OUT/player.log")"
