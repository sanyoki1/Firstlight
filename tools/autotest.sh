#!/usr/bin/env bash
# Runs the headless auto-test on the built player for several seeds and prints the headline lines.
# Copies the data JSON into the build first, so balance edits can be tried without rebuilding.
# Usage (Git Bash, from anywhere): tools/autotest.sh [years] [seeds...]     e.g. tools/autotest.sh 12 2026 7 1234
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
YEARS=${1:-12}; shift
SEEDS=${@:-2026}
cp "$ROOT"/Assets/StreamingAssets/Data/*.json "$ROOT"/Build/Firstlight_Data/StreamingAssets/Data/
for s in $SEEDS; do
  OUT="$ROOT/runs/autotest-$s"
  mkdir -p "$OUT"
  "$ROOT"/Build/Firstlight.exe -batchmode -nographics -autotest -seed $s -years $YEARS -outdir "$OUT" -logFile "$OUT/player.log" > /dev/null 2>&1
  echo "== seed $s exit $?: $(grep -m1 '^Ran' "$OUT/autotest-report.md")"
  grep -m1 '^Births' "$OUT/autotest-report.md"
  grep -m1 -E '^(Reached|Did not)' "$OUT/autotest-report.md"
  echo "errors/exceptions in player log: $(grep -c 'Exception\|Error' "$OUT/player.log")"
done
