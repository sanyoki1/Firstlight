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

## Phase 3: the Agricultural Age

Status: working. The slice plays from the campfire to "The settlement enters the Bronze Age" and an end screen; the
auto-player gets there on 10 of the 10 test seeds (see "Auto-test"). Screenshots `docs/art/quarter/agri-*.png`.

### What exists

**Sim** (`Game/Sim/`, new partial files; still deterministic: the same seed twice gives byte-identical reports)

| File | Contents |
|---|---|
| `Settlement.Agri.cs` | fields through the year (`CropWork`, `DailyCrops`, `ExpectedHarvest`, `StepIntoField`), seasonal field labour (`WantsWorkers`), granary restocking (`MoveStockDecide`), traders (`TraderVisit`) |
| `Settlement.Society.cs` | needs that arrive with the age (`NeedActive`, `RefreshHomes`, `DailyGoods`, `ExtraNeeds`, `NeedMet`), health tiers (`HealthCover`), laws (`SetLaw`, `CanSetLaw`, `LawActive`, `LawsInForce`), unrest (`DailyUnrest`: disputes, theft), outbreaks (`RefreshOutbreakRisk`, `DailyOutbreak`) |
| `Settlement.Buildings.cs` | + deposits (`DepositCells`, `onDeposit` placement rule), paths (`PlacePath`, `RemovePath`, `CanPlacePath`), upgrades in place (`UpgradeOption`, `CanUpgrade`, `Upgrade`, `CancelUpgrade`), store `Capacity` (x `storage` effect) and `Accepts` (granary: food only), `NearestStorageFor` (spoiling goods prefer the granary) |
| `AutoPlayer.Agri.cs` | the auto-player's Agricultural Age (below) |

Mechanics and numbers (data files unless noted):
- **Fields** (`crop`, 4x4, 3 farmers, need tools): spring = sowing (`sowWork` 3 worker-days per field); growth per day
  x share sown: spring `growthSpring` 0.07, summer `growthSummer` 0.09 x (0.65 untended .. 1 tended; `tendWork` 1.5
  worker-days a day); autumn = harvest (`harvestWork` 4) into the farmers' loads: full yield 400 grain (or 120 flax
  fibre) x growth x research (`yield.farming`) x irrigation (`yield.irrigated` 1.35 if water within
  `irrigationRadius` 10); winter: nothing, and fields want no workers (farmers haul and build). The crop
  (`exclusiveOutputs`, chosen with `SetFocus`) is fixed at sowing; fibre needs Weaving (resource `unlockedBy`). Farmers
  walk to the field's door and then step about among the rows (one path target per field). In sowing and harvest a
  High field outranks other High jobs (`Tier` -1: it may take their people). Grain is carried in sacks (resource
  `carry` 3: a load is 15).
- **Granary**: holds 300 (x1.2 with Pottery), food only (`accepts`), spoil x0.3; idle haulers move spoiling food from
  open stores into it (`Task.MoveStock`).
- **Housing**: Thatched Hut 8 / warmth 0.85; Stone-footed Hut 10 / 1.0 (Masonry). `upgradesTo`: lean-to and hide tent
  -> thatched hut -> stone hut, same footprint; the building keeps housing its people while builders bring the new
  building's full cost and build it (`Building.UpgradeTo`, `UnderWork`, `BuildDef`). Crowding now counts residents above
  max(4, 75% of the beds).
