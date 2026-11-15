# Pass 2 report: what was weak, what changed, what is left

Branch `experiment/quarter-pass2`, cut from `experiment/quarter-game` at tag `snapshot-pass2-start` (468a70a). 82
commits, not pushed, not merged; `main` untouched. `Build/Firstlight.exe` is rebuilt from the branch head.

How it was run: four audits (performance, code health, gameplay and balance, design and UX), a triage
(`plan.md`), then eight fix batches by Opus subagents, two at a time in separate working copies, each gated before it
was accepted. The orchestrator re-ran the identity checks after every batch and the whole gate on the final build.

## What was weak (from the audits)

| Area | Finding | Evidence |
|---|---|---|
| Hitches | Every building or path placed froze the game; floods stuttered once a second; a load stuttered for a minute | 120-133 ms worst frame laying a path at 4x; 14-16 frames over 33 ms in a flood |
| Steady cost | The water surface was rebuilt from the whole map every frame | 3.1-3.2 ms of a 5.1-5.5 ms frame |
| HUD | 31-42 KB of garbage and 1.7 ms every frame; this was QA's unexplained "one GC a second" (F27) | 944 collections a minute with the default HUD, 3535 with the research screen |
| Saves | Every save froze a frame; any new field or good in a later build would have broken every save; overwriting could lose the old file | 38-40 ms autosave frame; positional, unversioned format |
| Robustness | An error mid-frame left a frozen world and let autosaves overwrite good saves; a typo in a data file hung the tests | reading + a confirmed hang |
| First hour | The guide stranded the player without stone; warnings in the first minute that could not be acted on; winter help on the first day of winter; no way to find out who died or why; nothing said the next age was ready; Tab on the campfire prompt did nothing | `audit-ux.md` U1-U9, `ux-shots/` |
| Numbers | The same figure differed between panels; the building panel's output estimate ignored difficulty, water and laws | "Tools 12" vs "13"; "105 adults" vs "86 adults + 19 elders"; estimate off by 11-25 % |
| Balance | A late famine killed half a town in eight days; water, pottery and faith could be ignored at no cost; Harsh was a cold cliff | seed 13: 155 -> 74 people; needs off = no change; Harsh 1 of 6 seeds extinct, 34 cold deaths a run |
| Design | The Agricultural Age's added systems (laws, unrest, floods, disease, traders, tin) make no measurable difference, and the Bronze Age needs no bronze | `audit-gameplay.md`, systems table: see "Needs Nico" |

## What changed

### Performance (before -> after, same scenario; seed-7 day-452 save, 152 people, this PC)

| Scenario | Before | After | Where |
|---|---|---|---|
| Laying a path at 4x, worst frame | 120-133 ms | 25-27 ms | `perf-hitches.md` |
| Flood at full strength, frames over 33 ms | 14-16 | 0 | `perf-hitches.md` |
| First minute after a load, worst frame | 66-75 ms | 22 ms | `perf-hitches.md` |
| Steady frame, batch mode | 5.1-5.5 ms | 1.7-1.8 ms | `perf-hitches.md` |
| Water mesh per frame | 3.1-3.2 ms | 0.02 ms | `perf-hitches.md` |
| Autosave frame | 38-40 ms | 3.7-5.3 ms | `perf.md` |
| Quicksave frame | 23-44 ms | 2.2-4.2 ms | `perf.md` |
| Garbage collections per save | 9-10 | 0-1 | `perf.md` |
| HUD per frame, default view | 1.7 ms, 43 KB | 0.7-0.8 ms, 12 KB | `perf-hud.md` |
| HUD per frame, research screen | 3.3-4.0 ms, 87-90 KB | 1.5-1.7 ms, 28-30 KB | `perf-hud.md` |
| Collections a minute, default HUD | 944 | 186 | `perf-hud.md` |
| Title menu OnGUI | 3.4 ms | 0.1 ms | `perf-hitches.md` |
| Launch to title menu | 3.5-3.6 s | 1.0-1.1 s | `perf-hitches.md` |

