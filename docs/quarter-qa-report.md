# Quarter-game QA report (phase 5)

Branch `experiment/quarter-game`. Independent QA of the playable slice built in phases 1-4: find real bugs, fix them at
the root, and say honestly what still holds the slice back. Updated as the work goes (so nothing is lost if the run is
cut short).

Severity: **blocker** (crash, lost save, game can't go on), **major** (wrong result a player will hit, or a dead end),
**minor** (wrong but rare or easy to work round), **cosmetic** (text, looks).

## New test tools

- `-fuzztest -seed N -years Y -outdir D` (`Sim/FuzzTest.cs`, `tools/fuzztest.sh [years] [seeds...]`): a clumsy,
  hostile player drives the settlement through the same public commands as the HUD (random valid and invalid
  placements at the map edges, in water, on deposits, overlapping, with odd rotations; demolishing sites, upgrades,
  occupied homes, full stores, every store; random priorities, pauses, focus, crop choice, laws, research queue churn;
  builders 0 / 1000 / -5; path spam; trader answers; age-ups the moment they are allowed; null commands; forced
  floods; save + load at random and at awkward moments: before the campfire, the day before and during a flood, during
  an upgrade, with a trader's offer open, after extinction). On two of three seeds the auto-player plays alongside so the
  run reaches the later systems; every third seed (`seed % 3 == 0`) the hostile player is alone. After every game day it
  checks: no exceptions or logged errors; no NaN or negative goods anywhere (stores, output piles, inputs, loads,
  carried goods); each store's total equals its contents and stays within capacity; stock totals equal the sum of the
  stores; nobody points at a removed building (home, job, errand), works two jobs, or is a working child; nobody stuck on
  the same errand at the same spot for 3 days; workers <= max; homes not absurdly overfull; nobody homeless while beds
  stand free for two days running; population = start + births + migrants - leavers - deaths; every footprint matches
  the map's occupancy, every door is reserved and no reservation is stale, every cell's walking cost equals what the
  cell is, path count matches the tiles; the research queue holds no known, duplicate or out-of-order topics and no
  knowledge is banked while something is queued; and every save -> load gives back byte-identical state.
- `-autotest -soundcheck`: measures the generated sound buffers (peak, RMS, DC offset, NaN, loop seams, worst-case
  mix).

## Findings

| # | Severity | Finding | Found by | Status |
|---|---|---|---|---|
| F1 | major | **No end state when everyone is gone.** When the last person died or left, the game went on with an empty map, a running clock and no message: a zombie session with no way on but the Esc menu. Worse, the seasonal autosave kept firing, so within three seasons all three autosave slots (and "Continue") held the dead settlement. | reading the HUD flow (`Hud.CheckEnd` only knows the Bronze Age end) | fixed: a "The settlement is empty" screen (what happened, Load a save / Main menu / Look around), the game pauses; autosave skips an empty settlement |
| F2 | major | **Haulers could freeze holding goods no store takes.** "Is there storage room?" asked for room for *any* good, so with the piles full and a granary (food only) half empty, idle people picked up wood or stone, found nowhere to put it and stood holding it: doing no other work, not going home at night, until a pile had room. Workers did the same at shift end. | code review of hauling | fixed: room is checked per good (cached once per step); haulers and workers only take goods some store accepts. Fuzz invariant "stuck carrying" added. |
| F3 | minor | Demolishing a store with no room elsewhere silently destroyed its goods. | code review | fixed: the event says how many goods were lost (as a warning) |
| F4 | cosmetic | "1 migrants arrived.", "1 people are unhappy and may leave.", "1 people have no clean water nearby." | text audit | fixed |
| F5 | minor | A dispute could put the same person in the quarrel twice (step of 7 through the worker list) and report more people than were affected. | code review | fixed |
| F6 | minor | Esc or a right-click in the middle of a path drag left the half-drawn line behind; the next time the path tool opened it showed the stale line. | reading the input code | fixed (the drag is cleared when the tool changes) |
| F7 | cosmetic | The placement tip's "brings water to N homes" (well) and "can be irrigated" (field) lines ignored the building's rotation. | reading the input code | fixed |
| F8 | minor | Sound: the clips were rebuilt (about 4 MB of samples) on every new game and every load, and the loop cross-fade was a straight blend of two unrelated noise ends (a 3 dB dip once per loop); the wind's brown noise carried a DC offset into the normalisation. | reading `SoundScape` | fixed: clips made once per run, equal-power cross-fade, DC removed; `-autotest -soundcheck` measures the buffers |
| F9 | major | **Haulers shuttled grain round in circles.** Topping up a granary reserved room only for the walk to the pick-up; once a hauler had the sack the reservation was gone, so several haulers took full sacks for the same last bit of room, found the granary full, carried the grain back to the open pile, and were sent to fetch it again: people busy all day moving the same grain (fuzz seed 16: up to 104 "stuck carrying" samples). | fuzz (`stuck carrying`, seed 16) | fixed: the pick-up takes only what still fits, and the room stays promised until the sack is put down (reservations recounted hourly include it) |
| F10 | major | **Goods with nowhere to go froze whoever held them.** With every store full, a person holding goods stood idle with them indefinitely (not working, not sleeping). Builders were among them, so a full town could never build the store that would end the jam. | fuzz (`stuck carrying`, seed 11) | fixed: with no store that takes it, the load is put down on the nearest workplace pile for that good (builders can fetch from there); lost only if there is no such workplace |
| F11 | major | **Homes filled far past their beds while others stood empty.** Babies stay with their mothers and nobody was ever rehoused, so a hut could hold 19 people in 8 beds (crowding drives sickness and unrest) next to half-empty homes. | fuzz (`overcrowded`, seed 1) | fixed: hourly, adults in an overfull home move to the nearest free bed |
| F12 | minor | Demolished stores kept their old contents in memory (the goods had been moved out or lost, but the removed building still listed them; harmless to play, wrong in saves and to any code that forgot the Active check). | fuzz (`goods in a removed store`) | fixed |
| F13 | minor | Gentle difficulty started the campfire store overfull (255 goods in 180 room), so nothing could be stored there until it was eaten down and the "Storage is nearly full" warning stood from the first minute. | fuzz (`over capacity`, Gentle) | fixed: the campfire's store grows with the gentle start's goods (x1.5) |
| F14 | cosmetic | The store panel and the drawn piles measured fullness against the base capacity, ignoring Pottery's +20% (a granary could read "330 / 300" in warning colour). | code review after F13 | fixed |
| F15 | minor | A trade whose goods did not fit in store said they had arrived (the extra was silently lost). | code review of traders | fixed: the event says how much was left behind |
| F16 | - | Auto-player: while short of food it cut builders to one even when the sites were the food (fields), so a field stood unbuilt all spring and summer and the town starved (seed 3 after the fixes above changed its timing). Strategy, not game rules. | autotest seed 3 FAIL after F2-F11 | fixed in the auto-player |
| F17 | major | **No way to choose which site is built first.** Builders always served the oldest site; when a material was short (wood in a thin-woods Stone Age) a replacement woodcutter waited seasons behind a hunters' camp for the same wood, and a player could only demolish sites to get round it. | autotest seed 99 FAIL (wood 0 for two years, 11 died in one winter) after the hauling fixes changed its timing | fixed: a build order on each site (First / Normal / Last in the site panel, the building's priority); the auto-player marks a site that makes a scarce material First |
| F18 | minor | A large interface size (160%) at 1080p or 720p made the settings and help panels taller than the screen: their Back buttons fell off the bottom. | reading the layout (panel heights vs `UiTheme.Scale`) | fixed: the scale is capped at screen height / 800 |
| F19 | cosmetic | The site panel's "on N cells of the deposit" ignored the site's rotation. | code review | fixed |
| F20 | minor | Carriers (and field workers, and instant deposits) put goods into room that others had been promised, so the others arrived to a full store and went round the stores again; some held the same load for days. | fuzz (`stuck carrying`, seeds 13, 16, 19) after F9 | fixed: puts only take unpromised room; field loads reserve room too |
| P1 | balance | **Bronze came too early** (years 10-13 on 15 seeds; the brief asked for 12-25) and the slice's end asked for no bronze at all: the foundry, tin and the metal chain were optional. | baseline auto-test | changed: Writing and Bronze Working cost 600 (were 480), and the Bronze Age needs a Bronze Foundry as a fifth milestone. Auto-player: years 11-14. A human plays slower than the auto-player, so 12-20 is the likely range. |
| F21 | cosmetic | "The river has flooded: 9 buildings are under water and 1 goods were spoiled." | `-p4shots` screenshot | fixed ("a few goods" below 1.5) |
| F22 | minor | The outbreak warning always said "Crowded homes and unclean water", also in a town where every home had water (by the auto-test's numbers the risk in a big town comes mostly from its size and from homes out of a healer's reach). The player was sent to fix the wrong thing. | `-p4shots` screenshot (Water 100 % next to the warning) | fixed: the warning names the causes that apply (crowded homes / homes without clean water / homes out of a healer's reach / the town's size) |
| F23 | minor | At a large interface size (2560x1440 at 160 %, 1280x720 at 160 %) the top bar's research box ran over the food figure. | `-shot` stills at both sizes | fixed: a narrow top bar (shorter buttons without key hints, shorter research box) when the room runs out |
| F24 | cosmetic | The new outbreak warning (F22) was cut off in its chip (chips hold about 60 letters). | the same stills | fixed: shorter wording ("Sickness could spread: crowded homes, no healer near.") |
| F25 | minor | A hint card nobody dismissed stayed up after its moment ("Winter has come" was still showing in the summer of year 12) and held back every later hint. | `-uishots` screenshot | fixed: a hint whose condition has passed (winter over, flood gone, offer lapsed) closes by itself |
| F26 | cosmetic | The hint card sat under the goals / jobs / goods / laws panels when something was selected, hiding its buttons. | `-uishots` screenshot | fixed: the card waits while one of those panels is open |
| F27 | minor | About one garbage collection a second during play (~50 gen-0 collections a minute at 3x speed, with or without the auto-player; heap flat at ~8 MB). No leak and no growth, but each collection is a possible hitch. The source was not found: batch mode cannot count per-frame allocations here (`GC.GetAllocatedBytesForCurrentThread` reads 0 on this Mono). | `-soak` | **not fixed** (needs the Unity profiler on a development build) |
| F28 | balance | Harsh: the auto-player loses seed 2026 in its first winter (cold 7, starvation 4); seeds 7 and 1234 reach Bronze in years 14 and 11. Gentle: all three reach Bronze (years 9-12). Harsh is measurably harder, possibly too hard on slow maps for a first game. | `-autotest -difficulty` | not changed (the auto-player is not tuned for Harsh; needs a human balance pass) |
| F29 | cosmetic | "Storage is nearly full" is one of the first two warnings of every Standard game (170 starting goods in the campfire's 180). It points at the right next step (a storage pile, guide step 5) but reads like a problem in the first minute. | p4 screenshot | not changed |
| F30 | minor | The tin deposit is small: 4-19 valid Tin Mine spots per map, usually more than 30 cells from the campfire. The auto-player missed it on seed 2026 (traders brought tin); a human gets the reason and the deposit hint while placing. | `-sitecheck` | not changed |
| F31 | minor | F8 (quickload) loads at once even with unsaved play (the menu's Load asks first). | reading `Hud.Keys` | not changed (instant quickload is the convention) |
| F32 | cosmetic | With the auto-player on, its "running the settlement" toast covers the builders -/+ control at the bottom right. | screenshots | not changed (auto-player only) |

## Edge states and input paths

- **Extinction**: see F1 (end screen, pause, autosave guard). Checked by the fuzz test on every hostile seed (the band
  dies out in 2-14 years, then 40 more days are played, saved and loaded in the empty settlement: no errors).
- **Campfire stage**: placements before the campfire are refused with a reason; time does not run; saving and loading
  an empty settlement round-trips (fuzz, every seed). The campfire can't be demolished; a second one can't be placed.
- **Zero storage / no tools / demolishing everything**: the hostile fuzz player pulls down every store but the
  campfire, occupied homes, staffed workplaces, sites and upgrades; with F2, F9, F10, F20 fixed no one is left holding
  goods, nothing points at a removed building, and stock totals stay equal to the stores (fuzz invariants).
- **Damaged saves** (`-autotest -savecheck`): empty file, a foreign file, header only, body cut at 10 % / 50 % / 40 bytes
  short, a flipped byte, a newer format: each is refused with a player-facing message, none throws; a file missing only
  gzip's 8-byte trailer still loads (every byte of the save is there). The title's load list lists unreadable files
  with their reason.
- **New game / load / main menu repeatedly** (`-reloadtest`, 6 scene reloads incl. 2 loads of a 156-person save):
  managed heap 6.2-6.8 MB and Unity's allocated memory 127.2-127.4 MB at every stage, object/mesh/texture counts flat,
  no native-collection leak warnings in the log.
- **Quick-load with no save**: a toast ("No quicksave yet"); no load. F8 loads without asking even with unsaved play
  (not changed: quickload is expected to be instant).
- **Hotkeys while typing**: the only text fields (seed, save name) are on menu screens, where the HUD's keys are not
  read at all (the game menu holds every key but Esc). Verified by reading `Hud.Update` / `MenuKeys`.
- **Esc order**: menu screen back > end/empty screen > research > help > tool > build list > side panels > selection >
  game menu (reading `Hud.Back`). Matches cancel placement -> close panel -> open menu.
- **Click-through**: every panel registers its rectangle (`Box`), world clicks and wheel are ignored over last frame's
  rectangles, the game menu and the research screen block the world entirely. Not driven by a scripted mouse (the
  `-uishots`/`-p4shots` helpers bypass the hover check by design); verified by reading every `Draw*` panel.
- **Path drag**: invalid cells in a dragged line are skipped (each tile is checked); see F6 for the stale drag.
- **Rotation**: footprints, doors and the ghost turn together (fuzz places every building at random turns, `CanPlace`
  and `Place` always agree; the placement-tip lines are F7).
- **Large / small interface**: F18. 1280x720 is laid out 1707 wide (`p4-topbar-1280`, checked).
- **Speed keys while the game menu is open**: ignored (the menu holds the keys; the settlement is paused).

## Content audit

- **Every building has a place on every tested map** (`-sitecheck`, 15 seeds): all 32 buildings can be placed somewhere.
  The copper and tin deposits are mostly more than 30 cells from the suggested campfire (by design: they are far
  finds), and the tin deposit is small: 4-19 valid spots for a Tin Mine per map (seed 2026: 4, all near the campfire;
  seed 1234: 7). The auto-player missed the tin mine on seed 2026 (its site search wants a free ring round a building);
  a human gets the placement reason ("must stand more on the tin deposit (n of m cells)") and the deposit hint.
- **Every building is built by the auto-player** on the 15 seeds except the Hide Tent (once; lean-tos and thatched huts
  win) and the Stone-footed Hut, which only comes by rebuilding thatched huts (it does, 8-16 times a run).
- **Goods**: every good is made and used (wild food, meat, fish, preserved and grain are eaten; wood/stone build; firewood
  burns; hides -> clothing; fibre -> clothing; clay + firewood -> pottery (homes); copper ore + firewood -> copper;
  copper + tin ore + firewood -> bronze; tools of three tiers are worn out; herbs heal). Stone tools pile up (130+ by
  year 20) once bronze tools replace them: nothing uses old tools; harmless.
- **Research**: every topic's effect is applied by code (`yield.*` by yield tag, `spoil.food`, `wear.tools`, `births`,
  `happiness`, `migrants`, `storage`, `water.radius`, `reveal.ore`, `traders`, `yield.irrigated`, `knowledge.elders`) or
  unlocks the buildings its description names. Descriptions match the numbers (Flint Tools x0.7 wear = "40% longer").
- **Laws**: the four laws apply exactly the effects their descriptions give (checked in `RebuildEffects` and the code
  reading each stat); they lapse when the council is demolished.
- **Texts**: every warning and event line was read for grammar and tone; the four wrong ones are F4/F5. All are neutral.

## Balance read as a player's diary (seeds 2026 and 7)

- Stone Age: tense and readable. Food and firewood run close to zero at the end of most winters; a winter warning
  comes in autumn. Wood is the bottleneck on thin-woods maps (seed 99 starved of wood until F17).
- Agricultural Age: once fields run, food stops being a decision (2000-6000 in store, 30-45 food days, fields left
  fallow by the auto-player). Needs reach 95-100 % within a few years; unrest settles at 11-16 %; outbreaks 4-9 in 30
  years. Clothing stays near 0 in store all game (made and worn as fast as it comes); stone tools pile up once bronze
  tools arrive.
- Research: always taken in roughly the same order by the auto-player (its plan), and every topic is known by year
  13-15, so the last third of a 30-year run has nothing left to research. That is the end of the slice, not a bug.
- Deaths come with warning: starvation follows "Food will run out", cold follows "Not enough firewood for winter";
  old age dominates later.
- Pacing changed (P1): Bronze in years 11-15 for the auto-player (was 9-13).

## Test results

All on the final code (the last commits touch only the HUD; the sim is unchanged since the last full runs except for
warning wording).

### Auto-test, 30 years, the 10 standard + 5 new seeds (all PASS, no errors or exceptions)

| Seed | Agricultural Age | Bronze Age | Final pop (min, max) | Deaths | Result |
|---|---|---|---|---|---|
| 2026 | year 6, Spring | year 15, Autumn | 159 (10, 160) | cold 14, old age 123, sickness 8 | PASS |
| 7 | year 3, Winter | year 12, Spring | 168 (12, 168) | cold 4, old age 164 | PASS |
| 1234 | year 3, Winter | year 11, Summer | 160 (12, 160) | old age 173, sickness 1 | PASS |
| 99 | year 7, Spring | year 15, Summer | 159 (8, 160) | cold 10, old age 128, sickness 4 | PASS |
| 5 | year 4, Spring | year 12, Spring | 162 (12, 162) | cold 2, old age 169 | PASS |
| 11 | year 6, Spring | year 14, Summer | 166 (12, 166) | old age 142, sickness 3, starvation 1 | PASS |
| 42 | year 5, Summer | year 12, Autumn | 168 (12, 168) | cold 11, old age 137, sickness 7, starvation 4 | PASS |
| 3 | year 4, Spring | year 11, Winter | 160 (12, 160) | old age 178 | PASS |
| 100 | year 5, Spring | year 13, Spring | 162 (12, 162) | old age 161, sickness 3 | PASS |
| 777 | year 5, Summer | year 12, Summer | 160 (12, 160) | old age 160, sickness 2 | PASS |
| 13 (new) | year 4, Summer | year 11, Winter | 163 (12, 164) | old age 154, sickness 3, starvation 3 | PASS |
| 21 (new) | year 3, Winter | year 11, Summer | 158 (12, 158) | old age 174, sickness 1 | PASS |
| 555 (new) | year 3, Winter | year 11, Spring | 160 (12, 160) | old age 185, sickness 1 | PASS |
| 8080 (new) | year 5, Spring | year 13, Winter | 156 (12, 156) | cold 1, old age 162, sickness 2 | PASS |
| 31337 (new) | year 5, Summer | year 13, Summer | 173 (12, 174) | old age 140, sickness 4, starvation 3 | PASS |

Baseline before any fix (same 15 seeds, `runs/qa/baseline-autotest.txt`): 15/15 PASS, Bronze in years 9-13. Along the
way seeds 3 and 99 failed after game fixes changed their timing; both were auto-player weaknesses (F16, F17 and the
spring-fields rule) and were fixed in the auto-player. 70-85 s per seed, 0.46-0.53 ms per sim step.

- **Determinism**: seed 2026 twice, 30 years: md and json byte-identical (baseline and final).
- **Save/load check** (`-saveloadcheck`, 30 years, save at day 483): seeds 2026, 7, 11, 42, 3, 99 all IDENTICAL, runs
  A and B PASS (baseline: 2026 and 42 at 12 years, IDENTICAL).
- **Difficulty**: Gentle 2026 / 7 / 1234: Bronze in years 11 / 12 / 9. Harsh: 2026 dies out in year 2; 7 / 1234 reach
  Bronze in years 14 / 11 (F28).

### Fuzz test (final round, `tools/fuzztest.sh 20 ...`, 28 seeds, 20 years each)

| Mode | Seeds | Result |
|---|---|---|
| hostile 2.5 % of hours + auto-player | 1 4 7 10 13 16 19 22 25 28 31 34 37 40 43 46 49 52 | 18/18 no violations (12 reach Bronze, 2 Agricultural, the rest die out) |
| hostile 8 % of hours + auto-player | 2 5 8 11 14 17 20 23 26 29 | 10/10 no violations |
| hostile alone (seed % 3 == 0) | 3 6 9 12 (earlier rounds) | no violations; the band dies out in 1-3 years, then 40 days of the empty settlement |

Every run saves and loads 5-60 times at random and awkward moments; every round trip byte-identical. Earlier rounds
(about 70 runs) found F9-F13 and F20.

### Other probes

- `-autotest -savecheck`: PASS. `-autotest -soundcheck`: wind / river / fire loops peak 0.80, RMS 0.16 / 0.18 / 0.05,
  DC 0.000, no NaN, loop seams within 2x the typical sample step; click peak 0.35, age-up tone 0.59. Worst case with
  every source at its loudest at the same instant and all volumes at 100 %: sum of peaks 1.44 (clipping only if the
  age-up tone and a click land on all three loops' peaks together); the default volumes (80 / 70 / 70 %) keep it under
  0.8. Nothing plays audibly at volume 0 (every source is volume x master x setting). Sound is off in batch mode, so
  clip lifetime across new game / load was checked by code (clips now made once per run).
- `-sitecheck`, 15 seeds: PASS. `-reloadtest`: flat memory over 6 scene reloads, no leak warnings.
- `-soak` (batch, every frame rendered to 1920x1080; Ryzen 5700X / RX 7800 XT; seed 7's 156-person Bronze town at 3x
  speed with the auto-player): 5.4-5.7 ms per frame, heap 7.5-7.7 MB, Unity memory 128.0-128.4 MB over 4 minutes. With
  400 extra people (556): 6.9-7.4 ms per frame over 10 minutes (30 minutes of 1x play), heap 8.1-8.5 MB, Unity memory
  127.4 -> 128.4 MB, no leak warnings on quit; about 50 gen-0 collections a minute (F27). (The 400 added people have no
  homes and 10 days of food each, so that town starves down to 7 by minute 9: a load test, not play.)
- `-smoketest` + `-smokecontinue` at 1280x720: campfire by click, play, save from the menu, Continue: digest MATCH.
- Screenshots re-shot and looked at (windowed `-uishots -seed 7`, 31 shots; `-p4shots`, 21 shots; `-shot` stills at
  2560x1440 and 1280x720 at 160 %; the empty-settlement screen). F21-F26 came from them; no other regressions seen.
  New stills: `docs/art/quarter/qa-empty-settlement.png`, `docs/art/quarter/qa-site-build-order.png`.

## Summary

32 findings: 6 major, 15 minor, 9 cosmetic, 2 balance (plus 3 auto-player strategy fixes needed to keep the test
honest, F16 and two under F17/seed 3). Fixed: all 6 major, 12 of 15 minor, 7 of 9 cosmetic, 1 balance change (pacing).
Not fixed: F27 (GC rate, needs a profiler), F28 (Harsh balance), F29, F30, F31, F32. No blockers: no crash or exception
in any run, saves round-trip bit-exactly under abuse, and a damaged save never crashes the game.

## Honest assessment: how playable is it for a first-time player?

It is a complete-feeling slice: a stranger can start from the title, be walked through the first year by the guide,
hover any number for what moves it, save and load safely, and play from a campfire to a Bronze Age town of about 150
in a few hours. The simulation underneath is now robust: a deliberately hostile player could not break its bookkeeping
in the final 28 fuzz runs, and the worst bugs a human would have hit (people freezing with goods in their hands,
carriers going round in circles, homes overfilling, no end when everyone died, autosaves overwritten with a dead town,
no way to choose which site gets the scarce wood) are fixed. What holds it back now is feel, legibility and the late
game more than correctness.

Top 10 things that still hold it back:
1. **Nobody has played it.** The guide, warnings and pacing were tuned against a scripted player that never hesitates;
   a first-time human will be slower and will meet the first-winter famine harder than the tests suggest.
2. **The first winter is harsh and only partly explained.** Food and firewood run near zero at the end of most Stone Age
   winters even for the auto-player; why (gathering stops in winter) is explained in a one-time hint.
3. **Little to decide late.** By year 12-15 every topic is known, needs sit at 95-100 % and food piles up; the sandbox
   after Bronze has no goals.
4. **Haul distance is invisible.** Carriers walking 50-100 cells to a store with room are only visible by following a
   person; there is no logistics view.
5. **Full storage is only half-explained**: goods are now put down on workplace piles instead of freezing people, but the
   warning says "Storage is nearly full", not where goods are piling up.
6. **Difficulty balance is untested by people**: Harsh may be too hard on slow maps (F28).
7. **Sound is unheard**: the buffers measure clean; whether it sounds good is unknown.
8. **A garbage collection every second** (F27): probably invisible at 60 fps, possibly stutter on a slower PC.
9. **Readability in motion**: hundreds of small people, bundles and piles; stills read well, motion has not been judged.
10. **Dense panels at 1280x720** (the layout is 1707 wide there): usable, tight.

Not verifiable without a human: the feel of the camera and controls (drag, edge scroll, rotation keys), whether
click-through ever happens with a real mouse at panel edges, readability in motion and at night, the sound, how long
the first winter takes to understand, and whether the guide's nine steps are the right ones.
