# Batch 2 (hitches) checkpoint

Worktree `Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2` (from f64fc82). Numbers: `perf-hitches.md`.

## Status: done, gate passed on the final build
- [x] P1 495d9d8: commit + prefetch together (Settlement.CommitWalkCosts), walkers' misses batched per step
  (PrefetchWalkers), doors prefetched after a load, water flips committed by the hourly step while the sim steps, deer
  field misses batched
- [x] P2 a111707: water mesh rebuilt only when the water stepped, over a mask of cells that can be wet (grows if needed)
- [x] P4 df947d5: autosave 1 s after the season start, never in a day-tick frame
- [x] P6 d4884d2: command line read once
- [x] P8 0e726f6: title menu caches SaveGame.Latest()
- [x] P7 d30bbf3: deer threat test per herd circle first
- [x] P9 fd906b9: splash off (accepted by the batch build, setting survived)
- [x] e47e031: probe flood wait by real time; water mask size logged
- [x] before/after numbers (perf-hitches.md)
- [x] gate

## Lines changed in existing Settlement*.cs files
Only `Assets/Scripts/Game/Sim/Settlement.cs`, `StepOnce`: one added line `PrefetchWalkers();` right before
`bool work = IsWorkTime;`. Everything else is in the new `Settlement.Paths.cs` (+ `.meta`, hand-written guid).

## Gate (final build = e47e031 tree; `build-b2`: Succeeded, 0 errors)
1. `tools/gate-autotest.sh b2`: 15/15 exit 0, errs 0; `diff summary.txt` vs baseline empty; 30/30 report files
   `cmp`-identical (run twice: on the first after-build and again on the final build).
2. `-saveloadcheck` seeds 2026 and 7: IDENTICAL, run A PASS, run B PASS, exit 0.
3. `tools/fuzztest.sh 15 1 2 3 4 5 6`: 0 violations on every seed (same outcomes as the main tree's own runs: seeds 2,
   3, 5, 6 die out under the hostile player).
4. `-autoplay -simspeed 4 -quitafter 120`: exit 0, 0 exceptions. `-popupwindow -smoketest` then `-smokecontinue`:
   saved, continued, digest MATCH, 0 exceptions. Flood probe (B): no frame over 33 ms in the sim flood.
   Autosave: exactly one per season crossing in the A runs (day 456, 1 s after the season start). The A runs wrote
   autosave-2/-3 in the real save folder; both were restored from `runs/pass2/perf/savebackup/` afterwards (twice).

## Log
- Before build = f64fc82 + probe-only edits (`-perfautosave`; commit after path toggles like the real commands).
- Finding: the autosave was not in the season-start frame but the very next one (Autosave() ran before Sim.Update):
  38.7 ms then 85.4 ms back to back.
- First after flood run was invalid (probe waited 2000 frames for the flood; the faster build ran them in 3 s): fixed
  the probe (e47e031), rebuilt, reran B1/B2.