The HUD targets (2 KB, 0.6 ms) were not reached: Unity's IMGUI allocates 60-190 bytes per label on every repaint,
whatever the game does, so the remainder scales with the number of labels on screen. Getting under it means leaving
IMGUI.

### Saves and robustness
- Saves carry a format version and goods by id; a hash of the map settings refuses a save from a different map
  generator with a message. Version-1 saves still load (four tested). Saves written from this build keep loading when
  later builds add fields or goods.
- Overwriting swaps the new file in (`File.Replace`); a failed write leaves the old save intact.
- Saving runs on a worker thread; loading, Continue, the save list and quitting wait for a save in flight. A save
  written that way is byte-identical to one written in the call.
- An error in a frame pauses the game, stops autosaving and shows a message (Load / Main menu / Save a copy / Quit).
- A bad data file stops with the file name and the parser message instead of a black screen or a hung test.
- Test and screenshot modes write saves and settings to a scratch folder; only a plain launch touches the real one.
- Goods are fully accounted: spoilage, flood losses, theft and goods leaving with people are counted, and the fuzz
  test checks conservation per good every game day.

### The first hour and the HUD (screenshots in `docs/art/quarter/pass2/`)
- Guide: the Elders' Fire step names its stone cost, offers a Stone Gatherer and waits until the fire is finished.
- Warnings name the cause and the remedy; the no-tools and storage warnings no longer appear before anything can be
  done about them; chips are clickable and move the camera to what they are about.
- "Winter is coming" arrives on the first day of autumn with the sim's own numbers (`p2-autumn-winter-hint.png`).
- A spring warning when the expected harvest will not cover the year.
- An Events panel (V): the last 200 events, a problems-only filter, and every hint already seen
  (`p2-events-problems.png`). The death hint names who died and of what.
- "Ready to advance" as an event, a green chip and a lit Goals button.
- The research box says when nobody is researching, and shows days left.
- Jobs panel: why a job is empty (`p2-jobs-reasons.png`). Citizen panel: what holds happiness down.
- Hover tags for buildings; badges over buildings with a problem, toggled with P (`p2-badges-late-town.png`).
- Tab works; Home/H returns to the campfire; PageUp/PageDown zoom; a Paused tag; demolishing a finished building
  with the tool needs a second click; the end screen has Save and Main menu.
- One source per number: tools, the people split, food days, and the building panel's "Makes about N a day", which
  now uses the sim's own formula (measured 0.94-0.97 of actual output while working, on all three difficulties).
- Text: a 13 px floor, stronger contrast, layouts checked at 1080p (70 / 100 / 160 %) and 720p (100 / 160 %).

### Balance (15 seeds x 30 years, Standard; `balance.md`)

| | Before | After |
|---|---|---|
| Seeds reaching Bronze | 15 / 15 | 15 / 15 |
| Bronze year (min / median / max) | 11.1 / 12.6 / 13.9 | 11.2 / 12.4 / 15.5 |
| Starvation deaths per run | 2.9 | 0.1 |
| Leavers per run | 4.5 | 5.7 |
| Migrants per run | 130 | 116 |
| Harsh: seeds dying out (of 6) | 1 | 0 |
| Harsh: cold deaths per run | 34 | 2.7 |

Hunger harms half as fast (people leave before they die); migrants need 0.8 average happiness, so ignoring needs
stops growth; Harsh has a milder winter and more starting goods; fishing 1.9 -> 2.8; 90 names per sex and no name
shared among the living. The careless auto-player still dies out on 6 of 6 seeds. A flax increase was tested and
dropped (the single Weaver is the limit).

## Final gate (branch head, orchestrator's own run)
- Build: 0 errors.
- `tools/gate-autotest.sh final`: 15 / 15 PASS, no errors, Bronze on every seed; summary identical to the balance
  batch's run, reports byte-identical to the combined-tree run.
