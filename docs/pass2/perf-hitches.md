# Batch 2 (hitches): before and after

Branch `pass2-lane2`. Before = f64fc82 plus two probe-only edits (`-perfautosave` keeps autosaves on so a season start
can be timed with its save; the path-toggle scenario commits after `SetPath` as the real path commands do). After = the
six fix commits plus the splash setting (fd906b9), and the probe's flood wait measured in real time instead of 2000
frames (the after build runs 3x more frames a second, so 2000 frames ended before the flood started). Same machine,
same save (seed 7, day 452, 152 people), same commands, two runs each; another agent was using the CPU at the same
time. Batch mode renders every frame into a 1920x1080 texture. Logs: `runs/pass2/perf/{before,after}/<run>/player.log`
(git-ignored), summarised with `python runs/pass2/perf/summ.py <dirs>`.

Commands (from `runs/pass2/perf`, `W=$(pwd -W)`, `P=../../../Build/Firstlight.exe`; also in `runs/pass2/perf/scen.sh`):
- A: `$P -batchmode -screen-width 1920 -screen-height 1080 -load "$W/save7/seed7-day452.flsave" -perfprobe -perfspeeds 1,4 -perfsecs 60 -perfplace -perfautosave -outdir ... -logFile ...`
  (town 1x = the first minute after the load; town 4x crosses the day-456 season start with its autosave; then a path
  tile laid/lifted every 5 s at 1x and 4x)
- B: same load, `-perfprobe -perfnobase -perfflood` (sim flood at max strength, 4x; then the F6 surge, 85 s at 1x)
- S: `$P -batchmode -screen-width 1920 -screen-height 1080 -perfprobe -perfstartup 2026 -perfsecs 15` (title menu)
- W: the same with `-popupwindow` instead of `-batchmode` (windowed, unthrottled at 140-1000+ fps: real OnGUI numbers)
- C (after only): same load, `-perfprobe -perfnobase -perfspeeds 1 -perfcrowds 550,2000 -perfsecs 40`

Frame times are wall time Update to Update. "run1 / run2".

| Scenario | Before | After | Fix |
|---|---|---|---|
| Steady frame, 152 people, batch 1x (A) | avg 5.48 / 5.09 ms | avg 1.82 / 1.70 ms | P2, P6, P7 |
| `WaterMesh` per frame (A 1x) | avg 3.19 / 3.06 ms, max 4.0 / 3.9 | avg 0.024 / 0.021 ms; a rebuild (20 a second) costs about 0.6-0.95 ms | P2 |
| `Wild` (deer) per frame (A 1x) | avg 0.39 / 0.37 ms | avg 0.080 / 0.077 ms | P1 (deer prefetch), P7 |
| GC, batch (A 1x, 18-arg command line) | 92 / 99 per minute | 11 / 11 per minute | P6 |
| Load, then the first minute at 1x (A) | max 74.6 / 65.8 ms; >33 ms: 3 / 3; 180 fields in 180 single builds (274 / 245 ms) | max 21.9 / 22.1 ms; >33 ms: 0 / 0; 192 fields in 43 batches (86 / 82 ms) | P1 (c), (b) |
| Season start at 4x, day-tick frame (A) | 38.7 / 36.4 ms (136-138-field prefetch 28.6 ms + daily) | 25.9 / 27.0 ms | P1 (parallel batches) |
| Season start, autosave frame (A) | the very next frame: 85.4 / 77.8 ms (Autosave 79.9 / 72.1 incl. a GC) | 1 s later (02.4h at 4x), alone: 87.5 / 96.0 ms (Autosave 85.3 / 93.9) | P4 moves it; the save itself is batch 1's |
| Path tile toggled every 5 s, 1x (A) | max 65.9 / 59.7 ms; >33 ms: 7 / 7; >50: 6 / 3; 317 fields in 317 builds | max 22.8 / 21.6 ms; >33 ms: 0 / 0; 761 / 766 fields in 30 / 28 batches | P1 |
| Path tile toggled every 5 s, 4x (A) | max **132.5 / 120.5** ms (shift start 06:30); >33 ms: 11 / 10; >50: 9 / 9 | max **26.6 / 24.8** ms; >33 ms: 0 / 0 | P1 |
| Sim flood, max strength, 4x (B) | max **93.4 / 101.4** ms; >33 ms: 14 / 16; >50: 7 / 9; 1131 / 1145 single builds (948 / 1081 ms) | max **23.2 / 22.7** ms; >33 ms: 0 / 0; 2923 / 2931 fields in 108 / 110 batches (303 / 311 ms) | P1 (d) |
| F6 surge, 85 s at 1x (B) | max 72.2 / 83.3 ms; >33 ms: **48 / 52**; >50: 24 / 28; 4133 / 4161 fields in 4005 / 4033 builds (3.1 / 3.4 s) | max 17.8 / 80.3* ms; >33 ms: **0 / 4***; >50: 0 / 2*; 6904 / 6856 fields in 246 / 234 batches (0.65 / 0.70 s) | P1 (d) |
| Title menu, batch (S) | avg 4.04 ms (WaterMesh 3.22) | avg 0.71 ms (WaterMesh 0.009) | P2 |
| Title menu, windowed (W) | avg 7.17 / 7.24 ms; OnGUI **3.35 / 3.39** ms; 916 / 904 GC a minute | avg 0.94 / 0.90 ms; OnGUI **0.110 / 0.097** ms; 500 / 512 GC a minute | P8 (+P2) |
| Start to first title-menu frame, windowed (W) | 3613 / 3525 ms after engine start | 1081 / 1011 ms | P9 |

\* B2's F6 run had a burst of four frames at 19.2-19.4h with no flow-field build, no GC and no day boundary, spread
over `Render` (46.6 ms), `SettlementView` (23.2) and `WaterMesh` (24-26, normally under 1): the same section took
20-40x its usual time in one frame, which is the OS-preemption pattern the audit describes (the other agent's player
was running). B1's F6 run, and every other phase, has none.

Crowds after the fix (C1, one run, 1x, 40 s each; `Wild` avg per frame): **0.125 ms at 550 people, 0.367 ms at 2000**.
No before run was made in this session for crowds; the audit's numbers (same command, another session) were 1.2-1.3 ms
at 550 and 4.2-4.6 ms at 2000. At 152 people the same-session comparison is 0.38 -> 0.08 ms (table above).

## Notes
- More fields are built in total after the fix (the doors are rebuilt in one batch whenever the paths are dropped,
  whether or not anyone walks to each door that hour), but in a few dozen parallel batches instead of hundreds of
  single builds, so the time spent is 3-5x less and no frame pays for more than one batch.
- The worst regular frames left are about 20-27 ms: one parallel batch of 130-180 fields (a path change, an hourly
  commit during a flood, a season start). Making that cheaper is the deferred bucket-queue or a double-buffered
  asynchronous build.
- The autosave frame is still 85-95 ms: the save itself (batch 1, P5) is untouched here; it now stands alone a second
  after the season start instead of right after the season start's own hitch.
- The water surface mask covered 2578-2626 of 16384 cells on these maps and never had to grow, through the max-strength
  sim flood and the F6 surge (`[Water] surface mask` log line; it logs again if the depth scan ever grows it).
  A rebuild costs 0.6-0.95 ms (about 0.2 ms a frame at 60 fps); the optional Burst job was not done.
