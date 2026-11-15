# Balance after pass 2, batch 6 (tested data fixes)

Branch `experiment/quarter-pass2`. Per-change working notes, every variant tried and the raw run folders:
`docs/pass2/batch6-balance.md`. Findings referred to: `docs/pass2/audit-gameplay.md` (F1, F3, F6, F9) and
`docs/pass2/audit-code.md` (A14). Nothing on the plan's "Needs Nico" list was touched.

## What changed

| # | Change | Data / code | Decision | Commit |
|---|---|---|---|---|
| 1 | F1 famine harms more slowly | `balance.json` `starveDamage` 0.12 -> 0.06 | kept | 0e5880c |
| 2 | F3 unmet needs gate growth | `balance.json` `migrantMinHappiness` 0.5 -> 0.8 | kept (0.8; the Stone Age was not slowed, so 0.7/0.75 or an Agricultural-only threshold were not needed) | 6e8d4eb |
| 3 | F6 Harsh no longer a cold cliff | `balance.json` harsh `winterTemperature` -0.05 -> -0.02, `startGoods` 0.7 -> 0.85 | kept (mildest of 5 variants with no extinction on 12/12 seeds) | 8fe57c4 |
| 4a | F9 Fishing Spot | `buildings.json` fish 1.9 -> 2.8 per worker-day | kept | fb87699 |
| 4b | F9 flax | `buildings.json` fibre 120 -> 200 per field | dropped: fibre is not the bottleneck (over 90% already unused; the one Weaver is), clothing unchanged | none |
| 5 | A14 names | 90 names per sex (was 30); `NewCitizen` skips a name a living person has (same single random draw) | kept; every simulated number identical, only names differ | 0d4970c |

What a player feels: a late famine now empties a third of the town by people walking off instead of killing half of
it; a town that ignores water, pottery and faith stops receiving migrants (happiness ~0.70 < 0.8) while a well-run
town is unaffected; Harsh is a slower, tighter game (Bronze ~2 years later than Standard) instead of an extinction on
slow maps; a Fishing Spot is worth building (about 60% of a gatherer per worker-day over the year, better in winter,
still well below it in summer); far fewer people share a name.

## Before vs after

Before = the build and data at ab83ce6 (Standard: `runs/pass2/b1`; Harsh / Gentle / careless: `runs/pass2/b6/before-*`,
made for this batch). After = HEAD 0d4970c (`runs/pass2/b6-final`, `runs/pass2/b6/final-*`). Years are day/32 + 1.
Single seeds are chaotic (any data change re-rolls later events); read the aggregates.

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

## Reading
- Standard: years to Bronze unchanged in the median (12.6 -> 12.4), all within 11-16; starvation deaths 2.9 -> 0.1 per
  run; migrants -11% (130 -> 116) with the first four years' migrants about the same (11.5 -> 10.7); famine seeds 1 ->
  1, but the famine is now leavers, not deaths. Old-age deaths fall with fewer (older) migrants. The Agricultural
  median moved 4.4 -> 4.7 with single seeds moving both ways (noise); seed 99 is slowest (Agricultural 7.4, Bronze 15.5:
  a 25-people gate reached later, then its foundry waited 55 days for clay the potters had used up, an existing
  auto-player habit; see the checkpoint file).
- Harsh: no extinctions (was 1/6, plus one seed stuck in the Stone Age to year 20), cold deaths 34 -> 2.7 per run,
  Bronze median 14.6 (Standard 12.4), slowest seed 17.6. Losses are now modest; making Harsh deadlier without the
  cliff is a design call.
- Gentle: 6/6, no famine; Bronze median 11.3 -> 11.7 (seed 99 14.3, the clay wait above).
- Careless: still 6/6 dead in the Stone Age (years 2.75-5.75); the band now walks away (leavers 3.7 -> 9) more than it
  starves (5 -> 1.8) or freezes (14 -> 6.5). Neglect is still punished.

## Gate on the final build and data
- 15 seeds x 30 years: 15/15 PASS, exit 0, no errors, Bronze on all.
- Determinism: all 15 seeds run twice, reports byte-identical.
- Save/load identity, seeds 2026, 7, 13: IDENTICAL, PASS.
- `-savecheck`: PASS.
- Fuzz, 8 seeds x 15 years (`tools/fuzztest.sh 15 1 2 3 4 5 6 101 102`): 8/8 PASS, 0 violations (goods conservation
  included; seed 1's largest drift 11% of the allowance). The same six seeds as in batch 1 end with the hostile
  player's band dead, on similar days.
