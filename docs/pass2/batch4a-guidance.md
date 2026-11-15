# Pass 2 batch 4a: first-hour guidance (guide, hints, warnings, events)

Worktree `Downloads\ClaudeWorkspace\Firstlight-lane2`, branch `pass2-lane2`. Text, HUD and warnings only: no data,
production or needs rule changed; the save format is unchanged (still version 2, no new HUD state).

## Step 0
- a. Rebuild `build-b4a-0` (log `runs/build-b4a-0.log`): success, 0 `error CS`. `tools/gate-autotest.sh b4a-0`: 15/15
  exit 0, errs 0; every seed's `autotest-report.md` and `.json` byte-identical (`cmp`) to
  `Downloads/Firstlight/runs/pass2/b1/at-<seed>/`, and `summary.txt` identical. The rebased tree behaves as batch 1's.
- b. `-savedir DIR` and scratch folders for every test mode, commit `afd8655`: new `Game/DataPaths.cs`;
  `SaveGame.Dir`, `GameSettings.FilePath` and the smoke digest folder go through it. Test modes (`-autotest`,
  `-fuzztest`, `-uishots`, `-p4shots`, `-guideshots`, `-shot`, `-shots` and the clip modes, `-smoketest`,
  `-smokecontinue`, `-soak`, `-perfprobe`, `-faulttest`, `-autoplay`, `-reloadtest`, `-benchmark`, `-quitafter`, any
  `-batchmode`/`-nographics` run) use `<outdir>/userdata`, else `%TEMP%\Firstlight-scratch` (fixed, so the two smoke
  stages find each other); `-savedir DIR` overrides for any launch. A plain launch, `-seed N` alone or `-load FILE`
  alone, uses the real folder. The log says where (`[Paths] ...`). `docs/how-to-play.md` tests section updated.
  Real folder backed up to `runs/pass2/b4a/savebackup/` before any run; its file list and times before
  (`runs/pass2/b4a/realfolder-before.txt`) and after all runs (`realfolder-after.txt`) are identical: unchanged.
  (Its "Smoke test.flsave" and `smoke/digest.txt` are from 05:26 today, before this batch: another run.)
  Side effect: test modes now start with default settings (guide and hints on) instead of the owner's.

## Fixes (one commit each, in order; follow-ups listed under the fix they belong to)
| # | Finding | Commit | Files | Shot |
|---|---|---|---|---|
| 1 | U1 (+U23) guide stone trap | `30ac1ef` | Hud.Guide.cs | p2-guide-stone-two-buttons, p2-guide-waiting-for-stone |
| 2 | U2 tools, U3 storage | `d4c7187` | Settlement.cs (`StorageWarning`, `ToolsWarning`), Hud.Panels.cs (2-line chips) | p2-guide-stone-two-buttons (campfire store line), p2-autumn-winter-hint, p2-status-waiting-input |
| 3 | U4 research box | `936692a` (+ `5980922` shorter text) | Hud.TopBar.cs (`KnowledgeStalled`, ETA), Hud.Research.cs | p2-guide-stone-two-buttons ("No elders: build an Elders' Fire"), p2-guide-waiting-for-stone ("No elders: Elders' Fire unfinished"), p2-autumn-winter-hint ("+3.7/day · ~3 days") |
| 4 | U6 ready to advance | `4abf441` (+ `80f20e2` cache fix) | Settlement.cs (Info event once per age), UiTheme.cs `ChipGood`, Hud.Panels.cs, Hud.TopBar.cs | p2-ready-to-advance |
| 5 | U7 + F8 winter | `185a4b3` | Settlement.cs (`FoodNeedToSummer`, warnings, winter event), Hud.Guide.cs | p2-autumn-winter-hint |
| 6 | F1 harvest outlook | `c1cfe21` + `5f7f548` (calibration) | Settlement.Agri.cs (`HarvestOutlook`, `HarvestWarning`), Settlement.cs, Hud.TopBar.cs (food tooltip) | p2-harvest-warning-staged |
| 7 | U5, U5b, U18 | `164785e` | Hud.Events.cs (new), Hud.cs, Hud.Agri.cs, Hud.TopBar.cs, Hud.Panels.cs, Hud.Guide.cs | p2-events-problems, p2-events-topics, p2-death-hint |
| 8 | U9 | `099d20c` | Settlement.cs, Hud.Info.cs (`StatusText`), Hud.Panels.cs (clickable chips, "›") | p2-chip-clicked, p2-status-waiting-input |
| 9 | U16, U17 | `548dc6f` | Hud.cs (placement tip), Hud.Info.cs (`SiteWait`, "Overall") | p2-place-short-of-stone, p2-site-builders-resting |
| 10 | U19 hints | `a4a0175` | Hud.Guide.cs | p2-events-topics (Firewood only, Jobs fill by priority) |
| - | screenshot driver | `5980922`, `80f20e2` | GameController.GuideShots.cs (new, `-guideshots`), GameController.cs, Flow.cs, DataPaths.cs | all |

