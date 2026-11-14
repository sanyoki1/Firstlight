# Quarter-game build log

Branch `experiment/quarter-game`. Five phases: 1 simulation core + auto-test, 2 3D view + HUD, 3 Agricultural Age
content, 4 save/load + start flow + polish, 5 QA.

## Phase 1: Stone Age simulation core and headless auto-player

Status: working. The settlement sim runs the whole Stone Age from data, and the auto-player gets to the
Agricultural Age on most seeds (see "Auto-test results").

### What exists

`Assets/Scripts/Game/Sim/` (namespace `Firstlight.Game`), plain C#, no GameObjects, deterministic per seed:

| File | Contents |
|---|---|
| `SimTypes.cs` | `Building`, `Citizen`, `SimEvent`, `MilestoneState`, enums `BuildState`, `Priority`, `Activity`, `Severity` |
| `Settlement.cs` | calendar, fixed-step `Tick`, player commands, read API, research queue/effects, ages/milestones, events/warnings |
| `Settlement.Buildings.cs` | placement rules, construction sites, storage (per-building stores, spoilage), production, feature use (trees/rock/herds) |
| `Settlement.People.cs` | citizens walking the flow fields, errands (work, haul, fetch inputs, build), job fill, homes, daily needs, births/migrants/leaving/deaths |
| `AutoPlayer.cs` | scripted honest player (only public commands), also usable in-game with F9 |
| `AutoTest.cs` | `-autotest` runner and report writer |

How it works, briefly:
- **Time**: `Tick(gameSeconds)` advances in fixed 0.25 s steps. A day is `dayLengthSeconds` (40 s at 1x), 8 days a
  season, 4 seasons a year (21 min per year at 1x). Nothing runs until the campfire is placed.
- **Goods** live in stores (campfire 180, storage pile 250). Workplaces put finished goods in their own `Out` buffer;
  workers carry a load (`carryCapacity`) to the nearest store with room when it fills, and at the end of the shift;
  unassigned people haul the rest. Processing buildings (drying rack, toolmaker, hide worker) have workers fetch
  inputs from a store into the building's `In` buffer. Builders fetch cost materials from stores to the site, then
  build. Eating, firewood, clothing and herbs are taken from stores directly (people eat at home).
- **Production** per worker per full workday = output amount x season yield x nearby fullness x `yield.<tag>` research
  effect x tool speed x happiness speed. Commuting and carrying cost real time (about 70% of nominal in practice), so
  placement matters. `consumesFeaturePer` units of work fell a tree / take one stone off a rock / take one animal from a
  herd (`GameMap.Herds[i].Pop`, which `Wildlife` draws from). Trees and herds regrow via `GameMap.DailyRegrowth`.
- **Tools** are goods: tool users take the best tier in stock (`toolSpeed`), wear it over `toolWearDays` working days
  (x `wear.tools`); without one they work at `noToolWorkSpeed`, or not at all if `requiresTools`. Builders use tools
  too (`buildersUseTools`).
- **Needs** (once a day): food (perishable first, children fed first), firewood for occupied homes and fuel for fires
  with `warmthRadius`, clothing in cold seasons, warmth = season temperature + home warmth (less if unheated) +
  clothing + a burning fire in reach; sickness from crowding/cold/hunger, treated by a staffed herbalist with herbs
  in reach; health damage and death by cause; happiness = half the average, half the worst of food/warmth/shelter/health.
  Happiness sets work speed (`workSpeedMin`..1) and people leave after `leaveAfterDays` below `leaveHappiness`.
- **Population**: ageing at `ageYearsPerGameYear`, births (fertile woman with a home, free housing somewhere in the
  settlement, food in store), migrants arriving from the map edge when there is room/food/happiness, old-age deaths.
- **Research**: knowledge from knowledge workers (elders x `elderKnowledgeMult`, x `yield.knowledge`), banked if nothing
  is queued; a queue that pulls in missing prerequisites; effects are a product per `stat` string; buildings unlock by
  `unlockedBy` and by age (a building or topic of a later age needs that age).
- **Ages**: milestones from `ages.json` (population, wintersSurvived, building) latch when reached; `AdvanceAge()` needs
  the key research and every milestone.

