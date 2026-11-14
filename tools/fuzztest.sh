#!/usr/bin/env bash
# Runs the headless fuzz test (a clumsy, hostile player plus invariant checks after every game day) on the built
# player for several seeds and prints each verdict and its violation summary. Reports: runs/fuzz-<seed>/fuzz-report.md.
# Usage (Git Bash, from anywhere): tools/fuzztest.sh [years] [seeds...]     e.g. tools/fuzztest.sh 12 1 2 3
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
YEARS=${1:-12}; shift
SEEDS=${@:-1 2 3 4 5 6}
cp "$ROOT"/Assets/StreamingAssets/Data/*.json "$ROOT"/Build/Firstlight_Data/StreamingAssets/Data/
for s in $SEEDS; do
  OUT="$ROOT/runs/fuzz-$s"
  mkdir -p "$OUT"
  "$ROOT"/Build/Firstlight.exe -batchmode -nographics -fuzztest -seed $s -years $YEARS -outdir "$OUT" -logFile "$OUT/player.log" > /dev/null 2>&1
  echo "== seed $s exit $?: $(grep -m1 '^Asked' "$OUT/fuzz-report.md")"
  sed -n '/^## Violations/,/^## Notes/p' "$OUT/fuzz-report.md" | grep '^- [a-zA-Z /]*: [0-9]*$' | sed 's/^/   /'
  grep -m1 '\[Fuzz\]' "$OUT/player.log" | sed 's/.*\[Fuzz\]/  /'
done