Details per fix:
1. Step 6 text adds "The fire needs 8 stone: a Stone Gatherer by the rocks brings more." (the 8 is read from the data);
   a second button "Place a Stone Gatherer" while the fire is short of stone (store + delivered + on its way < cost)
   and no stone gatherer exists (sites count); progress "Elders' Fire not placed (stone 6 / 8)", "Elders' Fire:
   waiting for stone (6 / 8)", "being built (N %)", "built"; ticks only when the fire is finished (`CountBuildings`
   without sites) and Basic Tools is queued. Buttons say "Place an Elders' Fire" (article by first letter). Hide
   tooltip: "Hide the guide, in this game and in new ones. Settings > First-game guide brings it back for new games."
2. Before Basic Tools is known and with no tools in store: no chip; one Info event, once per game (kept in the saved
   `_warnSeen` under the key `~info:no tools yet`): "Workers without tools work at 60% speed. Research Basic Tools,
   then build a Toolmaker." While at least 1 tool is in store: no chip. Otherwise "N workers have no tools and none
   are in store: build or staff a Toolmaker." Storage: before any finished store of its own, "The campfire's store is
   nearly full: build a Storage Pile (Storage)." (logged as Info); later "Storage is nearly full (N of M): goods are
   waiting at workplaces. Build another Storage Pile." (", most of it food: ... or a Granary." when food > 60 %).
   Chips longer than 470 px wrap to two lines.
