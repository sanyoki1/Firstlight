# Pass 2 batches 5 and 7: HUD cost, problems shown in the world

Worktree `Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2`, from 77b60a0. HUD and view only: no sim
rule, balance, data or save-format change; the 15 auto-test reports are byte-identical to batch 4b's.

## Batch 5: the HUD's per-frame cost
Numbers, method, the screenshot comparison and what is left: `docs/pass2/perf-hud.md`. In short, with the default HUD
OnGUI went from 1.67-1.70 ms and 43 KB a frame (944 gen-0 collections a minute) to 0.69-0.82 ms and 12 KB (186 a
minute); the research screen from 3.3-4.0 ms and 87-90 KB (3530 a minute) to 1.5-1.7 ms and 28-30 KB (462 a minute).
The targets (default under 0.6 ms / 2 KB, research under 1.5 ms / 10 KB) were not reached: what is left is Unity 6
IMGUI's own text drawing, about 130-190 bytes per label per Repaint (measured with a throwaway experiment build).

| Commit | What |
|---|---|
| e07363c | probe only: `-perfpanels` also times the Events panel (the one edit to `GameController.PerfProbe.cs`) |
| 06b43f1 | tooltips built only while the pointer is over them |
| aebefda | 15 GUIStyle variants made once in UiTheme; colour tags made once per colour |
| 81ac89e | settlement totals counted once per OnGUI call (HUD-side snapshot, same figures) |
| 7a70ad4 | `TextLine`: labels built in a reused buffer, old string kept while the text is the same; chips, feed, build bar, Jobs, Goods, Events, hover tag, placement tip |
| 23c754b | research screen: node texts once per research change, rectangles once per screen size |
| 9864389 | the needs strip measured once per call (it was once per stock shown, each counting every citizen per need) |

Screenshots (`-guideshots`, `-panelshots`, `-uishots`, seed 7, windowed, before x2 and after): 63 of 69 byte-identical;
the 5 that differ between two runs of the unchanged build differ again (camera, not HUD); the sixth is an off-screen
hover tag refreshed by real time (explained in perf-hud.md).

## Batch 7: problems shown in the world
| Commit | What |
|---|---|
| b144552 | U8c: a person's hover and selection ring red when hungry or cold, amber when sick (`SettlementView.PersonRing`) |
| 86cc015 | U8a: problem badges (`UI/Hud.Badges.cs`), P key and build-bar "Problems" button, F1 row; `-panelshots -badgeshots` driver (`GameController.BadgeShots.cs`, one line in `GameController.cs`); the plural fixes in the top bar and Jobs header |
| 1f8b595 | the rest of the singular/plural sweep (building, citizen, end and load screens) |
| e8527fb | badges sit 1.5 m over workplaces and stores, 2.3 m over homes (they floated over low workshops up close) |

### What counts as a problem (`Hud.Problem`)
Short on purpose; each is something the player can act on, and each says so in 1-3 words.
- **Site**: no builders at all ("No builders"); a material it still needs (not delivered nor on its way) with none in
  store ("No stone", "No wood": the harsh winter shot has a woodcutter site waiting for wood with none in the town).
- **Workplace**: "Flooded"; "Paused" (grey dot, a reminder); and, in a season it has work (`JobIdle` is null):
  "No tools" (status Needs tools), "No clay" (an input with none here and none in store; on its way from storage is no
  problem), "Full" (output waiting for haulers), "No trees left" (nothing left in reach): each once it has lasted
  2 game hours. "No workers" once it has lasted a whole game day, and not when its priority is Low or the season
  yields under half its usual (gatherers 0.12, fishers 0.4 in winter are left empty on purpose).