### Public API for phase 2 (view + HUD)

All commands are methods on `Settlement`; the auto-player calls exactly these.

Commands
- `Place(BuildingDef, int2 origin[, out reason])` -> `Building` or null. The first placement must be the town center
  (campfire); it lights the fire and the starting band and goods appear.
- `CanPlace(BuildingDef, int2 origin[, out string reason])` for the placement ghost (reason text is player-facing).
- `SuggestCampfireSite()` -> a good campfire origin (for a default / "place for me").
- `Demolish(Building[, out reason])`; `SetPriority(Building, Priority)`; `SetPaused(Building, bool)`;
  `SetFocus(Building, int outputIndex)` (multi-output workplaces, -1 = mix); `SetBuilders(int)`.
- `QueueResearch(id[, out reason])` (queues missing prerequisites first), `DequeueResearch(id)`.
- `CanAdvanceAge([out reason])`, `AdvanceAge()`.
- `SetSpeed(float)` (0 = pause, 1, 2, 4), `Update(realSeconds)` (= `Tick(realSeconds * Speed)`), `Tick(gameSeconds)`.

Read API
- Calendar: `Day`, `DayFraction`, `Season`, `SeasonName`, `DayOfSeason`, `Year`, `Temperature`, `IsWorkTime`,
  `WintersSurvived`, `Speed`, `NeedsCampfire`, `TownCenter`.
- `Buildings` (list; skip `!Alive`): `Def`, `Origin`, `Center`, `Door`/`DoorPos`, `State`, `Progress` (0..1, for the
  rising site; `MaterialsFraction`, `Work`, `Delivered[]` per cost entry), `Workers`, `Residents`, `Priority`,
  `Paused`, `Focus`, `Store[]`/`Stored` (stores), `Out[]`/`OutTotal` (goods waiting at the workplace), `In[]`,
  `Fullness`/`FeatureLeft` (nearby feature), `Heated`, `Fueled`, `Status` (short text: "No workers", "Needs tools",
  "Waiting for inputs", ...).
- `Citizens` (list): `Name`, `Female`, `Age`, `Home`, `Job`, `IsBuilder`, `Pos` (cell units: x, map y), `Facing`
  (yaw, 0 = +y), `Activity` (Idle, Resting, Walking, Working, Building, Carrying, Fetching, Sick, Playing, Leaving,
  Arriving), `CarryRes`/`CarryAmt`, `Health`, `Happiness`, `Warmth`, `Hungry`, `Cold`, `SickDays`, `Treated`,
  `Tool` (resource index or -1), `ToolLife`, `ClothingLife`. `Departing` holds people walking off the map after leaving.
- Goods: `StockTotals[]` (by resource index), `Stock(id)`, `ResIndex(id)`, `FoodStock`, `FoodDays`, `DailyFoodNeed`,
  `ToolStock`, `StorageCapacity`, `StorageUsed`, `Produced[]`/`Consumed[]` (cumulative, for rates).
- People: `Population`, `Adults`, `Children`, `HousingCapacity`, `Homeless`, `AverageHappiness`, `Births`,
  `Migrants`, `Leavers`, `Deaths` (by cause), `BuildersWanted`.
- Research: `ResearchQueue`, `CurrentResearch`, `ResearchProgress(id)` (0..1), `IsResearched(id)`,
  `AvailableResearch`, `KnowledgeBank`, `KnowledgeTotal`, `Effect(stat)`, `IsUnlocked(def)`, `UnlockedBuildings`,
  `ResearchLog` (id, day).
- Ages: `Age` (AgeDef), `Milestones` (`Label`, `Done`, `DoneDay`, `Current`, `Def.value`), `AgeLog`.
- `Events` (`Day`, `Severity`, `Text`; neutral tone), `Warnings` (current list, refreshed hourly).

### Running the auto-test

```
Build/Firstlight.exe -batchmode -nographics -autotest -seed 2026 -years 12 -outdir runs/at
```
Writes `autotest-report.md` and `autotest-report.json` to the out dir and exits 0 (PASS) or 1 (FAIL). Also works
without `-batchmode`. A 12-year run takes about 15 s. In the normal game: F8 places the campfire at the suggested
site, F9 toggles the auto-player, space pauses, 1/2/3 = 1x/2x/4x.

