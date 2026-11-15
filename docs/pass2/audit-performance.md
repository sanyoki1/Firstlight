# Performance audit (pass 2)

Branch `experiment/quarter-pass2` (head 468a70a). I measured and reported; nothing in the game was fixed. Raw logs are
under `runs/pass2/perf/` (git-ignored). Machine: Ryzen 5700X (16 threads) + RX 7800 XT. Another audit agent ran its own
player at the same time.

**Instrument.** I added a `-perfprobe` flag. It is new and costs nothing when off: each hook is a single static bool
test. It times every part of `GameController.Update` and of `Hud.OnGUI`, the sim's own parts (`Settlement.Profile`),
flow-field builds, GC collections and the 12 worst frames of each phase, and records which subsystems those frames spent
their time in. Heap growth per section is a lower bound: Boehm counts heap blocks, and a collection inside a section
hides growth. Only sums over thousands of frames mean anything. A calibration line in every log shows that. Batch runs
render every frame into a 1920x1080 texture, as `-soak` does. The probe player is a separate build in
`runs/pass2/perf/build-probe/`. Its 2-year and 15-year auto-test reports are **byte-identical** to the original
`Build/` player's (seed 7), so the probe does not change what happens.

**Test town.** Seed 7 auto-test save at day 452: year 15, Bronze Age, 152 people, 153 buildings including paths and
sites. File: `runs/pass2/perf/save7/seed7-day452.flsave`.

**Windowed run.** Unlike earlier sessions, the windowed `-popupwindow` player ran unthrottled this time, at about 160
fps. So the IMGUI numbers below are real per-frame figures from a full game frame, not per-call figures taken from a
1.5 fps run.

## Summary (what a player feels)

1. **A 30-130 ms freeze after every change to walking costs.** This happens after laying or lifting a path tile,
   placing or demolishing a building, loading a save, and about once a second during a flood. The worst freeze comes at
   the next shift start. Cause: every cached path is dropped, then rebuilt one by one on the main thread at 1.35 ms
   each. The parallel prefetch that was built for this never runs in the real game. On a laptop 3-4x slower this would
   be a freeze of 0.3-0.5 s.
2. **The water surface mesh takes 3.0-3.4 ms of every frame**, about 60 % of a batch frame and half of a windowed one.
   It is rebuilt every frame from all 16 k cells in managed code, even though the river barely changes. This is the
   single largest steady cost, and the main thing between a mid laptop and 60 fps.
3. **The IMGUI HUD causes about 13-20 GCs a second in real play** (770-1260 a minute; 2650 a minute with the research
   screen open). It allocates 31-42 KB per frame (99 KB with the research screen) and costs 1.8-3.9 ms per frame. The
   top bar alone takes 1.4-1.6 ms and 26-32 KB. This is the real F27. Batch mode could not see it because batch mode
   does not call OnGUI. Batch mode's own ~1 GC/s comes from somewhere else: `Autosave()` reading the command line 3
   times every frame.
4. **Season start is the worst regular frame.** A soft path commit plus a parallel prefetch of about 139 fields takes
   27-30 ms, and the daily step adds its share, for a 36-50 ms frame. The autosave (30-44 ms, on the main thread, about
   9 GCs per save) runs in the same frame. Season start should therefore cost about 70-90 ms in a normal game; this
   figure is estimated from parts, because I switched autosaves off to protect the player's slots.
5. **Things that are fine:** the sim step (0.33-0.52 ms at 152 people, roughly linear to 2000 with no cliff), the
   Burst water sim, the settlement view packing, load (25-30 ms plus a 145 ms scene reload), new game (143-271 ms) and
   memory (flat).

## Measurements

All frame times are wall time from `Update` to `Update`, which includes rendering. "p99.9" is the frame time that only
0.1 % of frames exceed.