- **Needs** (balance `needs`): water from the Agricultural Age (home within `riverWaterRadius` 7 of the river, or a well's
  `waterRadius` 10 x `water.radius`; unmet: happiness capped at 0.6, sickness +0.004/day, work speed x0.9), pottery
  once Pottery is known (each occupied home uses one, lasting `goodsWearDays` 48; unmet 0.7), faith once Shamanism is
  known (home within a staffed shaman's `faithRadius` 22; unmet 0.65). Needs join the happiness average and the
  worst-need term.
- **Health tiers**: buildings carry `healChance` / `healDamage` (herbalist 0.5/0.4 radius 16, healer's hut 0.35/0.25
  radius 24); the best one in reach applies (with herbs in store).
- **Laws** (`laws.json`, need Tribal Law + a finished council; suspended without one; each changeable once every
  `lawCooldownDays` 4): Food Sharing (`law.foodsharing`: equal shares when short; `food.eaten` x1.08), Elders Settle
  Disputes (`unrest` x0.6, `knowledge.elders` x0.8), Shared Labour (`build.speed` x1.35, `happiness` x0.94), Rationing
  (`food.eaten` x0.85, `happiness` x0.9, `sickness` x1.25). Law effects join the research effects (`RebuildEffects`).
- **Unrest** (from `unrestAge`): target = pop/400 + 0.8 x share unhappy (<0.5) + 0.5 x crowded share + 0.15 x share
  without faith, x `unrest` effects, x (1 - 0.5) with a staffed council; smoothed 0.15/day. Warned at 20%; above 25% a
  daily chance of a dispute (2-4 workers lose 2 days), above 30% of a theft (8-20% of one store's most valuable good,
  at most 40).
- **Outbreaks** (from 30 people): daily chance 0.003 x (1 + 3 x share in crowded homes + 4 x share without water +
  pop/100) x (1 - 0.6 x share covered by a healer with herbs); warned at 1.5%. It starts in the worst home; people in
  homes within 6 cells fall ill with chance 0.55 (x1.3 without water, x the healer's chance in reach), longer
  untreated.
- **Traders** (Barter, stat `traders`): day 2 of spring, summer and autumn; want = the traded good furthest below its
  target (tools: adults x0.35, clothing: pop x0.3, pottery: homes x0.5, herbs 4, tin ore 12 once Bronze Working is
  known); give = the biggest surplus by `value`; worth 40, at 75%.
- **Metal chain**: Prospecting reveals copper/tin (map ground stained, outcrops drawn); Clay Pit, Copper Mine and Tin
  Mine must stand on their deposit (`onDeposit`, `depositMin`; reason text and a deposit hint while placing). Tin Mine,
  Bronze Foundry and Bronze Smith moved to the Agricultural Age (Bronze Working is researched there). Workers swap up
  to a better tool tier when one is in stock (the old one goes back to store if more than half its life is left).
- **Paths** (`dirt_path`, `path: true`): a map layer (`GameMap.Path`), not buildings; free and immediate; walking cost 1
  vs 2 for ground (trees 4, fields 3, shallow water 8) and 35% faster (`pathSpeed`); buildings can be placed over them.
- **Animal Pen**: meat 2.4 + hides 0.45 per herder-day, needs 25+ open grass cells within 6 (`nearby: open`).
  **Potter**: clay + firewood -> pottery. **Weaver**: flax fibre -> clothing.
- **Research effects** now: Irrigation +35% irrigated fields; Pottery storage +20%; Masonry stone huts + wells +50%
  reach; Barter traders; Writing knowledge +15%; Tribes migrants +30%; the rest unlock buildings (descriptions fixed to
  say what they do).
- **Ages**: Agricultural milestones 80 people, Mining Pit, Smelter, Granary; key Writing + Bronze Working. Reaching the
  Bronze Age (the last age in the data) is the end of the slice.
- **Famine**: hunger harms in proportion to what was not eaten; people hungry 3+ days in a row may leave (10%/day) to
  find food elsewhere, and the last adult of a household takes its children. **Food planning** (from
  `planFoodAge`): births need food for 25% and migrants 50% of the days to the next autumn (`LeanDays`).
- **Builders** fetch a material from a workplace's output pile when no store has it (a full campfire store no longer
  blocks the first storage pile).

**View** (`Game/View/`): fields from `cropStages` (bare, shoots, green, ripe, stubble; flax its own colour; the
harvest cuts row by row: ridged prism rows, `BuildingMeshes.Crop`); upgrades show scaffolding and their materials
round the old building; copper/tin ground stains and outcrops after Prospecting (`DrawOres`); clay beds as clay ground
from the start (`WorldView.LayerForCell`); paths as trodden earth (`SyncPaths`); deposit hints and path previews as
studs; new pile kinds (sacks, pots). New materials (`make_textures.py`): shoots, crop_green, stubble, flax, path,
stone_wall, pottery, cloth, ore_copper, ore_tin, metal, daub.

**HUD** (`Game/UI/Hud.Agri.cs` + edits): field section (stage, sown/grown/harvested, expected harvest, irrigation,
crop choice), "Rebuild as..." button with cost and progress / stop, coverage lines for wells, healers, shamans and the
council (radius and homes in reach; radius studs: water blue, faith violet, irrigation), deposit cells for mines,
household needs in the home panel, needs in the citizen panel, needs + unrest in the top bar (only once they apply), a
Laws screen (L), path drag-placement (press, drag an L-shaped line, release; X/Delete tool removes path tiles), new
build categories Crafts / Metal / Paths, research effect texts for the new stats, and the end screen ("You have reached
the Bronze Age", years taken, people, born/arrived, left/died, buildings, research, winters, food; Keep playing).

### Auto-player (Agricultural Age)
Fields sized to the people (a share of the year's food, 0.6 to start, +0.1 after a hungry spring/summer, -0.1 after a
summer with 15+ days of food; per-field yield measured from the last harvest; capped at 40% of the adults / 2),
granaries by population and fullness, thatched huts (rebuilds when wood allows), a well where 2+ homes have no water,
healer, council (laws: Elders Settle at unrest 12%, Food Sharing in a moderate shortage, Rationing in a deep one,
Shared Labour with 3+ sites), shaman, clay pit + potter, more elders, mining pit, copper mine, smelter, coppersmith,
tin mine, foundry, bronze smith, pens, a weaver with flax fields, paths from busy buildings to the campfire. Fields
High in sowing and harvest; council, shaman and healer High when unrest, faith or outbreak risk call for it; a year
and a half of food in store leaves fields fallow. Stone Age fixes: roofs before winter, a second gatherers' camp when
the first is in thin woods, a storage pile before the campfire store fills, a maker of any material a site waits for
and nobody makes, firewood focus in autumn before building, woodcutters relocated farther out when the near woods are
gone, no age-up on the edge of a hungry winter, housing only while food keeps up (and the town stops growing near 150:
its plan, not a rule).

### Auto-test
`tools/autotest.sh` now defaults to 30 years; the report has an "Agricultural systems" table (fields and growth,
harvest, grain, pottery, clay, ore, copper, tin, bronze, tools held per tier and stock, water/goods/faith met,
unrest, outbreaks, thefts, disputes, trader visits, path tiles, laws in force) and PASS needs the Bronze Age.
`player.log` also gets a line per game year and, after the report is written, a "sim alone" timing of two more days.

30-year runs (all PASS; the same seed twice gives byte-identical md and json):

| Seed | Agricultural Age | Bronze Age | Final pop (min, max) | Births / migrants / leavers | Deaths | Outbreaks / thefts / disputes | Trader visits | Rebuilds | Tool swaps | Result |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026 | year 5, Spring | year 13, Summer | 166 (min 8, max 168) | 161 / 144 / 9 | cold 4, old age 124, sickness 5, starvation 9 | 9 / 0 / 1 | 45 | 16 | 261 | PASS |
| 7 | year 4, Winter | year 11, Autumn | 162 (min 12, max 162) | 196 / 129 / 0 | old age 174, sickness 1 | 9 / 0 / 0 | 51 | 9 | 355 | PASS |
| 11 | year 9, Summer | year 18, Autumn | 164 (min 8, max 164) | 149 / 118 / 4 | cold 10, old age 94, sickness 3, starvation 4 | 10 / 2 / 4 | 41 | 10 | 248 | PASS |
| 1234 | year 4, Spring | year 11, Summer | 155 (min 12, max 156) | 196 / 115 / 0 | old age 165, sickness 3 | 13 / 0 / 0 | 59 | 10 | 354 | PASS |
| 99 | year 5, Summer | year 13, Autumn | 160 (min 8, max 160) | 189 / 120 / 3 | cold 4, old age 151, sickness 3 | 10 / 0 / 0 | 51 | 16 | 338 | PASS |
| 5 | year 4, Autumn | year 11, Summer | 158 (min 8, max 158) | 200 / 124 / 0 | cold 4, old age 170, sickness 4 | 6 / 0 / 0 | 62 | 11 | 299 | PASS |
| 42 | year 7, Summer | year 20, Summer | 160 (min 6, max 160) | 142 / 126 / 13 | cold 2, old age 90, sickness 2, starvation 13 | 7 / 0 / 0 | 36 | 9 | 47 | PASS |
| 3 | year 3, Winter | year 11, Spring | 160 (min 11, max 160) | 209 / 120 / 0 | old age 178, sickness 3 | 10 / 0 / 0 | 65 | 8 | 352 | PASS |
| 100 | year 5, Spring | year 12, Winter | 162 (min 11, max 162) | 178 / 139 / 0 | old age 165, sickness 2 | 8 / 0 / 0 | 60 | 9 | 333 | PASS |
| 777 | year 5, Summer | year 12, Spring | 162 (min 12, max 162) | 217 / 142 / 0 | cold 27, old age 173, sickness 9 | 6 / 0 / 1 | 63 | 9 | 339 | PASS |

Deaths are mostly old age once the fields are in; the hard maps (11, 42) are slow in the Stone Age (Agricultural in
year 7-9) and reach Bronze in years 18-20. Before this phase's fixes seed 11 died out in its second winter; along the
way most seeds collapsed after the age-up (see "Balance changes").

**Performance**: a town of 160 costs 0.3-0.55 ms per sim step with nothing drawn (two days timed after the run:
citizens ~0.2-0.4 ms, hourly work ~0.12 ms per step averaged), 0.55 ms per step with the auto-player thinking hourly;
at 4x speed that is ~0.15 ms per 60 fps frame. Drawn (batch `-uishots`, seed 7, 1920x1080 render target, sim at 1x): the Bronze-age town of 132 with 97 buildings and 348 path tiles takes 4.4 ms per frame; with 300 extra people (432) 6.0 ms; no gen-0 collections during the probes.

### Balance changes (and why)
- Fields 2.4 grain/worker-day (fewer per worker than gathering) -> a real seasonal crop yielding 400 per field: at
  first fields fed fewer people per worker than gatherers and the town starved every spring.
- Grain `carry` 3: with 5 per trip, carrying the harvest to the granary was the bottleneck (a 4x4 field needed 80
  trips) and grain rotted in the fields' piles while people starved.
- Builders take materials from workplace output piles: seeds 2026/11/3 deadlocked with a full campfire store.
- Births/migrants plan for the lean months in the Agricultural Age (not the Stone Age, where it stalled growth at
  ~23 and kept seed 42 from ever ageing up): towns grew in autumn on a full granary and starved by summer.
- Famine leaving (with children): famines used to kill everyone at once (Food Sharing made all hungry together).
- Tool handout fix: a worker only tried the best tier; with that tier scattered over stores in part-tools it got none,
  while 150 stone tools sat in store and the fields failed for want of tools (the "slow decline after Bronze").
- Agricultural research costs x1.6 (key research x2.4): Bronze came in years 8-9.
- Clean water unmet 0.5 -> 0.6, toolmaker 1 -> 2 workers and 0.6 -> 0.7 tools: the age-up itself caused a happiness
  crash and the tool supply could not keep 100+ workers in tools.
- Woodcutters relocate up to 18 cells farther when the woods near town are gone (towns froze in year 20+ winters).
- Path performance: tree felling/growth no longer invalidates every cached path (soft commit each season), the
  path cache holds 512 fields (was 192), doors are re-pathed in one parallel batch after a change, and farmers use
  their field's door as their only path target: the sim cost of a 160-town fell from ~2.7 to ~0.4 ms per step, and
  late years no longer slowed to a crawl (seed 3 took 113 s for one game year before).

### New data fields
- `BuildingDef`: `upgradesTo`, `accepts`, `onDeposit`, `depositMin`, `path`, `crop`, `exclusiveOutputs`, `sowWork`,
  `harvestWork`, `tendWork`, `irrigationRadius`, `cropStages` (`name`, `material`, `height`, `materials` per crop),
  `waterRadius`, `faithRadius`, `healChance`, `healDamage`, `orderPower`, `enablesLaws`.
- `ResourceDef`: `value`, `traded`, `carry`, `unlockedBy`.
- `Balance`: `needs` (`id`, `name`, `age`, `research`, `unmet`), `goodsResource`, `goodsWearDays`,
  `riverWaterRadius`, `noWaterSick`, `noWaterWorkSpeed`, `pathSpeed`, `famineLeaveDays`, `famineLeaveChance`,
  `growthSpring`, `growthSummer`, `untendedGrowth`, `planFoodAge`, `migrantWinterShare`, `birthWinterShare`,
  `unrestAge`, `unrestPopScale`, `unrestUnhappy`, `unrestCrowding`, `unrestFaithless`, `unrestSmoothing`,
  `unrestWarn`, `disputeAt`, `theftAt`, `disputeChance`, `theftChance`, `outbreakBase`, `outbreakMinPop`,
  `outbreakWarn`, `outbreakRadius`, `outbreakCatch`, `traderValue`, `traderRate`, `lawCooldownDays`.
- `laws.json` (`LawDef`: `id`, `name`, `description`, `research`, `effects`).
- Effect stats read by code: `yield.irrigated`, `storage`, `water.radius`, `traders`, `migrants`, `knowledge.elders`,
  `unrest`, `food.eaten`, `law.foodsharing`, `sickness` (plus the phase 1 ones).

### `-uishots`
Goes on through the Agricultural Age (`GameController.AgriShots.cs`, `-noagri` skips it): fields in spring, summer,
autumn (mid-harvest) and winter with the field panel, a citizen with the new needs, a well and its reach, a rebuild,
the laws screen, revealed ore and a copper mine, the smelter, the end screen, the grown town close and wide. Every
wait is bounded (a missing scene is skipped). The windowed run takes about 10 minutes.

### Known gaps and simplifications
- **Cut**: the optional spring flood (`GameController.StartFlood`) does not damage or pause floodplain buildings.
- Bronze comes in years 10-12 on most seeds (the brief said roughly 12-25); the hard maps take 18-20.
- With the auto-player the age is comfortable once the fields work: deaths are mostly old age, thefts are rare
  (the council is staffed early), 6-16 outbreaks in 30 years. A careless player still starves and freezes.
- The town stops at ~160 because the auto-player stops building homes at 150; nothing in the sim caps it.
- Deposits never run out; mines work at their door; ore outcrops are hidden under trees where the deposit is wooded
  (the stained ground shows through gaps).
- Farmers step between spots in their field (the view walks them there); harvest goes straight into loads (no stooks).
- Path drag-placement was written for the mouse and is not in the screenshots (the auto-player lays paths through
  `PlacePath`); paths are free.
- Traders are automatic (no offer screen).
- The top bar is tight at 1920 wide: with the needs strip it shows only two key stocks (T lists all).
- The normal game commits walking-cost changes every frame (GameController), so the parallel re-pathing batch only
  helps the headless runs; the lazy per-target rebuild still works in the game.

### Notes for phase 4 (save/load)
State added in phase 3 that must be saved (everything else is derived and refreshed hourly or daily):
- `Building`: `UpgradeTo` (def id; with `Delivered`/`InTransit` sized to the upgrade's cost while it runs; `CostRes`
  is derived), `Sown`, `Growth`, `Harvested`, `CropOut`, `TendedToday`, `GoodsLife`, `Focus` (now also the crop
  choice). Derived: `Irrigated`, `HasWater`, `HasFaith` (refresh after load: `RefreshFullness`, `RefreshHomes`).
- `Citizen`: `DisputeDays`, `HungryDays`, `Ate` (derived daily but used by today's damage), errand state for the new
  `Task.MoveStock` (`TaskB`, `TaskB2`, `TaskIdx`, `TaskAmt`). Derived daily: `NoWater`, `NoGoods`, `NoFaith`.
- `Settlement`: the laws in force (`_laws`) and when each last changed (`_lawChanged`), `Unrest`, `UnrestTarget`,
  counters `Disputes`, `Thefts`, `Outbreaks`, `Trades`, `Upgrades`, `ToolSwaps`. Derived: `OutbreakRisk`, the need
  flags, `PathCount` (count `Map.Path`).
- `GameMap`: `Path[]` (path tiles). Derived: `Open[]` (from crop buildings), `OreRevealed` (from research effects, set
  in `RebuildEffects`); after loading paths call `RefreshCost` on every cell and `CommitCostChanges`.
- HUD: `EndShown` (or treat a save already in the last age as shown), `LawsOpen` is UI only.
- Auto-player (only if it should resume): `_grainShare`, `_lastHungryYear`, `_lastFullYear`, `_harvestPerField`,
  `_grainAtAutumn`, `_pathed`, `_replacing`.
- RNG: `Settlement._rng` (System.Random) now also drives farmers' spots in fields, disputes, thefts, outbreaks and
  famine leaving; it is still not serialisable (save a seed + draw count or swap in a small serialisable RNG).
  `GameMap.DailyRegrowth` still draws from it.
- The view rebuilds its caches from the sim (`SettlementView` path and ore layers resync when `Map.PathsDirty` /
  building count change; set `Map.PathsDirty = true` after a load).

## Phase 4: save/load, menus, onboarding, polish, performance

Status: in progress (this section is updated at each commit). How to play: `docs/how-to-play.md`.

### What exists
- **Save / load** (`Game/Save/SaveIO.cs`, `SaveGame.cs`, `Sim/Settlement.Save.cs`, `GameMap.Write/Read`,
  `AutoPlayer.Write/Read`). Format: `FLSV`, format version (1), an uncompressed header (name, kind, seed, day, year,
  season, age, population, difficulty, UTC ticks: what the load screen lists), then a gzip body: map size, the map's
  changes (trees with kind/scale/yaw/offset, rock, paths, occupancy, reserved/open cells, water classes, live and
  committed walk costs, pending-commit flags, ore revealed, herd sizes), the settlement (every field the sim carries,
  float bits exact, buildings by index, citizens with their errand state, events, warnings and the warning memory,
  laws, floods, trader offer, difficulty, RNG state), the auto-player if one runs, the water depths, a length-prefixed
  game block (camera, last speed, HUD: end shown, guide step, hints seen) and a test block (the auto-test's report so
  far). The map is regenerated from its seed. Written to `name.tmp` and moved into place. A newer format, a foreign
  file or a damaged one gives a player-facing message, never a crash. Saves: `persistentDataPath/saves/*.flsave`.
- **Determinism through a save**: `Sim/SimRandom.cs` (SplitMix64, one ulong of state) replaces `System.Random` in the
  sim and the map's regrowth. Walk costs now have a live copy (`GameMap.LiveCost`) and a committed copy
  (`Flow.Cost`, copied at each commit): every path between two commits sees the same prices, so routes no longer
  depend on which flow fields happened to be cached, and a loaded game (empty cache) walks exactly as the original.
  The auto-player's replacement map became an ordered list (a dictionary's slot reuse is not restored by a load).
- **Save/load check**: `-autotest -saveloadcheck -seed N -years Y -outdir D` runs A (saves at day Y*32/2+3) and B
  (loads that save) and compares `autotest-report.md/json` line by line. `-savemid FILE [-saveday D]` on a normal
  auto-test also writes a save (used for the screenshot run).