### Auto-test results

12-year runs (`-years 12`), 10-15 s each, all PASS:

| Seed | Agricultural Age | Final pop (max) | Births / migrants / leavers | Deaths |
|---|---|---|---|---|
| 2026 | day 140 (year 5, summer) | 60 (60) | 25 / 30 / 0 | old age 6, sickness 1 |
| 7 | day 141 (year 5, summer) | 64 (70) | 26 / 39 / 0 | cold 5, old age 8 |
| 1234 | day 149 (year 5, autumn) | 63 (63) | 26 / 34 / 0 | old age 9 |
| 99 | day 143 (year 5, summer) | 43 (45) | 24 / 25 / 0 | cold 2, old age 6, sickness 2, starvation 8 |
| 5 | day 144 (year 5, autumn) | 71 (72) | 39 / 30 / 0 | old age 6, starvation 4 |
| 11 | day 281 (year 9, winter) | 39 (40) | 15 / 22 / 0 | cold 2, old age 6, sickness 1, starvation 1 |
| 42 | day 145 (year 5, autumn) | 50 (58) | 21 / 37 / 1 | cold 1, old age 4, sickness 1, starvation 13 |
| 3 | day 152 (year 5, winter) | 60 (60) | 19 / 37 / 0 | old age 7, starvation 1 |
| 100 | day 120 (year 4, winter) | 54 (65) | 27 / 33 / 0 | old age 11, sickness 4, starvation 3 |
| 777 | day 142 (year 5, summer) | 58 (59) | 25 / 28 / 0 | old age 7 |

Same seed twice gives byte-identical reports (md and json). Seed 11 is a hard map (labour-starved, slow to grow) and
takes until year 9. `-careless` (the auto-player builds the same things but never reacts: no priorities, no pausing,
no extra food/firewood buildings, no moving worn-out producers) dies out in year 4 on seed 2026 (cold 12, starvation
5), so neglect is punished. Earlier tuning rounds also showed single winters killing 15-19 people when firewood ran
out, and settlements recovering from them.

Typical shape (seed 2026): food runs low at the end of every winter (gatherers make almost nothing in winter), the
first winter is hungry, the Elders' Fire is up by the end of year 1, Basic Tools around the first winter, Spears and
Fishing in year 2, Farming and Tribes in year 4, 25 people in year 4-5, then the age-up. Population levels off at
50-70 in the Agricultural Age with Stone Age housing (phase 3 adds more).

Perf probe: `-autotest -crowd 400` adds 400 adults and times a season: 413 citizens cost 0.26 ms per sim step
(41 ms per game day), so a few hundred people are trivial at any game speed.

### Balance changes (and why)

- Day length 24 -> 40 s and walk speed 2.0 -> 2.2 cells/s: at 24 s a commute ate a third of the workday.
- Start: 12 people (2 children, 2 elders; design says 8-12), more starting food (60 wild food + 25 meat) and wood;
  campfire store 80 -> 180 so the start goods fit.
- Gatherers 1.6 -> 4.5 wild food per worker-day; woodcutter wood/firewood 1.4/1.4 -> 2.4/2.0; stone gatherer 1.2 ->
  1.6; hunters meat 2.0 -> 3.0, hides 0.35 -> 0.4. Effective output is ~70% of nominal
  because of walking and carrying, and the early band has no tools (x0.6), so the drafted numbers starved everyone.
- Hunters use up one animal per 10 meat (was 2: herds vanished in days); stone gatherer takes one stone off a rock
  per stone made (rocks now run out as described).
- Firewood per home [0.3, 0, 0.6, 1.2] -> [0.2, 0, 0.4, 0.8], campfire fuel [0.3, 0, 0.5, 0.8] -> [0.2, 0, 0.4, 0.6].
- Builder work per day 1.0 -> 1.5 (a lean-to took most of a season).
- Wild food spoils 2.5% -> 1.5% a day (roots and nuts keep).
- Births need 3 days of food (was 5), chance 0.02 -> 0.025 a day; migrants need 5 days of food (was 8) counted
  with the newcomers included. Births need free housing anywhere (the baby stays with its mother, so homes can end
  up crowded).