| # | Scenario (run dir) | Frame ms: avg / p50 / p99 / p99.9 / max | Other numbers |
|---|---|---|---|
| 1 | 152-town 1x, batch, 60 s (A) | 4.90 / 4.71 / 6.85 / 20.1 / 61.9 | max = path rebuild after load (37 fields, 48 ms) |
| 2 | same 2x (A) | 5.28 / 5.06 / 7.45 / 23.7 / 38.8 | max = season start: 139-field parallel prefetch 29.7 ms + daily |
| 3 | same 4x (A) | 5.30 / 5.05 / 10.1 / 24.4 / 56.1 | max = WaterMesh 50.6 ms (preemption-type spike) |
| 4 | Frame split, batch, all speeds (A, B, C) | WaterMesh **2.9-3.1**, Render submit 1.1-1.4, Wild 0.35, SettlementView 0.20-0.25, HUD Update 0.025-0.036, Water sim 0.006-0.009, Atmosphere 0.005 | repeated in 4 runs; spread ±0.1 ms |
| 5 | Sim ms per 0.25 s step, 152 people (A) | 1x 1.27 (first minute after load: path builds), 2x 0.52, 4x 0.33 (citizens 0.17 + hourly 0.15 + daily 0.01) | hour-tick frames avg 6.4-8.0 ms, max 12.8 |
| 6 | Path tile laid/lifted every 5 s, 1x (A) | 5.39 / 5.23 / 7.08 / 44.7 / 68.6 | 317 fields in 317 single builds, 429 ms in 30 s; 7 frames > 33 ms |
| 7 | same 4x (A) | 4.95 / 4.68 / 7.36 / 43.7 / **127.4** | 822 fields / 694 batches, 919 ms in 30 s; three frames of 102-127 ms at 06:30 (shift start) |
| 8 | Sim flood, max strength, 4x, 20.6 s (B) | 5.50 / 5.12 / 7.71 / 67.2 / **95.0** | 1141 fields in 1141 single builds = 999 ms; 14 frames > 33 ms. Water 0.09, FillFlood 0.08 ms/frame |
| 9 | F6 surge, 1x, 85 s (B) | 5.20 / 4.91 / 8.40 / 56.3 / 80.3 | **45 frames > 33 ms, 20 > 50 ms**; 4147 fields / 4019 batches = 3.04 s |
| 10 | Crowd 550 (C), 1x / 2x / 4x | 5.95 / 6.26 / 6.69 avg; p99 8.0-10.2 | sim 2.19 / 1.07 / 1.05 ms per step; Wild 1.2-1.3; SettlementView 0.47-0.57 |
| 11 | Crowd 1000 (C) | 7.89 / 7.76 / 7.10 avg; p99 10.8-12.0 | sim 1.35 / 1.42 / 1.18 ms per step; hour-tick frames 11.3 avg; Wild 1.9-2.3; SettlementView 0.80-0.88 |
| 12 | Crowd 2000 (C) | 10.9 / 10.6 / 11.0 avg; p99 17-20 | sim 2.07-2.87 ms per step (hourly 1.1); hour-tick frames 19 avg, 29 max; 05:24 frames 22-25 ms; **Wild 4.2-4.6** |
| 13 | Sim scaling, head-less `-autotest -crowd N` (fresh camp + auto-player, 1 spring) | 162: 0.44, 412: 0.57, 1012: 0.92, 2012: 1.21 ms per step | roughly linear, no cliff |
| 14 | GC, batch, 18-arg command line (A, B, C) | 82-90 gen-0/min at 152 people (fewer at lower fps) | only allocator seen: `Autosave` 1.9-2.8 KB/frame |
| 15 | GC, batch, 11-arg command line (D) | **40/min**; `Autosave` 1.4 KB/frame | confirms: the batch GC rate follows `GetCommandLineArgs` |
| 16 | **Windowed** 152-town 1x, HUD default (G) | 6.25 / 6.14 / 8.20 / 10.2 / 58.4 | **874 GC/min**; OnGUI **1.81 ms**, 41 KB/frame (top bar 1.36 ms / 32 KB, feed 0.27 / 6.8 KB, build bar 0.17 / 3.2 KB); GC frames avg 6.94 (vs p50 6.14), max 29.6 |
| 17 | Windowed, jobs panel open (G) | 7.21 avg | 1265 GC/min; OnGUI 2.73 ms (panel 1.05 ms / 12.6 KB) |
| 18 | Windowed, goods panel (G) | 6.88 avg | 1022 GC/min; OnGUI 2.29 (panel 0.59 / 9.2 KB) |
| 19 | Windowed, goals panel (G) | 6.40 avg | 796 GC/min; OnGUI 1.79 |
| 20 | Windowed, research screen (G) | 8.46 avg, max 40.4 | **2648 GC/min**; OnGUI **3.88 ms**, 99 KB/frame (research 2.15 ms / 76 KB) |
| 21 | Windowed, campfire selected / citizen selected / placing a lean-to (G) | 6.61 / 6.90 / 6.91 avg | 842 / 778 / 770 GC/min; OnGUI 1.97 / 2.06 / 2.01; info panel 0.14-0.18 ms |
| 22 | Windowed title menu, 2 runs (S/win1, win2) | 7.08 / 6.94 avg | **820 / 832 GC/min**; OnGUI **3.16 / 3.04 ms**, 38-40 KB/frame (7 saves in the save folder) |
| 23 | Windowed new game, campfire prompt (S) | 4.25 / 4.19 avg | 128 GC/min; OnGUI 0.19 ms, 6 KB/frame |
| 24 | Save, `SaveTo`, 151 / 549 / 999 people (A, C) | 29.7-43.8 / 34.0-34.3 / 33.0-36.3 ms, main thread | 112 / 117 / 124 KB; **9-11 collections during each save** |
| 25 | Load, `SaveGame.Read` without the scene (A, C) | 23.2-28.3 ms (152), 26.5-32.1 (549), 26.2-30.2 (999) | |
| 26 | Scene reload + load to the first frame (A) | 145 ms | `StateDigest` (smoke test only) 16.8 ms |
| 27 | Startup, batch (S) | engine start -> `Start` 46 ms; title menu's first frame at **0.44 s** | data 17, settings/camera 99, map gen 14, world view 64, water settle 106, rest 69 ms |
| 28 | Startup, windowed, 2 runs (S/winwall.txt) | process launch -> first title-menu frame **4.13 / 4.29 s** | `Start` begins at 2.61 / 2.64 s: Unity splash plus device start; Start itself 0.43-0.45 s |
| 29 | New game (menu -> campfire prompt) | 143 ms batch; 175 / 271 ms windowed | |
| 30 | Heap / memory | flat at 9-12 MB over all runs | no growth, no leak |