- Save/load identity: seeds 2026, 7, 13 IDENTICAL.
- `-savecheck` with four version-1 saves: PASS.
- Fuzz, 8 seeds x 15 years: no violations (goods conservation included).
- In-game: 120 s autoplay with 0 exceptions; smoke test then Continue, digest MATCH.
- Windowed `-uishots` (30 shots) and `-panelshots` (24 shots): 0 exceptions.
- The real saves folder: unchanged across all of it.

## Needs Nico
1. **The Bronze Age needs no bronze.** On 15 of 15 seeds the first bronze is cast after the age-up. A "Dig a Tin
   Mine" milestone was tested (+1 year; the auto-player needs a siting fix on seed 2026); a "Cast N bronze" milestone
   is the better version. Recommended: do it; it changes the age-up milestones.
2. **Needs that cost work.** Full work speed at happiness 0.85 instead of 0.6. The migrant gate done here is the
   mild version.
3. **Laws, unrest, floods and disease ask nothing.** Switching each off changed no outcome. Unrest grows with
   headcount alone (standing disputes past ~200 people, no counter). Floods are a net bonus.
4. **Research has no choices**: one order, everything known by year 12.
5. **Nothing to do after Bronze**: optional goals per age (already in design.md) or a score.
6. **A use for surplus food** (4,500-7,400 in store from year 7).
7. **Harsh is now slow rather than deadly** (about two years later than Standard, few deaths). Sharpen it or keep it?
8. **Pause while the research screen is open?** And the colour of falling-stock arrows.
9. **Hide Tent timing** (it arrives after the Thatched Hut and is built on 2 of 15 runs).
10. **The spring harvest warning** fires 40 times over 15 seeds x 30 years, true each time. Quieter, or as is?
11. **Art (not done, by rule)**: workplaces look alike at default zoom (the hover tag and badges cover it for now);
    winter snow reads as dense speckle at wide zoom (`final-bronze-end.png`).

## Deferred
Data-driven needs and traded goods (pays when Bronze content is added); splitting the test drivers out of
GameController; Burst for the per-citizen step and the hourly pass (later ages; 0.33-0.52 ms a step today); a
minimap; a HUD outside IMGUI; QA's F30-F32; sound, which nobody has heard yet.

## Incidents
- A test run overwrote the oldest real autosave slot (`autosave-1`) with a test town before the scratch-folder fix.
  The two manual saves and `autosave-3` were never touched; `autosave-2` was overwritten twice and restored from
  backup. A copy of the folder as it stood is in `Downloads\ClaudeWorkspace\Firstlight-snapshots\saves-backup-pass2`.
- Commits made by subagents carry a `Claude Opus 5.5` co-author line (the model that wrote them).
- One stray test player from the gameplay audit (`runs\pass2\build-audit\Firstlight.exe`, started 03:27) may still be
  running; ending it was not permitted in this session.
- The second working copy (`Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2`, same head) can be
  removed with `git worktree remove`.

## Honest assessment: how it feels now against the start of the pass
Better in the ways a first session shows, unchanged in the way a tenth session shows.

The first hour is the real gain. A new player is no longer stranded by the guide, is warned about winter while
there is still time, can see why a job is empty or a person unhappy, can find out who died, and is told when the
age is ready. Placing things no longer hitches, floods no longer stutter, saves are invisible, and numbers agree
with each other. Saves will survive the next build. These were all found by measurement or by reading the screen,
and each has a before and after.

What did not change is the shape of the game. The Stone Age was the good part and still is. From about year 7 the
Agricultural Age still asks little: the systems it adds are decoration, research is a list, and the slice ends on a
construction timer for a foundry that has cast nothing. The balance fixes make famine recoverable and needs worth
meeting, but they do not make the middle of the game a decision. That is design work, and it is the first six
items of the list above.

Still unverified by a person: everything about feel. The camera, the new follow and zoom keys, badge and chip
clicks, tooltips under a real mouse, readability in motion, and the sound. The windowed runs here were scripted.
