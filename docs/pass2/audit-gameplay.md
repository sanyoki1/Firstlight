# Pass 2 gameplay and balance audit

Branch `experiment/quarter-pass2`. Find-and-report only: no source or data file in the repo was changed. Raw runs:
`runs/pass2/gameplay/` (git-ignored). Experiments changed data only inside the audit copy of the player
(`runs/pass2/build-audit/Firstlight_Data/StreamingAssets/Data`); a pristine copy is in
`runs/pass2/gameplay/Data-pristine`, and the audit copy was restored to it at the end (checked with `diff -r`).
Scripts: `runs/pass2/gameplay/scripts/` (`run.sh`, `exp.sh`, `variants.py`, `agg.py`, `cmp.py`).

Evidence base: the 15-seed x 30-year baseline (`runs/pass2/baseline`), plus 6-seed experiments (seeds 2026 7 11 42
99 13, 30 years each): Gentle, Harsh, careless, careless on Gentle, 15 data variants (about 120 runs). The appendix
(N1-N29) holds the numbers behind every claim below.

**What a scripted player cannot tell.** The auto-player never hesitates, never misreads a warning, follows one fixed
plan (the same research order on every seed) and stops building homes at 150. These runs measure what the RULES make
possible and how much each system moves the outcome. They cannot measure fun, legibility, whether a human notices a
system, or how a human weighs the choices the script never makes. Where a finding depends on the script's habits (the
late famines on seeds 11 and 13 come partly from its grain-share rule), it says so. Single-seed events are chaotic:
any data change re-rolls them, so only 6-seed means and large effects are trusted (N10).

## Summary

The Stone Age is a good, tense game: three hungry winters, a visible race to Farming and Tribes, every building
matters, and neglect kills (the careless player dies 6/6, even on Gentle). The Agricultural Age is where it goes flat:
once the first big harvest lands (year 6-7), food is never again a decision, and of the systems the age adds, the
experiments show that **seven have no measurable effect on the outcome**: the three new needs, the four laws,
traders, floods, outbreaks, tool wear and clothing (table below). The age's real game is "wait for research, then
build the foundry"; the Bronze Age is reached before a single ingot of bronze exists (15/15 seeds) and the tin
deposit that should force expansion is dug after the end, or never. After Bronze nothing is left but placing storage
piles for a grain mountain, interrupted on unlucky seeds by a famine that builds invisibly for three years and then
halves the town in one game week.

Top fixes a player would feel (details in the ranked findings):
1. Make the Bronze Age need bronze (data now: a Tin Mine milestone, tested +1 year; better: a "make N bronze"
   milestone type, a small rule change). F2.
2. Give the Agricultural needs a consequence (data now: `migrantMinHappiness` 0.5 -> 0.8, tested; rule: full work
   speed only above happiness 0.85). F3.
3. Make late food a visible, recoverable problem (data now: `starveDamage` 0.12 -> 0.06, tested: seed 13's famine
   becomes 40 leavers + 3 deaths instead of 40 deaths + 41 leavers; rule: an expected-harvest-vs-need warning from
   spring). F1.
4. Harsh: `winterTemperature` -0.05 -> -0.02 (tested: cold deaths 34 -> 12 per run), but slow maps still die. F6.
5. Owner decisions: what unrest, laws, floods and research choice should cost, and a goal after Bronze (F4, F5, F7).

## Diaries (the baseline as a player would have lived it)

**Seed 2026, a typical game.** *Year 1:* campfire, three gatherers' camps and a woodcutter on day 0, the Elders' Fire
on day 3, Basic Tools by mid-autumn; the first winter ends with 2 days of food and 6 firewood: tense, as intended.
*Year 2:* hungry again (14 food, 1.2 days in summer); Spears, Fishing, Herbal Knowledge. *Year 3:* the first real
autumn surplus (298 food); Seed Gathering, Kinship, then Farming in winter. *Year 4 spring:* the Agricultural Age on
day 102. The best stretch of the game follows, and the most fragile: fields pay only in autumn, water arrives as a
need, happiness dips to 0.64-0.76, food sits at 1.5-3 days through years 4-6. *Year 7 autumn:* the harvest lands
1,934 food; from here food days never fall below 10 again. *Years 7-11:* a steady drip of research (one topic every
~10-20 game days = 7-13 minutes at 1x), population 60 -> 151, no deaths but old age, no disputes, no thefts. Nothing
is at risk. *Year 12 summer:* Bronze Age on day 367, before any bronze is made. *Years 13-30:* flat at 152,
4,000-6,000 food in store, old-age deaths, more storage piles.

**Seed 7, the fast one.** *Year 1:* "There is no food left" on day 33 (end of the first winter). *Year 3 autumn:*
Agricultural Age already on day 87, which turns out to be early: *year 4 spring-summer* has no food, 3 die
(starvation, and two hungry people of sickness), 2 leave, a dispute breaks out, unrest 21%: the one dramatic moment
of the run, and a consequence of the player's own timing (good). *Year 5 on:* fields pay, food 1,300-4,300;
outbreaks of 18-19 sick in years 8-9 kill nobody; "Not enough firewood for winter" every autumn from year 9, no
deaths. *Year 11 spring:* Bronze. *Years 12-30:* only old-age deaths.

**Seed 13, the collapse.** *Years 1-4:* a hard Stone Age (end of winter year 2: 0.1 food days, 1 firewood) but no
deaths. *Years 5-12:* the usual comfortable farm town, 29 -> 156 people, Bronze in year 12 winter. *Years 12-14:* the
food curve tips: autumn peaks 4,519 -> 4,227 -> 2,953 -> 1,789, because the script cut its fields to "half the
food", the gatherers' woods were felled, and Weaving turned a grain field to flax. The game warns early and plainly
("Food stores won't last until summer (2018 of about 2022)", year 13 autumn) but nothing shows *why*. *Year 14
summer:* food 0; the script adds 7 fields, which pay only in autumn. *Year 15 summer:* within 8 game days (~5 minutes
at 1x) 40 starve and 41 leave: 155 -> 74. *Years 15-19:* a 3,584 harvest and migrants rebuild the town to 153 with no
effort. Tense for one week, and then the loss does not matter.

