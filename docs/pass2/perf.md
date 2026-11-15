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