**Noise.** Single 15-25 ms spikes in Render or WaterMesh that coincide with no flow-field build, no GC and no day
boundary are most likely OS preemption: the other agent's player was running, and the Windows scheduler quantum is
about 15.6 ms. They sit in whichever section is longest. They set p99.9 in the batch runs but are not counted as
findings. Every hitch named in the findings has a cause in the same frame (for example "flow 84 (119.8 ms)").

## Ranked findings
Line numbers are at HEAD (468a70a), before my probe hooks were added.

### 1. Every walking-cost change rebuilds every path one at a time on the main thread: 30-127 ms freezes (floods: about one a second)
- **Evidence.**
  - Runs 6-9 and 1. After a path tile is laid or lifted, the next frames that need paths build 25-84 fields serially
    (`flow 84 (119.8 ms)` in one frame at 06:30, shift start, 4x). Run 7 has three frames of 102-127 ms in 30 s.
  - A flood (run 8) makes `SyncWaterClasses` change water classes every second. Each change is a hard commit, so all
    paths are dropped every second: 1141 single builds in 20 s, frames up to 95 ms. The F6 surge (run 9) gives 45
    frames over 33 ms in 85 s.
  - The first in-game minute after a load builds 180 fields one at a time (frames of 42-62 ms).
- **Why the existing prefetch never runs in the game.** Nothing in the sim is wrong; the problem is the order of calls.
  - `GameController.Update` calls `Map.CommitCostChanges()` before `Sim.Update` (`GameController.cs:168` at HEAD).
  - So the sim's `Hourly()` finds nothing left to commit, and `if (Map.CommitCostChanges()) PrefetchPaths();`
    (`Settlement.cs:211`) never fires in the real game. It only fires in head-less runs, which is why the auto-test
    numbers never showed this.
  - Citizens then call `Flow.Resolve` in `Move` (`Settlement.People.cs:92`). Each miss runs
    `Build(new List<int> { targetCell })` (`FlowFields.cs:185`): one Burst job of a single item, `Complete()` on the
    main thread, about 1.35 ms, with no parallelism.
  - Deer do the same in `Wildlife.cs:312/518/573` (Wild spikes of 8-14 ms).
  - Water-class flips (`GameController.cs:247` -> `WaterSim.SyncWaterClasses` -> `GameMap.RefreshCost`) count as hard
    changes.
- **What the player feels.** A visible stall each time they lay a path or place or remove a building, worst at the
  next shift start (about 0.1 s here, 0.3-0.5 s on a laptop 3-4x slower). The game stutters through every spring
  flood. Loading a save stutters for its first in-game minute.