## Systems vs measured effect

Each row: the system switched off or neutralised in data, 6 seeds x 30 years, mean vs the baseline on the same seeds
(appendix N10-N28 for per-seed numbers). "Noticeable?" = would a player who never used or met this system be worse
off in a way they could see.

| System | Experiment (data change in the audit copy) | Effect: Bronze year / population / deaths | Noticeable? |
|---|---|---|---|
| Needs: water, pottery, faith | `needs[*].unmet` = 1.0 | Bronze 12.78 -> 12.88; pop@y12 151 -> 153; happiness 0.93 -> 0.98 | **No** (N4, N12): all three unmet still leaves happiness 0.70, above every threshold |
| Laws (all four) | every law effect value = 1 | Bronze 12.78 -> 12.47; disputes 0.3 -> 0.3; thefts 0 -> 0 | **No** (N13); Elders Settle in force 86-98% of the age, Food Sharing / Rationing ~0% |
| Traders (Barter) | `traders` 2 -> 1 | Bronze 12.78 -> 12.93; pop 166 -> 163 | **No** (N14); their one job was the tin the slice does not need |
| Floods | `floodChance` 0.4 -> 0 | Bronze 12.78 -> 12.13; food peak 6,494 -> 6,016 | **No** (N15); flooded fields +25%: a net bonus |
| Outbreaks | `outbreakBase` 0 | sickness deaths 3.7 -> 0.8; nothing else | **No** (N7, N16); sickness alone cannot kill a healthy person |
| Tool wear | `toolWearDays` 40 -> 4000 | Bronze 12.78 -> 12.47; pop same | **No** after year ~2 (N8, N17); copper/bronze tools change nothing in the slice |
| Clothing | `clothingWarmth` 0 | 5/6 seeds byte-identical; seed 13 differs only after firewood ran out | **No** while firewood lasts (N6, N25) |
| Tin deposit | (baseline reading) | Tin Mine after the age-up on 13/15, never on 1; first bronze after the age-up 15/15 | **No** (N3) |
| Unrest | `unrestPopScale` 400 -> 250 | unrest max 0.20 -> 0.27; disputes 0.3 -> 1.7 | Barely (N26) |
| Gentle | `-difficulty 0` | Bronze -1.5 years; starvation and leavers -> 0 | Yes, as padding (N11) |
| Harsh | `-difficulty 2` | Bronze +4 years on survivors; cold deaths 1 -> 34; 1/6 dies out, 1/6 in the Stone Age to year 20 | Yes, a cliff (N11) |
| Active management (Stone Age) | `-careless` | 6/6 die out in years 2-5 (also on Gentle, also with winter-food padding) | **Yes**: the part of the game that works (N20, N28) |
| Food after year ~7 | (baseline reading) | 30-45 food days in store; 25 of ~41 storage piles built after Bronze | Only as late slow famines on 2/15 seeds (N2, N21) |

## Ranked findings

Effort: S = a data edit or a few lines; M = a small feature; L = a new system. Flags: **tuning** (safe now),
**small rule change**, **needs owner decision** (touches something design.md decided, or adds a mechanic or goal).

