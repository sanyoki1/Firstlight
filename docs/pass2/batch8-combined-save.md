# Pass 2 batch 8: combined-branch gate + saving off the main thread (checkpoint)

Started 2026-11-15 on `experiment/quarter-pass2` at cfc7315 (batches 1, 2, 3, 4a and 6 combined by a rebase).
Raw outputs: `runs/pass2/b8-0/`, `runs/pass2/b8/` (git-ignored).

## Status
- [x] Job A 1: build `build-b8-0` (runs/build-b8-0.log): compile check 0 errors, Unity "Build Finished, Result:
  Success", 0 `error CS`. (A player from `runs/pass2/build-audit/` was running, not this folder's Build/.)
- [x] Job A 2: gate `b8-0`
- [x] Job A 3: identity checks, all on the `build-b8-0` player (results below)
- [x] Job A 4: harvest-outlook table
- [x] Job B measure: d6925db (hooks), before runs `runs/pass2/b8/perf/before/{r4q,r1}-{1,2,3}` on the probe build
  `runs/pass2/perf/build-probe` (= d6925db). Finding: on the combined tree the save frame is 34-44 ms, not 85-95
  (the 85-95 came from lane 2's tree, which did not have batch 1's buffered writer). ~10 collections a save:
  map 6-7 (a 128 KB offset copy, the walk-cost ToArray, the 256 KB body stream growing twice), settlement 2, water 1.
- [x] Job B async: 8b0b950 (serialize in the frame, compress/write/replace on a worker; waits everywhere)
- [x] Job B garbage: reused body/header buffers, bulk array writes, no copies (build-b8-1). Savecheck on it: PASS,
  new background-save check PASS, collections per save (headless) 12-15 -> 0.4-0.8. Old-writer build (d6925db) vs
  new: every save the savecheck writes (good, damaged cases, unknown ids, the four compat re-saves) has the same
  header (save time aside) and a byte-identical gzip body (`ClaudeWorkspace/b8tools/bodycmp.py`, `runs/pass2/b8/sc-old`
  vs `sc-new`).
- [x] Job B after numbers: `docs/pass2/perf.md` "Saving off the main thread". Autosave frame 38-40 ms -> 3.7-5.3 ms,
  quicksave 23-44 -> 2.2-4.2 ms (one 11.2), collections per save 9-10 -> 0-1, file in place 15-25 ms after the request.
- [x] Final gate on build-b8-3 (= d173cc0): passed (below)

## Real saves folder
Snapshot before any run: `runs/pass2/b8/realsaves-before.txt` (names, sizes, mtimes, md5 of the 7 files in
`%USERPROFILE%\AppData\LocalLow\Firstlight\Firstlight\saves`).

## Job A 2: 15-seed gate on the combined tree (`runs/pass2/b8-0`)
15/15 exit 0, errs 0, Bronze on all (years 11-15). `summary.txt` is byte-identical (`cmp`) to
`runs/pass2/b6-final/summary.txt`.

