# Pass 2 performance changes: measurements

## Save on the main thread (fix batch 1, audit-performance finding 5)

Change: `SaveGame.Write` serializes the body into a `MemoryStream` (256 KB to start) and compresses it with one
`GZipStream.Write`, instead of a `BinaryWriter` writing straight into the `GZipStream` one value at a time (about 200k
tiny deflate writes). The bytes' meaning is unchanged (fuzz save round trips, save/load identity and the v1
compatibility check all pass on both builds). Saving stays on the main thread in this batch.

How measured: `-autotest -savecheck -compatsave FILE` loads each save, plays 40 days with the auto-player, then times
five `SaveGame.Write` calls of that game with a Stopwatch (`[SaveTime]` lines in `player.log`; no water depths, as in
the auto-test; a windowed game also writes the 64 KB water block). "Before" = the batch-1 build with the A2/A3/A10
fixes but the unbuffered writer (`runs/pass2/b1/a3a10/savecheck/player.log`); "after" = the same plus the buffer
(`runs/pass2/b1/perf/savecheck/player.log`). Same PC (Ryzen 5700X), other test players running in parallel on it.

| Scenario (game saved) | Before: min / mean of 5 | After: min / mean of 5 | File | Collections per save (before / after) |
|---|---|---|---|---|
| seed 7, day 492, 152 people, 155 buildings | 30.8 / 35.6 ms | 21.8 / 26.8 ms | 114 KB | 15.8 / 15.2 |
| seed 11, day 341, 131 people, 100 buildings | 29.6 / 33.5 ms | 21.4 / 25.0 ms | 100 KB | 15.6 / 15.2 |
| seed 2026, day 740, 167 people, 186 buildings | 34.1 / 37.1 ms | 23.4 / 27.6 ms | 131 KB | 15.6 / 15.2 |
| seed 42, day 640, 161 people, 183 buildings | 34.1 / 40.3 ms | 23.7 / 27.4 ms | 126 KB | 15.6 / 15.2 |

