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

(updated at each commit; see the end of this section)

### Balance changes (and why)

- Day length 24 -> 40 s and walk speed 2.0 -> 2.2 cells/s: at 24 s a commute ate a third of the workday.
- Start: 12 people (2 children, 2 elders; design says 8-12), more starting food (60 wild food + 25 meat) and wood;
  campfire store 80 -> 180 so the start goods fit.
- Gatherers 1.6 -> 4.5 wild food per worker-day; woodcutter wood/firewood 1.4/1.4 -> 2.4/2.0; stone gatherer 1.2 ->
  1.6; hunters meat 2.0 -> 3.0, hides 0.35 -> 0.4; elders' knowledge 1.0 -> 1.2. Effective output is ~70% of nominal
  because of walking and carrying, and the early band has no tools (x0.6), so the drafted numbers starved everyone.
- Hunters use up one animal per 10 meat (was 2: herds vanished in days); stone gatherer takes one stone off a rock
  per stone made (rocks now run out as described).
- Firewood per home [0.3, 0, 0.6, 1.2] -> [0.2, 0, 0.4, 0.8], campfire fuel [0.3, 0, 0.5, 0.8] -> [0.2, 0, 0.4, 0.6].
- Builder work per day 1.0 -> 1.5 (a lean-to took most of a season).
- Wild food spoils 2.5% -> 1.5% a day (roots and nuts keep).
- Births need 3 days of food (was 5); migrants 5 (was 8). Births need free housing anywhere (the baby stays with its
  mother, so homes can end up crowded).
- Trees regrow 3 -> 12 a day map-wide (only next to existing forest).
- New balance fields: `startChildren`, `startElders`, `migrantCheckDays`, `migrantChance`, `foodWarnDays`,
  `workHaulMin`, `demolishRefund`, `focusShare`, `homelessShelter`, `happinessSmoothing`, `buildersUseTools`,
  `fuelResource`/`clothingResource`/`herbsResource` (the only resource ids the code needs), name lists.

### Known gaps

(see the end of this section)