Per-seed reports vs `b6-final`: every one of the 30 files differs, and on every seed ONLY inside "## Warnings and bad
events" (md) and only the `warnings` list (json); every other section and JSON field is identical (script:
`ClaudeWorkspace/b8tools/secdiff.py`, section by section and JSON path by path). The bad events in that section
(deaths, disputes, ...) are unchanged; the differences are batch 4a's warning texts:
- every standing warning now ends with its remedy (": build more homes (Housing)", ": more gatherers, hunters or
  fishers now", ": set a woodcutter to Firewood", ": a well within reach of their homes brings it", ": a herbalist
  or healer with herbs treats them", "There is no food left: people go hungry, then leave or die. More ...");
- "N workers have no tools." (114 / 134 / 140 lines on seeds 2026 / 13 / 31337) is gone; it is replaced by
  "N workers have no tools and none are in store: build or staff a Toolmaker." only when the store is empty
  (6 / 8 / 7 lines). This is most of the drop in warning lines (2026: 361 -> 258; all seeds: 311-450 -> 202-314);
- "Storage is nearly full." (5 / 3 / 5) becomes "Storage is nearly full (N of N), most of it food: goods are waiting
  at workplaces ..." (3 / 1 / 3: fewer, as the new rule is narrower);
- "Unrest is rising (N %) ..." becomes "Unrest stands at N %: disputes start at 25 %, theft at 30 %. A staffed ..."
  (2026: 2 -> 2, 31337: 1 -> 1);
- new: the spring harvest-outlook warning "Expected harvest ~X from N fields (M on flax) and Y in store, against ~Z
  needed until autumn next year: sow more fields now." (2026: 7, 13: 4, 31337: 4).
Seeds compared line by line by kind: 2026, 13, 31337 (`ClaudeWorkspace/b8tools/warnkinds.py`); the section/JSON
check covers all 15.

## Job A 3: identity checks (build-b8-0)
- Determinism: seed 7 run again (`runs/pass2/b8/det-7`): report .md and .json byte-identical to the gate's at-7.
- Save/load identity `-autotest -saveloadcheck -years 30`, seeds 2026, 7, 13: all exit 0, "IDENTICAL reports; run A
  PASS, run B PASS".
- `-autotest -savecheck` with `-compatsave` on the four v1 saves (`runs/pass2/perf/save7/seed7-day452.flsave`,
  `runs/pass2/b1/v1saves/{s11,t2026,t42}/v1-*.flsave`, the only v1 saves found), `-compatdays 40`: exit 0, PASS
  (damaged-save cases, other map settings refused, overwrite/held file/lone backup, goods by id, unknown ids, and
  each v1 save: loads, plays 40 days, saved again as format 2, loads the same).
- Fuzz `tools/fuzztest.sh 15 1 2 3 4 5 6 101 102`: all exit 0, PASS, 0 violations (seeds 1 and 4 reach Bronze with
  47 / 28 save-load round trips; the hostile player kills 2, 3, 5, 6, 101, 102 as before).
- Batch `-seed 2026 -autoplay -simspeed 4 -quitafter 120` (`runs/pass2/b8/smoke`): exit 0, 0 exceptions, 0 errors.
- Windowed `-popupwindow -smoketest`, then `-smoketest -smokecontinue` (`timeout 900`, both with
  `-outdir runs/pass2/b8/smokewin`, so the save went to `smokewin/userdata/saves`): both exit 0, 0 exceptions,
  digest 38BEB6E6784E4EC2 at save and at load: MATCH.
- Real saves folder after all of Job A: names, sizes, mtimes and md5 identical to the snapshot.

## Job A 4: spring harvest-outlook warning with the batch 6 balance (gate `b8-0`)
"Expected harvest" lines per report (each is a spring the warning was raised); before = `b4a-final` (old balance,
from batch4a-guidance.md).

| Seed | Times now | Years (spring) | Bronze (year) | Before (b4a-final) |
|---|---|---|---|---|
| 2026 | 7 | 5, 6, 17, 18, 19, 20, 21 | 12 | 2 (5, 6) |
| 7 | 6 | 4, 11, 12, 13, 14, 15 | 11 | 3 (4, 19, 20) |
| 1234 | 0 | | 11 | 2 (14, 15) |
| 99 | 2 | 16, 17 | 15 | 1 (5) |
| 5 | 1 | 5 | 13 | 1 (5) |
| 11 | 2 | 5, 14 | 12 | 3 (12, 13, 14) |
| 42 | 1 | 6 | 14 | 0 |
| 3 | 1 | 4 | 11 | 1 (4) |
| 100 | 2 | 5, 13 | 13 | 1 (5) |
| 777 | 3 | 6, 23, 24 | 13 | 7 (5, 15-20) |
| 13 | 4 | 5, 12, 13, 14 | 12 | 3 (5, 14, 15) |
| 21 | 6 | 14-19 | 11 | 17 |
| 555 | 0 | | 12 | 0 |
| 8080 | 1 | 5 | 11 | 0 |
| 31337 | 4 | 6, 17, 18, 19 | 14 | 15 |
| total | 40 | | | 56 |

Seeds 21 and 31337 no longer warn almost every spring (17 -> 6, 15 -> 4); 2026 now warns in years 17-21 and 7 in
years 11-15. Not retuned (as asked).

## Job B commits
- d6925db measurement hooks ([SaveParts], [ProbeSave], -perfquick)
- 8b0b950 serialize in the frame, write on a worker; waits for the save in flight everywhere
- eeb785b garbage: reused buffers, bulk array writes, no copies (same bytes)
- 4900639 -savecheck: background save byte-identical to the in-call save, ordering, held-file error
- f3ea678 a background save failing just before going to the main menu shows on the title screen
- d173cc0 one-time warm-up so the first autosave's frame is as short as later ones

What waits for a save in flight (`SaveGame.WaitPending`): `Write`, `WriteInBackground` (so at most one in flight),
`Read`, `ReadHeader(path)`, `List` (and so `Latest`, the load/save screens, Continue, and the title menu's cache),
`NextAutosave`, `Application.quitting`; in GameController: `SaveTo`, `QuickLoad`, `Autosave`, and `Reload` (new game,
load, main menu). No HUD file was edited: the HUD's calls (`G.SaveTo`, `SaveGame.List/Latest`, `G.LoadGame`,
`G.QuitGame`) work unchanged; its save screen still saves in the call (the game is paused there).

## Final gate (build-b8-3, Build/, HEAD d173cc0)
- Build: 0 `error CS`, "[Build] Succeeded, 0 errors" (runs/build-b8-3.log); compile check 0 errors.
- `tools/gate-autotest.sh b8`: 15/15 exit 0, errs 0; `runs/pass2/b8/summary.txt` cmp-identical to
  `runs/pass2/b6-final/summary.txt`; all 30 report files (.md, .json) cmp-identical to `runs/pass2/b8-0`.
- Determinism: seed 7 again (`runs/pass2/b8/final/det-7`): both files identical to the gate's.
- `-saveloadcheck` 2026, 7, 13: IDENTICAL, run A PASS, run B PASS (exit 0).
- `-savecheck` + 4 v1 saves: PASS, including the new background-save check ("byte-identical to the one written in
  the call (78523 bytes), read after the request waited for it; overwrite + a second request: the second waited for
  the first; both byte-identical, no leftovers; loads the same state: yes; held file: failed on the worker
  (IOException), reported in Error; old save intact").
- Fuzz 8 seeds x 15 years: 8/8 exit 0, PASS, 0 violations.
- Batch `-autoplay -simspeed 4 -quitafter 120`: exit 0, 0 exceptions. Windowed smoketest + smokecontinue: both exit 0,
  0 exceptions, digest B73BDCD0E6E53A07 MATCH (the smoke game runs by wall-clock time, so its digest differs from run
  to run; Job A's run had 38BEB6E6784E4EC2).
- In-game async autosaves (`runs/pass2/b8/final/autosave2`, save7 at 4x for 150 s with `-perfautosave -savedigest`):
  autosave-1 (day 456, digest 71B31788207394E1) and autosave-2 (day 464, 1DC08604E8A8AAFA) written on the worker
  (on disk 18.7 / 15.7 ms after the request); each loaded back with `-load FILE -savedigest`: the same digests. (The
  -savedigest SHA1 runs in the save frame, so that run's frame times are not save timings.)
- Real saves folder after everything: identical to the snapshot (`runs/pass2/b8/realsaves-after.txt`).

## Caveats
- The water-depth block is never in a headless save (no water sim), so the old-vs-new byte comparison does not cover
  it; it goes through the same bulk float path as the map layers that are covered, and the in-game load digests
  match (the digest itself leaves water out).
- The quitting hook logs the last background save's error at quit even if it was already reported (seen once in the
  savecheck log for its deliberate held-file case); harmless, log only.
- A background save cannot be cancelled; a quit while one is in flight waits for it (15-25 ms).