### F1. Food stops being a decision after year ~7; the only late danger is a famine that builds invisibly and lands as a cliff
- Evidence: N1, N2, N21. Food days never fall below 10 after year 7-8 on most seeds; 4,500-7,400 food in store; 25 of
  the script's median 41 storage piles are placed after the Bronze Age just to hold grain. On seeds 11 and 13 the
  stock drains for three years (wild food fades as woods are felled, a flax field replaces a grain field, the script
  trims fields) and then a town of 155 loses half its people in 8 game days. The autumn warning ("won't last until
  summer") comes a year early, but fields pay only in autumn, so once the shortage shows in the top bar it cannot be
  fixed before the deaths, and nothing shows the cause.
- Player impact: a risk-free middle, then (on unlucky maps) a mass death that feels sudden. Part of the cause is the
  script (grain share 0.5); a human who keeps adding fields would not starve and becomes the grain-mountain player.
- Fix (tested, **tuning**, S): `balance.json`
  ```diff
  -  "starveDamage": 0.12,
  +  "starveDamage": 0.06,
  ```
  6 seeds: Bronze 12.78 -> 13.20 (within noise), starved 7.2 -> 0.5, leavers 10.2 -> 8.0, seed 2026 byte-identical;
  seed 13's famine becomes 40 leavers + 3 deaths (town 155 -> 110) instead of 40 deaths + 41 leavers (155 -> 74):
  people walk off to find food (`famineLeaveDays` 3, 10%/day) before they die (N22). Years-to-Bronze unchanged,
  survival up, the famine still costs the player a third of the town.
- Fix (**small rule change**, S-M, untested): from spring, a food-outlook line in the food tooltip and a warning chip:
  "Expected harvest ~X (N fields sown, M on flax) vs ~Y needed until next autumn" (the sim has `ExpectedHarvest` and
  `LeanDays`). Warning in SPRING, while sowing more still helps, not only in autumn.
- Fix (**needs owner decision**, M): a use for surplus food (feasts that raise births or happiness, food as trade
  goods, or stored grain as a milestone) so the surplus becomes a choice rather than a pile of storage piles.

### F2. The Bronze Age needs no bronze: tin, the tin deposit, traders and metal tools never matter
- Evidence: N3, N14, N17, N18, N24. On 15/15 seeds the first bronze comes after the age-up; the Tin Mine comes after
  it on 13/15 and never on seed 2026; traders hand over exactly 7 tin per visit so the foundry has ore. Without traders
  seed 2026 never makes bronze in 30 years and still "reaches the Bronze Age". The slice ends when the foundry's walls
  are up (`ages.json` milestone `building: bronze_foundry`; `EvaluateMilestones` knows only population /
  wintersSurvived / building, `Settlement.cs:419-424`).
- Player impact: the slice's climax is a construction timer; the design's "bronze forces the city to expand" (design.md,
  metal chain) never happens, and the end screen celebrates an age the town has not visibly entered.
- Fix (tested, **tuning** with a caveat, S): `ages.json`, Agricultural milestones:
  ```diff
           { "type": "building", "target": "bronze_foundry", "value": 1, "label": "Build a Bronze Foundry" }
  +        ,{ "type": "building", "target": "tin_mine", "value": 1, "label": "Dig a Tin Mine" }
  ```
  6 seeds: Bronze +1.0 year on 5/6 (7/11/13/42/99: 11.3 / 16.0 / 13.2 / 14.3 / 14.2 vs 11.1 / 12.6 / 12.9 / 13.7 /
  13.9), which also brings the slice closer to the brief's 12-25 years; seed 2026 never finishes because the
  AUTO-PLAYER cannot site a mine on its small deposit (QA F30). Apply together with an auto-player fix for tin siting,
  or the auto-test fails on 2026. Survival unchanged. (Also making tin untradeable changed nothing further: N24.)
- Fix (**small rule change**, S, untested): a milestone type `made` (counts `Produced[res]`): "Cast 20 bronze" (or "Make
  10 bronze tools") besides the foundry. Expected +1-2 years to Bronze; the metal chain (tin, smelting firewood,
  foundry) becomes the age's final push instead of a wait.
- **Needs owner decision**: whether traders keep bringing tin (design.md lists tin among trade goods). With a `made`
  milestone, traded tin is a fine but small shortcut (7 tin = 28 bronze per visit).

### F3. The Agricultural needs (water, pottery, faith) are cosmetic
- Evidence: N4 (formula), N12 (needs neutralised: no change in pace, size or deaths). With all three unmet, happiness
  is 0.70; full work speed needs only 0.6 (`Settlement.People.cs:636`), migrants 0.5, leaving starts at 0.35. Pottery
  has no effect at all beyond the happiness number.
- Player impact: three building chains (well, clay pit + potter, shaman) and three top-bar meters that hints introduce
  but that could be ignored without loss. A player who notices learns that needs do not matter, which undercuts every
  later age that layers more needs (design.md "Needs: layered by age").
- Fix (tested, **tuning**, S): `balance.json`
  ```diff
  -  "migrantMinHappiness": 0.5,
  +  "migrantMinHappiness": 0.8,
  ```
  A town ignoring all three needs (0.70) gets no migrants; a well-run town barely notices: 6 seeds: Bronze 12.78 ->
  12.68, migrants 140 -> 118, pop at year 12 151 -> 147 (N19). Needs become a growth gate without becoming lethal
  (design: an unmet need "caps happiness instead of being a survival need").
- Fix (**small rule change**, S, untested): full work speed at happiness 0.85 instead of 0.6 (`c.Happiness / 0.6f` ->
  `/ 0.85f`, `Settlement.People.cs:636`): one unmet need costs a household ~10% of its work, all three ~20%. Expected:
  a needs-ignoring player loses ~1 year to Bronze; a needs-meeting town unchanged.

### F4. Research is a fixed queue with no choices, and is exhausted by year 12
- Evidence: N1, N5, N9. All 27 topics rank identically on 15/15 seeds; every topic is known by day 315-407; ~900
  knowledge per year is wasted afterwards. Five Stone Age topics are taken after the age-up (the Stone Age is a beeline
  to Farming + Tribes). Total cost 3,765 vs ~4,200-5,500 earned by the age-up, so nothing ever has to be skipped. The
  fixed order is the script's, but the rules never make an order costly: with no budget pressure and most topics'
  effects unfelt (F3, F5), order barely matters.
- Player impact: the research screen is a to-do list; the only felt choice is "Farming and Tribes first".
- Fix (**needs owner decision**, M): (a) make side topics matter (F3/F5 fixes do part of this); (b) price side topics so
  only ~70% fit before Bronze at the current knowledge rate (e.g. Animal Husbandry, Weaving, Masonry, Irrigation,
  Healing x1.5), so the player picks; (c) give knowledge a sink after the last topic (or show the next age's topics
  locked, so banked knowledge has a purpose).

### F5. Laws, unrest, floods and disease ask nothing of the player
- Evidence: N13 (laws no-op: no change), N23 (Elders Settle costs ~0.3 game years of research for a benefit that never
  matters below ~200 people), N26 (unrest scale 400 -> 250: still mild), N15 (floods off: no change), N7/N16
  (outbreaks off: -3 deaths in 30 years). Unrest = population/400 + small terms, x0.5 council, x0.6 Elders
  (`Settlement.Society.cs:162-180`): it grows with size alone and has one always-right answer. Food Sharing and
  Rationing are in force 0-1 seasons per run.
- Player impact: the Laws screen and the unrest meter are decoration below ~200 people; the design's "challenges"
  pillar (disease and disasters, crime and unrest) is not felt in the slice.
- Fixes (**needs owner decision**, M): drive unrest from things the player chooses (Rationing, crowding, Shared Labour
  call-ups) rather than headcount, and give disputes a visible cost; make floods a gamble (flooded stores lose 40%+, or
  flooded homes are left for two days) rather than a +25% bonus; let untreated outbreaks threaten the very young and
  old (e.g. `sickDamage` x2 under age 6 or over 60; small rule change). Data tuning alone (N26) does not make unrest
  interesting.

### F6. Harsh is a cold cliff; Gentle is Standard with padding
- Evidence: N11, N27. Harsh: 1/6 dies out, 1/6 sits in the Stone Age to year 20, cold deaths 1 -> 34 per run (a
  heated Lean-to by the fire gives 0.05 + 0.35 + 0.2 = exactly the 0.6 comfort, so Harsh's -0.05 puts every lean-to
  sleeper in the cold all winter). Gentle: the same game 1.5 years faster with no famine; it does not save a passive
  player (N28).
- Fix (tested, **tuning**, S): `balance.json` `difficulties`, harsh: `"winterTemperature": -0.05` -> `-0.02`. Cold
  deaths 34.2 -> 11.8 per run; Bronze on survivors 13.5-15.6; but 2/6 slow maps (42, 99) still die in the Stone Age,
  so also try (untested) harsh `"startGoods": 0.7` -> `0.85`. Target: Harsh = Bronze in ~14-16 years with real losses,
  not extinction on slow maps.

### F7. Nothing to do after the Bronze Age ("Keep playing" has no goal)
- Evidence: N21. Decisions per game year fall from 15.9 to 5.0; needs 95-100%; all research known; deaths old age.
- Fix (**needs owner decision**, S-M): an end-of-slice score/summary with named records (largest population, years to
  each age, people lost, bronze cast); three or four optional goals per age (design.md already plans "optional
  milestone goals per age"), e.g. "200 people", "every home with water and faith", "50 bronze tools", "a winter with no
  deaths"; and slow late pressure (woods and herds near town that stop regrowing, deposits that run out) so the town
  has to change after year 15.

### F8. A first-time human will meet the first winters harder than the tests suggest
- Evidence: N1, N20, N28. Even the script ends its first winter with 0-4 days of food (median ~2) and 4-15 firewood;
  the careless player dies 6/6 by year 5, also on Gentle and with winter-food padding. Survival depends on reacting in
  the first ~14 minutes at 1x (the first autumn).
- Player impact: Standard is fair for a player who reads the guide and warnings and pauses; one who runs year 1 at
  2-4x without reading will likely lose the band in year 2-3. That is the intended lesson (design: "a few days of
  food"), but it is the one thing the first game teaches by failure, so the warnings carry it.
- Fix (**small rule change**, S): an autumn warning that names the action, sized from numbers the sim already has
  ("Winter needs ~N firewood and ~M food; you will have ~X and ~Y: set the woodcutter to firewood, add a gatherers'
  camp"), and an option to auto-pause on the first "Food will run out" warning. Data padding cannot rescue a passive
  player (N28), so this is a UI/warning fix, not a balance one.

### F9. Dominated and invisible buildings
- Evidence: N6, N25, build counts (N29). Fishing Spot 1.66 fish per worker-day vs gatherer 3.96 (no research
  boost, fish spoils 5%/day); Hide Tent built on 2/15 runs (it arrives after the Thatched Hut); Animal Pen and Weaver
  come after the age-up on most seeds and change nothing measurable; a flax field costs ~900 grain for ~320 fibre that
  becomes ~5 clothing a season (N2); Drying Rack converts at a 20% loss. Herbalist -> Healer is fine (an upgrade).
- Fix (**tuning**, S, untested): Fishing Spot output 1.9 -> 2.8 (a hunter's yield without tools); flax 120 -> 200
  fibre per field; Hide Tent hides 8 -> 4 and Hide Working 35 -> 25 so it can come before Farming. Low priority: none
  changes the pace.

### F10. Pace: the Agricultural Age is 2.4x the Stone Age, gated by research and then a construction wait
- Evidence: N5, N9. Stone Age 73 min at 1x, a new thing every ~5 min; Agricultural Age 173 min, every ~8 min, gaps up
  to 15-27 min (Flint Tools -> Pottery; Masonry -> Bronze Working); 80 people comes 2.5-4 game years before it is
  needed; the last gate is the foundry's construction on 15/15. Winter in the Agricultural Age asks nothing (fields
  idle, food in store, farmers haul and build). 11-14 game years matches the brief's 12-25.
- Not a length problem: the dull part is that nothing is at risk from year ~8 (F1, F3, F5). Fixing those fills the same
  minutes; shortening the age (e.g. Writing / Bronze Working 600 -> 480) would only make an empty stretch shorter. No
  pace change recommended on its own; F2's milestone adds ~1 year of purposeful play at the end.

## Smaller observations (no action proposed)
- Lifespan: old-age death chance 0.0006 x (age - 55) per day at 4 age-years per game year gives a median death at ~72,
  i.e. ~18 game years (~6 h at 1x) for a newborn, 8-14 game years for a migrant: long enough to know names. Births
  (~190) outnumber migrants (~130) over 30 years.
- Old stone tools pile up (to 130 by year 30 on seed 13) because nothing consumes them; harmless.
- The population plateau at ~150-186 is the script's home cap, not a rule; a human who keeps building will meet unrest
  past ~200 (dispute line) and ~400 (theft line) with no counter beyond council + Elders (code audit A11), i.e. F5.

## Data folder
- The audit copy's data folder was restored from `runs/pass2/gameplay/Data-pristine` after every batch and checked
  identical (`diff -r`) at the end. No repo file other than this report was written.

## Appendix: working notes and raw evidence (N1-N29, written as the audit went)

### N1. Baseline aggregates (15 seeds x 30 years, `runs/pass2/baseline`, script `runs/pass2/gameplay/scripts/agg.py`)
- Research order is IDENTICAL on all 15 seeds (rank of every one of the 27 topics is the same): Basic Tools, Storytelling,
  Spears, Fishing, Herbal Knowledge, Seed Gathering, Kinship, Farming, Tribes, Food Preservation, Hide Working, Tracking,
  Basic Mining, Flint Tools, Pottery, Healing, Tribal Law, Shamanism, Irrigation, Prospecting, Barter, Copper Working,
  Writing, Masonry, Bronze Working, Animal Husbandry, Weaving. Last topic (Weaving) day 315-407 (year 10.8-13.7).
  Total research cost 3,765 knowledge; cumulative knowledge reaches 4,200-5,500 by year 12 and ~900/year keeps
  arriving afterwards with nothing to spend it on.
- Binding gates: Stone Age ends on "Reach 25 people" (9/15 seeds) or Tribes (6/15). The Agricultural Age ends on
  "Build a Bronze Foundry" on 15/15 seeds, 10-38 days after Bronze Working (the 600-point key research) finishes.
  80 people is reached on day 212-310, i.e. 2.5-4 game years before the age-up: population is never the late gate.
- Laws (seasons in force out of ~100 post-Agricultural seasons): Elders Settle 86-98, Shared Labour 5-16 (toggled for
  building bursts), Food Sharing 0-1, Rationing 0-1. Elders Settle is never turned off once passed.
- Unrest max 0.13-0.36; disputes 0 on 12/15 seeds (777: 9, 2026: 1, 7: 1); thefts 0 on 15/15.
- Stone Age winter end: food days 0.0-8.4 (median ~2), firewood 0-53 units: the first 2-3 winters are tight on every
  seed, for the script too.
- Peak food in store 4,500-7,400 (30-45 food days) from year ~9 on.

### N2. Seed 13: why 40 starved and 41 left (year 15)
Read from `runs/pass2/baseline/at-13/autotest-report.md` (season table, "Agricultural systems", season detail, actions):
- Fields stayed at 11 from year 9 to year 14 while the town grew 100 -> 156. The auto-player had cut its grain share to
  0.5 ("food to spare" after summers with 15+ food days), so 11 fields harvested ~4,100 grain/year against ~4,100 eaten.
- The non-grain food quietly shrank: gatherers' camps wore out their woods (replaced at fullness 0.08-0.29 on days
  337-433), so wild food fell from ~40/season to ~10.
- Weaving (day 365, the last topic) made the auto-player sow 1 of the 11 fields with flax: year 13's harvest was
  3,211 grain + 324 fibre (year 12: 4,136 grain). The fibre became ~5 clothing a season; the field's ~900 grain was lost.
- Stock ran down over three years: autumn peaks 4,519 (y11) -> 4,227 -> 2,953 -> 1,789 (y14). Warnings came early and
  plainly: "Food stores won't last until summer (2018 of about 2022)" on day 404 (year 13 autumn), a full game year
  (21 min at 1x) before the deaths. Food hit 0 in summer of year 14 (no deaths then), the auto-player added 2 fields
  (day 429) and 5 more (day 453), but fields only pay in autumn.
- Then a cliff: food 0 again in spring year 15 and within 8 game days (days 457-464, ~5 real minutes at 1x)
  40 starved and 41 left: the town halved (155 -> 74). Recovery was fast and automatic (3,584-grain harvest that
  autumn; 85 people by year 16, 153 by year 19) thanks to migrants.
- Reading: the failure was telegraphed (a year of warnings) but the consequence arrives as a one-week cliff, and the
  cause (wild food fading as woods are cut; a flax field quietly replacing a grain field) is invisible in the HUD's
  food number. Rule: famine leaving `famineLeaveDays` 3 / `famineLeaveChance` 0.1 per day (balance.json) and
  `starveDamage` 0.12/day.

### N3. The Bronze Age is reached before any bronze exists; the tin deposit never matters (15/15 seeds)
- Tin ore first appears in store at the start of a spring (exactly 7.0 = one trade: `traderValue` 40 x `traderRate`
  0.75 / tin `value` 4 = 7) on 15/15 seeds: traders bring it as soon as Bronze Working is known
  (`Settlement.Agri.cs` `TraderVisit`, target 12 tin ore once Bronze Working is known).
- The first Tin Mine is placed AFTER the age-up on 13/15 seeds (median day 421 vs age-up 367), never on seed 2026.
  The first bronze is made in the season after the age-up or later on 15/15 seeds.
- Cause: the last milestone is "Build a Bronze Foundry" (`ages.json`, type `building`), so the slice ends when the
  foundry's walls are up. Milestone types are only `population`, `wintersSurvived`, `building`
  (`Settlement.cs:419-424` `EvaluateMilestones`). The design's "bronze forces the city to expand" (design.md, metal
  chain) does not happen inside the slice.

### N4. The three Agricultural needs cannot move anything that matters (code reading, confirmed by experiment below)
- Happiness target = 0.4 x average + 0.6 x worst need (`Settlement.People.cs:1046-1052`). An unmet need is worth its
  `unmet` value (water 0.6, pottery 0.7, faith 0.65; `balance.json` `needs`). With food, warmth, shelter and health met
  and ALL THREE needs unmet: avg (4 + 0.6 + 0.7 + 0.65)/7 = 0.85, worst 0.6 -> 0.4*0.85 + 0.6*0.6 = 0.70.
- What happiness does: work speed = lerp(0.5, 1, happiness/0.6) (`Settlement.People.cs:636`) = full speed at >= 0.6;
  migrants need average >= 0.5 (`migrantMinHappiness`); leaving below 0.35 (`leaveHappiness`); unrest counts people
  below 0.5. So even with every new need unmet, happiness 0.70 keeps full work speed, migrants coming and nobody
  leaving. Only side effects remain: no water = +0.004 sickness/day, x0.9 work speed, x(1+4 share) outbreak risk;
  faithless = +0.15 x share on unrest (x0.6 Elders law x0.5 council = at most +0.045). Pottery has no effect at all
  beyond the happiness number.
- The happiness multipliers of laws and research (Shared Labour x0.94, Rationing x0.9, Kinship x1.05) likewise do
  nothing while happiness stays above 0.6.

### N5. Pace and gates (baseline, 15 seeds; 1 game day = 40 s at 1x, so 1 day = 0.67 real minutes)
- Stone Age: median 109 days (73 min at 1x), ~15 "new things" (research done or milestone) = one per ~7 days
  (~5 min); longest gap 14-54 days. Agricultural Age: median 260 days (173 min at 1x), ~21 new things = one per
  ~12 days (~8 min); longest gap 23-40 days (15-27 min), typically Flint Tools/Mining Pit -> Pottery (the first 140-pt
  Agricultural topic) or Masonry -> Bronze Working (600 pts). The Agricultural Age is 2.4x the Stone Age.
- Bronze at median day 367 = ~4.1 h of play at 1x (about 1 h at 4x). Knowledge, not population, sets the pace: 80
  people comes 2.5-4 game years before the end; the last ~70 days are Writing + Bronze Working (1,200 of the age's
  3,190 points) and then the foundry's construction.
- Stone gates: "25 people" last on 9/15, Tribes on 6/15. Agri gate: Bronze Foundry construction on 15/15.

### N6. Output per worker from the data (`buildings.json`; season factors averaged over the year)
| Job | Nominal per worker-day, year average | Research boost | Needs tools | Notes |
|---|---|---|---|---|
| Gatherer | 4.5 x 0.88 = 3.96 wild food (winter x0.12) | Seed Gathering x1.15 | no | needs woods; woodcutters thin them |
| Hunter | 3.0 x 0.93 = 2.78 meat + 0.37 hides | Tracking x1.3 = 3.6 | yes (x0.6 without) | herds deplete (1 animal / 10 meat) |
| Fisher | 1.9 x 0.88 = 1.66 fish | none | no | dominated: under half a gatherer, fish spoils 5%/day |
| Herder (pen) | 2.4 x 0.95 = 2.28 meat + 0.43 hides | none | no | 160-pt research, built after Bronze is reached |
| Farmer | field 400 grain x growth (~0.9) x irrigation 1.35 / 3 farmers; measured 300-470 grain per field-year, ~5 grain per farmer working day in 3 seasons, free in winter | Irrigation | required | grain keeps (0.8%/day, x0.3 in a granary) |
| Food preserver | 2 meat/fish -> 1.6 preserved | Food Preservation x0.85 spoil | no | converts at a 20% loss; only worth it with spoiling meat |
- Fishing Spot: 4-7 built per run (first ~day 53); fish rarely exceeds 40/season. Pens come at day ~359, after the
  age-up on most seeds: by then grain is 90%+ of food. Hide Tent: built on 2/15 runs (Hide Working arrives day ~114,
  after Farming unlocks the Thatched Hut, 8 beds/0.85 warmth for 22 wood, vs Hide Tent 6/0.7 for 10 wood + 8 hides).
- Clothing: `cold = temp < warmthComfort` (0.6) is true in spring (0.55), autumn and winter, so everyone tries to wear
  clothing 3 seasons a year (64-day life): demand ~2.5/day at 160 people vs ~0.5-1/day made. Clothing sits at 0-4 all
  game. Nobody notices: a heated Thatched Hut gives 0.05 + 0.85 = 0.90 warmth in winter without clothing
  (`Settlement.People.cs:995-1002`). Clothing (and so the Hide Worker, Weaver, Weaving and flax) only matters for
  lean-to dwellers away from a fire or when firewood runs out.

### N7. Disease cannot kill a healthy person (code reading)
- Sick damage is `sickDamage` 0.05/day (x `healDamage` 0.25-0.4 when treated) for `sickDaysMin..Max` 4-10 days, +3
  days in an outbreak without care (`Settlement.Society.cs:279`, `Settlement.People.cs:1010-1016`). Worst case
  untreated: 13 x 0.05 = 0.65 health. So sickness kills only someone already below ~0.65 health (cold or hungry);
  the 0-9 "sickness" deaths in 30 years are people weakened by something else. Outbreak chance at 160 people with
  water and a healer: 0.003 x (1 + 1.6) x (1 - 0.6) ~ 0.3-0.8 %/day -> 6-15 outbreaks in 30 years (measured 6-15).
  Disease is a work-days nuisance, not a threat.

### N8. Tools: a real constraint for about two years, then a nag
- "N workers have no tools" warnings appear 2-10 times every year on every seed (1-7 workers at a time) even with
  30-80 stone tools in stock from year ~8 (handout lag, tools in a far store). Stock (stone/copper/bronze) at the
  end of winter of years 3/5/7/9/11/13 on seed 2026: 5, 5, 7, 28, 42, 73. Real shortages are years 1-2 only.

### N9. Timeline, median of 15 baseline seeds (day; game year; real minutes at 1x)
| Event | Day | Year | Min at 1x |
|---|---|---|---|
| Elders' Fire built | 15 | 1.5 | 10 |
| Basic Tools | 19 | 1.6 | 13 |
| Storytelling / Spears | 25 / 29 | 1.8-1.9 | 17-19 |
| First winter survived | 32 | 2.0 | 21 |
| Fishing / Herbal Knowledge / Seed Gathering / Kinship | 44 / 51 / 60 / 70 | 2.4-3.2 | 29-47 |
| Farming / Tribes | 84 / 93 | 3.6-3.9 | 56-62 |
| 25 people | 97 | 4.0 | 65 |
| **Agricultural Age** | 109 | 4.4 | 73 |
| Hide Working, Tracking, Granary, Basic Mining | 114-125 | 4.6-4.9 | 76-83 |
| Mining Pit / Flint Tools | 142 / 151 | 5.4-5.7 | 95-101 |
| Pottery (first Agricultural topic) | 174 | 6.4 | 116 |
| Healing / Tribal Law / Shamanism / Irrigation / Prospecting | 195-250 | 7.1-8.8 | 130-167 |
| 80 people | 252 | 8.9 | 168 |
| Barter / Copper Working / Smelter | 265 / 282 / 301 | 9.3-10.4 | 177-201 |
| Writing / Masonry / Bronze Working | 315 / 325 / 351 | 10.8-12.0 | 210-234 |
| Animal Husbandry / Weaving / Bronze Foundry | 358 / 365 / 368 | 12.2-12.5 | 239-245 |
| **Bronze Age** | 370 | 12.6 | 247 |
Five Stone Age topics (Food Preservation, Hide Working, Tracking, Basic Mining, Flint Tools) are researched after the
age-up by the script: the Stone Age is a beeline to Farming + Tribes.

### N10. Experiments: method and caveat
- Each experiment = the same 6 seeds (2026 7 11 42 99 13), 30 years, audit copy of the player, data changed only in
  the copy (variants built by `runs/pass2/gameplay/scripts/variants.py`, compared by `scripts/cmp.py` against
  `runs/pass2/baseline`). Raw: `runs/pass2/gameplay/<name>/at-<seed>/`.
- Caveat: the sim is deterministic but chaotic. Any data change moves every later random draw, so single-seed events
  (seed 13's year-15 famine, seed 11's leavers) appear or vanish for reasons unrelated to the change. Only means over
  the 6 seeds and effects much larger than ~1 game year / ~10 deaths are read as real.

### N11. Difficulty (6 seeds; `-difficulty 0|2`)
| | Agri year | Bronze year | Final pop | Starved | Cold | Left | Disputes/thefts | Result |
|---|---|---|---|---|---|---|---|---|
| Standard (baseline) | 4.7 | 12.8 | 166 | 7.2 | 1.3 | 10.2 | 0.3 / 0 | 6/6 |
| Gentle | 4.0 | 11.3 | 162 | 0 | 1.0 | 0 | 0 / 0 | 6/6 |
| Harsh | 8.7 (5 seeds) | 17.0 (5 seeds) | 136 | 3.7 | 34.2 | 10.8 | 6.7 / 2.7 | 5/6 (99 dies out in year 5, all 15 deaths cold) |
- Harsh is mostly a COLD difficulty: cold deaths 0-1 -> 4-74 per seed (2026: 64, 13: 74, 7: 47). Seed 42 sits in the
  Stone Age until year 20.5 (pop 19 at year 12). The step from Standard to Harsh is much bigger than from Gentle to
  Standard. Harsh's winter -0.05 matters because Lean-to warmth is 0.05 + 0.35 + 0.2 (fire) = 0.60 = exactly
  `warmthComfort`: -0.05 puts every Lean-to sleeper below comfort all winter.
- Gentle = Standard minus every famine (0 starved, 0 left on 6/6) and 1.5 years faster. It does not remove the tight
  first winter's lesson for the auto-player, but it is padding, not a different game.

### N12. Experiment: the three Agricultural needs neutralised (`needs[*].unmet` = 1.0)
Mean of 6 seeds, baseline -> variant: Bronze year 12.78 -> 12.88; pop at year 12 151 -> 153; average Agricultural
happiness 0.93 -> 0.98; births 193 -> 188; migrants 140 -> 129; outbreaks 10 -> 8.7. (Starved 7.2 -> 0.3 and left
10.2 -> 0.7 is seed 13's chaotic famine vanishing, see N10.) **No measurable effect**: confirms N4. A player who
ignores water, pottery and faith completely loses nothing but a happiness number.

### N13. Experiment: all four laws made no-ops (every law effect value = 1; the auto-player still passes them)
Mean of 6 seeds: Bronze 12.78 -> 12.47; pop at year 12 151 -> 150; unrest max 0.20 -> 0.25; disputes 0.33 -> 0.33;
thefts 0 -> 0; starved 7.2 -> 1 (chaos, N10). **No measurable effect.** Elders Settle (-40% unrest) is the only law in
force for long, and below ~200 people unrest stays under the 25% dispute line even without it (council alone halves
it: 160/400 = 0.40 x 0.5 = 0.20). Its cost (elders' knowledge -20%) touches only the 3-6 elders, a small share of
knowledge once Shaman's Huts (2.4/worker) run.

### N14. Experiment: no traders (Barter's `traders` effect 2 -> 1, so `TraderVisit` never runs)
Mean of 6 seeds: Bronze 12.78 -> 12.93; pop 166 -> 163; everything else within noise. **No measurable effect on the
slice.** Without traders the first tin ore comes from the Tin Mine in years 13-16, i.e. 1-4 years after the Bronze
Age; seed 2026 never builds a Tin Mine and never makes any bronze in 30 years, yet "reaches the Bronze Age" on day
365. Traders' only real job in the baseline was handing over the tin that the slice does not need.

### N15. Experiment: no floods (`floodChance` 0.4 -> 0)
Mean of 6 seeds: Bronze 12.78 -> 12.13; pop at year 12 151 -> 139; food peak 6,494 -> 6,016; everything else within
noise. Baseline floods: 8-16 in 30 years per seed, 1-25 buildings each (mostly fields: the auto-player keeps
everything else off the floodplain), losses "a few goods" except on seeds 11/13 (52-254 goods, a few times). Flooded
fields yield x1.25 (`floodFertility`), so for a player who farms the floodplain a flood is a bonus. **No measurable
cost; nobody has to plan around it beyond "don't build huts on the floodplain", which placement already warns about.**

### N16. Experiment: no outbreaks (`outbreakBase` 0.003 -> 0)
Mean of 6 seeds: outbreaks 10 -> 0; sickness deaths 3.7 -> 0.8; Bronze 12.78 -> 13.02; pop and happiness within
noise. **No measurable effect beyond ~3 deaths in 30 years** (consistent with N7).

### N17. Experiment: tools never wear out (`toolWearDays` 40 -> 4000)
Mean of 6 seeds: Bronze 12.78 -> 12.47; pop at year 12 151 -> 150; food peak 6,494 -> 6,445. **No measurable effect.**
Once a toolmaker runs (day ~26), wearing tools out never constrains the town: the whole stone -> copper -> bronze tool
chain (Coppersmith, Bronze Smith, tool swaps) changes nothing a player could feel inside the slice. (Tools still
matter in year 1 through `noToolWorkSpeed` 0.6 and fields/mines that need one; this variant keeps that.)

### N18. Experiment: "Dig a Tin Mine" added as an Agricultural milestone (`ages.json`)
Seeds 7/11/13/42/99: Bronze years 11.3 / 16.0 / 13.2 / 14.3 / 14.2 (baseline 11.1 / 12.6 / 12.9 / 13.7 / 13.9),
mean +1.0 year; seed 2026: never (the auto-player cannot find a site on its 4-spot tin deposit, QA F30; a human gets
the deposit hint while placing). So the milestone does bite and does push the town out to the tin, at the cost of an
auto-player gap on seed 2026. Tested with tin also untradeable in exp2 (`tinreal`, below).

### N19. Experiment: migrants need average happiness 0.8 (`migrantMinHappiness` 0.5 -> 0.8)
Mean of 6 seeds: Bronze 12.78 -> 12.68; migrants 140 -> 118 (-16%); pop at year 12 151 -> 147; births 193 -> 194.
The auto-player keeps needs met, so happiness sits at 0.9+ and the stricter threshold barely bites; it does make
an ignored need cost growth (all three unmet = 0.70 < 0.8 = no migrants) without hurting a well-run town. Safe as a
lever to give needs a consequence (see F3).

### N20. The careless auto-player (`-careless`: builds the same plan, never reacts: no priorities, no extra food or
firewood buildings, no pausing) on 6 seeds
All 6 die out in the Stone Age: seed 99 in year 2 winter, 2026 year 3 summer, 42 year 3 autumn, 13 year 3 winter,
7 year 4 winter, 11 year 5 winter (max population 13-22; deaths cold 2-36, starvation 2-11). Food at the end of the
first winter is 0.0 days on 6/6. The gap between "builds the right things" and "survives" is entirely active
management in the first 2-3 years (priorities, firewood focus before winter, a second/third food building when
food days fall). A first-time human sits somewhere between the careless and the full script.

### N21. After the Bronze Age (years ~12-30 of the baseline)
- Every topic is known by day 315-407; knowledge keeps arriving at ~800-950 per game year with nothing to buy
  (cumulative 4,200-5,500 at year 12, 8,000-9,000 by year 16).
- The script's decisions (placements, upgrades, laws, research) fall from a median 15.9 per game year before the
  Bronze Age to 5.0 after; 25 of its median 41 storage piles are placed after the age-up, i.e. the main late
  "decision" is where to put another pile for the grain mountain (food peaks 4,500-7,400 = 30-45 food days).
- Deaths after year 13 are almost all old age; population is flat at ~150-186 (the script stops building homes at
  150). The only late events are spring floods (bonus for fields), small outbreaks and old-age deaths. Seeds 11 and 13
  show the one late danger: a slow food decline into a famine (N2), which the HUD does not make visible early.

### N22. Experiment (candidate fix): famine harms more slowly (`starveDamage` 0.12 -> 0.06)
Mean of 6 seeds: Bronze 12.78 -> 13.20; pop at year 12 151 -> 154; starved 7.2 -> 0.5; left 10.2 -> 8.0. Seed 2026
is byte-for-byte unchanged (nobody there ever goes hungry long enough). Seed 13 still has its year-14 famine, but it
plays out as 40 people LEAVING and 3 dying (town 155 -> 110) instead of 40 dying and 41 leaving (155 -> 74): with
half the damage, the famine-leaving rule (`famineLeaveDays` 3, 10%/day) acts before health runs out. Same warning,
a recoverable and readable outcome ("they left to find food") instead of a one-week mass death.

### N23. Experiment: Elders Settle costs 10% of ALL knowledge instead of 20% of the elders' (`laws.json`)
Bronze Working on the same day as the baseline (seed 7: 303 vs 304; 13: 351 vs 351; 2026: 354 vs 348); mean Bronze
12.75 vs 12.78. The elders already make about half the knowledge, so the old cost (-20% elders) was already ~-10%
overall; this variant is cost-neutral. Combined with N13 (law removed: Bronze -0.3 years), the law's real price is
~0.3 game years of research, for a benefit (unrest -40%) that never matters below ~200 people.

### N24. Experiment: Tin Mine milestone + tin ore no longer traded (`tinreal`)
Same as N18 (traders did not matter once the mine was required): Bronze 7/11/13/42/99 = 11.3/16.3/13.2/14.3/14.1
(+1.06 years mean), seed 2026 never (no tin site found by the script). Seed 11's 16.3 comes with a famine (54 left)
that is the chaotic late-food issue, not the tin.

### N25. Experiment: clothing gives no warmth (`clothingWarmth` 0.25 -> 0)
Seeds 2026, 7, 11, 42, 99: IDENTICAL to the baseline in every reported number (not one person's warmth crossed the
comfort line because of clothing in 30 years). Seed 13 diverges after its late winters with firewood at 0 (cold
deaths 7 -> 108). **Clothing matters only as a backstop when firewood runs out**; the Hide Worker, Weaver, Weaving
and flax fields are invisible otherwise (confirms N6).

### N26. Experiment: unrest that grows faster with size (`unrestPopScale` 400 -> 250) + Elders cost as N23
Mean: unrest max 0.20 -> 0.27; disputes 0.33 -> 1.67 (seed 2026: 8, one theft); Bronze 12.73 (same). Still mild: the
council (x0.5) and Elders Settle (x0.6, always passed by the script at 12% unrest) hold it under the 25-30% lines at
~160 people. Unrest only becomes a system a player manages if something the player chooses (not size alone) drives it
and the counters cost something real. Data tuning alone cannot make it interesting.

### N27. Experiment (candidate fix): Harsh winter -0.05 -> -0.02 (`difficulties[harsh].winterTemperature`), run with `-difficulty 2`
Compared with Harsh as shipped (same 6 seeds): cold deaths 34.2 -> 11.8 mean (2026: 64 -> 26, 13: 74 -> 18, 7: 47
-> 8); Bronze on the surviving seeds 14.6/13.6/14.1/15.3 -> 13.5/15.6/14.4/13.5; disputes 6.7 -> 0.2. But 2/6 still
die out in the Stone Age (99 as before; 42, which crawled into the Agricultural Age in year 20 before, now dies). So
the winter offset explains most of Harsh's cold deaths but not its lethality on the slow maps (42, 99: thin woods,
slow labour): Harsh's x0.7 start goods and x0.9 yields kill those in years 2-5.

### N28. Careless player on Gentle, and with a "novice" padding (gatherers' winter yield 0.12 -> 0.3, start firewood 15 -> 30)
- Careless + Gentle: 6/6 still die out, in years 3-6 instead of 2-5 (2 of them reach the Agricultural Age first).
- Careless + novice padding (Standard): 6/6 die out (seed 11 limps to year 23 at up to 77 people, then freezes).
- So a player who builds the right things but never reacts cannot be saved by start goods, mild winters or winter
  food: the failure is the missing reaction (more food/firewood buildings and priorities when the warnings come).
  Gentle buys 1-2 years of lessons; it is not a mode where passivity survives. That is defensible design, but it means
  the guide/warnings carry the whole first-game experience.

### N29. Build counts (baseline, placements per 30-year run, median over 15 seeds) and the novice padding on the full script
- Storage Pile 41 (16 before Bronze, 25 after), Woodcutter 28, Gatherers' Camp 23, Hunters' Camp 22, Field 16, Thatched
  Hut 12, Lean-to 8, Granary 7, Stone Gatherer 6, Well 5, Fishing Spot 4 (first ~day 53), Toolmaker 3, Elders' Fire 3,
  Animal Pen 3 (first ~day 359, after the age-up), Hide Worker 3, Herbalist 2, Potter 2, Shaman's Hut 2, Healer 1,
  Weaver 1 (day ~365), metal chain 1 each, Tin Mine 1 (day ~421, after the age-up; never on 2026), Hide Tent 0 (2/15
  runs). Woodcutters and gatherers' camps are rebuilt ~25 times as woods are felled ("worn-out" relocations).
- The "novice" padding (gatherers' winter yield 0.12 -> 0.3, start firewood 15 -> 30) with the normal script:
  Agricultural Age 4.7 -> 4.0, Bronze 12.78 -> 12.07, starved 7.2 -> 0.5, left 10.2 -> 1.0. It makes the Stone Age
  easier for the script but cannot save the careless player (N28); not recommended (it removes the tension the Stone
  Age does well).
