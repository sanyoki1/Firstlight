# Firstlight: how to play (quarter-game slice)

A settlement from its first campfire to the edge of the Bronze Age. You place buildings and set priorities;
the people decide who walks where. Everything else (who works, who hauls, who builds) follows from that.

## Launching

- Run `Build/Firstlight.exe`. The title screen comes up over the generated land, turning slowly.
  - **Continue** loads the most recent save (manual, quick or autosave).
  - **New game**: a map seed (the same seed always makes the same land; *Random* picks one) and a difficulty:
    - *Gentle*: x1.5 starting goods, milder winters (+0.08 warmth), x0.85 firewood use, x1.15 yields.
    - *Standard*: the intended balance (what the auto-test plays).
    - *Harsh*: x0.7 starting goods, colder winters (-0.05), x1.15 firewood use, x0.9 yields.
  - **Load**: every save with its age, year, season, population and the real date it was saved.
  - **Settings**: window/fullscreen, resolution, vsync, frame cap, interface size, edge scrolling, camera speed,
    autosave, the first-game guide, hints, and sound volumes. Kept in `settings.json` next to the saves.
- Saves live in `%USERPROFILE%\AppData\LocalLow\<company>\Firstlight\saves` (Unity's persistentDataPath, or the
  folder given with `-savedir DIR`; test modes use a scratch folder, see the tests below; the load
  screen shows the exact folder).

## Controls

| Key / mouse | What it does |
|---|---|
| W A S D, arrows, screen edge | Move the camera |
| Q / E, right-drag | Turn and tilt the camera |
| Mouse wheel, middle-drag | Zoom, pan |
| Left-click | Select a building or person; place what you are building |
| Shift + left-click | Place several of the same building |
| `,` and `.` | Turn the building you are placing (its door turns with it) |
| Right-click, Esc | Stop placing; close a panel; clear the selection |
| Esc (nothing open) | Game menu: resume, save, load, settings, main menu, quit |
| B | Build menu |
| X, Delete | Demolish tool (also removes path tiles) |
| Space | Pause / resume (you can still build while paused) |
| 1, 2, 3 | Speed 1x, 2x, 4x |
| R | Research tree |
| G | Goals for the next age |
| J | Jobs overview |
| L | Laws (once there is a Tribal Council) |
| T | Goods in store |
| F1 | Help |
| F5 / F8 | Quicksave / quickload |
| F9 | Let the auto-player run the settlement (again to take it back) |
| F10 | Cinematic view: no interface, wide-screen bars, soft focus away from the middle and a slow turn round the view. Space pauses; F10 or Esc leaves it |
| F11 | Hide the interface |
| Tab | While choosing the campfire site: show a good site |

The game saves itself at the start of every season (the last three autosaves are kept; Settings can turn it off).

## The first ten minutes

The **First steps** card at the bottom left walks through these; each step ticks itself off when it is done, with
a sentence on why. It can be hidden, and turned back on in Settings.

1. **Place the campfire.** Look for open ground with woods, loose rock, the river and game within reach (Tab shows a
   good site). The band of 12 settles around it with a few days of food and some wood.
2. **A lean-to** (Housing) near the fire: people sleeping in the open lose health on cold nights.
3. **A gatherers' camp** in reach of woods: steady food (roots, nuts, berries) through spring to autumn.
4. **A woodcutter**: wood for building and firewood for winter. It can be set to firewood only in its panel.
5. **A storage pile**: the campfire holds little; goods with nowhere to go spoil in the workplaces' piles.
6. **The Elders' Fire**, then queue **Basic Tools** in the research tree (R): it unlocks tools, spears, mining and
   farming. More elders on the fire research faster.
7. **Prepare for winter.** Winter makes almost nothing. Before it: firewood for every home and the campfire until
   spring, and food for the lean days. Raise a building's priority (its panel) to push it.
8. **A toolmaker** once Basic Tools is known: tool users work faster; fields and mines need tools.
9. **Goals (G)** list the key research and milestones for the Agricultural Age. From there it is your settlement.

One-time hints come up the first time something new happens: the first winter, a first death, a new need (clean
water, pottery, faith), a first outbreak, laws, ore found, a river flood, the first traders.

Things worth knowing:
- Hover any number in the top bar for what it is and what moves it. The second row holds the key goods and, once
  they apply, the needs (water, pottery, faith) and unrest.
- **Spring floods**: some springs the river rises (a warning comes the day before). Buildings on the low ground by the
  river stop working while under water and their stored goods partly spoil; flooded fields yield 25% more that year.
- **Traders** (after Barter) call in spring, summer and autumn with an offer: accept or decline, or tick
  *Always accept*. An offer stands until the end of the next day.
- **Paths** (Agricultural Age) cost half a stone per tile: press, drag and release to lay a line.
- **The end of the slice** is the Bronze Age: the key research (Writing, Bronze Working) and the milestones (80
  people, a Mining Pit, a Smelter, a Granary and a Bronze Foundry). The settlement can be played on afterwards.