- Elders' knowledge 1.0 -> 1.4 per worker-day: Basic Tools came only in year 2.
- Happiness = 40% average of needs + 60% worst need (`happinessWorstWeight`), leave threshold 0.3 -> 0.35: before,
  starving people stayed above the threshold and nobody ever left.
- Trees regrow 3 -> 12 a day map-wide (only next to existing forest).
- New balance fields: `startChildren`, `startElders`, `migrantCheckDays`, `migrantChance`, `foodWarnDays`,
  `workHaulMin`, `demolishRefund`, `focusShare`, `homelessShelter`, `happinessSmoothing`, `happinessWorstWeight`, `buildersUseTools`,
  `fuelResource`/`clothingResource`/`herbsResource` (the only resource ids the code needs), name lists.

### Tools

- `tools/compile-check.py` + `dotnet build _check.csproj -nologo -v q`: compiles all scripts in about 10 s without
  Unity (catches C# errors before a 45 s player build).
- `tools/autotest.sh [years] [seeds...]`: copies the data JSON into the built player and runs the auto-test per seed
  (balance edits can be tried without rebuilding). Reports go to `runs/autotest-<seed>/`.
- Smoke test of the drawn game: `Build/Firstlight.exe -seed 2026 -autoplay -simspeed 4 -quitafter 60` (auto-player
  plays from the start; logs a `[Smoke]` line and quits). On this PC, while nobody is at the screen, a windowed player
  only gets ~1.5 fps (also without the sim); with `-batchmode` (still rendering offscreen) it runs at ~125 fps.

### Known gaps and simplifications

- Nothing settlement-related is drawn yet (phase 2): buildings, sites, people, carried goods. The status lines in the
  old IMGUI box are the only view. Keys: F8 campfire at the suggested site, F9 auto-player, space/1/2/3 speed.
- People walk between building doors (the cell below a footprint's middle, `Building.Door`). Workers stand at the
  door while working; gatherers, woodcutters and hunters do not walk out to the trees/herds they use (the nearest
  tree / rock / herd animal simply goes). Eating, firewood, clothing and herbs are drawn from stores without walking.
- The old animated `Band` (People.cs) still runs on its own in the normal game with `AutoHunt` and kills deer through
  `Wildlife.Kill`, on top of the sim's hunters. Phase 2 should drive its visuals from the Hunters' Camp workers (or
  turn `AutoHunt` off) so herds are not hunted twice. The `-huntshots` etc. demo modes are untouched.
- Tools: a worker swaps to a better tier only when the old tool wears out; tools held by people who die or leave are lost.
- Floods do not damage buildings; buildings can't be placed on wet cells, and people route around deep water via the
  existing flow-field costs.
- No disease outbreaks, disasters, crime, laws, water/faith needs (Agricultural Age design, phase 3). Agricultural
  buildings already in buildings.json (Field, Clay Pit, Copper Mine, Smelter, Coppersmith) work through the generic
  systems: the auto-player builds Fields after the age-up and they make grain in autumn.
- Save/load (phase 4): all sim state is plain classes and lists, but the `System.Random` inside `Settlement` (and the
  map's tree regrowth draws from it) is not serialisable; phase 4 needs to save a seed + draw count or swap in a
  small serialisable RNG. `Citizen.Task*` fields are internal errand state that would need saving too.
- The auto-player is a heuristic strategy, not an optimiser. Hard maps (seed 11) take 9 years to age up.

### Notes for phase 2 (view + HUD)

- Coordinates: cell units, x = map x, y = map y (world z). `GameMap.HeightAt(x, y)` gives ground height.
  `Building.Origin` is the lower-left footprint cell, `Def.sizeX/sizeY` the footprint, `Center` its middle;
  `Def.parts` are the drafted primitive parts for the model.
- Draw a site rising with `Building.Progress` (materials 30%, work 70%); `Delivered[i]` per `Def.cost[i]` for piles.
- Citizens: `Pos`, `Facing` (atan2(dx, dy), same convention as `Band`), `Activity`, `CarryRes`/`CarryAmt` (draw a
  bundle), `Departing` for leavers walking off. Resting people stand at their home's door: hide them at night or put
  them inside.
- The HUD's placement ghost should call `CanPlace(def, origin, out reason)` every frame (cheap) and show the reason.
  `CountFeature` around the cursor can preview fullness for producers (see `AutoPlayer.FindSite`).
- `Settlement.Update(Time.deltaTime)` is already called from `GameController.Update`; `SetSpeed` drives it.
- `Warnings` are refreshed hourly and are the "current problems" list; `Events` is the log (new entries are appended;
  remember the last count you showed).

## Phase 2: 3D view of the settlement and the HUD

Status: working. The game opens on campfire placement and is playable with mouse and keyboard; the settlement is
drawn from the sim; the auto-player (F9) can still run it. Screenshots (windowed `-uishots -seed 7`) in
`docs/art/quarter/`: campfire site, build menu with ghost, a rising site with builders and a citizen panel, a
building panel, the research tree, the town at the age-up, night, winter.

### What exists

**World view** (`Assets/Scripts/Game/View/`: no GameObject per object, no per-frame garbage once buffers have grown)
- Buildings: one mesh per building type, built once from the data `parts`. Every part has a `material` (new
  `PartDef.material`; `color` is now an optional tint over it). Parts whose material is `flame` go into a separate
  glowing mesh, drawn only while the fire burns (the campfire while it has firewood; work fires while staffed), with
  a flicker. Extra data prims: `log`, `logs` (a stack), `stonering`. An earth pad under each footprint shows as a
  levelled platform on slopes; the footprint and door cell turn to trodden dirt in the terrain material map (and go
  back when the building is removed). One instanced batch per building type, so hundreds of buildings are a few draws.
- Construction sites: corner stakes and a cord; delivered materials as piles by the footprint (log stack, split wood,
  stones, hides, tools...); from 30% progress the building rises inside lashed scaffolding (the instance shader clips
  everything above progress x height; instance colour alpha 1..254 = cut height, 0 = placement ghost); the piles
  shrink as it rises.
- Storage piles show their biggest stocks as goods piles on the platform.
- Citizens: `PersonModel` pose meshes, one GPU batch per pose x sex (new `Sit` pose). Drawn positions follow the sim
  smoothly (sim steps are 0.25 s); the walk cycle follows distance moved; a load = carry pose plus the goods bundle
  on the shoulders (errands and field loads); work pose from the new `workPose` data field (`chop` = windup/release
  loop, `dig`, `bend`, `sit` round the fire, `stalk` with occasional throws, `stand`); builders hammer round the
  site; idle and homeless people sit round the campfire; children play in small circles and are drawn smaller by
  age; people resting at home are hidden indoors; leavers walk off the map. Hunters carry spears.
- Wildlife: settlement people go into `Wildlife.Threats`, so deer notice and flee walking people and stalking
  hunters; a hunter's kill (`Settlement.Kills`) leaves a carcass in front of that hunter and the herd bolts (the last
  3 are kept). The free-running animated `Band` is no longer created in the normal game (only the `-shots`,
  `-watershots`, `-deershots`, `-deerpair` clip modes get one; `-huntshots` builds its own), so nothing hunts twice.
- Day/night and seasons (`Atmosphere.cs`): sun angle and colour over the day (warm low sun; a dim blue moonlight at
  night, never black), sky/fog colour, ambient, fires glow brighter at night (`_FL_Glow`); grass/leaf/needle tints
  by season, blended over the last days of each; snow settles over the first 2.5 days of winter and melts early in
  spring, drawn in 2x2-texel clumps on upward faces (terrain, roofs, trees, rocks; not people); bushes, tufts and
  flowers are hidden under snow.
- Trees and rocks: felled trees, emptied rocks and regrowth show (the batches are re-uploaded when the map changes;
  that path no longer allocates).

**HUD** (`Assets/Scripts/Game/UI/`, IMGUI laid out on a 1080-high virtual screen and scaled to the resolution; one
dark neutral style in `UiTheme.cs`)

| File | Contents |
|---|---|
| `Hud.cs` | state; the action methods every click goes through (and `-uishots` calls); world input (hover ray over the height field, place / demolish / select, right-click cancel vs camera drag); click-through rectangles; banner; tooltips; campfire prompt; placement tip |
| `Hud.TopBar.cs` | age, date and time, speed buttons, people (adults/children/elders, beds or homeless), food days, key stocks with daily trend arrows, research progress and knowledge per day, panel buttons |
| `Hud.BuildMenu.cs` | category bar, demolish, builders -/+; category list (current-age buildings, locked ones greyed with "Needs <research>"), cost, full tooltip |
| `Hud.Info.cs` | building panel (status, description, workers, priority Low/Normal/High, pause, output focus, output per day and the factors behind it, inputs, residents and warmth, store contents, site progress per material, demolish with confirm); citizen panel (age, job, home, activity, load, health/happiness/warmth bars, needs, tool, clothing, buttons to workplace/home) |
| `Hud.Research.cs` | research screen (R): a column group per age, each topic one column right of its deepest same-age prerequisite, elbow prerequisite lines, states known / current / queued with order number / available / locked / age-locked, progress bars, key-research marker, unlocks line, tooltip with effects; click queues (prerequisites first), right-click dequeues |
| `Hud.Panels.cs` | goals (G: key research and milestones with ticks, "Queue the key research", Advance button or the reason), jobs (J), goods in store (T), help (F1, with Quit), warning chips and the event feed (click an event that has a place to go there) |

The age-up shows a banner and moves the camera to the campfire. Selection is a glowing ring; radius previews (work
reach, warmth, health) are glowing studs on the ground, for the ghost and for the selected building.

**Sim changes** (`Game/Sim/`, still deterministic: the same seed twice gives identical reports)
- Field work (`fieldWork` data flag on the gatherers' camp, woodcutter, stone gatherer and hunters' camp): a worker
  walks to a cell next to one of the 6 trees/rocks nearest its workplace door (random among them), or to a spot about
  3 cells short of the nearest herd, works there into its own load, fells / takes from that exact tree or rock
  (`ConsumeFeature(b, at)`), and carries the load back when it reaches `carryCapacity` or the shift ends: to its own
  workplace if that is nearer than a store with room (idle people haul it on, as for every workplace), else to the
  store.
- `commuteLead` (balance, 0.06 of a day): people with a job leave home that much before the shift and wait at work.
- Trees in a one-cell ring round a new building are cleared (crowns hid buildings; the yard is trodden).
- `SimEvent.HasPlace` / `Place` for events with a location; `Settlement.Kills`; warning grammar ("1 day", "1 worker").
- Yields retuned for the walking: woodcutter wood/firewood 2.4/2.0 -> 3.0/2.6, stone gatherer 1.6 -> 1.8. Walking out
  first cost woodcutters ~45% of the workday (measured); raising the yield alone barely helped because output was
  bound by time at work, hence the commute lead.

### Controls
WASD/arrows pan (also the screen edge, in a focused window only), Q/E and right-drag turn/tilt, wheel zoom,
middle-drag pan; left-click select / place (Shift: keep placing); right-click (without dragging) or Esc: stop the
tool, close a panel, clear the selection; B build menu, X/Delete demolish, Space pause, 1/2/3 speed 1x/2x/4x, R
research, G goals, J jobs, T goods, F1 help, F8 show a good campfire site, F9 auto-player, F11 hide the HUD, F6 flood
(debug). World keys are ignored while the research screen is open (it also releases WASD); clicks and the wheel over
panels never reach the world.

### `-uishots`
`tools/uishots.sh [outdir] [seed] [extra args]` runs `Build/Firstlight.exe -batchmode -screen-width 1920
-screen-height 1080 -uishots -seed N -outdir DIR`. It places the campfire through `Hud.ClickWorld`, lets the
auto-player run (moving the sim on by hand, whole hours per frame), and saves PNGs: campfire site, early camp, build
menu with ghost and radius, invalid placement, construction site, building panel, citizen panel, research tree with a
tooltip, goals, age-up banner, town close and wide (~40 people), jobs, goods, help, hunters, night, winter; then a
frame-time probe (with `-crowd N` extra people, default 400; `-perfsplit` adds people-hidden / no-shadow / paused
probes).

Found the hard way:
- In batch mode the player neither renders cameras nor calls OnGUI, so `ScreenCapture` saves black frames. Shots go
  through a 1920x1080 RenderTexture: in batch mode the camera is rendered explicitly with
  `RenderPipeline.SubmitRenderRequest` (world only, no HUD); windowed, the camera targets the texture for that frame
  and OnGUI draws the HUD into it, read back at the end of the frame.
- So screenshots with the HUD need a windowed player: `Build/Firstlight.exe -popupwindow -screen-width 1920
  -screen-height 1080 -uishots -seed 7 -outdir DIR` (about 4-5 minutes: a windowed player nobody watches runs at
  ~1.5 fps, so the mode uses few, large frames on a fixed 10 fps game clock). Batch and windowed runs of a seed take
  the same path. Seed 2026's -uishots path starves out in year 8 (see known gaps), so the docs shots use seed 7.

### Performance
Batch mode, rendering into a 1920x1080 target every frame (Ryzen 5700X + RX 7800 XT): town of ~40 people about
4.2 ms per frame; the same plus 400 people about 6.0 ms; drawing 400 extra people costs ~1.9 ms CPU (pose choice,
smoothing, packing); no gen-0 collections during the probes. A first probe read 150 ms: that was the GPU throttled
while nobody is at the PC, so numbers vary between runs.

### Auto-test after phase 2
`tools/autotest.sh 12 2026 7 1234` PASS (also 99, 5, 42, 3, 100, 777); the same seed twice gives byte-identical
reports. The Agricultural Age now comes in years 3-5 (was 4-5). Seed 11 (the hard map: year 9 in phase 1) now dies
out in its second or third winter; it is labour- and wood-starved and the extra walking tips it over.

### Known gaps and simplifications
- Hunting is the simple version: hunters walk out to a spot short of the herd, stalk and throw in place, and carry
  meat home; a kill leaves a carcass in front of them. The animated `Band` hunt (stalk, real spear flight, two-man
  pole carry) is not driven by the Hunters' Camp.
- No building rotation (the door is always on the -y side; rotation needs the sim's door rule to change).
- Tree canopies hide people working in thick forest (gatherers, woodcutters); there is no cut-away or outline.
- Workers at a workplace door stand in a short row; elders sit round their fire; nobody goes inside buildings.
- The auto-player's Agricultural Age is fragile: small timing differences (as in -uishots) can end in a starving
  collapse in years 7-9 (seed 2026). Phase 3's farming and granary content should be checked against this.
- No point lights: night is moonlight plus glowing flames (no pool of light on the ground).
- The default IMGUI font (14-24 px at 1080p): fine at 1080p, tight at 720p.
- Save/load and a main menu are phase 4. Esc no longer quits the game (Help > Quit).

### Notes for phase 3 (Agricultural Age content)
- A new building is data: add it to `buildings.json` with `parts` (each with a `material`), a `category` (an existing
  one, or a new one: the build bar picks it up), a `workPose`, and `fieldWork: true` if its workers should walk to a
  nearby feature (`FieldSpot` handles trees, rocks and wildlife; fields would want a "work inside the footprint"
  variant). Flames: material `flame`. New primitive shapes go in `BuildingMeshes.Prim`.
- New materials: a painter function plus a `MATERIALS` entry in `tools/textures/make_textures.py`, run with
  PixelForge's venv; it writes the PNGs and `materials.json`; the import settings are automatic.
- Buildings whose look changes with state (a field's crop by season, a well's water) can add a per-instance choice in
  `SettlementView.DrawBuildings`, the way fires add their glow mesh (e.g. one mesh per growth stage).
- Panels: `Hud.Info.DrawBuilding` dispatches on what a building has (workplace / home / store); add a section the same
  way for new mechanics (laws, faith, disease), and a new screen as another `Hud.*.cs` partial file.
- New sim warnings show as chips automatically; events logged with `Log(severity, text, at)` are clickable.
- Research effect text: `Hud.Research.EffectText` formats the known stat prefixes; add new prefixes there.
