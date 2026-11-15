#!/usr/bin/env bash
# Pass-2 gate: the 15 standard seeds, 30 years each, several at a time. Reports: runs/pass2/<label>/at-<seed>/.
# Usage (Git Bash): tools/gate-autotest.sh <label> [parallel=5]
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LABEL=${1:-gate}; PAR=${2:-5}
SEEDS="2026 7 1234 99 5 11 42 3 100 777 13 21 555 8080 31337"
cp "$ROOT"/Assets/StreamingAssets/Data/*.json "$ROOT"/Build/Firstlight_Data/StreamingAssets/Data/
mkdir -p "$ROOT/runs/pass2/$LABEL"
run() { s=$1; OUT="$ROOT/runs/pass2/$LABEL/at-$s"; mkdir -p "$OUT"
  "$ROOT"/Build/Firstlight.exe -batchmode -nographics -autotest -seed $s -years 30 -outdir "$OUT" -logFile "$OUT/player.log" >/dev/null 2>&1
  echo "seed $s exit $? | $(grep -E '^(Reached|Did not)' "$OUT/autotest-report.md" | tr '\n' ' ') | $(grep -m1 '^Births' "$OUT/autotest-report.md") | errs $(grep -c 'Exception\|Error' "$OUT/player.log")"; }
export -f run; export ROOT LABEL
echo $SEEDS | tr ' ' '\n' | xargs -P $PAR -I{} bash -c 'run {}' | sort > "$ROOT/runs/pass2/$LABEL/summary.txt"
cat "$ROOT/runs/pass2/$LABEL/summary.txt"