- **If everyone dies or leaves**, the game stops and says so; load a save (the autosaves are not overwritten with
  the empty settlement) or start again from the main menu.

## Storage

Every store (the campfire's stash, Storage Piles, Granaries) has settings. Select it: its panel shows a **Priority**
(Low / Normal / High), what it takes in a line, and **Storage settings**, **Copy** and **Paste**.

- **Storage settings** opens a checklist of the goods the store can hold, grouped as Food, Materials, Fuel, Tools and
  Goods (a group's box ticks or clears the whole group; *All* / *None* do every good). A Granary only lists food.
- **Keep at least N** (per good): new goods of that kind come here first, haulers bring it from other stores until
  there are N, and nobody takes it below N while another store has some. Once every other store is out, people do use
  it (no one starves or freezes beside a full reserve). Traders never take it.
- **Keep at most N** (per good): no more comes in, and anything above N is moved out.
- **Priority**: new goods go to the highest-priority store that takes them, haulers move goods up from lower-priority
  stores, and eating and burning take from low-priority stores first. A good the store no longer takes is moved out.
- **Copy / Paste** copies a store's goods, priority and limits onto another (a Granary keeps to food).
- **Haulers** (bottom bar, next to Builders; 1 at the start) carry goods between stores as the settings ask, and goods
  waiting at workplaces to the stores. Like builders (kept only while there are sites), they are kept only while some
  store's settings ask for goods to be moved; otherwise they do their usual jobs ("not needed"). They are filled after
  high-priority jobs and builders and are never taken by them. People without a job haul too.
- A store left as built takes everything it can hold at Normal priority with no limits, which is how storage worked
  before settings. If a good is shut out of every store, a warning says so (it piles up where it is made), and the
  **Goods in store** panel (T) names it in red; hovering a good there shows which stores hold it.
- Number fields: type, then Enter, Tab or click elsewhere to set it (blank = no limit); Esc drops what you typed.
  While a field has the keyboard, the game's keys wait.

## The auto-player and the tests

- **F9** in a game lets the scripted auto-player run the settlement (it uses only the player's commands); F9 again
  hands it back.
- Head-less auto-test, 10 seeds, 30 years each (Git Bash): `tools/autotest.sh 30 2026 7 1234 99 5 11 42 3 100 777`.
  Reports go to `runs/autotest-<seed>/autotest-report.md`; PASS needs the Bronze Age and no errors.
- Save/load check (save at a mid point, load it, both runs must write byte-identical reports):
  `Build/Firstlight.exe -batchmode -nographics -autotest -saveloadcheck -seed 2026 -years 30 -outdir runs/slc-2026`.
- Fuzz test (a clumsy, hostile player with invariant checks after every game day; see `docs/quarter-qa-report.md`):
  `tools/fuzztest.sh 15 1 2 3 4`. Store settings at work (shut out, keep at most / least, priority, copy / paste, the
  warning, save): `-autotest -storagecheck -seed N [-storageyears Y]` (writes `storage-check.md` to `-outdir`).
  Other QA probes: `-autotest -savecheck`, `-autotest -sitecheck -seed N`,
  `-autotest -soundcheck`, `-load SAVE -soak MINUTES [-crowd N]`, `-reloadtest`, `-autotest -difficulty 0|1|2`.
- Screenshots: `-uishots` (phases 2-3, see the build log) and `-p4shots -load <save>` (phase 4: menus, guide, hints,
  traders, path dragging, night, canopy cut-away, floods, top bar at two sizes), both run windowed.
- Smoke test of the default launch path: `Build/Firstlight.exe -smoketest` (menu, new game, campfire, a minute of
  play, save, quit), then `Build/Firstlight.exe -smoketest -smokecontinue` (menu, Continue, checks the loaded state
  is exactly the saved one).
- **Test runs never touch the real saves.** Every non-interactive mode (`-autotest`, `-fuzztest`, `-uishots`,
  `-p4shots`, `-shot`, `-shots` and the other clip modes, `-smoketest`, `-soak`, `-perfprobe`, `-faulttest`,
  `-autoplay`, `-reloadtest`, `-quitafter`, anything in `-batchmode`) keeps its saves, autosaves, quicksave,
  `settings.json` and the smoke-test digest in a scratch folder: `<outdir>/userdata` when `-outdir` is given, else
  `%TEMP%\Firstlight-scratch` (one fixed folder, so `-smoketest` and `-smoketest -smokecontinue` find each other).
  `-savedir DIR` puts them in DIR instead (for any launch, a person's too). Only a plain launch (no test mode; `-seed N`
  or `-load FILE` alone count as plain) reads and writes the real folder. The player log says where they went
  (`[Paths] ...`). A scratch folder starts with default settings (guide and hints on).