- **Effort:** M.
- **Suggested fix**, in order of value:
  - (a) Expose a sim command, such as `Settlement.CommitWalkCosts()`, that does commit + `PrefetchPaths()`. Call it
    from `GameController.Update` instead of the bare `Map.CommitCostChanges()`. This is the one-line cause.
  - (b) At the top of `StepOnce`, collect the distinct uncached `Target`s of walking citizens (and departing people)
    and `Flow.Prefetch` them in one parallel batch, so a miss is never built alone.
  - (c) Prefetch the doors after a load (`SaveGame.Read` / the end of `Map.Read`).
  - (d) Floods: commit water-class changes at most once per game hour, or treat floodplain cells flipping
    shallow <-> dry as soft changes until the flood ends.
  - (e) Speed up one field. The costs are small integers (10/14 x 1..8), so a bucket queue (Dial's algorithm) instead
    of the `NativeList<long>` binary heap would likely make each field 2-3x faster (not measured).
- **Determinism and saves.**
  - (a), (b), (c): safe. A field is a pure function of the committed costs and the target. The cache is already built
    so that "routes never depend on which paths happened to be cached" (`GameMap.LiveCost` comment), so building them
    earlier or in a batch cannot change a route. No save change.
  - (d) changes when costs commit in the in-game loop only. The head-less auto-test does not run `UpdateWater`, so its
    reports stay identical. In-game water already runs on frame time and is not bit-exact through a save (build log,
    phase 4 known gaps).
  - (e) must keep equal-cost tie-breaking identical, or routes change. Re-baseline the auto-test if it changes.

### 2. The water surface mesh is rebuilt from the whole map every frame: 3.0-3.4 ms per frame, steady
- **Evidence.** `WaterMesh` 2.9-3.4 ms per frame in every run, on the title menu and in game, batch and windowed
  (runs 1-4, 16-23): about 60 % of a batch frame and about 50 % of a windowed frame. `WorldView.Draw` calls
  `UpdateWaterMesh()` (`WorldView.cs:382` at HEAD) unconditionally. The function (`WorldView.cs:195-260`) does this in
  managed C#:
  - it visits all 129 x 129 corners with 4 `NativeArray` reads each;
  - most corners are dry, and for each of those it reads 16 more;
  - it scans all 16 k cells with a 9-read `WetNeighbour` test;
  - it uploads four `SetVertices`/`SetColors`/`SetUVs` streams and the triangles.
  
  The water itself only steps every 0.05 s (`WaterSim.StepSeconds`).
- **What the player feels.** Nothing here, at 160-200 fps. On a laptop 3-4x slower it is about 10-13 ms on its own,
  which alone puts 60 fps out of reach. It also pads every frame on the title screen.
- **Effort:** S for the first two steps, M for the job.
- **Suggested fix.**
  - (1) Rebuild only on frames where `Water.Step` ran, and at most once per water step (20 Hz). This alone saves 3x at
    60 fps and about 8x at 160 fps.
  - (2) Keep a mask of cells that can ever be wet, computed once: river, floodplain (`Settlement.FloodRisk` / the cells
    `ComputeFloodCells(max)` returns) and a 2-cell margin. Loop over that list only, likely a 5-10x reduction.
  - (3) Move the corner pass into a Burst `IJobParallelFor`, and fill the mesh through `Mesh.AllocateWritableMeshData`.
  - Also cache `MaterialLibrary.Layer("water")` (a per-frame string lookup).
- **Determinism:** none (view only).

### 3. The IMGUI HUD allocates 31-42 KB per frame (99 KB with the research screen): 770-2650 GCs a minute in real play (this is F27)
- **Evidence.**
  - Windowed runs 16-23: about 15 collections a second with the default HUD, 21 a second with the jobs panel and 44 a
    second with the research screen.
  - OnGUI costs 1.8 ms per frame with the default HUD, 2.7 ms with the jobs panel and 3.9 ms with the research screen.
    It runs twice per frame (Layout and Repaint).
  - Breakdown (ms per frame / heap growth per frame):

    | Part | ms | KB |
    |---|---|---|
    | `DrawTopBar` | 1.36-1.57 | 26-32 |
    | `DrawFeed` | 0.06-0.27 | 0.2-6.8 |
    | `DrawBuildBar` | 0.17-0.22 | 1.2-3.2 |
    | Jobs panel | 1.05 | 12.6 |
    | Goods panel | 0.59 | 9.2 |
    | Research | 2.15 | 76 |

  - GC frames run on average 0.6-0.8 ms longer than the median frame, and up to 22-30 ms.
- **The allocators** (all rebuilt on every OnGUI call, whether shown or not):
  - **Tooltips built eagerly.** `Tip(rect, $"...")` formats the whole tooltip before testing the mouse (`Hud.cs:449`).
    The top bar builds about 20 tooltips per call, several with `:P0` / `:0.#` formatting and long concatenations
    (`Hud.TopBar.cs:84, 95, 107-108, 117-118, 136-137, 150, 169`). The build bar builds `Describe(d)` (a `List<string>`
    plus LINQ) for every row of an open category (`Hud.BuildMenu.cs:71, 90-114`).
  - **`Col()` everywhere** (`UiTheme.cs:105`): each call is `ColorUtility.ToHtmlStringRGB` (a new string) plus an
    interpolation. There are dozens per top-bar call, including `Arrow()` for each of up to 12 stocks
    (`Hud.TopBar.cs:61-66, 168`).
  - **`new GUIStyle(...)` per call**, which creates a native object with a finalizer each time:
    `Hud.TopBar.cs:133, 177`; `Hud.Panels.cs:218` (once per feed row, up to 7 per call); `Hud.cs:554` (placement tip);
    `Hud.Guide.cs:85-86`; `Hud.BuildMenu.cs:69-70` (twice per category row); plus `Hud.Agri.cs` (6) and `Hud.Menus.cs` (11).
    That is about 30 `new GUIStyle` lines outside `UiTheme`; a few may be one-time fields.
  - **New arrays and LINQ per call.** `float[] speeds`, `string[] names`, `labels`, `keys`, `open`, `tips`
    (`Hud.TopBar.cs:88-89, 139-142`). `S.AvailableResearch.Any()` (`Hud.TopBar.cs:132`), whose
    `r.prereqs.All(_researched.Contains)` makes a delegate per topic. `InCategory(c).Count(S.IsUnlocked)` for each of
    9 categories, and `S.Sites.Count()` (`Hud.BuildMenu.cs:31, 43`). `new GUIContent` per feed row and chip
    (`Hud.Panels.cs:188, 217`).
  - **O(N) settlement properties evaluated repeatedly per call.** `Population` (4x), `Homeless` (2x), `Adults`,
    `HousingCapacity` and `AverageHappiness` each loop over every citizen or building (`Hud.TopBar.cs:101-108`).
    That is CPU, not garbage, but it grows with the town.
- **What the player feels.** At 160 fps a 0.7 ms GC every few frames is invisible. On a slower PC the HUD alone is
  5-8 ms per frame (12-15 with the research screen), the GC pauses grow with it, and the occasional 20-30 ms GC frame
  becomes a visible hitch.
- **Effort:** M. It is mechanical, but there are many sites.
- **Suggested fix.**
  - (1) Make tooltips lazy, with `Tip(Rect r, Func<string> text)` or `if (r.Contains(mouse)) _tip = ...`. Biggest
    win, small change.
  - (2) Create every derived `GUIStyle` once in `UiTheme.Ensure` (`Label14`, `SmallRight`, `FeedRow` and so on).
  - (3) Precompute the colour tags once (`static readonly string GoodTag = "<color=#94CC85>"`) so `Col` is one
    concat. Better still, cache whole label strings and rebuild them only when the value shown changes (for example
    keep `_lastPop` and `_popLabel`).
  - (4) Hoist the per-call arrays to `static readonly`.
  - (5) Compute `Population` and the other totals once per OnGUI call, or once per sim step in the sim, as cached
    counters.
  - (6) Research screen: cache the laid-out node strings per research state.
- **Determinism:** none (view only).

### 4. Season start: soft commit, a 139-field prefetch and the autosave all fall in one frame (estimated 70-90 ms)
- **Evidence.**
  - The day-tick frames at a season start (runs 2, 7, 10, 12) take 36-50 ms: flow 137-139 fields (27-30 ms in
    parallel) plus a daily sim of 17-26 ms.
  - At HEAD, `Settlement.NewDay` calls `if (DayOfSeason == 0 && Map.CommitSoftCostChanges()) PrefetchPaths();`
    (`Settlement.cs:246`). It drops every cached path because trees were felled or grew.
  - The autosave runs in the same `Update` (`GameController.Flow.cs:113-123`). It takes 29.7-43.8 ms (finding 5). I
    switched autosaves off in the probe so it would not write into the player's three autosave slots, so the combined
    frame is an estimate from the measured parts: about 70-90 ms.
- **What the player feels.** A clear hitch every 8 game days (every 80 s at 4x). A laptop would see about 0.25 s.
- **Effort:** S-M.
- **Suggested fix.**
  - Put the autosave one frame (or a second) after the season start rather than in the same frame.
  - Make the save faster (finding 5).
  - Build the soft-commit fields as a scheduled job double-buffered against the old ones, completed before the first
    step that needs them. Alternatively, skip invalidating for soft (tree) changes inside a town whose routes run on
    paths, though that is a design call.
- **Determinism.** Moving the autosave changes nothing in the sim. The soft commit must stay at the same sim time;
  building it asynchronously is deterministic as long as the job completes before any `Resolve`.

### 5. The save runs on the main thread in 30-44 ms and triggers about 9-11 GCs per save
- **Evidence.** Runs 24 and 25: 29.7-43.8 ms, 112-124 KB on disk, 9-11 collections during each save, with no
  difference between 152 and 999 people. `SaveGame.Write` (`SaveGame.cs:51-80`) wraps the `GZipStream` directly in a
  `BinaryWriter` (`SaveGame.cs:63-64`). The map is then written one value at a time: 16 k-cell
  `Floats`/`Bools`/`Ints` arrays, `GameMap.cs:513-525`, about 200 k tiny writes, each going into Mono's DeflateStream.
  The save also copies the water depth into a new `float[]` and writes the whole event log (up to 20 000 strings,
  `Settlement.Save.cs:50-51`). The per-write overhead is my reading of the code; I did not profile inside the save.
- **What the player feels.** A small hitch on every autosave and quicksave (F5) here; 0.1-0.15 s on a laptop. The
  event log grows the save over a long game.
- **Effort:** S.
- **Suggested fix.**
  - Serialize into a `MemoryStream` first (or put a 64 KB `BufferedStream` between the `BinaryWriter` and the
    `GZipStream`), then compress in one call.
  - Better: take the snapshot into a `byte[]` on the main thread (a few ms), then compress and write on a worker thread
    (`Task.Run`), keeping the temp-file-then-move scheme.
  - Cap the saved event log to the last few hundred events.
- **Save compatibility.** Buffering does not change the bytes. Capping the event log changes what is saved (old saves
  still load); the save/load identity check compares reports, so check it still passes.

### 6. Batch-mode GC source (F27 as measured): `Autosave()` reads the command line three times every frame
- **Evidence.** `Update` calls `Autosave()` every frame (`GameController.cs:171` at HEAD). Its first line calls
  `HasArg("-uishots") || HasArg("-p4shots") || HasArg("-autoplay")` (`GameController.Flow.cs:115`). `HasArg` calls
  `Environment.GetCommandLineArgs()` (`GameController.cs:769`), which allocates a new array and new strings on every
  call on this Mono.
  - This was the only allocator visible in batch: 1.9-2.8 KB per frame, giving 82-90 GCs a minute (run 14).
  - With an 11-argument command line instead of 18: 1.4 KB per frame and 40 GCs a minute (run 15).
  - `HasArg` takes 1.7-4.2 µs per call.
- **What the player feels.** Nearly nothing for a player launched from Steam with no arguments, a few hundred bytes a
  frame. It made the QA soak's GC rate look like a gameplay problem, and it is a trap for the next probe.
- **Effort:** S.
- **Suggested fix.** Parse the flags once into `static readonly bool` fields at `Start`, or cache
  `GetCommandLineArgs()` in a static array. The same applies to `CommandLineMode` (LINQ, start only, harmless).
- **Determinism:** none.

### 7. Deer check every person every frame: O(deer x people), 4.5 ms at 2000 people
- **Evidence.** `Wild` costs 0.35 ms at 152 people, 1.2-1.3 ms at 550, 1.9-2.3 ms at 1000 and 4.2-4.6 ms at 2000
  (runs 1, 10-12). `Wildlife.Threatened` (`Wildlife.cs:373-382`) loops over `Threats`, which is every drawn citizen
  (`SettlementView.cs:489`), for every active deer, every frame. It runs on frame time, so it is not reduced by game
  speed.
- **What the player feels.** About 1 ms at 550 people here: fine now. It becomes a real cost in later ages, at 4-5 ms
  here and 15 ms on a laptop at 2000 people.
- **Effort:** S.
- **Suggested fix.** Bucket the threats into a coarse grid (8x8 cells) each frame, or first test against each herd's
  bounding circle (the herd centre plus a radius), and skip herds with no person within notice range plus the herd
  radius.
- **Determinism.** The deer are view-side and are not saved (the build log says so). Hunting kills come from the sim's
  herd `Pop`, not from these deer, so the sim is unaffected.

### 8. The title menu re-reads every save file's header on every OnGUI call
- **Evidence.** `DrawTitle` calls `SaveGame.Latest()` on every OnGUI call (`Hud.Menus.cs:168`). That runs
  `Directory.GetFiles`, opens every `.flsave` and reads its header, and sorts with LINQ (`SaveGame.cs:156-172`). The
  windowed title menu (run 22, 7 saves in the folder) shows OnGUI at 3.0-3.2 ms per frame, 38-40 KB per frame and
  820-832 GCs a minute. That is higher than the much busier in-game HUD's 1.8 ms. Attributing it to `Latest()` is my
  inference from the code; the menu has few other widgets.
- **What the player feels.** The first thing a player sees. The cost grows with the number of saves, and it is worse
  on a hard disk or with an antivirus scanning each file open: stutter or high CPU while idling on the title screen.
- **Effort:** S.
- **Suggested fix.** Cache `Latest()` / `List()` when the menu screen opens (`ShowScreen` already does this for the
  Load screen), and refresh after a save or delete.
- **Determinism:** none.

### 9. The Unity splash screen accounts for most of the 4.1-4.3 s to the title menu
- **Evidence.** Windowed, from process launch to the first title-menu frame takes 4.13 / 4.29 s. `GameController.Start`
  begins at 2.61 / 2.64 s after engine start in the windowed player, against 0.046 s in batch mode, which shows no
  splash. `ProjectSettings.asset` has `m_ShowUnitySplashScreen: 1`. `Start` itself takes 0.43-0.45 s, and the first
  menu frame follows 0.4-0.5 s later.
- **What the player feels.** A 2.5 s logo wait on every start. Unity 6 lets Personal-licence games turn the splash off.
- **Effort:** S (a project setting).
- **Determinism:** none.
- **Also possible (minor):** the water settle at start (4000 Burst steps, 70-150 ms) runs on every start and every
  load. It could be cached per seed, but it is not worth it now.

### 10. Later-age risks (not felt at 200-600 people; noted briefly)
- **Hourly work grows faster than linearly.** It costs 0.15 ms per step averaged at 152 people, 0.34 at 550, 0.6 at
  1000 and 1.1 at 2000. An hour-tick frame takes 19 ms on average and 29 ms at most at 2000 people (run 12). The
  `Hourly()` passes (`AssignHomes`, `AssignJobs`, `RefreshHomes`, reservations) are managed code and run in a single
  step. Spread them over the hour's steps, or move the heavy loops to Burst, before cities reach thousands.
- **Shift start at 05:24 costs 22-25 ms of citizen work at 2000 people** (10 ms at 1000). Every worker starts their
  errand in the same step.
- **Everything heavy in the sim is managed C#.** Only flow fields and the water sim are Burst (list below). The design
  target of thousands of citizens will need the per-citizen step in jobs. At the current slice's size it is cheap
  (0.17 ms of citizens per step at 152 people).
- The crowd numbers come from unhoused people added by `DebugSpawn`, who starve. That is a load test, not real play.

## Burst coverage

| Work | Burst? | Cost measured |
|---|---|---|
| Flow-field Dijkstra (`BuildFlowFieldsJob`, `FlowFields.cs:37`) | **yes** (parallel per field in batches, but one field per batch in the game: finding 1) | ~1.35 ms per field single, 139 in parallel 27-30 ms |
| Water sim (`BoundaryJob`, `FluxJob`, `DepthJob`, `WaterSim.cs:138-218`) | **yes** | 0.006-0.09 ms per frame |
| Stress-test agent jobs (`AgentJobs.cs`) | yes, stress test only | `CullAgentsJob.SampleHeight` is called from managed code by `GameMap.HeightAt`, so it is not Burst in the game |
| Per-citizen step (`Settlement.People.cs` `UpdateCitizen`/`Move`/`Act`) | no, managed | 0.17-0.3 ms per step at 152, 0.6-1.6 at 2000 |
| Hourly (`AssignJobs`/`AssignHomes`/...) and daily | no | 0.15 ms per step averaged (152) -> 1.1 (2000) |
| Water mesh (`WorldView.UpdateWaterMesh`) | no | **3.0-3.4 ms per frame** |
| View packing (`SettlementView.Draw`) | no | 0.2 (152) -> 1.56 ms (2000); linear, no garbage |
| Wildlife (`Wildlife.Update`) | no | 0.35 (152) -> 4.5 ms (2000) |
| `SyncWaterClasses` (once a second), `FillFlood` (during floods) | no | 0.15-0.43 ms per call; 0.08 ms per frame |
| Tree and rock instance rebuild (`RefreshTrees`/`RefreshRocks`) | no | up to 1.65 ms when a tree falls; rare |

There is no scaling cliff between 160 and 2000 citizens: the head-less step goes from 0.44 to 1.21 ms (run 13). The
non-linear parts are the hourly passes and the deer threat scan.

## Measured and fine (no need to re-check)
- Sim step at 152 people: 0.33-0.52 ms per step in steady play (4x is about 0.1 ms per frame). Hour-tick frames are
  only 1-2 ms above the median.
- Water simulation: Burst, 0.006-0.009 ms per frame normally, 0.09 ms during a flood. `FillFlood` 0.08 ms per frame.
  `SyncWaterClasses` 0.15-0.43 ms once a second. The water's cost during a flood is the re-pathing (finding 1), not the
  water.
- `SettlementView.Draw` (people, buildings, piles, fires): 0.20-0.30 ms at 152 people. It grows linearly and stays
  essentially allocation-free (the 0-200 B per frame readings are block noise). The night-only `_fires.Sort` lambda
  allocates a small closure per frame. Trivial.
- HUD `Update` (input, hover ray, guide and hints): 0.025-0.036 ms. Atmosphere and lighting: 0.005 ms.
- Tree and rock re-uploads on felling or quarrying: at most 1.3-1.65 ms, rare. The material map upload was not seen.
- Load: `SaveGame.Read` 23-32 ms. Scene reload plus load to the first frame: 145 ms. New game: 143-271 ms. Map
  generation: 6-14 ms. World view build: 15-64 ms.
- Startup work inside `Start`: 0.37-0.45 s (water settle 70-150 ms of it).
- Memory: managed heap flat at 9-12 MB in every run. Six scene reloads were already checked by QA (`-reloadtest`).
- GC pause per collection (incremental Boehm): GC frames are on average 0.5-0.8 ms longer than the median frame. The
  worst seen was 22-30 ms, rare. The problem is the count (finding 3), not the size of each.
- Render submission CPU (batch, URP, 1920x1080): 1.1-1.4 ms per frame at 152 people.
- Auto-player thinking (F9) was not part of the player runs (`Auto = null` unless `-perfauto`). The phase-4 notes
  already cover its cost.

## What could not be measured here
- The GPU side and a real laptop: every number is from this desktop. The laptop figures are a 3-4x scaling of CPU
  time, as the brief asked. GPU time was not separated from CPU time; render submission includes the driver.
- The autosave inside the season-start frame: estimated from its parts (finding 4), because autosaves were switched off
  in the probe to protect the player's slots.
- Allocation per call site inside a section. The Mono on this build only gives block-granular heap readings.
  `GC.GetAllocatedBytesForCurrentThread` reads 0, and 1000 x 1 KB arrays read as 0.29 MB with 5-7 collections in
  between. The call sites in finding 3 come from reading the code; the section totals were measured.
- I did not use the Unity profiler: that would need a development build and a GUI.

## Incident during this audit (please check)
My first build attempt used the stock `ProjectSetup.BuildWindows`, which writes into `Build/`. It failed partway,
because the other agent's player (PID 8712, started 02:36:43) held `Build/Firstlight_Data/Plugins/x86_64/lib_burst_generated.dll`.
By then Unity had already replaced `Firstlight.dll` and 8 data files in `Build/`.

- I restored those 9 files from my backup at 02:37:37. `diff -rq Build runs/pass2/perf/build-orig` now reports them
  identical.
- The other agent's run that started at 02:36:43 may have loaded the half-replaced build. That build was the
  probe-instrumented `Firstlight.dll` with the original Burst DLL. With `-perfprobe` off, the probe build plays exactly
  like the original (byte-identical 2-year and 15-year auto-test reports), so that run's results should still be valid.
  It is worth knowing about.
- After that I built only into `runs/pass2/perf/build-probe/`, using a new editor method.
- Sanity check on `Build/`: `-autotest -seed 7 -years 2` exits **1**. That is the test's FAIL verdict ("Reached the
  Agricultural Age: NO", which is impossible in 2 years), with "No errors or exceptions: yes". The probe build gives the
  same exit code and a byte-identical report, and the 15-year run of the original build exits 0 (PASS). Use 15 years
  if you want exit code 0.

## Files touched (all uncommitted)
- New: `Assets/Scripts/Game/PerfProbe.cs` (+ `.meta`), the static timers and section list.
- New: `Assets/Scripts/Game/GameController.PerfProbe.cs` (+ `.meta`), the driver: phases, logging, scenarios.
- New: `Assets/Editor/PerfProbeBuild.cs` (+ `.meta`), which builds the player into `runs/pass2/perf/build-probe/`
  instead of `Build/`.
- Marked `PERF PROBE` hooks (Begin/End calls; no-ops when off):
  - `Assets/Scripts/Game/GameController.cs`: `Start` sets `PerfProbe.On` and marks, and starts the driver; `Update`
    sections; `OnGUI` section; `UpdateWater` sub-sections.
  - `Assets/Scripts/Game/WorldView.cs`: `Draw` sub-sections.
  - `Assets/Scripts/Game/UI/Hud.cs`: `OnGUI` sub-sections.
  - `Assets/Scripts/Core/FlowFields.cs`: two read-only counters, `TotalBuildMs` and `Builds`.
- This report: `docs/pass2/audit-performance.md`.
- Not mine: the modified `Firstlight.slnx` and the untracked `tools/gate-autotest.sh` were there before I started. I
  deleted the `Assets/Resources/PerformanceTestRun*.json` files that my first Unity run generated.
- Everything else is under `runs/pass2/perf/` (git-ignored): `build-orig/` (backup of `Build/`), `build-probe/`,
  `save7/` (the test save and its 15-year report), run dirs `A B C D G S crowd sanity sanity-probe det15`.

## How to re-run my measurements
Build the probe player. This does not touch `Build/`; it takes about 15 s to 2 min:
`tools/unity.sh Firstlight.EditorTools.PerfProbeBuild.BuildWindows pass2-perf-build`

Then, from `runs/pass2/perf` in Git Bash, with `W=$(pwd -W)` and `P=build-probe/Firstlight.exe`:

1. Test save (37 s):
   `$P -batchmode -nographics -autotest -seed 7 -years 15 -savemid "$W/save7/seed7-day452.flsave" -saveday 452 -outdir "$W/save7" -logFile "$W/save7/player.log"`
2. Speeds, re-path, save/load, reload (run A, about 4 min):
   `$P -batchmode -screen-width 1920 -screen-height 1080 -load "$W/save7/seed7-day452.flsave" -perfprobe -perfsecs 60 -perfplace -perfsave -perfreload -outdir "$W/A" -logFile "$W/A/player.log"`
3. Flood (B): the same, with `-perfnobase -perfflood`.
4. Crowds (C): the same, with `-perfnobase -perfcrowds 550,1000,2000 -perfsecs 60 -perfsave`. Each value is a target
   population.
5. Windowed HUD (G): `$P -popupwindow -screen-width 1920 -screen-height 1080 -load ... -perfprobe -perfspeeds 1 -perfsecs 60 -perfpanels -outdir "$W/G" -logFile "$W/G/player.log"`.
6. Startup and title menu (S): `$P [-batchmode | -popupwindow -screen-width 1920 -screen-height 1080] -perfprobe -perfstartup 2026 -perfsecs 15 -logFile ...`.
   This needs no `-load` and no `-seed`, so it goes through the title menu and then New game. The wall time from
   launch to "first title-menu frame" is in `S/winwall.txt` (PowerShell `Start-Process` plus polling the log).
7. Sim scaling: `$P -batchmode -nographics -autotest -seed 7 -crowd N -outdir ... -logFile ...` for N = 150, 400,
   1000, 2000 (the `[Perf]` line).

Other flags: `-perfspeeds 1,2,4` (default), `-perfauto` keeps the auto-player, `-perfsecs N` sets the phase length.

Read the results with `grep -A40 "\[Probe\] phase" player.log`. Each phase prints frame percentiles; GC per minute;
sim ms per step split into citizens, hourly, daily and jobs; hour-tick and day-tick frame times; flow-field builds; a
per-section table (average ms, max ms, calls and heap growth per frame); and the 12 worst frames with their two largest
sections. Keep the command line short when you compare GC rates (finding 6).