Result: about 9-13 ms (25-30%) less per save. The number of collections did not change, so the garbage is not from
the deflate writes: likely candidates (not profiled) are the map's per-save copies (`TreeOffset` into a new 128 KB
`float[]`, `Flow.Cost.ToArray()`), the `MemoryStream` growing past 256 KB, the event-log strings, and the file
replace itself (Windows file creation and a virus scanner's look at the new file are inside the timed span). Moving
the compress-and-write to a worker thread (the audit's second suggestion) is left for a later batch.

## Saving off the main thread (pass 2 batch 8)

Same PC (Ryzen 5700X), same save (seed 7, day 452, 152 people, 155 buildings), batch mode rendering into a 1920x1080
texture, autosaves into the run's scratch folder. Scenarios (`runs/pass2/b8/perf/scen.sh`, three runs each):
- R4Q: `-load seed7-day452 -perfprobe -perfspeeds 4 -perfsecs 60 -perfautosave -perfquick 3`: the season-start
  autosave at 4x (day 456, 1 s after the season starts), then three quicksaves at 1x through the F5 call.
- R1: `-perfspeeds 1 -perfsecs 200 -perfautosave`: the same autosave at 1x.

Measured with the probe's `[ProbeSave]` line (the save frame's wall time Update to Update, the worst other frame
within 1 s, collections from the save's start to 1.2 s later, time from the request until the file is in place) and
`[SaveParts]` (Stopwatch sections inside the save, collections in each). Logs: `runs/pass2/b8/perf/<label>/<run>/player.log`.

### Where the time went before (d6925db: the combined tree plus the measurement hooks)
The in-game save frame on the combined tree was 34-44 ms, not the 85-95 ms of `perf-hitches.md`: that number came from
lane 2's tree, which did not yet have batch 1's buffered writer. One autosave (R1 run 1), all on the main thread:

| Part | ms | collections |
|---|---|---|
| choosing the autosave slot (3 file times) | 2.6 | 0 |
| game block (camera, HUD state) | 3.0 | 0 |
| open the .tmp file | 0.8 | 0 |
| header | 2.0 | 1 (the new 256 KB body stream) |
| map arrays (16k-cell layers one value at a time, a 128 KB offset copy, the walk-cost `ToArray`) | 6.6 | 6 |
| settlement (events, buildings, people) | 5.4 | 2 (the body stream growing 256 KB -> 512 KB -> 1 MB) |
| auto-player | 0.0 | 0 |
| water depths (64 KB copy, one value at a time) | 0.9 | 1 |
| game and test blocks | 0.1 | 0 |
| compress (gzip, Fastest, ~700 KB -> 114 KB) and write | 13.7 | 0 |
| close | 0.1 | 0 |
| replace (File.Replace, delete .bak) | 1.0; 6-10 when overwriting the quicksave | 0 |
| **save call** | **34.5** | **10** |

Quicksaves had the same shape without the slot and game-block first-use costs (21.6-41.4 ms). Collections: 9-10 per
save, each one a block-sized allocation (Unity's Boehm GC collects on almost every large allocation here).

### What changed
1. 8b0b950: the save is serialized in the frame (header and body into memory, one consistent moment: nothing steps
   in between) and compressed, written and swapped into place on a worker thread (`SaveGame.WriteInBackground`,
   `Task.Run`). At most one save in flight; a second save, a load, Continue, the save list, the next autosave slot,
   F8, a scene change and quitting (`Application.quitting`) wait for it. The toast comes when the file is on disk.
   The save screen, the fault screen and every test write in the call (`SaveGame.Write`), as before.
2. eeb785b: the header and body buffers are kept between saves, arrays are written in bulk (64 KB chunks), the tree
   offsets, walk costs and water depths are written from where they live (no 128 KB / 16 KB / 64 KB copies). Same
   bytes: the savecheck's saves from the old writer (d6925db) and the new one have byte-identical gzip bodies.
3. d173cc0: once per game (first frame after a load, or once the campfire is lit) the game is serialized into the
   buffer and thrown away, one empty worker task is run and the slots are looked at, so the first autosave does not
   pay for compiling the save code, starting the thread pool and the first file lookup (8-10 ms, logged as
   "save code warmed up", in a frame that is already long).

### Before and after (three runs each; "after 1" = 1+2, "after" = 1+2+3, the final build)

| | Before (d6925db) | After 1 (no warm-up, build-b8-1) | After (final, build-b8-3) |
|---|---|---|---|
| Season-start autosave at 4x: save frame | 38.4 / 39.8 / 37.9 ms | 19.3 / 19.8 / 18.7 ms | **5.3 / 4.7 / 3.7 ms** |
| same: in the save call on the main thread | 33.2 / 33.6 / 33.2 | 14.3 / 14.0 / 13.5 | 1.3 / 2.2 / 1.3 |
| Season-start autosave at 1x: save frame | 39.6 / 37.9 / 37.9 | 21.3 / 18.2 / 18.3 | **4.2 / 5.3 / 3.8** |
| same: in the save call | 34.5 / 33.3 / 33.0 | 15.4 / 13.5 / 13.8 | 1.9 / 2.2 / 1.4 |
| Quicksave (F5 call), 1x, 9 saves: save frame | 23.3-44.4 (median 30.9) | 2.7-4.7 | **2.2-4.2 (one 11.2)** |
| same: in the save call | 21.6-41.4 | 0.9-2.1 | 0.8-1.6 (one 5.6) |
| Collections from the save to 1.2 s later | 9-10 | 0-1 | 0-1 |
| Request until the file is in place | 33-35 (auto), 22-41 (quick) | 28-31 (auto), 15-31 (quick) | 17-20 (auto), 15-25 (quick) |
| Worst other frame within 1 s of an autosave | 3.2-4.1 | 3.0-5.6 (one 31.4) | 24.8-39.7 |

Notes:
- The quicksave outlier (11.2 ms frame, 5.6 ms call, R4Q run 3) had a collection inside the settlement write.
- "Worst other frame within 1 s" after the change is the season-start day tick itself (day 456 00.0h: daily step
  plus a 137-141-field path batch, 24.8-26.4 ms, the same frame as before: 25.9 ms in the before R1 run), which falls
  just inside the 1 s window now that the save frame ends sooner; the before runs had it just outside. R1 run 3's
  39.7 and after-1's 31.4 are the same frame or render frames with no save work (Render 20-28 ms bursts while another
  agent's players ran). No frame with save work is in that column.
- The commit itself (open, compress, close, replace) still takes 15-25 ms, now on a worker thread: compress 11-15 ms,
  replace 1 ms for a new slot and 6-13 ms when overwriting the quicksave (File.Replace plus the .bak delete).
- Headless (`-savecheck` `[SaveTime]`, in the call): 25-34 ms -> 11.6-15.7 ms min per save, collections per save
  11.6-15.2 -> 0.4-0.8.
- The commit message of 8b0b950 quotes the 85-95 ms of `perf-hitches.md`; the combined tree's real before number is
  the 34-44 ms above.