- **Campfire**: "No firewood" when its fire is out (it needs no fuel when the data says none for the season).
- **Home** (lived in): "Flooded"; "Cold" (not heated on a day that needed firewood: summer needs none, so never then);
  once the need applies, "No water" and "No faith" (both about where the home stands: build a well / shaman's hut
  near). Pots are **not** badged: they come from potters, not a place, and in the late town 80 % of homes lacked them
  at once (a badge on every hut; the top bar's red Pots figure already says it).
- **Store**: "Full" at 97 % or more.
- Not problems: idle fields and quiet-season gatherers, workers resting at night, an input on its way, empty homes,
  a home needing no fire, the town-wide food shortage (the chips and top bar carry it).
Colours: red dot for things that stop work or hurt people (missing material/input, cold, flooded, fire out), amber for
the rest, grey for Paused.

### How they are drawn
In IMGUI before every panel (so panels draw over them), on the Layout pass the buildings with a problem are projected
(`WorldToScreenPoint` of the building's top), kept if within 62 m of the camera and on screen, sorted by distance to the
screen's middle, and placed up to 30, skipping any that overlaps a panel (last frame's panel rectangles) or a badge
already placed. Each is a dark tip box, an 8 px coloured dot and the words in the small text style. Clicking one
selects the building; their rectangles join the click-through list so the click does not also go to the world.
Strings are constants or made once per resource ("No clay"), widths measured once per text: no per-frame garbage of
their own beyond IMGUI's per-label cost.

### Shots (`runs/pass2/b57/badges4`, `badges4-harsh`; earlier iterations badges1-3)
Copied to `docs/art/quarter/pass2/`:
- `p2-badges-summer-starving.png`: seed 7, year 2 summer, food for 2 days: one badge, the Elders' Fire "No workers"
  (the chip "The Elders' Fire has no elders" agrees). The first version (2-hour rule for "No workers") showed 4 here.
- `p2-badges-winter-cold-harsh.png`: seed 7 Harsh (`-difficulty 2`), winter of year 3, firewood 0: two "Cold"
  lean-tos (red), two woodcutter sites "No wood" (red), "No workers" on the toolmaker, stone gatherer, hunters. Seed 7
  Standard had no cold home in three winters (the auto-player keeps firewood), hence the Harsh run.
- `p2-badges-late-town.png`: seed 7 year 8, 95 people, default zoom: 8 "No workers" (toolmakers, hide workers,
  herbalist, potter, mining pit, stone gatherer the auto-player built but does not staff). Before dropping "No pots"
  the same view had 17, nearly every hut tagged (`badges2/07_late.png`).
- `p2-badges-late-close.png`: the same, close up.
- `p2-badges-cold-person-ring.png`: a cold woodcutter selected in the winter of year 4: red ring.
Looked at all of them: badges are legible (small text in the normal text colour on the dark tip box), sit over roofs or
just above low buildings, and do not cover the buildings. Up close a badge over a workplace whose models are spread
over its plot can sit a little off the visible workshop (anchored at the footprint's centre).

### Not done / uncertain (batch 7)
- Clicking a badge was not tried with a real mouse (nobody at the PC); it is a `GUI.Button` over the badge calling
  `Select`, as the chips do.
- No sick-person ring shot (the sick people found were in bed indoors); the amber colour is in the code only.
- The late Harsh town was empty by year 8, so no late Harsh shot.
- "No workers" in an auto-player town is common: the auto-player builds workplaces it does not staff. For a human
  it is the badge most likely to be ignored; Low priority silences it per building.

## Gate (final build `build-b57`, commit e8527fb)
- Build: `tools/unity.sh ... BuildWindows build-b57`: Succeeded, 0 `error CS` (`runs/build-b57.log`).
- `tools/gate-autotest.sh b57`: 15/15 exit 0, errs 0; every seed's `autotest-report.md` and `.json` byte-identical
  (`cmp`) to `runs/pass2/b4b/at-<seed>/` (`runs/gate-b57.out`).
- Save/load identity, seeds 2026 and 7: IDENTICAL reports, run A PASS, run B PASS (`runs/pass2/b57/slc-*`).
- Fuzz `tools/fuzztest.sh 15 1 2 3 4 5 6`: all PASS, 0 violations (`runs/pass2/b57/fuzz.out`).
- In-game: `-batchmode -seed 2026 -autoplay -simspeed 4 -quitafter 120`: exit 0, 0 exceptions
  (`runs/pass2/b57/smoke/player.log`); windowed `-smoketest` then `-smoketest -smokecontinue`: digest MATCH
  (CEC1DB7662C1727F), 0 exceptions (`runs/pass2/b57/smoke1.log`, `smoke2.log`).
- Real saves folder: listing before (`runs/pass2/b57/realfolder-before.txt`) and after all runs (`-after.txt`): identical.

## Edits outside the HUD
- `GameController.PerfProbe.cs`: one line (e07363c), the Events panel phase in `-perfpanels`.
- `GameController.cs`: one line, `-panelshots -badgeshots` starts `BadgeShots()` instead of `PanelShots()`.
- `GameController.Flow.cs`, `Game/Save/*`: not touched.
