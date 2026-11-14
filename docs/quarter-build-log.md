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

Status: in progress (this section is updated at each commit).

### What exists so far
- Settlement drawn from the sim (`Game/View/`): buildings from their data parts, each part with a material; sites
  marked out, piled with delivered materials, rising inside scaffolding; citizens in poses with what they carry;
  fires that burn while fuelled; day/night and seasons (tints, snow); selection ring, placement ghost, radius studs.
- IMGUI HUD (`Game/UI/`): campfire start flow, top bar, build menu, building/citizen panels, research tree, goals,
  feed of events and warnings, jobs, stocks, help.
- Field work in the sim (gatherers, woodcutters, stone gatherers, hunters walk out and carry loads back).
- `-uishots` screenshot mode (see below).
