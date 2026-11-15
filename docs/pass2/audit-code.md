# Pass 2 code health and robustness audit

Branch `experiment/quarter-pass2` (from 468a70a). Find-and-report only: no source or data changed. Raw logs:
`runs/pass2/code/` (git-ignored). Line numbers are at HEAD 468a70a (another agent was editing `GameController.cs`,
`Hud.cs`, `FlowFields.cs` and `WorldView.cs` during this audit).

Severity: blocker / major / minor / cosmetic / tech-debt. "CONFIRMED" = shown by a run; "reading" = from the code only.

## Summary and ranking

No blocker found. Every test run passed: same-seed determinism, save/load identity on two new seeds, the damaged-save
check, 18 new fuzz seeds (0 invariant violations, 553 save round trips), a 60-year game and a 512-person crowd. The sim's
bookkeeping is in good shape; the risks are in what surrounds it: failure handling, the save format's ability to evolve,
and formulas/ids duplicated outside the data.

Ranked (most player-relevant first; ids are the section numbers below):

| Rank | Id | Title | Severity | Effort | Found by |
|---|---|---|---|---|---|
| 1 | A2 | Saves break when this pass adds a field or a good (no versioned reads; goods by index) | major | S-M | reading |
| 2 | A1 | An exception in the sim/HUD vanishes the world, spams the log and rotates broken autosaves | major (rare) | S | reading |
| 3 | A16 | The Tab key ("Show a good site (Tab)") does nothing | minor | S | reading |
| 4 | A9 | A data JSON typo hangs headless tests forever / black window, no message | minor (major for the workflow) | S | CONFIRMED |
| 5 | A4 | Building panel output/knowledge per day ignores difficulty, no-water and the Elders law | minor | S | reading |
| 6 | A10 | Overwriting a save deletes the old file before moving the new one in | minor | S | reading |
| 7 | A11 | Unrest grows with population alone: 200+ towns get standing disputes, 400+ thefts | minor (balance) | S | reading |
| 8 | A3 | A save naming an unknown research topic throws inside Tick (-> A1) | minor | S | reading |
| 9 | A5 | Focus index survives a rebuild into another building (latent) | minor | S | reading |
| 10 | A7 | Per-frame `new GUIStyle` in the HUD (windowed garbage not in F27's batch soak) | minor (perf) | S | reading |
| 11 | A13 | Spoilage, flood, demolition and leaver losses are not counted: no conservation check possible | minor (test gap) | S | reading |
| 12 | A12 | At 4x, walkers on paths outrun the view's smoothing cap | cosmetic | S | reading |
| 13 | A14 | 30 names per sex for towns of 170 | cosmetic | S | reading |
| 14 | A6 | Needs, trader wants, piles, top-bar goods keyed by id in C# (5-8 places per new need/good) | tech-debt | M (S parts) | reading |
| 15 | A17 | Float cumulative counters drift in long games (reports only) | tech-debt | S | reading |
| 16 | A8 | Compile warnings (unused local function, unused field; misplaced comment) | cosmetic | S | CONFIRMED |
| 17 | A15 | ~1,900-line GameController mixes the game with test/clip drivers | tech-debt | M | reading |

What to do in this pass (cheap and pays now): A2 (before any field is added: version on the reader, resource id table,
milestone ids; fold A6's need bitmask, A13's loss counters and A17's doubles into the same version bump), A1, A16, A9,
A3, A10, A4, A5. Defer: A6 (beyond the data fields), A15.

## Findings (in the order found)

### A1. An exception in the sim or HUD freezes the drawn world, spams the log every frame, and autosaves keep rotating
- Severity: major (if it ever happens; nothing in the tests has thrown, so the likelihood is low but the failure is total)
- Evidence (reading): `GameController.Update` (HEAD `GameController.cs:156-192`) calls `Hud.Update`, `UpdateWater`,
  `Autosave()`, `Sim.Update`, `Auto.Think`, then `Wild.Update`, `View.Draw`, `SView.Draw`, `Sound.Update` with no
  try/catch. Unity catches the exception at the end of `Update`, so everything after the throw is skipped every frame:
  trees, buildings, people and wildlife are drawn by `View.Draw`/`SView.Draw` and stop being drawn; the IMGUI HUD
  (`OnGUI`) keeps running. `Settlement.Tick` has already advanced `_acc`/`Time` (and `Day`, for a throw in `NewDay`)
  before the throw, so the sim lurches on a partial step per frame. `Autosave()` runs *before* `Sim.Update`, so at each
  season change it writes the half-stepped state into the oldest of the three autosave slots; after three seasons every
  autosave holds the broken game. A path that throws today: a research id in a save that the data no longer has (A3).
- Player impact: the world vanishes/freezes with the HUD still up, no message; playing on overwrites the good autosaves.
- Effort: S. Fix: wrap the sim block (`Sim.Update` + `Auto.Think`) and `Hud.Update` in try/catch; on the first
  exception log it once with the day, pause (`SetSpeed(0)`), set a `_simFaulted` flag that stops autosave, and show a
  modal "Something went wrong (details in the log): Save a copy / Load / Main menu". Give the draw calls their own
  try/catch so a view bug does not take the HUD down and vice versa.
- Risk: none to determinism or saves.

### A2. Saves written now break when this pass adds a field or a good: no versioned reads, goods stored by index
- Severity: major (for this pass: it will add fields; Bronze-age content will add goods)
- Evidence (reading): `SaveGame.Version = 1` (`Save/SaveGame.cs:19`); `ReadHeader` accepts any version <= 1 but the
  version is not passed on: `SaveReader` has no `Version`, and `Settlement.Read`, `ReadBuilding`, `ReadCitizen`,
  `GameMap.Read`, `AutoPlayer.Read` read strictly by position (`Sim/Settlement.Save.cs:121-191`). One new field shifts
  every later read: the load fails with "damaged or from an incompatible build" (or reads garbage that happens to
  parse). Goods are positional: `StockTotals/Produced/Consumed` go through `CopyInto`, which throws "goods list does not
  match the game data" when `resources.json` gains a good (`Settlement.Save.cs:193-197`); every building's
  `Store/Out/In` and citizen `Load` are `ResCount`-long arrays (`ReadBuilding` throws the same, :103). Bare resource
  *indices* are saved for `Citizen.Tool`, `CarryRes`, `LoadRes`, `TaskIdx` (fetch/move errands), `TradeOffer.Give/Want`:
  if goods are reordered, or one removed and one added, the load succeeds and silently turns stone tools into copper
  tools or meat into clay. Milestones are saved by position (`Settlement.Save.cs:36, 158-163`): a changed milestone list
  in `ages.json` moves Done flags onto the wrong milestones (an extra one gets the label "?"). The map is regenerated
  from the seed with the *current* `balance.json` (`SaveGame.Read` -> `map.Generate(h.Seed, data.Balance)`), so a change
  to `copperDeposits`/`tinDeposits`/river generation moves deposits or water under existing buildings without error.
  Building defs, research and laws are saved by id (good).
- Player impact: after the next build every playtester's saves (and Continue) stop loading; with reordered goods they
  load with the wrong goods.
- Effort: S-M. Fix (smallest that pays now): (1) `SaveReader.Version` from the header, passed through; bump
  `SaveGame.Version` for each new field and read it as `x = r.Version >= 2 ? r.Float() : default`; (2) write the
  resource id list once at the start of the body and build an old-index -> new-index table on load; read every
  `ResCount` array and every saved resource index through it (unknown old goods dropped, new goods 0); (3) save
  milestone def ids (or labels) and match by them; (4) put a hash of the map-generation balance numbers in the header
  and refuse with a clear message when it differs.
- Risk: none to determinism (the same bytes come back); this is what keeps later saves loading.

### A3. A save naming a research topic the data no longer has crashes the running game (not the load)
- Severity: minor (only after a research id is renamed/removed between builds)
- Evidence (reading): `Settlement.Read` restores `ResearchQueue`, `_researched`, `_progress` ids unchecked
  (`Settlement.Save.cs:152-154`). The next `AddKnowledge` -> `SpendBank` does `var r = Data.ResearchById(id); ...
  float need = r.cost - p;` (`Settlement.cs:359-371`): NullReferenceException inside `Tick`, i.e. A1.
- Effort: S. Fix: on load drop queue/known/progress/law ids that `Data` does not know (log once). Risk: none.

### A4. The building panel's "Makes about N a day" is still a second copy of the production formula, and disagrees with the sim
- Severity: minor (same class as 468a70a's focus bug: that commit unified only the per-output base amount)
- Evidence (reading): the sim's rate per worker is `OutputPerWorkday x SeasonYield x Fullness x Effect(yield tag) x
  Diff.yield`, times `WorkSpeed` (mood, `noWaterWorkSpeed` x0.9 without water, tool) (`Settlement.Buildings.cs:574`,
  `Settlement.People.cs:634-641`). The panel (`UI/Hud.Info.cs:157-186`) multiplies season x fullness x research x tools x
  mood x workers and leaves out `Diff.yield` (Gentle x1.15, Harsh x0.9) and the no-water penalty; its knowledge line
  (`Hud.Info.cs:169-174`) leaves out `Effect("knowledge.elders")` (Elders Settle Disputes: x0.8 on elders). On Harsh the
  panel overstates every workplace by 11%, on Gentle it understates by 13%; with the Elders law an elders' fire is
  overstated by 25%. The "Uses ... a day" line has the same gaps.
- Effort: S. Fix: one `Settlement.EstimateWorkplace(b)` (daily amount per output and input, knowledge per day) built
  from the helpers the sim uses (`WorkSpeed`, `OutputPerWorkday`, the yield product); the panel only formats it.
- Risk: none.

### A5. A workplace's output focus survives a rebuild into a different building and can stop it producing
- Severity: minor (latent: today only homes have `upgradesTo`; a Bronze-age workplace upgrade would hit it)
- Evidence (reading): `CompleteUpgrade` swaps `b.Def` and rebuilds `OutRes/InRes` but keeps `b.Focus`
  (`Settlement.Buildings.cs:346-365`); `OutputPerWorkday` returns 0 for every output when the old focus index is not a
  valid output of the new def (`:689-698`). No error, the building just shows "Makes about" nothing.
- Effort: S. Fix: in `CompleteUpgrade` reset `b.Focus` to -1 (or to the new def's output with the same resource id).
- Risk: none.

### A6. Adding a need, a traded good or a new good means editing code in 5-8 places (needs, traders, piles, top bar are keyed by id in C#)
- Severity: tech-debt (cost lands on every later age)
- Evidence (reading):
  - Needs: `balance.json` `needs` only holds name/age/research/unmet; *how* a need is met is code per id:
    `RefreshHomes` (water = river radius or a `waterRadius` building, faith = staffed `faithRadius` building),
    `DailyGoods` (goods = one `goodsResource` per home), `ExtraNeeds` and `NeedMet` (`id == "water" ? c.NoWater : ...`),
    one bool per need on `Citizen` (`NoWater/NoGoods/NoFaith`, each written separately in the save,
    `Settlement.Save.cs:115`), the warnings (`RefreshWarnings` only knows water), `OutbreakWarning`, and the HUD text
    (`Hud.Agri.cs:351-354` switches on the id). A Bronze-age need (e.g. metal goods, a temple) touches about 8 places
    and the save format.
  - Traders: what they bring is a chain of special cases (`Settlement.Agri.cs:157-162`: tools x0.35 adults, clothing,
    pottery per home, herbs 4, `"tin_ore"` 12 once `"bronze_working"` is known). Every new traded good needs code.
  - Single structural goods: `fuelResource`, `clothingResource`, `herbsResource`, `goodsResource` are one id each;
    a second fuel (charcoal) or a second clothing good (linen vs hide) needs code changes in `DailyNeeds`.
  - View/HUD: goods piles by id (`View/BuildingMeshes.cs:185-201` switch: `"copper_ore"`, `"tin_ore"`, `"fibre"`...;
    a new good falls back to "basket"), the top bar's key stocks and tooltips (`UI/Hud.TopBar.cs:21-38`).
- Effort: M for needs, S for the others. Smallest refactor that pays now: data fields `pile`, `keyStock`/`tip` and
  `tradeTarget` (`{ "per": "adults", "x": 0.35, "after": "bronze_working" }`) on resources; and, while A2 changes the save
  format anyway, store citizen needs as one `unmet` bitmask indexed by the `needs` list instead of three bools. The full
  data-driven need ("met by a building with radius X / a good per home / a staffed building") can wait for the age
  that adds the next need.
- Risk: the bitmask changes the save format (do it together with A2's version bump); none to determinism.

### A7. IMGUI allocates new GUIStyles (finalizable native objects) on every OnGUI event; the windowed game makes more garbage than the F27 soak measured
- Severity: minor (performance; feeds F27)
- Evidence (reading): `new GUIStyle(...)` inside per-frame draw code: the top bar's research label
  (`UI/Hud.TopBar.cs:133`, always on screen), the event feed (`Hud.Panels.cs:218`, one per row, up to 7), every
  `BarRow` right label (`Hud.Info.cs:36`), every research node (`Hud.Research.cs:177, 183`), every build-menu row
  (`Hud.BuildMenu.cs:69-70`), the guide card (`Hud.Guide.cs:85-86`), the goods panel (`Hud.Panels.cs:109-110`), tooltips
  (`Hud.cs:561`). OnGUI runs 2+ times a frame (Layout, Repaint, input), so with the research screen open that is
  hundreds of GUIStyle copies a second, each with a native counterpart and a finalizer, plus the string interpolation
  of every label (top bar alone: ~19 `$"..."` per event). F27's soak ran in batch mode, where OnGUI is never called, so
  none of this was in the "~50 gen-0 collections a minute" figure: the windowed game collects more often than measured.
- Effort: S. Fix: create these styles once in `UiTheme` (they only differ by font size/alignment) and cache per-frame
  strings that change at most once a game hour (top bar figures, building panel lines) keyed by `S.Day`/hour.
- Risk: none.

### A8. Compile warnings worth fixing
- Severity: cosmetic / tech-debt. Evidence (CONFIRMED: `tools/compile-check.py` + a private copy of `_check.csproj`
  built with `-p:NoWarn=USG0001`, i.e. with CS0169 visible, which `_check.csproj` normally hides):
  - `Sim/AutoPlayer.Agri.cs:58` CS8321: local function `known` declared but never used.
  - `View/BuildingMeshes.cs:19` CS0169: field `_bundleBasket` never used.
  - (`GameController.PerfProbe.cs:35` CS0169 `_lastSimTime`: a file another agent is adding during this pass.)
  - CS0649 on `MaterialLibrary.SpriteEntry/Manifest` and `GameData.*File` fields: false positives (filled by
    `JsonUtility`); silence with `[SerializeField]`-free `#pragma warning disable 0649` around those types.
  - No obsolete-API (CS0618) or unreachable-code (CS0162) warnings. The project's `<NoWarn>0169</NoWarn>` hides unused
    fields; worth removing so dead fields show up.
  - Also cosmetic: `GameData.cs:208` carries `demolishRefund`'s comment on the `focusShare` line (left over from 468a70a's
    edit), and `demolishRefund` (:207) has none.
- Effort: S. Risk: none.

### A9. A typo in a data JSON hangs the headless tests forever and gives the windowed game a black screen with no message
- Severity: minor for players (shipped data is fixed), major for this pass's workflow (`tools/autotest.sh`/`fuzztest.sh`
  copy `Assets/StreamingAssets/Data/*.json` into the player, so every balance edit is one stray comma from a hang)
- Evidence (CONFIRMED): a temporary copy of the player (deleted afterwards; the real `Build/` untouched) with one extra
  comma in `balance.json`, run as `-batchmode -nographics -autotest -seed 5 -years 1`: the log shows
  `ArgumentException: JSON parse error: Missing a name for object member.` and the process never exits (killed by
  `timeout 90`, exit 124; `runs/pass2/code/badjson/player.log`). Cause: `GameController.Start` calls `GameData.Load()`
  first (`GameController.cs:42`); the exception ends `Start`, `View` stays null, `Update` returns at once forever, and
  nothing calls `Application.Quit`. In the windowed game the same path leaves a black window: no menu, no message
  (`OnGUI` also returns while `View == null`). Beyond parse errors, `GameData.Validate` only `Debug.LogError`s unknown
  ids (invisible in a player build) and does not check: that `fuelResource`/`clothingResource`/`herbsResource`/
  `goodsResource` exist (a typo makes `_fuel = -1` and `StockTotals[_fuel]` throws hourly in `RefreshWarnings`, i.e. A1),
  that `seasonNames`/`seasonTemperature`/`firewoodPerHome` have 4 entries (`Settlement.cs:43,46`,
  `Settlement.People.cs:959` index them unchecked), that every building/research `age` names an age (an unknown age is
  order 99: the building or topic silently never unlocks), or duplicate ids (silently overwritten in the lookups).
- Effort: S. Fix: try/catch round `GameData.Load` in `Start`: in batch mode log the error and `Application.Quit(2)`;
  windowed, show a one-line error screen with the file name and the parser message. Make `Validate` collect problems
  into a list, add the checks above, and treat any problem as fatal in `-autotest`/`-fuzztest` (exit 2) so a bad
  balance edit fails fast instead of producing a misleading report.
- Risk: none.

### A10. Overwriting a save deletes the old file before moving the new one in
- Severity: minor
- Evidence (reading): `SaveGame.Write` (`Save/SaveGame.cs:78-79`): `if (File.Exists(path)) File.Delete(path);
  File.Move(tmp, path);`. If the move fails (a virus scanner or a cloud-sync client briefly holding the new `.tmp` open
  is a common cause of sharing violations on Windows), the old save is already gone and the player gets "Could not save:
  ..." with only `name.flsave.tmp` left, which the load screen does not list (`List()` matches `*.flsave`). A crash in
  that window has the same result. Leftover `.tmp` files are never cleaned up. Disk full while writing is handled well:
  the exception comes from writing the `.tmp`, the old save is untouched, and the toast shows the reason.
- Effort: S. Fix: `File.Replace(tmp, path, path + ".bak")` when the target exists (atomic on NTFS), else `File.Move`;
  on start, offer `*.tmp`/`*.bak` files in the load list (or delete stale `.tmp`).
- Risk: none.

### A11. Unrest grows with population alone: towns past ~200 people get standing disputes, past ~400 thefts, and nothing more can be done
- Severity: minor (balance at scale; the auto-player stops at ~150-170, so no test has seen it)
- Evidence (reading, numbers from `balance.json`): `DailyUnrest` target = pop / `unrestPopScale` (400) + shares of
  unhappy/crowded/faithless people, x `Effect("unrest")` (Elders Settle Disputes 0.6, the only lever), x (1 - 0.5) with
  a staffed council (`Settlement.Society.cs:175-179`; `orderPower` is the *max* over councils, so a second council does
  nothing). With every need met: pop 160 -> 0.20 (the warning chip `unrestWarn`), 200 -> 0.25 (`disputeAt`: daily
  disputes), 240 -> 0.30 (`theftAt`) without the law; with the law the same thresholds come at 267 / 333 / 400. Nothing
  in the sim caps population (build log: the 160 cap is the auto-player's plan), so a human who keeps building huts
  meets a permanent warning and thefts with no building or research that answers it. The `-crowd 500` probe (500 extra
  people, 10 buildings) ran without errors at 0.51 ms per sim step, so performance is not the limit.
- Effort: S (balance) / M (a Bronze-age answer). Fix: make the population term grow sub-linearly or per council
  (e.g. `pop / (400 * councils staffed)`), or add order buildings with coverage; at least document the soft cap.
- Risk: balance only (changes auto-test numbers; none to saves).

### A12. At 4x, people walking on paths outrun the view's smoothing and trail behind, then slide after they stop
- Severity: cosmetic (same family as 468a70a's choppy-walking fix, different speed)
- Evidence (reading): the drawn position may move at most `(walkSpeed x Speed x 1.25 + 0.5)` cells/s
  (`View/SettlementView.cs:430`), i.e. 11.5 at 4x, but the sim walks `walkSpeed x pathSpeed` = 2.2 x 1.35 = 2.97 cells/s,
  11.9 at 4x (`Settlement.People.cs:87`). On a long path the figure falls ~0.4 cells behind per second, then glides on
  after the person has arrived (or snaps if it ever falls 4 cells behind). At 1x and 2x the cap is just above path speed.
- Effort: S. Fix: include `pathSpeed` in the cap (`walkSpeed * max(1, pathSpeed) * Speed * 1.25 + 0.5`); with
  `DrawPos` interpolating now, the cap could also simply be removed for walkers.
- Risk: none (view only).

### A13. Goods that leave the economy are not all counted, so stock cannot be reconciled (and leavers silently take goods)
- Severity: minor (test gap; small player effect)
- Evidence (reading): `Consumed[]` counts eating, burning, wear, theft, `DropCarried` losses and flood losses from stores,
  but not: daily spoilage (`DailySpoilage`, `Settlement.Buildings.cs:513-533`), flood losses from workplace `Out`/`In`
  (`Settlement.Flood.cs:99-100`), goods lost when a demolished store's contents find no room (`Demolish`, the `lost`
  total is only logged), goods carried or in a field load by someone who leaves (`Depart` zeroes `CarryAmt` and drops
  `Load`, `Settlement.People.cs:1156-1167`) or dies (`Die`), and tools held by the dead and leavers. So `Produced -
  Consumed` does not explain the stock and a conservation invariant cannot be written today. Player side: a leaver
  walking off with 15 grain or a hunter's load of meat is not mentioned anywhere.
- Effort: S. Fix: a `Lost[]` (or `Spoiled[]` + `Lost[]`) counter per good, fed at each of these points (and saved: do
  it with A2's version bump); then add the fuzz invariant in "Missing invariants".
- Risk: save format (one more array); none to determinism.

### A14. 30 names per sex for towns of 170: every name is shared by ~3 people at once
- Severity: cosmetic
- Evidence (reading): `balance.json` `namesFemale`/`namesMale` hold 30 each; `NewCitizen` picks at random
  (`Settlement.People.cs:39`). Events ("Ari died of old age", "Ari gave birth to Tova") and the citizen panel cannot tell
  same-named people apart. (A mother and her baby can get the same name: about 1 birth in 60.)
- Effort: S. Fix: more names, avoid names in use when picking, or add a household/family name.
- Risk: changes the RNG draw sequence if implemented by re-drawing (changes auto-test numbers, not saves).

### A16. The Tab key ("Show a good site (Tab)") does nothing: the check sits after an early return for the same state
- Severity: minor (player-visible in the first minute of every game; the button itself works)
- Evidence (reading): `Hud.Keys` (HEAD `UI/Hud.cs:230-259`): `if (S.NeedsCampfire) return;` (:246) comes before
  `if (kb.tabKey.wasPressedThisFrame && S.NeedsCampfire) PointAtSuggestedSite();` (:258), which can therefore never run.
  The campfire prompt's button says "Show a good site (Tab)" (`Hud.cs:499`) and `docs/how-to-play.md:45,54` tell the
  player to press Tab. (F8 used to do this; phase 4 moved it to Tab and the check stayed below the return.)
- Effort: S. Fix: move the Tab line above `if (S.NeedsCampfire) return;`. Risk: none.

### A17. Cumulative float counters lose precision in long games (test reports drift; save format fixes it cheaply now)
- Severity: tech-debt (reports only, nothing a player sees)
- Evidence (reading + arithmetic): `Produced[]`, `Consumed[]`, `WorkerSeconds`, `ProductiveSeconds`, `KnowledgeTotal`
  are `float` totals fed in tiny per-step amounts (a worker's step output is ~0.01-0.02; `WorkerSeconds += 0.25`). Once a
  total passes 2^15-2^17 (wild food / grain produced in a 20-30 year run) the float spacing is 0.004-0.016, the same size
  as the increments, so each add rounds by up to half of itself; `WorkerSeconds` reaches ~3 M by year 60 (seed 21) and
  stops counting 0.25 steps exactly from 4.2 M (~year 80). The auto-test report's per-season "made / used / at work"
  figures are differences of these totals.
- Effort: S. Fix: make them `double` (the save writes them; do it with A2's version bump). Risk: changes report digits
  (not sim behaviour: nothing in the sim reads them).

### A15. Test and clip code lives inside the game's main controller
- Severity: tech-debt
- Evidence (reading): `GameController` is one partial class of ~1,900 lines across 6 files at HEAD (7 with this pass's
  perf probe); only `GameController.cs`
  `Start/Update/OnGUI`, `.Flow.cs` and the water code are the game. The rest is test drivers and clip recorders:
  `WaterShots/DeerShots/DeerPairShots/HuntShots/DensityShots` (~420 lines in `GameController.cs`), `.UiShots.cs` (334),
  `.AgriShots.cs` (186), `.P4Shots.cs` (255), `.Soak.cs` (91), the smoke test in `.Flow.cs`, and a perf probe being added
  this pass. `Game/People.cs` (`Band`, 516 lines) is only reachable from the clip modes; `StressTest/` (464 lines) and
  `Core/AgentJobs.cs` only from the stress scene. They share private state with the game (`_demo`, `_hideGui`,
  `_pendingShot`, `CanopyCutOff`, static `_p4Stage/_reloadStage/_smoke`), so a change to the game flow has to be
  checked against every clip mode. Cost for Bronze content: low directly; the cost is that `Start`/`Update` keep
  growing branches (`_demo`, `HasArg` checks) and every new HUD flow has to keep the screenshot drivers working.
- Effort: M. Smallest step that pays now: none needed for this pass; when a mode next breaks, move it to its own
  `MonoBehaviour` added by `Start` from a `-mode` table, talking to the game only through `Hud`'s public actions.
- Risk: none.

## Test results

All on the player in `Build/` (data identical to `Assets/StreamingAssets/Data`, checked with `diff -r`), headless.

**Determinism** (`-autotest -seed 2026 -years 12`, two separate processes, `runs/pass2/code/detA`, `detB`): md and json
byte-identical (md5 `c82c2395...`, json `d3ff4a95...`). PASS, Agricultural Age day 102, Bronze Age day 367, final pop 152.

**Save/load identity** (`-autotest -saveloadcheck -years 30`, save at day 483, seeds not in QA's list):

| Seed | Run A | Loaded | Reports | Result |
|---|---|---|---|---|
| 21 | PASS (93.8 s) | day 483, pop 153 | md 1296 lines, json 7528 lines IDENTICAL | PASS |
| 555 | PASS (97.8 s) | day 483, pop 160 | md 1274 lines, json 7506 lines IDENTICAL | PASS |

**Damaged saves** (`-autotest -savecheck -seed 77`): PASS. Empty, foreign, header-only, cut at 10% / 50% / 40 bytes
short, flipped byte, newer version: all refused with a message, none threw; a missing gzip trailer still loads.

**Fuzz** (`-fuzztest -years 15`, 18 seeds, 3 at a time; `runs/pass2/code/fuzz-*`): 0 violations, 553 save round trips
byte-identical.

| Seed | Mode | Stopped | Births / migrants | Round trips | Violations |
|---|---|---|---|---|---|
| 101 | 8 % + auto | day 326 (y11), extinct, Stone | 10 / 2 | 30 | none |
| 102 | hostile alone | day 57 (y2), extinct day 17 | 0 / 0 | 22 | none |
| 103 | 2.5 % + auto | day 480 (y16), extinct, Agricultural | 9 / 12 | 37 | none |
| 104 | 8 % + auto | day 70 (y3), extinct day 30 | 0 / 0 | 7 | none |
| 105 | hostile alone | day 64 (y3), extinct day 24 | 0 / 0 | 18 | none |
| 106 | 2.5 % + auto | day 480, pop 131, Bronze | 65 / 75 | 32 | none |
| 107 | 8 % + auto | day 164 (y6), extinct | 0 / 0 | 16 | none |
| 108 | hostile alone | day 60 (y2), extinct day 20 | 0 / 0 | 23 | none |
| 109 | 2.5 % + auto | day 480, pop 162, Bronze | 92 / 85 | 39 | none |
| 110 | 8 % + auto | day 480, pop 108, Agricultural | 29 / 89 | 61 | none |
| 111 | hostile alone | day 64 (y3), extinct day 24 | 0 / 0 | 22 | none |
| 112 | 2.5 % + auto | day 480, pop 180, Bronze | 102 / 92 | 30 | none |
| 113 | 8 % + auto | day 480, pop 10, Agricultural | 8 / 13 | 65 | none |
| 115 | 2.5 % + auto | day 480, pop 120, Bronze | 74 / 67 | 25 | none |
| 116 | 8 % + auto | day 230 (y8), extinct, Stone | 1 / 3 | 20 | none |
| 118 | 2.5 % + auto | day 102 (y4), extinct, Stone | 0 / 0 | 8 | none |
| 119 | 8 % + auto | day 480, extinct, Agricultural | 3 / 15 | 47 | none |
| 121 | 2.5 % + auto | day 480, pop 172, Bronze | 96 / 89 | 51 | none |

**Long game** (`-autotest -seed 21 -years 60`, 245 s): PASS, no errors; Bronze in year 11; population steady at
164-166 from year 20 to 60. Buildings grew 133 (year 20) -> 191 (year 60) at the same population, nearly all storage
piles (68 placed in 60 years): goods with no sink pile up (food 5,120; tools in store: 117 stone, 81 copper, 29 bronze
at year 50; old tiers are never used again or traded away). The sim's hourly cost rose ~45% with the extra buildings and
the auto-player's `Relocate` search to ~2.5 s per game year; overall 0.80 ms per sim step with the auto-player (0.37 at
12 years). Events ~55 per game year (the 20,000 cap is ~360 years away).

**Crowd** (`-autotest -seed 7 -crowd 500`): 512 citizens, 0.51 ms per sim step, no errors (only 10 buildings, so this
times the people loop, not a 500-person town).

**Bad data** (copy of the player, one extra comma in `balance.json`): hangs, see A9.

**Compile** (`compile-check.py` + a private copy of `_check.csproj` with CS0169 visible): builds; warnings in A8.

## Checked and fine

- **468a70a completeness.** *Gone people:* every sim counter that runs inside a step after deaths/leavers are marked
  (`AssignHomes`, `AssignJobs`, `RecountReservations`, `RefreshOutbreakRisk`, `OutbreakWarning`, `DailyUnrest`,
  `DailyOutbreak`, `NeedMet`, `Population/Adults/DailyFoodNeed/AverageHappiness/Homeless`, the "no tools" warning via
  `Job == null`) skips or nulls them; the HUD and auto-player never see them (removed at the end of every step, before
  the frame continues). Only `Building.Residents` stays one hour stale after a death (a home whose last resident died
  burns that day's firewood): harmless. *Focus:* no other code still uses the old `total x focusShare` formula (searched
  every `focusShare`/`.Focus`/`outputs[` use); a focused output is now never below its mixed amount; the panel's copy of
  the rest of the formula is A4. *Smooth walking:* births, migrants and loaded citizens have `PrevPos` (0,0) and snap via
  the 1.5-cell rule; teleports (stuck walkers, farmers stepping into rows) snap the same way; paused play freezes
  `StepAlpha`; departing walkers get `PrevPos` (fixed in the commit). Remaining gap: A12.
- **Determinism by reading.** No `UnityEngine.Random`, `System.Random`, `Time.*` or `DateTime` in `Game/Sim` (the map's
  generation and deposits use `System.Random(seed)`, fixed per seed; `Wildlife` uses its own `System.Random` and is
  view-only in the game: `Wildlife.Kill` is only called by the clip-mode `Band`; herd positions never move).
  Dictionary/HashSet iteration feeds no decision: research and laws are written in data order, `_warnSeen`,
  `_lawChanged`, `_relocateFailed` are only looked up; `Deaths` is a `SortedDictionary`. Parallel flow-field builds
  write one slot per target from integer costs, so results do not depend on scheduling. The sim has no static state, and
  `GameData`/`Settlement`/`GameMap` are rebuilt on every scene load, so a second New game in one session starts like the
  first (statics in `GameController` are flow flags and test stages only). The HUD and view write no sim state except
  the `AutoTrade` toggle (a command) and trimming `Settlement.Kills` (a view-only list, not saved, never read by the
  sim). Accepted, as known: the windowed game's water and walk-cost commits run on frame time.
- **Saves:** building defs, research, laws and the age are stored by id; a missing def gives a clear message. Disk full
  while writing the `.tmp` leaves the old save intact and toasts the reason; autosave skips an empty settlement and does
  not run while the menu is open; the HUD's game block has its own version and is skipped (not fatal) if it does not
  match: the pattern A2 needs for the body.
- **Edge states (reading):** only children left: they idle at the fire, eat from store and come of age within about a
  game year (adult 14, 4 years per game year), no division by zero (`AverageHappiness`, `NeedMet`, `FoodDays`,
  `DailyUnrest` guard their denominators). No tools and none in store: the toolmaker *uses* tools but does not require
  them, so tools can always be made again. The campfire (also a store) can't be demolished; demolishing the council
  suspends the laws and rebuilding resumes them; floods only flag buildings and are saved with their day/strength.
  Speed: `SetSpeed` clamps negatives; Unity's max delta time bounds steps per frame (5 at 4x). `Time` (float) stays
  exact in 0.25 s steps for ~3,000 game years; `DayFraction`/hour accumulators reset daily.
- **List growth:** `Events` capped at 20,000 (55/year), `Kills` trimmed by the view (grows only headless, harmless),
  `Departing` drains, `ResearchLog`/`AgeLog` bounded by the data, `_warnSeen` by the number of distinct warnings.
  (Theoretical only: after the 20,000-event trim, the HUD's `_feedSeen` index is not shifted, so the feed would go quiet
  for 5,000 events; ~360 game years away.)

## Missing fuzz invariants (and how the fuzz test could catch more)

The fuzz test's checks are sound (none fired in 18 runs), but these would catch bug classes it can't see today:

1. **Goods conservation per good**: start + produced + traded in = consumed + lost + stores + workplace out/in + carried
   + field loads + delivered to sites + tools held. Needs A13's loss counters first.
2. **Reservations within bounds**: `HaulReserved <= Free(store) + eps`; `0 <= InTransit[i]` and
   `Delivered[i] + InTransit[i] <= cost[i] + eps` (today only `HaulReserved >= 0` and `Delivered >= 0`).
3. **Cached counters match the people** (what the HUD shows): `b.Workers == count(Job == b)` after `AssignJobs`,
   `b.Residents == count(Home == b)` right after the hourly `AssignHomes` (the test counts people itself but never
   compares the cached fields).
4. **Warnings agree with state**: e.g. "N people have no home" equals `Homeless`, "N are sick" equals the sick count,
   sampled hourly. This is the class 468a70a fixed (dead people in the homeless warning); daily checks of state can't
   see it.
5. **People**: `0 < Health <= 1` for the living (today only the upper bound), `Age` increases day to day and stays below
   `oldAgeStart + 60`, citizen ids unique across `Citizens` and `Departing`, departing walkers have no home/job, nobody
   alive standing for more than a few hours on a cell with walk cost 0.
6. **Flood state**: not flooding -> no building `Flooded` and `FloodCells == null` unless a flood is due; flooding ->
   `FloodCells != null`.
7. **Effects are fresh**: `_effects` equals a recompute from known research + laws in force (catches a missed
   `RebuildEffects`, e.g. a council completed/demolished/upgraded); every law in force has its research known.
8. **Research bookkeeping**: progress <= cost; `KnowledgeTotal` ~= sum of known costs + progress + bank.
9. **Map**: no tree or path under a footprint; `Rock >= 0`; `0 <= Herd.Pop <= Max`.
10. **Continuation after load**: at some random save points, run both the original and the loaded settlement on for a
    day and compare digests. Today a round trip is only compared at the instant of loading; `-saveloadcheck` checks
    continuation, but only for the auto-player's calm games, never right after a flood, an upgrade or a trader's offer.
11. **Fuzz determinism**: run a seed twice and compare the final digest (the hostile command paths are never in the
    determinism check).

Coverage: in these 18 runs only 5 reached the Bronze Age and 2 more stayed in the Agricultural Age with people; 11 died
out, 9 of them in the Stone Age (the fuzzer places the campfire at a random valid spot and draws a random difficulty;
hostile-alone runs die out in 17-24 days and then idle 40 days). So the later systems (granary hauling, traders, laws,
upgrades, metal chain) get abuse on about a third of seeds. Suggest: mixed seeds place the campfire at the suggested
site on Standard, and hostile-alone runs re-found from a save made before extinction. The fuzzer also never drives the
HUD's action layer or the windowed game's frame-time water, so floods there change only building flags, never walking.