3. Second line of the research box in amber when nothing makes knowledge (read from the buildings, at once): "No
   elders: build an Elders' Fire", "No elders: Elders' Fire unfinished", "The Elders' Fire(s) has/have no elders";
   otherwise the bar plus "+9.7/day · ~6 days" (days = remaining cost / last day's rate). Research screen: "from N
   elders" plus the amber reason. "thinkers" is gone.
4. Info event "Everything is ready for the Agricultural Age: open Goals (G) to advance." once per age (key in
   `_warnSeen`); green chip "Ready to advance to the <Age> (G)" first in the chip column (click opens Goals); Goals
   button drawn with `ButtonOn`. Same for the Bronze Age.
5. One helper for food: `Settlement.FoodNeedToSummer()` (the old warning's formula, `DailyFoodNeed x DaysUntilSummer x
   0.7` from autumn on; before autumn a whole winter and spring ahead), used by the warning, the hint and the guide;
   firewood by `FuelNeedUntilSpring()` in all three (the warning's 0.7 threshold unchanged). In the shot the hint says
   18 firewood / 188 food and the guide shows Firewood 27 / 27 (the guide floors fuel at 20, as before) and Food
   109 / 188: same numbers. The hint id stays "winter" (an old save that saw the old hint won't show it again).
   The winter-begins event reads "Winter of year N begins: nothing grows and little is gathered until spring."
6. See the calibration section below.
7. Events panel (V; button "Events V" between Jobs and Help; F1 lists it plus "Hover any number for what moves it;
   click an event (›) to go there."). Closes the other side panels and they close it; Esc closes it. Death events
   already said who, age and cause ("Mara (31) died of cold.", "Ferra died of old age at 65.") and are Severity.Bad
   (stronger than Warning; left as they are so the report's lists don't change for them). Auto-close: a hint not
   dismissed closes after 2 game days on screen (sim time, so not while paused); "Got it" has a tooltip saying so.
   Topics regenerate a hint's text with live numbers (the winter and builders hints show today's figures).
   No new saved state: hints seen were already saved; nothing else needed.
8. Chips: food "...: more gatherers, hunters or fishers."; "There is no food left: people go hungry, then leave or
   die. More gatherers, hunters or fishers."; homeless "...: build more homes (Housing)."; sick "...: a herbalist or
   healer with herbs treats them."; unhappy "...: most lack <food / warmth / a home / health / clean water / pottery /
   faith>."; unrest "Unrest stands at N%: disputes start at 25%, theft at 30%. A staffed Tribal Council halves it."
   (or "The council is already calming it: more homes, food and faith lower it." when staffed); water "...: a well
   within reach of their homes brings it." Statuses (HUD only; the sim's strings are unchanged because the auto-player
   compares "Needs tools"): "Waiting for clay (none in store)" / "(on its way from storage)", "Needs tools (none in
   store: a Toolmaker makes them)". Clickable chips cycle camera + selection through: tool-less workers' workplaces,
   homeless / sick / unhappy people, homes without water, full stores, woodcutters (firewood), fields (harvest),
   councils (unrest), food workplaces (food chips); tooltip "Click to go to the N <things>, one at each click."
   Feed lines with a place end in "›".
9. Tip line "Not enough stone in store: builders will wait for it." (names every short material). Site panel: "Builders
   are resting; work starts at 05:15" outside work hours; "Waiting for builders: N sites are ahead of it (Build order:
   First moves it up)" (ahead = sites with a higher build order, or the same and older); "Progress" renamed "Overall".
10. Hints (each max 3 sentences, in queue order after the existing ones; one at a time; each auto-closes): "Firewood
   only" (firewood warning standing and a woodcutter exists; placed right after the winter hint), "Tools wear out"
   (a tool used up: `Consumed` of a tool > 0), "Builders" ("Only N people build...": two or more sites without a
   builder for 2 game days of work time), "Jobs fill by priority" (a workplace wanting workers at 0 for a game day
   while another is full; "Show me" goes to it), "Rebuild in place" (a home that can be rebuilt into an unlocked
   building), "Clay" (Pottery), "Tin is far away" (Bronze Working), "What the next age needs" (Agricultural Age:
   built from the age's own key research and milestones).

### Wording changed from the audit, and why
- U2: the Info line keeps the audit's sentence but the speed comes from `noToolWorkSpeed` ("60%").
- U4: "No elders: Elders' Fire unfinished" (the longer "...is not built yet" was cut off in the 228 px box).
- U9 unrest: "Unrest stands at N%: ..." (not "Unrest is N%"): the sim de-duplicates logged warnings by their first 14
  letters; with the number in them it would have logged a new event at every percent.
- U9 homeless/food/water: short remedies added (the audit gave none for these); unhappy names a need per the audit.
- U7 hint title "Winter is coming" (it is now an autumn hint); body per the audit with "Until spring the homes and the
  fire need about X firewood (in store: Y); until summer the people need about F food (in store: G)".
- F1 chip: "Expected harvest ~X from N fields (M on flax) and Y in store, against ~Z needed until autumn next year:
  sow more fields now." ("until autumn next year" instead of "before next autumn", which read as this autumn).
- U19 rebuild hint adds "The people living there stay meanwhile." (true, from the data comment).

## Harvest outlook calibration (F1)
A temporary `[Outlook]` log line (build b4a-10, removed before the final build) gave the expected harvest, store and
need on spring days 1, 4 and 7 of every year on all 15 seeds. Findings: in spring `ExpectedHarvest` assumes full
growth (~400-500 a field) and fields bring about 75-85 % of it; with the lean-season 0.7 also on the need, the first
version fired in the first farming springs and never before seed 13's crisis (exp+store/need was 1.30 in its year-14
spring). The final rule: warn in spring when expected harvest (food fields) + food in store < DailyFoodNeed x
(LeanDays + DaysPerYear), counted in full. `ExpectedHarvest` itself is untouched (the auto-player uses it).

Logged warnings per seed in the final gate (`runs/pass2/b4a-final`, years of the spring it fired):

| Seed | Times | Years | Note |
|---|---|---|---|
| 2026 | 2 | 5, 6 | first farming springs (spring food 1.5-8 days) |
| 7 | 3 | 4, 19, 20 | first farming spring; years 19-20: spring stores 1376 and 909, falling every year since year 15 |
| 1234 | 2 | 14, 15 | |
| 99 | 1 | 5 | first farming spring |
| 5 | 1 | 5 | first farming spring |
| 11 | 3 | 12, 13, 14 | the springs before its food crisis (spring stores 1417 -> 847 -> 517 -> 252 in years 12-15) |
| 42 | 0 | | |
| 3 | 1 | 4 | first farming spring |
| 100 | 1 | 5 | first farming spring |
| 777 | 7 | 5, 15-20 | summers at 4.9-7 days of food in years 14-17 |
| 13 | 3 | 5, 14, 15 | 14 and 15: the springs before its famine (food 0 in the summers of years 14 and 15; 40 starved in year 15) |
| 21 | 17 | 4, 13-19, 21-25, 28-31 | this town lives at 1.7-5.8 days of food every summer from year 13 on |
| 555 | 0 | | |
| 8080 | 0 | | |
| 31337 | 15 | 14-22, 25-30 | summers at 1.1-2.5 days of food from year 15 on |

It fires before both crises and rarely on the healthy seeds (0-3 times in 30 years on 11 of 15), but every spring on
seeds 21 and 31337, whose towns sit at 1-2 days of food each summer for a decade (they never starve because gatherers,
hunters and fishers fill the gap). The warning's statement is true there; if the owner wants it quieter, a factor of
0.9 on the need silences most of 21/31337 but then seed 13 warns only in year 15, when the famine is already
unavoidable. Left at 1.0.

## Gate (final build `build-b4a`, commit `80f20e2`)
- Build: success, 0 `error CS` (`runs/build-b4a.log`).
- `tools/gate-autotest.sh b4a-final`: 15/15 exit 0, errs 0; `runs/pass2/b4a-final/summary.txt` identical (`cmp`) to
  `Downloads/Firstlight/runs/pass2/b1/summary.txt`. (`runs/pass2/b4a` is the same run on the build before the HUD-only
  `80f20e2`: all 15 reports byte-identical to b4a-final.)
- Report differences vs the reference (seeds 2026, 13, 7 and 21 checked): only lines in "Warnings and bad events";
  every other line (seasons, agricultural systems, season detail, ages, milestones, research, actions, result) is
  identical, and the .json differs only in the warnings list. In the warnings list: "N workers have no tools" 143 ->
  14 lines (seed 2026), 157 -> 8 (seed 13); "Storage is nearly full." 7 -> 5 / 4 -> 2 (the campfire-store ones are
  Info now, not listed); unrest 3 -> 1 (seed 2026) and 1 -> 1 (seed 13): one key instead of "rising"/"high"; new "Expected harvest ..." lines (2 and
  3); every other line keeps its count with the remedy appended to its text.
- Save/load identity, seeds 2026 and 7: IDENTICAL reports, run A PASS, run B PASS (`runs/pass2/b4a/slc-*`).
- `-autotest -savecheck` with `-compatsave` (one flag per file) on v1 saves s11 day 301, 2026 day 700, 42 day 600 and
  perf/save7 day 452: all four PASS, savecheck PASS (`runs/pass2/b4a/savecheck2/player.log`).
- Older saves with a HUD block load in the player without an error: perf/save7 (v1) and a copy of the real folder's
  "Shot town" (v2, written by an earlier build), windowed `-load ... -autoplay -quitafter 40` (`runs/pass2/b4a/oldload`).
- Fuzz `tools/fuzztest.sh 15 1 2 3 4 5 6` run from the worktree: all PASS, 0 violations (`runs/fuzz-1..6`).
- In-game: `-batchmode -seed 2026 -autoplay -simspeed 4 -quitafter 120`: 0 exceptions (`runs/pass2/b4a/smoke.log`);
  windowed `-smoketest` then `-smoketest -smokecontinue`: digest MATCH (`smoke1.log`, `smoke2.log`), both in
  `%TEMP%\Firstlight-scratch`.
- Real saves folder: unchanged (file list and times identical before and after all runs).

## Screenshots
`-guideshots -seed 7` (windowed 1920x1080), three runs: `runs/pass2/b4a/guideshots1` (before fixes from review; its
hand-placed start starved under the auto-player, so the driver now restarts the seed for its second stage),
`guideshots2`, `guideshots3` (final). Looked at: all 15 of guideshots3 and the earlier runs' 01-14. Fixed from them:
the research line cut off; a chip logged as Info repeated in the feed under its own chip; the ready chip and lit Goals
button missing (cache by real time only); the age-up banner over later shots (driver). 12 copied to
`docs/art/quarter/pass2/p2-*.png` (1920x1080). `p2-harvest-warning-staged` is staged: the driver pulls fields down in
that run only to make the warning stand (the food tooltip shows the same outlook).

## Not done / caveats
- U10-U15, U20-U30 (other half of the audit) left for the later batch, as asked.
- Feed lines for warnings with a number in their first 14 letters ("27 people have...") still repeat in the feed with
  an older count under the standing chip (old behaviour; the de-duplication key is the first 14 letters).
- The help panel's long key names ("Mouse wheel, middle-drag", "Right-click, Esc ...") were already clipped before this
  batch; not touched (U20 batch).
- The research ETA uses the last day's rate, so right after a change of elders it is a day behind.
- Chip click targets are matched by the warning's wording; a reworded warning needs its matcher updated
  (`Hud.ChipTargets`).
