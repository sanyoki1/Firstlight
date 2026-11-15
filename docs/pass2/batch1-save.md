# Fix batch 1: save-format safety and counters (checkpoint, final)

Branch `experiment/quarter-pass2`, started from e64cf88. Test outputs are in `runs/pass2/b1/` (not committed).

## Status: all six fixes done, final gate passed
- [x] 0. v1 test saves made with the ORIGINAL player copy (runs/pass2/build-ux), before any rebuild:
  `runs/pass2/b1/v1saves/s11/v1-seed11-day301.flsave`, `t2026/v1-seed2026-day700.flsave`, `t42/v1-seed42-day600.flsave`,
  plus the existing `runs/pass2/perf/save7/seed7-day452.flsave`.
- [x] 1. A2 versioned, id-keyed saves: 3bff84a
- [x] 2. A3 unknown research/law ids dropped on load: d4bcc7a
- [x] 3. A10 safe overwrite (File.Replace): 7599896
- [x] 4. Perf 5 buffered save: ccce9d8 (+ docs/pass2/perf.md)
- [x] 5. A17 double counters: 6009fda
- [x] 6. A13 Lost[]/Traded[] + goods-conservation fuzz invariant: bb9eede
- [x] Test follow-up: the unknown-id savecheck also runs on each v1 save (exercises a law in force), and this file.

## Gate results
| State | 15-seed summary vs baseline | full reports vs baseline | slc 2026/7/42 | savecheck (+4 v1 saves) | fuzz 8 seeds x 15 y |
|---|---|---|---|---|---|
| A2 (3bff84a) | identical | all 30 files byte-identical | IDENTICAL, PASS x3 | PASS | 0 violations, all round trips identical |
| A3+A10 (built together) | identical | byte-identical | IDENTICAL, PASS x3 | PASS | 0 violations |
| Perf 5 | identical | byte-identical | IDENTICAL, PASS x3 | PASS | 0 violations; reports byte-identical to A2's |
| A17 (with GrainProducedF) | identical | numbers only: knowledge, harvest, made/used | IDENTICAL, PASS x3 | PASS | 0 violations; reports byte-identical to A2's |
| A13 / final build | identical | numbers in knowledge/harvest/made; "used" lists changed (see below); one "Goods lost" line added | IDENTICAL, PASS x3 | PASS | 0 violations incl. conservation |

Determinism (final build): seed 7 run twice -> both reports byte-identical, and identical to the gate's seed 7; the
final gate's 30 report files are also byte-identical to the A13 build's gate run.

## Findings and decisions (in order)
- A2: GameMap and AutoPlayer Write/Read hold no good indices (checked), so they are unchanged. `Building.Delivered`/
  `InTransit` are per cost entry, `Focus`/`CropOut` per output: not goods. `TaskIdx` is a good only for MoveStock,
  FetchInput and DeliverInput. Totals are recounted only when goods actually moved (bit-for-bit otherwise). The
  auto-test's test block keeps its positional arrays (test-only).
- A17: a decision reads a cumulative counter. `AutoPlayer.Agri.WatchHarvest` sizes the fields it plans from
  `Produced[grain]`. With a plain double the 15-seed gate changed on seed 21 (births 199 vs 201, migrants 129 vs 126,
  old age 172 vs 171). Fix: `Settlement.GrainProducedF`, a float running total kept exactly as the old float counter
  (saved; a v1 save takes it from its float Produced[grain]); the auto-player reads it. Gate identical again.
- A13, bookkeeping gaps found while writing the invariant (by reading every place a good moves, before the first
  run; with them counted, the invariant found no further leak in 8 seeds x 15 years):
  1. daily spoilage (stores, workplace out and in piles): counted nowhere -> Lost
  2. flood losses from workplace piles: nowhere -> Lost (store flood losses moved from Consumed to Lost)
  3. demolition / cancelled-upgrade goods with no room (and refunds under 0.01 that were silently skipped) -> Lost
  4. goods in hand, field loads and tools of leavers and the dead -> Lost
  5. a half-worn tool swapped back to store with no room -> Lost
  6. float dust zeroed in stores (< 1e-4) and in field loads -> Lost
  7. workplace inputs used in production (`b.In` decreased with no counter) -> Consumed
  8. worn-out tools (`WearTool` dropped the tool with no counter) -> Consumed
  9. construction materials delivered to sites and upgrades -> Consumed (and back, minus, when a demolition or a
     cancelled upgrade returns them; an active building's salvage also counts back)
  10. trades: the given good was withdrawn and its Consumed undone, the received good counted nowhere -> Traded (net)
  11. theft and dropped goods moved from Consumed to Lost.
  Remaining source not counted: `DebugSpawn` (perf probe only) adds food without a counter.
- A13, a behaviour trap: my first A13 build changed 7 seeds (e.g. 2026 births 180 vs 189). Cause: two arithmetic
  lines rewritten with a float local in between (`In` spoilage, demolition refund). The JIT evidently keeps the
  product in higher precision when it is inline, so the local rounded differently and changed the game. Both lines
  are back to their exact original form, with the counters fed from the before/after values. Gate identical again.
- Report differences after A13 (all seeds: ages, births, migrants, leavers and deaths identical): numbers in
  knowledge, season harvest and "made" (A17 precision), the season "used" lists now include workplace inputs, worn
  tools and building materials, and lose theft/flood/dropped amounts (these went to Lost), plus a new line
  "Goods lost (...): food N, all goods N". JSON: only `knowledge`, `harvest` and `detail` fields differ.
- Fuzz conservation tolerance: 0.02 + 1e-6 x the good's flow (start + produced + |traded| + |consumed| + lost). Largest
  drift seen: 0.0103 of grain at flow 43784 (16% of the allowance). A day-10 self-test adds one uncounted unit to the
  campfire store and checks it is caught, then puts the store back exactly.
- Perf 5: see docs/pass2/perf.md (min 30-34 ms -> 21-24 ms; collections per save unchanged at ~12-15, so the garbage
  comes from elsewhere).

## Side effects and caveats
- The "before" perf probe run with the original build-ux player (-load save7 -perfprobe -perfnobase -perfsave)
  autosaved once into the real saves folder (`%USERPROFILE%/AppData/LocalLow/Firstlight/Firstlight/saves/autosave-1.flsave`,
  seed 7 day 456) before its probe started, overwriting the oldest autosave slot. It ran far too slowly unattended and
  was killed (it was mine). Save timing was measured instead with a Stopwatch in the savecheck's compat step.
- Saves written by the intermediate builds (A2..Perf commits) are format 2 with float counters and no Lost/Traded;
  the final format 2 cannot read them. They exist only under runs/.
- A3+A10 were built and gated as one build (neither can change play); A3's commit alone was not built separately.
