# Batch 6 checkpoint: tested balance tuning

Branch `experiment/quarter-pass2`, start HEAD ab83ce6. Written as the work goes (newest at the bottom of each section).

Method: data edits only unless noted; each change on top of the accepted ones; 15-seed gate
(`tools/gate-autotest.sh <label>`, raw in `runs/pass2/<label>/`); 6-seed runs for careless / Harsh / Gentle
(`runs/pass2/b6/<name>/`, script `runs/pass2/b6/scripts/run6.sh`); aggregates by `runs/pass2/b6/scripts/agg6.py`.
Years are game years as day/32 + 1 (so 12.5 = mid year 12). "Famine seed" = 10+ starvation deaths or 20+ leavers.
"First-winter starved" = starvation deaths in year 1 winter + year 2 spring.

## Before (current build, current data)

Standard, 15 seeds (`runs/pass2/b1`):
```
15 runs, PASS 15, extinct 0, stuck-in-Stone 0, famine seeds 1 (13)
agri min/med/max 3.7/4.4/6.5  bronze 11.1/12.6/13.9  final pop mean 163.5
per run: births 194.2 migrants 129.9 leavers 4.5 | starved 2.9 cold 0.7 sick 2.5 old 161.9
first-winter starved (total) 0  migrants years 1-4 mean 11.5  happiness stone 0.908 agri 0.861
```
Careless / Harsh / Gentle, 6 seeds (2026 7 11 42 99 13), current build and data (`runs/pass2/b6/before-*`):
```
careless: 6 runs, PASS 0, extinct 6 (years 2.75-5.75), famine seeds 1
   births 3 migrants 8.7 leavers 3.7 | starved 5 cold 14.2 sick 0.5 old 0.3 | first-winter starved (total) 3
   7 st6 c25 EXT y4.75; 11 st2 c36 EXT y5.75; 13 st0 c16 EXT y3.75; 42 st6 c0 EXT y3.5; 99 st5 c6 EXT y2.75; 2026 st11 c2 EXT y3.25
harsh: 6 runs, PASS 5, extinct 1 (99, year 5.75), famine seeds 2
   agri min/med/max 5.2/5.2/20.5  bronze 13.6/14.6/27.2  final pop mean 135.5
   births 144.7 migrants 116.5 leavers 10.8 | starved 3.7 cold 34.2 sick 3.7 old 85.3
   7 A5.22 B14.56 c47; 11 A5.16 B13.56 c1; 13 A5.19 B14.09 c74; 42 A20.47 B27.16 c4; 99 EXT y5.75 c15; 2026 A7.25 B15.31 c64
gentle: 6 runs, PASS 6, famine seeds 0
   agri 3.3/3.8/5.1  bronze 10.6/11.3/12.3  final pop mean 161.7
   births 203.5 migrants 120.3 leavers 0 | starved 0 cold 1 sick 1.5 old 171.7
```
(Harsh matches the gameplay audit's N11 run exactly.)

## Changes

### 1. F1 famine harms more slowly: KEPT (commit 0e5880c)
Diff (`balance.json`): `"starveDamage": 0.12` -> `0.06`.

| Standard, 15 seeds | before (b1) | after (`runs/pass2/b6-f1`) |
|---|---|---|
| PASS | 15/15 | 15/15 |
| Agricultural year min/med/max | 3.7/4.4/6.5 | 3.7/4.4/6.5 |
| Bronze year min/med/max | 11.1/12.6/13.9 | 10.9/12.6/14.9 |
| final pop mean | 163.5 | 161.1 |
| births / migrants / leavers per run | 194.2 / 129.9 / 4.5 | 195.0 / 125.9 / 5.1 |
| starved / cold / sick / old per run | 2.9 / 0.7 / 2.5 / 161.9 | 0.2 / 1.3 / 2.2 / 163.1 |
| famine seeds | 1 (13: 40 starved + 41 left) | 2 (13: 3 starved + 41 left, town 153 -> 110 in year 14; 3: 0 starved + 22 left in year 13) |
| first-winter starvation deaths (all seeds) | 0 | 0 |

Seed 13's famine now comes in year 14 (before the age-up), pushing its Bronze to 14.9: chaos plus the famine, within
the 10-16 band. Careless player (6 seeds): 6/6 still die out (years 2.75-5.75, same range as before); starved 5.0 ->
1.8, cold 14.2 -> 6.5, leavers 3.7 -> 9.0 per run, first-winter starved 3 -> 0. Neglect is still fatal; the band now
walks away instead of starving. Decision: kept.

### 2. F3 unmet needs gate growth: KEPT at 0.8 (commit 6e8d4eb)
Diff (`balance.json`): `"migrantMinHappiness": 0.5` -> `0.8`. On top of change 1, so "before" here = `runs/pass2/b6-f1`.

| Standard, 15 seeds | before (b6-f1) | after (`runs/pass2/b6-f3`) |
|---|---|---|
| PASS | 15/15 | 15/15 |
| Agricultural year min/med/max | 3.7/4.4/6.5 | 3.7/4.4/6.5 |
| Bronze year min/med/max | 10.9/12.6/14.9 | 10.6/12.4/14.0 |
| final pop mean | 161.1 | 161.8 |
| births / migrants / leavers per run | 195.0 / 125.9 / 5.1 | 194.6 / 118.1 / 5.9 |
| migrants in years 1-4 (mean per run) | 11.2 | 11.1 |
| starved / cold / sick / old per run | 0.2 / 1.3 / 2.2 / 163.1 | 0 / 0.7 / 1.0 / 155.4 |
| famine seeds | 2 (3, 13) | 1 (31337: 49 left, 0 starved) |
| mean happiness Stone / Agricultural | 0.91 / 0.86 | 0.91 / 0.864 |

Stone Age check: Stone Age happiness averages 0.91 (the auto-player keeps food/warmth/shelter met; only the first
two seasons of year 1 sit at ~0.5-0.6, before any migrant could come anyway), and migrants in years 1-4 are unchanged
(11.2 -> 11.1), the Agricultural Age is not later (median 4.4 both). So 0.7/0.75 or an Agricultural-only threshold were
not needed. The late leavers (31337 year 14 summer, 13 year 16, 11 year 14) are summer food gaps from the auto-player's
grain-share rule (audit N2), now ending in leaving rather than starving (change 1); not a migrant effect.
Decision: kept at 0.8.

### 3. F6 Harsh: KEPT as winter -0.02 + start goods 0.85 (commit 8fe57c4)
Diff (`balance.json` `difficulties` harsh): `"startGoods": 0.7` -> `0.85`, `"winterTemperature": -0.05` -> `-0.02`.
All variants run with `-difficulty 2` on the data with changes 1+2 (own build copies `runs/pass2/b6/bld-<v>`, data
edited only there; raw `runs/pass2/b6/harsh-<v>`). 6 seeds 2026 7 11 42 99 13; the two finalists also on 6 more
(1234 5 3 100 777 21, `harsh-<v>-x`).

| Harsh variant | died out | Agri yr min/med/max | Bronze yr min/med/max (survivors) | final pop | starved / cold / left per run |
|---|---|---|---|---|---|
| shipped, old data (`before-harsh`) | 1/6 (99) + 42 Stone Age to yr 20 | 5.2/5.2/20.5 | 13.6/14.6/27.2 | 135.5 | 3.7 / 34.2 / 10.8 |
| shipped, data 1+2 (`h0`) | 1/6 (99) + 42 Stone Age to yr 13 | 5.2/5.7/13.4 | 13.6/14.4/20.8 | 132.2 | 0.5 / 14.7 / 8.0 |
| winter -0.02 (`hA`) | 2/6 (42, 99) | 5.2/5.9/6.3 | 14.1/14.6/15.4 | 104.2 | 4.7 / 7.8 / 11.8 |
| goods 0.85 (`hC`) | 1/6 (2026) | 5.2/6.3/7.1 | 13.3/13.8/16.3 | 125.0 | 0.5 / 54.2 / 9.0 |
| winter -0.03 + goods 0.85 (`hD`) | 1/6 (2026) | 5.8/6.4/7.5 | 13.5/14.8/16.4 | 133.7 | 3.0 / 3.8 / 3.3 |
| hD, 6 more seeds | 0/6 | 4.6/5.5/7.0 | 12.4/13.4/15.7 | 157.7 | 0 / 1.0 / 4.8 |
| **winter -0.02 + goods 0.85 (`hB`, kept)** | **0/6** | 5.0/6.3/7.1 | 13.8/14.6/15.4 | 160.0 | 0 / 3.0 / 4.2 |
| hB, 6 more seeds | 0/6 | 4.8/5.5/8.3 | 12.7/14.6/18.4 | 161.0 | 1.2 / 3.8 / 7.8 |

Choice: hD is the milder edit and meets "5 of 6" exactly, but it lost seed 2026 (a seed that survives shipped Harsh)
and 1 of 12 overall; hB survived 12/12 at nearly the same pace (Bronze median 14.6 vs hD 14.8 / 13.4), so hB is the
mildest edit that clears the target with margin. Harsh vs Standard after: Agricultural Age ~1.5-2 years later, Bronze
~2 years later (14.6 vs 12.4), and one hB seed (3) reached Bronze only in year 18.4. Caveat: the "real losses" are
modest (cold 3-4, starved 0-1, leavers 4-8 per run against Standard's 0.7 / 0 / 5.9): Harsh is now mostly a slower,
tighter game rather than a deadly one. Making it bite harder without the cliff (e.g. yields 0.85) is a design call
left for Nico. Standard gate after the edit (`runs/pass2/b6-f6`): all 15 reports byte-identical to `b6-f3`.
Gentle check: at the final gate.

### 4a. F9 Fishing Spot 1.9 -> 2.8 fish per worker-day: KEPT (commit fb87699)
Diff (`buildings.json` fishing_spot): `"outputs": [{"id": "fish", "amount": 1.9}]` -> `2.8`.

| Standard, 15 seeds | before (b6-f6 = b6-f3) | after (`runs/pass2/b6-f9fish`) |
|---|---|---|
| PASS | 15/15 | 15/15 |
| Agricultural year min/med/max (mean) | 3.7/4.4/6.5 (4.64) | 3.6/4.7/7.4 (4.77) |
| Bronze year min/med/max | 10.6/12.4/14.0 | 11.2/12.4/15.5 |
| final pop mean | 161.8 | 161.2 |
| births / migrants / leavers per run | 194.6 / 118.1 / 5.9 | 193.3 / 115.8 / 5.7 |
| starved / cold / sick / old per run | 0 / 0.7 / 1.0 / 155.4 | 0.1 / 1.3 / 2.3 / 150.5 |
| famine seeds | 1 | 1 (100: 34 left) |
| fishing spots placed (median) / fish made per run | 4 / 4,158 | 4 / 5,988 |

Dominance check from the data (N6 method, per worker-day): summer fisher 2.8 x 1.1 = 3.08 < gatherer 4.5 x 1.2 = 5.4
(6.2 with Seed Gathering); year average 2.45 vs 3.96; winter 1.12 vs 0.54 (fishing was already the better winter job at
0.76). Fishing does not beat gathering in summer. The Agricultural median moved +0.3 but single seeds moved both ways
(99 +2.2 years, its 25-people gate came later after identical play to day ~96; 42 and 1234 -1.2): read as noise.
Decision: kept.

### 4b. F9 flax 120 -> 200 fibre per field: DROPPED (not committed)
Diff tried (`buildings.json` field): `{"id": "fibre", "amount": 120}` -> `200`. Gate `runs/pass2/b6-f9flax`: 15/15 PASS,
Agricultural 3.6/4.7/7.4, Bronze 11.2/12.4/15.5 (same as 4a), pop 161.9, births/migrants/leavers 192.5/117.9/6.6,
starved/cold 0.1/0.8, famine seeds 2 (13: 24 left, 100: 32 left).

Why dropped: fibre is not what limits clothing. Per run (15-seed means), before -> after: fibre made 5,659 -> 8,240,
fibre used 450 -> 423, clothing made 859 -> 807, clothing in store over years 13-30 23.5 -> 18.1 (years 1-12: 0.2 ->
0.2). Over 90% of the fibre already sits unused: the single Weaver (2 workers, 0.6 clothing per worker-day, built
around the age-up) is the bottleneck, so a bigger flax yield only piles up fibre. A fix for clothing would be the
Weaver's output or an earlier Weaver, not flax; not tested here (outside the batch's list). Note the audit's "clothing
sits at 0-4" holds for years 1-12 (mean 0.2); after the Weaver arrives the store does climb (~20-25).

### 5. A14 names: KEPT, data + a small code change (commit 0d4970c)
Diff: `balance.json` `namesFemale` / `namesMale` 30 -> 90 each (60 new per sex in the same style; checked: no duplicate
within or across lists, script `runs/pass2/b6/scripts/names.py`). `Settlement.People.cs`: `NewCitizen` calls a new
`PickName(list)`: one `_rng.Next(list.Length)` draw exactly as before, then step forward (wrapping) to the first name
no living citizen has; if all are taken, the drawn name. A HashSet of living names is built only if the drawn name is
taken. Player rebuilt (`runs/build-b6.log`, compile check 0 errors).

Gate `runs/pass2/b6-a14` (new build, new names) vs `runs/pass2/b6-f9fish` (old build, 30 names): 15/15 PASS, and
population, births, deaths and food per season are identical on all 15 seeds; the reports differ only in names in the
event lines. (The RNG's `Next(n)` uses one draw whatever `n` is, and names never feed back into the sim, so neither
the longer lists nor the step-forward changes anything but the names.) Not measured directly: how often a name is
shared now (the report has no citizen list); by construction it happens only when all 90 names of a sex are alive.

## Final gate (final data + build, HEAD 0d4970c)
- `tools/gate-autotest.sh b6-final`: 15/15 exit 0, errs 0, Bronze reached on all (11.2-15.5).
- Determinism: all 15 seeds run twice on the final build and data (`b6-a14` and `b6-final`): every
  `autotest-report.md` and `.json` byte-identical.
- Save/load identity (`runs/pass2/b6/slc-<seed>`): 2026, 7, 13 each "IDENTICAL reports; run A PASS, run B PASS".
- Savecheck (`runs/pass2/b6/savecheck`): PASS (the 11 "Exception" lines in its log are the deliberately damaged saves
  being refused, as in batch 1's run).
- Fuzz (`runs/pass2/b6/fuzz-final.out`): 8 seeds x 15 years, 8/8 PASS, 0 violations (conservation included); same
  pattern as batch 1's fuzz-final (seeds 1 and 4 run 15 years, the others' bands die under the hostile player).
- Seed 99 note: its late Bronze (Standard 15.5, Gentle 14.3) is the Bronze Foundry site waiting 55-112 days for its 20
  clay: seed 99 has one clay pit and the potters use all the clay (clay in store 0 for the whole wait). The same
  starvation is in the before-run (b1: clay 0 for the first five seasons after Bronze Working; 33 days to the foundry),
  so it is an existing auto-player habit that the reshuffled events made longer, not a rule the changes broke. No seed
  failed, so the auto-player was not changed.

**Standard, 15 seeds** (`runs/pass2/b1` -> `runs/pass2/b6-final`)

| | before | after |
|---|---|---|
| PASS (reached Bronze, no errors) | 15/15 | 15/15 |
| died out | 0 | 0 |
| alive but never left the Stone Age | 0 | 0 |
| Agricultural Age year min/median/max | 3.7/4.4/6.5 | 3.6/4.7/7.4 |
| Bronze Age year min/median/max | 11.1/12.6/13.9 | 11.2/12.4/15.5 |
| final population (mean) | 163.5 | 161.2 |
| births / migrants / leavers per run | 194.2 / 129.9 / 4.5 | 193.3 / 115.8 / 5.7 |
| deaths per run: starved / cold / sickness / old age | 2.9 / 0.7 / 2.5 / 161.9 | 0.1 / 1.3 / 2.3 / 150.5 |
| famine seeds (10+ starved or 20+ left) | 1 | 1 |
| first-winter starvation deaths (all seeds) | 0 | 0 |
| migrants in years 1-4 per run | 11.5 | 10.7 |

**Harsh (-difficulty 2), 6 seeds** (`runs/pass2/b6/before-harsh` -> `runs/pass2/b6/final-harsh`)

| | before | after |
|---|---|---|
| PASS (reached Bronze, no errors) | 5/6 | 6/6 |
| died out | 1 | 0 |
| alive but never left the Stone Age | 0 | 0 |
| Agricultural Age year min/median/max | 5.2/5.2/20.5 | 5.0/6.0/7.0 |
| Bronze Age year min/median/max | 13.6/14.6/27.2 | 12.9/14.6/17.6 |
| final population (mean) | 135.5 | 158.3 |
| births / migrants / leavers per run | 144.7 / 116.5 / 10.8 | 178.8 / 112.7 / 2.5 |
| deaths per run: starved / cold / sickness / old age | 3.7 / 34.2 / 3.7 / 85.3 | 0 / 2.7 / 3.8 / 136.2 |
| famine seeds (10+ starved or 20+ left) | 2 | 0 |
| first-winter starvation deaths (all seeds) | 0 | 0 |
| migrants in years 1-4 per run | 9.2 | 8.8 |

**Gentle (-difficulty 0), 6 seeds** (`runs/pass2/b6/before-gentle` -> `runs/pass2/b6/final-gentle`)

| | before | after |
|---|---|---|
| PASS (reached Bronze, no errors) | 6/6 | 6/6 |
| died out | 0 | 0 |
| alive but never left the Stone Age | 0 | 0 |
| Agricultural Age year min/median/max | 3.3/3.8/5.1 | 3.4/4.1/4.6 |
| Bronze Age year min/median/max | 10.6/11.3/12.3 | 10.3/11.7/14.3 |
| final population (mean) | 161.7 | 160.3 |
| births / migrants / leavers per run | 203.5 / 120.3 / 0 | 195.5 / 116.2 / 0 |
| deaths per run: starved / cold / sickness / old age | 0 / 1 / 1.5 / 171.7 | 0 / 0 / 0.5 / 162.8 |
| famine seeds (10+ starved or 20+ left) | 0 | 0 |
| first-winter starvation deaths (all seeds) | 0 | 0 |
| migrants in years 1-4 per run | 12.5 | 11.5 |

**Careless (-careless), 6 seeds** (`runs/pass2/b6/before-careless` -> `runs/pass2/b6/final-careless`)

| | before | after |
|---|---|---|
| PASS (reached Bronze, no errors) | 0/6 | 0/6 |
| died out | 6 | 6 |
| alive but never left the Stone Age | 0 | 0 |
| Agricultural Age year min/median/max | - | - |
| Bronze Age year min/median/max | - | - |
| final population (mean) | 0 | 0 |
| births / migrants / leavers per run | 3 / 8.7 / 3.7 | 1.7 / 4.3 / 9 |
| deaths per run: starved / cold / sickness / old age | 5 / 14.2 / 0.5 / 0.3 | 1.8 / 6.5 / 0 / 0.7 |
| famine seeds (10+ starved or 20+ left) | 1 | 0 |
| first-winter starvation deaths (all seeds) | 3 | 0 |
| migrants in years 1-4 per run | 7.5 | 4.3 |

**Bronze Age year per seed, Standard** (day/32 + 1)

| seed | before | after |
|---|---|---|
| 3 | 12.34 | 11.75 |
| 5 | 12.78 | 13.5 |
| 7 | 11.09 | 11.38 |
| 11 | 12.56 | 12.38 |
| 13 | 12.91 | 12.41 |
| 21 | 11.44 | 11.25 |
| 42 | 13.72 | 14.06 |
| 99 | 13.88 | 15.53 |
| 100 | 12.81 | 13.66 |
| 555 | 12.5 | 12.06 |
| 777 | 12.59 | 13.62 |
| 1234 | 12.31 | 11.72 |
| 2026 | 12.47 | 12.56 |
| 8080 | 11.78 | 11.59 |
| 31337 | 13.25 | 14.19 |

**Harsh per seed**: 7: 14.56 -> 14.56; 11: 13.56 -> 13.53; 13: 14.09 -> 17.59; 42: 27.16 -> 12.94; 99: died y5.75 -> 15.28; 2026: 15.31 -> 14.69

**Gentle per seed**: 7: 11.34 -> 11.62; 11: 10.84 -> 11.78; 13: 11.38 -> 11.22; 42: 12.28 -> 11.81; 99: 11.25 -> 14.28; 2026: 10.59 -> 10.31

**Careless (year died out) per seed**: 7: 4.75 -> 3.25; 11: 5.75 -> 4.75; 13: 3.75 -> 3.75; 42: 3.5 -> 5.75; 99: 2.75 -> 2.75; 2026: 3.25 -> 3.25
